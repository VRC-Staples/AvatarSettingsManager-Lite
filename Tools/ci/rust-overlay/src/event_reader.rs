use crate::protocol::{load_events_from_file_tolerant, SmokeProtocolEvent};
use crate::session::{load_host_state_from_str, SmokeHostStateDocument, SmokeSessionPaths};
use crate::unity_launcher::{
    map_host_state_to_status, map_missing_host_state_to_status, map_process_exit_to_status,
    UnityHostSupervisorStatus,
};
use std::fs;

#[derive(Debug, Clone)]
pub struct StartupPollResult {
    pub status: UnityHostSupervisorStatus,
    pub host_state: Option<SmokeHostStateDocument>,
    pub events: Vec<SmokeProtocolEvent>,
    pub warnings: Vec<String>,
}

#[derive(Debug, Clone)]
pub struct EventReader {
    session_paths: SmokeSessionPaths,
    startup_timeout_seconds: u64,
    stale_after_seconds: u64,
}

impl EventReader {
    pub fn new(
        session_paths: SmokeSessionPaths,
        startup_timeout_seconds: u64,
        stale_after_seconds: u64,
    ) -> Self {
        Self {
            session_paths,
            startup_timeout_seconds,
            stale_after_seconds,
        }
    }

    pub fn poll(
        &self,
        now_utc: &str,
        startup_elapsed_seconds: u64,
        process_exit_code: Option<i32>,
    ) -> StartupPollResult {
        let mut warnings = Vec::new();
        let host_state = self.read_host_state_tolerant(&mut warnings);
        let events = self.read_events_tolerant(&mut warnings);

        let mut status = if let Some(host_state_document) = &host_state {
            map_host_state_to_status(host_state_document, now_utc, self.stale_after_seconds)
        } else if process_exit_code.is_some() {
            map_process_exit_to_status(process_exit_code)
        } else {
            map_missing_host_state_to_status(startup_elapsed_seconds, self.startup_timeout_seconds)
        };

        if matches!(status, UnityHostSupervisorStatus::Starting) && process_exit_code.is_some() {
            status = map_process_exit_to_status(process_exit_code);
        }

        StartupPollResult {
            status,
            host_state,
            events,
            warnings,
        }
    }

    fn read_host_state_tolerant(
        &self,
        warnings: &mut Vec<String>,
    ) -> Option<SmokeHostStateDocument> {
        let path = self.session_paths.host_state_path();
        if !path.exists() {
            return None;
        }

        let raw = match fs::read_to_string(&path) {
            Ok(raw) => raw,
            Err(error) => {
                warnings.push(format!("host-state read failed: {error}"));
                return None;
            }
        };

        if raw.trim().is_empty() {
            return None;
        }

        match load_host_state_from_str(&raw) {
            Ok(parsed) => Some(parsed),
            Err(error) => {
                warnings.push(format!("host-state parse deferred: {error}"));
                None
            }
        }
    }

    fn read_events_tolerant(&self, warnings: &mut Vec<String>) -> Vec<SmokeProtocolEvent> {
        let path = self.session_paths.events_log_path();
        match load_events_from_file_tolerant(&path) {
            Ok(events) => events,
            Err(error) => {
                warnings.push(format!("events parse deferred: {error}"));
                Vec::new()
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::path::PathBuf;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn poll_missing_host_state_before_timeout_maps_to_starting() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root())
            .expect("session root should initialize");
        session_paths
            .ensure_layout()
            .expect("layout should initialize");
        let reader = EventReader::new(session_paths, 120, 15);

        let snapshot = reader.poll("2026-04-23T04:37:12Z", 30, None);
        assert_eq!(snapshot.status, UnityHostSupervisorStatus::Starting);
        assert!(snapshot.host_state.is_none());
    }

    #[test]
    fn poll_missing_host_state_after_timeout_maps_to_timed_out() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root())
            .expect("session root should initialize");
        session_paths
            .ensure_layout()
            .expect("layout should initialize");
        let reader = EventReader::new(session_paths, 120, 15);

        let snapshot = reader.poll("2026-04-23T04:37:12Z", 121, None);
        assert_eq!(snapshot.status, UnityHostSupervisorStatus::TimedOut);
    }

    #[test]
    fn poll_host_state_ready_maps_to_ready() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root())
            .expect("session root should initialize");
        session_paths
            .ensure_layout()
            .expect("layout should initialize");
        let reader = EventReader::new(session_paths.clone(), 120, 15);

        fs::write(
            session_paths.host_state_path(),
            r#"{
  "sessionId": "session-20260423T043708Z-8f02f9b1",
  "protocolVersion": "1.0.0",
  "state": "ready",
  "hostVersion": "unity-host-dev",
  "unityVersion": "2022.3.22f1",
  "heartbeatUtc": "2026-04-23T04:37:09Z",
  "lastEventSeq": 2,
  "lastCommandSeq": 1,
  "activeRunId": "",
  "message": "ready"
}"#,
        )
        .expect("host-state fixture should write");

        let snapshot = reader.poll("2026-04-23T04:37:12Z", 2, None);
        assert_eq!(snapshot.status, UnityHostSupervisorStatus::Ready);
        assert!(snapshot.warnings.is_empty());
    }

    #[test]
    fn poll_process_exit_without_host_state_maps_to_exited_with_error() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root())
            .expect("session root should initialize");
        session_paths
            .ensure_layout()
            .expect("layout should initialize");
        let reader = EventReader::new(session_paths, 120, 15);

        let snapshot = reader.poll("2026-04-23T04:37:12Z", 5, Some(1));
        assert_eq!(snapshot.status, UnityHostSupervisorStatus::ExitedWithError);
    }

    fn make_temp_session_root() -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be monotonic")
            .as_nanos();
        std::env::temp_dir().join(format!("asmlite-event-reader-{nanos}"))
    }
}
