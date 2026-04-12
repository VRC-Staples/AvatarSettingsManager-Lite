# CLAUDE.md

Guidance for Claude Code (claude.ai/code) in this repo.

## Project Overview

**ASM-Lite (Avatar Settings Manager - Lite)**: VRChat avatar utility VPM package (`com.staples.asm-lite`). Users save/load/clear expression parameter presets in-game. Integrates with VRChat Avatar SDK via `IPreprocessCallbackBehaviour`. Generates deterministic assets consumed by VRCFury FullController wiring at build time. Zero synced parameter budget impact.

**Key deps:** VRChat Avatars SDK ≥ 3.7.0, VRCFury ≥ 1.999.0, Unity 2022.3+

## Build & CI

### Compile check (no Unity license)

```bash
UNITY_PATH=Tools/ci/unity-project/UnityManaged \
PACKAGE_PATH=Packages/com.staples.asm-lite \
CI_PROJECT_PATH=Tools/ci/unity-project \
bash Tools/ci/generate-csproj.sh

dotnet build Tools/ci/unity-project/ASMLite.Runtime.csproj
dotnet build Tools/ci/unity-project/ASMLite.Editor.csproj
```

### Run tests (Unity required)

```bash
Unity -projectPath Tools/ci/unity-project -runTests -testPlatform editmode -testResults artifacts/results.xml
```

### M010 contract anchor verification

```bash
python Tools/ci/verify-m010-contract-tests.py --results Tools/ci/unity-project/artifacts/M010-S04-editmode.xml --require TB13 --require TB14 --require TB15 --require TB16 --require TB17 --require TB18 --require TB19 --require A26 --require A27 --require VF06
```

### Local release gate verification

```powershell
pwsh Tools/ci/verify-release-gate.ps1
```

### Linting

Super-Linter validates Markdown, JSON, YAML, GitHub Actions, shell scripts.
Config: `.markdownlint.json`, `.yamllint.yml`, `.shellcheckrc`.
Max line length: 200 chars (Markdown/YAML).

## Architecture

### Assemblies

- **`ASMLite`** (runtime): `ASMLiteComponent.cs` only. No editor deps. Calls `ASMLiteBuilder.Build()` via reflection.
- **`ASMLite.Editor`** (editor-only): all editor scripts in `Packages/com.staples.asm-lite/Editor/`.
- **`ASMLite.Tests.Editor`** (test-only): NUnit suites in `Packages/com.staples.asm-lite/Tests/Editor/`.

### Build pipeline integration

1. User adds prefab via `Tools → .Staples. → ASM-Lite`.
2. On avatar upload, VRChat SDK calls `IPreprocessCallbackBehaviour` callbacks.
3. VRCFury runs first (`callbackOrder = int.MinValue`) and merges params into descriptor.
4. `ASMLiteComponent.OnPreprocess()` fires (`PreprocessOrder = -10`) and calls `ASMLiteBuilder.Build()` via reflection.
5. `ASMLiteBuilder.Build()` regenerates deterministic generated-assets payload (`ASMLite_FX.controller`, `ASMLite_Params.asset`, `ASMLite_Menu.asset`). Prefab VRCFury FullController wiring delivers assets at upload.

### Parameter model

All generated params are **local-only** (not synced). Sync budget cost: zero bits.

| Parameter | Type | Purpose |
|-----------|------|---------|
| `ASMLite_Ctrl` | Int | Shared trigger: Save = `(slot-1)*3+1`, Load = `(slot-1)*3+2`, Clear = `(slot-1)*3+3` |
| `ASMLite_Bak_*` | Varies | Per-slot backup copies of avatar params |
| `ASMLite_Def_*` | Varies | Default values for reset/clear |

### Key files

| File | Purpose |
|------|---------|
| `Packages/com.staples.asm-lite/ASMLiteComponent.cs` | Runtime MonoBehaviour; reflection bridge to editor builder |
| `Editor/ASMLiteBuilder.cs` | Core build logic: parameter discovery, FX layer generation, menu injection |
| `Editor/ASMLiteWindow.cs` | `Tools → .Staples. → ASM-Lite` editor window with radial preview |
| `Editor/ASMLiteAssetPaths.cs` | Centralized icon + asset path constants |
| `Editor/ASMLitePrefabCreator.cs` | Prefab instantiation + legacy migration detection |
| `Tools/ci/generate-csproj.sh` | Generates `.csproj` files for dotnet compile against CI shadow project DLLs |
| `Tools/ci/unity-project/` | Minimal Unity 2022.3.22f1 shadow project; `packages-lock.json` intentionally committed (CI no network) |

### Icon system

- 8 built-in gear colors: Blue, Red, Green, Purple, Cyan, Orange, Pink, Yellow
- `MultiColor`: slot cycles colors
- `SameColor`: all slots one color
- `Custom`: user `Texture2D` per slot
- Action icons (Save/Load/Clear/Reset): bundled defaults or override

### Generated assets

`Packages/com.staples.asm-lite/GeneratedAssets/` holds runtime payloads (`ASMLite_FX.controller`, `ASMLite_Params.asset`, `ASMLite_Menu.asset`) consumed by prefab VRCFury FullController wiring. Regenerated every build. Canonical delivery source of truth.

## CI Workflows

| Workflow | Trigger | What it does |
|----------|---------|-------------|
| `ci.yml` | push/PR to main/dev | Identity guard, Super-Linter, compile checks, GameCI EditMode run, M010 contract-anchor verification (`TB13`–`TB19`, `A26`, `A27`, `VF06`) |
| `release.yml` | push to main or manual dispatch | Version gate, required-check polling, build/publish artifacts, VPM listing deployment |
| `scheduled.yml` | daily 05:00 UTC, dev push, manual dispatch | Nightly prerelease package/unitypackage publish |

All workflows use SHA-pinned actions.

<!-- GSD:project-start source:PROJECT.md -->
## Project

**ASM-Lite (Avatar Settings Manager — Lite)**

Drop-in VRCFury prefab for VRChat avatars. Adds in-game preset manager. User drags prefab into avatar hierarchy. At build, ASM-Lite auto-discovers custom params (Bool, Int, Float) and generates Save/Load/Clear preset flow across configurable slots (1–8). Generated params are local-only, zero sync-bit cost.

**Core value:** In-game Save/Load/Clear of avatar parameter presets. Save current settings, swap configs, reset defaults without leaving VRChat.

### Constraints

- **Tech stack**: Unity 2022.3.22f1, VRChat Avatars SDK ≥ 3.7.0, VRCFury ≥ 1.999.0
- **Distribution**: VPM zip + .unitypackage (D001); VCC package deferred
- **CI**: Personal Unity license; periodic GameCI re-activation needed
- **Dependencies**: VRCFury types internal; integration via reflection (D003)
<!-- GSD:project-end -->

<!-- GSD:stack-start source:codebase/STACK.md -->
## Technology Stack

## Language & Runtime
- **Language:** C# (Unity subset)
- **Runtime:** Unity 2022.3 LTS (minimum `"unity": "2022.3"` in package.json)
- **Package format:** VPM (`com.staples.asm-lite`)

## Frameworks & SDKs
| Dependency | Version | Role |
|---|---|---|
| VRChat Avatars SDK (`com.vrchat.avatars`) | ≥ 3.7.0 | `VRCAvatarDescriptor`, `VRCExpressionParameters`, `VRCExpressionsMenu`, `IPreprocessCallbackBehaviour` |
| VRCFury (`com.vrcfury.vrcfury`) | ≥ 1.999.0 | Merges Toggle/FullController params before ASM-Lite build; runs at `callbackOrder=int.MinValue` |
| UnityEditor (built-in) | 2022.3 | `AnimatorController`, `AssetDatabase`, `EditorWindow`, `Undo` |
| NUnit | Unity bundled | EditMode test framework |

## Assemblies
| Assembly | Type | Key references |
|---|---|---|
| `ASMLite` (`ASMLite.asmdef`) | Runtime | `VRC.SDKBase`; no editor deps; reflection call to `ASMLiteBuilder.Build()` |
| `ASMLite.Editor` (`ASMLite.Editor.asmdef`) | Editor-only | `ASMLite`, `VRC.SDKBase`, `VRC.SDK3A` |
| `ASMLite.Tests.Editor` (`ASMLite.Tests.Editor.asmdef`) | Test | `ASMLite`, `ASMLite.Editor`, `VRC.SDK3A`, NUnit |

## Build Tooling
- **Compile check:** `dotnet build` via generated `.csproj` (`Tools/ci/generate-csproj.sh`), no Unity license needed
- **Test:** GameCI + Unity 2022.3.22f1 headless EditMode
- **Lint:** Super-Linter (Markdown, JSON, YAML, GitHub Actions, ShellCheck)
- **Release validation:** `Tools/ci/verify-release-gate.ps1`, `Tools/ci/verify-shadow-project-hygiene.ps1`

## Configuration Files
- `Packages/com.staples.asm-lite/package.json` — VPM manifest (version, dependencies)
- `Packages/com.staples.asm-lite/ASMLite.asmdef` — runtime asmdef; define `ASM_LITE_VRCFURY` when VRCFury present
- `Packages/com.staples.asm-lite/Editor/ASMLite.Editor.asmdef` — editor asmdef
- `.markdownlint.json` — Markdown lint (max line 200)
- `.yamllint.yml` — YAML lint
- `.shellcheckrc` — shell lint

## Optional Build Flags
- `ASM_LITE_VERBOSE` — verbose `Debug.Log` in build pipeline (Player Settings → Scripting Define Symbols)
<!-- GSD:stack-end -->

<!-- GSD:conventions-start source:CONVENTIONS.md -->
## Conventions

## Naming
- **Types:** PascalCase with `ASMLite` prefix (`ASMLiteBuilder`, `ASMLiteComponent`)
- **Namespaces:** `ASMLite` (runtime), `ASMLite.Editor` (editor), `ASMLite.Tests.Editor` (tests)
- **Constants:** PascalCase (`CtrlParam`, `FXController`, `GearIconPaths`)
- **Fields:** camelCase for serialized fields (`slotCount`, `iconMode`, `selectedGearIndex`)
- **VRC param names:** `ASMLite_` prefix (`ASMLite_Ctrl`, `ASMLite_Bak_<name>`, `ASMLite_Def_<name>`)
- **Asset paths:** centralized in `ASMLiteAssetPaths`; no inline literals

## Access Modifiers
- Prefer `internal` for editor utilities/constants outside public VPM surface
- Use `public` only for VPM public API (`ASMLiteComponent`, `ASMLiteBuilder.Build()`)
- Utility/builder classes should be `static` (`ASMLiteBuilder`, `ASMLitePrefabCreator`, `ASMLiteAssetPaths`)

## XML Documentation
## Error Handling
- Early returns + `Debug.LogError` for fatal errors (missing descriptor, invalid slot count)
- `Debug.LogWarning` for recoverable situations (zero params, missing expressionParameters)
- Prefix all logs with `[ASM-Lite]`
- Gate verbose logs with `#if ASM_LITE_VERBOSE`

## Null Safety
- Use `avDesc?.expressionParameters` null-conditional pattern
- Explicit null checks before iterating arrays
- Prefer early-return guards (`if (x == null) return`) over deep nesting

## C# Patterns Used
- `StringComparison.Ordinal` for string compares (no culture-sensitive default)
- `List<T>` with pre-sized capacity when count known
- `#if UNITY_EDITOR` guards in runtime files to keep editor-only code out of runtime assembly
- No async/await; all editor ops synchronous

## Visual Separator Comments
## Reflection Bridge Pattern
<!-- GSD:conventions-end -->

<!-- GSD:architecture-start source:ARCHITECTURE.md -->
## Architecture

## Pattern
## Layers
```
```

## Reflection Bridge
```csharp
```

## Parameter Model
| Parameter | Type | Encoding |
|---|---|---|
| `ASMLite_Ctrl` | Int | Shared trigger: Save=`(slot-1)*3+1`, Load=`(slot-1)*3+2`, Clear=`(slot-1)*3+3`; 0=idle |
| `ASMLite_Bak_<name>` | Matches source | Per-slot backup copies |
| `ASMLite_Def_<name>` | Matches source | Default values for reset/clear |

## FX Layer Structure
- **Idle state** → waits for `ASMLite_Ctrl` trigger value
- **Save state** → `VRCAvatarParameterDriver` copies live params → backup params
- **Load state** → `VRCAvatarParameterDriver` copies backup params → live params
- **Clear state** → `VRCAvatarParameterDriver` copies default params → live params

## Data Flow
```
```

## Entry Points
| Entry point | Trigger |
|---|---|
| `Tools → .Staples. → ASM-Lite` | Opens `ASMLiteWindow` |
| `ASMLiteWindow` → Add Prefab button | Calls `ASMLitePrefabCreator.CreatePrefab()` |
| Avatar upload (VRChat SDK) | Calls `ASMLiteComponent.OnPreprocess()` → `ASMLiteBuilder.Build()` via reflection |
<!-- GSD:architecture-end -->

<!-- GSD:skills-start source:skills/ -->
## Project Skills

No project skills found. Add skills in: `.claude/skills/`, `.agents/skills/`, `.cursor/skills/`, `.github/skills/` with `SKILL.md` index.
<!-- GSD:skills-end -->

## Graphify — Proactive Use for Exploration

A persistent knowledge graph of this codebase is available via the `graphify` skill. Use it proactively:

- **Before any multi-file exploration task** (tracing a call chain, finding all usages of a symbol, understanding how two systems connect): run `/graphify query "<question>"` against the existing graph rather than grepping blind.
- **If `graphify-out/graph.json` does not exist** (first session, or after a major refactor): run `/graphify Packages/com.staples.asm-lite` to build it, then query.
- **Prefer** `/graphify query` over repeated Grep/Read cycles when the question spans more than two files or involves symbol relationships.
- Graphify does **not** replace Grep for pinpoint lookups (single known symbol, exact string). Use Grep for those. Use graphify when the question is relational ("what calls X", "how does A connect to B", "where is this pattern used").

<!-- GSD:workflow-start source:GSD defaults -->
## GSD Workflow Enforcement

Before `Edit`, `Write`, or other file-changing tools, start via GSD command so planning artifacts + execution context stay in sync.

Entry points:
- `/gsd-quick` for small fixes, docs, ad-hoc tasks
- `/gsd-debug` for investigation + bug fixing
- `/gsd-execute-phase` for planned phase work

Do not edit repo directly outside GSD workflow unless user explicitly asks to bypass.
<!-- GSD:workflow-end -->

<!-- GSD:profile-start -->
## Developer Profile

> Profile not configured yet. Run `/gsd-profile-user` to generate.
> Managed by `generate-claude-profile` — do not edit manually.
<!-- GSD:profile-end -->
