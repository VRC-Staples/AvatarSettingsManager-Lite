use crate::model::{SuiteSelectionModel, RUN_DISPATCH_READY_MESSAGE};
use crate::theme::{GROUP_HEADER_PREFIX, ROW_SELECTED_PREFIX, ROW_UNSELECTED_PREFIX};

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct SuiteRowView {
    pub group_id: String,
    pub group_label: String,
    pub suite_id: String,
    pub suite_label: String,
    pub selected: bool,
}

pub fn build_grouped_suite_rows(model: &SuiteSelectionModel) -> Vec<SuiteRowView> {
    let mut rows = Vec::new();
    for group in &model.catalog.groups {
        for suite in &group.suites {
            rows.push(SuiteRowView {
                group_id: group.group_id.clone(),
                group_label: group.label.clone(),
                suite_id: suite.suite_id.clone(),
                suite_label: suite.label.clone(),
                selected: suite.suite_id == model.selected_suite_id,
            });
        }
    }

    rows
}

pub fn render_pre_run_surface(model: &SuiteSelectionModel) -> String {
    let mut lines = Vec::new();
    let mut active_group_id = String::new();

    for row in build_grouped_suite_rows(model) {
        if row.group_id != active_group_id {
            active_group_id = row.group_id.clone();
            lines.push(format!("{GROUP_HEADER_PREFIX} {}", row.group_label));
        }

        let marker = if row.selected {
            ROW_SELECTED_PREFIX
        } else {
            ROW_UNSELECTED_PREFIX
        };
        lines.push(format!("{marker} [{}] {}", row.suite_id, row.suite_label));
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

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;
    use crate::model::SuiteSelectionModel;

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
                "open-select-add",
                "lifecycle-roundtrip",
                "playmode-runtime-validation"
            ]
        );
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
}
