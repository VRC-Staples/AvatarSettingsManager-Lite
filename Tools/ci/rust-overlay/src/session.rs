use crate::protocol::{protocol_fixture_directory, SmokeProtocolEvent};
use serde::{Deserialize, Serialize};
use std::fmt;
use std::fs;

pub const SUPPORTED_PROTOCOL_VERSION: &str = "1.0.0";

pub const HOST_STATE_READY: &str = "ready";
pub const HOST_STATE_RUNNING: &str = "running";
pub const HOST_STATE_REVIEW_REQUIRED: &str = "review-required";
pub const HOST_STATE_IDLE: &str = "idle";
pub const HOST_STATE_PROTOCOL_ERROR: &str = "protocol-error";
pub const HOST_STATE_EXITING: &str = "exiting";

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeSessionDocument {
    pub session_id: String,
    pub protocol_version: String,
    pub catalog_version: i32,
    pub catalog_path: String,
    pub catalog_snapshot_path: String,
    pub project_path: String,
    pub overlay_version: String,
    pub host_version: String,
    pub package_version: String,
    pub unity_version: String,
    #[serde(default)]
    pub capabilities: Vec<String>,
    pub global_reset_default: String,
    pub created_at_utc: String,
    #[serde(default)]
    pub updated_at_utc: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeHostStateDocument {
    pub session_id: String,
    pub protocol_version: String,
    pub state: String,
    pub host_version: String,
    pub unity_version: String,
    pub heartbeat_utc: String,
    pub last_event_seq: i32,
    pub last_command_seq: i32,
    #[serde(default)]
    pub active_run_id: String,
    pub message: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ProtocolCompatibilityResult {
    pub is_compatible: bool,
    pub message: String,
}

impl ProtocolCompatibilityResult {
    pub fn compatible() -> Self {
        Self {
            is_compatible: true,
            message: String::new(),
        }
    }

    pub fn incompatible(message: String) -> Self {
        Self {
            is_compatible: false,
            message,
        }
    }
}

#[derive(Debug, Clone)]
pub struct SessionContractError(pub String);

impl fmt::Display for SessionContractError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for SessionContractError {}

impl From<std::io::Error> for SessionContractError {
    fn from(value: std::io::Error) -> Self {
        SessionContractError(value.to_string())
    }
}

impl From<serde_json::Error> for SessionContractError {
    fn from(value: serde_json::Error) -> Self {
        SessionContractError(value.to_string())
    }
}

pub fn load_session_fixture(file_name: &str) -> Result<SmokeSessionDocument, SessionContractError> {
    let fixture_path = protocol_fixture_directory().join(file_name);
    let raw = fs::read_to_string(&fixture_path)?;
    load_session_from_json(&raw)
}

pub fn load_session_from_json(raw: &str) -> Result<SmokeSessionDocument, SessionContractError> {
    let mut value: SmokeSessionDocument = serde_json::from_str(raw)?;
    normalize_and_validate_session(&mut value)?;
    Ok(value)
}

pub fn load_host_state_fixture(file_name: &str) -> Result<SmokeHostStateDocument, SessionContractError> {
    let fixture_path = protocol_fixture_directory().join(file_name);
    let raw = fs::read_to_string(&fixture_path)?;
    load_host_state_from_json(&raw)
}

pub fn load_host_state_from_json(raw: &str) -> Result<SmokeHostStateDocument, SessionContractError> {
    let mut value: SmokeHostStateDocument = serde_json::from_str(raw)?;
    normalize_and_validate_host_state(&mut value)?;
    Ok(value)
}

pub fn evaluate_protocol_compatibility(
    host_supported_protocol_version: &str,
    session: &SmokeSessionDocument,
    catalog_protocol_version: &str,
) -> Result<ProtocolCompatibilityResult, SessionContractError> {
    let host_protocol = require_non_blank(host_supported_protocol_version, "hostSupportedProtocolVersion")?;
    let catalog_protocol = require_non_blank(catalog_protocol_version, "catalogProtocolVersion")?;

    let mut normalized_session = session.clone();
    normalize_and_validate_session(&mut normalized_session)?;

    if normalized_session.protocol_version != host_protocol
        || normalized_session.protocol_version != catalog_protocol
    {
        return Ok(ProtocolCompatibilityResult::incompatible(format!(
            "Protocol version mismatch. host={host}, session={session}, catalog={catalog}. Update overlay and host to the same protocolVersion before accepting run-suite commands.",
            host = host_protocol,
            session = normalized_session.protocol_version,
            catalog = catalog_protocol
        )));
    }

    Ok(ProtocolCompatibilityResult::compatible())
}

pub fn can_accept_run_suite(host_state: &SmokeHostStateDocument) -> (bool, String) {
    let mut normalized = host_state.clone();
    if let Err(error) = normalize_and_validate_host_state(&mut normalized) {
        return (false, error.0);
    }

    if normalized.state == HOST_STATE_PROTOCOL_ERROR {
        return (
            false,
            "Cannot accept run-suite command while host state is protocol-error. Resolve protocol mismatch and relaunch session."
                .to_string(),
        );
    }

    if normalized.state == HOST_STATE_EXITING {
        return (
            false,
            "Cannot accept run-suite command while host state is exiting.".to_string(),
        );
    }

    (true, String::new())
}

pub fn build_protocol_error_host_state(
    session: &SmokeSessionDocument,
    message: &str,
    last_event_seq: i32,
    last_command_seq: i32,
) -> Result<SmokeHostStateDocument, SessionContractError> {
    let mut normalized_session = session.clone();
    normalize_and_validate_session(&mut normalized_session)?;

    let message = require_non_blank(message, "message")?;

    let mut state = SmokeHostStateDocument {
        session_id: normalized_session.session_id,
        protocol_version: normalized_session.protocol_version,
        state: HOST_STATE_PROTOCOL_ERROR.to_string(),
        host_version: normalized_session.host_version,
        unity_version: normalized_session.unity_version,
        heartbeat_utc: normalized_session.created_at_utc,
        last_event_seq,
        last_command_seq,
        active_run_id: String::new(),
        message,
    };

    normalize_and_validate_host_state(&mut state)?;
    Ok(state)
}

pub fn build_protocol_error_event(
    session: &SmokeSessionDocument,
    command_id: &str,
    event_seq: i32,
    message: &str,
) -> Result<SmokeProtocolEvent, SessionContractError> {
    let mut normalized_session = session.clone();
    normalize_and_validate_session(&mut normalized_session)?;

    let command_id = require_non_blank(command_id, "commandId")?;
    let message = require_non_blank(message, "message")?;

    let mut capabilities: Vec<String> = normalized_session
        .capabilities
        .into_iter()
        .map(|item| normalize_optional(&item))
        .filter(|item| !item.is_empty())
        .collect();
    capabilities.sort();
    capabilities.dedup();

    let event = SmokeProtocolEvent {
        protocol_version: normalized_session.protocol_version,
        session_id: normalized_session.session_id,
        event_id: format!("evt_{event_seq:06}_protocol-error"),
        event_seq,
        event_type: "protocol-error".to_string(),
        timestamp_utc: normalized_session.created_at_utc,
        command_id,
        run_id: String::new(),
        group_id: String::new(),
        suite_id: String::new(),
        case_id: String::new(),
        step_id: String::new(),
        effective_reset_policy: String::new(),
        host_state: HOST_STATE_PROTOCOL_ERROR.to_string(),
        message,
        review_decision_options: Vec::new(),
        supported_capabilities: capabilities,
    };

    Ok(event)
}

fn normalize_and_validate_session(value: &mut SmokeSessionDocument) -> Result<(), SessionContractError> {
    value.session_id = require_non_blank(&value.session_id, "sessionId")?;
    value.protocol_version = require_non_blank(&value.protocol_version, "protocolVersion")?;

    if value.catalog_version <= 0 {
        return Err(SessionContractError(
            "catalogVersion must be greater than zero.".to_string(),
        ));
    }

    value.catalog_path = require_non_blank(&value.catalog_path, "catalogPath")?;
    value.catalog_snapshot_path = require_non_blank(&value.catalog_snapshot_path, "catalogSnapshotPath")?;
    value.project_path = require_non_blank(&value.project_path, "projectPath")?;
    value.overlay_version = require_non_blank(&value.overlay_version, "overlayVersion")?;
    value.host_version = require_non_blank(&value.host_version, "hostVersion")?;
    value.package_version = require_non_blank(&value.package_version, "packageVersion")?;
    value.unity_version = require_non_blank(&value.unity_version, "unityVersion")?;
    value.global_reset_default = require_non_blank(&value.global_reset_default, "globalResetDefault")?;
    value.created_at_utc = require_non_blank(&value.created_at_utc, "createdAtUtc")?;
    value.updated_at_utc = normalize_optional(&value.updated_at_utc);

    value.capabilities = value
        .capabilities
        .iter()
        .map(|item| normalize_optional(item))
        .filter(|item| !item.is_empty())
        .collect();
    value.capabilities.sort();
    value.capabilities.dedup();

    Ok(())
}

fn normalize_and_validate_host_state(value: &mut SmokeHostStateDocument) -> Result<(), SessionContractError> {
    value.session_id = require_non_blank(&value.session_id, "sessionId")?;
    value.protocol_version = require_non_blank(&value.protocol_version, "protocolVersion")?;
    value.state = require_non_blank(&value.state, "state")?;

    if !is_supported_host_state(&value.state) {
        return Err(SessionContractError(format!(
            "state '{state}' is not supported.",
            state = value.state
        )));
    }

    value.host_version = require_non_blank(&value.host_version, "hostVersion")?;
    value.unity_version = require_non_blank(&value.unity_version, "unityVersion")?;
    value.heartbeat_utc = require_non_blank(&value.heartbeat_utc, "heartbeatUtc")?;

    if value.last_event_seq < 0 {
        return Err(SessionContractError(
            "lastEventSeq must be greater than or equal to zero.".to_string(),
        ));
    }

    if value.last_command_seq < 0 {
        return Err(SessionContractError(
            "lastCommandSeq must be greater than or equal to zero.".to_string(),
        ));
    }

    value.active_run_id = normalize_optional(&value.active_run_id);
    value.message = require_non_blank(&value.message, "message")?;

    Ok(())
}

fn normalize_optional(value: &str) -> String {
    value.trim().to_string()
}

fn require_non_blank(value: &str, path: &str) -> Result<String, SessionContractError> {
    let normalized = normalize_optional(value);
    if normalized.is_empty() {
        return Err(SessionContractError(format!("{path} is required.")));
    }

    Ok(normalized)
}

fn is_supported_host_state(value: &str) -> bool {
    matches!(
        value,
        HOST_STATE_READY
            | HOST_STATE_RUNNING
            | HOST_STATE_REVIEW_REQUIRED
            | HOST_STATE_IDLE
            | HOST_STATE_PROTOCOL_ERROR
            | HOST_STATE_EXITING
    )
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;
    use crate::protocol::load_event_fixture;

    #[test]
    fn compatibility_valid_startup_exact_protocol_match_allows_run_suite_acceptance() {
        let catalog = load_canonical_catalog().expect("load canonical catalog fixture");
        let session = load_session_fixture("session.valid.json").expect("load valid session fixture");

        let compatibility = evaluate_protocol_compatibility(
            SUPPORTED_PROTOCOL_VERSION,
            &session,
            &catalog.protocol_version,
        )
        .expect("evaluate compatibility");

        assert!(compatibility.is_compatible);
        assert!(compatibility.message.is_empty());

        let host_state =
            load_host_state_fixture("host-state.ready.json").expect("load ready host state fixture");
        let (can_accept, reason) = can_accept_run_suite(&host_state);
        assert!(can_accept, "expected run-suite acceptance: {reason}");

        let run_suite = crate::protocol::load_command_fixture("run-suite.json")
            .expect("load run-suite command fixture");
        assert_eq!(run_suite.command_type, "run-suite");
    }

    #[test]
    fn compatibility_mismatch_startup_emits_protocol_error_contract_and_blocks_startup() {
        let catalog = load_canonical_catalog().expect("load canonical catalog fixture");
        let session =
            load_session_fixture("session.protocol-mismatch.json").expect("load mismatch session fixture");

        let compatibility = evaluate_protocol_compatibility(
            SUPPORTED_PROTOCOL_VERSION,
            &session,
            &catalog.protocol_version,
        )
        .expect("evaluate compatibility");

        assert!(!compatibility.is_compatible);
        assert!(compatibility.message.contains("Protocol version mismatch"));
        assert!(compatibility.message.contains("host=1.0.0"));
        assert!(compatibility.message.contains("session=2.0.0"));

        let host_state = load_host_state_fixture("host-state.protocol-error.json")
            .expect("load protocol-error host state fixture");
        assert_eq!(host_state.state, HOST_STATE_PROTOCOL_ERROR);

        let events = load_event_fixture("events.protocol-error.ndjson")
            .expect("load protocol-error event fixture");
        assert!(!events.is_empty());
        assert!(events.iter().any(|event| event.event_type == "protocol-error"));
        assert!(events
            .iter()
            .any(|event| event.event_type == "command-rejected"));

        let protocol_error_state = build_protocol_error_host_state(
            &session,
            &compatibility.message,
            host_state.last_event_seq,
            host_state.last_command_seq,
        )
        .expect("build protocol-error host state");
        assert_eq!(protocol_error_state.state, HOST_STATE_PROTOCOL_ERROR);

        let protocol_error_event = build_protocol_error_event(
            &session,
            "cmd_000002_run-suite",
            host_state.last_event_seq,
            &compatibility.message,
        )
        .expect("build protocol-error event");
        assert_eq!(protocol_error_event.host_state, HOST_STATE_PROTOCOL_ERROR);
        assert_eq!(protocol_error_event.event_type, "protocol-error");
    }

    #[test]
    fn compatibility_post_mismatch_run_suite_is_rejected_while_host_state_is_protocol_error() {
        let host_state = load_host_state_fixture("host-state.protocol-error.json")
            .expect("load protocol-error host state fixture");

        let (can_accept, reason) = can_accept_run_suite(&host_state);
        assert!(!can_accept);
        assert!(reason.contains("protocol-error"));

        let run_suite = crate::protocol::load_command_fixture("run-suite.json")
            .expect("load run-suite command fixture");
        assert_eq!(run_suite.command_type, "run-suite");
    }
}
