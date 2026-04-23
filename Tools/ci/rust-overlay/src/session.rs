use crate::protocol::{protocol_fixture_directory, SmokeProtocolEvent};
use serde::{Deserialize, Serialize};
use std::fmt;
use std::fs;
use std::path::{Path, PathBuf};

pub const SUPPORTED_PROTOCOL_VERSION: &str = "1.0.0";

pub const HOST_STATE_READY: &str = "ready";
pub const HOST_STATE_RUNNING: &str = "running";
pub const HOST_STATE_REVIEW_REQUIRED: &str = "review-required";
pub const HOST_STATE_IDLE: &str = "idle";
pub const HOST_STATE_PROTOCOL_ERROR: &str = "protocol-error";
pub const HOST_STATE_EXITING: &str = "exiting";

const SESSION_FILE_NAME: &str = "session.json";
const CATALOG_SNAPSHOT_FILE_NAME: &str = "suite-catalog.snapshot.json";
const COMMANDS_DIR_NAME: &str = "commands";
const EVENTS_DIR_NAME: &str = "events";
const EVENTS_FILE_NAME: &str = "events.ndjson";
const HOST_STATE_FILE_NAME: &str = "host-state.json";
const RUNS_DIR_NAME: &str = "runs";
const RESULT_FILE_NAME: &str = "result.json";
const FAILURE_FILE_NAME: &str = "failure.json";
const EVENTS_SLICE_FILE_NAME: &str = "events.slice.ndjson";
const NUNIT_FILE_NAME: &str = "nunit.xml";
const DEBUG_SUMMARY_FILE_NAME: &str = "debug-summary.txt";

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SessionContractError(pub String);

impl fmt::Display for SessionContractError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for SessionContractError {}

impl From<std::io::Error> for SessionContractError {
    fn from(value: std::io::Error) -> Self {
        Self(value.to_string())
    }
}

impl From<serde_json::Error> for SessionContractError {
    fn from(value: serde_json::Error) -> Self {
        Self(value.to_string())
    }
}

#[derive(Debug, Clone)]
pub struct ProtocolCompatibilityResult {
    pub is_compatible: bool,
    pub message: String,
}

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

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeArtifactPaths {
    pub result_path: String,
    #[serde(default)]
    pub failure_path: String,
    pub events_slice_path: String,
    pub nunit_path: String,
    #[serde(default)]
    pub debug_summary_path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeRunResultDocument {
    pub protocol_version: String,
    pub session_id: String,
    pub run_id: String,
    pub suite_id: String,
    pub suite_label: String,
    pub group_id: String,
    pub group_label: String,
    pub result: String,
    pub started_at_utc: String,
    pub ended_at_utc: String,
    pub duration_seconds: f64,
    pub effective_reset_policy: String,
    pub first_event_seq: i32,
    pub last_event_seq: i32,
    pub artifact_paths: SmokeArtifactPaths,
    pub catalog_snapshot_path: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeFailureEventSeqRange {
    pub first: i32,
    pub last: i32,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeFailureDocument {
    pub protocol_version: String,
    pub session_id: String,
    pub run_id: String,
    pub suite_id: String,
    pub suite_label: String,
    pub case_id: String,
    pub case_label: String,
    pub step_id: String,
    pub step_label: String,
    pub failure_message: String,
    pub stack_trace: String,
    pub effective_reset_policy: String,
    pub scene_path: String,
    pub avatar_name: String,
    pub command_id: String,
    pub event_seq_range: SmokeFailureEventSeqRange,
    #[serde(default)]
    pub last_events: Vec<String>,
    pub debug_hint: String,
    pub artifact_paths: SmokeArtifactPaths,
    pub timestamp_utc: String,
}

#[derive(Debug, Clone)]
pub struct SmokeSessionPaths {
    session_root: PathBuf,
}

impl SmokeSessionPaths {
    pub fn new(session_root: impl Into<PathBuf>) -> Result<Self, SessionContractError> {
        let raw = session_root.into();
        if raw.as_os_str().is_empty() {
            return Err(SessionContractError("session_root must not be empty.".to_string()));
        }

        let absolute = if raw.is_absolute() {
            raw
        } else {
            std::env::current_dir()?.join(raw)
        };

        Ok(Self { session_root: absolute })
    }

    pub fn session_root(&self) -> &Path {
        &self.session_root
    }

    pub fn session_metadata_path(&self) -> PathBuf {
        self.session_root.join(SESSION_FILE_NAME)
    }

    pub fn catalog_snapshot_path(&self) -> PathBuf {
        self.session_root.join(CATALOG_SNAPSHOT_FILE_NAME)
    }

    pub fn commands_directory_path(&self) -> PathBuf {
        self.session_root.join(COMMANDS_DIR_NAME)
    }

    pub fn events_directory_path(&self) -> PathBuf {
        self.session_root.join(EVENTS_DIR_NAME)
    }

    pub fn events_log_path(&self) -> PathBuf {
        self.events_directory_path().join(EVENTS_FILE_NAME)
    }

    pub fn host_state_path(&self) -> PathBuf {
        self.session_root.join(HOST_STATE_FILE_NAME)
    }

    pub fn runs_directory_path(&self) -> PathBuf {
        self.session_root.join(RUNS_DIR_NAME)
    }

    pub fn ensure_layout(&self) -> Result<(), SessionContractError> {
        fs::create_dir_all(self.commands_directory_path())?;
        fs::create_dir_all(self.events_directory_path())?;
        fs::create_dir_all(self.runs_directory_path())?;
        Ok(())
    }

    pub fn command_file_name(
        &self,
        command_seq: i32,
        command_type: &str,
        command_id: &str,
    ) -> Result<String, SessionContractError> {
        if command_seq <= 0 {
            return Err(SessionContractError("command_seq must be greater than zero.".to_string()));
        }

        let normalized_type = sanitize_identifier(command_type, "command_type")?;
        let normalized_id = sanitize_identifier(command_id, "command_id")?;
        let name = format!("{command_seq:06}-{normalized_type}-{normalized_id}.json");
        validate_portable_file_name(&name, "command file name")?;
        Ok(name)
    }

    pub fn command_path(
        &self,
        command_seq: i32,
        command_type: &str,
        command_id: &str,
    ) -> Result<PathBuf, SessionContractError> {
        Ok(self
            .commands_directory_path()
            .join(self.command_file_name(command_seq, command_type, command_id)?))
    }

    pub fn run_directory_name(&self, run_ordinal: i32, suite_id: &str) -> Result<String, SessionContractError> {
        if run_ordinal <= 0 {
            return Err(SessionContractError("run_ordinal must be greater than zero.".to_string()));
        }

        let normalized_suite = sanitize_identifier(suite_id, "suite_id")?;
        let name = format!("run-{run_ordinal:04}-{normalized_suite}");
        validate_portable_file_name(&name, "run directory name")?;
        Ok(name)
    }

    pub fn run_directory_path(&self, run_ordinal: i32, suite_id: &str) -> Result<PathBuf, SessionContractError> {
        Ok(self
            .runs_directory_path()
            .join(self.run_directory_name(run_ordinal, suite_id)?))
    }

    pub fn result_path(&self, run_ordinal: i32, suite_id: &str) -> Result<PathBuf, SessionContractError> {
        Ok(self.run_directory_path(run_ordinal, suite_id)?.join(RESULT_FILE_NAME))
    }

    pub fn failure_path(&self, run_ordinal: i32, suite_id: &str) -> Result<PathBuf, SessionContractError> {
        Ok(self.run_directory_path(run_ordinal, suite_id)?.join(FAILURE_FILE_NAME))
    }

    pub fn events_slice_path(&self, run_ordinal: i32, suite_id: &str) -> Result<PathBuf, SessionContractError> {
        Ok(self.run_directory_path(run_ordinal, suite_id)?.join(EVENTS_SLICE_FILE_NAME))
    }

    pub fn nunit_path(&self, run_ordinal: i32, suite_id: &str) -> Result<PathBuf, SessionContractError> {
        Ok(self.run_directory_path(run_ordinal, suite_id)?.join(NUNIT_FILE_NAME))
    }

    pub fn debug_summary_path(&self, run_ordinal: i32, suite_id: &str) -> Result<PathBuf, SessionContractError> {
        Ok(self
            .run_directory_path(run_ordinal, suite_id)?
            .join(DEBUG_SUMMARY_FILE_NAME))
    }

    pub fn resolve_session_relative_path(
        &self,
        relative_path: &str,
        field_name: &str,
    ) -> Result<PathBuf, SessionContractError> {
        let normalized_relative = ensure_relative_artifact_path(relative_path, field_name)?;
        let separator = std::path::MAIN_SEPARATOR.to_string();
        let combined = self
            .session_root
            .join(normalized_relative.replace('/', &separator));
        let full = combined
            .canonicalize()
            .unwrap_or_else(|_| normalize_absolute(&combined));
        let session_root_full = self
            .session_root
            .canonicalize()
            .unwrap_or_else(|_| normalize_absolute(&self.session_root));

        let root_prefix = format!("{}{}", session_root_full.display(), std::path::MAIN_SEPARATOR);
        let full_display = full.display().to_string();
        let root_display = session_root_full.display().to_string();
        if full_display != root_display && !full_display.starts_with(&root_prefix) {
            return Err(SessionContractError(format!(
                "{field_name} must stay under the session root."
            )));
        }

        Ok(full)
    }
}

pub fn load_session_fixture(file_name: &str) -> Result<SmokeSessionDocument, SessionContractError> {
    let raw = fs::read_to_string(protocol_fixture_directory().join(file_name))?;
    load_session_from_str(&raw)
}

pub fn load_session_from_str(raw: &str) -> Result<SmokeSessionDocument, SessionContractError> {
    if raw.trim().is_empty() {
        return Err(SessionContractError("Smoke session JSON is required.".to_string()));
    }

    let mut session: SmokeSessionDocument = serde_json::from_str(raw)?;
    normalize_and_validate_session(&mut session)?;
    Ok(session)
}

pub fn load_host_state_fixture(file_name: &str) -> Result<SmokeHostStateDocument, SessionContractError> {
    let raw = fs::read_to_string(protocol_fixture_directory().join(file_name))?;
    load_host_state_from_str(&raw)
}

pub fn load_host_state_from_str(raw: &str) -> Result<SmokeHostStateDocument, SessionContractError> {
    if raw.trim().is_empty() {
        return Err(SessionContractError("Smoke host-state JSON is required.".to_string()));
    }

    let mut host_state: SmokeHostStateDocument = serde_json::from_str(raw)?;
    normalize_and_validate_host_state(&mut host_state)?;
    Ok(host_state)
}

pub fn evaluate_protocol_compatibility(
    host_supported_protocol_version: &str,
    session: &SmokeSessionDocument,
    catalog_protocol_version: &str,
) -> Result<ProtocolCompatibilityResult, SessionContractError> {
    let host = require_non_blank(host_supported_protocol_version, "hostSupportedProtocolVersion")?;
    let mut normalized_session = session.clone();
    normalize_and_validate_session(&mut normalized_session)?;
    let catalog = require_non_blank(catalog_protocol_version, "catalogProtocolVersion")?;

    if host != normalized_session.protocol_version
        || host != catalog
        || normalized_session.protocol_version != catalog
    {
        return Ok(ProtocolCompatibilityResult {
            is_compatible: false,
            message: format!(
                "Protocol version mismatch: host '{host}', session '{}', catalog '{catalog}'. Update overlay and host to the same protocolVersion before accepting run-suite commands.",
                normalized_session.protocol_version
            ),
        });
    }

    Ok(ProtocolCompatibilityResult {
        is_compatible: true,
        message: "Protocol versions match exactly.".to_string(),
    })
}

pub fn build_protocol_error_host_state(
    session: &SmokeSessionDocument,
    message: &str,
    last_event_seq: i32,
    last_command_seq: i32,
) -> Result<SmokeHostStateDocument, SessionContractError> {
    let mut normalized_session = session.clone();
    normalize_and_validate_session(&mut normalized_session)?;
    if last_event_seq < 0 {
        return Err(SessionContractError(
            "lastEventSeq must be zero or greater.".to_string(),
        ));
    }
    if last_command_seq < 0 {
        return Err(SessionContractError(
            "lastCommandSeq must be zero or greater.".to_string(),
        ));
    }

    let mut state = SmokeHostStateDocument {
        session_id: normalized_session.session_id,
        protocol_version: normalized_session.protocol_version,
        state: HOST_STATE_PROTOCOL_ERROR.to_string(),
        host_version: normalized_session.host_version,
        unity_version: normalized_session.unity_version,
        heartbeat_utc: "2026-01-01T00:00:00Z".to_string(),
        last_event_seq,
        last_command_seq,
        active_run_id: String::new(),
        message: require_non_blank(message, "message")?,
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
    if event_seq <= 0 {
        return Err(SessionContractError("eventSeq must be greater than zero.".to_string()));
    }

    Ok(SmokeProtocolEvent {
        protocol_version: normalized_session.protocol_version,
        session_id: normalized_session.session_id,
        event_id: format!("evt_{event_seq:06}_protocol-error"),
        event_seq,
        event_type: HOST_STATE_PROTOCOL_ERROR.to_string(),
        timestamp_utc: "2026-01-01T00:00:00Z".to_string(),
        command_id: require_non_blank(command_id, "commandId")?,
        run_id: String::new(),
        group_id: String::new(),
        suite_id: String::new(),
        case_id: String::new(),
        step_id: String::new(),
        effective_reset_policy: String::new(),
        host_state: HOST_STATE_PROTOCOL_ERROR.to_string(),
        message: require_non_blank(message, "message")?,
        review_decision_options: Vec::new(),
        supported_capabilities: normalize_capabilities(std::mem::take(&mut normalized_session.capabilities)),
    })
}

pub fn can_accept_run_suite(host_state: &SmokeHostStateDocument) -> (bool, String) {
    let mut normalized = host_state.clone();
    if let Err(error) = normalize_and_validate_host_state(&mut normalized) {
        return (false, error.0);
    }

    if normalized.state == HOST_STATE_PROTOCOL_ERROR {
        return (
            false,
            if normalized.message.trim().is_empty() {
                "run-suite rejected while host remains in protocol-error state.".to_string()
            } else {
                normalized.message
            },
        );
    }

    if normalized.state == HOST_STATE_EXITING {
        return (false, "run-suite rejected while host is exiting.".to_string());
    }

    (true, String::new())
}

pub fn load_result_fixture(file_name: &str) -> Result<SmokeRunResultDocument, SessionContractError> {
    let raw = fs::read_to_string(protocol_fixture_directory().join(file_name))?;
    load_result_from_str(&raw)
}

pub fn load_result_from_str(raw: &str) -> Result<SmokeRunResultDocument, SessionContractError> {
    if raw.trim().is_empty() {
        return Err(SessionContractError("Smoke result JSON is required.".to_string()));
    }

    let mut result: SmokeRunResultDocument = serde_json::from_str(raw)?;
    normalize_and_validate_result_document(&mut result)?;
    Ok(result)
}

pub fn load_failure_fixture(file_name: &str) -> Result<SmokeFailureDocument, SessionContractError> {
    let raw = fs::read_to_string(protocol_fixture_directory().join(file_name))?;
    load_failure_from_str(&raw)
}

pub fn load_failure_from_str(raw: &str) -> Result<SmokeFailureDocument, SessionContractError> {
    if raw.trim().is_empty() {
        return Err(SessionContractError("Smoke failure JSON is required.".to_string()));
    }

    let mut failure: SmokeFailureDocument = serde_json::from_str(raw)?;
    normalize_and_validate_failure_document(&mut failure)?;
    Ok(failure)
}

fn normalize_and_validate_session(session: &mut SmokeSessionDocument) -> Result<(), SessionContractError> {
    session.session_id = require_non_blank(&session.session_id, "sessionId")?;
    session.protocol_version = require_non_blank(&session.protocol_version, "protocolVersion")?;
    if session.catalog_version <= 0 {
        return Err(SessionContractError("catalogVersion must be greater than zero.".to_string()));
    }
    session.catalog_path = require_non_blank(&session.catalog_path, "catalogPath")?;
    session.catalog_snapshot_path = require_non_blank(&session.catalog_snapshot_path, "catalogSnapshotPath")?;
    session.project_path = require_non_blank(&session.project_path, "projectPath")?;
    session.overlay_version = require_non_blank(&session.overlay_version, "overlayVersion")?;
    session.host_version = require_non_blank(&session.host_version, "hostVersion")?;
    session.package_version = require_non_blank(&session.package_version, "packageVersion")?;
    session.unity_version = require_non_blank(&session.unity_version, "unityVersion")?;
    session.global_reset_default = require_non_blank(&session.global_reset_default, "globalResetDefault")?;
    session.created_at_utc = require_non_blank(&session.created_at_utc, "createdAtUtc")?;
    session.updated_at_utc = normalize_optional(&session.updated_at_utc);
    session.capabilities = normalize_capabilities(std::mem::take(&mut session.capabilities));
    Ok(())
}

fn normalize_and_validate_host_state(host_state: &mut SmokeHostStateDocument) -> Result<(), SessionContractError> {
    host_state.session_id = require_non_blank(&host_state.session_id, "sessionId")?;
    host_state.protocol_version = require_non_blank(&host_state.protocol_version, "protocolVersion")?;
    host_state.state = require_non_blank(&host_state.state, "state")?;
    if !is_supported_host_state(&host_state.state) {
        return Err(SessionContractError(format!(
            "state '{}' is not supported.",
            host_state.state
        )));
    }
    host_state.host_version = require_non_blank(&host_state.host_version, "hostVersion")?;
    host_state.unity_version = require_non_blank(&host_state.unity_version, "unityVersion")?;
    host_state.heartbeat_utc = require_non_blank(&host_state.heartbeat_utc, "heartbeatUtc")?;
    if host_state.last_event_seq < 0 {
        return Err(SessionContractError(
            "lastEventSeq must be zero or greater.".to_string(),
        ));
    }
    if host_state.last_command_seq < 0 {
        return Err(SessionContractError(
            "lastCommandSeq must be zero or greater.".to_string(),
        ));
    }
    host_state.active_run_id = normalize_optional(&host_state.active_run_id);
    host_state.message = require_non_blank(&host_state.message, "message")?;
    Ok(())
}

fn normalize_and_validate_result_document(
    result: &mut SmokeRunResultDocument,
) -> Result<(), SessionContractError> {
    result.protocol_version = require_non_blank(&result.protocol_version, "protocolVersion")?;
    result.session_id = require_non_blank(&result.session_id, "sessionId")?;
    result.run_id = require_non_blank(&result.run_id, "runId")?;
    result.suite_id = require_non_blank(&result.suite_id, "suiteId")?;
    result.suite_label = require_non_blank(&result.suite_label, "suiteLabel")?;
    result.group_id = require_non_blank(&result.group_id, "groupId")?;
    result.group_label = require_non_blank(&result.group_label, "groupLabel")?;
    result.result = require_non_blank(&result.result, "result")?;
    result.started_at_utc = require_non_blank(&result.started_at_utc, "startedAtUtc")?;
    result.ended_at_utc = require_non_blank(&result.ended_at_utc, "endedAtUtc")?;
    if result.duration_seconds < 0.0 {
        return Err(SessionContractError(
            "durationSeconds must be zero or greater.".to_string(),
        ));
    }
    result.effective_reset_policy =
        require_non_blank(&result.effective_reset_policy, "effectiveResetPolicy")?;
    if result.first_event_seq <= 0 {
        return Err(SessionContractError(
            "firstEventSeq must be greater than zero.".to_string(),
        ));
    }
    if result.last_event_seq < result.first_event_seq {
        return Err(SessionContractError(
            "lastEventSeq must be greater than or equal to firstEventSeq.".to_string(),
        ));
    }
    result.catalog_snapshot_path =
        ensure_relative_artifact_path(&result.catalog_snapshot_path, "catalogSnapshotPath")?;
    normalize_and_validate_artifact_paths(&mut result.artifact_paths)?;
    Ok(())
}

fn normalize_and_validate_failure_document(
    failure: &mut SmokeFailureDocument,
) -> Result<(), SessionContractError> {
    failure.protocol_version = require_non_blank(&failure.protocol_version, "protocolVersion")?;
    failure.session_id = require_non_blank(&failure.session_id, "sessionId")?;
    failure.run_id = require_non_blank(&failure.run_id, "runId")?;
    failure.suite_id = require_non_blank(&failure.suite_id, "suiteId")?;
    failure.suite_label = require_non_blank(&failure.suite_label, "suiteLabel")?;
    failure.case_id = require_non_blank(&failure.case_id, "caseId")?;
    failure.case_label = require_non_blank(&failure.case_label, "caseLabel")?;
    failure.step_id = require_non_blank(&failure.step_id, "stepId")?;
    failure.step_label = require_non_blank(&failure.step_label, "stepLabel")?;
    failure.failure_message = require_non_blank(&failure.failure_message, "failureMessage")?;
    failure.stack_trace = require_non_blank(&failure.stack_trace, "stackTrace")?;
    failure.effective_reset_policy =
        require_non_blank(&failure.effective_reset_policy, "effectiveResetPolicy")?;
    failure.scene_path = require_non_blank(&failure.scene_path, "scenePath")?;
    failure.avatar_name = require_non_blank(&failure.avatar_name, "avatarName")?;
    failure.command_id = require_non_blank(&failure.command_id, "commandId")?;
    if failure.event_seq_range.first <= 0 {
        return Err(SessionContractError(
            "eventSeqRange.first must be greater than zero.".to_string(),
        ));
    }
    if failure.event_seq_range.last < failure.event_seq_range.first {
        return Err(SessionContractError(
            "eventSeqRange.last must be greater than or equal to eventSeqRange.first.".to_string(),
        ));
    }
    failure.debug_hint = require_non_blank(&failure.debug_hint, "debugHint")?;
    failure.timestamp_utc = require_non_blank(&failure.timestamp_utc, "timestampUtc")?;
    failure.last_events = failure
        .last_events
        .iter()
        .map(|line| line.trim().to_string())
        .filter(|line| !line.is_empty())
        .collect();
    normalize_and_validate_artifact_paths(&mut failure.artifact_paths)?;
    Ok(())
}

fn normalize_and_validate_artifact_paths(
    artifact_paths: &mut SmokeArtifactPaths,
) -> Result<(), SessionContractError> {
    artifact_paths.result_path = ensure_relative_artifact_path(&artifact_paths.result_path, "artifactPaths.resultPath")?;
    artifact_paths.failure_path = normalize_optional_relative_artifact_path(
        &artifact_paths.failure_path,
        "artifactPaths.failurePath",
    )?;
    artifact_paths.events_slice_path =
        ensure_relative_artifact_path(&artifact_paths.events_slice_path, "artifactPaths.eventsSlicePath")?;
    artifact_paths.nunit_path = ensure_relative_artifact_path(&artifact_paths.nunit_path, "artifactPaths.nunitPath")?;
    artifact_paths.debug_summary_path = normalize_optional_relative_artifact_path(
        &artifact_paths.debug_summary_path,
        "artifactPaths.debugSummaryPath",
    )?;
    Ok(())
}

fn ensure_relative_artifact_path(
    relative_path: &str,
    field_name: &str,
) -> Result<String, SessionContractError> {
    let normalized = require_non_blank(relative_path, field_name)?;
    normalize_relative_artifact_path(&normalized, field_name)
}

fn normalize_optional_relative_artifact_path(
    relative_path: &str,
    field_name: &str,
) -> Result<String, SessionContractError> {
    let normalized = normalize_optional(relative_path);
    if normalized.is_empty() {
        return Ok(String::new());
    }

    normalize_relative_artifact_path(&normalized, field_name)
}

fn normalize_relative_artifact_path(
    raw: &str,
    field_name: &str,
) -> Result<String, SessionContractError> {
    let value = raw.replace('\\', "/");
    if value.starts_with('/') || value.starts_with("~/") {
        return Err(SessionContractError(format!(
            "{field_name} must be a session-relative path."
        )));
    }

    if Path::new(&value).is_absolute() {
        return Err(SessionContractError(format!(
            "{field_name} must be a session-relative path."
        )));
    }

    let segments: Vec<&str> = value.split('/').collect();
    if segments.is_empty() {
        return Err(SessionContractError(format!("{field_name} must not be blank.")));
    }

    let mut normalized_segments = Vec::with_capacity(segments.len());
    for segment in segments {
        let trimmed = segment.trim();
        if trimmed.is_empty() || trimmed == "." || trimmed == ".." {
            return Err(SessionContractError(format!(
                "{field_name} contains an invalid relative segment."
            )));
        }

        validate_portable_file_name(trimmed, field_name)?;
        normalized_segments.push(trimmed);
    }

    Ok(normalized_segments.join("/"))
}

fn sanitize_identifier(value: &str, field_name: &str) -> Result<String, SessionContractError> {
    let normalized = require_non_blank(value, field_name)?;
    let mapped: String = normalized
        .chars()
        .map(|ch| {
            if ch.is_ascii_alphanumeric() || matches!(ch, '-' | '_' | '.') {
                ch
            } else {
                '-'
            }
        })
        .collect();
    let collapsed = mapped
        .split('-')
        .filter(|segment| !segment.is_empty())
        .collect::<Vec<_>>()
        .join("-");

    if collapsed.is_empty() {
        return Err(SessionContractError(format!(
            "{field_name} did not contain any portable identifier characters."
        )));
    }

    validate_portable_file_name(&collapsed, field_name)?;
    Ok(collapsed)
}

fn validate_portable_file_name(value: &str, field_name: &str) -> Result<(), SessionContractError> {
    if value.is_empty() {
        return Err(SessionContractError(format!("{field_name} must not be blank.")));
    }

    if !value.is_ascii() {
        return Err(SessionContractError(format!(
            "{field_name} must be ASCII-only for cross-platform safety."
        )));
    }

    if value.ends_with('.') || value.ends_with(' ') {
        return Err(SessionContractError(format!(
            "{field_name} must not end with '.' or space."
        )));
    }

    if value.chars().any(|ch| matches!(ch, '<' | '>' | ':' | '"' | '/' | '\\' | '|' | '?' | '*')) {
        return Err(SessionContractError(format!(
            "{field_name} contains a Windows-reserved character."
        )));
    }

    let upper = value.to_ascii_uppercase();
    let reserved = [
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7",
        "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];
    if reserved.iter().any(|item| *item == upper) {
        return Err(SessionContractError(format!(
            "{field_name} must not use a reserved Windows device name."
        )));
    }

    Ok(())
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

fn normalize_capabilities(values: Vec<String>) -> Vec<String> {
    let mut normalized: Vec<String> = values
        .into_iter()
        .map(|value| value.trim().to_string())
        .filter(|value| !value.is_empty())
        .collect();
    normalized.sort();
    normalized.dedup();
    normalized
}

fn normalize_optional(value: &str) -> String {
    value.trim().to_string()
}

fn require_non_blank(value: &str, field_name: &str) -> Result<String, SessionContractError> {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return Err(SessionContractError(format!("{field_name} must not be blank.")));
    }

    Ok(trimmed.to_string())
}

fn normalize_absolute(path: &Path) -> PathBuf {
    if path.is_absolute() {
        path.to_path_buf()
    } else {
        std::env::current_dir()
            .unwrap_or_else(|_| PathBuf::from("."))
            .join(path)
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;
    use crate::protocol::load_event_fixture;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn compatibility_valid_startup_exact_protocol_match_allows_run_suite_acceptance() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let session = load_session_fixture("session.valid.json").expect("session fixture should parse");

        let compatibility = evaluate_protocol_compatibility(
            SUPPORTED_PROTOCOL_VERSION,
            &session,
            &catalog.protocol_version,
        )
        .expect("compatibility check should run");

        assert!(compatibility.is_compatible, "{}", compatibility.message);

        let host_state =
            load_host_state_fixture("host-state.ready.json").expect("host-state fixture should parse");
        let (accepted, reason) = can_accept_run_suite(&host_state);
        assert!(accepted, "{reason}");
    }

    #[test]
    fn compatibility_mismatch_startup_emits_protocol_error_contract_and_blocks_startup() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mismatch_session =
            load_session_fixture("session.protocol-mismatch.json").expect("mismatch fixture should parse");

        let compatibility = evaluate_protocol_compatibility(
            SUPPORTED_PROTOCOL_VERSION,
            &mismatch_session,
            &catalog.protocol_version,
        )
        .expect("compatibility check should run");

        assert!(!compatibility.is_compatible);
        assert!(compatibility.message.contains("Protocol version mismatch"));
        assert!(compatibility.message.contains("Update overlay and host"));

        let host_state =
            load_host_state_fixture("host-state.protocol-error.json").expect("protocol-error state should parse");
        assert_eq!(host_state.state, HOST_STATE_PROTOCOL_ERROR);

        let events =
            load_event_fixture("events.protocol-error.ndjson").expect("protocol-error events should parse");
        assert!(events
            .iter()
            .any(|item| item.event_type == HOST_STATE_PROTOCOL_ERROR));
        assert!(events.iter().any(|item| item.event_type == "command-rejected"));
    }

    #[test]
    fn compatibility_post_mismatch_run_suite_is_rejected_while_host_state_is_protocol_error() {
        let host_state =
            load_host_state_fixture("host-state.protocol-error.json").expect("protocol-error state should parse");
        let (accepted, reason) = can_accept_run_suite(&host_state);
        assert!(!accepted);
        assert!(reason.to_lowercase().contains("protocol"));
    }

    #[test]
    fn artifacts_session_layout_and_paths_follow_canonical_contract() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root()).expect("session root should initialize");
        session_paths.ensure_layout().expect("layout should initialize");

        assert!(session_paths.commands_directory_path().is_dir());
        assert!(session_paths.events_directory_path().is_dir());
        assert!(session_paths.runs_directory_path().is_dir());

        assert_eq!(
            session_paths
                .session_metadata_path()
                .file_name()
                .and_then(|item| item.to_str())
                .unwrap_or_default(),
            SESSION_FILE_NAME
        );
        assert_eq!(
            session_paths
                .events_log_path()
                .file_name()
                .and_then(|item| item.to_str())
                .unwrap_or_default(),
            EVENTS_FILE_NAME
        );
    }

    #[test]
    fn artifacts_naming_is_sortable_ascii_and_windows_safe() {
        let session_paths = SmokeSessionPaths::new(make_temp_session_root()).expect("session root should initialize");
        let command_a = session_paths
            .command_file_name(2, "run-suite", "cmd_000002_run-suite")
            .expect("command file should build");
        let command_b = session_paths
            .command_file_name(11, "run-suite", "cmd_000011_run-suite")
            .expect("command file should build");

        let run_a = session_paths
            .run_directory_name(2, "lifecycle-roundtrip")
            .expect("run dir should build");
        let run_b = session_paths
            .run_directory_name(11, "playmode-runtime-validation")
            .expect("run dir should build");

        assert!(command_a < command_b);
        assert!(run_a < run_b);
        validate_portable_file_name(&command_a, "command_a").expect("command_a should be portable");
        validate_portable_file_name(&command_b, "command_b").expect("command_b should be portable");
        validate_portable_file_name(&run_a, "run_a").expect("run_a should be portable");
        validate_portable_file_name(&run_b, "run_b").expect("run_b should be portable");
    }

    #[test]
    fn artifacts_fixtures_use_relative_paths_and_slice_stays_under_session_root() {
        let result = load_result_fixture("result.sample.json").expect("result fixture should parse");
        let failure = load_failure_fixture("failure.sample.json").expect("failure fixture should parse");
        let slice_events =
            load_event_fixture("events.slice.sample.ndjson").expect("events slice fixture should parse");

        assert!(!slice_events.is_empty());
        assert_eq!(slice_events[0].event_seq, 12);

        let session_paths = SmokeSessionPaths::new(make_temp_session_root()).expect("session root should initialize");
        session_paths.ensure_layout().expect("layout should initialize");

        let result_slice_path = session_paths
            .resolve_session_relative_path(&result.artifact_paths.events_slice_path, "artifactPaths.eventsSlicePath")
            .expect("result slice path should resolve");
        let failure_slice_path = session_paths
            .resolve_session_relative_path(&failure.artifact_paths.events_slice_path, "artifactPaths.eventsSlicePath")
            .expect("failure slice path should resolve");

        let session_root = normalize_absolute(session_paths.session_root());
        let root_prefix = format!("{}{}", session_root.display(), std::path::MAIN_SEPARATOR);
        assert!(result_slice_path.display().to_string().starts_with(&root_prefix));
        assert!(failure_slice_path.display().to_string().starts_with(&root_prefix));
    }

    fn make_temp_session_root() -> PathBuf {
        let nanos = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("clock should be monotonic")
            .as_nanos();
        std::env::temp_dir().join(format!("asmlite-smoke-session-{nanos}"))
    }
}
