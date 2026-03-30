# Changelog

All notable changes to ASM-Lite are documented here.

---

## [1.0.1] - 2026-03-29

### Added
- **Icon Mode is now configurable before the first build.** Icon Mode, Gear Color, and Custom icon slots are all visible and editable in the ASM-Lite window as soon as an avatar is selected — no longer gated behind adding the prefab first. Settings are applied to the component at the moment the prefab is added.
- **Remove Prefab button.** When the prefab is present on an avatar, a "Remove Prefab" button appears alongside "Rebuild ASM-Lite". Clicking it prompts for confirmation, then removes the prefab from the avatar hierarchy with full undo support.

### Fixed
- Pending icon settings are now synced from an existing component when switching between avatars, keeping the UI consistent with what is already configured.

---

## [1.0.0] - Initial Release

### Added
- **Save / Load / Clear Preset** for up to 8 expression parameter slots, all from the in-game expression menu.
- **Non-destructive VRCFury integration.** ASM-Lite merges its generated FX layers and expression menu at build time without modifying any existing avatar assets.
- **Two control schemes:**
  - *Safe Bool* — 3 synced Bool parameters per slot. Simplest setup, costs 3× slot count bits.
  - *Compact Int* — 1 shared synced Int for all slots. Costs 8 bits regardless of slot count.
- **Three icon modes for expression menu slots:**
  - *Same Color* — all slots share one gear color (Blue, Red, Green, Purple, Cyan, Orange, Pink, Yellow).
  - *Multi Color* — each slot cycles through a distinct gear color automatically.
  - *Custom* — user-supplied Texture2D per slot.
- **Configurable slot count** (1–8) editable in the ASM-Lite editor window before and after adding the prefab.
- **Confirmation steps** for Save and Clear Preset to prevent accidental overwrites or clears.
- **Synced bit cost display** in the Status panel showing how many of the 256 available bits ASM-Lite consumes.
- **Backup parameters are local-only** — not synced, so they do not consume the expression parameter sync budget. VRCFury Unlimited Parameters handles sync compression at upload.
- **VCC / VPM distribution** via `https://vrc-staples.github.io/AvatarSettingsManager-Lite/index.json`.
- **GPL-3.0 license.**
