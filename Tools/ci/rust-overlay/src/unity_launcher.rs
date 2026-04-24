use crate::session::{
    SmokeHostStateDocument, HOST_STATE_CRASHED, HOST_STATE_EXITING, HOST_STATE_IDLE,
    HOST_STATE_READY, HOST_STATE_STALLED,
};
use std::fmt;
use std::path::PathBuf;
use std::process::{Child, Command, Stdio};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnityLauncherError(pub String);

impl fmt::Display for UnityLauncherError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for UnityLauncherError {}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct UnityHostLaunchConfig {
    pub unity_executable: PathBuf,
    pub project_path: PathBuf,
    pub session_root: PathBuf,
    pub catalog_path: PathBuf,
    pub scene_path: String,
    pub avatar_name: String,
    pub startup_timeout_seconds: u64,
    pub heartbeat_seconds: u64,
    pub exit_on_ready: bool,
}

impl UnityHostLaunchConfig {
    pub fn new(
        unity_executable: impl Into<PathBuf>,
        project_path: impl Into<PathBuf>,
        session_root: impl Into<PathBuf>,
        catalog_path: impl Into<PathBuf>,
    ) -> Self {
        Self {
            unity_executable: unity_executable.into(),
            project_path: project_path.into(),
            session_root: session_root.into(),
            catalog_path: catalog_path.into(),
            scene_path: "Assets/Click ME.unity".to_string(),
            avatar_name: "Oct25_Dress".to_string(),
            startup_timeout_seconds: 120,
            heartbeat_seconds: 5,
            exit_on_ready: false,
        }
    }
}

pub fn build_unity_host_args(
    config: &UnityHostLaunchConfig,
) -> Result<Vec<String>, UnityLauncherError> {
    if config.project_path.as_os_str().is_empty() {
        return Err(UnityLauncherError(
            "project_path must not be empty.".to_string(),
        ));
    }
    if config.session_root.as_os_str().is_empty() {
        return Err(UnityLauncherError(
            "session_root must not be empty.".to_string(),
        ));
    }
    if config.catalog_path.as_os_str().is_empty() {
        return Err(UnityLauncherError(
            "catalog_path must not be empty.".to_string(),
        ));
    }
    if config.scene_path.trim().is_empty() {
        return Err(UnityLauncherError(
            "scene_path must not be blank.".to_string(),
        ));
    }
    if config.avatar_name.trim().is_empty() {
        return Err(UnityLauncherError(
            "avatar_name must not be blank.".to_string(),
        ));
    }
    if config.startup_timeout_seconds == 0 {
        return Err(UnityLauncherError(
            "startup_timeout_seconds must be greater than zero.".to_string(),
        ));
    }
    if config.heartbeat_seconds == 0 {
        return Err(UnityLauncherError(
            "heartbeat_seconds must be greater than zero.".to_string(),
        ));
    }

    Ok(vec![
        "-projectPath".to_string(),
        config.project_path.display().to_string(),
        "-executeMethod".to_string(),
        "ASMLite.Tests.Editor.ASMLiteSmokeOverlayHost.RunFromCommandLine".to_string(),
        "-asmliteSmokeSessionRoot".to_string(),
        config.session_root.display().to_string(),
        "-asmliteSmokeCatalogPath".to_string(),
        config.catalog_path.display().to_string(),
        "-asmliteSmokeScenePath".to_string(),
        config.scene_path.trim().to_string(),
        "-asmliteSmokeAvatarName".to_string(),
        config.avatar_name.trim().to_string(),
        "-asmliteSmokeStartupTimeoutSeconds".to_string(),
        config.startup_timeout_seconds.to_string(),
        "-asmliteSmokeHeartbeatSeconds".to_string(),
        config.heartbeat_seconds.to_string(),
        "-asmliteSmokeExitOnReady".to_string(),
        if config.exit_on_ready {
            "true".to_string()
        } else {
            "false".to_string()
        },
    ])
}

pub fn spawn_unity_host(config: &UnityHostLaunchConfig) -> Result<Child, UnityLauncherError> {
    if config.unity_executable.as_os_str().is_empty() {
        return Err(UnityLauncherError(
            "unity_executable must not be empty.".to_string(),
        ));
    }

    let args = build_unity_host_args(config)?;

    Command::new(&config.unity_executable)
        .args(args)
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .spawn()
        .map_err(|error| {
            UnityLauncherError(format!(
                "failed to spawn Unity host '{}': {error}",
                config.unity_executable.display()
            ))
        })
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum UnityHostSupervisorStatus {
    Starting,
    Ready,
    Stalled,
    Crashed,
    ExitedCleanly,
    ExitedWithError,
    TimedOut,
}

pub fn map_host_state_to_status(
    host_state: &SmokeHostStateDocument,
    now_utc: &str,
    stale_after_seconds: u64,
) -> UnityHostSupervisorStatus {
    if host_state.state == HOST_STATE_STALLED {
        return UnityHostSupervisorStatus::Stalled;
    }

    if host_state.state == HOST_STATE_CRASHED {
        return UnityHostSupervisorStatus::Crashed;
    }

    if host_state.state == HOST_STATE_EXITING {
        return UnityHostSupervisorStatus::ExitedCleanly;
    }

    if host_state.state == HOST_STATE_READY || host_state.state == HOST_STATE_IDLE {
        if heartbeat_is_stale(&host_state.heartbeat_utc, now_utc, stale_after_seconds) {
            return UnityHostSupervisorStatus::Stalled;
        }

        return UnityHostSupervisorStatus::Ready;
    }

    UnityHostSupervisorStatus::Starting
}

pub fn map_process_exit_to_status(exit_code: Option<i32>) -> UnityHostSupervisorStatus {
    match exit_code {
        Some(0) => UnityHostSupervisorStatus::ExitedCleanly,
        Some(_) => UnityHostSupervisorStatus::ExitedWithError,
        None => UnityHostSupervisorStatus::Starting,
    }
}

pub fn startup_timed_out(started_elapsed_seconds: u64, timeout_seconds: u64) -> bool {
    started_elapsed_seconds > timeout_seconds
}

pub fn map_missing_host_state_to_status(
    started_elapsed_seconds: u64,
    timeout_seconds: u64,
) -> UnityHostSupervisorStatus {
    if startup_timed_out(started_elapsed_seconds, timeout_seconds) {
        UnityHostSupervisorStatus::TimedOut
    } else {
        UnityHostSupervisorStatus::Starting
    }
}

fn heartbeat_is_stale(heartbeat_utc: &str, now_utc: &str, stale_after_seconds: u64) -> bool {
    let heartbeat_seconds = match parse_utc_rfc3339_seconds(heartbeat_utc) {
        Some(value) => value,
        None => return true,
    };

    let now_seconds = match parse_utc_rfc3339_seconds(now_utc) {
        Some(value) => value,
        None => return true,
    };

    if now_seconds < heartbeat_seconds {
        return false;
    }

    (now_seconds - heartbeat_seconds) > stale_after_seconds as i64
}

fn parse_utc_rfc3339_seconds(value: &str) -> Option<i64> {
    let token = value.trim();
    if token.is_empty() {
        return None;
    }

    let (date_part, time_part) = token.split_once('T')?;
    let mut date_iter = date_part.split('-');
    let year: i32 = date_iter.next()?.parse().ok()?;
    let month: u32 = date_iter.next()?.parse().ok()?;
    let day: u32 = date_iter.next()?.parse().ok()?;

    if !(1..=12).contains(&month) || !(1..=31).contains(&day) {
        return None;
    }

    if time_part.len() < 8 {
        return None;
    }

    let hour: i64 = time_part[0..2].parse().ok()?;
    let minute: i64 = time_part[3..5].parse().ok()?;
    let second: i64 = time_part[6..8].parse().ok()?;
    if hour > 23 || minute > 59 || second > 59 {
        return None;
    }

    let mut index = 8usize;
    let bytes = time_part.as_bytes();
    if index < bytes.len() && bytes[index] == b'.' {
        index += 1;
        while index < bytes.len() && bytes[index].is_ascii_digit() {
            index += 1;
        }
    }

    let timezone = &time_part[index..];
    let offset_seconds: i64 = if timezone == "Z" {
        0
    } else if timezone.len() == 6 && (timezone.starts_with('+') || timezone.starts_with('-')) {
        let sign = if timezone.starts_with('-') { -1 } else { 1 };
        let offset_hour: i64 = timezone[1..3].parse().ok()?;
        let offset_minute: i64 = timezone[4..6].parse().ok()?;
        if timezone.as_bytes()[3] != b':' || offset_hour > 23 || offset_minute > 59 {
            return None;
        }

        sign * (offset_hour * 3600 + offset_minute * 60)
    } else {
        return None;
    };

    let days = days_from_civil(year, month, day);
    let day_seconds = hour * 3600 + minute * 60 + second;
    Some(days * 86_400 + day_seconds - offset_seconds)
}

fn days_from_civil(year: i32, month: u32, day: u32) -> i64 {
    let year = year - if month <= 2 { 1 } else { 0 };
    let era = if year >= 0 { year } else { year - 399 } / 400;
    let yoe = year - era * 400;
    let month = month as i32;
    let doy = (153 * (month + if month > 2 { -3 } else { 9 }) + 2) / 5 + day as i32 - 1;
    let doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;
    (era * 146097 + doe - 719468) as i64
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn launch_args_include_smoke_host_execute_method() {
        let config = UnityHostLaunchConfig::new(
            "C:/Unity/Unity.exe",
            "C:/Projects/ASM-Lite",
            "C:/tmp/session",
            "C:/tmp/catalog.json",
        );

        let args = build_unity_host_args(&config).expect("args should build");
        assert!(args.contains(&"-executeMethod".to_string()));
        assert!(args.contains(
            &"ASMLite.Tests.Editor.ASMLiteSmokeOverlayHost.RunFromCommandLine".to_string()
        ));
        assert!(args.contains(&"-asmliteSmokeSessionRoot".to_string()));
        assert!(args.contains(&"-asmliteSmokeCatalogPath".to_string()));
    }

    #[test]
    fn launch_args_default_to_click_me_and_oct25_dress() {
        let config = UnityHostLaunchConfig::new(
            "C:/Unity/Unity.exe",
            "C:/Projects/ASM-Lite",
            "C:/tmp/session",
            "C:/tmp/catalog.json",
        );

        let args = build_unity_host_args(&config).expect("args should build");
        assert!(args.contains(&"Assets/Click ME.unity".to_string()));
        assert!(args.contains(&"Oct25_Dress".to_string()));
        assert!(args.contains(&"120".to_string()));
        assert!(args.contains(&"5".to_string()));
        assert!(args.contains(&"false".to_string()));
    }

    #[test]
    fn launch_args_reject_blank_scene_or_avatar() {
        let mut blank_scene = UnityHostLaunchConfig::new(
            "C:/Unity/Unity.exe",
            "C:/Projects/ASM-Lite",
            "C:/tmp/session",
            "C:/tmp/catalog.json",
        );
        blank_scene.scene_path = "   ".to_string();
        let scene_error = build_unity_host_args(&blank_scene).expect_err("blank scene should fail");
        assert!(scene_error.to_string().contains("scene_path"));

        let mut blank_avatar = UnityHostLaunchConfig::new(
            "C:/Unity/Unity.exe",
            "C:/Projects/ASM-Lite",
            "C:/tmp/session",
            "C:/tmp/catalog.json",
        );
        blank_avatar.avatar_name = "   ".to_string();
        let avatar_error =
            build_unity_host_args(&blank_avatar).expect_err("blank avatar should fail");
        assert!(avatar_error.to_string().contains("avatar_name"));
    }

    #[test]
    fn host_state_ready_with_fresh_heartbeat_maps_to_ready() {
        let host_state = sample_host_state(HOST_STATE_READY, "2026-04-23T04:37:09Z");
        let status = map_host_state_to_status(&host_state, "2026-04-23T04:37:12Z", 5);
        assert_eq!(status, UnityHostSupervisorStatus::Ready);
    }

    #[test]
    fn stale_heartbeat_maps_to_stalled() {
        let host_state = sample_host_state(HOST_STATE_READY, "2026-04-23T04:37:00Z");
        let status = map_host_state_to_status(&host_state, "2026-04-23T04:37:10Z", 5);
        assert_eq!(status, UnityHostSupervisorStatus::Stalled);
    }

    #[test]
    fn host_state_stalled_maps_to_stalled() {
        let host_state = sample_host_state(HOST_STATE_STALLED, "2026-04-23T04:37:09Z");
        let status = map_host_state_to_status(&host_state, "2026-04-23T04:37:10Z", 5);
        assert_eq!(status, UnityHostSupervisorStatus::Stalled);
    }

    #[test]
    fn host_state_crashed_maps_to_crashed() {
        let host_state = sample_host_state(HOST_STATE_CRASHED, "2026-04-23T04:37:09Z");
        let status = map_host_state_to_status(&host_state, "2026-04-23T04:37:10Z", 5);
        assert_eq!(status, UnityHostSupervisorStatus::Crashed);
    }

    #[test]
    fn host_state_exiting_maps_to_exited_cleanly() {
        let host_state = sample_host_state(HOST_STATE_EXITING, "2026-04-23T04:37:09Z");
        let status = map_host_state_to_status(&host_state, "2026-04-23T04:37:10Z", 5);
        assert_eq!(status, UnityHostSupervisorStatus::ExitedCleanly);
    }

    #[test]
    fn process_exit_mapping_distinguishes_success_and_failure() {
        assert_eq!(
            map_process_exit_to_status(Some(0)),
            UnityHostSupervisorStatus::ExitedCleanly
        );
        assert_eq!(
            map_process_exit_to_status(Some(13)),
            UnityHostSupervisorStatus::ExitedWithError
        );
        assert_eq!(
            map_process_exit_to_status(None),
            UnityHostSupervisorStatus::Starting
        );
    }

    #[test]
    fn startup_timeout_returns_true_only_when_elapsed_greater_than_timeout() {
        assert!(!startup_timed_out(120, 120));
        assert!(startup_timed_out(121, 120));
    }

    #[test]
    fn missing_host_state_before_timeout_maps_to_starting() {
        assert_eq!(
            map_missing_host_state_to_status(30, 120),
            UnityHostSupervisorStatus::Starting
        );
    }

    #[test]
    fn missing_host_state_after_timeout_maps_to_timed_out() {
        assert_eq!(
            map_missing_host_state_to_status(121, 120),
            UnityHostSupervisorStatus::TimedOut
        );
    }

    fn sample_host_state(state: &str, heartbeat_utc: &str) -> SmokeHostStateDocument {
        SmokeHostStateDocument {
            session_id: "session-20260423T043708Z-8f02f9b1".to_string(),
            protocol_version: "1.0.0".to_string(),
            state: state.to_string(),
            host_version: "host-2026.04.23".to_string(),
            unity_version: "2022.3.22f1".to_string(),
            heartbeat_utc: heartbeat_utc.to_string(),
            last_event_seq: 2,
            last_command_seq: 0,
            active_run_id: String::new(),
            message: "status".to_string(),
        }
    }
}
