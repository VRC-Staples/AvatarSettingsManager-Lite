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
    #[serde(default)]
    pub args: SmokeStepArgs,
    pub expected_outcome: String,
    pub debug_hint: String,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
#[serde(rename_all = "camelCase")]
pub struct SmokeStepArgs {
    #[serde(default)]
    pub scene_path: String,
    #[serde(default)]
    pub avatar_name: String,
    #[serde(default)]
    pub object_name: String,
    #[serde(default)]
    pub fixture_mutation: String,
    #[serde(default)]
    pub expected_primary_action: String,
    #[serde(default)]
    pub expected_diagnostic_code: String,
    #[serde(default)]
    pub expected_diagnostic_contains: String,
    #[serde(default)]
    pub expected_state: String,
    #[serde(default)]
    pub slot_count: i32,
    #[serde(default)]
    pub install_path_preset_id: String,
    #[serde(default)]
    pub expected_install_path_enabled: bool,
    #[serde(default)]
    pub expected_normalized_effective_path: String,
    #[serde(default)]
    pub expected_component_present: bool,
    #[serde(default)]
    pub expect_step_failure: bool,
    #[serde(default)]
    pub preserve_failure_evidence: bool,
    #[serde(default)]
    pub require_clean_reset: bool,
    #[serde(default, alias = "customRootNameEnabled")]
    pub use_custom_root_name: Option<bool>,
    #[serde(default)]
    pub custom_root_name: Option<String>,
    #[serde(default, alias = "customPresetNames")]
    pub preset_names_by_slot: Option<Vec<String>>,
    #[serde(default, alias = "customSaveLabel")]
    pub save_label: Option<String>,
    #[serde(default, alias = "customLoadLabel")]
    pub load_label: Option<String>,
    #[serde(default, alias = "customClearLabel")]
    pub clear_label: Option<String>,
    #[serde(default, alias = "customConfirmLabel")]
    pub confirm_label: Option<String>,
    #[serde(default)]
    pub clear_existing: bool,
    #[serde(default)]
    pub icon_mode: Option<String>,
    #[serde(default)]
    pub selected_gear_index: Option<i32>,
    #[serde(default)]
    pub gear_color: Option<String>,
    #[serde(default)]
    pub use_custom_slot_icons: Option<bool>,
    #[serde(default)]
    pub root_icon_fixture_id: Option<String>,
    #[serde(default, alias = "slotIconFixtureIds")]
    pub slot_icon_fixture_ids_by_slot: Option<Vec<String>>,
    #[serde(default)]
    pub action_icon_mode: Option<String>,
    #[serde(default)]
    pub save_icon_fixture_id: Option<String>,
    #[serde(default)]
    pub load_icon_fixture_id: Option<String>,
    #[serde(default)]
    pub clear_icon_fixture_id: Option<String>,
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
    normalize_and_validate_step_args(&step.action_type, &mut step.args, &(path.clone() + ".args"))?;
    step.expected_outcome =
        require_non_blank(&step.expected_outcome, &(path.clone() + ".expectedOutcome"))?;
    step.debug_hint = require_non_blank(&step.debug_hint, &(path.clone() + ".debugHint"))?;
    Ok(())
}

fn normalize_and_validate_step_args(
    action_type: &str,
    args: &mut SmokeStepArgs,
    path: &str,
) -> Result<(), ContractError> {
    normalize_step_args(args);
    if args.expect_step_failure {
        args.expected_diagnostic_code = require_non_blank(
            &args.expected_diagnostic_code,
            &(path.to_string() + ".expectedDiagnosticCode"),
        )?;
        args.expected_diagnostic_contains = require_non_blank(
            &args.expected_diagnostic_contains,
            &(path.to_string() + ".expectedDiagnosticContains"),
        )?;
    }

    match action_type {
        "assert-pending-customization-snapshot" | "assert-attached-customization-snapshot" => {
            validate_optional_slot_count(args.slot_count, &(path.to_string() + ".slotCount"))?;
            if !args.install_path_preset_id.is_empty() {
                let preset_id = require_install_path_preset(
                    &args.install_path_preset_id,
                    &(path.to_string() + ".installPathPresetId"),
                )?;
                args.install_path_preset_id = preset_id;
            }
            validate_optional_preset_names_by_slot(args, path)?;
        }
        "set-root-name-state" => {
            let enabled = args.use_custom_root_name.ok_or_else(|| {
                ContractError(format!(
                    "{path}.useCustomRootName is required for actionType 'set-root-name-state'."
                ))
            })?;
            let root_name =
                normalize_optional_option(args.custom_root_name.take()).unwrap_or_default();
            if enabled && root_name.is_empty() {
                return Err(ContractError(format!(
                    "{path}.customRootName must not be blank when useCustomRootName is true."
                )));
            }
            args.custom_root_name = Some(if enabled { root_name } else { String::new() });
            reject_present_arg(
                args.preset_names_by_slot.is_some(),
                &(path.to_string() + ".presetNamesBySlot"),
            )?;
            reject_present_arg(
                args.save_label.is_some(),
                &(path.to_string() + ".saveLabel"),
            )?;
            reject_present_arg(
                args.load_label.is_some(),
                &(path.to_string() + ".loadLabel"),
            )?;
            reject_present_arg(
                args.clear_label.is_some(),
                &(path.to_string() + ".clearLabel"),
            )?;
            reject_present_arg(
                args.confirm_label.is_some(),
                &(path.to_string() + ".confirmLabel"),
            )?;
        }
        "set-preset-name-mask" => {
            let names = args.preset_names_by_slot.as_ref().ok_or_else(|| {
                ContractError(format!(
                    "{path}.presetNamesBySlot is required for actionType 'set-preset-name-mask'."
                ))
            })?;
            if names.is_empty() {
                return Err(ContractError(format!(
                    "{path}.presetNamesBySlot must include at least one slot label."
                )));
            }
            validate_optional_preset_names_by_slot(args, path)?;
            reject_present_arg(
                args.use_custom_root_name.is_some(),
                &(path.to_string() + ".useCustomRootName"),
            )?;
            reject_present_arg(
                args.custom_root_name.is_some(),
                &(path.to_string() + ".customRootName"),
            )?;
            reject_present_arg(
                args.save_label.is_some(),
                &(path.to_string() + ".saveLabel"),
            )?;
            reject_present_arg(
                args.load_label.is_some(),
                &(path.to_string() + ".loadLabel"),
            )?;
            reject_present_arg(
                args.clear_label.is_some(),
                &(path.to_string() + ".clearLabel"),
            )?;
            reject_present_arg(
                args.confirm_label.is_some(),
                &(path.to_string() + ".confirmLabel"),
            )?;
        }
        "set-action-label-mask" => {
            let has_label = args.save_label.is_some()
                || args.load_label.is_some()
                || args.clear_label.is_some()
                || args.confirm_label.is_some();
            if !has_label {
                return Err(ContractError(format!(
                    "{path} must include at least one of saveLabel, loadLabel, clearLabel, or confirmLabel for actionType 'set-action-label-mask'."
                )));
            }
            reject_present_arg(
                args.use_custom_root_name.is_some(),
                &(path.to_string() + ".useCustomRootName"),
            )?;
            reject_present_arg(
                args.custom_root_name.is_some(),
                &(path.to_string() + ".customRootName"),
            )?;
            reject_present_arg(
                args.preset_names_by_slot.is_some(),
                &(path.to_string() + ".presetNamesBySlot"),
            )?;
        }
        "set-icon-mode" => {
            let icon_mode = args.icon_mode.as_deref().ok_or_else(|| {
                ContractError(format!(
                    "{path}.iconMode is required for actionType 'set-icon-mode'."
                ))
            })?;
            args.icon_mode = Some(require_icon_mode(
                icon_mode,
                &(path.to_string() + ".iconMode"),
            )?);
        }
        "set-gear-color" => {
            if let Some(index) = args.selected_gear_index {
                require_gear_index(index, &(path.to_string() + ".selectedGearIndex"))?;
            }
            if let Some(color) = args.gear_color.as_deref() {
                args.gear_color = Some(require_gear_color(
                    color,
                    &(path.to_string() + ".gearColor"),
                )?);
            }
            if args.selected_gear_index.is_none() && args.gear_color.is_none() {
                return Err(ContractError(format!(
                    "{path} must include selectedGearIndex or gearColor for actionType 'set-gear-color'."
                )));
            }
        }
        "set-custom-icons-enabled" => {
            if args.use_custom_slot_icons.is_none() {
                return Err(ContractError(format!(
                    "{path}.useCustomSlotIcons is required for actionType 'set-custom-icons-enabled'."
                )));
            }
        }
        "set-root-icon-fixture" => {
            let fixture_id = args.root_icon_fixture_id.as_deref().ok_or_else(|| {
                ContractError(format!(
                    "{path}.rootIconFixtureId is required for actionType 'set-root-icon-fixture'."
                ))
            })?;
            validate_optional_icon_fixture_id(
                fixture_id,
                &(path.to_string() + ".rootIconFixtureId"),
            )?;
        }
        "set-slot-icon-mask" => {
            let fixture_ids = args.slot_icon_fixture_ids_by_slot.as_ref().ok_or_else(|| {
                ContractError(format!(
                    "{path}.slotIconFixtureIdsBySlot is required for actionType 'set-slot-icon-mask'."
                ))
            })?;
            for (index, fixture_id) in fixture_ids.iter().enumerate() {
                validate_optional_icon_fixture_id(
                    fixture_id,
                    &format!("{path}.slotIconFixtureIdsBySlot[{index}]"),
                )?;
            }
        }
        "set-action-icon-mask" => {
            if let Some(action_icon_mode) = args.action_icon_mode.as_deref() {
                args.action_icon_mode = Some(require_action_icon_mode(
                    action_icon_mode,
                    &(path.to_string() + ".actionIconMode"),
                )?);
            }
            validate_optional_icon_fixture_id(
                args.save_icon_fixture_id.as_deref().unwrap_or_default(),
                &(path.to_string() + ".saveIconFixtureId"),
            )?;
            validate_optional_icon_fixture_id(
                args.load_icon_fixture_id.as_deref().unwrap_or_default(),
                &(path.to_string() + ".loadIconFixtureId"),
            )?;
            validate_optional_icon_fixture_id(
                args.clear_icon_fixture_id.as_deref().unwrap_or_default(),
                &(path.to_string() + ".clearIconFixtureId"),
            )?;
            if args.action_icon_mode.is_none()
                && args.save_icon_fixture_id.is_none()
                && args.load_icon_fixture_id.is_none()
                && args.clear_icon_fixture_id.is_none()
            {
                return Err(ContractError(format!(
                    "{path} must include actionIconMode or an action icon fixture ID for actionType 'set-action-icon-mask'."
                )));
            }
        }
        _ => {}
    }

    Ok(())
}

fn normalize_step_args(args: &mut SmokeStepArgs) {
    args.scene_path = normalize_optional(&args.scene_path);
    args.avatar_name = normalize_optional(&args.avatar_name);
    args.object_name = normalize_optional(&args.object_name);
    args.fixture_mutation = normalize_optional(&args.fixture_mutation);
    args.expected_primary_action = normalize_optional(&args.expected_primary_action);
    args.expected_diagnostic_code = normalize_optional(&args.expected_diagnostic_code);
    args.expected_diagnostic_contains = normalize_optional(&args.expected_diagnostic_contains);
    args.expected_state = normalize_optional(&args.expected_state);
    args.install_path_preset_id = normalize_optional(&args.install_path_preset_id);
    args.expected_normalized_effective_path =
        normalize_install_path(&args.expected_normalized_effective_path);
    args.custom_root_name = normalize_optional_option(args.custom_root_name.take());
    args.preset_names_by_slot = args.preset_names_by_slot.take().map(|names| {
        names
            .into_iter()
            .map(|name| normalize_optional(&name))
            .collect()
    });
    args.save_label = normalize_optional_option(args.save_label.take());
    args.load_label = normalize_optional_option(args.load_label.take());
    args.clear_label = normalize_optional_option(args.clear_label.take());
    args.confirm_label = normalize_optional_option(args.confirm_label.take());
    args.icon_mode = normalize_optional_option(args.icon_mode.take());
    args.gear_color = normalize_optional_option(args.gear_color.take());
    args.root_icon_fixture_id = normalize_optional_option(args.root_icon_fixture_id.take());
    args.slot_icon_fixture_ids_by_slot = args
        .slot_icon_fixture_ids_by_slot
        .take()
        .map(|ids| ids.into_iter().map(|id| normalize_optional(&id)).collect());
    args.action_icon_mode = normalize_optional_option(args.action_icon_mode.take());
    args.save_icon_fixture_id = normalize_optional_option(args.save_icon_fixture_id.take());
    args.load_icon_fixture_id = normalize_optional_option(args.load_icon_fixture_id.take());
    args.clear_icon_fixture_id = normalize_optional_option(args.clear_icon_fixture_id.take());
}

fn reject_present_arg(is_present: bool, path: &str) -> Result<(), ContractError> {
    if is_present {
        return Err(ContractError(format!(
            "{path} is not valid for this actionType."
        )));
    }
    Ok(())
}

fn require_slot_count_in_range(slot_count: i32, path: &str) -> Result<(), ContractError> {
    if !(1..=8).contains(&slot_count) {
        return Err(ContractError(format!("{path} must be between 1 and 8.")));
    }
    Ok(())
}

fn validate_optional_slot_count(slot_count: i32, path: &str) -> Result<(), ContractError> {
    if slot_count != 0 {
        require_slot_count_in_range(slot_count, path)?;
    }
    Ok(())
}

fn require_install_path_preset(value: &str, path: &str) -> Result<String, ContractError> {
    normalize_enum_value(value, path, &["disabled", "root", "simple", "nested"])
}

fn require_icon_mode(value: &str, path: &str) -> Result<String, ContractError> {
    normalize_enum_alias(
        value,
        path,
        &[
            ("multiColor", "multiColor"),
            ("MultiColor", "multiColor"),
            ("sameColor", "sameColor"),
            ("SameColor", "sameColor"),
            ("custom", "custom"),
            ("Custom", "custom"),
        ],
    )
}

fn require_action_icon_mode(value: &str, path: &str) -> Result<String, ContractError> {
    normalize_enum_alias(
        value,
        path,
        &[
            ("default", "default"),
            ("Default", "default"),
            ("custom", "custom"),
            ("Custom", "custom"),
        ],
    )
}

fn require_gear_color(value: &str, path: &str) -> Result<String, ContractError> {
    normalize_enum_alias(
        value,
        path,
        &[
            ("blue", "blue"),
            ("Blue", "blue"),
            ("red", "red"),
            ("Red", "red"),
            ("green", "green"),
            ("Green", "green"),
            ("purple", "purple"),
            ("Purple", "purple"),
            ("cyan", "cyan"),
            ("Cyan", "cyan"),
            ("orange", "orange"),
            ("Orange", "orange"),
            ("pink", "pink"),
            ("Pink", "pink"),
            ("yellow", "yellow"),
            ("Yellow", "yellow"),
        ],
    )
}

fn require_gear_index(value: i32, path: &str) -> Result<(), ContractError> {
    if !(0..=7).contains(&value) {
        return Err(ContractError(format!("{path} must be between 0 and 7.")));
    }
    Ok(())
}

fn validate_optional_icon_fixture_id(value: &str, path: &str) -> Result<(), ContractError> {
    let fixture_id = normalize_optional(value);
    if fixture_id.is_empty() || is_known_icon_fixture_id(&fixture_id) {
        return Ok(());
    }

    Err(ContractError(format!(
        "{path} has unsupported icon fixture ID '{fixture_id}'. Known IDs: {}.",
        KNOWN_ICON_FIXTURE_IDS.join(", ")
    )))
}

fn is_known_icon_fixture_id(value: &str) -> bool {
    KNOWN_ICON_FIXTURE_IDS
        .iter()
        .any(|fixture_id| *fixture_id == value)
}

const KNOWN_ICON_FIXTURE_IDS: &[&str] = &[
    "asm-lite-icon/root",
    "asm-lite-icon/slot-01",
    "asm-lite-icon/slot-02",
    "asm-lite-icon/slot-03",
    "asm-lite-icon/slot-04",
    "asm-lite-icon/slot-05",
    "asm-lite-icon/slot-06",
    "asm-lite-icon/slot-07",
    "asm-lite-icon/slot-08",
    "asm-lite-icon/action-save",
    "asm-lite-icon/action-load",
    "asm-lite-icon/action-clear",
];

fn validate_expected_install_path_matches_preset(
    args: &SmokeStepArgs,
    preset_id: &str,
    args_path: &str,
) -> Result<(), ContractError> {
    let (expected_enabled, expected_path) = resolve_install_path_preset(preset_id)?;
    if args.expected_install_path_enabled != expected_enabled {
        return Err(ContractError(format!(
            "{args_path}.expectedInstallPathEnabled must be {} for installPathPresetId '{preset_id}'.",
            expected_enabled.to_string().to_lowercase()
        )));
    }

    let normalized_expected_path = normalize_install_path(expected_path);
    if args.expected_normalized_effective_path != normalized_expected_path {
        return Err(ContractError(format!(
            "{args_path}.expectedNormalizedEffectivePath must be '{normalized_expected_path}' for installPathPresetId '{preset_id}'."
        )));
    }
    Ok(())
}

fn resolve_install_path_preset(preset_id: &str) -> Result<(bool, &'static str), ContractError> {
    match preset_id {
        "disabled" => Ok((false, "")),
        "root" => Ok((true, "")),
        "simple" => Ok((true, "ASM-Lite")),
        "nested" => Ok((true, "Avatars/ASM-Lite")),
        _ => Err(ContractError(format!(
            "installPathPresetId '{preset_id}' is not supported."
        ))),
    }
}

fn validate_optional_preset_names_by_slot(
    args: &SmokeStepArgs,
    path: &str,
) -> Result<(), ContractError> {
    if let Some(names) = &args.preset_names_by_slot {
        if names.len() > 8 {
            return Err(ContractError(format!(
                "{path}.presetNamesBySlot must not contain more than 8 slot labels."
            )));
        }
    }
    Ok(())
}

fn normalize_optional(value: &str) -> String {
    value.trim().to_string()
}

fn normalize_optional_option(value: Option<String>) -> Option<String> {
    value.map(|item| normalize_optional(&item))
}

fn normalize_install_path(value: &str) -> String {
    let mut normalized = normalize_optional(value).replace('\\', "/");
    while normalized.contains("//") {
        normalized = normalized.replace("//", "/");
    }
    normalized.trim_matches('/').to_string()
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

fn normalize_enum_alias(
    value: &str,
    path: &str,
    allowed_values: &[(&str, &str)],
) -> Result<String, ContractError> {
    let normalized = require_non_blank(value, path)?;
    for (allowed, canonical) in allowed_values {
        if *allowed == normalized {
            return Ok((*canonical).to_string());
        }
    }

    Err(ContractError(format!(
        "{path} '{normalized}' is not supported."
    )))
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
        | "close-window"
        | "prelude-recover-context"
        | "assert-window-focused"
        | "assert-package-resource-present"
        | "assert-catalog-loads"
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
        | "assert-no-component"
        | "set-slot-count"
        | "set-install-path-state"
        | "assert-pending-customization-snapshot"
        | "assert-attached-customization-snapshot"
        | "set-root-name-state"
        | "set-preset-name-mask"
        | "set-action-label-mask"
        | "set-icon-mode"
        | "set-gear-color"
        | "set-custom-icons-enabled"
        | "set-root-icon-fixture"
        | "set-slot-icon-mask"
        | "set-action-icon-mask"
        | "assert-generated-references-package-managed"
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
            vec![
                "preflight",
                "editor-window",
                "lifecycle",
                "playmode-runtime"
            ]
        );
        assert_eq!(catalog.fixture.scene_path, "Assets/Click ME.unity");
        assert_eq!(catalog.fixture.avatar_name, "Oct25_Dress");
        assert_eq!(
            catalog.groups[0].suites[0].suite_id,
            "asm-lite-readiness-check"
        );
        assert_eq!(catalog.groups[1].suites[0].suite_id, "setup-scene-avatar");
        let setup_steps: Vec<&str> = catalog.groups[1].suites[0].cases[0]
            .steps
            .iter()
            .map(|step| step.action_type.as_str())
            .collect();
        assert_eq!(
            setup_steps,
            vec![
                "open-scene",
                "close-window",
                "open-window",
                "assert-window-focused",
                "close-window",
                "open-window",
                "assert-window-focused",
                "select-avatar",
                "add-prefab",
                "assert-primary-action"
            ]
        );
        assert_eq!(catalog.groups[0].suites[0].cases.len(), 6);
        assert!(catalog.groups[1].suites.len() >= 11);
        assert_eq!(catalog.groups[2].suites[0].suite_id, "lifecycle-roundtrip");
        let lifecycle_steps: Vec<&str> = catalog.groups[2].suites[0].cases[0]
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
            catalog.groups[3].suites[0].suite_id,
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
                "asm-lite-readiness-check",
                "setup-scene-avatar",
                "lifecycle-roundtrip",
                "playmode-runtime-validation"
            ]
        );
        assert!(suites
            .iter()
            .filter(|suite| suite.default_selected)
            .all(|suite| suite.risk == "safe"));
        assert!(suites
            .iter()
            .filter(|suite| suite.default_selected)
            .all(|suite| !suite.is_destructive()));
        let destructive = suites
            .iter()
            .find(|suite| suite.suite_id == "destructive-recovery-reset")
            .expect("destructive recovery suite exists");
        assert_eq!(destructive.speed, "destructive");
        assert_eq!(destructive.risk, "destructive");
        assert!(destructive.is_destructive());
        let quick_default_ids: Vec<&str> = suites
            .iter()
            .filter(|suite| suite.preset_groups.contains(&"quick-default".to_string()))
            .map(|suite| suite.suite_id.as_str())
            .collect();
        assert_eq!(
            quick_default_ids,
            vec![
                "asm-lite-readiness-check",
                "setup-scene-avatar",
                "lifecycle-roundtrip",
                "playmode-runtime-validation"
            ]
        );
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
    fn catalog_accepts_phase1_and_recover_context_action_tokens() {
        for action_type in [
            "prelude-recover-context",
            "assert-no-component",
            "set-slot-count",
            "set-install-path-state",
            "assert-pending-customization-snapshot",
            "assert-attached-customization-snapshot",
        ] {
            let raw = format!(
                r#"{{
  "catalogVersion": 1,
  "protocolVersion": "1.0.0",
  "fixture": {{ "scenePath": "Assets/Click ME.unity", "avatarName": "Oct25_Dress" }},
  "groups": [
    {{
      "groupId": "phase1",
      "label": "Phase 1",
      "description": "desc",
      "suites": [
        {{
          "suiteId": "phase1-suite",
          "label": "Phase 1 suite",
          "description": "desc",
          "resetOverride": "Inherit",
          "speed": "quick",
          "risk": "safe",
          "defaultSelected": true,
          "presetGroups": ["quick-default"],
          "requiresPlayMode": false,
          "stopOnFirstFailure": true,
          "expectedOutcome": "ok",
          "debugHint": "hint",
          "cases": [
            {{
              "caseId": "phase1-case",
              "label": "Case",
              "description": "desc",
              "expectedOutcome": "ok",
              "debugHint": "hint",
              "steps": [
                {{
                  "stepId": "phase1-step",
                  "label": "Step",
                  "description": "desc",
                  "actionType": "{}",
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                }}
              ]
            }}
          ]
        }}
      ]
    }}
  ]
}}"#,
                action_type
            );

            load_catalog_from_str(&raw).unwrap_or_else(|error| {
                panic!("phase1 action token {action_type} should parse: {error}")
            });
        }
    }

    #[test]
    fn catalog_accepts_setup36_icon_suite_actions_and_long_fixture_masks() {
        let raw = r#"{
  "catalogVersion": 1,
  "protocolVersion": "1.0.0",
  "fixture": { "scenePath": "Assets/Click ME.unity", "avatarName": "Oct25_Dress" },
  "groups": [
    {
      "groupId": "setup",
      "label": "Setup",
      "description": "desc",
      "suites": [
        {
          "suiteId": "setup-prebuild-icons-matrix",
          "label": "Icons matrix",
          "description": "desc",
          "resetOverride": "Inherit",
          "speed": "quick",
          "risk": "safe",
          "defaultSelected": false,
          "presetGroups": ["all-setup"],
          "requiresPlayMode": false,
          "stopOnFirstFailure": true,
          "expectedOutcome": "ok",
          "debugHint": "hint",
          "cases": [
            {
              "caseId": "icon-contract",
              "label": "Case",
              "description": "desc",
              "expectedOutcome": "ok",
              "debugHint": "hint",
              "steps": [
                {
                  "stepId": "icon-mode",
                  "label": "Icon mode",
                  "description": "desc",
                  "actionType": "set-icon-mode",
                  "args": { "iconMode": "Custom" },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                },
                {
                  "stepId": "gear-color",
                  "label": "Gear color",
                  "description": "desc",
                  "actionType": "set-gear-color",
                  "args": { "selectedGearIndex": 7, "gearColor": "Yellow" },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                },
                {
                  "stepId": "custom-icons-enabled",
                  "label": "Custom icons",
                  "description": "desc",
                  "actionType": "set-custom-icons-enabled",
                  "args": { "useCustomSlotIcons": true },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                },
                {
                  "stepId": "root-fixture",
                  "label": "Root fixture",
                  "description": "desc",
                  "actionType": "set-root-icon-fixture",
                  "args": { "rootIconFixtureId": "asm-lite-icon/root" },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                },
                {
                  "stepId": "slot-mask",
                  "label": "Slot mask",
                  "description": "desc",
                  "actionType": "set-slot-icon-mask",
                  "args": {
                    "slotIconFixtureIdsBySlot": [
                      "asm-lite-icon/slot-01", "asm-lite-icon/slot-02", "asm-lite-icon/slot-03", "asm-lite-icon/slot-04",
                      "asm-lite-icon/slot-05", "asm-lite-icon/slot-06", "asm-lite-icon/slot-07", "asm-lite-icon/slot-08",
                      "", "asm-lite-icon/slot-01", "asm-lite-icon/action-save", "asm-lite-icon/action-clear"
                    ]
                  },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                },
                {
                  "stepId": "slot-mask-alias",
                  "label": "Slot mask alias",
                  "description": "desc",
                  "actionType": "set-slot-icon-mask",
                  "args": {
                    "slotIconFixtureIds": ["asm-lite-icon/slot-01"]
                  },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                },
                {
                  "stepId": "action-mask",
                  "label": "Action mask",
                  "description": "desc",
                  "actionType": "set-action-icon-mask",
                  "args": {
                    "actionIconMode": "Custom",
                    "saveIconFixtureId": "asm-lite-icon/action-save",
                    "loadIconFixtureId": "asm-lite-icon/action-load",
                    "clearIconFixtureId": "asm-lite-icon/action-clear"
                  },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}"#;

        let catalog = load_catalog_from_str(raw).expect("setup36 icon catalog should parse");
        let suite = &catalog.groups[0].suites[0];
        assert_eq!(suite.suite_id, "setup-prebuild-icons-matrix");
        assert_eq!(suite.cases[0].steps.len(), 7);
        let slot_mask = &suite.cases[0].steps[4].args;
        assert_eq!(
            slot_mask
                .slot_icon_fixture_ids_by_slot
                .as_ref()
                .expect("slot fixture mask should deserialize")
                .len(),
            12
        );
        let slot_mask_alias = &suite.cases[0].steps[5].args;
        assert_eq!(
            slot_mask_alias
                .slot_icon_fixture_ids_by_slot
                .as_ref()
                .expect("slot fixture alias should deserialize"),
            &vec!["asm-lite-icon/slot-01".to_string()]
        );
    }

    #[test]
    fn catalog_rejects_unknown_icon_fixture_ids() {
        let raw = r#"{
  "catalogVersion": 1,
  "protocolVersion": "1.0.0",
  "fixture": { "scenePath": "Assets/Click ME.unity", "avatarName": "Oct25_Dress" },
  "groups": [
    {
      "groupId": "setup",
      "label": "Setup",
      "description": "desc",
      "suites": [
        {
          "suiteId": "setup-prebuild-icons-matrix",
          "label": "Icons matrix",
          "description": "desc",
          "resetOverride": "Inherit",
          "speed": "quick",
          "risk": "safe",
          "defaultSelected": false,
          "presetGroups": ["all-setup"],
          "requiresPlayMode": false,
          "stopOnFirstFailure": true,
          "expectedOutcome": "ok",
          "debugHint": "hint",
          "cases": [
            {
              "caseId": "icon-contract",
              "label": "Case",
              "description": "desc",
              "expectedOutcome": "ok",
              "debugHint": "hint",
              "steps": [
                {
                  "stepId": "bad-slot-mask",
                  "label": "Slot mask",
                  "description": "desc",
                  "actionType": "set-slot-icon-mask",
                  "args": { "slotIconFixtureIdsBySlot": ["asm-lite-icon/not-real"] },
                  "expectedOutcome": "ok",
                  "debugHint": "hint"
                }
              ]
            }
          ]
        }
      ]
    }
  ]
}"#;

        let error = load_catalog_from_str(raw).expect_err("unknown fixture id should fail");
        assert!(error.to_string().contains("unsupported icon fixture ID"));
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
