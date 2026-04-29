use crate::model::{
    review_actions, ReviewSummaryModel, SuiteSelectionModel, RUN_DISPATCH_READY_MESSAGE,
};
use crate::theme::{GROUP_HEADER_PREFIX, ROW_SELECTED_PREFIX, ROW_UNSELECTED_PREFIX};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SuiteRowView {
    pub group_id: String,
    pub group_label: String,
    pub suite_id: String,
    pub suite_label: String,
    pub speed: String,
    pub risk: String,
    pub default_selected: bool,
    pub preset_groups: Vec<String>,
    pub destructive: bool,
    pub selectable: bool,
    pub disabled_reason: Option<&'static str>,
    pub selected: bool,
    pub checked: bool,
    pub focused: bool,
    pub selected_order: Option<usize>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SuiteChecklistViewModel {
    pub rows: Vec<SuiteRowView>,
    pub selected_count: usize,
    pub can_run_selected_batch: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RunPlanStepRowView {
    pub ordinal: usize,
    pub group_id: String,
    pub group_label: String,
    pub suite_id: String,
    pub suite_label: String,
    pub case_id: String,
    pub case_label: String,
    pub step_id: String,
    pub step_label: String,
    pub action_type: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct RunPlanSuiteSectionView {
    pub title: String,
    pub group_id: String,
    pub group_label: String,
    pub suite_id: String,
    pub suite_label: String,
    pub default_open: bool,
    pub steps: Vec<RunPlanStepRowView>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CurrentRunPlanViewModel {
    pub rows: Vec<RunPlanStepRowView>,
    pub suite_sections: Vec<RunPlanSuiteSectionView>,
    pub available_suite_count: usize,
    pub selected_suite_count: usize,
    pub total_step_count: usize,
    pub summary: String,
    pub empty_message: Option<String>,
    pub can_run_selected_batch: bool,
}

pub fn build_current_run_plan_view(model: &SuiteSelectionModel) -> CurrentRunPlanViewModel {
    let available_suite_count = model.available_suite_ids().len();
    let selected_suite_ids = model.selected_suite_ids();
    let selected_suite_count = selected_suite_ids.len();
    let mut rows = Vec::new();
    let mut suite_sections = Vec::new();

    for suite_id in selected_suite_ids {
        let Some((group_index, suite_index)) =
            model.catalog.suite_index_by_id.get(suite_id).copied()
        else {
            continue;
        };
        let Some(group) = model.catalog.groups.get(group_index) else {
            continue;
        };
        let Some(suite) = group.suites.get(suite_index) else {
            continue;
        };
        let mut section_steps = Vec::new();
        for case_item in &suite.cases {
            for step in &case_item.steps {
                let row = RunPlanStepRowView {
                    ordinal: rows.len() + 1,
                    group_id: group.group_id.clone(),
                    group_label: group.label.clone(),
                    suite_id: suite.suite_id.clone(),
                    suite_label: suite.label.clone(),
                    case_id: case_item.case_id.clone(),
                    case_label: case_item.label.clone(),
                    step_id: step.step_id.clone(),
                    step_label: step.label.clone(),
                    action_type: step.action_type.clone(),
                };
                rows.push(row.clone());
                section_steps.push(row);
            }
        }
        suite_sections.push(RunPlanSuiteSectionView {
            title: format!("{} · {}", group.label, suite.label),
            group_id: group.group_id.clone(),
            group_label: group.label.clone(),
            suite_id: suite.suite_id.clone(),
            suite_label: suite.label.clone(),
            default_open: false,
            steps: section_steps,
        });
    }

    let total_step_count = rows.len();
    CurrentRunPlanViewModel {
        rows,
        suite_sections,
        available_suite_count,
        selected_suite_count,
        total_step_count,
        summary: format!(
            "{available_suite_count} suites available · {selected_suite_count} selected · {total_step_count} total steps"
        ),
        empty_message: if selected_suite_count == 0 {
            Some("Select at least one suite".to_string())
        } else {
            None
        },
        can_run_selected_batch: selected_suite_count > 0,
    }
}

pub fn build_suite_checklist_view(model: &SuiteSelectionModel) -> SuiteChecklistViewModel {
    let rows = build_grouped_suite_rows(model);
    let selected_count = rows.iter().filter(|row| row.checked).count();
    SuiteChecklistViewModel {
        rows,
        selected_count,
        can_run_selected_batch: model.can_run_selected_suite(),
    }
}

pub fn build_grouped_suite_rows(model: &SuiteSelectionModel) -> Vec<SuiteRowView> {
    let mut rows = Vec::new();
    for group in &model.catalog.groups {
        for suite in &group.suites {
            let selected_order = model.selected_order_for_suite_id(&suite.suite_id);
            let checked = selected_order.is_some();
            rows.push(SuiteRowView {
                group_id: group.group_id.clone(),
                group_label: group.label.clone(),
                suite_id: suite.suite_id.clone(),
                suite_label: suite.label.clone(),
                speed: suite.speed.clone(),
                risk: suite.risk.clone(),
                default_selected: suite.default_selected,
                preset_groups: suite.preset_groups.clone(),
                destructive: suite.is_destructive(),
                selectable: model.is_suite_selectable(&suite.suite_id),
                disabled_reason: model.disabled_reason_for_suite_id(&suite.suite_id),
                selected: checked,
                checked,
                focused: suite.suite_id == model.selected_suite_id,
                selected_order,
            });
        }
    }

    rows
}

pub fn render_pre_run_surface(model: &SuiteSelectionModel) -> String {
    let mut lines = Vec::new();
    let mut active_group_id = String::new();
    let checklist = build_suite_checklist_view(model);

    lines.push(format!(
        "Selected suites: {}/{}",
        checklist.selected_count,
        checklist.rows.len()
    ));

    for row in checklist.rows {
        if row.group_id != active_group_id {
            active_group_id = row.group_id.clone();
            lines.push(format!("{GROUP_HEADER_PREFIX} {}", row.group_label));
        }

        let marker = if row.checked {
            ROW_SELECTED_PREFIX
        } else {
            ROW_UNSELECTED_PREFIX
        };
        let order = row
            .selected_order
            .map(|value| format!(" #{value}"))
            .unwrap_or_default();
        let focus = if row.focused { " ← current" } else { "" };
        lines.push(format!(
            "{marker} [{}]{} {}{} · speed={} risk={} presets={}",
            row.suite_id,
            order,
            row.suite_label,
            focus,
            row.speed,
            row.risk,
            row.preset_groups.join(",")
        ));
        if let Some(reason) = row.disabled_reason {
            lines.push(format!("  {reason}"));
        }
    }

    if let (Some(group), Some(suite)) = (model.selected_group(), model.selected_suite()) {
        lines.push(String::new());
        lines.push(format!("Selected group: {}", group.label));
        lines.push(format!("Selected suite: {}", suite.label));
        lines.push(format!("Description: {}", suite.description));
        let reset_behavior = if suite.reset_override.trim().is_empty() {
            "Inherit"
        } else {
            suite.reset_override.trim()
        };
        lines.push(format!("Suite reset behavior: {reset_behavior}"));
    }

    lines.push(format!(
        "Global reset default: {}",
        model.global_reset_default.as_str()
    ));
    lines.push(RUN_DISPATCH_READY_MESSAGE.to_string());

    lines.join("\n")
}

pub fn render_review_surface(review: &ReviewSummaryModel) -> String {
    let mut lines = Vec::new();
    lines.push("Review Required".to_string());
    lines.push(format!("Run: {}", review.run_id));
    lines.push(format!(
        "Suite: {} ({})",
        review.suite_label, review.suite_id
    ));
    lines.push(format!("Result: {}", review.run_result));

    if let Some(excerpt) = &review.failure_excerpt {
        lines.push(String::new());
        lines.push("Failure excerpt:".to_string());
        lines.push(format!("- Step: {}", excerpt.step_label));
        lines.push(format!("- Message: {}", excerpt.failure_message));
        if let Some(context_line) = &excerpt.context_line {
            if !context_line.trim().is_empty() {
                lines.push(format!("- Context: {}", context_line.trim()));
            }
        }
    }

    lines.push(String::new());
    lines.push("Actions:".to_string());
    for action in review_actions() {
        lines.push(format!(
            "- {} ({})",
            action.label(),
            action.decision_token()
        ));
    }

    lines.join("\n")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;
    use crate::model::{ReviewFailureExcerpt, ReviewSummaryModel, SuiteSelectionModel};

    #[test]
    fn grouped_rows_follow_catalog_order() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let rows = build_grouped_suite_rows(&model);
        let ids: Vec<&str> = rows.iter().map(|row| row.suite_id.as_str()).collect();
        assert_eq!(
            ids,
            vec![
                "setup-scene-avatar",
                "setup-package-presence",
                "setup-scene-acquisition",
                "setup-window-launch-focus",
                "setup-avatar-discovery-selection",
                "setup-scaffold-add-idempotency",
                "setup-existing-state-recognition",
                "lifecycle-roundtrip",
                "playmode-runtime-validation"
            ]
        );
    }

    #[test]
    fn suite_checklist_view_tracks_checkbox_focus_and_selected_count() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let checklist = build_suite_checklist_view(&model);
        assert_eq!(checklist.selected_count, 4);
        assert!(checklist.can_run_selected_batch);

        let setup = checklist
            .rows
            .iter()
            .find(|row| row.suite_id == "setup-scene-avatar")
            .expect("setup row should exist");
        assert!(setup.checked);
        assert!(setup.focused);
        assert_eq!(setup.selected_order, Some(1));
        assert_eq!(setup.speed, "quick");
        assert_eq!(setup.risk, "safe");
        assert!(setup.default_selected);
        assert!(setup.preset_groups.contains(&"all-setup".to_string()));
        assert!(!setup.destructive);
        assert!(setup.selectable);
        assert_eq!(setup.disabled_reason, None);

        let lifecycle = checklist
            .rows
            .iter()
            .find(|row| row.suite_id == "lifecycle-roundtrip")
            .expect("lifecycle row should exist");
        assert!(lifecycle.checked);
        assert!(!lifecycle.focused);
        assert_eq!(lifecycle.selected_order, Some(3));

        let playmode = checklist
            .rows
            .iter()
            .find(|row| row.suite_id == "playmode-runtime-validation")
            .expect("playmode row should exist");
        assert!(playmode.checked);
        assert!(!playmode.focused);
        assert_eq!(playmode.selected_order, Some(4));
    }

    #[test]
    fn run_plan_setup_suite_includes_expected_step_labels() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let plan = build_current_run_plan_view(&model);
        let labels: Vec<&str> = plan
            .rows
            .iter()
            .map(|row| row.step_label.as_str())
            .collect();

        let available_suite_count = catalog
            .groups
            .iter()
            .map(|group| group.suites.len())
            .sum::<usize>();
        let default_selected_count = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| suite.default_selected)
            .count();
        let default_step_count = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| suite.default_selected)
            .flat_map(|suite| suite.cases.iter())
            .map(|case_item| case_item.steps.len())
            .sum::<usize>();

        assert!(plan.available_suite_count >= 3);
        assert_eq!(plan.available_suite_count, available_suite_count);
        assert_eq!(plan.selected_suite_count, default_selected_count);
        assert_eq!(plan.total_step_count, default_step_count);
        assert_eq!(
            plan.summary,
            format!(
                "{available_suite_count} suites available · {default_selected_count} selected · {default_step_count} total steps"
            )
        );
        assert_eq!(
            &labels[..5],
            &[
                "Open Click ME scene",
                "Open ASM-Lite window",
                "Select Oct25_Dress",
                "Add ASM-Lite prefab",
                "Verify rebuild action is visible"
            ]
        );
        assert!(plan
            .rows
            .iter()
            .any(|row| row.suite_id == "setup-scene-avatar"));
    }

    #[test]
    fn run_plan_lifecycle_keeps_hygiene_cleanup_steps_visible() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("lifecycle-roundtrip")
            .expect("lifecycle should toggle on");

        let plan = build_current_run_plan_view(&model);
        let hygiene_labels: Vec<&str> = plan
            .rows
            .iter()
            .filter(|row| row.action_type == "lifecycle-hygiene-cleanup")
            .map(|row| row.step_label.as_str())
            .collect();

        assert_eq!(plan.total_step_count, 7);
        assert_eq!(
            hygiene_labels,
            vec![
                "Hygiene cleanup after rebuild",
                "Hygiene cleanup after vendorize",
                "Hygiene cleanup after detach"
            ]
        );
    }

    #[test]
    fn run_plan_suite_sections_start_collapsed_with_steps_behind_title() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let plan = build_current_run_plan_view(&model);

        let default_selected_count = catalog
            .groups
            .iter()
            .flat_map(|group| group.suites.iter())
            .filter(|suite| suite.default_selected)
            .count();
        assert_eq!(plan.suite_sections.len(), default_selected_count);
        let section = &plan.suite_sections[0];
        assert_eq!(section.title, "Setup · Setup Scene / Avatar / Prefab");
        assert_eq!(section.suite_id, "setup-scene-avatar");
        assert!(!section.default_open);
        assert_eq!(section.steps.len(), 5);
        assert_eq!(section.steps[0].step_label, "Open Click ME scene");
    }

    #[test]
    fn run_plan_selected_batch_uses_operator_order_and_only_selected_suites() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();
        model
            .toggle_suite_selection_by_id("playmode-runtime-validation")
            .expect("playmode should toggle on");
        model
            .toggle_suite_selection_by_id("setup-scene-avatar")
            .expect("setup should toggle on");
        model
            .toggle_suite_selection_by_id("lifecycle-roundtrip")
            .expect("lifecycle should toggle on");

        let plan = build_current_run_plan_view(&model);
        let mut suite_sequence = Vec::new();
        for row in &plan.rows {
            if suite_sequence
                .last()
                .map(|suite_id: &&str| *suite_id != row.suite_id)
                .unwrap_or(true)
            {
                suite_sequence.push(row.suite_id.as_str());
            }
        }

        assert_eq!(plan.selected_suite_count, 3);
        assert_eq!(plan.total_step_count, 18);
        assert_eq!(
            suite_sequence,
            vec![
                "playmode-runtime-validation",
                "setup-scene-avatar",
                "lifecycle-roundtrip"
            ]
        );
    }

    #[test]
    fn run_plan_empty_selection_explains_empty_state_and_disables_batch() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model.clear_suite_selection();

        let plan = build_current_run_plan_view(&model);

        assert!(plan.rows.is_empty());
        let available_suite_count = catalog
            .groups
            .iter()
            .map(|group| group.suites.len())
            .sum::<usize>();
        assert_eq!(plan.available_suite_count, available_suite_count);
        assert_eq!(plan.selected_suite_count, 0);
        assert_eq!(plan.total_step_count, 0);
        assert_eq!(
            plan.summary,
            format!("{available_suite_count} suites available · 0 selected · 0 total steps")
        );
        assert_eq!(
            plan.empty_message.as_deref(),
            Some("Select at least one suite")
        );
        assert!(!plan.can_run_selected_batch);
    }

    #[test]
    fn render_surface_includes_run_dispatch_message() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let rendered = render_pre_run_surface(&model);
        assert!(rendered.contains(RUN_DISPATCH_READY_MESSAGE));
        assert!(rendered.contains("Selected suite:"));
    }

    #[test]
    fn render_review_surface_includes_failure_excerpt_and_actions() {
        let review = ReviewSummaryModel {
            run_id: "run-0002-playmode-runtime-validation".to_string(),
            suite_id: "playmode-runtime-validation".to_string(),
            suite_label: "Enter / Validate / Exit Playmode".to_string(),
            run_result: "failed".to_string(),
            failure_excerpt: Some(ReviewFailureExcerpt {
                step_label: "Validate runtime component".to_string(),
                failure_message: "Runtime ASM-Lite component missing expected parameter state."
                    .to_string(),
                context_line: Some("step-failed assert-runtime-component-valid".to_string()),
            }),
        };

        let rendered = render_review_surface(&review);
        assert!(rendered.contains("Review Required"));
        assert!(rendered.contains("Failure excerpt:"));
        assert!(rendered.contains("Return to Suite List (return-to-suite-list)"));
        assert!(rendered.contains("Rerun Suite (rerun-suite)"));
        assert!(rendered.contains("Exit (exit)"));
    }
}
