# ASM-Lite - Avatar Settings Manager Lite

A lightweight VRCFury prefab that adds Save, Load, and Reset for expression parameter presets on VRChat avatars.

---

## Overview

ASM-Lite lets you save your current expression parameter values into one of three preset slots, reload them at any time, or reset to defaults - all from the in-game expression menu with no extra tools needed.

Drop the prefab onto your avatar hierarchy and you're done. At build time, ASM-Lite scans the avatar's expression parameters, generates the FX animator layers and menu entries, and wires everything together through VRCFury non-destructively.

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
5. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, and click **Add ASM-Lite Prefab**.

### Local Install (Testing / Dev)

1. Clone or download this repository.
2. In VCC, go to **Settings → User Packages → Add** and select the `Packages/com.staples.asm-lite` folder.
3. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
4. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, and click **Add ASM-Lite Prefab**.

---

## Usage

After uploading your avatar:

1. Open the **Expression Menu** in-game.
2. Navigate to **ASM-Lite**.
3. Pick a slot (**Slot 1**, **Slot 2**, or **Slot 3**) and choose an action:
   - **Save** - snapshots all current expression parameter values into the slot. Requires a confirmation step to avoid accidental overwrites.
   - **Load** - restores expression parameter values from the slot.
   - **Reset** - returns all expression parameters to their default values.

---

## How It Works

At build time, ASM-Lite:

1. **Discovers parameters** - reads all expression parameters from the avatar's VRC Avatar Descriptor.
2. **Generates FX layers** - creates animator layers with states and transitions that read and write synced float, int, and bool parameters across the three slots.
3. **Builds the expression menu** - creates the `ASM-Lite → Slot 1/2/3 → Save / Load / Reset` menu structure.
4. **Injects via VRCFury** - merges the generated layers and menus non-destructively, so nothing interferes with other VRCFury components on the avatar.

Backup and default parameters are local-only (not synced), so they don't eat into the expression parameter budget.

---

## Building from Source

The distributable package lives in `Packages/com.staples.asm-lite/`. Open the project through VCC to ensure the VRChat SDK and VRCFury dependencies resolve correctly.

---

## Releases

Releases are published automatically via GitHub Actions and include:
- A `.zip` of the package (for VPM)
- A `.unitypackage` (for manual import)
- The `package.json` manifest

The VPM listing rebuilds automatically on each new release.

---

## License

MIT - see [LICENSE](LICENSE) for details.
