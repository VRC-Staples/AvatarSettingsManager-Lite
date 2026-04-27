pub const RIGHT_PANEL_WIDTH_PX: f32 = 480.0;
pub const ROW_SELECTED_PREFIX: &str = "▶";
pub const ROW_UNSELECTED_PREFIX: &str = " ";
pub const GROUP_HEADER_PREFIX: &str = "##";

pub const TITLE_TEXT_SIZE: f32 = 20.0;
pub const CARD_TITLE_TEXT_SIZE: f32 = 14.0;
pub const BODY_TEXT_SIZE: f32 = 13.0;
pub const META_TEXT_SIZE: f32 = 11.0;
pub const SECTION_GAP_PX: f32 = 12.0;
pub const RELATED_GAP_PX: f32 = 6.0;
pub const OUTER_MARGIN_PX: f32 = 16.0;
pub const CARD_MARGIN_PX: f32 = 14.0;
pub const CARD_RADIUS_PX: f32 = 12.0;
pub const PRIMARY_BUTTON_HEIGHT_PX: f32 = 46.0;

#[derive(Debug, Clone, Copy, PartialEq)]
pub struct OverlayThemeTokens {
    pub app_background: egui::Color32,
    pub card_surface: egui::Color32,
    pub raised_card_surface: egui::Color32,
    pub primary_text: egui::Color32,
    pub muted_text: egui::Color32,
    pub accent: egui::Color32,
    pub primary_action: egui::Color32,
    pub warning: egui::Color32,
    pub success: egui::Color32,
    pub danger: egui::Color32,
    pub danger_action: egui::Color32,
    pub disabled_fill: egui::Color32,
    pub disabled_text: egui::Color32,
    pub card_border: egui::Color32,
}

pub fn overlay_theme_tokens() -> OverlayThemeTokens {
    OverlayThemeTokens {
        app_background: egui::Color32::from_rgb(6, 11, 20),
        card_surface: egui::Color32::from_rgb(12, 22, 38),
        raised_card_surface: egui::Color32::from_rgb(14, 27, 46),
        primary_text: egui::Color32::from_rgb(232, 238, 248),
        muted_text: egui::Color32::from_rgb(168, 182, 204),
        accent: egui::Color32::from_rgb(47, 107, 255),
        primary_action: egui::Color32::from_rgb(47, 107, 255),
        warning: egui::Color32::from_rgb(245, 158, 11),
        success: egui::Color32::from_rgb(76, 214, 122),
        danger: egui::Color32::from_rgb(226, 59, 59),
        danger_action: egui::Color32::from_rgb(185, 28, 28),
        disabled_fill: egui::Color32::from_rgb(22, 33, 51),
        disabled_text: egui::Color32::from_rgb(105, 121, 145),
        card_border: egui::Color32::from_rgb(35, 52, 77),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn overlay_theme_tokens_cover_reference_dashboard_palette() {
        let theme = overlay_theme_tokens();

        assert_eq!(theme.app_background, egui::Color32::from_rgb(6, 11, 20));
        assert_eq!(theme.card_surface, egui::Color32::from_rgb(12, 22, 38));
        assert_eq!(
            theme.raised_card_surface,
            egui::Color32::from_rgb(14, 27, 46)
        );
        assert_eq!(theme.card_border, egui::Color32::from_rgb(35, 52, 77));
        assert_eq!(theme.primary_text, egui::Color32::from_rgb(232, 238, 248));
        assert_eq!(theme.muted_text, egui::Color32::from_rgb(168, 182, 204));
        assert_eq!(theme.accent, egui::Color32::from_rgb(47, 107, 255));
        assert_eq!(theme.success, egui::Color32::from_rgb(76, 214, 122));
        assert_eq!(theme.warning, egui::Color32::from_rgb(245, 158, 11));
        assert_eq!(theme.danger, egui::Color32::from_rgb(226, 59, 59));
        assert_eq!(theme.disabled_fill, egui::Color32::from_rgb(22, 33, 51));
        assert_eq!(theme.disabled_text, egui::Color32::from_rgb(105, 121, 145));
    }
}
