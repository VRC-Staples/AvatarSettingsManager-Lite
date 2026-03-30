# Changelog

All notable changes to ASM-Lite are documented here.

---

## [1.0.1] - 2026-03-29

### Added
- **Two control schemes** for encoding slot actions as VRChat expression parameters:
  - *Safe Bool* — 3 synced Bool parameters per slot. Costs 3× slot count bits.
  - *Compact Int* — 1 shared synced Int for all slots. Costs 8 bits regardless of slot count.
- **Three icon modes** for expression menu slot icons:
  - *Same Color* — all slots share one gear color (Blue, Red, Green, Purple, Cyan, Orange, Pink, Yellow).
  - *Multi Color* — each slot cycles through a distinct gear color automatically.
  - *Custom* — user-supplied Texture2D per slot.
- **Icon Mode configurable before the first build.** All icon settings are visible and editable as soon as an avatar is selected — no longer gated behind adding the prefab first.
- **Remove Prefab button.** When the prefab is present, a "Remove Prefab" button appears alongside "Rebuild ASM-Lite". Prompts for confirmation and supports full undo.
- **Synced bit cost display** in the Status panel showing how many of the 256 available bits ASM-Lite consumes.
- **Slot count cap raised to 8** to match the VRChat expression menu toggle limit.
- **Confirmation step for Save** to prevent accidental overwrites.
- **Clear Preset** replaces Reset — clears only the saved slot values back to defaults without touching live avatar parameters.

### Fixed
- Pending icon settings now sync from an existing component when switching between avatars.
- Save, Load, and Reset icon aspect ratios corrected — uniform padding, true square canvas.
- Root menu folder icon updated to use BlueGear (Presets.png was missing).

---

## [1.0.0] - Initial Release

### Added
- **Save / Load / Reset** for expression parameter presets across up to 3 slots, all from the in-game expression menu.
- **Non-destructive VRCFury integration.** ASM-Lite merges its generated FX layers and expression menu at build time without modifying any existing avatar assets.
- **Configurable slot count** (1–3) editable in the ASM-Lite editor window.
- **Backup parameters are local-only** — not synced, so they do not consume the expression parameter sync budget. VRCFury Unlimited Parameters handles sync compression at upload.
- **VCC / VPM distribution** via `https://vrc-staples.github.io/AvatarSettingsManager-Lite/index.json`.
- **GPL-3.0 license.**
