use asmlite_smoke_contract::app::run_overlay_bootstrap;
use asmlite_smoke_contract::model::{OverlayBootstrapConfig, OverlayMode, RuntimeTuning};
use std::env;
use std::path::PathBuf;

fn main() {
    match parse_cli_args() {
        Ok(config) => match run_overlay_bootstrap(&config) {
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
        },
        Err(error) => {
            eprintln!("error: {error}");
            eprintln!("usage: asmlite_smoke_overlay --repo-root <path> --project-path <path> --catalog-path <path> --session-root <path> --mode <overlay|uat> [--unity-executable <path>] [--startup-timeout-seconds <N>] [--heartbeat-seconds <N>] [--stale-after-seconds <N>] [--poll-interval-millis <N>] [--exit-on-ready]");
            std::process::exit(2);
        }
    }
}

fn parse_cli_args() -> Result<OverlayBootstrapConfig, String> {
    let mut repo_root: Option<PathBuf> = None;
    let mut project_path: Option<PathBuf> = None;
    let mut catalog_path: Option<PathBuf> = None;
    let mut session_root: Option<PathBuf> = None;
    let mut mode: Option<OverlayMode> = None;
    let mut unity_executable: Option<PathBuf> = None;
    let mut exit_on_ready = false;
    let mut tuning = RuntimeTuning::default();

    let args: Vec<String> = env::args().skip(1).collect();
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

fn parse_string_value(args: &[String], index: &mut usize, key: &str) -> Result<String, String> {
    *index += 1;
    if *index >= args.len() {
        return Err(format!("{key} requires a value"));
    }

    Ok(args[*index].clone())
}
