# ASM-Lite - Avatar Settings Manager Lite

[![CI](https://img.shields.io/github/actions/workflow/status/VRC-Staples/AvatarSettingsManager-Lite/ci.yml?branch=main&label=ci)](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/VRC-Staples/AvatarSettingsManager-Lite?label=release)](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/releases/latest)
[![VPM Listing](https://img.shields.io/github/actions/workflow/status/VRC-Staples/AvatarSettingsManager-Lite/release.yml?branch=main&label=vpm%20listing)](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/actions/workflows/release.yml)
[![License](https://img.shields.io/github/license/VRC-Staples/AvatarSettingsManager-Lite)](LICENSE)
[![Discord](https://img.shields.io/badge/Discord-join-5865F2?logo=discord&logoColor=white)](https://discord.gg/WyDmWdThXM)

A lightweight prefab that adds Save, Load, and Clear Preset for expression parameter presets on VRChat avatars.

See [CHANGELOG.md](CHANGELOG.md) for version history and change details.

Policy regarding the use of Agentic AI Tooling view [AIPOLICY.md](AIPOLICY.md) for more information.

---

## Overview

ASM-Lite lets you save your current expression parameter values into preset slots, reload them at any time, or clear a slot back to defaults, all from the in-game expression menu.

An expression parameter is a named avatar setting, such as a clothing toggle or a color value.
Each preset slot stores the settings you choose to include in backups.

Configure ASM-Lite in **Tools → .Staples. → ASM-Lite**. Select an avatar, choose your settings, and click **Add ASM-Lite Prefab**.
ASM-Lite generates an animation controller, expression parameters, and menus for that avatar.
VRCFury's **FullController** feature connects these assets to the avatar during its build.

---

## Prerequisites

- **Unity 2022.3** - use a VRChat avatar project managed through VCC
- **VRChat Creator Companion (VCC)** - for managing your avatar project
- **VRChat SDK** (Avatar SDK 3) - installed via VCC
- **VRCFury** - installed via VCC

---

## Installation

### Maintenance Beta: 1.2.0-beta

The [1.2.0-beta prerelease](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/releases/tag/1.2.0-beta)
contains the maintenance changes described in the [changelog](CHANGELOG.md#120-beta---2026-10-25).
It does not replace stable `1.1.0` or update the VCC listing.

1. Back up the complete Unity project. Open a separate copy for beta testing.
2. Install the VRChat Avatar SDK and VRCFury through VCC in that copy.
3. Download `com.staples.asm-lite-1.2.0-beta.1.unitypackage` from the beta release.
   GitHub's **Source code** downloads are not the Unity installer.
4. In Unity, select **Assets → Import Package → Custom Package** and import the downloaded file.
5. Wait for compilation. Check that `Packages/com.staples.asm-lite/package.json` reports `1.2.0-beta.1`.

The release tag is `1.2.0-beta`, but the tested file and its internal version still use `1.2.0-beta.1`.
The repository's source manifest remains `1.1.0`. The packaging step set the beta version in the exported package.
Do not add another ASM-Lite copy under `Assets`.

To undo an upgrade, restore the complete project backup. Installing an older package alone does not restore scenes or generated avatar assets.

### Stable Release via VCC (Recommended)

1. Open the **VRChat Creator Companion**.
2. Go to **Settings → Packages → Add Repository**.
3. Paste the listing URL:
   ```
   https://vrc-staples.github.io/AvatarSettingsManager-Lite/index.json
   ```
4. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
5. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, configure settings, and click **Add ASM-Lite Prefab**.

### Development Checkout

1. Clone or download this repository.
2. In VCC, go to **Settings → User Packages → Add** and select the `Packages/com.staples.asm-lite` folder.
3. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
4. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, configure settings, and click **Add ASM-Lite Prefab**.

### Developer Git Identity Guard

CI blocks commits that use banned personal identity metadata or blocked co-author trailers on push and pull request.

---

## Usage

### In the Editor

1. Open **Tools → .Staples. → ASM-Lite**.
2. Select your avatar from the **Avatar Root** field.
3. Configure your settings (all options are available before adding the prefab):
   - **Slot Count** - number of preset slots (1-8).
   - **Menu Labels** - optional custom root, preset, action, and confirmation labels.
   - **Icon Mode** - what icons appear in the expression menu root and preset slots (see below).
   - **Action Icon Mode** - default bundled action icons or custom Save/Load/Clear icons.
   - **Install Path** - optional expression-menu path where ASM-Lite should appear.
   - **Parameter Backups** - optional presets for excluding parameters from Save/Load storage.
4. Click **Add ASM-Lite Prefab**.

Once the prefab is added, management actions appear:
- **Rebuild ASM-Lite** - regenerates payload assets and refreshes live wiring after changing settings.
- **Detach ASM-Lite** - bakes ASM-Lite runtime data into avatar assets, then removes the editable ASM-Lite GameObject from the avatar.
- **Vendorize (Keep Attached)** - copies the generated assets into `Assets/ASM-Lite/<owner>/GeneratedAssets` and connects the avatar to that copy. ASM-Lite stays editable.
- **Return to Package Managed** - when vendorized/detached state is detected, restores the normal package-managed editable workflow.
- **Remove Prefab** - removes the ASM-Lite prefab from the avatar hierarchy and cleans up ASM-Lite managed state, including legacy direct-injection remnants on older avatars.

### In-Game

1. Open the **Expression Menu**.
2. Navigate to **Settings Manager**.
3. Pick a slot and choose an action:
   - **Save** - copies the selected avatar settings into the slot. Requires confirmation to prevent accidental overwrites.
   - **Load** - restores the selected avatar settings from the slot.
   - **Clear Preset** - resets the selected slot and its included live avatar settings to their configured defaults. Requires confirmation. Other saved slots remain unchanged.

---

## Settings

### Slot Count

The number of independent preset slots ASM-Lite manages, from 1 to 8.
Each slot stores the expression parameters selected for backup. Changes take effect after a rebuild.

### Menu Labels

ASM-Lite can rename the generated expression-menu entries before attach or while attached. You can override the root menu name, each preset slot label, Save/Load/Clear Preset labels, and confirmation labels. Blank fields fall back to the default text.

### Install Path

By default ASM-Lite appears at the expression-menu root as **Settings Manager**. You can place it under an existing menu path, including paths discovered from VRCFury MoveMenu destinations. Choosing root clears the custom path and keeps the normal root placement.

### Parameter Backups

ASM-Lite normally stores every discovered non-ASM-Lite expression parameter.
Parameter backup presets let you exclude settings from storage, including VRCFury-created toggle names shown in the editor window.
Excluded settings still work on the avatar, but ASM-Lite does not save or restore them.

### Parameter Capacity

ASM-Lite uses local parameters, so it adds no network sync bits. Local parameters still count toward the SDK's separate parameter-count limit.
More preset slots need more backup parameters.
The editor shows a count or an estimate. Build-time tools can change that count, so a final check also measures the processed avatar.
For guarded ASM-Lite builds, an over-limit or unverifiable final count stops the build rather than reporting a false success.

If ASM-Lite's backup storage exceeds the limit, enable **Customize parameter backup** and exclude settings you do not need to save.
If the original avatar already exceeds the limit, reduce its parameters outside ASM-Lite too.

### Icon Mode

Controls the icons displayed for the ASM-Lite root menu and each preset slot.

| Mode | Behavior |
|---|---|
| **Multi Color** | Each slot automatically gets a distinct gear color cycling through Blue, Red, Green, Purple, Cyan, Orange, Pink, Yellow. |
| **Same Color** | All slots use one gear icon. Choose from Blue, Red, Green, Purple, Cyan, Orange, Pink, or Yellow. |
| **Custom** | Assign your own Texture2D per slot. |

### Action Icon Mode

Controls the icons shown for **Save**, **Load**, and **Clear Preset** actions inside each slot menu.

| Mode | Behavior |
|---|---|
| **Default** | Uses bundled action icons for Save, Load, and Clear Preset. |
| **Custom** | Lets you assign custom Texture2D icons per action. If a custom icon is left unassigned, ASM-Lite falls back to the bundled default for that action. |

---

## How It Works

During a rebuild or avatar build, ASM-Lite follows this flow:

1. **Find the settings to save.** Read the avatar's expression parameters and supported VRCFury parameters.
   Skip ASM-Lite's own parameters and apply the user's exclusions.
2. **Generate an avatar-owned copy.** Build the animation controller, expression parameters, and menus in a temporary folder.
   Check the generated assets before selecting them for use.
3. **Connect the generated assets.** Update the avatar's asset references and its live VRCFury FullController references.
   Keep the previous assets available until the operation completes, so a failed change can restore the previous state.
4. **Merge with the avatar build.** VRCFury merges the generated controller, parameters, and menus into its build output.
   ASM-Lite then reconciles its controller content with that processed output.
5. **Check the final parameter count.** Measure the processed expression parameters after build-time tools have run.
   Do not treat an editor estimate as proof that an avatar can upload.

### Where Generated Assets Live

- `Packages/com.staples.asm-lite/GeneratedAssets` contains the shipped templates, not the default location for an avatar's rebuilt output.
- The normal attached workflow uses `Assets/ASM-Lite/<owner>/WorkingAssets`.
- Vendorize and Detach use an avatar-owned `Assets/ASM-Lite/<owner>/GeneratedAssets` folder.

`<owner>` identifies the avatar object, not just its display name. Two avatars with the same name must not share generated assets.
ASM-Lite checks ownership before replacing or removing files. Do not rename these folders to match an avatar's display name.

The backup parameters (`ASMLite_Bak_*`) are local-only and do not consume the expression parameter sync budget.
New output uses configured default values directly instead of generating separate `ASMLite_Def_*` storage parameters.

The control trigger parameter (`ASMLite_Ctrl`) is also local-only and never networked. ASM-Lite takes **zero synced bits** from your parameter budget regardless of slot count.

> Special thanks to **[Nanochip](https://jinxxy.com/Nanochip)** for pointing out that synced parameters were never necessary here in the first place.

---

## Upgrading from Earlier Versions

Back up the complete project and try the upgrade in a separate copy first.
Click **Rebuild ASM-Lite** to refresh the generated assets and VRCFury connections.
Rebuild repairs supported missing or empty FullController entries. Removing and re-adding the prefab is not the default repair step.

If ASM-Lite reports unknown ownership or an unsupported VRCFury layout, stop and retain the existing assets.
Record the Console diagnostic before changing anything. The ownership checks intentionally refuse to overwrite files they cannot prove belong to that avatar.

---

## Building from Source

The distributable package lives in `Packages/com.staples.asm-lite/`.
Use VCC to resolve the VRChat SDK and VRCFury dependencies in your development project.

### Code Map for New Contributors

Start with [ASMLiteComponent.cs](Packages/com.staples.asm-lite/ASMLiteComponent.cs) for the settings and build entry point.
Then follow [ASMLiteBuilder.cs](Packages/com.staples.asm-lite/Editor/ASMLiteBuilder.cs) to see how it generates the controller, parameters, and menus.

| Area | Entry point | Responsibility |
|---|---|---|
| Editor operations | [ASMLiteLifecycleTransactionService.cs](Packages/com.staples.asm-lite/Editor/ASMLiteLifecycleTransactionService.cs) | Coordinates Rebuild, Vendorize, and Detach. Keeps backups until the complete operation can commit. |
| Generated files | [ASMLiteGeneratedAssetMirrorService.cs](Packages/com.staples.asm-lite/Editor/ASMLiteGeneratedAssetMirrorService.cs) | Selects avatar-owned folders, stages copies, and restores previous files after a failure. |
| VRCFury integration | [ASMLiteFullControllerWiring.cs](Packages/com.staples.asm-lite/Editor/ASMLiteFullControllerWiring.cs) | Reads and updates the live controller, menu, and parameter references on the prefab instance. |
| Parameter limits | [ASMLiteBuilder.ParameterBudget.cs](Packages/com.staples.asm-lite/Editor/ASMLiteBuilder.ParameterBudget.cs) and [ASMLiteParameterBudgetBuildGuard.cs](Packages/com.staples.asm-lite/Editor/ASMLiteParameterBudgetBuildGuard.cs) | Estimate capacity in the editor and validate the final processed count. |
| Tests | [Tests](Packages/com.staples.asm-lite/Tests/) and [suites.json](Tools/ci/test-suites/suites.json) | Hold the regression tests and define which groups the runners select. |

Edit the canonical package above, not an installed copy in another avatar project.

### Automated Validation

CI (continuous integration) runs automated checks on GitHub for a particular source commit.
EditMode tests check editor behavior. PlayMode tests run Save/Load behavior in Unity with an avatar emulator.

Run contributor EditMode validation through the canonical command:

```bash
bash Tools/ci/bin/run-editmode-local.sh
```

With no arguments, the runner uses the Unity project at `Tools/ci/unity-project`.
It selects the default test groups from `Tools/ci/test-suites/suites.json`.
The runner defaults to Docker and needs a configured Unity license. It launches Unity and writes test artifacts.
It is not a documentation-only check.

Expected canonical artifacts from the default run:
- `artifacts/editmode-results.xml`
- `artifacts/editmode-core-results.xml`
- `artifacts/editmode-integration-results.xml`
- `artifacts/editmode.log`
- `artifacts/asmlite-generation-wiring-summary.json`

This command is the canonical local EditMode path. CI runs the PlayMode Save/Load tests separately.

Test-suite coverage inventory is tracked in:
- `Tools/ci/docs/asmlite-tests-audit.md` - human-readable suite ledger with isolation class, CI bucket, and source location.
- `Tools/ci/test-suites/test-suite-ledger.json` - machine-readable ledger consumed by CI/smoke automation.

The current inventory contains 589 Unity C# test methods, with 7 suite groups and 5 default CI groups.
A method can produce multiple test cases, so this inventory is not the number of tests executed in one run.

At the beta source commit, [CI run 40171851917](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/actions/runs/40171851917)
passed all eight jobs, including the exact expected 618 EditMode and 7 PlayMode cases.
These automated results do not certify every existing avatar upgrade or behavior inside the VRChat client.

CI adds these release-facing guardrails around the local EditMode path:
- **CI Asset Hygiene** rejects tracked local Test Project payloads, runtime-generated CI Unity `Assets` payloads except `.gitkeep`, generated `bin`/`obj` outputs, generated `.csproj` files, and Unity `Library`/`Temp`/`Logs`/`UserSettings` state.
- **Strict Unity result verification** requires nonzero passing NUnit XML and fails skipped or inconclusive tests in CI Unity runs.
- **Generated asset cleanliness checks** run after EditMode and PlayMode jobs so package-managed `GeneratedAssets` and the shipped prefab stay clean.
- **PlayMode Save/Load Tests** run `ASMLite.Tests.PlayMode.ASMLiteAv3SaveLoadRuntimeTests` in the CI Unity project with VRCFury and Lyuma Av3 Emulator available.

Both CI test launches use `-overrideTextureCompression forceFastCompressor` to disable Crunch during test-project texture imports.
This avoids the reported Linux Crunch import crash by using faster, lower-quality test compression. It does not edit the SDK's texture metadata.

Optional local-only helper:
- `Tools/ci/bin/run-visible-smoke-local.sh` (interactive/local smoke validation entrypoint)

---

## Releases

- **Stable releases** publish from `main` when the package version is newer than the latest stable release.
  The workflow checks compile, lint, and EditMode results for the exact commit, creates the version tag, and updates the VPM listing.
- **Maintenance betas** are separate prereleases for testing before a merge into `main`.
  The current `1.2.0-beta` release contains the tested Unity package and GitHub's source downloads. It does not change the stable listing.
- **Nightly builds** use `dev`, not `maintenance`. The workflow is scheduled for 05:00 UTC daily and also runs on `dev` pushes or manual dispatch.
  It checks compile, lint, and EditMode results for the selected source commit before publishing. Scheduled start times can be delayed by GitHub.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a full version history.

---

## Thanks

- **[Blue Angel](https://payhip.com/BlueAngel)** - for the challenge that started this: proving a Settings Manager could work without OSC and be fully Quest compatible.
- **[Nanochip](https://jinxxy.com/Nanochip)** - for the insight that no synced parameters were needed at all, leading to a zero-bit footprint.

---

## License

GPL-3.0 - see [LICENSE](LICENSE) for details.
