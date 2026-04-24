use crate::catalog::{SmokeGroupDefinition, SmokeSuiteCatalog, SmokeSuiteDefinition};
use crate::unity_launcher::UnityHostSupervisorStatus;
use std::path::PathBuf;

pub const DEFAULT_STARTUP_TIMEOUT_SECONDS: u64 = 120;
pub const DEFAULT_HEARTBEAT_SECONDS: u64 = 5;
pub const DEFAULT_STALE_AFTER_SECONDS: u64 = 15;
pub const DEFAULT_POLL_INTERVAL_MILLIS: u64 = 250;
pub const PHASE07_RUN_GATE_MESSAGE: &str =
    "Suite execution is enabled in Phase 08; run-suite dispatch is disabled in Phase 07.";

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
pub struct SuiteSelectionModel {
    pub catalog: SmokeSuiteCatalog,
    pub selected_suite_id: String,
    pub global_reset_default: GlobalResetDefault,
}

impl SuiteSelectionModel {
    pub fn new_from_catalog(catalog: &SmokeSuiteCatalog) -> Result<Self, String> {
        let first_group = catalog
            .groups
            .first()
            .ok_or_else(|| "suite catalog requires at least one group".to_string())?;
        let first_suite = first_group
            .suites
            .first()
            .ok_or_else(|| "suite catalog requires at least one suite in the first group".to_string())?;

        Ok(Self {
            catalog: catalog.clone(),
            selected_suite_id: first_suite.suite_id.clone(),
            global_reset_default: GlobalResetDefault::SceneReload,
        })
    }

    pub fn select_suite_by_id(&mut self, suite_id: &str) -> Result<(), String> {
        let normalized = suite_id.trim();
        if normalized.is_empty() {
            return Err("suite_id must not be blank".to_string());
        }

        if self.catalog.suite_index_by_id.contains_key(normalized) {
            self.selected_suite_id = normalized.to_string();
            Ok(())
        } else {
            Err(format!("unknown suite_id '{normalized}'"))
        }
    }

    pub fn selected_suite(&self) -> Option<&SmokeSuiteDefinition> {
        let (group_index, suite_index) = self
            .catalog
            .suite_index_by_id
            .get(&self.selected_suite_id)
            .copied()?;
        self.catalog.groups.get(group_index)?.suites.get(suite_index)
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
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;

    #[test]
    fn single_selection_invariant_is_maintained() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model = SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        assert_eq!(model.selected_suite_id, "open-select-add");
        model
            .select_suite_by_id("lifecycle-roundtrip")
            .expect("suite id should be selectable");
        assert_eq!(model.selected_suite_id, "lifecycle-roundtrip");
        assert_eq!(model.selected_suite().expect("suite should resolve").suite_id, "lifecycle-roundtrip");
    }

    #[test]
    fn list_detail_sync_holds_across_selection_changes() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model = SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

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
        let mut model = SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        model.set_global_reset_default(GlobalResetDefault::FullPackageRebuild);
        model
            .select_suite_by_id("lifecycle-roundtrip")
            .expect("suite id should select");
        model
            .select_suite_by_id("open-select-add")
            .expect("suite id should select");

        assert_eq!(model.global_reset_default, GlobalResetDefault::FullPackageRebuild);
    }
}
