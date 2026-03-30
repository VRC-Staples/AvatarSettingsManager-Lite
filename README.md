# ASM-Lite — Avatar Settings Manager Lite

A lightweight VRCFury prefab that adds Save, Load, and Clear Preset for expression parameter presets on VRChat avatars.

---

## Overview

ASM-Lite lets you save your current expression parameter values into preset slots, reload them at any time, or clear a slot back to defaults — all from the in-game expression menu with no extra tools needed.

Drop the prefab onto your avatar, configure your slot count and icon style, and click **Add ASM-Lite Prefab**. At build time, ASM-Lite scans the avatar's expression parameters, generates the FX animator layers and menu entries, and wires everything together through VRCFury non-destructively.

---

## Prerequisites

- **VRChat Creator Companion (VCC)** — for managing your avatar project
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
5. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, configure settings, and click **Add ASM-Lite Prefab**.

### Local Install (Testing / Dev)

1. Clone or download this repository.
2. In VCC, go to **Settings → User Packages → Add** and select the `Packages/com.staples.asm-lite` folder.
3. Open your avatar project in VCC and add **Avatar Settings Manager - Lite** from the package list.
4. In Unity, open **Tools → .Staples. → ASM-Lite**, select your avatar, configure settings, and click **Add ASM-Lite Prefab**.

---

## Usage

### In the Editor

1. Open **Tools → .Staples. → ASM-Lite**.
2. Select your avatar from the **Avatar Root** field.
3. Configure your settings (all options are available before adding the prefab):
   - **Slot Count** — number of preset slots (1–8).
   - **Control Scheme** — how ASM-Lite encodes slot actions (see below).
   - **Icon Mode** — what icons appear in the expression menu for each slot (see below).
4. Click **Add ASM-Lite Prefab**.

Once the prefab is added, two buttons appear:
- **Rebuild ASM-Lite** — regenerates all assets after changing slot count, control scheme, or icon settings.
- **Remove Prefab** — removes the ASM-Lite prefab from the avatar hierarchy with undo support.

### In-Game

1. Open the **Expression Menu**.
2. Navigate to **ASM-Lite**.
3. Pick a slot and choose an action:
   - **Save** — snapshots all current expression parameter values into the slot. Requires confirmation to prevent accidental overwrites.
   - **Load** — restores expression parameter values from the slot.
   - **Clear Preset** — resets the slot's saved values back to defaults. Requires confirmation. Does not affect your live expression parameters.

---

## Settings

### Slot Count

The number of independent preset slots ASM-Lite manages, from 1 to 8. Each slot stores a full snapshot of every expression parameter on the avatar. Changes take effect after a rebuild.

### Control Scheme

Controls how ASM-Lite encodes the Save / Load / Clear actions as VRChat expression parameters.

| Scheme | Parameters | Synced Bits | Best For |
|---|---|---|---|
| **Safe Bool** | 3 Bool params per slot | 3 × slot count | Avatars with a small existing parameter budget |
| **Compact Int** | 1 shared Int for all slots | 8 bits flat | Avatars with many other synced parameters |

Both schemes are functionally identical in-game. Compact Int is more efficient at 5 or more slots.

### Icon Mode

Controls the icons displayed in the expression menu for each preset slot.

| Mode | Behavior |
|---|---|
| **Same Color** | All slots use one gear icon. Choose from Blue, Red, Green, Purple, Cyan, Orange, Pink, or Yellow. |
| **Multi Color** | Each slot automatically gets a distinct gear color cycling through the same 8 colors. |
| **Custom** | Assign your own Texture2D per slot. |

---

## How It Works

At build time, ASM-Lite:

1. **Discovers parameters** — reads all expression parameters from the avatar's VRC Avatar Descriptor, skipping any already prefixed with `ASMLite_`.
2. **Generates FX layers** — creates an animator layer per slot with Idle, SaveSlot, LoadSlot, and ResetSlot states backed by `VRCAvatarParameterDriver` Copy and Set operations.
3. **Builds the expression menu** — generates the nested `ASM-Lite → Preset N → Save / Load / Clear Preset` menu structure with confirmation sub-menus for Save and Clear.
4. **Injects via VRCFury** — merges the generated layers and menus non-destructively at upload time, so nothing interferes with other VRCFury components on the avatar.

Backup parameters (`ASMLite_Bak_*`) and default parameters (`ASMLite_Def_*`) are local-only and not synced, so they do not consume the 256-bit expression parameter sync budget.

---

## Building from Source

The distributable package lives in `Packages/com.staples.asm-lite/`. Open the project through VCC to ensure the VRChat SDK and VRCFury dependencies resolve correctly.

---

## Releases

Releases are published automatically via GitHub Actions. The VPM listing rebuilds automatically on each new release.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for a full version history.

---

## License

GPL-3.0 — see [LICENSE](LICENSE) for details.
