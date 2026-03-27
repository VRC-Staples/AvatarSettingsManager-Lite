# ASM-Lite — Avatar Settings Manager Lite

A lightweight, drop-in VRCFury prefab for Save/Load/Reset parameter preset management on VRChat avatars.

---

## Overview

ASM-Lite lets avatar wearers save their current expression-parameter values into one of three preset slots, reload them at any time, or reset to defaults — all through in-game expression menus with no additional tools required.

It works entirely at build time: ASM-Lite discovers all expression parameters on the avatar descriptor, generates the necessary FX animator layers and expression menu entries, and wires everything together via VRCFury so it is compatible with any VRCFury-based modular avatar workflow.

---

## Prerequisites

Install the following via the **VRChat Creator Companion (VCC)** before importing ASM-Lite:

- **VRChat SDK** (Avatar SDK 3) — required for avatar uploading and expression parameter support
- **VRCFury** — required for build-time component execution and FX layer injection

---

## Installation

1. Download the latest `ASM-Lite.unitypackage` from the Releases page.
2. In your Unity project, go to **Assets → Import Package → Custom Package…** and select the downloaded `.unitypackage`.
3. Import all assets.
4. In the **Project** window, navigate to `Assets/ASM-Lite/Prefabs/`.
5. Drag the **ASM-Lite** prefab onto your **avatar root** GameObject (the same object that has the VRC Avatar Descriptor).

That's it — VRCFury handles the rest at build time.

---

## Usage

After building and uploading your avatar:

1. Open the **Expression Menu** in-game.
2. Navigate to **ASM-Lite**.
3. Choose a slot (**Slot 1**, **Slot 2**, or **Slot 3**), then select an action:
   - **Save** — writes all current expression-parameter values into the chosen slot.
   - **Load** — restores expression-parameter values from the chosen slot.
   - **Reset** — resets all expression parameters to their default values.

---

## How It Works

At avatar build time, ASM-Lite:

1. **Discovers parameters** — reads all expression parameters from the avatar's VRC Avatar Descriptor.
2. **Generates FX layers** — creates animator layers with states and transitions that read/write synced float/int/bool parameters mapped to the three save slots.
3. **Builds the expression menu** — creates `ASM-Lite → Slot 1/2/3 → Save / Load / Reset` submenus and wires the control parameters.
4. **Injects via VRCFury** — the generated layers and menu entries are applied non-destructively so they don't interfere with other VRCFury components.

---

## Building from Source

### Prerequisites

The project resolves VRChat SDK and VRCFury packages from public scoped registries (`packages.vrchat.com` and `package.openupm.com`). These registries require auth tokens injected by the **VRChat Creator Companion (VCC)**:

1. Add this repository as a project in VCC (or open it via **File → Open Project**).
2. Let VCC fully resolve and load the project — this writes `Packages/packages-lock.json` with the VRChat SDK entries.
3. Close Unity (the editor window VCC opened).

> **The export script checks for this lockfile before running.** If it's missing or incomplete it will print a clear error and exit rather than failing inside Unity with a cryptic "Access denied" message.

> **Do not open the project directly in Unity Hub** before VCC has resolved the packages. Unity Hub cannot authenticate against `packages.vrchat.com` on its own.

### Export

With VCC prerequisites satisfied, run the PowerShell export script from the project root:

```powershell
.\export-package.ps1
```

This invokes Unity in headless batch mode and writes `Dist/ASM-Lite.unitypackage`. Unity is discovered automatically from `ProjectSettings/ProjectVersion.txt` and the Unity Hub install directory. If Unity is installed to a non-standard location, pass it explicitly:

```powershell
.\export-package.ps1 -UnityExe "D:\MyUnityInstalls\2022.3.22f1\Editor\Unity.exe"
```

Alternatively, with the project open in the Unity Editor, use **Tools → .Staples. → ASM-Lite Dev → Export Package**.

---

## License

*(Placeholder — license to be determined)*
