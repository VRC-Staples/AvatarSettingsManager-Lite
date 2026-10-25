# Changelog

<!-- markdownlint-disable MD024 -->

All notable changes to ASM-Lite are documented here.

---

## [Unreleased]

No changes recorded yet after the `1.2.0-beta` prerelease.

## [1.2.0-beta] - 2026-10-25

This maintenance beta contains the changes since `1.1.0`. It does not replace the stable VCC package.
The release tag is `1.2.0-beta`; the tested Unity package's filename and internal version remain `1.2.0-beta.1`.
Back up the complete project and test the beta in a separate copy.

### Added

- Parameter-capacity feedback in the editor and a final count check after avatar build processing.
  Local backup parameters use no network sync bits, but still count toward the SDK's separate parameter-count limit.
  An over-limit or unverifiable final count blocks a guarded ASM-Lite build instead of reporting success.
- Regression coverage for generated-file ownership, rollback, partially initialized VRCFury connections, and parameter-budget checks.
  Rollback means restoring the assets and references that existed before a failed operation.

### Changed

- Normal attached builds now generate each avatar's output under `Assets/ASM-Lite/<owner>/WorkingAssets`.
  The package's `GeneratedAssets` folder supplies templates, rather than acting as a shared output folder for every avatar.
- Generated folders use avatar identity rather than only the display name.
  Renaming an avatar can retain its owned output, while two avatars with the same name must not share generated files.
- Controller generation uses configured defaults directly instead of separate `ASMLite_Def_*` parameters.
  Compatible Clear Preset reset operations share a driver in processed build output to reduce duplicate reset instructions.
- Optional smoke-test presentation moved out of the normal product window into test-owned code.
  This keeps test controls separate from the editor used to configure an avatar.

### Fixed

- Rebuild, Vendorize, and Detach retain previous generated assets until the complete operation commits.
  A failure before commit can restore those assets and their references. A cleanup failure after commit is reported as cleanup failure, not a successful rollback.
- Rebuild can repair supported empty VRCFury FullController lists without losing the original state if repair fails.
  FullController is VRCFury's feature for connecting ASM-Lite's generated controller, menus, and parameters to the avatar.
  Unknown layouts still stop the operation rather than being accepted as a complete installation.
- Generated-file replacement and cleanup now require matching avatar ownership and expected file contents.
  A familiar folder name alone is not permission to overwrite another avatar's assets or unrelated user files.
- Test-fixture recovery preserves its original baseline and reports failed restoration rather than silently accepting changed files as a new baseline.
  A fixture is the temporary avatar and project state a test creates before checking behavior.

### Tests and CI

- Restored six portable EditMode tests that Unity's category filtering had omitted.
  Windows-specific file-behavior tests and installed-NDMF-specific tests remain separate from the default portable selection.
- PlayMode setup now reads the generated assets connected to the test avatar through VRCFury, not unchanged package templates.
  The `ASMLite_Ctrl`, generated-output ownership, and Save/Load assertions remain in place.
- Removed a test expectation that required an SDK exception even when the exception never occurred.
  Unexpected exceptions still fail the tests; they are not broadly suppressed.
- CI hydrates checksum-verified stock VRChat SDK `3.10.2` packages.
  Both Unity test launches use `-overrideTextureCompression forceFastCompressor` to avoid the Linux Crunch texture-import crash.
  This lowers compression quality for test imports without changing SDK source files or their texture metadata.
- The inventory contains 589 test methods; parameterized methods can produce more than one executed case.
  [CI run 40171851917](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/actions/runs/40171851917)
  passed all eight jobs, including the exact expected 618 EditMode and 7 PlayMode cases.
  This is automated Editor/emulator coverage, not complete real-avatar upgrade or VRChat-client certification.

### Known Limitations

- The armature-toggle discovery issue recorded under `1.1.0` has no confirmed resolution recorded here.
  Do not treat this beta's automated test results as proof that the issue is fixed.

## [1.1.0] - 2026-05-24

These entries shipped in `1.1.0` but previously remained under the `[Unreleased]` heading.

### Added

- Runtime workflow modes in ASM-Lite window:
  - **Detach ASM-Lite** to bake direct runtime delivery and remove the editable ASM-Lite object.
  - **Vendorize (Keep Attached)** to mirror generated payload assets into `Assets/ASM-Lite/<Avatar>/GeneratedAssets` while keeping ASM-Lite editable.
  - **Return to Package Managed** recovery path for vendorized/detached states.
- Vendorized state persistence fields on `ASMLiteComponent` (`useVendorizedGeneratedAssets`, `vendorizedGeneratedAssetsPath`).
- Editor customization draft flow for deterministic pre-attach and attached updates to slot count, root/preset/action labels, slot icons, action icons, install path, and backup-parameter presets.
- Stable icon fixture registry plus the bundled `FlowArrow` icon fixture used by customization automation and smoke coverage.
- Parameter backup preset resolver for selecting visible generated/VRCFury-backed parameter groups by stable preset id or exact visible name.
- Generated-asset ownership, lifecycle, and direct-delivery policy modules that classify package-managed, vendorized, and detached avatar states without mixing ownership boundaries.
- Expanded EditMode coverage for install-path wiring, root menu overrides, MoveMenu adoption/remap behavior, customization snapshots, lifecycle rollback, package-output isolation, and upload preprocess cleanup boundaries.
- Expanded smoke/PlayMode coverage for setup matrices, avatar discovery, scaffold state, negative diagnostics, destructive recovery reset, naming/icon/backup combinations, AV3 Save/Load runtime checks, package-resource loading, package-managed generated-reference assertions, suite step timing, fail-fast behavior, console-error failures, and heartbeat/stall/crash host signals.
- Canonical test-suite ledger and CI-derived batch plan covering 530 inventoried package tests across core, integration, smoke protocol, overlay host, visible/manual, and PlayMode review lanes.
- Generated compatibility summary documenting Unity, VRChat SDK, and VRCFury policy floors with machine-checkable reason codes.
- CI Asset Hygiene verifier and workflow lane to reject tracked local Test Project payloads, runtime-generated CI Unity `Assets` payloads except `.gitkeep`, generated `bin`/`obj` outputs, generated `.csproj` files, and Unity `Library`/`Temp`/`Logs`/`UserSettings` state.
- PlayMode Save/Load CI lane that runs `ASMLite.Tests.PlayMode.ASMLiteAv3SaveLoadRuntimeTests` in the CI Unity project with VRCFury and Lyuma Av3 Emulator available.
- Unity license serial derivation helper for raw or base64 `.ulf` secrets without printing license contents.

### Changed

- Build pipeline now syncs install-path routing in `Build()` and uses a safer prefab-instance path
  that routes via MoveMenu helper behavior instead of relying on brittle FullController managed-reference overrides.
- Package-managed generated outputs are now snapshotted/restored around rebuild, vendorize, detach, tests, and fixture setup so package assets do not stay dirty after avatar-local delivery flows.
- FullController menu-prefix normalization now writes through both serialized properties and the live VRCFury payload, clearing stale prefab overrides before package-managed asset retargeting.
- Generated-asset fixture setup now recreates folders/metas from disk when Unity's AssetDatabase creates suffixed paths or stale import state.
- Parameter exclusions now expand across VRCFury toggle mapping pairs (original global name + assigned deterministic name) so either-side exclusion behaves consistently.
- Visible automation and smoke overlay UX now has clearer suite filters, current-suite/recent-event states, review/monitor copy, live-run spacing, and automatic event monitor opening.
- Test tree, CI scripts, validators, release gates, and audit docs were reorganized into taxonomy folders with canonical suite-map validation and release-critical result checks.
- Local CI-equivalent EditMode runner (`Tools/ci/bin/run-editmode-local.sh`) now supports `.env` defaults for Unity credential/license variables, adds local-only Docker Desktop credential helper shim behavior, and enforces bounded timeout handling to fail fast on hangs.
- CI Unity result verification now requires nonzero passing NUnit XML and can fail skipped or inconclusive tests for strict EditMode and PlayMode gates.
- EditMode and PlayMode CI jobs now verify package-managed generated assets and the shipped prefab stay clean after Unity runs.
- CI Unity project manifest now declares ASM-Lite as testable and includes VRCFury plus Lyuma Av3 Emulator dependencies for PlayMode runtime coverage.
- Updated README editor workflow docs to reflect detach/vendorize/package-managed action set, deterministic generated-asset ownership, and current release automation behavior.
- Pruned stale `.gsd` documentation entries and corrected requirements traceability metadata drift.

### Fixed

- Restored live ASM-Lite Save/Load behavior and added AV3 replay/fuzz/runtime coverage for backup/default parameter continuity.
- Migration rebuild now normalizes persisted custom root names before reapplying state so stale-prefab rebuild preserves trimmed customization values consistently.
- Slot icon resolution now treats `IconMode.Custom` as explicit custom-slot-only mode and fails closed to the bundled presets icon when a slot override is null or out-of-range.
- Build and rebuild now auto-heal missing or corrupted FullController parameter enrollment before regenerating assets.
- Vendorize/detach recovery classification now accepts already-retargeted vendorized generated references when deciding whether detached recovery can continue.
- Stale ASM-Lite editor caches are cleared across same-window lifecycle cycles so vendorize/detach/recovery actions use current avatar state.
- `ASMLiteTestFixtures.SetExpressionParams` now guards null `ParamsAsset` by recovering descriptor-bound parameters or creating a fallback expression-parameters asset before dirty/save calls.
- Smoke setup fixture selection now validates the shipped package prefab directly instead of creating a temporary prefab asset, reducing fixture asset churn.
- Generated asset validators now include `.meta` files and package-output snapshots to catch stale GUID/meta drift.
- Fixture isolation now restores generated assets, prefab bytes, scene selection, open scenes, and fixture roots across concurrent avatar tests.

### Removed

- Tracked CI Unity `bin`/`obj` generated outputs from version control so compile artifacts are recreated instead of carried in the repository.

### Known Issues

- VRCFury toggles that interact with objects inside the armature hierarchy are currently not being picked up by ASM-Lite parameter discovery. Investigation is in progress.

## [1.0.9] - 2026-04-10

### Added

- Deterministic VRCFury Toggle global-name enrollment via broker flow (`ASM_VF_*`) with fail-closed, serialized-field-scoped mutation and build-time restore lifecycle.
- Passive broker diagnostics surfaced in editor status/reporting to expose enrollment, collision planning, restore outcomes, and continuity mapping counters.
- Added a fail-closed CI verification script and workflow wiring to ensure critical regression test cases are present and passing in generated NUnit XML results.

### Changed

- Parameter discovery now includes broker-assigned global toggles even when they are not present
  in descriptor `expressionParameters`, preserving preset backup/load coverage for eligible `useGlobalParam` toggles.
- Collision handling moved to deterministic planning with descriptor preflight reservation and order-invariant assignment behavior.
- Legacy backup continuity now uses broker original→assigned identity mapping for safe alias mirroring, with explicit `mapped`/`mirrored`/`unmatched`/`malformed` accounting.
- CI verification flow now runs explicit critical regression checks in addition to existing release-gate checks.
- Added ignore rules for generated analysis output directories so generated artifacts are not accidentally tracked.

### Fixed

- Preset Save/Load continuity for legacy `VF{id}_` schemas after deterministic enrollment, including safe handling of malformed/unmatched legacy aliases without incorrect load-path wiring.
- VRCFury dual-schema discovery compatibility (`content` and `features[]`) for Toggle candidate discovery under schema drift.

## [1.0.8] - 2026-04-09

### Fixed

- Restored deterministic VRCFury FullController parameter wiring by repopulating `content.prms` with the generated `ASMLite_Params` reference during prefab creation.
- Updated the shipped `ASM-Lite.prefab` FullController payload so `prms` includes the generated expression-parameters asset instead of an empty list.
- Added prefab contract assertions to prevent regressions where FullController menu/controller references exist but parameter wiring is missing.

## [1.0.7] - 2026-04-09

### Changed

- Reverted delivery architecture from direct avatar-descriptor injection back to generated-assets + VRCFury FullController wiring.
  Build now regenerates managed FX, expression parameter, and menu assets for deterministic VF pickup at upload time.
- Preserved the shared single-Int control model: `ASMLite_Ctrl` remains the only control trigger, encoding Save/Load/Clear as `(slot-1)*3+1/2/3`.
- Preserved local-only parameter behavior: `ASMLite_Ctrl`, backup (`ASMLite_Bak_*`), and default (`ASMLite_Def_*`) parameters remain non-synced and consume zero expression sync bits.

### Added

- EditMode test coverage for VRCFury FullController delivery:
  prefab contract tests verify generated asset presence and structure;
  prefab wiring tests verify VRCFury FullController field wiring;
  VRCFury pipeline tests assert Build() writes to generated assets without mutating the live avatar descriptor.

## [1.0.6] - 2026-04-06

### Added

- Unity EditMode test workflow (`unity-test.yml`) runs the full integration test suite via GameCI on push to `dev` and is required to pass before release.
- Auto-tag workflow (`auto-tag.yml`) creates a version tag on `main` when `package.json` changes, replacing the manual dispatch release trigger.
- Nightly prerelease workflow (`nightly.yml`) builds and publishes a prerelease from `dev` daily at 05:00 UTC and on every `dev` push.
- Gitleaks secret-scan workflow (`secret-scan.yml`) runs weekly to detect committed credentials.
- Fail-closed release gate enforces that compile, test, and listing checks all pass before the release artifact is published.
- Dependabot config for GitHub Actions dependency updates.
- `Tools/ci/verify-release-gate.ps1` and `Tools/ci/verify-shadow-project-hygiene.ps1` scripts for local gate validation.

### Fixed

- EditMode test compile stability improved in Unity batchmode by switching driver assertions in
  `ASMLiteFXControllerTests` to `VRC_AvatarParameterDriver`, removing a brittle direct dependency on
  `VRCAvatarParameterDriver` type resolution.
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

- Menus and Save/Load/Clear actions now work correctly. Previously, VRCFury's FullController merged
  stale stub assets before ASM-Lite could populate them, causing a one-upload lag where the first
  upload always had empty menus and non-functional buttons. Additionally, removing `globalParams`
  from the FullController in an earlier fix caused all FX parameters to receive VF-prefixed names,
  breaking the menu-to-FX parameter binding entirely.
- VRCFury Toggle parameters (e.g. `Clothing_Rezz`) are now reliably backed up and restored.
  The previous clone-based discovery could produce parameter names that diverged from the actual
  runtime names, causing Copy drivers to silently miss those parameters.

### Changed

- (Historical for 1.0.5; superseded by the 1.0.7 revert.) ASM-Lite temporarily removed VRCFury
  FullController delivery and directly injected FX layers, expression parameters, and menu entries into
  the avatar descriptor at preprocess time (`callbackOrder=-2048`) after VRCFury had merged Toggle parameters.
  This was later reverted back to generated-assets + FullController delivery.
- Prefab simplified: only contains `ASMLiteComponent`. The VRCFury component and all reflection-based VRCFury type wiring have been removed from `ASMLitePrefabCreator` (~350 lines removed).
- Parameter discovery no longer uses a pre-VRCFury clone build. ASM-Lite reads `avDesc.expressionParameters` directly, which already contains all VRCFury-injected parameters by the time `Build()` runs.
- Control trigger parameter (`ASMLite_Ctrl`) is local-only and never synced. ASM-Lite takes zero synced bits from the expression parameter budget regardless of slot count.
- Removed the Safe Bool control scheme. All builds now use a single shared local Int trigger. The Control Scheme setting has been removed from the editor window.
- Icon Settings section is always visible, no longer behind a foldout. Slot Icons and Action Icons are displayed in grouped boxes with a live preview below.
- Removing the ASM-Lite prefab now cleans up injected FX layers, expression parameters, and menu entries from the avatar.

### Added

- Auto-migration on Rebuild: stale VRCFury FullController components from pre-1.0.5 prefab instances
  are automatically detected and removed, preventing double-merged content and VF-prefixed parameter name conflicts.
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
