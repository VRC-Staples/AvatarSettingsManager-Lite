use crate::catalog::load_catalog_from_str;
use crate::event_reader::{EventReader, StartupPollResult};
use crate::model::{
    AppState, OverlayBootstrapConfig, ReviewAction, ReviewFailureExcerpt, ReviewSummaryModel,
    StartupSnapshot, SuiteSelectionModel,
};
use crate::protocol::{
    load_command_from_str, load_events_from_ndjson_tolerant, to_json as protocol_to_json,
    ReviewDecisionPayload, RunSuitePayload, SmokeProtocolCommand,
};
use crate::session::{
    allocate_next_command_identity, generate_session_id, load_failure_from_str,
    load_result_from_str, update_session_global_reset_default_atomically,
    write_command_document_atomically, write_initial_session_documents, InitialSessionMetadata,
    SmokeFailureDocument, SmokeHostStateDocument, SmokeRunResultDocument, SmokeSessionPaths,
    HOST_STATE_IDLE, HOST_STATE_READY, HOST_STATE_REVIEW_REQUIRED,
};
use crate::ui_suite_list::{render_pre_run_surface, render_review_surface};
use crate::unity_launcher::{spawn_unity_host, UnityHostLaunchConfig, UnityHostSupervisorStatus};
use std::fs;
use std::path::PathBuf;
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

pub fn run_overlay_bootstrap(config: &OverlayBootstrapConfig) -> Result<StartupSnapshot, String> {
    let catalog_raw = fs::read_to_string(&config.catalog_path).map_err(|error| {
        format!(
            "failed to read catalog '{}': {error}",
            config.catalog_path.display()
        )
    })?;
    let catalog = load_catalog_from_str(&catalog_raw)
        .map_err(|error| format!("catalog validation failed: {error}"))?;

    let suite_model = SuiteSelectionModel::new_from_catalog(&catalog)
        .map_err(|error| format!("suite model initialization failed: {error}"))?;

    let session_paths = SmokeSessionPaths::new(config.session_root.clone())
        .map_err(|error| format!("session root initialization failed: {error}"))?;
    session_paths
        .ensure_layout()
        .map_err(|error| format!("session layout initialization failed: {error}"))?;

    let session_id = generate_session_id();
    let metadata = InitialSessionMetadata {
        catalog_path: config.catalog_path.display().to_string(),
        project_path: config.project_path.display().to_string(),
        overlay_version: env!("CARGO_PKG_VERSION").to_string(),
        host_version: "asmlite-smoke-host".to_string(),
        package_version: "com.staples.asm-lite".to_string(),
        unity_version: "unknown".to_string(),
        global_reset_default: suite_model.global_reset_default.as_str().to_string(),
        capabilities: vec!["launch-session".to_string()],
    };

    write_initial_session_documents(&session_paths, &session_id, &catalog, &metadata)
        .map_err(|error| format!("failed to write initial session documents: {error}"))?;

    update_session_global_reset_default_atomically(
        &session_paths,
        suite_model.global_reset_default.as_str(),
    )
    .map_err(|error| format!("failed to persist session reset default: {error}"))?;

    let unity_executable = config
        .unity_executable
        .clone()
        .unwrap_or_else(|| PathBuf::from("Unity"));

    let mut launch_config = UnityHostLaunchConfig::new(
        unity_executable,
        config.project_path.clone(),
        config.session_root.clone(),
        config.catalog_path.clone(),
    );
    launch_config.startup_timeout_seconds = config.tuning.startup_timeout_seconds;
    launch_config.heartbeat_seconds = config.tuning.heartbeat_seconds;
    launch_config.exit_on_ready = config.exit_on_ready;

    let mut child = spawn_unity_host(&launch_config)
        .map_err(|error| format!("unity host launch failed: {error}"))?;

    let reader = EventReader::new(
        session_paths.clone(),
        config.tuning.startup_timeout_seconds,
        config.tuning.stale_after_seconds,
    );
    let started = Instant::now();

    loop {
        let elapsed = started.elapsed().as_secs();
        let process_exit_code = child
            .try_wait()
            .map_err(|error| format!("failed to poll Unity host process: {error}"))?
            .map(|status| status.code().unwrap_or(1));

        let poll_result = reader.poll(&unix_epoch_seconds_utc(), elapsed, process_exit_code);

        match poll_result.status {
            UnityHostSupervisorStatus::Ready => {
                let host_state = poll_result.host_state.as_ref().ok_or_else(|| {
                    "unity host reported ready without host-state payload".to_string()
                })?;
                let app_state = app_state_from_host_state(host_state);

                if matches!(app_state, AppState::ReviewRequired) {
                    let review_summary = if host_state.active_run_id.trim().is_empty() {
                        None
                    } else {
                        load_review_summary_for_run(&session_paths, &host_state.active_run_id).ok()
                    };
                    if let Some(surface) =
                        render_review_surface_for_state(&app_state, review_summary.as_ref())
                    {
                        println!("{surface}");
                    } else {
                        println!(
                            "review required for run '{}' but no review summary was available",
                            host_state.active_run_id
                        );
                    }
                    let _ = terminate_child_if_running(&mut child, process_exit_code);
                    return Ok(snapshot_from_poll(&poll_result));
                }

                let dispatched =
                    dispatch_selected_suite_run_command(&session_paths, host_state, &suite_model)?;
                println!(
                    "dispatched run-suite command: id={}, seq={}, suite={}",
                    dispatched.command_id,
                    dispatched.command_seq,
                    dispatched
                        .run_suite
                        .as_ref()
                        .map(|payload| payload.suite_id.as_str())
                        .unwrap_or_default()
                );
                if let Some(surface) = render_suite_surface_for_state(&app_state, &suite_model) {
                    println!("{surface}");
                }
                let _ = terminate_child_if_running(&mut child, process_exit_code);
                return Ok(snapshot_from_poll(&poll_result));
            }
            UnityHostSupervisorStatus::ExitedCleanly if config.exit_on_ready => {
                if let Some(host_state) = poll_result.host_state.as_ref() {
                    let app_state = app_state_from_host_state(host_state);

                    if matches!(app_state, AppState::ReviewRequired) {
                        let review_summary = if host_state.active_run_id.trim().is_empty() {
                            None
                        } else {
                            load_review_summary_for_run(&session_paths, &host_state.active_run_id)
                                .ok()
                        };
                        if let Some(surface) =
                            render_review_surface_for_state(&app_state, review_summary.as_ref())
                        {
                            println!("{surface}");
                        } else {
                            println!(
                                "review required for run '{}' but no review summary was available",
                                host_state.active_run_id
                            );
                        }
                        return Ok(snapshot_from_poll(&poll_result));
                    }

                    let dispatched = dispatch_selected_suite_run_command(
                        &session_paths,
                        host_state,
                        &suite_model,
                    )?;
                    println!(
                        "dispatched run-suite command: id={}, seq={}, suite={}",
                        dispatched.command_id,
                        dispatched.command_seq,
                        dispatched
                            .run_suite
                            .as_ref()
                            .map(|payload| payload.suite_id.as_str())
                            .unwrap_or_default()
                    );
                    if let Some(surface) = render_suite_surface_for_state(&app_state, &suite_model)
                    {
                        println!("{surface}");
                    }
                } else if let Some(surface) =
                    render_suite_surface_for_state(&AppState::SuiteSelect, &suite_model)
                {
                    println!("{surface}");
                }
                return Ok(snapshot_from_poll(&poll_result));
            }
            UnityHostSupervisorStatus::Starting => {
                thread::sleep(Duration::from_millis(
                    config.tuning.poll_interval_millis.max(25),
                ));
            }
            UnityHostSupervisorStatus::Stalled
            | UnityHostSupervisorStatus::Crashed
            | UnityHostSupervisorStatus::ExitedCleanly
            | UnityHostSupervisorStatus::ExitedWithError
            | UnityHostSupervisorStatus::TimedOut => {
                let _ = terminate_child_if_running(&mut child, process_exit_code);
                return Err(format!(
                    "overlay bootstrap failed in state {}: status={:?}, warnings={:?}",
                    AppState::HostError.as_str(),
                    poll_result.status,
                    poll_result.warnings
                ));
            }
        }
    }
}

pub fn app_state_from_host_state(host_state: &SmokeHostStateDocument) -> AppState {
    match host_state.state.as_str() {
        HOST_STATE_REVIEW_REQUIRED => AppState::ReviewRequired,
        HOST_STATE_READY | HOST_STATE_IDLE => AppState::SuiteSelect,
        _ => AppState::WaitingForReady,
    }
}

pub fn render_suite_surface_for_state(
    app_state: &AppState,
    suite_model: &SuiteSelectionModel,
) -> Option<String> {
    if matches!(app_state, AppState::SuiteSelect) {
        Some(render_pre_run_surface(suite_model))
    } else {
        None
    }
}

pub fn render_review_surface_for_state(
    app_state: &AppState,
    review_model: Option<&ReviewSummaryModel>,
) -> Option<String> {
    if matches!(app_state, AppState::ReviewRequired) {
        review_model.map(render_review_surface)
    } else {
        None
    }
}

pub fn build_review_summary_model(
    result: &SmokeRunResultDocument,
    failure: Option<&SmokeFailureDocument>,
    fallback_failure_message: Option<&str>,
) -> ReviewSummaryModel {
    let normalized_result = result.result.trim().to_ascii_lowercase();
    let failure_excerpt = if normalized_result == "failed" {
        if let Some(failure_doc) = failure.filter(|item| item.run_id == result.run_id) {
            let context_line = failure_doc
                .last_events
                .iter()
                .find(|line| !line.trim().is_empty())
                .cloned()
                .or_else(|| {
                    let hint = failure_doc.debug_hint.trim();
                    if hint.is_empty() {
                        None
                    } else {
                        Some(hint.to_string())
                    }
                });

            Some(ReviewFailureExcerpt {
                step_label: failure_doc.step_label.clone(),
                failure_message: failure_doc.failure_message.clone(),
                context_line,
            })
        } else if let Some(message) = fallback_failure_message {
            let normalized = message.trim();
            if normalized.is_empty() {
                None
            } else {
                Some(ReviewFailureExcerpt {
                    step_label: "Latest event".to_string(),
                    failure_message: normalized.to_string(),
                    context_line: None,
                })
            }
        } else {
            None
        }
    } else {
        None
    };

    ReviewSummaryModel {
        run_id: result.run_id.clone(),
        suite_id: result.suite_id.clone(),
        suite_label: result.suite_label.clone(),
        run_result: normalized_result,
        failure_excerpt,
    }
}

pub fn load_review_summary_for_run(
    session_paths: &SmokeSessionPaths,
    run_id: &str,
) -> Result<ReviewSummaryModel, String> {
    let normalized_run_id = run_id.trim();
    if normalized_run_id.is_empty() {
        return Err("run_id must not be blank".to_string());
    }

    let runs_root = session_paths.runs_directory_path();
    let entries = fs::read_dir(&runs_root)
        .map_err(|error| format!("failed to enumerate run directories: {error}"))?;

    for entry in entries {
        let entry =
            entry.map_err(|error| format!("failed to read run directory entry: {error}"))?;
        let file_type = entry
            .file_type()
            .map_err(|error| format!("failed to read run directory type: {error}"))?;
        if !file_type.is_dir() {
            continue;
        }

        let result_path = entry.path().join("result.json");
        if !result_path.exists() {
            continue;
        }

        let raw_result = fs::read_to_string(&result_path)
            .map_err(|error| format!("failed to read result artifact: {error}"))?;
        let result = load_result_from_str(&raw_result)
            .map_err(|error| format!("failed to parse result artifact: {error}"))?;

        if result.run_id != normalized_run_id {
            continue;
        }

        let failure = if result.artifact_paths.failure_path.trim().is_empty() {
            None
        } else {
            let failure_path = session_paths
                .resolve_session_relative_path(
                    &result.artifact_paths.failure_path,
                    "artifactPaths.failurePath",
                )
                .map_err(|error| format!("failed to resolve failure artifact path: {error}"))?;

            if failure_path.exists() {
                let raw_failure = fs::read_to_string(&failure_path)
                    .map_err(|error| format!("failed to read failure artifact: {error}"))?;
                let parsed_failure = load_failure_from_str(&raw_failure)
                    .map_err(|error| format!("failed to parse failure artifact: {error}"))?;
                if parsed_failure.run_id == result.run_id {
                    Some(parsed_failure)
                } else {
                    None
                }
            } else {
                None
            }
        };

        let fallback_message =
            extract_fallback_failure_message_from_events_slice(session_paths, &result)
                .ok()
                .flatten();

        return Ok(build_review_summary_model(
            &result,
            failure.as_ref(),
            fallback_message.as_deref(),
        ));
    }

    Err(format!(
        "could not find result artifact for run_id '{normalized_run_id}'"
    ))
}

fn extract_fallback_failure_message_from_events_slice(
    session_paths: &SmokeSessionPaths,
    result: &SmokeRunResultDocument,
) -> Result<Option<String>, String> {
    if result.artifact_paths.events_slice_path.trim().is_empty() {
        return Ok(None);
    }

    let events_slice_path = session_paths
        .resolve_session_relative_path(
            &result.artifact_paths.events_slice_path,
            "artifactPaths.eventsSlicePath",
        )
        .map_err(|error| format!("failed to resolve events slice path: {error}"))?;

    if !events_slice_path.exists() {
        return Ok(None);
    }

    let raw = fs::read_to_string(events_slice_path)
        .map_err(|error| format!("failed to read events slice artifact: {error}"))?;
    let events = load_events_from_ndjson_tolerant(&raw)
        .map_err(|error| format!("failed to parse events slice artifact: {error}"))?;

    let message = events
        .iter()
        .rev()
        .find(|event| {
            matches!(
                event.event_type.as_str(),
                "step-failed" | "suite-failed" | "command-rejected"
            )
        })
        .map(|event| event.message.clone())
        .or_else(|| {
            events
                .iter()
                .rev()
                .find(|event| !event.message.trim().is_empty())
                .map(|event| event.message.clone())
        });

    Ok(message)
}

pub fn dispatch_review_decision_command(
    session_paths: &SmokeSessionPaths,
    host_state: &SmokeHostStateDocument,
    review_summary: &ReviewSummaryModel,
    action: ReviewAction,
) -> Result<SmokeProtocolCommand, String> {
    if review_summary.run_id.trim().is_empty() {
        return Err("review decision dispatch requires non-blank run_id".to_string());
    }
    if review_summary.suite_id.trim().is_empty() {
        return Err("review decision dispatch requires non-blank suite_id".to_string());
    }

    let (command_seq, command_id) =
        allocate_next_command_identity(host_state.last_command_seq, "review-decision")
            .map_err(|error| format!("failed to allocate review-decision identity: {error}"))?;

    let command = SmokeProtocolCommand {
        protocol_version: host_state.protocol_version.trim().to_string(),
        session_id: host_state.session_id.trim().to_string(),
        command_id: command_id.clone(),
        command_seq,
        command_type: "review-decision".to_string(),
        created_at_utc: unix_epoch_seconds_utc(),
        launch_session: None,
        run_suite: None,
        review_decision: Some(ReviewDecisionPayload {
            run_id: review_summary.run_id.trim().to_string(),
            suite_id: review_summary.suite_id.trim().to_string(),
            decision: action.decision_token().to_string(),
            requested_by: "operator".to_string(),
            notes: "operator-review-decision".to_string(),
        }),
    };

    let command_path = session_paths
        .command_path(command_seq, "review-decision", &command_id)
        .map_err(|error| format!("failed to build review-decision path: {error}"))?;
    write_command_document_atomically(&command_path, &command, true)
        .map_err(|error| format!("failed to write review-decision command: {error}"))?;

    let normalized = protocol_to_json(&command, false)
        .map_err(|error| format!("failed to normalize review-decision command: {error}"))?;
    load_command_from_str(&normalized)
        .map_err(|error| format!("review-decision command failed protocol validation: {error}"))?;

    Ok(command)
}

fn dispatch_selected_suite_run_command(
    session_paths: &SmokeSessionPaths,
    host_state: &SmokeHostStateDocument,
    suite_model: &SuiteSelectionModel,
) -> Result<SmokeProtocolCommand, String> {
    let selected_suite = suite_model
        .selected_suite()
        .ok_or_else(|| "selected suite could not be resolved from catalog".to_string())?;

    let (command_seq, command_id) =
        allocate_next_command_identity(host_state.last_command_seq, "run-suite")
            .map_err(|error| format!("failed to allocate run-suite command identity: {error}"))?;

    let command = build_run_suite_command(
        &host_state.session_id,
        &host_state.protocol_version,
        command_seq,
        &command_id,
        &selected_suite.suite_id,
        suite_model.global_reset_default.as_str(),
    );

    let command_path = session_paths
        .command_path(command_seq, "run-suite", &command_id)
        .map_err(|error| format!("failed to build command path: {error}"))?;
    write_command_document_atomically(&command_path, &command, true)
        .map_err(|error| format!("failed to write run-suite command: {error}"))?;

    Ok(command)
}

fn build_run_suite_command(
    session_id: &str,
    protocol_version: &str,
    command_seq: i32,
    command_id: &str,
    suite_id: &str,
    requested_reset_default: &str,
) -> SmokeProtocolCommand {
    SmokeProtocolCommand {
        protocol_version: protocol_version.trim().to_string(),
        session_id: session_id.trim().to_string(),
        command_id: command_id.trim().to_string(),
        command_seq,
        command_type: "run-suite".to_string(),
        created_at_utc: unix_epoch_seconds_utc(),
        launch_session: None,
        run_suite: Some(RunSuitePayload {
            suite_id: suite_id.trim().to_string(),
            requested_by: "operator".to_string(),
            requested_reset_default: requested_reset_default.trim().to_string(),
            reason: "operator-selected".to_string(),
        }),
        review_decision: None,
    }
}

fn snapshot_from_poll(poll_result: &StartupPollResult) -> StartupSnapshot {
    StartupSnapshot {
        status: poll_result.status,
        last_event_seq: poll_result.events.last().map(|event| event.event_seq),
        last_event_type: poll_result
            .events
            .last()
            .map(|event| event.event_type.clone()),
        host_state: poll_result
            .host_state
            .as_ref()
            .map(|state| state.state.clone()),
        warning_count: poll_result.warnings.len(),
    }
}

fn terminate_child_if_running(
    child: &mut std::process::Child,
    process_exit_code: Option<i32>,
) -> Result<(), String> {
    if process_exit_code.is_none() {
        child
            .kill()
            .map_err(|error| format!("failed to terminate Unity host process: {error}"))?;
        let _ = child.wait();
    }

    Ok(())
}

fn unix_epoch_seconds_utc() -> String {
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_else(|_| Duration::from_secs(0));
    format!("{}Z", now.as_secs())
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;
    use crate::session::{load_failure_fixture, load_result_fixture};
    use std::fs;
    use std::path::PathBuf;

    #[test]
    fn suite_list_only_renders_in_suite_select_state() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let suite_model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        assert!(render_suite_surface_for_state(&AppState::Boot, &suite_model).is_none());
        assert!(render_suite_surface_for_state(&AppState::HostError, &suite_model).is_none());

        let rendered = render_suite_surface_for_state(&AppState::SuiteSelect, &suite_model)
            .expect("suite select should render");
        assert!(rendered.contains("Selected suite:"));
        assert!(rendered.contains(crate::model::RUN_DISPATCH_READY_MESSAGE));
    }

    #[test]
    fn app_state_maps_review_required_host_state() {
        let host_state = SmokeHostStateDocument {
            session_id: "session-20260423T043708Z-8f02f9b1".to_string(),
            protocol_version: "1.0.0".to_string(),
            state: HOST_STATE_REVIEW_REQUIRED.to_string(),
            host_version: "host-dev".to_string(),
            unity_version: "2022.3.22f1".to_string(),
            heartbeat_utc: "2026-04-23T04:37:10Z".to_string(),
            last_event_seq: 12,
            last_command_seq: 4,
            active_run_id: "run-0001-lifecycle-roundtrip".to_string(),
            message: "review pending".to_string(),
        };

        assert_eq!(
            app_state_from_host_state(&host_state),
            AppState::ReviewRequired
        );
    }

    #[test]
    fn build_review_summary_uses_failure_artifact_excerpt_for_failed_run() {
        let mut result =
            load_result_fixture("result.sample.json").expect("result fixture should load");
        let failure =
            load_failure_fixture("failure.sample.json").expect("failure fixture should load");

        result.run_id = failure.run_id.clone();
        result.suite_id = failure.suite_id.clone();
        result.suite_label = failure.suite_label.clone();
        result.result = "failed".to_string();

        let summary = build_review_summary_model(&result, Some(&failure), None);
        let excerpt = summary
            .failure_excerpt
            .expect("failed run should include failure excerpt");
        assert_eq!(excerpt.step_label, "Validate runtime component");
        assert!(excerpt
            .failure_message
            .contains("Runtime ASM-Lite component missing expected parameter state."));
    }

    #[test]
    fn build_review_summary_ignores_mismatched_failure_run_id_for_pass_run() {
        let result = load_result_fixture("result.sample.json").expect("result fixture should load");
        let failure =
            load_failure_fixture("failure.sample.json").expect("failure fixture should load");

        let summary = build_review_summary_model(&result, Some(&failure), None);
        assert_eq!(summary.run_result, "passed");
        assert!(summary.failure_excerpt.is_none());
    }

    #[test]
    fn dispatch_selected_suite_run_command_writes_monotonic_run_suite_payload() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let suite_model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let session_paths = SmokeSessionPaths::new(make_temp_session_root())
            .expect("session root should initialize");
        session_paths
            .ensure_layout()
            .expect("layout should initialize");

        let host_state = SmokeHostStateDocument {
            session_id: "session-20260423T043708Z-8f02f9b1".to_string(),
            protocol_version: "1.0.0".to_string(),
            state: "ready".to_string(),
            host_version: "host-dev".to_string(),
            unity_version: "2022.3.22f1".to_string(),
            heartbeat_utc: "2026-04-23T04:37:10Z".to_string(),
            last_event_seq: 12,
            last_command_seq: 4,
            active_run_id: String::new(),
            message: "ready".to_string(),
        };

        let command =
            dispatch_selected_suite_run_command(&session_paths, &host_state, &suite_model)
                .expect("run-suite dispatch should succeed");

        assert!(command.command_seq > 0);
        assert!(!command.command_id.trim().is_empty());
        assert_eq!(command.command_seq, 5);
        assert_eq!(command.command_id, "cmd_000005_run-suite");
        let payload = command
            .run_suite
            .as_ref()
            .expect("run-suite payload should exist");
        assert_eq!(payload.suite_id, suite_model.selected_suite_id);
        assert_eq!(payload.requested_by, "operator");
        assert_eq!(payload.reason, "operator-selected");

        let command_path = session_paths
            .command_path(command.command_seq, "run-suite", &command.command_id)
            .expect("command path should build");
        let raw = fs::read_to_string(&command_path).expect("command document should exist");
        assert!(raw.contains("\"suiteId\": \"open-select-add\""));
        assert!(raw.contains("\"requestedResetDefault\": \"SceneReload\""));
    }

    #[test]
    fn dispatch_review_decision_command_writes_protocol_valid_payload() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root())
            .expect("session root should initialize");
        session_paths
            .ensure_layout()
            .expect("layout should initialize");

        let host_state = SmokeHostStateDocument {
            session_id: "session-20260423T043708Z-8f02f9b1".to_string(),
            protocol_version: "1.0.0".to_string(),
            state: HOST_STATE_REVIEW_REQUIRED.to_string(),
            host_version: "host-dev".to_string(),
            unity_version: "2022.3.22f1".to_string(),
            heartbeat_utc: "2026-04-23T04:37:10Z".to_string(),
            last_event_seq: 22,
            last_command_seq: 7,
            active_run_id: "run-0002-playmode-runtime-validation".to_string(),
            message: "review pending".to_string(),
        };

        let review_summary = ReviewSummaryModel {
            run_id: "run-0002-playmode-runtime-validation".to_string(),
            suite_id: "playmode-runtime-validation".to_string(),
            suite_label: "Enter / Validate / Exit Playmode".to_string(),
            run_result: "failed".to_string(),
            failure_excerpt: None,
        };

        let command = dispatch_review_decision_command(
            &session_paths,
            &host_state,
            &review_summary,
            ReviewAction::RerunSuite,
        )
        .expect("review-decision dispatch should succeed");

        assert_eq!(command.command_seq, 8);
        assert_eq!(command.command_id, "cmd_000008_review-decision");
        let payload = command
            .review_decision
            .as_ref()
            .expect("review payload should exist");
        assert_eq!(payload.decision, "rerun-suite");
        assert_eq!(payload.requested_by, "operator");
        assert_eq!(payload.notes, "operator-review-decision");

        let command_path = session_paths
            .command_path(command.command_seq, "review-decision", &command.command_id)
            .expect("command path should build");
        let raw = fs::read_to_string(&command_path).expect("review command should be written");
        let parsed = load_command_from_str(&raw).expect("review command should parse");
        assert_eq!(parsed.command_type, "review-decision");
    }

    fn make_temp_session_root() -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be monotonic")
            .as_nanos();
        std::env::temp_dir().join(format!("asmlite-app-dispatch-{nanos}"))
    }
}
