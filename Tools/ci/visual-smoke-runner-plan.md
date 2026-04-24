# Visual Smoke Runner Plan

> For Hermes: use `design-for-ai` when implementing the Rust overlay UI.

## Goal

Replace the old Python overlay/state-ack flow with a new Rust overlay and a new Unity protocol.

The new system should:
- keep Unity open for the full session
- let the user choose a grouped smoke suite from the overlay
- run the selected suite against `Assets/Click ME.unity` using `Oct25_Dress`
- stop on first failure within that suite
- require a manual review gate before returning to the suite list, rerunning, or exiting
- support a global reset default:
  - Scene Reload
  - Full Package Rebuild
- allow per-suite reset override metadata

---

## Grounded Base Already Present

Bentwire verified these existing foundations in the repo:

- Scene target already exists:
  - `Assets/Click ME.unity`
- Avatar target already exists in scene:
  - `Oct25_Dress`
- Existing ASM-Lite editor window already exposes automation hooks:
  - `OpenForAutomation`
  - `SelectAvatarForAutomation`
  - `AddPrefabForAutomation`
  - `RebuildForAutomation`
  - `VendorizeForAutomation`
  - `DetachForAutomation`
  - `ReturnToPackageManagedForAutomation`
- Existing launch scripts already pass custom command-line args into Unity execute methods.
- Existing Python overlay attempt used familiar panel geometry:
  - width `460`
  - top-right anchoring with `24px` margin
- Unity Test Framework docs in repo confirm:
  - `UnityTest` EditMode tests run on `EditorApplication.update`
  - `runSynchronously` cannot be used for multi-frame visible smoke work

---

## Final Product Decisions

These decisions were confirmed:

1. Rust overlay launches Unity and passes selected suite args.
2. Many discrete smoke cases/suites run inside one Unity session.
3. Overlay shows a suite list the user can select from.
4. Stop on first failure within a suite.
5. Emit structured failure output usable for LLM debugging.
6. Manual gate remains required.
7. Review screen includes:
   - Return to Suite List
   - Rerun Suite
   - Exit
8. Reset choice is:
   - global default set by user
   - per-suite override metadata allowed
9. Coverage includes:
   - ASM-Lite editor window only
   - lifecycle actions: rebuild/vendorize/detach/return
   - playmode/runtime checks
10. Canonical avatar name is `Oct25_Dress`.
11. Rust overlay lives at:
    - `Tools/ci/rust-overlay`
12. Overlay design should use `design-for-ai` principles.

---

## Recommended Architecture

1. Rust overlay becomes session authority.
2. Rust overlay launches Unity once in overlay-host mode.
3. Unity host loads scene, selects `Oct25_Dress`, then idles waiting for commands.
4. Overlay renders grouped suite list from a shared suite catalog.
5. User selects suite.
6. Overlay sends run command through the new protocol.
7. Unity runs the selected suite and streams structured events.
8. On pass/fail, Unity enters review-wait state.
9. Overlay shows results screen with:
   - summary
   - failed step or pass state
   - structured debug excerpt
   - buttons:
     - Return to Suite List
     - Rerun Suite
     - Exit
10. Overlay sends review decision command.
11. Unity either:
   - returns to idle suite-select-ready state
   - reruns the same suite
   - shuts down cleanly

---

## New Protocol Recommendation

Bentwire recommends a new versioned file-based command/event protocol.

Reason:
- current repo/tooling already passes filesystem paths cleanly into Unity
- Unity domain reload and editor lifecycle tolerate files better than live socket state
- easier artifact capture and replay
- easier LLM handoff after failure
- still fully fresh protocol, not a reuse of the old state/ack design

### Session Directory

Per overlay session:

`artifacts/smoke-overlay/session-<id>/`

### Core Files

- `session.json`
  - overlay-created session metadata
  - project path
  - protocol version
  - Unity pid if available
- `suite-catalog.snapshot.json`
  - exact suite list shown in UI for the session
- `commands/`
  - append-only command files from overlay to Unity
- `events/events.ndjson`
  - append-only Unity-emitted event stream
- `runs/run-<id>/`
  - per-suite artifacts

### Command Types

- `launch-session`
- `run-suite`
- `review-decision`
- `rerun-suite`
- `shutdown-session`

### Event Types

- `session-started`
- `unity-ready`
- `suite-started`
- `case-started`
- `step-started`
- `step-passed`
- `step-failed`
- `reset-started`
- `reset-finished`
- `suite-passed`
- `suite-failed`
- `review-required`
- `session-idle`
- `session-exiting`

### Per-Run Result Files

- `runs/run-<id>/result.json`
- `runs/run-<id>/failure.json` when failed
- `runs/run-<id>/events.slice.ndjson`
- `runs/run-<id>/nunit.xml`
- optional `runs/run-<id>/debug-summary.txt`

### Failure Payload Schema

Minimum fields:
- `suiteId`
- `suiteLabel`
- `caseId`
- `caseLabel`
- `stepId`
- `stepLabel`
- `failureMessage`
- `stackTrace`
- `resetPolicyUsed`
- `scenePath`
- `avatarName`
- `lastEvents[]`
- `artifactPaths{}`
- `timestampUtc`

---

## Shared Suite Catalog

Create one shared catalog both Rust and Unity read.

Path:
- `Tools/ci/smoke/suite-catalog.json`

Why:
- overlay can render suite list before Unity starts
- Unity can validate requested suite id
- suite definitions stay centralized

### Catalog Structure

Include:
- protocol version
- grouped suites
- suite label and description
- suite id
- group id
- optional per-suite reset override
- ordered cases
- ordered steps
- automation action references
- expected outcome
- stop-on-fail flag
- runtime/playmode requirement flag
- artifact/debug hint text

### Initial Groups

1. Editor Window
2. Lifecycle
3. Playmode / Runtime

### Initial Suite Examples

#### Editor Window
- open window
- select avatar
- add prefab
- verify rebuild action visible

#### Lifecycle
- rebuild
- vendorize
- return to package-managed
- detach
- reattach/return

#### Playmode / Runtime
- install
- enter playmode
- verify runtime component remains valid
- return to edit mode

---

## Rust Overlay Design

Use `design-for-ai` principles:
- one dominant status/header region
- grouped suite list with strong whitespace hierarchy
- minimal ornamentation
- stable right-side fixed panel
- obvious failure block
- no noisy animation-heavy chrome

### Visual Direction

Bentwire recommends a polished ship-gate panel:
- dark neutral background
- one accent color for active/running state
- green/red/yellow only for semantic status
- high-contrast text
- small number of visual layers
- no glassmorphism

### Window Behavior

- always-on-top
- fixed width `460px`
- top-right anchor with `24px` margin
- fixed right-side behavior like Python attempt
- fixed default height clamped to screen
- internal scroll areas when content exceeds visible height
- standard window chrome unless later review proves borderless is materially better

### Recommended Overlay States

1. Boot
   - launch Unity
   - show progress until `unity-ready`
2. Suite List
   - grouped scrollable suite picker
   - global reset selector
   - suite description pane
   - Run button
3. Running
   - dominant current suite/case/step
   - step progress list
   - event log tail
   - current reset policy
4. Results Review
   - pass/fail summary
   - dominant failed step or success banner
   - structured debug excerpt
   - buttons:
     - Return to Suite List
     - Rerun Suite
     - Exit

### Recommended Rust Crate Layout

- `Tools/ci/rust-overlay/Cargo.toml`
- `Tools/ci/rust-overlay/src/main.rs`
- `Tools/ci/rust-overlay/src/app.rs`
- `Tools/ci/rust-overlay/src/model.rs`
- `Tools/ci/rust-overlay/src/protocol.rs`
- `Tools/ci/rust-overlay/src/session.rs`
- `Tools/ci/rust-overlay/src/unity_launcher.rs`
- `Tools/ci/rust-overlay/src/event_reader.rs`
- `Tools/ci/rust-overlay/src/ui_suite_list.rs`
- `Tools/ci/rust-overlay/src/ui_running.rs`
- `Tools/ci/rust-overlay/src/ui_results.rs`
- `Tools/ci/rust-overlay/src/theme.rs`

---

## Unity Host Side Plan

New Unity-side host should be separate from old visible overlay classes.

### New Unity Files

- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeOverlayHost.cs`
- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeProtocol.cs`
- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeCatalog.cs`
- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeRunExecutor.cs`
- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeResetService.cs`
- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeFailureReport.cs`

### Host Responsibilities

- parse command-line args from Rust launcher
- initialize session directory
- open `Assets/Click ME.unity`
- find/select `Oct25_Dress`
- emit `unity-ready`
- poll command mailbox on editor update
- execute selected suite
- stop on first failure in current suite
- emit structured result artifacts
- block in review-required state until overlay decision
- remain alive for next suite without closing Unity

---

## Reset Model

Global default from overlay:
- `SceneReload`
- `FullPackageRebuild`

Per-suite override:
- if suite metadata specifies override, Unity uses suite override
- overlay UI still shows effective policy during run

### Reset Actions

#### Scene Reload
- reload `Assets/Click ME.unity`
- re-find `Oct25_Dress`
- reselect avatar
- restore ASM-Lite window automation context

#### Full Package Rebuild
- re-open scene if needed
- reselect avatar
- rerun suite-defined baseline install/rebuild path
- confirm canonical editor state before next case

Important:
- reset choice should be visible during run
- reset choice should be captured in failure artifacts

---

## Old Protocol Removal Plan

Remove old external overlay path once new stack passes verification.

### Likely Removal Targets

- old external overlay state/ack plumbing in `ASMLiteWindow`
- old Python overlay dependency from local smoke scripts
- old visible overlay env vars and args after replacement
- legacy visible-overlay helper script artifact (removed in canonical path)

### Keep and Reuse

- useful `ASMLiteWindow` automation action methods
- useful scene/avatar bootstrap logic
- useful batch/smoke execution helpers that can be refactored into the new executor

---

## Implementation Phases

### Phase 1 — Shared Contract

Create:
- `Tools/ci/smoke/suite-catalog.json`
- `Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeProtocol.cs`
- Rust `protocol.rs`

Deliver:
- versioned schemas
- grouped suite metadata
- command/event/result models

Verify:
- Unity and Rust can both parse the same catalog
- protocol serialization tests pass

### Phase 2 — Rust Overlay Shell

Create:
- Rust crate under `Tools/ci/rust-overlay`
- suite list UI
- theme/layout system
- Unity launcher process wrapper

Deliver:
- launch Unity from overlay
- fixed right-side always-on-top panel
- grouped scrollable suite list
- global reset selector

Verify:
- overlay launches Unity with expected args
- overlay remains responsive while waiting
- suite count overflow scroll works

### Phase 3 — Unity Host + Idle Loop

Create:
- `ASMLiteSmokeOverlayHost.cs`
- `ASMLiteSmokeRunExecutor.cs`

Deliver:
- Unity host starts once
- scene loads
- `Oct25_Dress` selected
- `unity-ready` emitted
- host waits for `run-suite`

Verify:
- boot-to-ready works repeatedly
- host survives scene reload and editor update cycles
- suite selection command enters run state

### Phase 4 — First Real Suites

Implement grouped suites:
- Editor Window
- Lifecycle
- Playmode / Runtime

Deliver:
- ordered case/step execution
- stop-on-first-fail
- structured failure payload
- per-run artifacts

Verify:
- each suite can pass
- injected fail stops suite immediately
- failure artifact is useful for debugging

### Phase 5 — Review Gate + Rerun/Menu Flow

Deliver:
- results screen
- Return to Suite List
- Rerun Suite
- Exit
- Unity review-required wait state

Verify:
- pass run returns to suite list without restarting Unity
- fail run can rerun same suite
- exit shuts Unity down cleanly

### Phase 6 — Old Stack Removal

Deliver:
- scripts updated to Rust overlay flow
- Python overlay removed
- old state/ack plumbing removed
- docs updated

Verify:
- no script path references old overlay
- no Unity code path depends on old external overlay files
- local end-to-end run works from new launcher only

---

## Testing Plan

### Unity/NUnit Tests to Add

- `ASMLiteSmokeProtocolTests.cs`
- `ASMLiteSmokeCatalogTests.cs`
- `ASMLiteSmokeResetServiceTests.cs`
- `ASMLiteSmokeRunExecutorTests.cs`
- `ASMLiteSmokeOverlayHostTests.cs`

### Rust Tests to Add

- protocol encode/decode
- session directory management
- event reader tailing
- suite list grouping/selection model
- review-state transition tests

### End-to-End Verification

1. Launch overlay
2. Unity becomes ready
3. Run one editor suite
4. Return to suite list
5. Run lifecycle suite in same Unity session
6. Inject fail in playmode suite
7. Review failure payload
8. Rerun
9. Exit cleanly

---

## Acceptance Criteria

- overlay launches Unity itself
- Unity opens once per session
- grouped suites are visible in a right-side fixed overlay
- suite list scrolls when long
- selected suite runs without restarting Unity
- stop-on-first-fail works
- manual gate works
- Return to Suite List / Rerun / Exit all work
- global reset default works
- per-suite reset override works
- failure artifact is immediately usable for LLM debugging
- old overlay protocol is fully removed

---

## Design Notes for Overlay

Apply `design-for-ai`:
- dominant element:
  - current suite/case/step panel in running state
  - result banner in results state
- hierarchy:
  - whitespace first
  - weight second
  - color only for semantic status
  - minimal ornamentation
- composition:
  - one strong header/status anchor
  - grouped suite list beneath
  - action row at bottom
  - eye path: title -> selected suite -> reset mode -> Run
- scroll:
  - suite list only
  - event log tail only
  - avoid full-window scroll when possible

---

## Bentwire Recommendation Summary

- new file-based versioned command/event protocol
- Rust overlay as session authority
- shared suite catalog JSON
- persistent Unity host process
- grouped suite picker
- fixed 460px right-side always-on-top overlay
- opaque polished dark ship-gate design
- stop-on-first-fail
- manual review gate
- full old overlay removal after new path passes
