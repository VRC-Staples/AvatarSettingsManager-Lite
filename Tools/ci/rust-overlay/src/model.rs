use crate::catalog::{SmokeGroupDefinition, SmokeSuiteCatalog, SmokeSuiteDefinition};
use crate::protocol::{
    REVIEW_DECISION_EXIT, REVIEW_DECISION_RERUN_SUITE, REVIEW_DECISION_RETURN_TO_SUITE_LIST,
};
use crate::unity_launcher::UnityHostSupervisorStatus;
use std::path::PathBuf;

pub const DEFAULT_STARTUP_TIMEOUT_SECONDS: u64 = 120;
pub const DEFAULT_HEARTBEAT_SECONDS: u64 = 5;
pub const DEFAULT_STALE_AFTER_SECONDS: u64 = 15;
pub const DEFAULT_POLL_INTERVAL_MILLIS: u64 = 250;
pub const DEFAULT_STEP_SLEEP_SECONDS: f64 = 1.5;
pub const RUN_DISPATCH_READY_MESSAGE: &str =
    "Selecting a suite queues a run-suite command in session/commands for Unity host execution.";
pub const DESTRUCTIVE_DISABLED_REASON: &str = "Enable destructive drills to select this suite.";

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
    ReviewRequired,
    HostError,
}

impl AppState {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::Boot => "boot",
            Self::LaunchingUnity => "launching-unity",
            Self::WaitingForReady => "waiting-for-ready",
            Self::SuiteSelect => "suite-select",
            Self::ReviewRequired => "review-required",
            Self::HostError => "host-error",
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum GlobalResetDefault {
    SceneReload,
    FullPackageRebuild,
}

impl GlobalResetDefault {
    pub fn as_str(&self) -> &'static str {
        match self {
            Self::SceneReload => "SceneReload",
            Self::FullPackageRebuild => "FullPackageRebuild",
        }
    }
}

#[derive(Debug, Clone)]
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
    pub selected_suite_ids: Vec<String>,
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

#[derive(Debug, Clone)]
pub struct StepSleepTimerModel {
    pub enabled: bool,
    pub seconds: f64,
}

impl Default for StepSleepTimerModel {
    fn default() -> Self {
        Self {
            enabled: false,
            seconds: DEFAULT_STEP_SLEEP_SECONDS,
        }
    }
}

#[derive(Debug, Clone)]
pub struct SuiteSelectionModel {
    pub catalog: SmokeSuiteCatalog,
    pub selected_suite_id: String,
    selected_suite_ids: Vec<String>,
    pub global_reset_default: GlobalResetDefault,
    pub step_sleep_timer: StepSleepTimerModel,
    pub destructive_suites_enabled: bool,
}

impl SuiteSelectionModel {
    pub fn new_from_catalog(catalog: &SmokeSuiteCatalog) -> Result<Self, String> {
        let first_group = catalog
            .groups
            .first()
            .ok_or_else(|| "suite catalog requires at least one group".to_string())?;
        let first_suite = first_group.suites.first().ok_or_else(|| {
            "suite catalog requires at least one suite in the first group".to_string()
        })?;
        let mut selected_suite_ids: Vec<String> = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| suite.default_selected && !suite.is_destructive())
            .map(|suite| suite.suite_id.clone())
            .collect();
        if selected_suite_ids.is_empty() {
            selected_suite_ids.push(first_suite.suite_id.clone());
        }
        let selected_suite_id = selected_suite_ids
            .first()
            .cloned()
            .unwrap_or_else(|| first_suite.suite_id.clone());

        Ok(Self {
            catalog: catalog.clone(),
            selected_suite_id,
            selected_suite_ids,
            global_reset_default: GlobalResetDefault::SceneReload,
            step_sleep_timer: StepSleepTimerModel::default(),
            destructive_suites_enabled: false,
        })
    }

    pub fn select_suite_by_id(&mut self, suite_id: &str) -> Result<(), String> {
        let normalized = self.require_selectable_suite_id(suite_id)?;
        self.selected_suite_id = normalized.to_string();
        self.selected_suite_ids = vec![normalized.to_string()];
        Ok(())
    }

    pub fn select_suite_batch_by_ids(&mut self, suite_ids: &[String]) -> Result<(), String> {
        let mut normalized_ids = Vec::new();
        for suite_id in suite_ids {
            let normalized = self.require_selectable_suite_id(suite_id)?;
            normalized_ids.push(normalized.to_string());
        }
        if normalized_ids.is_empty() {
            return Err("selected batch requires at least one suite".to_string());
        }
        self.selected_suite_ids = normalized_ids;
        self.sync_selected_suite_head();
        Ok(())
    }

    pub fn toggle_suite_selection_by_id(&mut self, suite_id: &str) -> Result<(), String> {
        let normalized = self.require_selectable_suite_id(suite_id)?;
        if let Some(index) = self
            .selected_suite_ids
            .iter()
            .position(|selected| selected == normalized)
        {
            self.selected_suite_ids.remove(index);
        } else {
            self.selected_suite_ids.push(normalized.to_string());
        }
        self.sync_selected_suite_head();
        Ok(())
    }

    pub fn clear_suite_selection(&mut self) {
        self.selected_suite_ids.clear();
        self.selected_suite_id.clear();
    }

    pub fn apply_preset_group(&mut self, preset_group: &str) -> Result<(), String> {
        let normalized = preset_group.trim();
        if normalized.is_empty() {
            return Err("preset_group must not be blank".to_string());
        }

        let suite_ids: Vec<String> = self
            .catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| suite.preset_groups.iter().any(|group| group == normalized))
            .filter(|suite| self.destructive_suites_enabled || !suite.is_destructive())
            .map(|suite| suite.suite_id.clone())
            .collect();

        if suite_ids.is_empty() {
            return Err(format!(
                "preset_group '{normalized}' has no selectable suites"
            ));
        }

        self.selected_suite_ids = suite_ids;
        self.sync_selected_suite_head();
        Ok(())
    }

    pub fn selected_suite_ids(&self) -> Vec<&str> {
        self.selected_suite_ids.iter().map(String::as_str).collect()
    }

    pub fn available_suite_ids(&self) -> Vec<&str> {
        self.catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .map(|suite| suite.suite_id.as_str())
            .collect()
    }

    pub fn can_run_selected_suite(&self) -> bool {
        self.selected_suite().is_some()
    }

    pub fn selected_order_for_suite_id(&self, suite_id: &str) -> Option<usize> {
        let normalized = suite_id.trim();
        self.selected_suite_ids
            .iter()
            .position(|selected| selected == normalized)
            .map(|index| index + 1)
    }

    pub fn is_suite_selected(&self, suite_id: &str) -> bool {
        let normalized = suite_id.trim();
        self.selected_suite_ids
            .iter()
            .any(|selected| selected == normalized)
    }

    pub fn set_current_suite_id_preserving_batch(&mut self, suite_id: &str) -> Result<(), String> {
        let normalized = self.require_known_suite_id(suite_id)?;
        if !self.is_suite_selected(normalized) {
            return Err(format!(
                "suite_id '{normalized}' is not in the selected batch"
            ));
        }
        self.selected_suite_id = normalized.to_string();
        Ok(())
    }

    pub fn reset_current_suite_to_selected_head(&mut self) {
        self.sync_selected_suite_head();
    }

    fn require_known_suite_id<'a>(&self, suite_id: &'a str) -> Result<&'a str, String> {
        let normalized = suite_id.trim();
        if normalized.is_empty() {
            return Err("suite_id must not be blank".to_string());
        }

        if self.catalog.suite_index_by_id.contains_key(normalized) {
            Ok(normalized)
        } else {
            Err(format!("unknown suite_id '{normalized}'"))
        }
    }

    fn require_selectable_suite_id<'a>(&self, suite_id: &'a str) -> Result<&'a str, String> {
        let normalized = self.require_known_suite_id(suite_id)?;
        if !self.is_suite_selectable(normalized) {
            return Err(DESTRUCTIVE_DISABLED_REASON.to_string());
        }
        Ok(normalized)
    }

    pub fn is_suite_selectable(&self, suite_id: &str) -> bool {
        let normalized = suite_id.trim();
        let Some(suite) = self.suite_by_id(normalized) else {
            return false;
        };
        self.destructive_suites_enabled || !suite.is_destructive()
    }

    pub fn disabled_reason_for_suite_id(&self, suite_id: &str) -> Option<&'static str> {
        if self.is_suite_selectable(suite_id) {
            None
        } else if self.suite_by_id(suite_id.trim()).is_some() {
            Some(DESTRUCTIVE_DISABLED_REASON)
        } else {
            None
        }
    }

    fn suite_by_id(&self, suite_id: &str) -> Option<&SmokeSuiteDefinition> {
        let (group_index, suite_index) = self.catalog.suite_index_by_id.get(suite_id).copied()?;
        self.catalog
            .groups
            .get(group_index)?
            .suites
            .get(suite_index)
    }

    fn remove_disabled_destructive_selections(&mut self) {
        if self.destructive_suites_enabled {
            return;
        }
        let catalog = self.catalog.clone();
        self.selected_suite_ids.retain(|suite_id| {
            let Some((group_index, suite_index)) = catalog.suite_index_by_id.get(suite_id).copied()
            else {
                return false;
            };
            let Some(suite) = catalog
                .groups
                .get(group_index)
                .and_then(|group| group.suites.get(suite_index))
            else {
                return false;
            };
            !suite.is_destructive()
        });
        self.sync_selected_suite_head();
    }

    fn sync_selected_suite_head(&mut self) {
        self.selected_suite_id = self
            .selected_suite_ids
            .first()
            .cloned()
            .unwrap_or_else(String::new);
    }

    pub fn selected_suite(&self) -> Option<&SmokeSuiteDefinition> {
        let (group_index, suite_index) = self
            .catalog
            .suite_index_by_id
            .get(&self.selected_suite_id)
            .copied()?;
        self.catalog
            .groups
            .get(group_index)?
            .suites
            .get(suite_index)
    }

    pub fn selected_group(&self) -> Option<&SmokeGroupDefinition> {
        let (group_index, _) = self
            .catalog
            .suite_index_by_id
            .get(&self.selected_suite_id)
            .copied()?;
        self.catalog.groups.get(group_index)
    }

    pub fn set_global_reset_default(&mut self, value: GlobalResetDefault) {
        self.global_reset_default = value;
    }

    pub fn set_destructive_suites_enabled(&mut self, enabled: bool) {
        self.destructive_suites_enabled = enabled;
        self.remove_disabled_destructive_selections();
    }

    pub fn set_step_sleep_enabled(&mut self, enabled: bool) {
        self.step_sleep_timer.enabled = enabled;
    }

    pub fn set_step_sleep_seconds(&mut self, seconds: f64) {
        self.step_sleep_timer.seconds = if seconds.is_finite() {
            seconds.max(0.0)
        } else {
            DEFAULT_STEP_SLEEP_SECONDS
        };
    }

    pub fn step_sleep_seconds_for_run(&self) -> Option<f64> {
        if self.step_sleep_timer.enabled {
            Some(self.step_sleep_timer.seconds.max(0.0))
        } else {
            None
        }
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BatchStopReason {
    ReviewRequired,
    Aborted,
    HostError,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum BatchRunStatus {
    Running,
    Complete,
    Stopped(BatchStopReason),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchRunTransition {
    pub next_suite_id: Option<String>,
    pub batch_complete: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchRunQueue {
    selected_suite_ids: Vec<String>,
    completed_suite_ids: Vec<String>,
    current_index: usize,
    failed_suite_id: Option<String>,
    status: BatchRunStatus,
}

impl BatchRunQueue {
    pub fn new_from_selection(model: &SuiteSelectionModel) -> Result<Self, String> {
        let selected_suite_ids: Vec<String> = model
            .selected_suite_ids()
            .into_iter()
            .map(str::to_string)
            .collect();
        if selected_suite_ids.is_empty() {
            return Err("selected batch requires at least one suite".to_string());
        }
        Ok(Self {
            selected_suite_ids,
            completed_suite_ids: Vec::new(),
            current_index: 0,
            failed_suite_id: None,
            status: BatchRunStatus::Running,
        })
    }

    pub fn current_suite_id(&self) -> Option<&str> {
        self.selected_suite_ids
            .get(self.current_index)
            .map(String::as_str)
    }

    pub fn selected_suite_ids(&self) -> Vec<&str> {
        self.selected_suite_ids.iter().map(String::as_str).collect()
    }

    pub fn pending_suite_ids(&self) -> Vec<&str> {
        self.selected_suite_ids
            .iter()
            .skip(self.current_index + 1)
            .map(String::as_str)
            .collect()
    }

    pub fn completed_suite_ids(&self) -> Vec<&str> {
        self.completed_suite_ids
            .iter()
            .map(String::as_str)
            .collect()
    }

    pub fn failed_suite_id(&self) -> Option<&str> {
        self.failed_suite_id.as_deref()
    }

    pub fn is_running(&self) -> bool {
        self.status == BatchRunStatus::Running
    }

    pub fn is_stopped(&self) -> bool {
        matches!(self.status, BatchRunStatus::Stopped(_))
    }

    pub fn record_current_passed(&mut self) -> BatchRunTransition {
        if !self.is_running() {
            return BatchRunTransition {
                next_suite_id: None,
                batch_complete: self.status == BatchRunStatus::Complete,
            };
        }

        if let Some(current) = self.current_suite_id().map(str::to_string) {
            if !self.completed_suite_ids.iter().any(|item| item == &current) {
                self.completed_suite_ids.push(current);
            }
        }

        if self.current_index + 1 < self.selected_suite_ids.len() {
            self.current_index += 1;
            BatchRunTransition {
                next_suite_id: self.current_suite_id().map(str::to_string),
                batch_complete: false,
            }
        } else {
            self.current_index = 0;
            self.status = BatchRunStatus::Complete;
            BatchRunTransition {
                next_suite_id: None,
                batch_complete: true,
            }
        }
    }

    pub fn record_current_stopped(&mut self, reason: BatchStopReason) {
        self.failed_suite_id = self.current_suite_id().map(str::to_string);
        self.status = BatchRunStatus::Stopped(reason);
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReviewFailureExcerpt {
    pub step_label: String,
    pub failure_message: String,
    pub context_line: Option<String>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ReviewSummaryModel {
    pub run_id: String,
    pub suite_id: String,
    pub suite_label: String,
    pub run_result: String,
    pub failure_excerpt: Option<ReviewFailureExcerpt>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ReviewAction {
    ReturnToSuiteList,
    RerunSuite,
    Exit,
}

impl ReviewAction {
    pub fn label(&self) -> &'static str {
        match self {
            Self::ReturnToSuiteList => "Return to Suite List",
            Self::RerunSuite => "Rerun Suite",
            Self::Exit => "Exit",
        }
    }

    pub fn decision_token(&self) -> &'static str {
        match self {
            Self::ReturnToSuiteList => REVIEW_DECISION_RETURN_TO_SUITE_LIST,
            Self::RerunSuite => REVIEW_DECISION_RERUN_SUITE,
            Self::Exit => REVIEW_DECISION_EXIT,
        }
    }
}

pub fn review_actions() -> [ReviewAction; 3] {
    [
        ReviewAction::ReturnToSuiteList,
        ReviewAction::RerunSuite,
        ReviewAction::Exit,
    ]
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::{load_canonical_catalog, load_catalog_from_str};

    fn load_destructive_gate_catalog() -> SmokeSuiteCatalog {
        load_catalog_from_str(
            r#"{
  "catalogVersion": 1,
  "protocolVersion": "asmlite-smoke-v1",
  "fixture": {
    "scenePath": "Assets/Scenes/Click ME.unity",
    "avatarName": "Oct25_Dress"
  },
  "groups": [
    {
      "groupId": "setup",
      "label": "Setup",
      "description": "Setup suites",
      "suites": [
        {
          "suiteId": "safe-default",
          "label": "Safe default",
          "description": "Safe setup suite",
          "resetOverride": "Inherit",
          "speed": "quick",
          "risk": "safe",
          "defaultSelected": true,
          "presetGroups": ["quick-default", "all-setup"],
          "requiresPlayMode": false,
          "stopOnFirstFailure": true,
          "expectedOutcome": "safe outcome",
          "debugHint": "safe hint",
          "cases": [
            {
              "caseId": "safe-case",
              "label": "Safe case",
              "description": "Safe case",
              "expectedOutcome": "safe outcome",
              "debugHint": "safe hint",
              "steps": [
                {
                  "stepId": "safe-step",
                  "label": "Safe step",
                  "description": "Safe step",
                  "actionType": "open-scene",
                  "expectedOutcome": "safe outcome",
                  "debugHint": "safe hint"
                }
              ]
            }
          ]
        },
        {
          "suiteId": "destructive-drill",
          "label": "Destructive drill",
          "description": "Destructive setup suite",
          "resetOverride": "FullPackageRebuild",
          "speed": "destructive",
          "risk": "destructive",
          "defaultSelected": true,
          "presetGroups": ["all-setup", "destructive-drills"],
          "requiresPlayMode": false,
          "stopOnFirstFailure": true,
          "expectedOutcome": "destructive outcome",
          "debugHint": "destructive hint",
          "cases": [
            {
              "caseId": "destructive-case",
              "label": "Destructive case",
              "description": "Destructive case",
              "expectedOutcome": "destructive outcome",
              "debugHint": "destructive hint",
              "steps": [
                {
                  "stepId": "destructive-step",
                  "label": "Destructive step",
                  "description": "Destructive step",
                  "actionType": "rebuild",
                  "expectedOutcome": "destructive outcome",
                  "debugHint": "destructive hint"
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}"#,
        )
        .expect("destructive gate catalog should load")
    }

    #[test]
    fn step_sleep_timer_defaults_off_with_one_point_five_seconds_ready_when_enabled() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        assert!(!model.step_sleep_timer.enabled);
        assert_eq!(model.step_sleep_timer.seconds, 1.5);
        assert_eq!(model.step_sleep_seconds_for_run(), None);

        let mut enabled = model.clone();
        enabled.set_step_sleep_enabled(true);
        assert_eq!(enabled.step_sleep_seconds_for_run(), Some(1.5));
    }

    #[test]
    fn step_sleep_timer_clamps_negative_edits_to_zero() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        model.set_step_sleep_enabled(true);
        model.set_step_sleep_seconds(-2.0);
        assert_eq!(model.step_sleep_seconds_for_run(), Some(0.0));
    }

    #[test]
    fn single_selection_invariant_is_maintained() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        assert_eq!(model.selected_suite_id, "asm-lite-readiness-check");
        model
            .select_suite_by_id("lifecycle-roundtrip")
            .expect("suite id should be selectable");
        assert_eq!(model.selected_suite_id, "lifecycle-roundtrip");
        assert_eq!(
            model
                .selected_suite()
                .expect("suite should resolve")
                .suite_id,
            "lifecycle-roundtrip"
        );
    }

    #[test]
    fn list_detail_sync_holds_across_selection_changes() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        model
            .select_suite_by_id("playmode-runtime-validation")
            .expect("playmode suite should select");

        let group = model.selected_group().expect("group should resolve");
        let suite = model.selected_suite().expect("suite should resolve");
        assert_eq!(group.group_id, "playmode-runtime");
        assert_eq!(suite.suite_id, "playmode-runtime-validation");
    }

    #[test]
    fn global_reset_default_persists_while_switching_suites() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        model.set_global_reset_default(GlobalResetDefault::FullPackageRebuild);
        model
            .select_suite_by_id("lifecycle-roundtrip")
            .expect("suite id should select");
        model
            .select_suite_by_id("setup-scene-avatar")
            .expect("suite id should select");

        assert_eq!(
            model.global_reset_default,
            GlobalResetDefault::FullPackageRebuild
        );
    }

    #[test]
    fn suite_selection_defaults_to_metadata_selected_suites() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let expected_default_ids: Vec<&str> = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| suite.default_selected)
            .map(|suite| suite.suite_id.as_str())
            .collect();
        assert_eq!(model.selected_suite_ids(), expected_default_ids);
        assert_eq!(model.selected_suite_id, "asm-lite-readiness-check");
        assert!(model.can_run_selected_suite());
    }

    #[test]
    fn destructive_suites_are_gated_until_explicit_toggle() {
        let catalog = load_destructive_gate_catalog();
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        assert_eq!(model.selected_suite_ids(), vec!["safe-default"]);
        assert!(!model.destructive_suites_enabled);
        assert!(!model.is_suite_selectable("destructive-drill"));
        assert_eq!(
            model.disabled_reason_for_suite_id("destructive-drill"),
            Some(DESTRUCTIVE_DISABLED_REASON)
        );
        assert!(model
            .toggle_suite_selection_by_id("destructive-drill")
            .is_err());

        model.set_destructive_suites_enabled(true);
        model
            .toggle_suite_selection_by_id("destructive-drill")
            .expect("explicit toggle should unlock destructive selection");
        assert_eq!(
            model.selected_suite_ids(),
            vec!["safe-default", "destructive-drill"]
        );

        model.set_destructive_suites_enabled(false);
        assert_eq!(model.selected_suite_ids(), vec!["safe-default"]);
    }

    #[test]
    fn destructive_presets_do_not_auto_enable_destructive_suites() {
        let catalog = load_destructive_gate_catalog();
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();

        model
            .apply_preset_group("destructive-drills")
            .expect_err("destructive preset should remain disabled");
        assert_eq!(model.selected_suite_ids(), Vec::<&str>::new());

        model.set_destructive_suites_enabled(true);
        model
            .apply_preset_group("destructive-drills")
            .expect("destructive preset should apply after explicit toggle");
        assert_eq!(model.selected_suite_ids(), vec!["destructive-drill"]);
    }

    #[test]
    fn preset_group_replaces_selection_without_appending_to_prior_choices() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("playmode-runtime-validation")
            .expect("playmode should toggle on");

        model
            .apply_preset_group("all-setup")
            .expect("all setup preset should apply");

        let expected_all_setup_ids: Vec<&str> = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| {
                suite
                    .preset_groups
                    .iter()
                    .any(|preset| preset == "all-setup")
            })
            .filter(|suite| !suite.is_destructive())
            .map(|suite| suite.suite_id.as_str())
            .collect();
        assert_eq!(model.selected_suite_ids(), expected_all_setup_ids);
        assert_eq!(model.selected_suite_id, "setup-scene-avatar");
    }

    #[test]
    fn suite_selection_toggle_order_tracks_operator_sequence() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        model.clear_suite_selection();
        assert!(!model.can_run_selected_suite());

        model
            .toggle_suite_selection_by_id("playmode-runtime-validation")
            .expect("playmode should toggle on");
        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle on after playmode");
        model
            .toggle_suite_selection_by_id("lifecycle-roundtrip")
            .expect("lifecycle should toggle on after setup");

        assert_eq!(
            model.selected_suite_ids(),
            vec![
                "playmode-runtime-validation",
                "setup-scene-avatar",
                "lifecycle-roundtrip"
            ]
        );

        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle off");
        assert_eq!(
            model.selected_suite_ids(),
            vec!["playmode-runtime-validation", "lifecycle-roundtrip"]
        );
    }

    #[test]
    fn suite_selection_available_ids_are_exact_catalog_universe_and_reject_unknown() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let expected_available_ids: Vec<&str> = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .map(|suite| suite.suite_id.as_str())
            .collect();
        assert_eq!(model.available_suite_ids(), expected_available_ids);
        assert!(model.toggle_suite_selection_by_id("synthetic-all").is_err());
    }

    #[test]
    fn suite_selection_batch_seed_preserves_operator_order_and_head() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let requested = vec![
            "lifecycle-roundtrip".to_string(),
            "setup-scene-avatar".to_string(),
        ];

        model
            .select_suite_batch_by_ids(&requested)
            .expect("valid batch should seed selection");

        assert_eq!(
            model.selected_suite_ids(),
            vec!["lifecycle-roundtrip", "setup-scene-avatar"]
        );
        assert_eq!(model.selected_suite_id, "lifecycle-roundtrip");
    }

    #[test]
    fn batch_queue_head_is_first_selected_suite() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("playmode-runtime-validation")
            .expect("playmode should toggle on first");
        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle on second");

        let queue = BatchRunQueue::new_from_selection(&model).expect("batch should start");

        assert_eq!(
            queue.current_suite_id(),
            Some("playmode-runtime-validation")
        );
        assert_eq!(queue.pending_suite_ids(), vec!["setup-scene-avatar"]);
        assert_eq!(queue.completed_suite_ids(), Vec::<&str>::new());
    }

    #[test]
    fn batch_queue_pass_advances_to_next_pending_suite() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle on");
        model
            .toggle_suite_selection_by_id("lifecycle-roundtrip")
            .expect("lifecycle should toggle on");
        let mut queue = BatchRunQueue::new_from_selection(&model).expect("batch should start");

        let transition = queue.record_current_passed();

        assert_eq!(
            transition.next_suite_id.as_deref(),
            Some("lifecycle-roundtrip")
        );
        assert!(!transition.batch_complete);
        assert_eq!(queue.current_suite_id(), Some("lifecycle-roundtrip"));
        assert_eq!(queue.completed_suite_ids(), vec!["setup-scene-avatar"]);
    }

    #[test]
    fn batch_queue_final_pass_keeps_selection_and_resets_head_to_top() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle on");
        model
            .toggle_suite_selection_by_id("lifecycle-roundtrip")
            .expect("lifecycle should toggle on");
        let mut queue = BatchRunQueue::new_from_selection(&model).expect("batch should start");

        assert!(!queue.record_current_passed().batch_complete);
        let transition = queue.record_current_passed();

        assert!(transition.batch_complete);
        assert_eq!(transition.next_suite_id, None);
        assert_eq!(queue.current_suite_id(), Some("setup-scene-avatar"));
        assert_eq!(
            queue.selected_suite_ids(),
            vec!["setup-scene-avatar", "lifecycle-roundtrip"]
        );
    }

    #[test]
    fn batch_queue_review_required_stops_on_current_suite() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle on");
        model
            .toggle_suite_selection_by_id("lifecycle-roundtrip")
            .expect("lifecycle should toggle on");
        let mut queue = BatchRunQueue::new_from_selection(&model).expect("batch should start");

        queue.record_current_stopped(BatchStopReason::ReviewRequired);

        assert!(queue.is_stopped());
        assert_eq!(queue.failed_suite_id(), Some("setup-scene-avatar"));
        assert_eq!(queue.current_suite_id(), Some("setup-scene-avatar"));
        assert_eq!(queue.pending_suite_ids(), vec!["lifecycle-roundtrip"]);
    }

    #[test]
    fn review_actions_are_exact_and_token_stable() {
        let actions = review_actions();
        assert_eq!(actions.len(), 3);
        assert_eq!(actions[0].label(), "Return to Suite List");
        assert_eq!(
            actions[0].decision_token(),
            REVIEW_DECISION_RETURN_TO_SUITE_LIST
        );
        assert_eq!(actions[1].label(), "Rerun Suite");
        assert_eq!(actions[1].decision_token(), REVIEW_DECISION_RERUN_SUITE);
        assert_eq!(actions[2].label(), "Exit");
        assert_eq!(actions[2].decision_token(), REVIEW_DECISION_EXIT);
    }
}
