# ASM-Lite - Avatar Settings Manager Lite

[![CI](https://img.shields.io/github/actions/workflow/status/VRC-Staples/AvatarSettingsManager-Lite/ci.yml?branch=main&label=ci)](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/VRC-Staples/AvatarSettingsManager-Lite?label=release)](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/releases/latest)
[![VPM Listing](https://img.shields.io/github/actions/workflow/status/VRC-Staples/AvatarSettingsManager-Lite/release.yml?branch=main&label=vpm%20listing)](https://github.com/VRC-Staples/AvatarSettingsManager-Lite/actions/workflows/release.yml)
[![License](https://img.shields.io/github/license/VRC-Staples/AvatarSettingsManager-Lite)](LICENSE)
[![Discord](https://img.shields.io/badge/Discord-join-5865F2?logo=discord&logoColor=white)](https://discord.gg/WyDmWdThXM)

A lightweight prefab that adds Save, Load, and Clear Preset for expression parameter presets on VRChat avatars.

---

## Overview

ASM-Lite lets you save your current expression parameter values into preset slots, reload them at any time, or clear a slot back to defaults, all from the in-game expression menu.

You configure ASM-Lite from the editor window (**Tools → .Staples. → ASM-Lite**): pick your avatar, choose a slot count and icon style, then click **Add ASM-Lite Prefab**. At build time, ASM-Lite scans the avatar's expression parameters, regenerates its managed FX/parameter/menu assets, and delivers them through the prefab's VRCFury FullController wiring.

---

## Prerequisites

- **VRChat Creator Companion (VCC)** - for managing your avatar project
- **VRChat SDK** (Avatar SDK 3) - installed via VCC
- **VRCFury** - installed via VCC

---

## Installation

### Via VCC (Recommended)

1. Open the **VRChat Creator Companion**.
2. Go to **Settings → Packages → Add Repository**.
3. Paste the listing URL:
   ```
   https://vrc-staples.github.io/AvatarSettingsManager-Lite/index.json
   ```
4. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
5. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, configure settings, and click **Add ASM-Lite Prefab**.

### Local Install (Testing / Dev)

1. Clone or download this repository.
2. In VCC, go to **Settings → User Packages → Add** and select the `Packages/com.staples.asm-lite` folder.
3. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
4. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, configure settings, and click **Add ASM-Lite Prefab**.

### Developer Git Identity Guard

Run this once after cloning to enable repository hooks:

```bash
bash Tools/ci/setup-git-hooks.sh
```

The hooks block commits that use banned personal identity metadata or blocked co-author trailers. CI also enforces the same checks on push and pull request.

---

## Usage

### In the Editor

1. Open **Tools → .Staples. → ASM-Lite**.
2. Select your avatar from the **Avatar Root** field.
3. Configure your settings (all options are available before adding the prefab):
   - **Slot Count** - number of preset slots (1-8).
   - **Icon Mode** - what icons appear in the expression menu for each slot (see below).
   - **Action Icon Mode** - default bundled action icons or custom Save/Load/Clear icons.
4. Click **Add ASM-Lite Prefab**.

Once the prefab is added, management actions appear:
- **Rebuild ASM-Lite** - regenerates payload assets and refreshes live wiring after changing settings.
- **Detach ASM-Lite** - bakes ASM-Lite runtime data into avatar assets, then removes the editable ASM-Lite GameObject from the avatar.
- **Vendorize (Keep Attached)** - mirrors generated payload assets into `Assets/ASM-Lite/<AvatarName>/GeneratedAssets` and retargets live references there while keeping ASM-Lite editable.
- **Return to Package Managed** - when vendorized/detached state is detected, restores the normal package-managed editable workflow.
- **Remove Prefab** - removes the ASM-Lite prefab from the avatar hierarchy and cleans up ASM-Lite managed state, including legacy direct-injection remnants on older avatars.

### In-Game

1. Open the **Expression Menu**.
2. Navigate to **Settings Manager**.
3. Pick a slot and choose an action:
   - **Save** - snapshots all current expression parameter values into the slot. Requires confirmation to prevent accidental overwrites.
   - **Load** - restores expression parameter values from the slot.
   - **Clear Preset** - resets the slot's saved values back to defaults. Requires confirmation. Does not affect your live expression parameters.

---

## Settings

### Slot Count

The number of independent preset slots ASM-Lite manages, from 1 to 8. Each slot stores a full snapshot of every expression parameter on the avatar. Changes take effect after a rebuild.

### Icon Mode

Controls the icons displayed in the expression menu for each preset slot.

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

At build time, ASM-Lite runs via `IPreprocessCallbackBehaviour` after VRCFury has merged avatar parameters into the descriptor snapshot used for discovery. It then:

1. **Discovers parameters** - reads expression parameters from `avDesc.expressionParameters` and skips any `ASMLite_`-prefixed entries to avoid self-referential loops.
2. **Generates managed assets** - rebuilds the managed FX controller, expression-parameter asset, and menu assets in `GeneratedAssets` using the discovered schema.
3. **Builds the expression menu** - generates the nested `Settings Manager → Preset N → Save / Load / Clear Preset` menu structure with confirmation sub-menus for Save and Clear.
4. **Delivers through VRCFury FullController** - the prefab's FullController wiring references those generated assets, so the current upload consumes the freshly rebuilt payload instead of stale content.

Backup parameters (`ASMLite_Bak_*`) and default parameters (`ASMLite_Def_*`) are local-only and not synced, so they do not consume the 256-bit expression parameter sync budget.

The control trigger parameter (`ASMLite_Ctrl`) is also local-only and never networked. ASM-Lite takes **zero synced bits** from your parameter budget regardless of slot count.

> Special thanks to **[Nanochip](https://jinxxy.com/Nanochip)** for pointing out that synced parameters were never necessary here in the first place.

---

## Upgrading from Earlier Versions

If your avatar already has an ASM-Lite prefab from earlier direct-injection builds, click **Rebuild ASM-Lite** in the editor window. Rebuild normalizes stale VRCFury components, refreshes generated assets, and keeps the prefab on the generated-assets + VRCFury FullController delivery path.

If you are upgrading from versions that may have an empty FullController parameter list, remove and re-add the ASM-Lite prefab once so the instance picks up the current prefab wiring where FullController `prms` references `ASMLite_Params`.

---

## Building from Source

The distributable package lives in `Packages/com.staples.asm-lite/`. Open the project through VCC to ensure the VRChat SDK and VRCFury dependencies resolve correctly.

### Full Validation (Local)

Run contributor EditMode validation through the canonical command:

```bash
Tools/ci/run-editmode-local.sh
```

No-arg execution uses the repo-owned Unity project at `Tools/ci/unity-project` and the shared batch plan at `Tools/ci/editmode-batch-runs.json`.

Expected canonical artifacts from the default run:
- `artifacts/editmode-results.xml`
- `artifacts/editmode-core-results.xml`
- `artifacts/editmode-integration-results.xml`
- `artifacts/editmode.log`
- `artifacts/asmlite-generation-wiring-summary.json`

For contributor validation, `Tools/ci/run-editmode-local.sh` remains the only required full test path before pushing changes.

Optional local-only helpers:
- `Tools/ci/run-visible-smoke-local.sh`
- `Tools/ci/run-UAT-smoke.sh`

---

## Releases

Release artifacts are published automatically from pushes to `main` when the package version in `Packages/com.staples.asm-lite/package.json` is newer than the latest GitHub release. The release workflow creates the semantic tag during publish, then deploys the VPM listing. A nightly prerelease build runs from `dev` at 05:00 UTC daily (and on `dev` pushes/manual dispatch).

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
