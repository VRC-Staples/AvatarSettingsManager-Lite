# Changelog

All notable changes to ASM-Lite are documented here.

---

## [Unreleased]

## [1.0.7] - 2026-04-09

### Changed
- Reverted delivery architecture from direct avatar-descriptor injection back to generated-assets + VRCFury FullController wiring. Build now regenerates managed FX, expression parameter, and menu assets for deterministic VF pickup at upload time.
- Preserved the shared single-Int control model: `ASMLite_Ctrl` remains the only control trigger, encoding Save/Load/Clear as `(slot-1)*3+1/2/3`.
- Preserved local-only parameter behavior: `ASMLite_Ctrl`, backup (`ASMLite_Bak_*`), and default (`ASMLite_Def_*`) parameters remain non-synced and consume zero expression sync bits.

### Added
- EditMode test coverage for VRCFury FullController delivery: prefab contract tests verify generated asset presence and structure; prefab wiring tests verify VRCFury FullController field wiring; VRCFury pipeline tests assert Build() writes to generated assets without mutating the live avatar descriptor.

## [1.0.6] - 2026-04-06

### Added
- Unity EditMode test workflow (`unity-test.yml`) runs the full integration test suite via GameCI on push to `dev` and is required to pass before release.
- Auto-tag workflow (`auto-tag.yml`) creates a version tag on `main` when `package.json` changes, replacing the manual dispatch release trigger.
- Nightly prerelease workflow (`nightly.yml`) builds and publishes a prerelease from `dev` daily at 05:00 UTC and on every `dev` push.
- Gitleaks secret-scan workflow (`secret-scan.yml`) runs weekly to detect committed credentials.
- Fail-closed release gate enforces that compile, test, and listing checks all pass before the release artifact is published.
- Dependabot config for GitHub Actions dependency updates.
- `ci/verify-release-gate.ps1` and `ci/verify-shadow-project-hygiene.ps1` scripts for local gate validation.

### Fixed
- EditMode test compile stability improved in Unity batchmode by switching driver assertions in `ASMLiteFXControllerTests` to `VRC_AvatarParameterDriver`, removing a brittle direct dependency on `VRCAvatarParameterDriver` type resolution.
- CI C# compile pipeline now resolves VRChat SDK dependencies consistently by ensuring required SDK plugin DLLs are tracked in the CI Unity project package tree.
- Generated CI project files now disable default SDK compile globs to avoid pulling unrelated package sources into the compile check.
- Silent Editor build failure fixed by propagating both Runtime and Editor csproj exit codes via bitwise OR.
- All `uses:` workflow action references SHA-pinned to prevent supply chain drift.

### Changed
- CI compile check targets `netstandard2.1` for generated projects and no longer depends on Mono setup in workflow.
- README compile badge now tracks `main` branch. Unity Tests badge added.
- `.mcp.json` removed from version control.

---

## [1.0.5] - 2026-04-05

### Fixed
- Menus and Save/Load/Clear actions now work correctly. Previously, VRCFury's FullController merged stale stub assets before ASM-Lite could populate them, causing a one-upload lag where the first upload always had empty menus and non-functional buttons. Additionally, removing `globalParams` from the FullController in an earlier fix caused all FX parameters to receive VF-prefixed names, breaking the menu-to-FX parameter binding entirely.
- VRCFury Toggle parameters (e.g. `Clothing_Rezz`) are now reliably backed up and restored. The previous clone-based discovery could produce parameter names that diverged from the actual runtime names, causing Copy drivers to silently miss those parameters.

### Changed
- (Historical for 1.0.5; superseded by the Unreleased revert.) ASM-Lite temporarily removed VRCFury FullController delivery and directly injected FX layers, expression parameters, and menu entries into the avatar descriptor at preprocess time (`callbackOrder=-2048`) after VRCFury had merged Toggle parameters. This was later reverted back to generated-assets + FullController delivery.
- Prefab simplified: only contains `ASMLiteComponent`. The VRCFury component and all reflection-based VRCFury type wiring have been removed from `ASMLitePrefabCreator` (~350 lines removed).
- Parameter discovery no longer uses a pre-VRCFury clone build. ASM-Lite reads `avDesc.expressionParameters` directly, which already contains all VRCFury-injected parameters by the time `Build()` runs.
- Control trigger parameter (`ASMLite_Ctrl`) is local-only and never synced. ASM-Lite takes zero synced bits from the expression parameter budget regardless of slot count.
- Removed the Safe Bool control scheme. All builds now use a single shared local Int trigger. The Control Scheme setting has been removed from the editor window.
- Icon Settings section is always visible, no longer behind a foldout. Slot Icons and Action Icons are displayed in grouped boxes with a live preview below.
- Removing the ASM-Lite prefab now cleans up injected FX layers, expression parameters, and menu entries from the avatar.

### Added
- Auto-migration on Rebuild: stale VRCFury FullController components from pre-1.0.5 prefab instances are automatically detected and removed, preventing double-merged content and VF-prefixed parameter name conflicts.
- Thanks section in README crediting Blue Angel and Nanochip.

---

## [1.0.4] - 2026-04-03

### Fixed
- Guard against duplicate discovered parameters in slot driver generation to prevent assignment errors on rebuild.
- Removed direct `VRCExpressionParameters.Parameter` type reference from the test assembly to resolve a compile error when `VRCSDK3A` is not directly referenced by the test project.

---

## [1.0.3] - 2026-03-30

### Added
- Live VRC-style radial menu preview in the Icon Settings section.
- EditMode test suite covering `ASMLiteBuilder`, `ASMLiteAssetPaths`, and `ASMLiteComponent`.
- Added `InternalsVisibleTo` declaration so the test assembly can access internal types like `ASMLiteAssetPaths` directly, without making them part of the public API.
- Slot backup parameters are preserved when increasing slot count and rebuilding, preventing loss of previously saved presets.
- ASM-Lite editor window now shows a banner at the top of the window.

### Changed
- Top-level expression menu entry renamed from "ASM-Lite" to "Settings Manager".
- Top-level menu icon changed from a gear to a sliders icon.
- Default icon mode changed to Multi Color. Multi Color is now the first option in the dropdown.
- Wheel preview dividers offset by half a step so lines fall between icons rather than on them.
- Icon Mode and Action Icon settings are configurable before adding the prefab.
- Editor window minimum size increased. Banner scales to fill available width.
- UX audit pass: section separators, Remove Prefab hit target raised to 32px, horizontal scrollbar suppressed.

### Fixed
- Gear and preset icons retoned to muted VRC style.
- Banner updated to correct VCC page design with Quest Compatible and No External Dependencies (OSC) text.

---

## [1.0.2] - 2026-03-29

### Added
- Custom action icons: user-supplied Texture2D for Save, Load, and Clear Preset buttons.

### Fixed
- Stale asset type conflicts resolved on rebuild when parameter schema changes between builds.

---

## [1.0.1] - 2026-03-28

### Added
- Two control schemes for encoding slot actions as VRChat expression parameters:
  - *Safe Bool* - 3 synced Bool parameters per slot. Costs 3x slot count bits.
  - *Compact Int* - 1 shared synced Int for all slots. Costs 8 bits regardless of slot count.
- Three icon modes for expression menu slot icons:
  - *Same Color* - all slots share one gear color (Blue, Red, Green, Purple, Cyan, Orange, Pink, Yellow).
  - *Multi Color* - each slot cycles through a distinct gear color automatically.
  - *Custom* - user-supplied Texture2D per slot.
- Icon Mode configurable before the first build. All icon settings are visible and editable as soon as an avatar is selected.
- Remove Prefab button. When the prefab is present, a "Remove Prefab" button appears alongside "Rebuild ASM-Lite". Prompts for confirmation and supports full undo.
- Slot count cap raised to 8 to match the VRChat expression menu toggle limit.
- Confirmation step for Save to prevent accidental overwrites.
- Clear Preset replaces Reset - clears only the saved slot values back to defaults without touching live avatar parameters.

### Fixed
- Pending icon settings now sync from an existing component when switching between avatars.
- Save, Load, and Reset icon aspect ratios corrected - uniform padding, true square canvas.
- Root menu folder icon updated to use BlueGear.

---

## [1.0.0] - Initial Release

### Added
- Save, Load, and Reset for expression parameter presets across up to 3 slots, all from the in-game expression menu.
- Non-destructive integration. ASM-Lite merges its generated FX layers and expression menu at build time without modifying any existing avatar assets.
- Configurable slot count (1-3) editable in the ASM-Lite editor window.
- Backup parameters are local-only and not synced, so they do not consume the expression parameter sync budget.
- VCC / VPM distribution via `https://vrc-staples.github.io/AvatarSettingsManager-Lite/index.json`.
- GPL-3.0 license.
