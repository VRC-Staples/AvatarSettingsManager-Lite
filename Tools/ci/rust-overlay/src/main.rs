use asmlite_smoke_contract::app::run_overlay_bootstrap;
use asmlite_smoke_contract::gui::run_overlay_window;
use asmlite_smoke_contract::model::{OverlayBootstrapConfig, OverlayMode, RuntimeTuning};
use std::env;
use std::path::PathBuf;

fn main() {
    match parse_cli_args() {
        Ok(config) => {
            if config.exit_on_ready {
                match run_overlay_bootstrap(&config) {
                    Ok(snapshot) => {
                        println!("mode: {}", config.mode.as_str());
                        println!("status: {:?}", snapshot.status);
                        if let Some(host_state) = snapshot.host_state {
                            println!("host-state: {host_state}");
                        }
                        if let Some(event_type) = snapshot.last_event_type {
                            println!("last-event: {event_type}");
                        }
                        if let Some(event_seq) = snapshot.last_event_seq {
                            println!("last-event-seq: {event_seq}");
                        }
                        println!("warnings: {}", snapshot.warning_count);
                    }
                    Err(error) => {
                        eprintln!("error: {error}");
                        std::process::exit(1);
                    }
                }
            } else if let Err(error) = run_overlay_window(config) {
                eprintln!("error: {error}");
                std::process::exit(1);
            }
        }
        Err(error) => {
            eprintln!("error: {error}");
            eprintln!("usage: asmlite_smoke_overlay --repo-root <path> --project-path <path> --catalog-path <path> --session-root <path> --mode <overlay|uat> [--unity-executable <path>] [--suite-id <id>] [--startup-timeout-seconds <N>] [--heartbeat-seconds <N>] [--stale-after-seconds <N>] [--poll-interval-millis <N>] [--exit-on-ready]");
            std::process::exit(2);
        }
    }
}

fn parse_cli_args() -> Result<OverlayBootstrapConfig, String> {
    parse_cli_args_from(env::args().skip(1).collect())
}

fn parse_cli_args_from(args: Vec<String>) -> Result<OverlayBootstrapConfig, String> {
    let mut repo_root: Option<PathBuf> = None;
    let mut project_path: Option<PathBuf> = None;
    let mut catalog_path: Option<PathBuf> = None;
    let mut session_root: Option<PathBuf> = None;
    let mut mode: Option<OverlayMode> = None;
    let mut unity_executable: Option<PathBuf> = None;
    let mut selected_suite_ids: Vec<String> = Vec::new();
    let mut exit_on_ready = false;
    let mut tuning = RuntimeTuning::default();

    let mut index = 0usize;
    while index < args.len() {
        let key = &args[index];
        match key.as_str() {
            "--repo-root" => {
                repo_root = Some(parse_path_value(&args, &mut index, key)?);
            }
            "--project-path" => {
                project_path = Some(parse_path_value(&args, &mut index, key)?);
            }
            "--catalog-path" => {
                catalog_path = Some(parse_path_value(&args, &mut index, key)?);
            }
            "--session-root" => {
                session_root = Some(parse_path_value(&args, &mut index, key)?);
            }
            "--mode" => {
                let value = parse_string_value(&args, &mut index, key)?;
                mode = Some(OverlayMode::parse(&value)?);
            }
            "--unity-executable" => {
                unity_executable = Some(parse_path_value(&args, &mut index, key)?);
            }
            "--suite-id" => {
                let raw = parse_string_value(&args, &mut index, key)?;
                selected_suite_ids.extend(parse_suite_id_batch(&raw)?);
            }
            "--startup-timeout-seconds" => {
                tuning.startup_timeout_seconds = parse_u64_value(&args, &mut index, key)?;
            }
            "--heartbeat-seconds" => {
                tuning.heartbeat_seconds = parse_u64_value(&args, &mut index, key)?;
            }
            "--stale-after-seconds" => {
                tuning.stale_after_seconds = parse_u64_value(&args, &mut index, key)?;
            }
            "--poll-interval-millis" => {
                tuning.poll_interval_millis = parse_u64_value(&args, &mut index, key)?;
            }
            "--exit-on-ready" => {
                exit_on_ready = true;
            }
            _ => {
                return Err(format!("unknown option: {key}"));
            }
        }

        index += 1;
    }

    let repo_root = repo_root.ok_or_else(|| "missing required --repo-root".to_string())?;
    let project_path = project_path.ok_or_else(|| "missing required --project-path".to_string())?;
    let catalog_path = catalog_path.ok_or_else(|| "missing required --catalog-path".to_string())?;
    let session_root = session_root.ok_or_else(|| "missing required --session-root".to_string())?;
    let mode = mode.ok_or_else(|| "missing required --mode".to_string())?;

    if tuning.startup_timeout_seconds == 0 {
        return Err("--startup-timeout-seconds must be greater than zero".to_string());
    }
    if tuning.heartbeat_seconds == 0 {
        return Err("--heartbeat-seconds must be greater than zero".to_string());
    }
    if tuning.poll_interval_millis == 0 {
        return Err("--poll-interval-millis must be greater than zero".to_string());
    }

    Ok(OverlayBootstrapConfig {
        repo_root,
        project_path,
        catalog_path,
        session_root,
        mode,
        unity_executable,
        selected_suite_ids,
        exit_on_ready,
        tuning,
    })
}

fn parse_path_value(args: &[String], index: &mut usize, key: &str) -> Result<PathBuf, String> {
    let value = parse_string_value(args, index, key)?;
    Ok(PathBuf::from(value))
}

fn parse_u64_value(args: &[String], index: &mut usize, key: &str) -> Result<u64, String> {
    let raw = parse_string_value(args, index, key)?;
    raw.parse::<u64>()
        .map_err(|_| format!("{key} expects an integer value, got '{raw}'"))
}

fn parse_suite_id_batch(raw: &str) -> Result<Vec<String>, String> {
    let values: Vec<String> = raw.split(',').map(str::trim).map(str::to_string).collect();
    if values.is_empty() || values.iter().any(|value| value.is_empty()) {
        return Err("--suite-id must not contain empty values".to_string());
    }
    Ok(values)
}

fn parse_string_value(args: &[String], index: &mut usize, key: &str) -> Result<String, String> {
    *index += 1;
    if *index >= args.len() {
        return Err(format!("{key} requires a value"));
    }

    Ok(args[*index].clone())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn cli_single_suite_id_becomes_one_item_selected_batch() {
        let config = parse_cli_args_from(base_args(["--suite-id", "setup-scene-avatar"]))
            .expect("single suite id should parse");

        assert_eq!(config.selected_suite_ids, vec!["setup-scene-avatar"]);
    }

    #[test]
    fn cli_repeated_and_csv_suite_ids_preserve_operator_order() {
        let config = parse_cli_args_from(base_args([
            "--suite-id",
            "setup-scene-avatar,lifecycle-roundtrip",
            "--suite-id",
            "playmode-runtime-validation",
        ]))
        .expect("batch suite ids should parse");

        assert_eq!(
            config.selected_suite_ids,
            vec![
                "setup-scene-avatar",
                "lifecycle-roundtrip",
                "playmode-runtime-validation"
            ]
        );
    }

    #[test]
    fn cli_rejects_empty_suite_id_values() {
        let error = parse_cli_args_from(base_args(["--suite-id", "setup-scene-avatar,"]))
            .expect_err("empty suite id entry should fail");

        assert!(error.contains("--suite-id must not contain empty values"));
    }

    fn base_args<const N: usize>(extra: [&str; N]) -> Vec<String> {
        let mut args = vec![
            "--repo-root",
            ".",
            "--project-path",
            "../Test Project/TestUnityProject",
            "--catalog-path",
            "Tools/ci/smoke/suite-catalog.json",
            "--session-root",
            "artifacts/smoke-overlay/test-session",
            "--mode",
            "uat",
        ]
        .into_iter()
        .map(str::to_string)
        .collect::<Vec<_>>();
        args.extend(extra.into_iter().map(str::to_string));
        args
    }
}
