# ASM-Lite — Avatar Settings Manager Lite

A lightweight VRCFury prefab for Save/Load/Reset expression parameter preset management on VRChat avatars.

---

## Overview

ASM-Lite lets avatar wearers save their current expression-parameter values into one of three preset slots, reload them at any time, or reset to defaults — all through in-game expression menus with no additional tools required.

It works entirely at build time: ASM-Lite discovers all expression parameters on the avatar descriptor, generates the necessary FX animator layers and expression menu entries, and wires everything together via VRCFury so it is compatible with any VRCFury-based modular avatar workflow.

---

## Prerequisites

- **VRChat Creator Companion (VCC)** — used to manage your avatar project
- **VRChat SDK** (Avatar SDK 3) — installed via VCC
- **VRCFury** — installed via VCC

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
5. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, and click **Add ASM-Lite Prefab**.

### Local Install (Testing / Dev)

1. Clone or download this repository.
2. In VCC, go to **Settings → User Packages → Add** and select the `Packages/com.staples.asm-lite` folder.
3. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
4. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, and click **Add ASM-Lite Prefab**.

---

## Usage

After building and uploading your avatar:

1. Open the **Expression Menu** in-game.
2. Navigate to **ASM-Lite**.
3. Choose a slot (**Slot 1**, **Slot 2**, or **Slot 3**), then select an action:
   - **Save** — writes all current expression-parameter values into the chosen slot. Requires confirmation to prevent accidental overwrites.
   - **Load** — restores expression-parameter values from the chosen slot.
   - **Reset** — resets all expression parameters to their default values.

---

## How It Works

At avatar build time, ASM-Lite:

1. **Discovers parameters** — reads all expression parameters from the avatar's VRC Avatar Descriptor.
2. **Generates FX layers** — creates animator layers with states and transitions that read/write synced float/int/bool parameters mapped to the three save slots.
3. **Builds the expression menu** — creates `ASM-Lite → Slot 1/2/3 → Save / Load / Reset` submenus and wires the control parameters.
4. **Injects via VRCFury** — the generated layers and menu entries are applied non-destructively so they don't interfere with other VRCFury components.

Backup and default parameters are local-only (not synced), so they don't consume your avatar's expression parameter budget.

---

## Building from Source

This repository is itself a Unity project used for development. The distributable package lives in `Packages/com.staples.asm-lite/`.

Open the project through VCC (add it as an existing project) to ensure the VRChat SDK and VRCFury dependencies resolve correctly.

---

## Releases

Releases are published automatically via GitHub Actions. Each release includes:
- A `.zip` of the package (for VPM)
- A `.unitypackage` (for manual import)
- The `package.json` manifest

The VPM listing at `https://vrc-staples.github.io/AvatarSettingsManager-Lite/index.json` is rebuilt automatically whenever a new release is published.

---

## License

MIT License — see [LICENSE](LICENSE) for details.
