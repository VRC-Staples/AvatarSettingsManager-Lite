use crate::app::{
    build_suite_model_for_bootstrap, dispatch_abort_run_command, dispatch_review_decision_command,
    dispatch_suite_run_command, export_review_logs, load_review_summary_for_run,
};
use crate::catalog::load_catalog_from_str;
use crate::event_reader::{EventReader, StartupPollResult};
use crate::model::{
    BatchRunQueue, BatchStopReason, OverlayBootstrapConfig, ReviewAction, ReviewSummaryModel,
    SuiteSelectionModel,
};
use crate::session::{
    generate_session_id, write_initial_session_documents, InitialSessionMetadata,
    SmokeHostStateDocument, SmokeSessionPaths, HOST_STATE_CRASHED, HOST_STATE_IDLE,
    HOST_STATE_READY, HOST_STATE_REVIEW_REQUIRED, HOST_STATE_RUNNING, HOST_STATE_STALLED,
};
use crate::theme::{
    overlay_theme_tokens, BODY_TEXT_SIZE, CARD_MARGIN_PX, CARD_RADIUS_PX, CARD_TITLE_TEXT_SIZE,
    META_TEXT_SIZE, OUTER_MARGIN_PX, PRIMARY_BUTTON_HEIGHT_PX, RELATED_GAP_PX,
    RIGHT_PANEL_WIDTH_PX, SECTION_GAP_PX,
};
use crate::ui_suite_list::{
    build_current_run_plan_view, build_filtered_suite_checklist_view, RunPlanStepRowView,
    SuiteChecklistFilter, SuiteRowView, SuiteSpeedFilter,
};
use crate::unity_launcher::{spawn_unity_host, UnityHostLaunchConfig, UnityHostSupervisorStatus};
use eframe::egui;
use egui_phosphor::regular as icons;
use std::fs;
use std::process::Child;
use std::time::{Duration, Instant, SystemTime, UNIX_EPOCH};

const BACKGROUND: egui::Color32 = egui::Color32::from_rgb(6, 11, 20);
const PANEL: egui::Color32 = egui::Color32::from_rgb(12, 22, 38);
const PANEL_RAISED: egui::Color32 = egui::Color32::from_rgb(14, 27, 46);
const CARD_BORDER: egui::Color32 = egui::Color32::from_rgb(35, 52, 77);
const TEXT: egui::Color32 = egui::Color32::from_rgb(232, 238, 248);
const MUTED: egui::Color32 = egui::Color32::from_rgb(168, 182, 204);
const DISABLED_TEXT: egui::Color32 = egui::Color32::from_rgb(105, 121, 145);
const ACCENT: egui::Color32 = egui::Color32::from_rgb(47, 107, 255);
const ACTIVE_BLUE: egui::Color32 = egui::Color32::from_rgb(47, 107, 255);
const WARNING: egui::Color32 = egui::Color32::from_rgb(245, 158, 11);
const SUCCESS: egui::Color32 = egui::Color32::from_rgb(76, 214, 122);
const DANGER: egui::Color32 = egui::Color32::from_rgb(226, 59, 59);
const DANGER_BUTTON_FILL: egui::Color32 = egui::Color32::from_rgb(185, 28, 28);
const RECENT_EVENT_LOG_MAX_HEIGHT_PX: f32 = 240.0;
const RECENT_EVENT_LOG_EXPANDED_HEIGHT_PX: f32 = 360.0;
const ACTION_TILE_WIDTH_PX: f32 = 184.0;
const ACTION_TILE_HEIGHT_PX: f32 = 36.0;

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct AmbientGlowLayer {
    pub x_factor: f32,
    pub y_factor: f32,
    pub radius_factor: f32,
    pub color: egui::Color32,
    pub alpha: u8,
}
const HERO_STATUS_CARD_WIDTH_PX: f32 = 192.0;
const HERO_STATUS_ICON_SIZE_PX: f32 = 28.0;
const HERO_TITLE_TEXT_SIZE_PX: f32 = 22.0;
const HERO_CARD_PADDING_PX: f32 = 10.0;
#[cfg(test)]
const PHASE_PRIMARY_ACTION_TILE_WIDTH_PX: f32 = 224.0;
#[cfg(test)]
const PHASE_PRIMARY_ACTION_TILE_HEIGHT_PX: f32 = 52.0;

pub fn run_overlay_window(config: OverlayBootstrapConfig) -> Result<(), String> {
    let catalog_raw = fs::read_to_string(&config.catalog_path).map_err(|error| {
        format!(
            "failed to read catalog '{}': {error}",
            config.catalog_path.display()
        )
    })?;
    let catalog = load_catalog_from_str(&catalog_raw)
        .map_err(|error| format!("catalog validation failed: {error}"))?;
    let suite_model = build_suite_model_for_bootstrap(&catalog, &config)?;
    let session_paths = SmokeSessionPaths::new(config.session_root.clone())
        .map_err(|error| format!("session root initialization failed: {error}"))?;
    session_paths
        .ensure_layout()
        .map_err(|error| format!("session layout initialization failed: {error}"))?;

    let options = native_options();
    eframe::run_native(
        "ASM-Lite Smoke Overlay",
        options,
        Box::new(|_cc| {
            Ok(Box::new(SmokeOverlayApp::new(
                config,
                suite_model,
                session_paths,
            )))
        }),
    )
    .map_err(|error| format!("failed to run native overlay window: {error}"))
}

pub fn native_options() -> eframe::NativeOptions {
    eframe::NativeOptions {
        viewport: egui::ViewportBuilder::default()
            .with_inner_size([RIGHT_PANEL_WIDTH_PX, 760.0])
            .with_min_inner_size([RIGHT_PANEL_WIDTH_PX, 560.0])
            .with_always_on_top()
            .with_decorations(true)
            .with_maximized(false)
            .with_fullscreen(false)
            .with_resizable(true),
        ..Default::default()
    }
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct StartupWindowPlacement {
    pub position: egui::Pos2,
    pub inner_size: egui::Vec2,
    pub maximized: bool,
    pub fullscreen: bool,
    pub decorated: bool,
}

pub fn startup_window_placement(
    monitor_size: egui::Vec2,
    frame_chrome_size: egui::Vec2,
) -> Option<StartupWindowPlacement> {
    if monitor_size.x <= RIGHT_PANEL_WIDTH_PX || monitor_size.y <= 1.0 {
        return None;
    }

    let chrome_width = frame_chrome_size
        .x
        .clamp(0.0, monitor_size.x - RIGHT_PANEL_WIDTH_PX);
    let chrome_height = frame_chrome_size.y.clamp(0.0, monitor_size.y - 1.0);
    let outer_width = RIGHT_PANEL_WIDTH_PX + chrome_width;
    let inner_height = (monitor_size.y - chrome_height).max(1.0);

    Some(StartupWindowPlacement {
        position: egui::pos2((monitor_size.x - outer_width).max(0.0), 0.0),
        inner_size: egui::vec2(RIGHT_PANEL_WIDTH_PX, inner_height),
        maximized: false,
        fullscreen: false,
        decorated: true,
    })
}

#[cfg(test)]
mod window_placement_tests {
    use super::*;

    #[test]
    fn startup_window_placement_uses_right_edge_and_full_monitor_height_without_maximizing() {
        let placement = startup_window_placement(egui::vec2(1920.0, 1080.0), egui::Vec2::ZERO)
            .expect("monitor dimensions should produce startup placement");

        assert_eq!(
            placement.position,
            egui::pos2(1920.0 - RIGHT_PANEL_WIDTH_PX, 0.0)
        );
        assert_eq!(
            placement.inner_size,
            egui::vec2(RIGHT_PANEL_WIDTH_PX, 1080.0)
        );
        assert!(!placement.maximized);
        assert!(!placement.fullscreen);
        assert!(placement.decorated);
    }

    #[test]
    fn startup_window_placement_accounts_for_window_chrome() {
        let placement =
            startup_window_placement(egui::vec2(1920.0, 1080.0), egui::vec2(16.0, 39.0))
                .expect("monitor dimensions should produce startup placement");

        assert_eq!(
            placement.position,
            egui::pos2(1920.0 - RIGHT_PANEL_WIDTH_PX - 16.0, 0.0)
        );
        assert_eq!(
            placement.inner_size,
            egui::vec2(RIGHT_PANEL_WIDTH_PX, 1041.0)
        );
    }
}

struct SmokeOverlayApp {
    config: OverlayBootstrapConfig,
    model: SuiteSelectionModel,
    session_paths: SmokeSessionPaths,
    reader: EventReader,
    child: Option<Child>,
    launched_at: Option<Instant>,
    latest_poll: Option<StartupPollResult>,
    status_message: String,
    command_error: Option<String>,
    pending_command_id: Option<String>,
    pending_command_seq: Option<i32>,
    batch_queue: Option<BatchRunQueue>,
    suite_filter_query: String,
    suite_speed_filter: Option<SuiteSpeedFilter>,
    startup_window_placement_applied: bool,
    icon_font_registered: bool,
    last_operator_phase: Option<OperatorPhase>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct OverlayControlState {
    pub can_launch: bool,
    pub can_select_suite: bool,
    pub can_run_suite: bool,
    pub can_abort: bool,
    pub can_review: bool,
    pub can_exit: bool,
    pub can_relaunch: bool,
    pub can_edit_step_sleep_timer: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OperatorPhase {
    NotLaunched,
    Starting,
    Ready,
    Running,
    ReviewRequired,
    HostError,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum StatusTone {
    Neutral,
    Accent,
    Warning,
    Success,
    Danger,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum DashboardSectionTone {
    Gold,
    Cream,
    Green,
    Orange,
    Blue,
    Lavender,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct StatusVisualModel {
    pub icon: &'static str,
    pub headline: &'static str,
    pub tone: StatusTone,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ControlActionId {
    LaunchHost,
    RunSelectedSuite,
    AbortRun,
    ExportLogs,
    ReturnToSuiteList,
    RerunSuite,
    Exit,
    RelaunchHost,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ControlActionRole {
    Primary,
    Secondary,
    Destructive,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ControlActionModel {
    pub id: ControlActionId,
    pub label: String,
    pub role: ControlActionRole,
    pub enabled: bool,
    pub disabled_reason: Option<&'static str>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ControlActionEmphasis {
    PhasePrimary,
    DecisionPeer,
    Standard,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PhaseControlActionModel {
    pub action: ControlActionModel,
    pub emphasis: ControlActionEmphasis,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct ActionButtonStyle {
    pub fill: egui::Color32,
    pub stroke: egui::Stroke,
    pub text_color: egui::Color32,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SectionSubheaderVisualSpec {
    pub bold_text: bool,
    pub uses_bubble_frame: bool,
    pub uses_background_fill: bool,
    pub text_color: egui::Color32,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SuitePickerTypographySpec {
    pub summary_text_color: egui::Color32,
    pub summary_text_size_px: f32,
    pub group_header_text_color: egui::Color32,
    pub group_header_text_size_px: f32,
    pub group_header_bold: bool,
    pub group_header_uses_collapsing_header: bool,
    pub row_text_color: egui::Color32,
    pub row_text_size_px: f32,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct SuiteCheckboxVisualSpec {
    pub icon: &'static str,
    pub icon_color: egui::Color32,
    pub text_color: egui::Color32,
    pub row_fill: egui::Color32,
    pub row_stroke: egui::Stroke,
    pub icon_size_px: f32,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct InfoCalloutVisualSpec {
    pub icon: &'static str,
    pub icon_color: egui::Color32,
    pub text_color: egui::Color32,
    pub icon_size_px: f32,
    pub text_size_px: f32,
    pub row_vertical_align: egui::Align,
    pub horizontal_wrapped: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CurrentSuiteInfoPresentation {
    FramedCallout,
    InlineMuted,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct CurrentRunPlanStepVisualSpec {
    pub ordinal_bold: bool,
    pub ordinal_uses_bubble_frame: bool,
    pub separator_icon: &'static str,
    pub renders_trailing_step_index: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct FooterStatusModel {
    pub host_state: Option<String>,
    pub event_count: usize,
    pub pending_command: Option<String>,
    pub selected_suite_count: usize,
    pub hidden_by_filter_count: usize,
    pub batch_progress: Option<String>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ReferenceDashboardComponentId {
    HeroHeader,
    LiveStatusCard,
    CurrentRunPlanCard,
    SuitesTreeCard,
    CurrentSuiteBriefingCard,
    UtilitiesCard,
    ActionsDock,
    FooterStatusStrip,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct HeaderVisualModel {
    pub title: &'static str,
    pub subtitle: &'static str,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StatusCardViewModel {
    pub icon: &'static str,
    pub headline: &'static str,
    pub phase_label: &'static str,
    pub tone: StatusTone,
    pub subtext: String,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ActionDockLayoutModel {
    pub primary_ids: Vec<ControlActionId>,
    pub decision_peer_ids: Vec<ControlActionId>,
    pub secondary_ids: Vec<ControlActionId>,
    pub destructive_ids: Vec<ControlActionId>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ActionDockShellSpec {
    pub fixed_bottom_panel: bool,
    pub above_footer_status_strip: bool,
    pub inside_main_scroll_area: bool,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum CurrentSuiteStepStatus {
    Pending,
    Running,
    Passed,
    Failed,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CurrentSuiteStepModel {
    pub step_id: String,
    pub label: String,
    pub status: CurrentSuiteStepStatus,
    pub icon: &'static str,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CurrentSuiteBriefingModel {
    pub suite_id: String,
    pub suite_icon: &'static str,
    pub title: String,
    pub description: String,
    pub expected_outcomes: Vec<String>,
    pub steps: Vec<CurrentSuiteStepModel>,
    pub info_note: String,
    pub debug_hint: String,
}

pub fn reference_dashboard_component_stack() -> Vec<ReferenceDashboardComponentId> {
    vec![
        ReferenceDashboardComponentId::HeroHeader,
        ReferenceDashboardComponentId::LiveStatusCard,
        ReferenceDashboardComponentId::CurrentRunPlanCard,
        ReferenceDashboardComponentId::SuitesTreeCard,
        ReferenceDashboardComponentId::CurrentSuiteBriefingCard,
        ReferenceDashboardComponentId::UtilitiesCard,
        ReferenceDashboardComponentId::ActionsDock,
        ReferenceDashboardComponentId::FooterStatusStrip,
    ]
}

pub fn reference_header_model() -> HeaderVisualModel {
    HeaderVisualModel {
        title: "ASM-Lite Smoke Overlay",
        subtitle: "operator dashboard for visible Unity UAT smoke suites",
    }
}

pub fn status_card_view_model(label: &str, message: &str) -> StatusCardViewModel {
    let visual = status_visual_model(label);
    StatusCardViewModel {
        icon: visual.icon,
        headline: visual.headline,
        phase_label: status_phase_label(label),
        tone: visual.tone,
        subtext: message.trim().to_string(),
    }
}

pub fn action_dock_layout_for_phase(
    phase: OperatorPhase,
    controls: OverlayControlState,
    selected_suite_count: usize,
) -> ActionDockLayoutModel {
    let actions =
        phase_control_action_models_for_selected_batch(phase, controls, selected_suite_count);
    let visible_action_ids = phase_visible_action_ids(phase);
    ActionDockLayoutModel {
        primary_ids: actions
            .iter()
            .filter(|action| {
                action.emphasis == ControlActionEmphasis::PhasePrimary
                    && visible_action_ids.contains(&action.action.id)
            })
            .map(|action| action.action.id)
            .collect(),
        decision_peer_ids: actions
            .iter()
            .filter(|action| {
                action.emphasis == ControlActionEmphasis::DecisionPeer
                    && visible_action_ids.contains(&action.action.id)
            })
            .map(|action| action.action.id)
            .collect(),
        secondary_ids: actions
            .iter()
            .filter(|action| {
                action.emphasis == ControlActionEmphasis::Standard
                    && action.action.role != ControlActionRole::Destructive
                    && visible_action_ids.contains(&action.action.id)
            })
            .map(|action| action.action.id)
            .collect(),
        destructive_ids: actions
            .iter()
            .filter(|action| {
                action.emphasis == ControlActionEmphasis::Standard
                    && action.action.id == ControlActionId::Exit
                    && visible_action_ids.contains(&action.action.id)
            })
            .map(|action| action.action.id)
            .collect(),
    }
}

pub fn action_dock_shell_spec() -> ActionDockShellSpec {
    ActionDockShellSpec {
        fixed_bottom_panel: true,
        above_footer_status_strip: true,
        inside_main_scroll_area: false,
    }
}

pub fn build_current_suite_briefing_model(
    model: &SuiteSelectionModel,
) -> Option<CurrentSuiteBriefingModel> {
    build_current_suite_briefing_model_for_poll(model, None)
}

pub fn build_current_suite_briefing_model_for_poll(
    model: &SuiteSelectionModel,
    poll: Option<&StartupPollResult>,
) -> Option<CurrentSuiteBriefingModel> {
    let suite = model.selected_suite()?;
    let mut expected_outcomes = Vec::new();
    if !suite.expected_outcome.trim().is_empty() {
        expected_outcomes.push(suite.expected_outcome.trim().to_string());
    }
    if !suite.reset_override.trim().is_empty() {
        expected_outcomes.push(format!("Reset policy: {}", suite.reset_override.trim()));
    }
    let has_hygiene = suite.cases.iter().any(|case_item| {
        case_item
            .steps
            .iter()
            .any(|step| step.action_type == "lifecycle-hygiene-cleanup")
    });
    if has_hygiene {
        expected_outcomes.push(
            "Visible hygiene cleanup steps remain in the operator-reviewed lifecycle flow."
                .to_string(),
        );
    }

    let steps = suite
        .cases
        .iter()
        .flat_map(|case_item| case_item.steps.iter())
        .map(|step| {
            let status = current_suite_step_status(&suite.suite_id, &step.step_id, poll);
            CurrentSuiteStepModel {
                step_id: step.step_id.clone(),
                label: step.label.clone(),
                status,
                icon: current_suite_step_icon(status),
            }
        })
        .collect();

    Some(CurrentSuiteBriefingModel {
        suite_id: suite.suite_id.clone(),
        suite_icon: current_suite_badge_text(&suite.suite_id),
        title: suite.label.clone(),
        description: suite.description.clone(),
        expected_outcomes,
        steps,
        info_note: current_suite_info_note().to_string(),
        debug_hint: suite.debug_hint.clone(),
    })
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct HeroProminenceSpec {
    pub title_text_size_px: f32,
    pub status_card_width_px: f32,
    pub status_icon_size_px: f32,
    pub hero_card_padding_px: f32,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct HeroStatusLayoutSpec {
    pub card_count: usize,
    pub inter_card_gap_px: f32,
    pub header_card_full_width: bool,
    pub status_card_full_width: bool,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct OperatorDensitySpec {
    pub panel_width_px: f32,
    pub outer_margin_px: f32,
    pub card_margin_px: f32,
    pub section_gap_px: f32,
    pub primary_button_height_px: f32,
}

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct FullWidthCardLayout {
    pub outer_width_px: f32,
    pub horizontal_inner_margin_px: f32,
    pub content_min_width_px: f32,
}

pub fn full_width_card_layout(
    available_width_px: f32,
    horizontal_inner_margin_px: f32,
) -> FullWidthCardLayout {
    let safe_available_width = available_width_px.max(0.0);
    let safe_horizontal_inner_margin = horizontal_inner_margin_px.max(0.0);
    FullWidthCardLayout {
        outer_width_px: safe_available_width,
        horizontal_inner_margin_px: safe_horizontal_inner_margin,
        content_min_width_px: (safe_available_width - (safe_horizontal_inner_margin * 2.0))
            .max(0.0),
    }
}

pub fn hero_prominence_spec() -> HeroProminenceSpec {
    HeroProminenceSpec {
        title_text_size_px: HERO_TITLE_TEXT_SIZE_PX,
        status_card_width_px: HERO_STATUS_CARD_WIDTH_PX,
        status_icon_size_px: HERO_STATUS_ICON_SIZE_PX,
        hero_card_padding_px: HERO_CARD_PADDING_PX,
    }
}

pub fn hero_status_layout_spec() -> HeroStatusLayoutSpec {
    HeroStatusLayoutSpec {
        card_count: 2,
        inter_card_gap_px: RELATED_GAP_PX,
        header_card_full_width: true,
        status_card_full_width: true,
    }
}

pub fn suite_group_header_text(group_label: &str, group_count: usize) -> String {
    format!(
        "{} · {} suite{}",
        group_label,
        group_count,
        if group_count == 1 { "" } else { "s" }
    )
}

pub fn suite_group_dropdown_icon(open: bool) -> &'static str {
    if open {
        icons::CARET_DOWN
    } else {
        icons::CARET_RIGHT
    }
}

pub fn suite_row_indent_marker() -> &'static str {
    icons::DOT_OUTLINE
}

const CURRENT_SUITE_SETUP_ICON: &str = icons::CUBE;
const CURRENT_SUITE_LIFECYCLE_ICON: &str = icons::ARROWS_CLOCKWISE;
const CURRENT_SUITE_PLAYMODE_ICON: &str = icons::MONITOR_PLAY;

pub fn current_suite_badge_text(suite_id: &str) -> &'static str {
    match suite_id {
        "setup-scene-avatar" => CURRENT_SUITE_SETUP_ICON,
        "lifecycle-roundtrip" => CURRENT_SUITE_LIFECYCLE_ICON,
        "playmode-runtime-validation" => CURRENT_SUITE_PLAYMODE_ICON,
        _ => icons::DIAMOND,
    }
}

fn current_suite_step_status(
    suite_id: &str,
    step_id: &str,
    poll: Option<&StartupPollResult>,
) -> CurrentSuiteStepStatus {
    let Some(poll) = poll else {
        return CurrentSuiteStepStatus::Pending;
    };
    let active_run_id = poll
        .host_state
        .as_ref()
        .map(|host_state| host_state.active_run_id.trim())
        .filter(|active_run_id| !active_run_id.is_empty());
    let mut status = CurrentSuiteStepStatus::Pending;
    let mut suite_passed = false;
    for event in &poll.events {
        if event.suite_id != suite_id {
            continue;
        }
        if let Some(active_run_id) = active_run_id {
            if event.run_id != active_run_id {
                continue;
            }
        }
        match event.event_type.as_str() {
            "suite-passed" => suite_passed = true,
            "step-started" if event.step_id == step_id => status = CurrentSuiteStepStatus::Running,
            "step-passed" if event.step_id == step_id => status = CurrentSuiteStepStatus::Passed,
            "step-failed" if event.step_id == step_id => status = CurrentSuiteStepStatus::Failed,
            _ => {}
        }
    }
    if suite_passed && status != CurrentSuiteStepStatus::Failed {
        CurrentSuiteStepStatus::Passed
    } else {
        status
    }
}

pub fn current_suite_step_icon(status: CurrentSuiteStepStatus) -> &'static str {
    match status {
        CurrentSuiteStepStatus::Pending => icons::DOT_OUTLINE,
        CurrentSuiteStepStatus::Running => icons::SPINNER_GAP,
        CurrentSuiteStepStatus::Passed => icons::CHECK_CIRCLE,
        CurrentSuiteStepStatus::Failed => icons::X_CIRCLE,
    }
}

pub fn current_suite_step_color(status: CurrentSuiteStepStatus) -> egui::Color32 {
    match status {
        CurrentSuiteStepStatus::Pending => MUTED,
        CurrentSuiteStepStatus::Running => ACCENT,
        CurrentSuiteStepStatus::Passed => SUCCESS,
        CurrentSuiteStepStatus::Failed => DANGER,
    }
}

pub fn current_suite_steps_section_label() -> &'static str {
    "STEPS"
}

pub fn current_suite_step_label(step: &CurrentSuiteStepModel) -> String {
    format!("{} {}", step.icon, step.label)
}

pub fn current_run_plan_step_visual_spec() -> CurrentRunPlanStepVisualSpec {
    CurrentRunPlanStepVisualSpec {
        ordinal_bold: true,
        ordinal_uses_bubble_frame: false,
        separator_icon: icons::CARET_RIGHT,
        renders_trailing_step_index: false,
    }
}

pub fn current_run_plan_step_label(
    step_label: &str,
    _ordinal: usize,
    _suite_step_index: usize,
) -> String {
    step_label.trim().to_string()
}

pub fn operator_density_spec() -> OperatorDensitySpec {
    OperatorDensitySpec {
        panel_width_px: RIGHT_PANEL_WIDTH_PX,
        outer_margin_px: OUTER_MARGIN_PX,
        card_margin_px: CARD_MARGIN_PX,
        section_gap_px: SECTION_GAP_PX,
        primary_button_height_px: PRIMARY_BUTTON_HEIGHT_PX,
    }
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum OverlaySectionId {
    HeaderStatus,
    SuitePicker,
    CurrentRunPlan,
    CurrentSuite,
    Controls,
    RunMonitor,
    FooterStatus,
}

pub fn overlay_section_order() -> Vec<OverlaySectionId> {
    vec![
        OverlaySectionId::HeaderStatus,
        OverlaySectionId::CurrentRunPlan,
        OverlaySectionId::SuitePicker,
        OverlaySectionId::CurrentSuite,
        OverlaySectionId::RunMonitor,
        OverlaySectionId::Controls,
        OverlaySectionId::FooterStatus,
    ]
}

pub fn overlay_section_order_for_phase(phase: OperatorPhase) -> Vec<OverlaySectionId> {
    if matches!(phase, OperatorPhase::Running) {
        vec![
            OverlaySectionId::HeaderStatus,
            OverlaySectionId::CurrentSuite,
            OverlaySectionId::RunMonitor,
            OverlaySectionId::CurrentRunPlan,
            OverlaySectionId::SuitePicker,
            OverlaySectionId::Controls,
            OverlaySectionId::FooterStatus,
        ]
    } else {
        overlay_section_order()
    }
}

fn run_selected_action_label() -> &'static str {
    "Run Selected"
}

fn rerun_from_first_selected_action_label() -> &'static str {
    "Rerun from First Selected"
}

fn utilities_section_title() -> &'static str {
    "Utilities / Advanced"
}

fn review_evidence_title() -> &'static str {
    "Review Evidence"
}

fn run_monitor_title() -> &'static str {
    "Run Monitor"
}

fn recent_events_section_title() -> &'static str {
    "Recent Events"
}

fn destructive_drills_helper_copy(enabled: bool) -> &'static str {
    if enabled {
        "Destructive drills still require a confirm before they run."
    } else {
        "Destructive drills stay off until you explicitly enable them."
    }
}

fn destructive_drills_helper_color(enabled: bool) -> egui::Color32 {
    if enabled {
        WARNING
    } else {
        MUTED
    }
}

fn debug_hint_section_title() -> &'static str {
    "Debug hint"
}

fn current_suite_empty_state_copy() -> &'static str {
    "Select a suite to preview its steps here."
}

fn suite_filter_empty_state_copy() -> &'static str {
    "No suites match the current filters. Clear search or change speed filters."
}

fn recent_event_log_empty_state_copy() -> &'static str {
    "No events yet. Run a suite to stream activity here."
}

fn current_suite_info_note() -> &'static str {
    ""
}

fn review_followup_copy() -> &'static str {
    "Inspect Unity, then rerun from the first selected suite or return to the suite list."
}

fn review_failure_excerpt_color() -> egui::Color32 {
    tone_color(StatusTone::Warning)
}

fn run_monitor_review_heading(review: &ReviewSummaryModel) -> String {
    format!(
        "{} · {}",
        review.run_result.to_ascii_uppercase(),
        review.suite_label
    )
}

fn run_monitor_failure_label(step_label: &str) -> String {
    format!("Failed step: {step_label}")
}

fn recent_event_log_default_open(phase: OperatorPhase) -> bool {
    matches!(
        phase,
        OperatorPhase::Running | OperatorPhase::ReviewRequired | OperatorPhase::HostError
    )
}

fn recent_event_log_forced_open(phase: OperatorPhase) -> Option<bool> {
    if recent_event_log_default_open(phase) {
        Some(true)
    } else {
        None
    }
}

fn brief_timestamp_label(timestamp_utc: &str) -> Option<String> {
    let trimmed = timestamp_utc.trim();
    let time_portion = trimmed.split('T').nth(1)?;
    let brief = time_portion.get(..8)?;
    if brief.chars().enumerate().all(|(index, ch)| match index {
        2 | 5 => ch == ':',
        _ => ch.is_ascii_digit(),
    }) {
        Some(brief.to_string())
    } else {
        None
    }
}

fn current_system_time_brief() -> String {
    brief_timestamp_label(&current_utc_rfc3339()).unwrap_or_else(|| "00:00:00".to_string())
}

fn format_recent_event_log_entry(event: &crate::protocol::SmokeProtocolEvent) -> String {
    let timestamp =
        brief_timestamp_label(&event.timestamp_utc).unwrap_or_else(current_system_time_brief);
    format!("{timestamp} {} — {}", event.event_type, event.message)
}

fn batch_progress_copy(current_ordinal: usize, selected_count: usize) -> Option<String> {
    if selected_count == 0 {
        return None;
    }

    Some(format!(
        "{} of {}",
        current_ordinal.clamp(1, selected_count),
        selected_count
    ))
}

pub fn dashboard_card_collapsed_for_phase(phase: OperatorPhase, section: OverlaySectionId) -> bool {
    matches!(phase, OperatorPhase::Running)
        && matches!(
            section,
            OverlaySectionId::CurrentRunPlan | OverlaySectionId::SuitePicker
        )
}

pub fn status_visual_model(label: &str) -> StatusVisualModel {
    match label {
        HOST_STATE_READY | HOST_STATE_IDLE => StatusVisualModel {
            icon: icons::CHECK_CIRCLE,
            headline: "READY",
            tone: StatusTone::Success,
        },
        HOST_STATE_REVIEW_REQUIRED => StatusVisualModel {
            icon: icons::WARNING_CIRCLE,
            headline: "REVIEW REQUIRED",
            tone: StatusTone::Warning,
        },
        HOST_STATE_RUNNING => StatusVisualModel {
            icon: icons::SPINNER_GAP,
            headline: "RUNNING",
            tone: StatusTone::Accent,
        },
        HOST_STATE_CRASHED => StatusVisualModel {
            icon: icons::X_CIRCLE,
            headline: "CRASHED",
            tone: StatusTone::Danger,
        },
        HOST_STATE_STALLED => StatusVisualModel {
            icon: icons::WARNING_OCTAGON,
            headline: "STALLED",
            tone: StatusTone::Danger,
        },
        "timed-out" => StatusVisualModel {
            icon: icons::CLOCK_COUNTDOWN,
            headline: "TIMED OUT",
            tone: StatusTone::Danger,
        },
        "starting" => StatusVisualModel {
            icon: icons::SPINNER_GAP,
            headline: "STARTING",
            tone: StatusTone::Accent,
        },
        _ => StatusVisualModel {
            icon: icons::CIRCLE,
            headline: "NOT LAUNCHED",
            tone: StatusTone::Neutral,
        },
    }
}

fn status_phase_label(label: &str) -> &'static str {
    match label {
        HOST_STATE_READY | HOST_STATE_IDLE => "Unity host idle",
        HOST_STATE_REVIEW_REQUIRED => "Suite needs review",
        HOST_STATE_RUNNING => "Batch in progress",
        HOST_STATE_CRASHED | HOST_STATE_STALLED => "Unity host issue",
        "timed-out" => "Startup issue",
        "starting" => "Launching host",
        _ => "Awaiting launch",
    }
}

pub fn control_action_models(controls: OverlayControlState) -> Vec<ControlActionModel> {
    control_action_models_for_selected_batch(controls, 1)
}

pub fn control_action_models_for_selected_batch(
    mut controls: OverlayControlState,
    selected_suite_count: usize,
) -> Vec<ControlActionModel> {
    if selected_suite_count == 0 {
        controls.can_run_suite = false;
    }
    vec![
        ControlActionModel {
            id: ControlActionId::LaunchHost,
            label: "Launch Unity Host".to_string(),
            role: ControlActionRole::Primary,
            enabled: controls.can_launch,
            disabled_reason: disabled_reason(
                controls.can_launch,
                "Unity host is already launched or a command is pending.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::RunSelectedSuite,
            label: run_selected_action_label().to_string(),
            role: ControlActionRole::Primary,
            enabled: controls.can_run_suite,
            disabled_reason: disabled_reason(
                controls.can_run_suite,
                "Run is available when the Unity host is ready and no command is pending.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::AbortRun,
            label: "Abort Run".to_string(),
            role: ControlActionRole::Destructive,
            enabled: controls.can_abort,
            disabled_reason: disabled_reason(
                controls.can_abort,
                "Abort is available only while a suite is running.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::ExportLogs,
            label: "Export Logs".to_string(),
            role: ControlActionRole::Secondary,
            enabled: controls.can_review,
            disabled_reason: disabled_reason(
                controls.can_review,
                "Log export is available after a failed or aborted suite.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::RerunSuite,
            label: rerun_from_first_selected_action_label().to_string(),
            role: ControlActionRole::Primary,
            enabled: controls.can_review,
            disabled_reason: disabled_reason(
                controls.can_review,
                "Review decisions are available after a failed or aborted suite.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::ReturnToSuiteList,
            label: "Return to Suite List".to_string(),
            role: ControlActionRole::Secondary,
            enabled: controls.can_review,
            disabled_reason: disabled_reason(
                controls.can_review,
                "Review decisions are available after a failed or aborted suite.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::Exit,
            label: "Exit (no save)".to_string(),
            role: ControlActionRole::Destructive,
            enabled: controls.can_exit,
            disabled_reason: disabled_reason(
                controls.can_exit,
                "Exit is always available from the overlay.",
            ),
        },
        ControlActionModel {
            id: ControlActionId::RelaunchHost,
            label: "Relaunch Host".to_string(),
            role: ControlActionRole::Primary,
            enabled: controls.can_relaunch,
            disabled_reason: disabled_reason(
                controls.can_relaunch,
                "Relaunch is available after a host crash or stall.",
            ),
        },
    ]
}

pub fn phase_control_action_models(
    phase: OperatorPhase,
    controls: OverlayControlState,
) -> Vec<PhaseControlActionModel> {
    phase_control_action_models_for_selected_batch(phase, controls, 1)
}

pub fn phase_control_action_models_for_selected_batch(
    phase: OperatorPhase,
    controls: OverlayControlState,
    selected_suite_count: usize,
) -> Vec<PhaseControlActionModel> {
    let primary_id = phase_primary_action_id(phase);
    let decision_peer_ids = phase_decision_peer_action_ids(phase);
    control_action_models_for_selected_batch(controls, selected_suite_count)
        .into_iter()
        .map(|action| {
            let emphasis = if Some(action.id) == primary_id {
                ControlActionEmphasis::PhasePrimary
            } else if decision_peer_ids.iter().any(|id| *id == action.id) {
                ControlActionEmphasis::DecisionPeer
            } else {
                ControlActionEmphasis::Standard
            };
            PhaseControlActionModel { action, emphasis }
        })
        .collect()
}

fn phase_primary_action_id(phase: OperatorPhase) -> Option<ControlActionId> {
    match phase {
        OperatorPhase::NotLaunched => Some(ControlActionId::LaunchHost),
        OperatorPhase::Ready => Some(ControlActionId::RunSelectedSuite),
        OperatorPhase::Running => Some(ControlActionId::AbortRun),
        OperatorPhase::ReviewRequired => Some(ControlActionId::RerunSuite),
        OperatorPhase::HostError => Some(ControlActionId::RelaunchHost),
        OperatorPhase::Starting => None,
    }
}

fn phase_decision_peer_action_ids(phase: OperatorPhase) -> Vec<ControlActionId> {
    match phase {
        OperatorPhase::ReviewRequired => vec![
            ControlActionId::ExportLogs,
            ControlActionId::ReturnToSuiteList,
        ],
        _ => Vec::new(),
    }
}

fn phase_visible_action_ids(phase: OperatorPhase) -> Vec<ControlActionId> {
    match phase {
        OperatorPhase::NotLaunched => vec![ControlActionId::LaunchHost, ControlActionId::Exit],
        OperatorPhase::Starting => vec![ControlActionId::Exit],
        OperatorPhase::Ready => vec![ControlActionId::RunSelectedSuite, ControlActionId::Exit],
        OperatorPhase::Running => vec![ControlActionId::AbortRun, ControlActionId::Exit],
        OperatorPhase::ReviewRequired => vec![
            ControlActionId::ExportLogs,
            ControlActionId::RerunSuite,
            ControlActionId::ReturnToSuiteList,
            ControlActionId::Exit,
        ],
        OperatorPhase::HostError => vec![ControlActionId::RelaunchHost, ControlActionId::Exit],
    }
}

pub fn phase_primary_action_ids(
    phase: OperatorPhase,
    controls: OverlayControlState,
) -> Vec<ControlActionId> {
    phase_control_action_models(phase, controls)
        .into_iter()
        .filter(|action| action.emphasis == ControlActionEmphasis::PhasePrimary)
        .map(|action| action.action.id)
        .collect()
}

fn disabled_reason(enabled: bool, reason: &'static str) -> Option<&'static str> {
    if enabled {
        None
    } else {
        Some(reason)
    }
}

pub fn control_state_for_host_state(
    host_state: Option<&SmokeHostStateDocument>,
    supervisor_status: Option<UnityHostSupervisorStatus>,
    command_pending: bool,
    launch_in_progress: bool,
) -> OverlayControlState {
    let host_token = host_state.map(|state| state.state.as_str()).unwrap_or("");
    let crashed_or_stalled = matches!(
        supervisor_status,
        Some(UnityHostSupervisorStatus::Crashed) | Some(UnityHostSupervisorStatus::Stalled)
    ) || matches!(host_token, HOST_STATE_CRASHED | HOST_STATE_STALLED);
    let ready_for_suite =
        matches!(host_token, HOST_STATE_READY | HOST_STATE_IDLE) && !command_pending;
    let running = host_token == HOST_STATE_RUNNING && !command_pending;
    let review = host_token == HOST_STATE_REVIEW_REQUIRED && !command_pending;

    OverlayControlState {
        can_launch: host_state.is_none() && !command_pending && !launch_in_progress,
        can_select_suite: ready_for_suite,
        can_run_suite: ready_for_suite,
        can_abort: running,
        can_review: review,
        can_exit: true,
        can_relaunch: crashed_or_stalled && !command_pending,
        can_edit_step_sleep_timer: !running && !command_pending,
    }
}

pub fn control_state_for_suite_selection(
    mut controls: OverlayControlState,
    model: &SuiteSelectionModel,
) -> OverlayControlState {
    if !model.can_run_selected_suite() {
        controls.can_run_suite = false;
    }
    controls
}

pub fn operator_phase_for_host_state(
    host_state: Option<&SmokeHostStateDocument>,
    supervisor_status: Option<UnityHostSupervisorStatus>,
    command_pending: bool,
    launch_in_progress: bool,
) -> OperatorPhase {
    if let Some(host_state) = host_state {
        return match host_state.state.as_str() {
            HOST_STATE_READY | HOST_STATE_IDLE => OperatorPhase::Ready,
            HOST_STATE_RUNNING => OperatorPhase::Running,
            HOST_STATE_REVIEW_REQUIRED => OperatorPhase::ReviewRequired,
            HOST_STATE_CRASHED | HOST_STATE_STALLED => OperatorPhase::HostError,
            _ => OperatorPhase::HostError,
        };
    }

    match supervisor_status {
        Some(UnityHostSupervisorStatus::Starting) => {
            if command_pending || launch_in_progress {
                OperatorPhase::Starting
            } else {
                OperatorPhase::NotLaunched
            }
        }
        Some(UnityHostSupervisorStatus::TimedOut)
        | Some(UnityHostSupervisorStatus::Crashed)
        | Some(UnityHostSupervisorStatus::Stalled)
        | Some(UnityHostSupervisorStatus::ExitedWithError) => OperatorPhase::HostError,
        Some(UnityHostSupervisorStatus::Ready) | Some(UnityHostSupervisorStatus::ExitedCleanly) => {
            if command_pending {
                OperatorPhase::Starting
            } else {
                OperatorPhase::NotLaunched
            }
        }
        None => {
            if command_pending || launch_in_progress {
                OperatorPhase::Starting
            } else {
                OperatorPhase::NotLaunched
            }
        }
    }
}

impl SmokeOverlayApp {
    fn new(
        config: OverlayBootstrapConfig,
        model: SuiteSelectionModel,
        session_paths: SmokeSessionPaths,
    ) -> Self {
        let reader = EventReader::new(
            session_paths.clone(),
            config.tuning.startup_timeout_seconds,
            config
                .tuning
                .stale_after_seconds
                .max(config.tuning.heartbeat_seconds * 3)
                .max(15),
        );
        Self {
            config,
            model,
            session_paths,
            reader,
            child: None,
            launched_at: None,
            latest_poll: None,
            status_message: "Launch Unity host, then run suites from this window.".to_string(),
            command_error: None,
            pending_command_id: None,
            pending_command_seq: None,
            batch_queue: None,
            suite_filter_query: String::new(),
            suite_speed_filter: None,
            startup_window_placement_applied: false,
            icon_font_registered: false,
            last_operator_phase: None,
        }
    }

    fn launch_host(&mut self) {
        self.command_error = None;
        self.pending_command_id = None;
        self.pending_command_seq = None;
        self.batch_queue = None;
        let catalog_raw = match fs::read_to_string(&self.config.catalog_path) {
            Ok(raw) => raw,
            Err(error) => {
                self.command_error = Some(format!("failed to read catalog: {error}"));
                return;
            }
        };
        let catalog = match load_catalog_from_str(&catalog_raw) {
            Ok(catalog) => catalog,
            Err(error) => {
                self.command_error = Some(format!("catalog validation failed: {error}"));
                return;
            }
        };

        let session_id = generate_session_id();
        let metadata = InitialSessionMetadata {
            catalog_path: self.config.catalog_path.display().to_string(),
            project_path: self.config.project_path.display().to_string(),
            overlay_version: env!("CARGO_PKG_VERSION").to_string(),
            host_version: "asmlite-smoke-host".to_string(),
            package_version: "com.staples.asm-lite".to_string(),
            unity_version: "unknown".to_string(),
            global_reset_default: self.model.global_reset_default.as_str().to_string(),
            capabilities: vec![
                "launch-session".to_string(),
                "run-suite".to_string(),
                "review-decision".to_string(),
                "abort-run".to_string(),
            ],
        };
        if let Err(error) =
            write_initial_session_documents(&self.session_paths, &session_id, &catalog, &metadata)
        {
            self.command_error = Some(format!("failed to write session documents: {error}"));
            return;
        }

        let unity_executable = self
            .config
            .unity_executable
            .clone()
            .unwrap_or_else(|| "Unity".into());
        let mut launch_config = UnityHostLaunchConfig::new(
            unity_executable,
            self.config.project_path.clone(),
            self.config.session_root.clone(),
            self.config.catalog_path.clone(),
        );
        launch_config.startup_timeout_seconds = self.config.tuning.startup_timeout_seconds;
        launch_config.heartbeat_seconds = self.config.tuning.heartbeat_seconds;
        launch_config.exit_on_ready = false;

        match spawn_unity_host(&launch_config) {
            Ok(child) => {
                self.child = Some(child);
                self.launched_at = Some(Instant::now());
                self.status_message = "Unity host launching.".to_string();
            }
            Err(error) => self.command_error = Some(error.to_string()),
        }
    }

    fn relaunch_host(&mut self) {
        self.child = None;
        self.launch_host();
    }

    fn exit_without_saving(&mut self, ctx: &egui::Context) {
        if let Some(child) = self.child.as_mut() {
            let already_exited = child.try_wait().ok().flatten().is_some();
            if !already_exited {
                let _ = child.kill();
                let _ = child.wait();
            }
        }
        self.child = None;
        self.status_message = "Exited Unity without saving and closing overlay.".to_string();
        ctx.send_viewport_cmd(egui::ViewportCommand::Close);
    }

    fn poll_host(&mut self) {
        let process_exit_code = self
            .child
            .as_mut()
            .and_then(|child| child.try_wait().ok().flatten())
            .map(|status| status.code().unwrap_or(1));
        let elapsed = self
            .launched_at
            .map(|started| started.elapsed().as_secs())
            .unwrap_or(0);
        let poll = self
            .reader
            .poll(&current_utc_rfc3339(), elapsed, process_exit_code);
        if process_exit_code.is_some() {
            self.child = None;
        }
        if let (Some(command_seq), Some(host_state)) = (self.pending_command_seq, &poll.host_state)
        {
            if host_state.last_command_seq >= command_seq {
                self.pending_command_id = None;
                self.pending_command_seq = None;
            }
        }
        self.status_message = status_message_from_poll(&poll);
        self.latest_poll = Some(poll);
        self.advance_batch_if_ready();
    }

    fn run_selected_suite(&mut self) {
        let Some(host_state) = self
            .latest_poll
            .as_ref()
            .and_then(|poll| poll.host_state.as_ref())
        else {
            self.command_error = Some("Unity host is not ready yet.".to_string());
            return;
        };
        let queue = match BatchRunQueue::new_from_selection(&self.model) {
            Ok(queue) => queue,
            Err(error) => {
                self.command_error = Some(error);
                return;
            }
        };
        let suite_id = queue.current_suite_id().unwrap_or_default().to_string();
        if let Err(error) = self.model.set_current_suite_id_preserving_batch(&suite_id) {
            self.command_error = Some(error);
            return;
        }
        match dispatch_suite_run_command(&self.session_paths, host_state, &self.model, &suite_id) {
            Ok(command) => {
                self.pending_command_id = Some(command.command_id.clone());
                self.pending_command_seq = Some(command.command_seq);
                self.batch_queue = Some(queue);
                self.command_error = None;
                self.status_message = format!("Queued run-suite command {}.", command.command_id);
            }
            Err(error) => self.command_error = Some(error),
        }
    }

    fn advance_batch_if_ready(&mut self) {
        if self.pending_command_id.is_some() {
            return;
        }
        let Some(host_state) = self
            .latest_poll
            .as_ref()
            .and_then(|poll| poll.host_state.as_ref())
            .cloned()
        else {
            return;
        };
        let Some(queue) = self.batch_queue.as_mut() else {
            return;
        };
        if !queue.is_running() {
            return;
        }

        match host_state.state.as_str() {
            HOST_STATE_REVIEW_REQUIRED => {
                queue.record_current_stopped(BatchStopReason::ReviewRequired);
                self.status_message = "Batch stopped for review.".to_string();
            }
            HOST_STATE_CRASHED | HOST_STATE_STALLED => {
                queue.record_current_stopped(BatchStopReason::HostError);
                self.status_message =
                    "Batch stopped because the Unity host needs attention.".to_string();
            }
            HOST_STATE_READY | HOST_STATE_IDLE if host_state.active_run_id.trim().is_empty() => {
                let transition = queue.record_current_passed();
                if transition.batch_complete {
                    self.model.reset_current_suite_to_selected_head();
                    self.status_message = "Selected suite batch passed.".to_string();
                    return;
                }
                if let Some(next_suite_id) = transition.next_suite_id {
                    if let Err(error) = self
                        .model
                        .set_current_suite_id_preserving_batch(&next_suite_id)
                    {
                        self.command_error = Some(error);
                        queue.record_current_stopped(BatchStopReason::HostError);
                        return;
                    }
                    match dispatch_suite_run_command(
                        &self.session_paths,
                        &host_state,
                        &self.model,
                        &next_suite_id,
                    ) {
                        Ok(command) => {
                            self.pending_command_id = Some(command.command_id.clone());
                            self.pending_command_seq = Some(command.command_seq);
                            self.command_error = None;
                            self.status_message = format!(
                                "Queued next run-suite command {} for {}.",
                                command.command_id, next_suite_id
                            );
                        }
                        Err(error) => {
                            self.command_error = Some(error);
                            queue.record_current_stopped(BatchStopReason::HostError);
                        }
                    }
                }
            }
            _ => {}
        }
    }

    fn abort_run(&mut self) {
        let Some(host_state) = self
            .latest_poll
            .as_ref()
            .and_then(|poll| poll.host_state.as_ref())
        else {
            self.command_error = Some("No running Unity host state available.".to_string());
            return;
        };
        let suite_id = active_suite_id(self.latest_poll.as_ref(), &self.model.selected_suite_id);
        match dispatch_abort_run_command(
            &self.session_paths,
            host_state,
            &suite_id,
            "operator-abort",
        ) {
            Ok(command) => {
                self.pending_command_id = Some(command.command_id.clone());
                self.pending_command_seq = Some(command.command_seq);
                if let Some(queue) = self.batch_queue.as_mut() {
                    if queue.is_running() {
                        queue.record_current_stopped(BatchStopReason::Aborted);
                    }
                }
                self.command_error = None;
                self.status_message = format!("Queued abort-run command {}.", command.command_id);
            }
            Err(error) => self.command_error = Some(error),
        }
    }

    fn apply_startup_window_placement(&mut self, ctx: &egui::Context) {
        if self.startup_window_placement_applied {
            return;
        }

        let placement = ctx.input(|input| {
            let viewport = input.viewport();
            let monitor_size = viewport.monitor_size?;
            let frame_chrome_size = match (viewport.outer_rect, viewport.inner_rect) {
                (Some(outer), Some(inner)) => outer.size() - inner.size(),
                _ => egui::Vec2::ZERO,
            };
            startup_window_placement(monitor_size, frame_chrome_size)
        });

        let Some(placement) = placement else {
            return;
        };

        ctx.send_viewport_cmd(egui::ViewportCommand::Decorations(placement.decorated));
        ctx.send_viewport_cmd(egui::ViewportCommand::Fullscreen(placement.fullscreen));
        ctx.send_viewport_cmd(egui::ViewportCommand::Maximized(placement.maximized));
        ctx.send_viewport_cmd(egui::ViewportCommand::Resizable(true));
        ctx.send_viewport_cmd(egui::ViewportCommand::InnerSize(placement.inner_size));
        ctx.send_viewport_cmd(egui::ViewportCommand::MinInnerSize(egui::vec2(
            RIGHT_PANEL_WIDTH_PX,
            560.0,
        )));
        ctx.send_viewport_cmd(egui::ViewportCommand::OuterPosition(placement.position));
        self.startup_window_placement_applied = true;
    }

    fn dispatch_review(&mut self, action: ReviewAction) {
        let Some(host_state) = self
            .latest_poll
            .as_ref()
            .and_then(|poll| poll.host_state.as_ref())
        else {
            self.command_error = Some("No review host state available.".to_string());
            return;
        };
        let review_model = review_summary_for_poll(
            self.latest_poll.as_ref(),
            &self.session_paths,
            &self.model.selected_suite_id,
        );
        let Some(review_model) = review_model else {
            self.command_error = Some("No review context available yet.".to_string());
            return;
        };
        match dispatch_review_decision_command(
            &self.session_paths,
            host_state,
            &review_model,
            action,
        ) {
            Ok(command) => {
                self.pending_command_id = Some(command.command_id.clone());
                self.pending_command_seq = Some(command.command_seq);
                self.command_error = None;
                self.status_message =
                    format!("Queued review-decision command {}.", command.command_id);
            }
            Err(error) => self.command_error = Some(error),
        }
    }

    fn export_logs(&mut self) {
        let review_model = review_summary_for_poll(
            self.latest_poll.as_ref(),
            &self.session_paths,
            &self.model.selected_suite_id,
        );
        let Some(review_model) = review_model else {
            self.command_error = Some("No review context available yet.".to_string());
            return;
        };

        match export_review_logs(&self.session_paths, &review_model) {
            Ok(export_path) => {
                self.command_error = None;
                self.status_message = format!("Exported review logs to {}.", export_path.display());
            }
            Err(error) => self.command_error = Some(error),
        }
    }
}

impl eframe::App for SmokeOverlayApp {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        if !self.icon_font_registered {
            register_icon_font(ctx);
            self.icon_font_registered = true;
        }
        self.poll_host();
        self.apply_startup_window_placement(ctx);
        apply_visuals(ctx);

        let host_state_owned = self
            .latest_poll
            .as_ref()
            .and_then(|poll| poll.host_state.clone());
        let supervisor_status = self.latest_poll.as_ref().map(|poll| poll.status);
        let status_message = self.status_message.clone();
        let command_pending = self.pending_command_id.is_some();
        let launch_in_progress = host_state_owned.is_none() && self.child.is_some();
        let controls = control_state_for_suite_selection(
            control_state_for_host_state(
                host_state_owned.as_ref(),
                supervisor_status,
                command_pending,
                launch_in_progress,
            ),
            &self.model,
        );
        let operator_phase = operator_phase_for_host_state(
            host_state_owned.as_ref(),
            supervisor_status,
            command_pending,
            launch_in_progress,
        );
        self.last_operator_phase = Some(operator_phase);

        let footer_filter = SuiteChecklistFilter {
            search_query: self.suite_filter_query.clone(),
            speed_filter: self.suite_speed_filter,
        };
        let footer_hidden_by_filter_count =
            build_filtered_suite_checklist_view(&self.model, &footer_filter).hidden_by_filter_count;
        let footer_model = footer_status_model(
            self.latest_poll.as_ref(),
            self.pending_command_id.as_deref(),
            &self.model,
            footer_hidden_by_filter_count,
            self.batch_queue.as_ref(),
        );
        egui::TopBottomPanel::bottom("footer-status-strip")
            .resizable(false)
            .frame(
                egui::Frame::default()
                    .fill(PANEL_RAISED)
                    .inner_margin(egui::Margin::symmetric(OUTER_MARGIN_PX, RELATED_GAP_PX)),
            )
            .show(ctx, |ui| footer_status_strip(ui, &footer_model));

        egui::TopBottomPanel::bottom("actions-dock")
            .resizable(false)
            .frame(
                egui::Frame::default()
                    .fill(BACKGROUND)
                    .inner_margin(egui::Margin::symmetric(OUTER_MARGIN_PX, RELATED_GAP_PX)),
            )
            .show(ctx, |ui| {
                controls_card(ui, controls, operator_phase, self, ctx);
                if let Some(error) = &self.command_error {
                    ui.add_space(RELATED_GAP_PX);
                    ui.label(
                        egui::RichText::new(error)
                            .color(DANGER)
                            .size(META_TEXT_SIZE),
                    );
                }
            });

        egui::CentralPanel::default()
            .frame(
                egui::Frame::default()
                    .fill(BACKGROUND)
                    .inner_margin(OUTER_MARGIN_PX),
            )
            .show(ctx, |ui| {
                paint_ambient_glow(ui);
                hero_header_row(
                    ui,
                    &status_message,
                    host_state_owned.as_ref(),
                    supervisor_status,
                );
                ui.add_space(SECTION_GAP_PX);
                egui::ScrollArea::vertical()
                    .auto_shrink([false, false])
                    .show(ui, |ui| {
                        if matches!(operator_phase, OperatorPhase::Running) {
                            selected_suite_card(
                                ui,
                                &self.model,
                                self.latest_poll.as_ref(),
                                operator_phase,
                            );
                            ui.add_space(SECTION_GAP_PX);
                            run_monitor_card(
                                ui,
                                self.latest_poll.as_ref(),
                                &self.session_paths,
                                &self.model.selected_suite_id,
                                operator_phase,
                            );
                            ui.add_space(SECTION_GAP_PX);
                            current_run_plan_card(
                                ui,
                                &self.model,
                                dashboard_card_collapsed_for_phase(
                                    operator_phase,
                                    OverlaySectionId::CurrentRunPlan,
                                ),
                            );
                            ui.add_space(SECTION_GAP_PX);
                            suite_selector(
                                ui,
                                &mut self.model,
                                &mut self.suite_filter_query,
                                &mut self.suite_speed_filter,
                                !controls.can_select_suite,
                                dashboard_card_collapsed_for_phase(
                                    operator_phase,
                                    OverlaySectionId::SuitePicker,
                                ),
                            );
                        } else {
                            current_run_plan_card(ui, &self.model, false);
                            ui.add_space(SECTION_GAP_PX);
                            suite_selector(
                                ui,
                                &mut self.model,
                                &mut self.suite_filter_query,
                                &mut self.suite_speed_filter,
                                !controls.can_select_suite,
                                false,
                            );
                            ui.add_space(SECTION_GAP_PX);
                            selected_suite_card(
                                ui,
                                &self.model,
                                self.latest_poll.as_ref(),
                                operator_phase,
                            );
                            ui.add_space(SECTION_GAP_PX);
                            run_monitor_card(
                                ui,
                                self.latest_poll.as_ref(),
                                &self.session_paths,
                                &self.model.selected_suite_id,
                                operator_phase,
                            );
                        }
                        ui.add_space(SECTION_GAP_PX);
                        utilities_card(ui, &mut self.model, !controls.can_edit_step_sleep_timer);
                        ui.add_space(SECTION_GAP_PX);
                        if matches!(operator_phase, OperatorPhase::ReviewRequired) {
                            review_evidence_card(
                                ui,
                                self.latest_poll.as_ref(),
                                &self.session_paths,
                                &self.model.selected_suite_id,
                            );
                        }
                    });
            });

        ctx.request_repaint_after(Duration::from_millis(
            self.config.tuning.poll_interval_millis.max(50),
        ));
    }
}

fn step_sleep_timer_card(ui: &mut egui::Ui, model: &mut SuiteSelectionModel, disabled: bool) {
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL)
            .rounding(CARD_RADIUS_PX)
            .inner_margin(CARD_MARGIN_PX),
        CARD_MARGIN_PX,
        |ui| {
            ui.label(
                egui::RichText::new("Step sleep timer")
                    .strong()
                    .size(CARD_TITLE_TEXT_SIZE),
            );
            ui.add_space(RELATED_GAP_PX);
            ui.add_enabled_ui(!disabled, |ui| {
                let mut enabled = model.step_sleep_timer.enabled;
                if ui.checkbox(&mut enabled, "Slow step execution").changed() {
                    model.set_step_sleep_enabled(enabled);
                }
                ui.horizontal(|ui| {
                    ui.label(
                        egui::RichText::new("Seconds")
                            .color(MUTED)
                            .size(META_TEXT_SIZE),
                    );
                    let mut seconds = model.step_sleep_timer.seconds;
                    if ui
                        .add(
                            egui::DragValue::new(&mut seconds)
                                .speed(0.1)
                                .range(0.0..=60.0)
                                .fixed_decimals(1),
                        )
                        .changed()
                    {
                        model.set_step_sleep_seconds(seconds);
                    }
                });
            });
            if disabled {
                ui.label(
                    egui::RichText::new("Locked while a suite or command is running.")
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
            }
        },
    );
}

fn current_run_plan_card(ui: &mut egui::Ui, model: &SuiteSelectionModel, collapsed: bool) {
    let plan = build_current_run_plan_view(model);
    if collapsed {
        dashboard_collapsed_section(
            ui,
            "Current Run Plan",
            DashboardSectionTone::Green,
            &plan.summary,
        );
        return;
    }
    dashboard_section(ui, "Current Run Plan", DashboardSectionTone::Green, |ui| {
        ui.label(
            egui::RichText::new(&plan.summary)
                .color(MUTED)
                .size(META_TEXT_SIZE),
        );
        ui.add_space(RELATED_GAP_PX);

        if let Some(message) = plan.empty_message.as_deref() {
            ui.label(
                egui::RichText::new(message)
                    .color(WARNING)
                    .strong()
                    .size(BODY_TEXT_SIZE),
            );
            return;
        }

        for (section_index, section) in plan.suite_sections.iter().enumerate() {
            if section_index > 0 {
                ui.add_space(RELATED_GAP_PX);
            }
            egui::CollapsingHeader::new(
                egui::RichText::new(&section.title)
                    .strong()
                    .color(TEXT)
                    .size(BODY_TEXT_SIZE),
            )
            .id_source(format!("current-run-plan-{}", section.suite_id))
            .default_open(section.default_open)
            .show(ui, |ui| {
                ui.label(
                    egui::RichText::new(format!("Suite: {}", section.suite_id))
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
                ui.add_space(RELATED_GAP_PX);
                for (suite_step_index, row) in section.steps.iter().enumerate() {
                    show_full_width_frame(
                        ui,
                        egui::Frame::default()
                            .fill(PANEL_RAISED)
                            .stroke(egui::Stroke::new(1.0, CARD_BORDER))
                            .rounding(8.0)
                            .inner_margin(egui::Margin::symmetric(8.0, 5.0)),
                        8.0,
                        |ui| {
                            current_run_plan_step_row(ui, row, suite_step_index + 1);
                        },
                    );
                }
            });
        }
    });
}

fn current_run_plan_step_row(ui: &mut egui::Ui, row: &RunPlanStepRowView, suite_step_index: usize) {
    let spec = current_run_plan_step_visual_spec();
    ui.horizontal(|ui| {
        ui.label(
            egui::RichText::new(format!("{}", row.ordinal))
                .strong()
                .color(ACTIVE_BLUE)
                .size(META_TEXT_SIZE),
        );
        ui.label(
            egui::RichText::new(spec.separator_icon)
                .color(MUTED)
                .size(META_TEXT_SIZE),
        );
        ui.label(
            egui::RichText::new(current_run_plan_step_label(
                &row.step_label,
                row.ordinal,
                suite_step_index,
            ))
            .color(TEXT)
            .size(META_TEXT_SIZE),
        );
    });
}

fn selected_suite_card(
    ui: &mut egui::Ui,
    model: &SuiteSelectionModel,
    poll: Option<&StartupPollResult>,
    operator_phase: OperatorPhase,
) {
    dashboard_section(ui, "Current Suite", DashboardSectionTone::Cream, |ui| {
        if let Some(briefing) = build_current_suite_briefing_model_for_poll(model, poll) {
            ui.horizontal_top(|ui| {
                badge_label(ui, briefing.suite_icon, ACCENT);
                ui.vertical(|ui| {
                    ui.label(
                        egui::RichText::new(&briefing.title)
                            .strong()
                            .color(TEXT)
                            .size(CARD_TITLE_TEXT_SIZE),
                    );
                    ui.label(
                        egui::RichText::new(&briefing.description)
                            .color(MUTED)
                            .size(META_TEXT_SIZE),
                    );
                });
            });
            ui.add_space(RELATED_GAP_PX);
            section_subheader(
                ui,
                current_suite_steps_section_label(),
                DashboardSectionTone::Green,
            );
            ui.add_space(RELATED_GAP_PX);
            show_full_width_frame(
                ui,
                egui::Frame::default()
                    .fill(PANEL_RAISED)
                    .stroke(egui::Stroke::new(1.0, CARD_BORDER))
                    .rounding(CARD_RADIUS_PX)
                    .inner_margin(CARD_MARGIN_PX),
                CARD_MARGIN_PX,
                |ui| {
                    for step in &briefing.steps {
                        ui.label(
                            egui::RichText::new(current_suite_step_label(step))
                                .color(current_suite_step_color(step.status))
                                .size(META_TEXT_SIZE),
                        );
                    }
                },
            );
            if !briefing.info_note.trim().is_empty() {
                ui.add_space(RELATED_GAP_PX);
                match current_suite_info_presentation(operator_phase) {
                    CurrentSuiteInfoPresentation::FramedCallout => {
                        info_callout(ui, &briefing.info_note);
                    }
                    CurrentSuiteInfoPresentation::InlineMuted => {
                        compact_current_suite_note(ui, &briefing.info_note);
                    }
                }
            }
            if !briefing.debug_hint.trim().is_empty() {
                ui.add_space(RELATED_GAP_PX);
                egui::CollapsingHeader::new(
                    egui::RichText::new(debug_hint_section_title())
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                )
                .id_source(format!("debug-hint-{}", briefing.suite_id))
                .default_open(false)
                .show(ui, |ui| {
                    ui.label(
                        egui::RichText::new(&briefing.debug_hint)
                            .color(MUTED)
                            .size(META_TEXT_SIZE),
                    );
                });
            }
        } else {
            ui.label(
                egui::RichText::new(current_suite_empty_state_copy())
                    .color(MUTED)
                    .size(META_TEXT_SIZE),
            );
        }
    });
}

fn utilities_card(ui: &mut egui::Ui, model: &mut SuiteSelectionModel, disabled: bool) {
    dashboard_section(
        ui,
        utilities_section_title(),
        DashboardSectionTone::Lavender,
        |ui| {
            ui.label(
                egui::RichText::new("Operator pacing")
                    .strong()
                    .color(TEXT)
                    .size(BODY_TEXT_SIZE),
            );
            ui.add_space(RELATED_GAP_PX);
            step_sleep_timer_card(ui, model, disabled);
            ui.add_space(SECTION_GAP_PX);
            ui.separator();
            ui.add_space(RELATED_GAP_PX);
            ui.label(
                egui::RichText::new("Risk gates")
                    .strong()
                    .color(TEXT)
                    .size(BODY_TEXT_SIZE),
            );
            ui.add_enabled_ui(!disabled, |ui| {
                let mut destructive_enabled = model.destructive_suites_enabled;
                if ui
                    .checkbox(&mut destructive_enabled, "Enable destructive drills")
                    .changed()
                {
                    model.set_destructive_suites_enabled(destructive_enabled);
                }
                ui.label(
                    egui::RichText::new(destructive_drills_helper_copy(destructive_enabled))
                        .color(destructive_drills_helper_color(destructive_enabled))
                        .size(META_TEXT_SIZE),
                );
            });
        },
    );
}

fn controls_card(
    ui: &mut egui::Ui,
    controls: OverlayControlState,
    phase: OperatorPhase,
    app: &mut SmokeOverlayApp,
    ctx: &egui::Context,
) {
    dashboard_section(ui, "Actions", DashboardSectionTone::Blue, |ui| {
        let actions = phase_control_action_models_for_selected_batch(
            phase,
            controls,
            app.model.selected_suite_ids().len(),
        );
        let dock =
            action_dock_layout_for_phase(phase, controls, app.model.selected_suite_ids().len());
        if !dock.primary_ids.is_empty() {
            let title = if matches!(phase, OperatorPhase::ReviewRequired) {
                "Review decision"
            } else {
                "Primary action"
            };
            section_subheader(ui, title, DashboardSectionTone::Green);
            ui.add_space(RELATED_GAP_PX);
            render_action_id_row(ui, &actions, &dock.primary_ids, app, ctx);
            ui.add_space(SECTION_GAP_PX);
        }

        if !dock.decision_peer_ids.is_empty() {
            section_subheader(ui, "Review tools", DashboardSectionTone::Orange);
            ui.add_space(RELATED_GAP_PX);
            render_action_id_row(ui, &actions, &dock.decision_peer_ids, app, ctx);
            ui.add_space(SECTION_GAP_PX);
        }

        if !dock.secondary_ids.is_empty() {
            section_subheader(ui, "Secondary controls", DashboardSectionTone::Blue);
            ui.add_space(RELATED_GAP_PX);
            render_action_id_row(ui, &actions, &dock.secondary_ids, app, ctx);
            ui.add_space(SECTION_GAP_PX);
        }

        section_subheader(ui, "Exit without saving", DashboardSectionTone::Orange);
        ui.add_space(RELATED_GAP_PX);
        render_action_id_row(ui, &actions, &dock.destructive_ids, app, ctx);
    });
}

fn render_action_id_row(
    ui: &mut egui::Ui,
    actions: &[PhaseControlActionModel],
    ids: &[ControlActionId],
    app: &mut SmokeOverlayApp,
    ctx: &egui::Context,
) {
    ui.horizontal_wrapped(|ui| {
        for id in ids {
            if let Some(action) = actions
                .iter()
                .find(|action| action.action.id == *id)
                .cloned()
            {
                render_action_button(ui, action, app, ctx);
            }
        }
    });
}

fn action_button_style(role: ControlActionRole) -> ActionButtonStyle {
    match role {
        ControlActionRole::Primary => ActionButtonStyle {
            fill: ACTIVE_BLUE,
            stroke: egui::Stroke::NONE,
            text_color: egui::Color32::WHITE,
        },
        ControlActionRole::Secondary => ActionButtonStyle {
            fill: PANEL,
            stroke: egui::Stroke::new(1.0, MUTED),
            text_color: TEXT,
        },
        ControlActionRole::Destructive => ActionButtonStyle {
            fill: DANGER_BUTTON_FILL,
            stroke: egui::Stroke::NONE,
            text_color: egui::Color32::WHITE,
        },
    }
}

fn action_button_style_for_action(action: &ControlActionModel) -> ActionButtonStyle {
    if action.enabled {
        return action_button_style(action.role);
    }

    ActionButtonStyle {
        fill: egui::Color32::from_rgb(22, 33, 51),
        stroke: egui::Stroke::new(1.0, CARD_BORDER),
        text_color: DISABLED_TEXT,
    }
}

fn action_button_size(_emphasis: ControlActionEmphasis) -> egui::Vec2 {
    egui::vec2(ACTION_TILE_WIDTH_PX, ACTION_TILE_HEIGHT_PX)
}

fn action_button_size_for_action(phase_action: &PhaseControlActionModel) -> egui::Vec2 {
    action_button_size(phase_action.emphasis)
}

fn action_button_label_for_action(phase_action: &PhaseControlActionModel) -> String {
    let icon = control_action_icon(phase_action.action.id);
    format!("{} {}", icon, phase_action.action.label)
}

fn render_action_button(
    ui: &mut egui::Ui,
    phase_action: PhaseControlActionModel,
    app: &mut SmokeOverlayApp,
    ctx: &egui::Context,
) {
    let label = action_button_label_for_action(&phase_action);
    let size = action_button_size_for_action(&phase_action);
    let action = phase_action.action;
    let style = action_button_style_for_action(&action);
    let button = egui::Button::new(
        egui::RichText::new(label)
            .strong()
            .color(style.text_color)
            .size(BODY_TEXT_SIZE),
    )
    .fill(style.fill)
    .stroke(style.stroke)
    .min_size(size);

    let mut response = ui.add_enabled(action.enabled, button);
    if !action.enabled {
        if let Some(reason) = action.disabled_reason {
            response = response.on_disabled_hover_text(reason);
        }
    }
    if response.clicked() {
        match action.id {
            ControlActionId::LaunchHost => app.launch_host(),
            ControlActionId::RunSelectedSuite => app.run_selected_suite(),
            ControlActionId::AbortRun => app.abort_run(),
            ControlActionId::ExportLogs => app.export_logs(),
            ControlActionId::ReturnToSuiteList => {
                app.dispatch_review(ReviewAction::ReturnToSuiteList)
            }
            ControlActionId::RerunSuite => app.dispatch_review(ReviewAction::RerunSuite),
            ControlActionId::Exit => app.exit_without_saving(ctx),
            ControlActionId::RelaunchHost => app.relaunch_host(),
        }
    }
}

fn control_action_icon(action: ControlActionId) -> &'static str {
    match action {
        ControlActionId::LaunchHost => icons::ROCKET_LAUNCH,
        ControlActionId::RunSelectedSuite => icons::PLAY_CIRCLE,
        ControlActionId::AbortRun => icons::STOP_CIRCLE,
        ControlActionId::ExportLogs => icons::EXPORT,
        ControlActionId::ReturnToSuiteList => icons::LIST_CHECKS,
        ControlActionId::RerunSuite => icons::ARROWS_CLOCKWISE,
        ControlActionId::Exit => icons::SIGN_OUT,
        ControlActionId::RelaunchHost => icons::POWER,
    }
}

#[cfg(test)]
fn is_private_icon_glyph(icon: &str) -> bool {
    let mut chars = icon.chars();
    let Some(ch) = chars.next() else {
        return false;
    };
    chars.next().is_none() && ('\u{E000}'..='\u{F8FF}').contains(&ch)
}

fn show_full_width_frame(
    ui: &mut egui::Ui,
    frame: egui::Frame,
    horizontal_inner_margin_px: f32,
    add_body: impl FnOnce(&mut egui::Ui),
) {
    let layout = full_width_card_layout(ui.available_width(), horizontal_inner_margin_px);
    frame.show(ui, |ui| {
        ui.set_min_width(layout.content_min_width_px);
        add_body(ui);
    });
}

fn dashboard_section(
    ui: &mut egui::Ui,
    title: &str,
    tone: DashboardSectionTone,
    add_body: impl FnOnce(&mut egui::Ui),
) {
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL)
            .stroke(egui::Stroke::new(1.0, CARD_BORDER))
            .rounding(CARD_RADIUS_PX)
            .inner_margin(CARD_MARGIN_PX),
        CARD_MARGIN_PX,
        |ui| {
            ui.label(
                egui::RichText::new(section_label_text(title))
                    .strong()
                    .color(dashboard_section_text(tone))
                    .size(META_TEXT_SIZE),
            );
            ui.add_space(RELATED_GAP_PX);
            add_body(ui);
        },
    );
}

fn dashboard_collapsed_section(
    ui: &mut egui::Ui,
    title: &str,
    tone: DashboardSectionTone,
    summary: &str,
) {
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL)
            .stroke(egui::Stroke::new(1.0, CARD_BORDER))
            .rounding(CARD_RADIUS_PX)
            .inner_margin(egui::Margin::symmetric(CARD_MARGIN_PX, RELATED_GAP_PX)),
        CARD_MARGIN_PX,
        |ui| {
            ui.horizontal_wrapped(|ui| {
                ui.label(
                    egui::RichText::new(section_label_text(title))
                        .strong()
                        .color(dashboard_section_text(tone))
                        .size(META_TEXT_SIZE),
                );
                ui.label(
                    egui::RichText::new("Collapsed while running")
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
            });
            ui.label(
                egui::RichText::new(summary)
                    .color(MUTED)
                    .size(META_TEXT_SIZE),
            );
        },
    );
}

pub fn section_label_text(title: &str) -> String {
    title.to_uppercase()
}

pub fn section_subheader_visual_spec() -> SectionSubheaderVisualSpec {
    SectionSubheaderVisualSpec {
        bold_text: true,
        uses_bubble_frame: false,
        uses_background_fill: false,
        text_color: MUTED,
    }
}

pub fn suite_picker_typography_spec() -> SuitePickerTypographySpec {
    SuitePickerTypographySpec {
        summary_text_color: MUTED,
        summary_text_size_px: META_TEXT_SIZE,
        group_header_text_color: TEXT,
        group_header_text_size_px: BODY_TEXT_SIZE,
        group_header_bold: true,
        group_header_uses_collapsing_header: true,
        row_text_color: TEXT,
        row_text_size_px: META_TEXT_SIZE,
    }
}

pub fn suite_checkbox_visual_spec(
    checked: bool,
    focused: bool,
    disabled: bool,
) -> SuiteCheckboxVisualSpec {
    let icon = if checked {
        icons::CHECK_SQUARE
    } else {
        icons::SQUARE
    };
    if disabled {
        return SuiteCheckboxVisualSpec {
            icon,
            icon_color: DISABLED_TEXT,
            text_color: DISABLED_TEXT,
            row_fill: PANEL_RAISED.linear_multiply(0.72),
            row_stroke: egui::Stroke::new(1.0, CARD_BORDER.linear_multiply(0.72)),
            icon_size_px: BODY_TEXT_SIZE + 3.0,
        };
    }

    if checked {
        SuiteCheckboxVisualSpec {
            icon,
            icon_color: ACTIVE_BLUE,
            text_color: TEXT,
            row_fill: ACTIVE_BLUE.linear_multiply(if focused { 0.28 } else { 0.18 }),
            row_stroke: egui::Stroke::new(1.35, ACTIVE_BLUE),
            icon_size_px: BODY_TEXT_SIZE + 3.0,
        }
    } else {
        SuiteCheckboxVisualSpec {
            icon,
            icon_color: MUTED,
            text_color: TEXT,
            row_fill: PANEL_RAISED,
            row_stroke: egui::Stroke::new(1.0, CARD_BORDER),
            icon_size_px: BODY_TEXT_SIZE + 3.0,
        }
    }
}

pub fn current_suite_info_callout_visual_spec() -> InfoCalloutVisualSpec {
    InfoCalloutVisualSpec {
        icon: icons::INFO,
        icon_color: ACTIVE_BLUE,
        text_color: TEXT,
        icon_size_px: BODY_TEXT_SIZE + 2.0,
        text_size_px: META_TEXT_SIZE,
        row_vertical_align: egui::Align::Center,
        horizontal_wrapped: true,
    }
}

pub fn current_suite_info_presentation(
    operator_phase: OperatorPhase,
) -> CurrentSuiteInfoPresentation {
    if matches!(operator_phase, OperatorPhase::Running) {
        CurrentSuiteInfoPresentation::InlineMuted
    } else {
        CurrentSuiteInfoPresentation::FramedCallout
    }
}

fn section_subheader(ui: &mut egui::Ui, title: &str, tone: DashboardSectionTone) {
    let spec = section_subheader_visual_spec();
    let text = egui::RichText::new(title)
        .strong()
        .color(if spec.text_color == MUTED {
            dashboard_section_text(tone)
        } else {
            spec.text_color
        })
        .size(BODY_TEXT_SIZE);
    ui.label(text);
}

pub fn dashboard_section_fill(_tone: DashboardSectionTone) -> egui::Color32 {
    PANEL_RAISED
}

pub fn dashboard_section_text(_tone: DashboardSectionTone) -> egui::Color32 {
    MUTED
}

fn badge_label(ui: &mut egui::Ui, text: &str, color: egui::Color32) {
    egui::Frame::default()
        .fill(color.linear_multiply(0.35))
        .stroke(egui::Stroke::new(1.0, color))
        .rounding(10.0)
        .inner_margin(egui::Margin::symmetric(8.0, 4.0))
        .show(ui, |ui| {
            ui.label(
                egui::RichText::new(text)
                    .color(egui::Color32::WHITE)
                    .strong()
                    .size(CARD_TITLE_TEXT_SIZE),
            );
        });
}

fn info_callout(ui: &mut egui::Ui, text: &str) {
    let spec = current_suite_info_callout_visual_spec();
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(egui::Color32::from_rgb(11, 33, 58))
            .stroke(egui::Stroke::new(1.0, ACCENT.linear_multiply(0.65)))
            .rounding(CARD_RADIUS_PX)
            .inner_margin(egui::Margin::symmetric(10.0, 6.0)),
        10.0,
        |ui| {
            if spec.horizontal_wrapped {
                ui.horizontal_wrapped(|ui| {
                    ui.spacing_mut().item_spacing.x = RELATED_GAP_PX;
                    ui.label(
                        egui::RichText::new(spec.icon)
                            .color(spec.icon_color)
                            .strong()
                            .size(spec.icon_size_px),
                    );
                    ui.add(
                        egui::Label::new(
                            egui::RichText::new(text)
                                .color(spec.text_color)
                                .size(spec.text_size_px),
                        )
                        .wrap(),
                    );
                });
            } else {
                ui.with_layout(egui::Layout::left_to_right(spec.row_vertical_align), |ui| {
                    ui.label(
                        egui::RichText::new(spec.icon)
                            .color(spec.icon_color)
                            .strong()
                            .size(spec.icon_size_px),
                    );
                    ui.label(
                        egui::RichText::new(text)
                            .color(spec.text_color)
                            .size(spec.text_size_px),
                    );
                });
            }
        },
    );
}

fn compact_current_suite_note(ui: &mut egui::Ui, text: &str) {
    ui.label(egui::RichText::new(text).color(MUTED).size(META_TEXT_SIZE));
}

fn meta_chip(ui: &mut egui::Ui, text: &str) {
    egui::Frame::default()
        .fill(PANEL_RAISED)
        .rounding(6.0)
        .inner_margin(egui::Margin::symmetric(8.0, 3.0))
        .show(ui, |ui| {
            ui.label(egui::RichText::new(text).color(MUTED).size(META_TEXT_SIZE));
        });
}

fn apply_visuals(ctx: &egui::Context) {
    let theme = overlay_theme_tokens();
    let mut visuals = egui::Visuals::dark();
    visuals.panel_fill = theme.app_background;
    visuals.window_fill = theme.app_background;
    visuals.extreme_bg_color = theme.card_surface;
    visuals.override_text_color = Some(theme.primary_text);
    visuals.widgets.inactive.bg_fill = theme.raised_card_surface;
    visuals.widgets.hovered.bg_fill = egui::Color32::from_rgb(54, 60, 70);
    visuals.widgets.active.bg_fill = theme.primary_action;
    ctx.set_visuals(visuals);
}

fn register_icon_font(ctx: &egui::Context) {
    let mut fonts = egui::FontDefinitions::default();
    egui_phosphor::add_to_fonts(&mut fonts, egui_phosphor::Variant::Regular);
    ctx.set_fonts(fonts);
}

pub fn ambient_glow_spec() -> [AmbientGlowLayer; 3] {
    [
        AmbientGlowLayer {
            x_factor: 0.50,
            y_factor: 0.25,
            radius_factor: 0.78,
            color: egui::Color32::from_rgb(47, 107, 255),
            alpha: 48,
        },
        AmbientGlowLayer {
            x_factor: 0.18,
            y_factor: 0.68,
            radius_factor: 0.54,
            color: egui::Color32::from_rgb(0, 229, 255),
            alpha: 30,
        },
        AmbientGlowLayer {
            x_factor: 0.86,
            y_factor: 0.58,
            radius_factor: 0.48,
            color: egui::Color32::from_rgb(91, 64, 255),
            alpha: 28,
        },
    ]
}

fn paint_ambient_glow(ui: &mut egui::Ui) {
    let rect = ui.max_rect();
    let painter = ui.painter();
    for layer in ambient_glow_spec() {
        painter.circle_filled(
            egui::pos2(
                rect.left() + rect.width() * layer.x_factor,
                rect.top() + rect.height() * layer.y_factor,
            ),
            rect.width() * layer.radius_factor,
            egui::Color32::from_rgba_unmultiplied(
                layer.color.r(),
                layer.color.g(),
                layer.color.b(),
                layer.alpha,
            ),
        );
    }
}

fn hero_header_row(
    ui: &mut egui::Ui,
    message: &str,
    host_state: Option<&SmokeHostStateDocument>,
    status: Option<UnityHostSupervisorStatus>,
) {
    let spec = hero_prominence_spec();
    let layout = hero_status_layout_spec();
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL)
            .stroke(egui::Stroke::new(1.0, CARD_BORDER))
            .rounding(CARD_RADIUS_PX)
            .inner_margin(spec.hero_card_padding_px),
        spec.hero_card_padding_px,
        |ui| header(ui),
    );
    ui.add_space(layout.inter_card_gap_px);
    status_card(ui, message, host_state, status);
}

fn header(ui: &mut egui::Ui) {
    let header = reference_header_model();
    let spec = hero_prominence_spec();
    ui.label(
        egui::RichText::new(header.title)
            .size(spec.title_text_size_px)
            .strong()
            .color(TEXT),
    );
    ui.label(
        egui::RichText::new(header.subtitle)
            .size(BODY_TEXT_SIZE)
            .color(MUTED),
    );
}

fn status_card(
    ui: &mut egui::Ui,
    message: &str,
    host_state: Option<&SmokeHostStateDocument>,
    status: Option<UnityHostSupervisorStatus>,
) {
    let label = host_state
        .map(|state| state.state.as_str())
        .unwrap_or_else(|| match status {
            Some(UnityHostSupervisorStatus::Starting) => "starting",
            Some(UnityHostSupervisorStatus::TimedOut) => "timed-out",
            Some(UnityHostSupervisorStatus::Crashed) => "crashed",
            Some(UnityHostSupervisorStatus::Stalled) => "stalled",
            _ => "not-launched",
        });
    let view = status_card_view_model(label, message);
    let color = tone_color(view.tone);
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL)
            .stroke(egui::Stroke::new(1.0, CARD_BORDER))
            .rounding(CARD_RADIUS_PX)
            .inner_margin(CARD_MARGIN_PX),
        CARD_MARGIN_PX,
        |ui| status_line(ui, view, color),
    );
}

fn status_line(ui: &mut egui::Ui, view: StatusCardViewModel, color: egui::Color32) {
    let spec = hero_prominence_spec();
    ui.horizontal(|ui| {
        ui.label(
            egui::RichText::new(view.icon)
                .color(color)
                .size(spec.status_icon_size_px)
                .strong(),
        );
        ui.vertical(|ui| {
            ui.label(
                egui::RichText::new(view.headline)
                    .strong()
                    .color(color)
                    .size(CARD_TITLE_TEXT_SIZE),
            );
            ui.label(
                egui::RichText::new(view.phase_label)
                    .color(TEXT)
                    .size(BODY_TEXT_SIZE),
            );
            ui.label(
                egui::RichText::new(view.subtext)
                    .color(MUTED)
                    .size(META_TEXT_SIZE),
            );
        });
    });
}

fn tone_color(tone: StatusTone) -> egui::Color32 {
    match tone {
        StatusTone::Neutral => MUTED,
        StatusTone::Accent => ACCENT,
        StatusTone::Warning => WARNING,
        StatusTone::Success => SUCCESS,
        StatusTone::Danger => DANGER,
    }
}

fn suite_checkbox_row_label(row: &SuiteRowView) -> String {
    let order = row
        .selected_order
        .map(|value| format!(" #{value}"))
        .unwrap_or_default();
    let focus = if row.focused { " · current" } else { "" };

    format!("{}{}{}", row.suite_label, order, focus)
}

fn render_suite_checkbox_row(
    ui: &mut egui::Ui,
    row: &SuiteRowView,
    disabled: bool,
) -> egui::Response {
    let label = suite_checkbox_row_label(row);
    let row_disabled = disabled || !row.selectable;
    let visual = suite_checkbox_visual_spec(row.checked, row.focused, row_disabled);
    let mut text = egui::text::LayoutJob::default();
    text.append(
        visual.icon,
        0.0,
        egui::text::TextFormat {
            font_id: egui::FontId::proportional(visual.icon_size_px),
            color: visual.icon_color,
            ..Default::default()
        },
    );
    text.append(
        "  ",
        0.0,
        egui::text::TextFormat {
            font_id: egui::FontId::proportional(META_TEXT_SIZE),
            color: visual.text_color,
            ..Default::default()
        },
    );
    text.append(
        &label,
        0.0,
        egui::text::TextFormat {
            font_id: egui::FontId::proportional(META_TEXT_SIZE),
            color: visual.text_color,
            ..Default::default()
        },
    );

    let button = egui::Button::new(text)
        .fill(visual.row_fill)
        .stroke(visual.row_stroke)
        .rounding(8.0)
        .min_size(egui::vec2(ui.available_width(), 30.0));
    ui.add_enabled(!row_disabled, button)
}

fn phosphor_collapsing_icon(ui: &mut egui::Ui, openness: f32, response: &egui::Response) {
    let icon = suite_group_dropdown_icon(openness > 0.5);
    let visuals = ui.style().interact(response);
    ui.painter().text(
        response.rect.center(),
        egui::Align2::CENTER_CENTER,
        icon,
        egui::FontId::proportional(BODY_TEXT_SIZE),
        visuals.fg_stroke.color,
    );
}

pub fn suite_filter_summary_text(
    selected_count: usize,
    visible_count: usize,
    hidden_by_filter_count: usize,
) -> String {
    let mut summary =
        format!("Selected suites: {selected_count} selected · {visible_count} visible");
    if hidden_by_filter_count > 0 {
        summary.push_str(&format!(" · {hidden_by_filter_count} hidden by filters"));
    }
    summary
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SuitePresetButtonSpec {
    pub label: &'static str,
    pub preset_group: &'static str,
}

pub fn suite_preset_button_specs() -> [SuitePresetButtonSpec; 4] {
    [
        SuitePresetButtonSpec {
            label: "Quick default",
            preset_group: "quick-default",
        },
        SuitePresetButtonSpec {
            label: "All setup",
            preset_group: "all-setup",
        },
        SuitePresetButtonSpec {
            label: "Safe negatives",
            preset_group: "safe-negatives",
        },
        SuitePresetButtonSpec {
            label: "Destructive drills",
            preset_group: "destructive-drills",
        },
    ]
}

fn apply_suite_preset_selection(
    model: &mut SuiteSelectionModel,
    speed_filter: &mut Option<SuiteSpeedFilter>,
    preset_group: &str,
) -> Result<(), String> {
    model.apply_preset_group(preset_group)?;
    *speed_filter = None;
    Ok(())
}

fn suite_selector(
    ui: &mut egui::Ui,
    model: &mut SuiteSelectionModel,
    filter_query: &mut String,
    speed_filter: &mut Option<SuiteSpeedFilter>,
    disabled: bool,
    collapsed: bool,
) {
    let filter = SuiteChecklistFilter {
        search_query: filter_query.clone(),
        speed_filter: *speed_filter,
    };
    let checklist = build_filtered_suite_checklist_view(model, &filter);
    let selected_suite_count = model.selected_suite_ids().len();
    let summary = suite_filter_summary_text(
        selected_suite_count,
        checklist.rows.len(),
        checklist.hidden_by_filter_count,
    );
    if collapsed {
        dashboard_collapsed_section(ui, "Suites", DashboardSectionTone::Gold, &summary);
        return;
    }
    dashboard_section(ui, "Suites", DashboardSectionTone::Gold, |ui| {
        ui.label(
            egui::RichText::new(summary)
                .color(MUTED)
                .size(META_TEXT_SIZE),
        );
        ui.add_space(RELATED_GAP_PX);
        ui.horizontal_wrapped(|ui| {
            ui.label(
                egui::RichText::new("Filter")
                    .color(MUTED)
                    .size(META_TEXT_SIZE),
            );
            ui.add(
                egui::TextEdit::singleline(filter_query)
                    .hint_text("Search id, label, description, or preset")
                    .desired_width(180.0),
            );
            if !filter_query.trim().is_empty() && ui.small_button("Clear").clicked() {
                filter_query.clear();
            }
        });
        ui.add_enabled_ui(!disabled, |ui| {
            ui.horizontal_wrapped(|ui| {
                ui.label(
                    egui::RichText::new("Presets")
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
                for preset in suite_preset_button_specs() {
                    let destructive_preset = preset.preset_group == "destructive-drills";
                    let enabled = !destructive_preset || model.destructive_suites_enabled;
                    if ui
                        .add_enabled(enabled, egui::Button::new(preset.label))
                        .clicked()
                    {
                        let _ =
                            apply_suite_preset_selection(model, speed_filter, preset.preset_group);
                    }
                }
            });
        });
        if checklist.selected_count == 0 {
            ui.label(
                egui::RichText::new("Select at least one suite to enable Run selected suite.")
                    .color(WARNING)
                    .size(META_TEXT_SIZE),
            );
        }
        if checklist.rows.is_empty() {
            ui.label(
                egui::RichText::new(suite_filter_empty_state_copy())
                    .color(WARNING)
                    .size(META_TEXT_SIZE),
            );
            return;
        }
        ui.add_space(RELATED_GAP_PX);

        for (section_index, section) in checklist.group_sections.iter().enumerate() {
            let group_count = section.rows.len();
            let spec = suite_picker_typography_spec();
            let header =
                egui::RichText::new(suite_group_header_text(&section.group_label, group_count))
                    .strong()
                    .color(spec.group_header_text_color)
                    .size(spec.group_header_text_size_px);

            egui::CollapsingHeader::new(header)
                .id_source(format!("suite-selector-group-{}", section.group_id))
                .default_open(section.default_open)
                .icon(phosphor_collapsing_icon)
                .show(ui, |ui| {
                    for row in &section.rows {
                        let response = render_suite_checkbox_row(ui, row, disabled);
                        if disabled {
                            response.on_disabled_hover_text(
                                "Suite selection is available when the Unity host is ready.",
                            );
                        } else if let Some(reason) = row.disabled_reason {
                            response.on_disabled_hover_text(reason);
                        } else if response.clicked() {
                            let _ = model.toggle_suite_selection_by_id(&row.suite_id);
                        }
                    }
                });

            if section_index + 1 < checklist.group_sections.len() {
                ui.add_space(SECTION_GAP_PX);
            }
        }
    });
}

fn review_evidence_card(
    ui: &mut egui::Ui,
    poll: Option<&StartupPollResult>,
    session_paths: &SmokeSessionPaths,
    fallback_suite_id: &str,
) {
    let review = review_summary_for_poll(poll, session_paths, fallback_suite_id);
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL_RAISED)
            .rounding(CARD_RADIUS_PX)
            .inner_margin(CARD_MARGIN_PX),
        CARD_MARGIN_PX,
        |ui| {
            ui.label(
                egui::RichText::new(review_evidence_title())
                    .strong()
                    .size(CARD_TITLE_TEXT_SIZE),
            );
            ui.add_space(RELATED_GAP_PX);
            if let Some(review) = review {
                let visual = review_result_visual_model(&review.run_result);
                let color = tone_color(visual.tone);
                ui.label(
                    egui::RichText::new(format!("{} · {}", visual.headline, review.suite_label))
                        .strong()
                        .color(color)
                        .size(BODY_TEXT_SIZE),
                );
                ui.label(
                    egui::RichText::new(format!("Suite: {}", review.suite_id))
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
                ui.label(
                    egui::RichText::new(format!("Run: {}", review.run_id))
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
                if let Some(excerpt) = review.failure_excerpt {
                    ui.add_space(RELATED_GAP_PX);
                    ui.label(
                        egui::RichText::new(format!("Failed step: {}", excerpt.step_label))
                            .color(review_failure_excerpt_color())
                            .strong()
                            .size(BODY_TEXT_SIZE),
                    );
                    ui.label(
                        egui::RichText::new(excerpt.failure_message)
                            .color(TEXT)
                            .size(META_TEXT_SIZE),
                    );
                }
                ui.add_space(RELATED_GAP_PX);
                ui.label(
                    egui::RichText::new(review_followup_copy())
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
            } else {
                ui.label(
                    egui::RichText::new("Review context is loading from the run artifacts.")
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
            }
        },
    );
}

fn run_monitor_card(
    ui: &mut egui::Ui,
    poll: Option<&StartupPollResult>,
    session_paths: &SmokeSessionPaths,
    selected_suite_id: &str,
    operator_phase: OperatorPhase,
) {
    let review = review_summary_for_poll(poll, session_paths, selected_suite_id);
    show_full_width_frame(
        ui,
        egui::Frame::default()
            .fill(PANEL)
            .rounding(CARD_RADIUS_PX)
            .inner_margin(CARD_MARGIN_PX),
        CARD_MARGIN_PX,
        |ui| {
            ui.label(
                egui::RichText::new(run_monitor_title())
                    .strong()
                    .size(CARD_TITLE_TEXT_SIZE),
            );
            ui.add_space(RELATED_GAP_PX);

            if let Some(review) = review {
                let visual = review_result_visual_model(&review.run_result);
                let color = tone_color(visual.tone);
                ui.horizontal(|ui| {
                    ui.label(
                        egui::RichText::new(visual.icon)
                            .color(color)
                            .strong()
                            .size(CARD_TITLE_TEXT_SIZE),
                    );
                    ui.label(
                        egui::RichText::new(run_monitor_review_heading(&review))
                            .color(color)
                            .strong()
                            .size(BODY_TEXT_SIZE),
                    );
                });
                ui.label(
                    egui::RichText::new(format!("Run: {}", review.run_id))
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
                if let Some(excerpt) = review.failure_excerpt {
                    ui.add_space(RELATED_GAP_PX);
                    ui.label(
                        egui::RichText::new(run_monitor_failure_label(&excerpt.step_label))
                            .color(review_failure_excerpt_color())
                            .strong()
                            .size(BODY_TEXT_SIZE),
                    );
                    ui.label(
                        egui::RichText::new(excerpt.failure_message)
                            .color(TEXT)
                            .size(META_TEXT_SIZE),
                    );
                }
            } else {
                ui.label(
                    egui::RichText::new("Artifacts appear after a run completes.")
                        .color(MUTED)
                        .size(META_TEXT_SIZE),
                );
            }

            ui.add_space(SECTION_GAP_PX);
            egui::CollapsingHeader::new(
                egui::RichText::new(recent_events_section_title())
                    .strong()
                    .color(TEXT)
                    .size(BODY_TEXT_SIZE),
            )
            .id_source("recent-events-log-section")
            .default_open(recent_event_log_default_open(operator_phase))
            .open(recent_event_log_forced_open(operator_phase))
            .show(ui, |ui| {
                if recent_event_log_row_count(poll) > 0 {
                    egui::ScrollArea::vertical()
                        .id_source("recent-events-log")
                        .max_height(recent_event_log_scroll_max_height_px(operator_phase))
                        .stick_to_bottom(true)
                        .auto_shrink([false, false])
                        .show(ui, |ui| {
                            if let Some(poll) = poll {
                                for event in &poll.events {
                                    ui.label(
                                        egui::RichText::new(format_recent_event_log_entry(event))
                                            .color(MUTED)
                                            .size(META_TEXT_SIZE),
                                    );
                                }
                            }
                        });
                } else {
                    ui.label(egui::RichText::new(recent_event_log_empty_state_copy()).color(MUTED));
                }
            });
        },
    );
}

fn status_message_from_poll(poll: &StartupPollResult) -> String {
    if let Some(host_state) = &poll.host_state {
        return host_state.message.clone();
    }
    match poll.status {
        UnityHostSupervisorStatus::Starting => "Waiting for Unity host-state.".to_string(),
        UnityHostSupervisorStatus::TimedOut => "Unity host startup timed out.".to_string(),
        UnityHostSupervisorStatus::Crashed => "Unity host crashed.".to_string(),
        UnityHostSupervisorStatus::Stalled => "Unity host heartbeat stalled.".to_string(),
        UnityHostSupervisorStatus::ExitedCleanly => "Unity host exited cleanly.".to_string(),
        UnityHostSupervisorStatus::ExitedWithError => {
            "Unity host exited with an error.".to_string()
        }
        UnityHostSupervisorStatus::Ready => "Unity host ready.".to_string(),
    }
}

fn review_result_visual_model(result: &str) -> StatusVisualModel {
    match result.trim().to_ascii_lowercase().as_str() {
        "passed" => StatusVisualModel {
            icon: icons::CHECK_CIRCLE,
            headline: "PASSED",
            tone: StatusTone::Success,
        },
        "failed" => StatusVisualModel {
            icon: icons::WARNING_CIRCLE,
            headline: "FAILED",
            tone: StatusTone::Warning,
        },
        "aborted" => StatusVisualModel {
            icon: icons::PROHIBIT,
            headline: "ABORTED",
            tone: StatusTone::Warning,
        },
        _ => StatusVisualModel {
            icon: icons::QUESTION,
            headline: "UNKNOWN",
            tone: StatusTone::Warning,
        },
    }
}

fn footer_status_model(
    poll: Option<&StartupPollResult>,
    pending_command: Option<&str>,
    model: &SuiteSelectionModel,
    hidden_by_filter_count: usize,
    batch_queue: Option<&BatchRunQueue>,
) -> FooterStatusModel {
    let host_state = poll
        .and_then(|poll| poll.host_state.as_ref())
        .and_then(|state| non_empty_string(&state.state));
    let batch_progress = batch_queue.and_then(|queue| {
        let selected_count = queue.selected_suite_ids().len();
        queue.current_suite_id()?;
        let current_ordinal = queue.completed_suite_ids().len() + 1;
        batch_progress_copy(current_ordinal, selected_count)
    });

    FooterStatusModel {
        host_state,
        event_count: poll.map(|poll| poll.events.len()).unwrap_or_default(),
        pending_command: pending_command.and_then(non_empty_string),
        selected_suite_count: model.selected_suite_ids().len(),
        hidden_by_filter_count,
        batch_progress,
    }
}

fn footer_status_chip_texts(footer: &FooterStatusModel) -> Vec<String> {
    let mut chips = vec![
        format!(
            "Host: {}",
            footer.host_state.as_deref().unwrap_or("not-launched")
        ),
        format!("Selected: {}", footer.selected_suite_count),
    ];

    if footer.event_count > 0 {
        chips.push(format!("Events: {}", footer.event_count));
    }
    if footer.hidden_by_filter_count > 0 {
        chips.push(format!("Hidden: {}", footer.hidden_by_filter_count));
    }
    if let Some(command) = footer.pending_command.as_deref() {
        chips.push(format!("Pending: {command}"));
    }
    if let Some(progress) = footer.batch_progress.as_deref() {
        chips.push(format!("Batch: {progress}"));
    }

    chips
}

fn footer_status_strip(ui: &mut egui::Ui, footer: &FooterStatusModel) {
    ui.horizontal_wrapped(|ui| {
        for chip in footer_status_chip_texts(footer) {
            meta_chip(ui, &chip);
        }
    });
}

fn non_empty_string(value: &str) -> Option<String> {
    let trimmed = value.trim();
    if trimmed.is_empty() {
        None
    } else {
        Some(trimmed.to_string())
    }
}

fn active_suite_id(poll: Option<&StartupPollResult>, fallback_suite_id: &str) -> String {
    poll.and_then(|poll| {
        poll.events
            .iter()
            .rev()
            .find(|event| !event.suite_id.trim().is_empty())
            .map(|event| event.suite_id.trim().to_string())
    })
    .unwrap_or_else(|| fallback_suite_id.trim().to_string())
}

fn recent_event_log_row_count(poll: Option<&StartupPollResult>) -> usize {
    poll.map(|poll| poll.events.len()).unwrap_or_default()
}

fn recent_event_log_scroll_max_height_px(operator_phase: OperatorPhase) -> f32 {
    if matches!(
        operator_phase,
        OperatorPhase::Running | OperatorPhase::ReviewRequired | OperatorPhase::HostError
    ) {
        RECENT_EVENT_LOG_EXPANDED_HEIGHT_PX
    } else {
        RECENT_EVENT_LOG_MAX_HEIGHT_PX
    }
}

fn review_summary_for_poll(
    poll: Option<&StartupPollResult>,
    session_paths: &SmokeSessionPaths,
    fallback_suite_id: &str,
) -> Option<ReviewSummaryModel> {
    let poll = poll?;
    let host_state = poll.host_state.as_ref()?;
    if host_state.state != HOST_STATE_REVIEW_REQUIRED || host_state.active_run_id.trim().is_empty()
    {
        return None;
    }
    if let Ok(summary) = load_review_summary_for_run(session_paths, &host_state.active_run_id) {
        return Some(summary);
    }
    let suite_id = active_suite_id(Some(poll), fallback_suite_id);
    Some(ReviewSummaryModel {
        run_id: host_state.active_run_id.trim().to_string(),
        suite_id: suite_id.clone(),
        suite_label: suite_id,
        run_result: "unknown".to_string(),
        failure_excerpt: None,
    })
}

fn current_utc_rfc3339() -> String {
    let seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_else(|_| Duration::from_secs(0))
        .as_secs() as i64;
    let days = seconds.div_euclid(86_400);
    let day_seconds = seconds.rem_euclid(86_400);
    let (year, month, day) = civil_from_days(days);
    let hour = day_seconds / 3600;
    let minute = (day_seconds % 3600) / 60;
    let second = day_seconds % 60;
    format!("{year:04}-{month:02}-{day:02}T{hour:02}:{minute:02}:{second:02}Z")
}

fn civil_from_days(days_since_epoch: i64) -> (i64, i64, i64) {
    let z = days_since_epoch + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = z - era * 146_097;
    let yoe = (doe - doe / 1_460 + doe / 36_524 - doe / 146_096) / 365;
    let y = yoe + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let day = doy - (153 * mp + 2) / 5 + 1;
    let month = mp + if mp < 10 { 3 } else { -9 };
    let year = y + if month <= 2 { 1 } else { 0 };
    (year, month, day)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::catalog::load_canonical_catalog;
    use crate::session::SmokeHostStateDocument;

    #[test]
    fn suite_filter_summary_stays_human_friendly_about_selected_and_visible_counts() {
        assert_eq!(
            suite_filter_summary_text(4, 12, 0),
            "Selected suites: 4 selected · 12 visible"
        );
        assert_eq!(
            suite_filter_summary_text(4, 1, 11),
            "Selected suites: 4 selected · 1 visible · 11 hidden by filters"
        );
    }

    #[test]
    fn suite_preset_buttons_use_locked_catalog_preset_tokens() {
        let specs = suite_preset_button_specs();
        let labels: Vec<&str> = specs.iter().map(|spec| spec.label).collect();
        let preset_groups: Vec<&str> = specs.iter().map(|spec| spec.preset_group).collect();

        assert_eq!(
            labels,
            vec![
                "Quick default",
                "All setup",
                "Safe negatives",
                "Destructive drills"
            ]
        );
        assert_eq!(
            preset_groups,
            vec![
                "quick-default",
                "all-setup",
                "safe-negatives",
                "destructive-drills"
            ]
        );
    }

    #[test]
    fn suite_preset_selection_keeps_full_suite_list_visible() {
        let catalog = load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let mut speed_filter = Some(SuiteSpeedFilter::Quick);

        apply_suite_preset_selection(&mut model, &mut speed_filter, "all-setup")
            .expect("preset should apply");

        let filter = SuiteChecklistFilter {
            search_query: String::new(),
            speed_filter,
        };
        let checklist = build_filtered_suite_checklist_view(&model, &filter);

        assert_eq!(speed_filter, None);
        assert_eq!(
            checklist.visible_suite_ids().len(),
            model.available_suite_ids().len()
        );
        assert_eq!(checklist.hidden_by_filter_count, 0);
        assert_eq!(
            model.selected_suite_ids(),
            vec![
                "setup-scene-avatar",
                "avatar-discovery-selection-regression",
                "add-prefab-idempotency",
                "setup-existing-state-recognition",
                "setup-generated-asset-readiness",
                "setup-negative-diagnostics"
            ]
        );
    }

    #[test]
    fn native_window_keeps_default_width_and_allows_resize() {
        let options = native_options();
        let viewport = options.viewport;
        let debug = format!("{viewport:?}");
        assert!(debug.contains("480"));
        assert!(debug.contains("resizable: Some(true)"));
    }

    #[test]
    fn dashboard_cards_span_resized_window_while_preserving_edge_padding() {
        let layout = full_width_card_layout(900.0, CARD_MARGIN_PX);

        assert_eq!(layout.outer_width_px, 900.0);
        assert_eq!(layout.horizontal_inner_margin_px, CARD_MARGIN_PX);
        assert_eq!(layout.content_min_width_px, 900.0 - (CARD_MARGIN_PX * 2.0));
        assert_eq!(operator_density_spec().outer_margin_px, OUTER_MARGIN_PX);
    }

    #[test]
    fn current_suite_info_callout_wraps_long_copy_inside_card() {
        let spec = current_suite_info_callout_visual_spec();

        assert!(spec.horizontal_wrapped);
    }

    #[test]
    fn operator_phase_maps_host_and_supervisor_states() {
        assert_eq!(
            operator_phase_for_host_state(None, None, false, false),
            OperatorPhase::NotLaunched
        );
        assert_eq!(
            operator_phase_for_host_state(
                None,
                Some(UnityHostSupervisorStatus::Starting),
                false,
                false
            ),
            OperatorPhase::NotLaunched
        );
        assert_eq!(
            operator_phase_for_host_state(
                None,
                Some(UnityHostSupervisorStatus::Starting),
                false,
                true
            ),
            OperatorPhase::Starting
        );
        assert_eq!(
            operator_phase_for_host_state(
                None,
                Some(UnityHostSupervisorStatus::TimedOut),
                false,
                false
            ),
            OperatorPhase::HostError
        );

        let ready = host_state(HOST_STATE_READY, "");
        let idle = host_state(HOST_STATE_IDLE, "");
        let running = host_state(HOST_STATE_RUNNING, "run-0001-suite");
        let review = host_state(HOST_STATE_REVIEW_REQUIRED, "run-0001-suite");
        let crashed = host_state(HOST_STATE_CRASHED, "");
        assert_eq!(
            operator_phase_for_host_state(
                Some(&ready),
                Some(UnityHostSupervisorStatus::Ready),
                false,
                true,
            ),
            OperatorPhase::Ready
        );
        assert_eq!(
            operator_phase_for_host_state(
                Some(&idle),
                Some(UnityHostSupervisorStatus::Ready),
                false,
                true,
            ),
            OperatorPhase::Ready
        );
        assert_eq!(
            operator_phase_for_host_state(
                Some(&running),
                Some(UnityHostSupervisorStatus::Ready),
                false,
                true,
            ),
            OperatorPhase::Running
        );
        assert_eq!(
            operator_phase_for_host_state(
                Some(&review),
                Some(UnityHostSupervisorStatus::Ready),
                false,
                true,
            ),
            OperatorPhase::ReviewRequired
        );
        assert_eq!(
            operator_phase_for_host_state(
                Some(&crashed),
                Some(UnityHostSupervisorStatus::Ready),
                false,
                true,
            ),
            OperatorPhase::HostError
        );
    }

    #[test]
    fn controls_enable_abort_only_while_running_without_pending_command() {
        let running = host_state(HOST_STATE_RUNNING, "run-0001-suite");
        let controls = control_state_for_host_state(
            Some(&running),
            Some(UnityHostSupervisorStatus::Starting),
            false,
            true,
        );
        assert!(controls.can_abort);
        assert!(!controls.can_run_suite);
        assert!(!controls.can_review);
        assert!(!controls.can_edit_step_sleep_timer);

        let pending = control_state_for_host_state(
            Some(&running),
            Some(UnityHostSupervisorStatus::Starting),
            true,
            true,
        );
        assert!(!pending.can_abort);
    }

    #[test]
    fn controls_enable_review_only_in_review_required() {
        let review = host_state(HOST_STATE_REVIEW_REQUIRED, "run-0001-suite");
        let controls = control_state_for_host_state(
            Some(&review),
            Some(UnityHostSupervisorStatus::Ready),
            false,
            true,
        );
        assert!(controls.can_review);
        assert!(!controls.can_run_suite);
        assert!(!controls.can_abort);
        assert!(controls.can_edit_step_sleep_timer);
    }

    #[test]
    fn controls_enable_relaunch_for_stalled_or_crashed() {
        let stalled = host_state(HOST_STATE_STALLED, "");
        let controls = control_state_for_host_state(
            Some(&stalled),
            Some(UnityHostSupervisorStatus::Stalled),
            false,
            true,
        );
        assert!(controls.can_relaunch);
        assert!(!controls.can_run_suite);
    }

    #[test]
    fn controls_enable_exit_before_and_after_host_launch() {
        let boot_controls = control_state_for_host_state(None, None, false, false);
        assert!(boot_controls.can_exit);
        assert!(boot_controls.can_launch);

        let startup_controls = control_state_for_host_state(
            None,
            Some(UnityHostSupervisorStatus::Starting),
            false,
            true,
        );
        assert!(!startup_controls.can_launch);
        assert!(startup_controls.can_exit);

        let ready_controls = control_state_for_host_state(
            Some(&host_state(HOST_STATE_READY, "")),
            Some(UnityHostSupervisorStatus::Ready),
            false,
            true,
        );
        assert!(!ready_controls.can_launch);
        assert!(ready_controls.can_exit);
        assert!(ready_controls.can_edit_step_sleep_timer);

        let running = host_state(HOST_STATE_RUNNING, "run-0001-suite");
        let running_controls = control_state_for_host_state(
            Some(&running),
            Some(UnityHostSupervisorStatus::Starting),
            false,
            true,
        );
        assert!(running_controls.can_exit);

        let review = host_state(HOST_STATE_REVIEW_REQUIRED, "run-0001-suite");
        let review_controls = control_state_for_host_state(
            Some(&review),
            Some(UnityHostSupervisorStatus::Ready),
            false,
            true,
        );
        assert!(review_controls.can_exit);
    }

    #[test]
    fn status_visual_model_uses_redundant_icon_and_label_cues() {
        let ready = status_visual_model(HOST_STATE_READY);
        assert!(is_private_icon_glyph(ready.icon));
        assert_eq!(ready.headline, "READY");
        assert_eq!(ready.tone, StatusTone::Success);

        let running = status_visual_model(HOST_STATE_RUNNING);
        assert!(is_private_icon_glyph(running.icon));
        assert_eq!(running.headline, "RUNNING");
        assert_eq!(running.tone, StatusTone::Accent);

        let crashed = status_visual_model(HOST_STATE_CRASHED);
        assert!(is_private_icon_glyph(crashed.icon));
        assert_eq!(crashed.headline, "CRASHED");
        assert_eq!(crashed.tone, StatusTone::Danger);

        let review = status_visual_model(HOST_STATE_REVIEW_REQUIRED);
        assert!(is_private_icon_glyph(review.icon));
        assert_eq!(review.headline, "REVIEW REQUIRED");
        assert_eq!(review.tone, StatusTone::Warning);
    }

    #[test]
    fn status_card_support_copy_uses_operator_facing_phase_labels() {
        let ready = status_card_view_model(HOST_STATE_READY, "Unity host ready.");
        assert_eq!(ready.phase_label, "Unity host idle");

        let running = status_card_view_model(HOST_STATE_RUNNING, "Running selected suite.");
        assert_eq!(running.phase_label, "Batch in progress");

        let review = status_card_view_model(HOST_STATE_REVIEW_REQUIRED, "Suite entered review.");
        assert_eq!(review.phase_label, "Suite needs review");
        assert_ne!(review.phase_label, HOST_STATE_REVIEW_REQUIRED);

        let not_launched = status_card_view_model("not-launched", "Launch Unity host.");
        assert_eq!(not_launched.phase_label, "Awaiting launch");
    }

    #[test]
    fn review_summary_visual_model_separates_suite_failure_from_host_crash() {
        let failed = review_result_visual_model("failed");
        assert_eq!(failed.headline, "FAILED");
        assert_eq!(failed.tone, StatusTone::Warning);

        let aborted = review_result_visual_model("aborted");
        assert_eq!(aborted.headline, "ABORTED");
        assert_eq!(aborted.tone, StatusTone::Warning);

        let passed = review_result_visual_model("passed");
        assert_eq!(passed.tone, StatusTone::Success);
    }

    #[test]
    fn review_failure_excerpt_uses_warning_tone_not_host_error_danger() {
        assert_eq!(review_failure_excerpt_color(), WARNING);
        assert_ne!(review_failure_excerpt_color(), DANGER);
    }

    #[test]
    fn status_message_keeps_telemetry_out_of_primary_copy() {
        let poll = StartupPollResult {
            status: UnityHostSupervisorStatus::Ready,
            host_state: Some(host_state(HOST_STATE_READY, "run-123")),
            events: Vec::new(),
            warnings: Vec::new(),
        };

        let message = status_message_from_poll(&poll);
        assert_eq!(message, "state");
        assert!(!message.contains("activeRun="));
        assert!(!message.contains("eventSeq="));
        assert!(!message.contains("commandSeq="));
    }

    #[test]
    fn operator_density_keeps_panel_narrow_with_reference_touch_targets() {
        let density = operator_density_spec();

        assert_eq!(density.panel_width_px, 480.0);
        assert!(density.outer_margin_px >= 16.0);
        assert!(density.card_margin_px >= 14.0);
        assert!(density.section_gap_px >= 12.0);
        assert!(density.primary_button_height_px >= 44.0);
    }

    #[test]
    fn ambient_glow_spec_is_stronger_scifi_blue_stack() {
        let glow = ambient_glow_spec();

        assert!(glow.len() >= 3);
        assert!(glow.iter().any(|layer| layer.alpha >= 44));
        assert!(glow.iter().any(|layer| layer.radius_factor >= 0.70));
    }

    #[test]
    fn destructive_button_style_uses_contrast_safe_danger_fill() {
        let style = action_button_style(ControlActionRole::Destructive);
        assert_eq!(style.fill, egui::Color32::from_rgb(185, 28, 28));
        assert_eq!(style.text_color, egui::Color32::WHITE);
    }

    #[test]
    fn primary_button_style_uses_smart_panel_active_blue() {
        let style = action_button_style(ControlActionRole::Primary);
        assert_eq!(style.fill, ACTIVE_BLUE);
        assert_eq!(style.text_color, egui::Color32::WHITE);
    }

    #[test]
    fn dashboard_section_labels_are_uppercase_operator_labels() {
        assert_eq!(section_label_text("Current Run Plan"), "CURRENT RUN PLAN");
        assert_eq!(section_label_text("Suites"), "SUITES");
    }

    #[test]
    fn dashboard_section_palette_uses_restrained_neutral_chrome() {
        for tone in [
            DashboardSectionTone::Gold,
            DashboardSectionTone::Cream,
            DashboardSectionTone::Green,
            DashboardSectionTone::Orange,
            DashboardSectionTone::Blue,
            DashboardSectionTone::Lavender,
        ] {
            assert_eq!(dashboard_section_fill(tone), PANEL_RAISED);
            assert_eq!(dashboard_section_text(tone), MUTED);
        }
    }

    #[test]
    fn disabled_action_style_stays_neutral_so_unavailable_controls_recede() {
        let style = action_button_style_for_action(&ControlActionModel {
            id: ControlActionId::LaunchHost,
            label: "Launch Unity Host".to_string(),
            role: ControlActionRole::Primary,
            enabled: false,
            disabled_reason: Some("not available"),
        });

        assert_eq!(style.fill, egui::Color32::from_rgb(22, 33, 51));
        assert_eq!(
            style.stroke,
            egui::Stroke::new(1.0, egui::Color32::from_rgb(35, 52, 77))
        );
        assert_eq!(style.text_color, DISABLED_TEXT);
    }

    #[test]
    fn phase_cta_model_promotes_one_expected_primary_action_per_phase() {
        let controls = all_controls_enabled();

        assert_eq!(
            phase_primary_action_ids(OperatorPhase::NotLaunched, controls),
            vec![ControlActionId::LaunchHost]
        );
        assert_eq!(
            phase_primary_action_ids(OperatorPhase::Ready, controls),
            vec![ControlActionId::RunSelectedSuite]
        );
        assert_eq!(
            phase_primary_action_ids(OperatorPhase::Running, controls),
            vec![ControlActionId::AbortRun]
        );
        assert_eq!(
            phase_primary_action_ids(OperatorPhase::ReviewRequired, controls),
            vec![ControlActionId::RerunSuite]
        );
        assert_eq!(
            phase_primary_action_ids(OperatorPhase::HostError, controls),
            vec![ControlActionId::RelaunchHost]
        );
        assert!(phase_primary_action_ids(OperatorPhase::Starting, controls).is_empty());
    }

    #[test]
    fn review_required_phase_keeps_return_as_decision_peer_not_generic_control() {
        let controls = all_controls_enabled();
        let actions = phase_control_action_models(OperatorPhase::ReviewRequired, controls);
        let return_action = actions
            .iter()
            .find(|action| action.action.id == ControlActionId::ReturnToSuiteList)
            .expect("return decision should exist");

        assert_eq!(return_action.emphasis, ControlActionEmphasis::DecisionPeer);
    }

    #[test]
    fn review_required_phase_offers_export_logs_before_rerun_and_return() {
        let controls = all_controls_enabled();
        let actions = phase_control_action_models(OperatorPhase::ReviewRequired, controls);
        let ids: Vec<ControlActionId> = actions
            .iter()
            .filter(|action| action.emphasis != ControlActionEmphasis::Standard)
            .map(|action| action.action.id)
            .collect();

        assert_eq!(
            ids,
            vec![
                ControlActionId::ExportLogs,
                ControlActionId::RerunSuite,
                ControlActionId::ReturnToSuiteList,
            ]
        );
        let export = actions
            .iter()
            .find(|action| action.action.id == ControlActionId::ExportLogs)
            .expect("export logs action should exist");
        assert_eq!(export.action.label, "Export Logs");
        assert!(export.action.enabled);
        assert_eq!(export.emphasis, ControlActionEmphasis::DecisionPeer);
    }

    #[test]
    fn action_tiles_use_redundant_icons_for_state_recognition() {
        for id in [
            ControlActionId::LaunchHost,
            ControlActionId::RunSelectedSuite,
            ControlActionId::AbortRun,
            ControlActionId::ExportLogs,
            ControlActionId::ReturnToSuiteList,
            ControlActionId::RerunSuite,
            ControlActionId::Exit,
            ControlActionId::RelaunchHost,
        ] {
            let icon = control_action_icon(id);
            assert!(is_private_icon_glyph(icon));
            assert_ne!(icon, "▶");
            assert_ne!(icon, "■");
            assert_ne!(icon, "✕");
            assert_ne!(icon, "↻");
        }
    }

    #[test]
    fn suite_picker_typography_matches_current_run_plan_hierarchy() {
        let spec = suite_picker_typography_spec();

        assert_eq!(spec.summary_text_color, MUTED);
        assert_eq!(spec.summary_text_size_px, META_TEXT_SIZE);
        assert_eq!(spec.group_header_text_color, TEXT);
        assert_eq!(spec.group_header_text_size_px, BODY_TEXT_SIZE);
        assert!(spec.group_header_bold);
        assert!(spec.group_header_uses_collapsing_header);
        assert_eq!(spec.row_text_color, TEXT);
        assert_eq!(spec.row_text_size_px, META_TEXT_SIZE);
        assert_eq!(suite_group_header_text("Setup", 1), "Setup · 1 suite");
    }

    #[test]
    fn suite_picker_dropdowns_use_phosphor_open_and_closed_icons() {
        assert_eq!(suite_group_dropdown_icon(true), icons::CARET_DOWN);
        assert_eq!(suite_group_dropdown_icon(false), icons::CARET_RIGHT);
        assert!(is_private_icon_glyph(suite_group_dropdown_icon(true)));
        assert!(is_private_icon_glyph(suite_group_dropdown_icon(false)));
        assert_ne!(
            suite_group_dropdown_icon(true),
            suite_group_dropdown_icon(false)
        );
    }

    #[test]
    fn current_run_plan_step_numbers_are_plain_bold_with_phosphor_separator() {
        let spec = current_run_plan_step_visual_spec();

        assert!(spec.ordinal_bold);
        assert!(!spec.ordinal_uses_bubble_frame);
        assert_eq!(spec.separator_icon, icons::CARET_RIGHT);
        assert!(is_private_icon_glyph(spec.separator_icon));
        assert!(!spec.renders_trailing_step_index);
        assert_eq!(
            current_run_plan_step_label("Click ME scene is open", 1, 3),
            "Click ME scene is open"
        );
    }

    #[test]
    fn suite_checkbox_row_label_keeps_suite_name_but_drops_trailing_membership_clutter() {
        let row = SuiteRowView {
            group_id: "setup".to_string(),
            group_label: "Setup".to_string(),
            suite_id: "setup-scene-avatar".to_string(),
            suite_label: "Setup Scene / Avatar / Prefab".to_string(),
            suite_description: "Bootstrap the canonical scene.".to_string(),
            speed: "quick".to_string(),
            risk: "safe".to_string(),
            default_selected: true,
            preset_groups: vec!["quick-default".to_string(), "all-setup".to_string()],
            destructive: false,
            selectable: true,
            disabled_reason: None,
            selected: true,
            checked: true,
            focused: true,
            selected_order: Some(1),
        };

        assert_eq!(
            suite_checkbox_row_label(&row),
            "Setup Scene / Avatar / Prefab #1 · current"
        );
    }

    #[test]
    fn selected_suite_checkbox_visual_uses_high_contrast_active_state() {
        let checked = suite_checkbox_visual_spec(true, false, false);
        let unchecked = suite_checkbox_visual_spec(false, false, false);
        let disabled_checked = suite_checkbox_visual_spec(true, false, true);

        assert_eq!(checked.icon, icons::CHECK_SQUARE);
        assert_eq!(unchecked.icon, icons::SQUARE);
        assert_eq!(checked.icon_color, ACTIVE_BLUE);
        assert_eq!(checked.text_color, TEXT);
        assert_ne!(checked.icon_color, CARD_BORDER);
        assert_ne!(
            checked.icon_color,
            dashboard_section_fill(DashboardSectionTone::Gold)
        );
        assert_eq!(unchecked.icon_color, MUTED);
        assert_eq!(unchecked.text_color, TEXT);
        assert_eq!(disabled_checked.icon_color, DISABLED_TEXT);
        assert!(checked.icon_size_px > BODY_TEXT_SIZE);
    }

    #[test]
    fn dashboard_section_order_ends_at_run_monitor_before_footer() {
        assert_eq!(overlay_section_order().len(), 7);
        assert_eq!(
            overlay_section_order_for_phase(OperatorPhase::Running).len(),
            7
        );
    }

    #[test]
    fn overlay_section_order_matches_reference_dashboard_shell() {
        assert_eq!(
            overlay_section_order(),
            vec![
                OverlaySectionId::HeaderStatus,
                OverlaySectionId::CurrentRunPlan,
                OverlaySectionId::SuitePicker,
                OverlaySectionId::CurrentSuite,
                OverlaySectionId::RunMonitor,
                OverlaySectionId::Controls,
                OverlaySectionId::FooterStatus,
            ]
        );
    }

    #[test]
    fn dashboard_no_longer_renders_running_now_card() {
        let removed_title = ["Running", "now"].join(" ");
        assert!(!include_str!("gui.rs").contains(&removed_title));
    }

    #[test]
    fn running_suite_dashboard_layout_focuses_current_suite_flow() {
        assert_eq!(
            overlay_section_order_for_phase(OperatorPhase::Running),
            vec![
                OverlaySectionId::HeaderStatus,
                OverlaySectionId::CurrentSuite,
                OverlaySectionId::RunMonitor,
                OverlaySectionId::CurrentRunPlan,
                OverlaySectionId::SuitePicker,
                OverlaySectionId::Controls,
                OverlaySectionId::FooterStatus,
            ]
        );
        assert!(dashboard_card_collapsed_for_phase(
            OperatorPhase::Running,
            OverlaySectionId::CurrentRunPlan
        ));
        assert!(dashboard_card_collapsed_for_phase(
            OperatorPhase::Running,
            OverlaySectionId::SuitePicker
        ));
        assert!(!dashboard_card_collapsed_for_phase(
            OperatorPhase::Running,
            OverlaySectionId::CurrentSuite
        ));
    }

    #[test]
    fn ready_dashboard_keeps_plan_and_suite_picker_expanded_before_current_suite() {
        assert_eq!(
            overlay_section_order_for_phase(OperatorPhase::Ready),
            overlay_section_order()
        );
        assert!(!dashboard_card_collapsed_for_phase(
            OperatorPhase::Ready,
            OverlaySectionId::CurrentRunPlan
        ));
        assert!(!dashboard_card_collapsed_for_phase(
            OperatorPhase::Ready,
            OverlaySectionId::SuitePicker
        ));
    }

    #[test]
    fn visual_dashboard_composition_matches_reference_stack() {
        assert_eq!(
            reference_dashboard_component_stack(),
            vec![
                ReferenceDashboardComponentId::HeroHeader,
                ReferenceDashboardComponentId::LiveStatusCard,
                ReferenceDashboardComponentId::CurrentRunPlanCard,
                ReferenceDashboardComponentId::SuitesTreeCard,
                ReferenceDashboardComponentId::CurrentSuiteBriefingCard,
                ReferenceDashboardComponentId::UtilitiesCard,
                ReferenceDashboardComponentId::ActionsDock,
                ReferenceDashboardComponentId::FooterStatusStrip,
            ]
        );
    }

    #[test]
    fn visual_header_and_status_models_match_reference_hierarchy() {
        let header = reference_header_model();
        assert_eq!(header.title, "ASM-Lite Smoke Overlay");
        assert!(header.subtitle.contains("operator dashboard"));

        let status = status_card_view_model("starting", "Waiting for Unity host-state.");
        assert_eq!(status.headline, "STARTING");
        assert_eq!(status.tone, StatusTone::Accent);
        assert_eq!(status.subtext, "Waiting for Unity host-state.");
        assert!(!status.subtext.contains("activeRun="));
        assert!(!status.subtext.contains("commandSeq="));
    }

    #[test]
    fn visual_hero_status_prominence_uses_reference_scale() {
        let spec = hero_prominence_spec();

        assert!(spec.title_text_size_px >= 22.0);
        assert!(spec.status_card_width_px >= 188.0);
        assert!(spec.status_icon_size_px >= 28.0);
        assert!(spec.hero_card_padding_px >= 10.0);
    }

    #[test]
    fn hero_and_live_status_render_as_separate_stacked_cards() {
        let layout = hero_status_layout_spec();

        assert_eq!(layout.card_count, 2);
        assert!(layout.inter_card_gap_px >= RELATED_GAP_PX);
        assert!(layout.header_card_full_width);
        assert!(layout.status_card_full_width);
    }

    #[test]
    fn suite_navigation_markers_use_phosphor_icons() {
        let group = suite_group_header_text("Setup", 1);
        assert_eq!(group, "Setup · 1 suite");
        assert_eq!(suite_group_dropdown_icon(true), icons::CARET_DOWN);
        assert_eq!(suite_group_dropdown_icon(false), icons::CARET_RIGHT);
        assert!(is_private_icon_glyph(suite_group_dropdown_icon(true)));
        assert!(is_private_icon_glyph(suite_group_dropdown_icon(false)));
        assert_eq!(suite_row_indent_marker(), icons::DOT_OUTLINE);
        assert_eq!(current_suite_badge_text("unknown-suite"), icons::DIAMOND);
        assert!(!group.contains('\u{25BE}'));
        assert!(!suite_row_indent_marker().contains('\u{2502}'));
        assert!(!current_suite_badge_text("unknown-suite").contains('\u{25C6}'));
    }

    #[test]
    fn review_result_icons_use_phosphor_and_avoid_decorative_unicode() {
        for result in ["passed", "failed", "aborted", "unknown"] {
            let icon = review_result_visual_model(result).icon;
            assert!(is_private_icon_glyph(icon), "{result} used {icon:?}");
            assert_ne!(icon, "✓");
            assert_ne!(icon, "!");
            assert_ne!(icon, "x");
            assert_ne!(icon, "?");
        }
    }

    #[test]
    fn current_suite_badge_icons_match_requested_phosphor_font_source() {
        assert_eq!(icons::CUBE, "\u{E1DA}");
        assert_eq!(icons::ARROWS_CLOCKWISE, "\u{E094}");
        assert_eq!(icons::MONITOR_PLAY, "\u{E58C}");

        assert_eq!(current_suite_badge_text("setup-scene-avatar"), icons::CUBE);
        assert_eq!(
            current_suite_badge_text("lifecycle-roundtrip"),
            icons::ARROWS_CLOCKWISE
        );
        assert_eq!(
            current_suite_badge_text("playmode-runtime-validation"),
            icons::MONITOR_PLAY
        );
        assert!(is_private_icon_glyph(current_suite_badge_text(
            "setup-scene-avatar"
        )));
        assert!(is_private_icon_glyph(current_suite_badge_text(
            "lifecycle-roundtrip"
        )));
        assert!(is_private_icon_glyph(current_suite_badge_text(
            "playmode-runtime-validation"
        )));
        assert!(!current_suite_badge_text("setup-scene-avatar").contains('\u{25C6}'));
    }

    #[test]
    fn current_suite_steps_section_renames_expected_area() {
        assert_eq!(current_suite_steps_section_label(), "STEPS");
    }

    #[test]
    fn current_suite_steps_use_catalog_steps_and_event_status_colors() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model
            .set_current_suite_id_preserving_batch("lifecycle-roundtrip")
            .expect("current suite should move inside batch");
        let poll = StartupPollResult {
            status: UnityHostSupervisorStatus::Ready,
            host_state: Some(host_state(
                HOST_STATE_RUNNING,
                "run-0001-lifecycle-roundtrip",
            )),
            events: vec![
                step_event(1, "lifecycle-roundtrip", "rebuild", "step-passed"),
                step_event(2, "lifecycle-roundtrip", "vendorize", "step-started"),
                step_event(3, "lifecycle-roundtrip", "detach", "step-failed"),
            ],
            warnings: Vec::new(),
        };

        let briefing = build_current_suite_briefing_model_for_poll(&model, Some(&poll))
            .expect("briefing should exist");

        assert_eq!(briefing.steps[0].label, "ASM-Lite assets are rebuilt");
        assert_eq!(briefing.steps[0].status, CurrentSuiteStepStatus::Passed);
        assert_eq!(briefing.steps[0].icon, icons::CHECK_CIRCLE);
        assert_eq!(current_suite_step_color(briefing.steps[0].status), SUCCESS);
        assert_eq!(briefing.steps[2].label, "Generated assets are vendorized");
        assert_eq!(briefing.steps[2].status, CurrentSuiteStepStatus::Running);
        assert_eq!(briefing.steps[4].label, "ASM-Lite component is detached");
        assert_eq!(briefing.steps[4].status, CurrentSuiteStepStatus::Failed);
        assert_eq!(briefing.steps[4].icon, icons::X_CIRCLE);
        assert_eq!(current_suite_step_color(briefing.steps[4].status), DANGER);
        assert!(
            current_suite_step_label(&briefing.steps[0]).contains("ASM-Lite assets are rebuilt")
        );
    }

    #[test]
    fn current_suite_steps_ignore_stale_events_from_prior_runs() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let mut stale = step_event(1, "setup-scene-avatar", "open-scene", "step-passed");
        stale.run_id = "run-0001-setup-scene-avatar".to_string();
        let poll = StartupPollResult {
            status: UnityHostSupervisorStatus::Ready,
            host_state: Some(host_state(
                HOST_STATE_RUNNING,
                "run-0002-setup-scene-avatar",
            )),
            events: vec![stale],
            warnings: Vec::new(),
        };

        let briefing = build_current_suite_briefing_model_for_poll(&model, Some(&poll))
            .expect("briefing should exist");

        assert_eq!(briefing.steps[0].label, "ASM-Lite is open");
        assert_eq!(briefing.steps[0].status, CurrentSuiteStepStatus::Pending);
    }

    #[test]
    fn action_tiles_use_uniform_smaller_size_for_aligned_bottom_dock() {
        let expected = egui::vec2(ACTION_TILE_WIDTH_PX, ACTION_TILE_HEIGHT_PX);

        assert_eq!(
            action_button_size(ControlActionEmphasis::PhasePrimary),
            expected
        );
        assert_eq!(
            action_button_size(ControlActionEmphasis::DecisionPeer),
            expected
        );
        assert_eq!(
            action_button_size(ControlActionEmphasis::Standard),
            expected
        );
        assert!(expected.x < PHASE_PRIMARY_ACTION_TILE_WIDTH_PX);
        assert!(expected.y < PHASE_PRIMARY_ACTION_TILE_HEIGHT_PX);
    }

    #[test]
    fn all_action_tiles_keep_icon_and_text_on_one_line() {
        for phase_action in [
            PhaseControlActionModel {
                action: ControlActionModel {
                    id: ControlActionId::RunSelectedSuite,
                    label: "Run Selected Suite".to_string(),
                    role: ControlActionRole::Primary,
                    enabled: true,
                    disabled_reason: None,
                },
                emphasis: ControlActionEmphasis::PhasePrimary,
            },
            PhaseControlActionModel {
                action: ControlActionModel {
                    id: ControlActionId::ExportLogs,
                    label: "Export Logs".to_string(),
                    role: ControlActionRole::Secondary,
                    enabled: true,
                    disabled_reason: None,
                },
                emphasis: ControlActionEmphasis::DecisionPeer,
            },
            PhaseControlActionModel {
                action: ControlActionModel {
                    id: ControlActionId::LaunchHost,
                    label: "Launch Unity Host".to_string(),
                    role: ControlActionRole::Primary,
                    enabled: false,
                    disabled_reason: Some("not available"),
                },
                emphasis: ControlActionEmphasis::Standard,
            },
        ] {
            let label = action_button_label_for_action(&phase_action);
            assert!(!label.contains('\n'));
            assert!(label.contains(' '));
            assert_eq!(
                action_button_size_for_action(&phase_action),
                action_button_size(ControlActionEmphasis::Standard)
            );
        }
    }

    #[test]
    fn subsection_titles_render_as_plain_bold_text_without_badge_bubbles() {
        let spec = section_subheader_visual_spec();

        assert!(spec.bold_text);
        assert!(!spec.uses_bubble_frame);
        assert!(!spec.uses_background_fill);
        assert_eq!(spec.text_color, MUTED);
    }

    #[test]
    fn action_dock_uses_fixed_bottom_panel_above_footer_not_scroll_body() {
        let shell = action_dock_shell_spec();

        assert!(shell.fixed_bottom_panel);
        assert!(shell.above_footer_status_strip);
        assert!(!shell.inside_main_scroll_area);
    }

    #[test]
    fn visual_action_dock_separates_primary_decisions_secondary_and_exit() {
        let controls = all_controls_enabled();
        let dock = action_dock_layout_for_phase(OperatorPhase::ReviewRequired, controls, 2);

        assert_eq!(dock.primary_ids, vec![ControlActionId::RerunSuite]);
        assert_eq!(
            dock.decision_peer_ids,
            vec![
                ControlActionId::ExportLogs,
                ControlActionId::ReturnToSuiteList
            ]
        );
        assert!(dock.secondary_ids.is_empty());
        assert_eq!(dock.destructive_ids, vec![ControlActionId::Exit]);
    }

    #[test]
    fn action_dock_hides_irrelevant_controls_for_each_phase() {
        let controls = all_controls_enabled();

        let ready = action_dock_layout_for_phase(OperatorPhase::Ready, controls, 2);
        assert_eq!(ready.primary_ids, vec![ControlActionId::RunSelectedSuite]);
        assert!(ready.decision_peer_ids.is_empty());
        assert!(ready.secondary_ids.is_empty());
        assert_eq!(ready.destructive_ids, vec![ControlActionId::Exit]);

        let starting = action_dock_layout_for_phase(OperatorPhase::Starting, controls, 2);
        assert!(starting.primary_ids.is_empty());
        assert!(starting.decision_peer_ids.is_empty());
        assert!(starting.secondary_ids.is_empty());
        assert_eq!(starting.destructive_ids, vec![ControlActionId::Exit]);

        let host_error = action_dock_layout_for_phase(OperatorPhase::HostError, controls, 2);
        assert_eq!(host_error.primary_ids, vec![ControlActionId::RelaunchHost]);
        assert!(host_error.decision_peer_ids.is_empty());
        assert!(host_error.secondary_ids.is_empty());
        assert_eq!(host_error.destructive_ids, vec![ControlActionId::Exit]);
    }

    #[test]
    fn visual_current_suite_briefing_has_expected_outcomes_and_hygiene_context() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model
            .set_current_suite_id_preserving_batch("lifecycle-roundtrip")
            .expect("lifecycle should be a valid suite");

        let briefing = build_current_suite_briefing_model(&model).expect("briefing should exist");

        assert_eq!(briefing.suite_id, "lifecycle-roundtrip");
        assert!(briefing.title.contains("Rebuild"));
        assert!(briefing
            .expected_outcomes
            .iter()
            .any(|item| item.contains("Lifecycle actions")));
        assert!(briefing
            .expected_outcomes
            .iter()
            .any(|item| item.contains("hygiene cleanup")));
        assert!(briefing.info_note.is_empty());
    }

    #[test]
    fn current_suite_info_note_is_suppressed_to_reduce_clutter() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");

        let briefing = build_current_suite_briefing_model(&model).expect("briefing should exist");

        assert!(briefing.info_note.is_empty());
    }

    #[test]
    fn current_suite_info_callout_uses_legible_phosphor_symbol() {
        let spec = current_suite_info_callout_visual_spec();

        assert_eq!(spec.icon, icons::INFO);
        assert_ne!(spec.icon, "i");
        assert!(is_private_icon_glyph(spec.icon));
        assert_eq!(spec.icon_color, ACTIVE_BLUE);
        assert_eq!(spec.text_color, TEXT);
        assert!(spec.icon_size_px > META_TEXT_SIZE);
    }

    #[test]
    fn current_suite_info_callout_centers_icon_and_text_row() {
        let spec = current_suite_info_callout_visual_spec();

        assert_eq!(spec.row_vertical_align, egui::Align::Center);
        assert!(spec.horizontal_wrapped);
    }

    #[test]
    fn current_suite_running_phase_uses_compact_note_instead_of_framed_warning_box() {
        assert_eq!(
            current_suite_info_presentation(OperatorPhase::Running),
            CurrentSuiteInfoPresentation::InlineMuted
        );
        assert_eq!(
            current_suite_info_presentation(OperatorPhase::Ready),
            CurrentSuiteInfoPresentation::FramedCallout
        );
        assert_eq!(
            current_suite_info_presentation(OperatorPhase::ReviewRequired),
            CurrentSuiteInfoPresentation::FramedCallout
        );
    }

    #[test]
    fn recent_events_scroll_area_grows_for_live_run_monitor_states() {
        assert_eq!(
            recent_event_log_scroll_max_height_px(OperatorPhase::Ready),
            RECENT_EVENT_LOG_MAX_HEIGHT_PX
        );
        assert_eq!(
            recent_event_log_scroll_max_height_px(OperatorPhase::Running),
            RECENT_EVENT_LOG_EXPANDED_HEIGHT_PX
        );
        assert_eq!(
            recent_event_log_scroll_max_height_px(OperatorPhase::ReviewRequired),
            RECENT_EVENT_LOG_EXPANDED_HEIGHT_PX
        );
        assert_eq!(
            recent_event_log_scroll_max_height_px(OperatorPhase::HostError),
            RECENT_EVENT_LOG_EXPANDED_HEIGHT_PX
        );
        assert!(
            recent_event_log_scroll_max_height_px(OperatorPhase::Running)
                > recent_event_log_scroll_max_height_px(OperatorPhase::Ready)
        );
    }

    #[test]
    fn footer_status_model_summarizes_host_events_pending_selection_and_batch_progress() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        model
            .set_current_suite_id_preserving_batch("lifecycle-roundtrip")
            .expect("current suite should move inside batch");
        let mut queue = BatchRunQueue::new_from_selection(&model).expect("queue should initialize");
        queue.record_current_passed();
        let poll = StartupPollResult {
            status: UnityHostSupervisorStatus::Ready,
            host_state: Some(host_state(HOST_STATE_RUNNING, "run-123")),
            events: (1..=4).map(test_event).collect(),
            warnings: Vec::new(),
        };

        let footer =
            footer_status_model(Some(&poll), Some("command-0005"), &model, 11, Some(&queue));

        assert_eq!(footer.host_state.as_deref(), Some(HOST_STATE_RUNNING));
        assert_eq!(footer.event_count, 4);
        assert_eq!(footer.pending_command.as_deref(), Some("command-0005"));
        assert_eq!(footer.selected_suite_count, 4);
        assert_eq!(footer.hidden_by_filter_count, 11);
        assert_eq!(footer.batch_progress.as_deref(), Some("2 of 4"));
    }

    #[test]
    fn footer_batch_progress_copy_stays_human_friendly_and_count_only() {
        assert_eq!(batch_progress_copy(2, 4), Some("2 of 4".to_string()));
        assert_eq!(batch_progress_copy(0, 4), Some("1 of 4".to_string()));
        assert_eq!(batch_progress_copy(9, 4), Some("4 of 4".to_string()));
        assert_eq!(batch_progress_copy(1, 0), None);
    }

    #[test]
    fn recent_event_log_auto_opens_for_live_and_recovery_phases() {
        assert!(!recent_event_log_default_open(OperatorPhase::NotLaunched));
        assert!(!recent_event_log_default_open(OperatorPhase::Starting));
        assert!(!recent_event_log_default_open(OperatorPhase::Ready));
        assert!(recent_event_log_default_open(OperatorPhase::Running));
        assert!(recent_event_log_default_open(OperatorPhase::ReviewRequired));
        assert!(recent_event_log_default_open(OperatorPhase::HostError));
    }

    #[test]
    fn recent_event_log_forces_open_while_live_or_recovering() {
        assert_eq!(
            recent_event_log_forced_open(OperatorPhase::NotLaunched),
            None
        );
        assert_eq!(recent_event_log_forced_open(OperatorPhase::Starting), None);
        assert_eq!(recent_event_log_forced_open(OperatorPhase::Ready), None);
        assert_eq!(
            recent_event_log_forced_open(OperatorPhase::Running),
            Some(true)
        );
        assert_eq!(
            recent_event_log_forced_open(OperatorPhase::ReviewRequired),
            Some(true)
        );
        assert_eq!(
            recent_event_log_forced_open(OperatorPhase::HostError),
            Some(true)
        );
    }

    #[test]
    fn recent_event_log_entries_use_brief_timestamps_instead_of_sequence_prefixes() {
        let event = test_event(7);
        assert_eq!(
            brief_timestamp_label(&event.timestamp_utc),
            Some("04:37:09".to_string())
        );
        assert_eq!(
            format_recent_event_log_entry(&event),
            "04:37:09 step-complete — event 7"
        );
        assert!(!format_recent_event_log_entry(&event).starts_with('#'));
    }

    #[test]
    fn recent_event_log_timestamp_falls_back_to_current_system_time_when_event_time_is_invalid() {
        let mut event = test_event(8);
        event.timestamp_utc = "not-a-timestamp".to_string();

        let prefix = current_system_time_brief();
        let rendered = format_recent_event_log_entry(&event);

        assert!(rendered.starts_with(&prefix));
        assert!(rendered.contains("step-complete — event 8"));
    }

    #[test]
    fn current_suite_copy_uses_review_safe_and_actionable_labels() {
        assert_eq!(run_selected_action_label(), "Run Selected");
        assert_eq!(
            rerun_from_first_selected_action_label(),
            "Rerun from First Selected"
        );
        assert_eq!(utilities_section_title(), "Utilities / Advanced");
        assert_eq!(review_evidence_title(), "Review Evidence");
        assert_eq!(run_monitor_title(), "Run Monitor");
        assert_eq!(recent_events_section_title(), "Recent Events");
        assert_eq!(debug_hint_section_title(), "Debug hint");
        assert_eq!(
            current_suite_empty_state_copy(),
            "Select a suite to preview its steps here."
        );
        assert!(current_suite_info_note().is_empty());
    }

    #[test]
    fn suite_picker_filter_empty_state_copy_stays_actionable() {
        assert_eq!(
            suite_filter_empty_state_copy(),
            "No suites match the current filters. Clear search or change speed filters."
        );
        assert!(suite_filter_empty_state_copy().contains("Clear search"));
        assert!(suite_filter_empty_state_copy().contains("speed filters"));
    }

    #[test]
    fn recent_event_log_empty_state_copy_stays_actionable() {
        assert_eq!(
            recent_event_log_empty_state_copy(),
            "No events yet. Run a suite to stream activity here."
        );
        assert!(recent_event_log_empty_state_copy().contains("Run a suite"));
        assert!(recent_event_log_empty_state_copy().contains("stream activity"));
    }

    #[test]
    fn review_copy_keeps_rerun_guidance_operator_facing() {
        let review = ReviewSummaryModel {
            run_id: "run-0002-playmode-runtime-validation".to_string(),
            suite_id: "playmode-runtime-validation".to_string(),
            suite_label: "Enter / Validate / Exit Playmode".to_string(),
            run_result: "failed".to_string(),
            failure_excerpt: None,
        };

        assert_eq!(
            review_followup_copy(),
            "Inspect Unity, then rerun from the first selected suite or return to the suite list."
        );
        assert!(review_followup_copy().contains("rerun from the first selected suite"));
        assert_eq!(
            run_monitor_review_heading(&review),
            "FAILED · Enter / Validate / Exit Playmode"
        );
        assert!(!run_monitor_review_heading(&review).contains("playmode-runtime-validation"));
        assert_eq!(
            run_monitor_failure_label("Runtime component is valid"),
            "Failed step: Runtime component is valid"
        );
    }

    #[test]
    fn destructive_drill_helper_copy_stays_explicit_about_toggle_and_confirm_gate() {
        assert_eq!(
            destructive_drills_helper_copy(false),
            "Destructive drills stay off until you explicitly enable them."
        );
        assert_eq!(
            destructive_drills_helper_copy(true),
            "Destructive drills still require a confirm before they run."
        );
        assert_eq!(destructive_drills_helper_color(false), MUTED);
        assert_eq!(destructive_drills_helper_color(true), WARNING);
    }

    #[test]
    fn footer_status_model_keeps_idle_batch_progress_empty() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let poll = StartupPollResult {
            status: UnityHostSupervisorStatus::Ready,
            host_state: Some(host_state(HOST_STATE_READY, "")),
            events: Vec::new(),
            warnings: Vec::new(),
        };

        let footer = footer_status_model(Some(&poll), None, &model, 0, None);

        assert_eq!(footer.host_state.as_deref(), Some(HOST_STATE_READY));
        assert_eq!(footer.event_count, 0);
        assert_eq!(footer.pending_command, None);
        assert_eq!(footer.selected_suite_count, 4);
        assert_eq!(footer.hidden_by_filter_count, 0);
        assert_eq!(footer.batch_progress, None);
    }

    #[test]
    fn footer_status_chip_copy_hides_zero_noise_but_keeps_actionable_signals() {
        let quiet_footer = FooterStatusModel {
            host_state: Some(HOST_STATE_READY.to_string()),
            event_count: 0,
            pending_command: None,
            selected_suite_count: 4,
            hidden_by_filter_count: 0,
            batch_progress: None,
        };
        assert_eq!(
            footer_status_chip_texts(&quiet_footer),
            vec!["Host: ready".to_string(), "Selected: 4".to_string(),]
        );

        let active_footer = FooterStatusModel {
            host_state: Some(HOST_STATE_RUNNING.to_string()),
            event_count: 4,
            pending_command: Some("command-0005".to_string()),
            selected_suite_count: 4,
            hidden_by_filter_count: 11,
            batch_progress: Some("2 of 4".to_string()),
        };
        assert_eq!(
            footer_status_chip_texts(&active_footer),
            vec![
                "Host: running".to_string(),
                "Selected: 4".to_string(),
                "Events: 4".to_string(),
                "Hidden: 11".to_string(),
                "Pending: command-0005".to_string(),
                "Batch: 2 of 4".to_string(),
            ]
        );
    }

    #[test]
    fn recent_event_log_keeps_all_events_available_for_scrollback() {
        let poll = StartupPollResult {
            status: UnityHostSupervisorStatus::Ready,
            host_state: None,
            events: (1..=7).map(test_event).collect(),
            warnings: Vec::new(),
        };

        assert_eq!(recent_event_log_row_count(Some(&poll)), 7);
    }

    #[test]
    fn suite_selection_control_state_disables_run_when_batch_empty() {
        let catalog = crate::catalog::load_canonical_catalog().expect("catalog should load");
        let mut model =
            SuiteSelectionModel::new_from_catalog(&catalog).expect("model should initialize");
        let ready_controls = OverlayControlState {
            can_launch: false,
            can_select_suite: true,
            can_run_suite: true,
            can_abort: false,
            can_review: false,
            can_exit: true,
            can_relaunch: false,
            can_edit_step_sleep_timer: true,
        };

        assert!(control_state_for_suite_selection(ready_controls, &model).can_run_suite);

        model.clear_suite_selection();
        let empty_controls = control_state_for_suite_selection(ready_controls, &model);
        assert!(empty_controls.can_select_suite);
        assert!(!empty_controls.can_run_suite);
    }

    #[test]
    fn ready_primary_cta_uses_locked_run_selected_label_and_zero_selection_is_disabled() {
        let ready_controls = OverlayControlState {
            can_launch: false,
            can_select_suite: true,
            can_run_suite: true,
            can_abort: false,
            can_review: false,
            can_exit: true,
            can_relaunch: false,
            can_edit_step_sleep_timer: true,
        };

        let actions = control_action_models_for_selected_batch(ready_controls, 3);
        let run = actions
            .iter()
            .find(|action| action.id == ControlActionId::RunSelectedSuite)
            .expect("run action should exist");
        assert_eq!(run.label.as_str(), "Run Selected");
        assert!(run.enabled);

        let empty_actions = control_action_models_for_selected_batch(ready_controls, 0);
        let empty_run = empty_actions
            .iter()
            .find(|action| action.id == ControlActionId::RunSelectedSuite)
            .expect("run action should exist");
        assert_eq!(empty_run.label.as_str(), "Run Selected");
        assert!(!empty_run.enabled);
    }

    #[test]
    fn review_primary_cta_uses_rerun_from_first_selected_label() {
        let review_controls = OverlayControlState {
            can_launch: false,
            can_select_suite: false,
            can_run_suite: false,
            can_abort: false,
            can_review: true,
            can_exit: true,
            can_relaunch: false,
            can_edit_step_sleep_timer: true,
        };

        let actions = control_action_models(review_controls);
        let rerun = actions
            .iter()
            .find(|action| action.id == ControlActionId::RerunSuite)
            .expect("rerun action should exist");
        assert_eq!(rerun.label.as_str(), "Rerun from First Selected");
        assert_eq!(rerun.role, ControlActionRole::Primary);
    }

    #[test]
    fn control_action_models_expose_primary_secondary_and_destructive_hierarchy() {
        let ready_controls = OverlayControlState {
            can_launch: false,
            can_select_suite: true,
            can_run_suite: true,
            can_abort: false,
            can_review: false,
            can_exit: true,
            can_relaunch: false,
            can_edit_step_sleep_timer: true,
        };
        let ready_actions = control_action_models(ready_controls);
        let launch = ready_actions
            .iter()
            .find(|action| action.id == ControlActionId::LaunchHost)
            .expect("launch action should exist");
        assert_eq!(
            launch.disabled_reason,
            Some("Unity host is already launched or a command is pending.")
        );
        let run = ready_actions
            .iter()
            .find(|action| action.id == ControlActionId::RunSelectedSuite)
            .expect("run action should exist");
        assert_eq!(run.role, ControlActionRole::Primary);
        assert!(run.enabled);
        assert_eq!(run.disabled_reason, None);
        let ready_exit = ready_actions
            .iter()
            .find(|action| action.id == ControlActionId::Exit)
            .expect("exit action should exist");
        assert_eq!(ready_exit.role, ControlActionRole::Destructive);
        assert!(ready_exit.enabled);

        let running_controls = OverlayControlState {
            can_launch: false,
            can_select_suite: false,
            can_run_suite: false,
            can_abort: true,
            can_review: false,
            can_exit: true,
            can_relaunch: false,
            can_edit_step_sleep_timer: false,
        };
        let running_actions = control_action_models(running_controls);
        let run_disabled = running_actions
            .iter()
            .find(|action| action.id == ControlActionId::RunSelectedSuite)
            .expect("run action should exist");
        assert_eq!(
            run_disabled.disabled_reason,
            Some("Run is available when the Unity host is ready and no command is pending.")
        );
        let abort = running_actions
            .iter()
            .find(|action| action.id == ControlActionId::AbortRun)
            .expect("abort action should exist");
        assert_eq!(abort.role, ControlActionRole::Destructive);
        assert!(abort.enabled);
        let running_exit = running_actions
            .iter()
            .find(|action| action.id == ControlActionId::Exit)
            .expect("exit action should exist");
        assert_eq!(running_exit.role, ControlActionRole::Destructive);
        assert!(running_exit.enabled);

        let review_controls = OverlayControlState {
            can_launch: false,
            can_select_suite: false,
            can_run_suite: false,
            can_abort: false,
            can_review: true,
            can_exit: true,
            can_relaunch: false,
            can_edit_step_sleep_timer: true,
        };
        let review_actions = control_action_models(review_controls);
        let rerun = review_actions
            .iter()
            .find(|action| action.id == ControlActionId::RerunSuite)
            .expect("rerun action should exist");
        assert_eq!(rerun.role, ControlActionRole::Primary);
        assert!(rerun.enabled);
        let exit = review_actions
            .iter()
            .find(|action| action.id == ControlActionId::Exit)
            .expect("exit action should exist");
        assert_eq!(exit.role, ControlActionRole::Destructive);
        assert!(exit.enabled);
    }

    fn all_controls_enabled() -> OverlayControlState {
        OverlayControlState {
            can_launch: true,
            can_select_suite: true,
            can_run_suite: true,
            can_abort: true,
            can_review: true,
            can_exit: true,
            can_relaunch: true,
            can_edit_step_sleep_timer: true,
        }
    }

    fn test_event(event_seq: i32) -> crate::protocol::SmokeProtocolEvent {
        crate::protocol::SmokeProtocolEvent {
            protocol_version: "1.0.0".to_string(),
            session_id: "session-test".to_string(),
            event_id: format!("event-{event_seq:03}"),
            event_seq,
            event_type: "step-complete".to_string(),
            timestamp_utc: "2026-04-23T04:37:09Z".to_string(),
            command_id: "command-test".to_string(),
            run_id: "run-test".to_string(),
            group_id: String::new(),
            suite_id: "suite-test".to_string(),
            case_id: String::new(),
            step_id: format!("step-{event_seq:03}"),
            effective_reset_policy: String::new(),
            host_state: HOST_STATE_RUNNING.to_string(),
            message: format!("event {event_seq}"),
            review_decision_options: Vec::new(),
            supported_capabilities: Vec::new(),
        }
    }

    fn step_event(
        event_seq: i32,
        suite_id: &str,
        step_id: &str,
        event_type: &str,
    ) -> crate::protocol::SmokeProtocolEvent {
        let mut event = test_event(event_seq);
        event.event_type = event_type.to_string();
        event.run_id = format!("run-0001-{suite_id}");
        event.group_id = if suite_id == "lifecycle-roundtrip" {
            "lifecycle".to_string()
        } else {
            String::new()
        };
        event.suite_id = suite_id.to_string();
        event.step_id = step_id.to_string();
        event
    }

    fn host_state(state: &str, active_run_id: &str) -> SmokeHostStateDocument {
        SmokeHostStateDocument {
            session_id: "session-test".to_string(),
            protocol_version: "1.0.0".to_string(),
            state: state.to_string(),
            host_version: "host".to_string(),
            unity_version: "unity".to_string(),
            heartbeat_utc: "2026-04-23T04:37:09Z".to_string(),
            last_event_seq: 1,
            last_command_seq: 1,
            active_run_id: active_run_id.to_string(),
            message: "state".to_string(),
        }
    }
}
