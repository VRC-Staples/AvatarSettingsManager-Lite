use crate::catalog::load_catalog_from_str;
use crate::event_reader::{EventReader, StartupPollResult};
use crate::model::{AppState, OverlayBootstrapConfig, StartupSnapshot, SuiteSelectionModel};
use crate::session::{
    generate_session_id, update_session_global_reset_default_atomically,
    write_initial_session_documents, InitialSessionMetadata, SmokeSessionPaths,
};
use crate::ui_suite_list::render_pre_run_surface;
use crate::unity_launcher::{spawn_unity_host, UnityHostLaunchConfig, UnityHostSupervisorStatus};
use std::fs;
use std::path::PathBuf;
use std::thread;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

pub fn run_overlay_bootstrap(config: &OverlayBootstrapConfig) -> Result<StartupSnapshot, String> {
    let catalog_raw = fs::read_to_string(&config.catalog_path)
        .map_err(|error| format!("failed to read catalog '{}': {error}", config.catalog_path.display()))?;
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
        session_paths,
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
                if let Some(surface) = render_suite_surface_for_state(&AppState::SuiteSelect, &suite_model) {
                    println!("{surface}");
                }
                let _ = terminate_child_if_running(&mut child, process_exit_code);
                return Ok(snapshot_from_poll(&poll_result));
            }
            UnityHostSupervisorStatus::ExitedCleanly if config.exit_on_ready => {
                if let Some(surface) = render_suite_surface_for_state(&AppState::SuiteSelect, &suite_model) {
                    println!("{surface}");
                }
                return Ok(snapshot_from_poll(&poll_result));
            }
            UnityHostSupervisorStatus::Starting => {
                thread::sleep(Duration::from_millis(config.tuning.poll_interval_millis.max(25)));
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

fn snapshot_from_poll(poll_result: &StartupPollResult) -> StartupSnapshot {
    StartupSnapshot {
        status: poll_result.status,
        last_event_seq: poll_result.events.last().map(|event| event.event_seq),
        last_event_type: poll_result.events.last().map(|event| event.event_type.clone()),
        host_state: poll_result.host_state.as_ref().map(|state| state.state.clone()),
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

    #[test]
    fn suite_list_only_renders_in_suite_select_state() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let suite_model = SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        assert!(render_suite_surface_for_state(&AppState::Boot, &suite_model).is_none());
        assert!(render_suite_surface_for_state(&AppState::HostError, &suite_model).is_none());

        let rendered = render_suite_surface_for_state(&AppState::SuiteSelect, &suite_model)
            .expect("suite select should render");
        assert!(rendered.contains("Selected suite:"));
        assert!(rendered.contains(crate::model::PHASE07_RUN_GATE_MESSAGE));
    }
}
