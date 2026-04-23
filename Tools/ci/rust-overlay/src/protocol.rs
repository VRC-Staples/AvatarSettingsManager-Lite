use serde::{Deserialize, Serialize};
use std::collections::HashSet;
use std::fmt;
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ProtocolError(pub String);

impl fmt::Display for ProtocolError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for ProtocolError {}

impl From<std::io::Error> for ProtocolError {
    fn from(value: std::io::Error) -> Self {
        Self(value.to_string())
    }
}

impl From<serde_json::Error> for ProtocolError {
    fn from(value: serde_json::Error) -> Self {
        Self(value.to_string())
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeProtocolCommand {
    pub protocol_version: String,
    pub session_id: String,
    pub command_id: String,
    pub command_seq: i32,
    pub command_type: String,
    pub created_at_utc: String,
    pub launch_session: Option<LaunchSessionPayload>,
    pub run_suite: Option<RunSuitePayload>,
    pub review_decision: Option<ReviewDecisionPayload>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct LaunchSessionPayload {
    pub catalog_version: i32,
    pub catalog_path: String,
    pub catalog_snapshot_path: String,
    pub project_path: String,
    pub package_version: String,
    pub unity_version: String,
    pub overlay_version: String,
    pub host_version: String,
    pub global_reset_default: String,
    pub requested_by: String,
    #[serde(default)]
    pub capabilities: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct RunSuitePayload {
    pub suite_id: String,
    pub requested_by: String,
    pub requested_reset_default: String,
    pub reason: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct ReviewDecisionPayload {
    pub run_id: String,
    pub suite_id: String,
    pub decision: String,
    pub requested_by: String,
    pub notes: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeProtocolEvent {
    pub protocol_version: String,
    pub session_id: String,
    pub event_id: String,
    pub event_seq: i32,
    pub event_type: String,
    pub timestamp_utc: String,
    pub command_id: String,
    #[serde(default)]
    pub run_id: String,
    #[serde(default)]
    pub group_id: String,
    #[serde(default)]
    pub suite_id: String,
    #[serde(default)]
    pub case_id: String,
    #[serde(default)]
    pub step_id: String,
    #[serde(default)]
    pub effective_reset_policy: String,
    #[serde(default)]
    pub host_state: String,
    pub message: String,
    #[serde(default)]
    pub review_decision_options: Vec<String>,
    #[serde(default)]
    pub supported_capabilities: Vec<String>,
}

const HOST_STATE_READY: &str = "ready";
const HOST_STATE_RUNNING: &str = "running";
const HOST_STATE_REVIEW_REQUIRED: &str = "review-required";
const HOST_STATE_IDLE: &str = "idle";
const HOST_STATE_PROTOCOL_ERROR: &str = "protocol-error";
const HOST_STATE_EXITING: &str = "exiting";

pub fn protocol_fixture_directory() -> PathBuf {
    repository_root().join("Tools/ci/smoke/protocol-fixtures")
}

pub fn load_command_fixture(file_name: &str) -> Result<SmokeProtocolCommand, ProtocolError> {
    let raw = fs::read_to_string(protocol_fixture_directory().join(file_name))?;
    load_command_from_str(&raw)
}

pub fn load_command_from_str(raw: &str) -> Result<SmokeProtocolCommand, ProtocolError> {
    if raw.trim().is_empty() {
        return Err(ProtocolError("Smoke protocol command JSON is required.".to_string()));
    }

    let mut command: SmokeProtocolCommand = serde_json::from_str(raw)?;
    normalize_and_validate_command(&mut command)?;
    Ok(command)
}

pub fn to_json(command: &SmokeProtocolCommand, pretty: bool) -> Result<String, ProtocolError> {
    let mut cloned = command.clone();
    normalize_and_validate_command(&mut cloned)?;
    if pretty {
        Ok(serde_json::to_string_pretty(&cloned)?)
    } else {
        Ok(serde_json::to_string(&cloned)?)
    }
}

pub fn load_event_fixture(file_name: &str) -> Result<Vec<SmokeProtocolEvent>, ProtocolError> {
    let raw = fs::read_to_string(protocol_fixture_directory().join(file_name))?;
    load_events_from_ndjson(&raw)
}

pub fn load_events_from_ndjson(raw: &str) -> Result<Vec<SmokeProtocolEvent>, ProtocolError> {
    if raw.trim().is_empty() {
        return Err(ProtocolError("Smoke protocol event NDJSON is required.".to_string()));
    }

    let mut events = Vec::new();
    let mut event_ids = HashSet::new();
    let mut previous_seq = 0;
    for (index, line) in raw.lines().filter(|line| !line.trim().is_empty()).enumerate() {
        let mut event: SmokeProtocolEvent = serde_json::from_str(line)?;
        normalize_and_validate_event(&mut event, index + 1, &mut event_ids, &mut previous_seq)?;
        events.push(event);
    }

    if events.is_empty() {
        return Err(ProtocolError("Smoke protocol event NDJSON requires at least one line.".to_string()));
    }

    Ok(events)
}

pub fn to_ndjson(events: &[SmokeProtocolEvent]) -> Result<String, ProtocolError> {
    if events.is_empty() {
        return Err(ProtocolError("Smoke protocol events must not be empty.".to_string()));
    }

    let mut builder = String::new();
    let mut event_ids = HashSet::new();
    let mut previous_seq = 0;
    for (index, event) in events.iter().cloned().enumerate() {
        let mut cloned = event;
        normalize_and_validate_event(&mut cloned, index + 1, &mut event_ids, &mut previous_seq)?;
        builder.push_str(&serde_json::to_string(&cloned)?);
        builder.push('\n');
    }

    Ok(builder)
}

fn normalize_and_validate_command(command: &mut SmokeProtocolCommand) -> Result<(), ProtocolError> {
    command.protocol_version = require_non_blank(&command.protocol_version, "protocolVersion")?;
    command.session_id = require_non_blank(&command.session_id, "sessionId")?;
    command.command_id = require_non_blank(&command.command_id, "commandId")?;
    if command.command_seq <= 0 {
        return Err(ProtocolError("commandSeq must be greater than zero.".to_string()));
    }
    command.command_type = require_non_blank(&command.command_type, "commandType")?;
    command.created_at_utc = require_non_blank(&command.created_at_utc, "createdAtUtc")?;

    match command.command_type.as_str() {
        "launch-session" => {
            let payload = command.launch_session.as_mut().ok_or_else(|| {
                ProtocolError("launchSession payload is required for commandType 'launch-session'.".to_string())
            })?;
            if command.run_suite.is_some() || command.review_decision.is_some() {
                return Err(ProtocolError("launch-session command must not include unrelated payloads.".to_string()));
            }
            normalize_and_validate_launch_session(payload)?;
        }
        "run-suite" => {
            let payload = command.run_suite.as_mut().ok_or_else(|| {
                ProtocolError("runSuite payload is required for commandType 'run-suite'.".to_string())
            })?;
            if command.launch_session.is_some() || command.review_decision.is_some() {
                return Err(ProtocolError("run-suite command must not include unrelated payloads.".to_string()));
            }
            normalize_and_validate_run_suite(payload)?;
        }
        "review-decision" => {
            let payload = command.review_decision.as_mut().ok_or_else(|| {
                ProtocolError("reviewDecision payload is required for commandType 'review-decision'.".to_string())
            })?;
            if command.launch_session.is_some() || command.run_suite.is_some() {
                return Err(ProtocolError("review-decision command must not include unrelated payloads.".to_string()));
            }
            normalize_and_validate_review_decision(payload)?;
        }
        other => {
            return Err(ProtocolError(format!("commandType '{other}' is not supported.")));
        }
    }

    Ok(())
}

fn normalize_and_validate_launch_session(payload: &mut LaunchSessionPayload) -> Result<(), ProtocolError> {
    if payload.catalog_version <= 0 {
        return Err(ProtocolError("launchSession.catalogVersion must be greater than zero.".to_string()));
    }
    payload.catalog_path = require_non_blank(&payload.catalog_path, "launchSession.catalogPath")?;
    payload.catalog_snapshot_path = require_non_blank(&payload.catalog_snapshot_path, "launchSession.catalogSnapshotPath")?;
    payload.project_path = require_non_blank(&payload.project_path, "launchSession.projectPath")?;
    payload.package_version = require_non_blank(&payload.package_version, "launchSession.packageVersion")?;
    payload.unity_version = require_non_blank(&payload.unity_version, "launchSession.unityVersion")?;
    payload.overlay_version = require_non_blank(&payload.overlay_version, "launchSession.overlayVersion")?;
    payload.host_version = require_non_blank(&payload.host_version, "launchSession.hostVersion")?;
    payload.global_reset_default = require_non_blank(&payload.global_reset_default, "launchSession.globalResetDefault")?;
    payload.requested_by = require_non_blank(&payload.requested_by, "launchSession.requestedBy")?;
    payload.capabilities = normalize_string_vec(std::mem::take(&mut payload.capabilities));
    Ok(())
}

fn normalize_and_validate_run_suite(payload: &mut RunSuitePayload) -> Result<(), ProtocolError> {
    payload.suite_id = require_non_blank(&payload.suite_id, "runSuite.suiteId")?;
    payload.requested_by = require_non_blank(&payload.requested_by, "runSuite.requestedBy")?;
    payload.requested_reset_default = require_non_blank(&payload.requested_reset_default, "runSuite.requestedResetDefault")?;
    payload.reason = require_non_blank(&payload.reason, "runSuite.reason")?;
    Ok(())
}

fn normalize_and_validate_review_decision(payload: &mut ReviewDecisionPayload) -> Result<(), ProtocolError> {
    payload.run_id = require_non_blank(&payload.run_id, "reviewDecision.runId")?;
    payload.suite_id = require_non_blank(&payload.suite_id, "reviewDecision.suiteId")?;
    payload.decision = require_non_blank(&payload.decision, "reviewDecision.decision")?;
    payload.requested_by = require_non_blank(&payload.requested_by, "reviewDecision.requestedBy")?;
    payload.notes = require_non_blank(&payload.notes, "reviewDecision.notes")?;
    Ok(())
}

fn normalize_and_validate_event(
    event: &mut SmokeProtocolEvent,
    line_number: usize,
    event_ids: &mut HashSet<String>,
    previous_seq: &mut i32,
) -> Result<(), ProtocolError> {
    let prefix = format!("event line {line_number}");
    event.protocol_version = require_non_blank(&event.protocol_version, &(prefix.clone() + " protocolVersion"))?;
    event.session_id = require_non_blank(&event.session_id, &(prefix.clone() + " sessionId"))?;
    event.event_id = require_non_blank(&event.event_id, &(prefix.clone() + " eventId"))?;
    if !event_ids.insert(event.event_id.clone()) {
        return Err(ProtocolError(format!("{prefix} eventId must be unique.")));
    }
    if event.event_seq <= 0 {
        return Err(ProtocolError(format!("{prefix} eventSeq must be greater than zero.")));
    }
    if event.event_seq <= *previous_seq {
        return Err(ProtocolError(format!("{prefix} eventSeq must be strictly increasing.")));
    }
    *previous_seq = event.event_seq;
    event.event_type = require_non_blank(&event.event_type, &(prefix.clone() + " eventType"))?;
    event.timestamp_utc = require_non_blank(&event.timestamp_utc, &(prefix.clone() + " timestampUtc"))?;
    event.command_id = require_non_blank(&event.command_id, &(prefix.clone() + " commandId"))?;
    event.message = require_non_blank(&event.message, &(prefix.clone() + " message"))?;
    event.run_id = normalize_optional(&event.run_id);
    event.group_id = normalize_optional(&event.group_id);
    event.suite_id = normalize_optional(&event.suite_id);
    event.case_id = normalize_optional(&event.case_id);
    event.step_id = normalize_optional(&event.step_id);
    event.effective_reset_policy = normalize_optional(&event.effective_reset_policy);
    event.host_state = normalize_optional(&event.host_state);
    if !event.host_state.is_empty() && !is_supported_host_state(&event.host_state) {
        return Err(ProtocolError(format!(
            "{prefix} hostState '{}' is not supported.",
            event.host_state
        )));
    }
    event.review_decision_options = normalize_string_vec(std::mem::take(&mut event.review_decision_options));
    event.supported_capabilities = normalize_string_vec(std::mem::take(&mut event.supported_capabilities));
    Ok(())
}

fn normalize_string_vec(values: Vec<String>) -> Vec<String> {
    values
        .into_iter()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
        .collect()
}

fn normalize_optional(value: &str) -> String {
    value.trim().to_string()
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

fn require_non_blank(value: &str, path: &str) -> Result<String, ProtocolError> {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return Err(ProtocolError(format!("{path} must not be blank.")));
    }
    Ok(trimmed.to_string())
}

fn repository_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../..").canonicalize().unwrap_or_else(|_| {
        PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../..")
    })
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn protocol_command_fixtures_round_trip_preserve_required_fields() {
        let launch = round_trip(load_command_fixture("launch-session.json").expect("launch fixture should parse"));
        let run_suite = round_trip(load_command_fixture("run-suite.json").expect("run-suite fixture should parse"));
        let review = round_trip(load_command_fixture("review-decision.json").expect("review fixture should parse"));

        assert_eq!(launch.command_type, "launch-session");
        assert_eq!(launch.launch_session.expect("launch payload").global_reset_default, "SceneReload");
        assert_eq!(run_suite.command_type, "run-suite");
        assert_eq!(run_suite.run_suite.expect("run payload").suite_id, "lifecycle-roundtrip");
        assert_eq!(review.command_type, "review-decision");
        assert_eq!(review.review_decision.expect("review payload").decision, "return-to-suite-list");
    }

    #[test]
    fn protocol_event_fixture_preserves_ordering_and_protocol_fields() {
        let events = load_event_fixture("events.sample.ndjson").expect("event fixture should parse");
        let event_sequences: Vec<i32> = events.iter().map(|event| event.event_seq).collect();
        assert_eq!(event_sequences, vec![1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11]);
        assert!(events.iter().all(|event| event.protocol_version == "1.0.0"));
        assert_eq!(events[0].event_type, "session-started");
        assert_eq!(events[9].event_type, "review-required");
        assert_eq!(events[10].event_type, "session-idle");

        let round_tripped = load_events_from_ndjson(&to_ndjson(&events).expect("ndjson serialization should succeed"))
            .expect("round-tripped events should parse");
        assert_eq!(events.len(), round_tripped.len());
        assert_eq!(events[10].message, round_tripped[10].message);
    }

    #[test]
    fn protocol_rejects_missing_protocol_version() {
        let raw = fs::read_to_string(protocol_fixture_directory().join("launch-session.json"))
            .expect("launch fixture should exist")
            .replace("\"protocolVersion\": \"1.0.0\",\n", "");

        let error = load_command_from_str(&raw).expect_err("missing protocolVersion should fail");
        assert!(error.to_string().contains("protocolVersion"));
    }

    #[test]
    fn protocol_rejects_missing_typed_payload_for_command_type() {
        let raw = fs::read_to_string(protocol_fixture_directory().join("run-suite.json"))
            .expect("run-suite fixture should exist")
            .replace(
                "\"runSuite\": {\n    \"suiteId\": \"lifecycle-roundtrip\",\n    \"requestedBy\": \"operator\",\n    \"requestedResetDefault\": \"FullPackageRebuild\",\n    \"reason\": \"operator-selected\"\n  }",
                "\"reviewDecision\": {\n    \"runId\": \"run-0001-lifecycle-roundtrip\",\n    \"suiteId\": \"lifecycle-roundtrip\",\n    \"decision\": \"return-to-suite-list\",\n    \"requestedBy\": \"operator\",\n    \"notes\": \"wrong payload\"\n  }",
            );

        let error = load_command_from_str(&raw).expect_err("wrong typed payload should fail");
        assert!(error.to_string().contains("runSuite"));
    }

    fn round_trip(command: SmokeProtocolCommand) -> SmokeProtocolCommand {
        load_command_from_str(&to_json(&command, false).expect("json serialization should succeed"))
            .expect("round-tripped command should parse")
    }
}
