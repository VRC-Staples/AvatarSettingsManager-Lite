use serde::{Deserialize, Serialize};
use std::collections::{BTreeMap, HashSet};
use std::fmt;
use std::fs;
use std::path::PathBuf;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ContractError(pub String);

impl fmt::Display for ContractError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        f.write_str(&self.0)
    }
}

impl std::error::Error for ContractError {}

impl From<std::io::Error> for ContractError {
    fn from(value: std::io::Error) -> Self {
        Self(value.to_string())
    }
}

impl From<serde_json::Error> for ContractError {
    fn from(value: serde_json::Error) -> Self {
        Self(value.to_string())
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeSuiteCatalog {
    pub catalog_version: i32,
    pub protocol_version: String,
    pub fixture: SmokeFixtureDefinition,
    pub groups: Vec<SmokeGroupDefinition>,
    #[serde(skip)]
    pub group_index_by_id: BTreeMap<String, usize>,
    #[serde(skip)]
    pub suite_index_by_id: BTreeMap<String, (usize, usize)>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeFixtureDefinition {
    pub scene_path: String,
    pub avatar_name: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeGroupDefinition {
    pub group_id: String,
    pub label: String,
    pub description: String,
    pub suites: Vec<SmokeSuiteDefinition>,
    #[serde(skip)]
    pub suite_index_by_id: BTreeMap<String, usize>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeSuiteDefinition {
    pub suite_id: String,
    pub label: String,
    pub description: String,
    pub reset_override: String,
    pub speed: String,
    pub risk: String,
    pub default_selected: bool,
    pub preset_groups: Vec<String>,
    pub requires_play_mode: bool,
    pub stop_on_first_failure: bool,
    pub expected_outcome: String,
    pub debug_hint: String,
    pub cases: Vec<SmokeCaseDefinition>,
    #[serde(skip)]
    pub case_index_by_id: BTreeMap<String, usize>,
}

impl SmokeSuiteDefinition {
    pub fn is_destructive(&self) -> bool {
        self.risk == "destructive"
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeCaseDefinition {
    pub case_id: String,
    pub label: String,
    pub description: String,
    pub expected_outcome: String,
    pub debug_hint: String,
    pub steps: Vec<SmokeStepDefinition>,
    #[serde(skip)]
    pub step_index_by_id: BTreeMap<String, usize>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeStepDefinition {
    pub step_id: String,
    pub label: String,
    pub description: String,
    pub action_type: String,
    pub expected_outcome: String,
    pub debug_hint: String,
}

pub fn canonical_catalog_path() -> PathBuf {
    repository_root().join("Tools/ci/smoke/suite-catalog.json")
}

pub fn load_canonical_catalog() -> Result<SmokeSuiteCatalog, ContractError> {
    let raw = fs::read_to_string(canonical_catalog_path())?;
    load_catalog_from_str(&raw)
}

pub fn load_catalog_from_str(raw: &str) -> Result<SmokeSuiteCatalog, ContractError> {
    if raw.trim().is_empty() {
        return Err(ContractError(
            "Smoke suite catalog JSON is required.".to_string(),
        ));
    }

    let mut catalog: SmokeSuiteCatalog = serde_json::from_str(raw)?;
    normalize_and_validate_catalog(&mut catalog)?;
    Ok(catalog)
}

fn normalize_and_validate_catalog(catalog: &mut SmokeSuiteCatalog) -> Result<(), ContractError> {
    if catalog.catalog_version <= 0 {
        return Err(ContractError(
            "Smoke suite catalog requires a positive catalogVersion.".to_string(),
        ));
    }

    catalog.protocol_version = require_non_blank(&catalog.protocol_version, "protocolVersion")?;
    catalog.fixture.scene_path =
        require_non_blank(&catalog.fixture.scene_path, "fixture.scenePath")?;
    catalog.fixture.avatar_name =
        require_non_blank(&catalog.fixture.avatar_name, "fixture.avatarName")?;
    if catalog.groups.is_empty() {
        return Err(ContractError(
            "Smoke suite catalog requires at least one group.".to_string(),
        ));
    }

    let mut group_ids = HashSet::new();
    let mut suite_ids = HashSet::new();
    catalog.group_index_by_id.clear();
    catalog.suite_index_by_id.clear();

    for (group_index, group) in catalog.groups.iter_mut().enumerate() {
        group.group_id = normalize_unique_id(
            &group.group_id,
            &format!("groups[{group_index}].groupId"),
            &mut group_ids,
        )?;
        group.label = require_non_blank(&group.label, &format!("groups[{group_index}].label"))?;
        group.description = require_non_blank(
            &group.description,
            &format!("groups[{group_index}].description"),
        )?;
        if group.suites.is_empty() {
            return Err(ContractError(format!(
                "groups[{group_index}].suites must not be empty."
            )));
        }

        group.suite_index_by_id.clear();
        for (suite_index, suite) in group.suites.iter_mut().enumerate() {
            normalize_and_validate_suite(suite, group_index, suite_index, &mut suite_ids)?;
            group
                .suite_index_by_id
                .insert(suite.suite_id.clone(), suite_index);
            catalog
                .suite_index_by_id
                .insert(suite.suite_id.clone(), (group_index, suite_index));
        }

        catalog
            .group_index_by_id
            .insert(group.group_id.clone(), group_index);
    }

    Ok(())
}

fn normalize_and_validate_suite(
    suite: &mut SmokeSuiteDefinition,
    group_index: usize,
    suite_index: usize,
    suite_ids: &mut HashSet<String>,
) -> Result<(), ContractError> {
    let path = format!("groups[{group_index}].suites[{suite_index}]");
    suite.suite_id = normalize_unique_id(&suite.suite_id, &(path.clone() + ".suiteId"), suite_ids)?;
    suite.label = require_non_blank(&suite.label, &(path.clone() + ".label"))?;
    suite.description = require_non_blank(&suite.description, &(path.clone() + ".description"))?;
    suite.reset_override = if suite.reset_override.trim().is_empty() {
        "Inherit".to_string()
    } else {
        suite.reset_override.trim().to_string()
    };
    suite.speed = normalize_enum_value(
        &suite.speed,
        &(path.clone() + ".speed"),
        &[
            "quick",
            "standard",
            "exhaustive",
            "destructive",
            "manual-only",
        ],
    )?;
    suite.risk = normalize_enum_value(
        &suite.risk,
        &(path.clone() + ".risk"),
        &["safe", "destructive"],
    )?;
    suite.preset_groups =
        normalize_preset_groups(&suite.preset_groups, &(path.clone() + ".presetGroups"))?;
    suite.expected_outcome = require_non_blank(
        &suite.expected_outcome,
        &(path.clone() + ".expectedOutcome"),
    )?;
    suite.debug_hint = require_non_blank(&suite.debug_hint, &(path.clone() + ".debugHint"))?;
    if suite.cases.is_empty() {
        return Err(ContractError(format!("{path}.cases must not be empty.")));
    }

    let mut case_ids = HashSet::new();
    suite.case_index_by_id.clear();
    for (case_index, case_item) in suite.cases.iter_mut().enumerate() {
        normalize_and_validate_case(case_item, &path, case_index, &mut case_ids)?;
        suite
            .case_index_by_id
            .insert(case_item.case_id.clone(), case_index);
    }

    Ok(())
}

fn normalize_and_validate_case(
    case_item: &mut SmokeCaseDefinition,
    suite_path: &str,
    case_index: usize,
    case_ids: &mut HashSet<String>,
) -> Result<(), ContractError> {
    let path = format!("{suite_path}.cases[{case_index}]");
    case_item.case_id =
        normalize_unique_id(&case_item.case_id, &(path.clone() + ".caseId"), case_ids)?;
    case_item.label = require_non_blank(&case_item.label, &(path.clone() + ".label"))?;
    case_item.description =
        require_non_blank(&case_item.description, &(path.clone() + ".description"))?;
    case_item.expected_outcome = require_non_blank(
        &case_item.expected_outcome,
        &(path.clone() + ".expectedOutcome"),
    )?;
    case_item.debug_hint =
        require_non_blank(&case_item.debug_hint, &(path.clone() + ".debugHint"))?;
    if case_item.steps.is_empty() {
        return Err(ContractError(format!("{path}.steps must not be empty.")));
    }

    let mut step_ids = HashSet::new();
    case_item.step_index_by_id.clear();
    for (step_index, step) in case_item.steps.iter_mut().enumerate() {
        normalize_and_validate_step(step, &path, step_index, &mut step_ids)?;
        case_item
            .step_index_by_id
            .insert(step.step_id.clone(), step_index);
    }

    Ok(())
}

fn normalize_and_validate_step(
    step: &mut SmokeStepDefinition,
    case_path: &str,
    step_index: usize,
    step_ids: &mut HashSet<String>,
) -> Result<(), ContractError> {
    let path = format!("{case_path}.steps[{step_index}]");
    step.step_id = normalize_unique_id(&step.step_id, &(path.clone() + ".stepId"), step_ids)?;
    step.label = require_non_blank(&step.label, &(path.clone() + ".label"))?;
    step.description = require_non_blank(&step.description, &(path.clone() + ".description"))?;
    step.action_type = require_non_blank(&step.action_type, &(path.clone() + ".actionType"))?;
    if !is_supported_action_type(&step.action_type) {
        return Err(ContractError(format!(
            "{path}.actionType '{}' is not supported.",
            step.action_type
        )));
    }
    step.expected_outcome =
        require_non_blank(&step.expected_outcome, &(path.clone() + ".expectedOutcome"))?;
    step.debug_hint = require_non_blank(&step.debug_hint, &(path.clone() + ".debugHint"))?;
    Ok(())
}

fn normalize_unique_id(
    value: &str,
    path: &str,
    seen_ids: &mut HashSet<String>,
) -> Result<String, ContractError> {
    let normalized = require_non_blank(value, path)?;
    if !seen_ids.insert(normalized.clone()) {
        return Err(ContractError(format!(
            "{path} '{normalized}' must be unique within its scope."
        )));
    }
    Ok(normalized)
}

fn normalize_enum_value(
    value: &str,
    path: &str,
    allowed_values: &[&str],
) -> Result<String, ContractError> {
    let normalized = require_non_blank(value, path)?;
    if !allowed_values.iter().any(|allowed| *allowed == normalized) {
        return Err(ContractError(format!(
            "{path} '{normalized}' is not supported."
        )));
    }
    Ok(normalized)
}

fn normalize_preset_groups(values: &[String], path: &str) -> Result<Vec<String>, ContractError> {
    if values.is_empty() {
        return Err(ContractError(format!(
            "{path} must include at least one preset group."
        )));
    }

    let mut seen = HashSet::new();
    let mut normalized = Vec::with_capacity(values.len());
    for (index, value) in values.iter().enumerate() {
        normalized.push(normalize_unique_id(
            value,
            &format!("{path}[{index}]"),
            &mut seen,
        )?);
    }
    Ok(normalized)
}

fn require_non_blank(value: &str, path: &str) -> Result<String, ContractError> {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        return Err(ContractError(format!("{path} must not be blank.")));
    }
    Ok(trimmed.to_string())
}

fn is_supported_action_type(action_type: &str) -> bool {
    matches!(action_type, |"open-scene"| "open-window"
        | "select-avatar"
        | "add-prefab"
        | "rebuild"
        | "vendorize"
        | "detach"
        | "lifecycle-hygiene-cleanup"
        | "return-to-package-managed"
        | "enter-playmode"
        | "exit-playmode"
        | "assert-primary-action"
        | "assert-runtime-component-valid"
        | "assert-host-ready")
}

fn repository_root() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("../../..")
        .canonicalize()
        .unwrap_or_else(|_| PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("../../.."))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn catalog_parses_canonical_order_and_fixture_metadata() {
        let catalog = load_canonical_catalog().expect("canonical catalog should parse");
        let group_ids: Vec<&str> = catalog
            .groups
            .iter()
            .map(|group| group.group_id.as_str())
            .collect();
        assert_eq!(
            group_ids,
            vec!["editor-window", "lifecycle", "playmode-runtime"]
        );
        assert_eq!(catalog.fixture.scene_path, "Assets/Click ME.unity");
        assert_eq!(catalog.fixture.avatar_name, "Oct25_Dress");
        assert_eq!(catalog.groups[0].suites[0].suite_id, "setup-scene-avatar");
        let setup_steps: Vec<&str> = catalog.groups[0].suites[0].cases[0]
            .steps
            .iter()
            .map(|step| step.action_type.as_str())
            .collect();
        assert_eq!(
            setup_steps,
            vec![
                "open-scene",
                "open-window",
                "select-avatar",
                "add-prefab",
                "assert-primary-action"
            ]
        );
        assert_eq!(catalog.groups[0].suites.len(), 1);
        assert_eq!(catalog.groups[1].suites[0].suite_id, "lifecycle-roundtrip");
        let lifecycle_steps: Vec<&str> = catalog.groups[1].suites[0].cases[0]
            .steps
            .iter()
            .map(|step| step.step_id.as_str())
            .collect();
        assert_eq!(
            lifecycle_steps,
            vec![
                "rebuild",
                "hygiene-cleanup-after-rebuild",
                "vendorize",
                "hygiene-cleanup-after-vendorize",
                "detach",
                "hygiene-cleanup-after-detach",
                "return-to-package-managed"
            ]
        );
        assert_eq!(
            catalog.groups[2].suites[0].suite_id,
            "playmode-runtime-validation"
        );
    }

    #[test]
    fn catalog_parses_suite_metadata_for_default_presets_and_risk() {
        let catalog = load_canonical_catalog().expect("canonical catalog should parse");
        let suites: Vec<&SmokeSuiteDefinition> = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .collect();
        let default_ids: Vec<&str> = suites
            .iter()
            .filter(|suite| suite.default_selected)
            .map(|suite| suite.suite_id.as_str())
            .collect();

        assert_eq!(
            default_ids,
            vec![
                "setup-scene-avatar",
                "lifecycle-roundtrip",
                "playmode-runtime-validation"
            ]
        );
        assert!(suites.iter().all(|suite| suite.risk == "safe"));
        assert!(suites.iter().all(|suite| !suite.is_destructive()));
        assert!(suites
            .iter()
            .all(|suite| suite.preset_groups.contains(&"quick-default".to_string())));
        assert_eq!(
            suites
                .iter()
                .find(|suite| suite.suite_id == "setup-scene-avatar")
                .expect("setup suite exists")
                .speed,
            "quick"
        );
    }

    #[test]
    fn catalog_rejects_unknown_suite_metadata_values() {
        let raw = fs::read_to_string(canonical_catalog_path())
            .expect("catalog fixture should exist")
            .replace("\"risk\": \"safe\"", "\"risk\": \"maybe\"");

        let error = load_catalog_from_str(&raw).expect_err("unknown risk should fail");
        assert!(error.to_string().contains("risk"));
    }

    #[test]
    fn catalog_rejects_blank_preset_groups() {
        let raw = fs::read_to_string(canonical_catalog_path())
            .expect("catalog fixture should exist")
            .replace("\"quick-default\"", "\"   \"");

        let error = load_catalog_from_str(&raw).expect_err("blank preset should fail");
        assert!(error.to_string().contains("presetGroups"));
    }

    #[test]
    fn catalog_rejects_blank_group_ids() {
        let raw = fs::read_to_string(canonical_catalog_path())
            .expect("catalog fixture should exist")
            .replace("\"groupId\": \"editor-window\"", "\"groupId\": \"   \"");

        let error = load_catalog_from_str(&raw).expect_err("blank group id should fail");
        assert!(error.to_string().contains("groupId"));
    }

    #[test]
    fn catalog_rejects_duplicate_suite_ids() {
        let raw = fs::read_to_string(canonical_catalog_path())
            .expect("catalog fixture should exist")
            .replace(
                "\"suiteId\": \"lifecycle-roundtrip\"",
                "\"suiteId\": \"setup-scene-avatar\"",
            );

        let error = load_catalog_from_str(&raw).expect_err("duplicate suite id should fail");
        assert!(error.to_string().contains("suiteId"));
    }

    #[test]
    fn catalog_rejects_unknown_action_types() {
        let raw = fs::read_to_string(canonical_catalog_path())
            .expect("catalog fixture should exist")
            .replace(
                "\"actionType\": \"open-window\"",
                "\"actionType\": \"mystery-action\"",
            );

        let error = load_catalog_from_str(&raw).expect_err("unknown action type should fail");
        assert!(error.to_string().contains("actionType"));
    }

    #[test]
    fn catalog_rejects_empty_step_arrays() {
        let raw = concat!(
            "{\n",
            "  \"catalogVersion\": 1,\n",
            "  \"protocolVersion\": \"1.0.0\",\n",
            "  \"fixture\": { \"scenePath\": \"Assets/Click ME.unity\", \"avatarName\": \"Oct25_Dress\" },\n",
            "  \"groups\": [\n",
            "    {\n",
            "      \"groupId\": \"editor-window\",\n",
            "      \"label\": \"Editor Window\",\n",
            "      \"description\": \"desc\",\n",
            "      \"suites\": [\n",
            "        {\n",
            "          \"suiteId\": \"open-select-add\",\n",
            "          \"label\": \"Open\",\n",
            "          \"description\": \"desc\",\n",
            "          \"resetOverride\": \"Inherit\",\n",
            "          \"speed\": \"quick\",\n",
            "          \"risk\": \"safe\",\n",
            "          \"defaultSelected\": true,\n",
            "          \"presetGroups\": [\"quick-default\"],\n",
            "          \"requiresPlayMode\": false,\n",
            "          \"stopOnFirstFailure\": true,\n",
            "          \"expectedOutcome\": \"ok\",\n",
            "          \"debugHint\": \"hint\",\n",
            "          \"cases\": [\n",
            "            {\n",
            "              \"caseId\": \"window-scaffold\",\n",
            "              \"label\": \"Case\",\n",
            "              \"description\": \"desc\",\n",
            "              \"expectedOutcome\": \"ok\",\n",
            "              \"debugHint\": \"hint\",\n",
            "              \"steps\": []\n",
            "            }\n",
            "          ]\n",
            "        }\n",
            "      ]\n",
            "    }\n",
            "  ]\n",
            "}"
        );

        let error = load_catalog_from_str(raw).expect_err("empty steps should fail");
        assert!(error.to_string().contains("steps"));
    }
}
