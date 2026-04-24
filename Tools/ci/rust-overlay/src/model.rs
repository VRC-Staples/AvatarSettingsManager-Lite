use crate::unity_launcher::UnityHostSupervisorStatus;
use std::path::PathBuf;

pub const DEFAULT_STARTUP_TIMEOUT_SECONDS: u64 = 120;
pub const DEFAULT_HEARTBEAT_SECONDS: u64 = 5;
pub const DEFAULT_STALE_AFTER_SECONDS: u64 = 15;
pub const DEFAULT_POLL_INTERVAL_MILLIS: u64 = 250;

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum OverlayMode {
    Overlay,
    Uat,
}

impl OverlayMode {
    pub fn parse(value: &str) -> Result<Self, String> {
        match value.trim().to_ascii_lowercase().as_str() {
            "overlay" => Ok(Self::Overlay),
            "uat" => Ok(Self::Uat),
            _ => Err(format!(
                "unsupported --mode '{}'; expected one of: overlay, uat",
                value
            )),
        }
    }

    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Overlay => "overlay",
            Self::Uat => "uat",
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum AppState {
    Boot,
    LaunchingUnity,
    WaitingForReady,
    SuiteSelect,
    HostError,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RuntimeTuning {
    pub startup_timeout_seconds: u64,
    pub heartbeat_seconds: u64,
    pub stale_after_seconds: u64,
    pub poll_interval_millis: u64,
}

impl Default for RuntimeTuning {
    fn default() -> Self {
        Self {
            startup_timeout_seconds: DEFAULT_STARTUP_TIMEOUT_SECONDS,
            heartbeat_seconds: DEFAULT_HEARTBEAT_SECONDS,
            stale_after_seconds: DEFAULT_STALE_AFTER_SECONDS,
            poll_interval_millis: DEFAULT_POLL_INTERVAL_MILLIS,
        }
    }
}

#[derive(Debug, Clone)]
pub struct OverlayBootstrapConfig {
    pub repo_root: PathBuf,
    pub project_path: PathBuf,
    pub catalog_path: PathBuf,
    pub session_root: PathBuf,
    pub mode: OverlayMode,
    pub unity_executable: Option<PathBuf>,
    pub exit_on_ready: bool,
    pub tuning: RuntimeTuning,
}

#[derive(Debug, Clone)]
pub struct StartupSnapshot {
    pub status: UnityHostSupervisorStatus,
    pub last_event_seq: Option<i32>,
    pub last_event_type: Option<String>,
    pub host_state: Option<String>,
    pub warning_count: usize,
}
