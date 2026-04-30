# Rust Overlay QA Design Lock

## Locked model

- Rust overlay is session authority.
- Unity launches idle and only executes file commands.
- Visible QA is operator-driven; CLI seeds suites but does not auto-run.
- `--exit-on-ready` is readiness smoke only, not full UAT evidence.
- External UAT project only: `../Test Project/TestUnityProject`.
- Never use `Tools/ci/unity-project` for UAT.

## Suite flow

- Checklist multi-select.
- Selected order = run order.
- Safe quick setup batch selected by default.
- Empty selection disables Run and shows `Select at least one suite`.
- Unknown/synthetic suite IDs rejected.
- Catalog is single source: `Tools/ci/smoke/suite-catalog.json`.
- Preset chips select batches; they do not filter/hide suite list.
- Search may filter suite list.
- If selected suites are hidden by search, show warning count.

## Batch flow

- One `run-suite` command per suite.
- Overlay chains batch.
- On pass: auto-dispatch next selected suite.
- On full pass: show quiet success summary, stay on suite select, keep selection, reset head to first selected.
- Progress display is only `2 of 5` in header/status and Current Run Plan. No extra batch text.
- Current suite label and active step live in Current Suite card.

## Failure/review

- On fail/abort: stop batch, enter review-required, preserve Unity state.
- No auto-clean on failure.
- Review body shows failed case/step, message, debug hint, artifact paths.
- Raw JSON is linked/path only, not inline.
- Export Logs is non-mutating; review state stays review-required.
- Suite failure is warning/review tone, not host crash.

## Rerun lock

- Review primary action is rerun.
- Label: `Rerun from First Selected`.
- Rerun always starts at first selected suite.
- If rerun passes, treat it like a normal run and continue normal selected-batch flow.

## Review actions

- Return to Suite List keeps selected suites and resets head to first selected.
- Return status says `Ready`.
- Rerun is primary.
- Export is secondary.
- Exit No Save is available as danger action.

## Destructive lock

- Destructive suites disabled unless checkbox enabled.
- Destructive rows show danger badge + disabled reason until enabled.
- Running any batch containing destructive suites requires confirm before dispatch.
- Confirm text: `Run destructive drills, are you sure?`
- Passed destructive cleanup only via catalog-visible cleanup steps.
- Failed destructive suite preserves state like any failure.

## Protocol/artifacts

- Versioned file protocol.
- Commands JSON, events NDJSON, host-state JSON.
- Append-only numbered command files, Unity processes sorted seq.
- Failure artifacts:
  - `result.json`
  - `failure.json`
  - `events.slice.ndjson`
  - `nunit.xml`
  - `debug-summary.txt`

## UI layout

- Right-edge Rust overlay panel.
- Default 480px, resizable wider.
- Structure:
  - header/status
  - Current Run Plan
  - Suites
  - Current Suite
  - Actions
  - Utilities/Advanced
  - Recent Events
- One dominant primary CTA per phase.
- Disabled secondary controls stay visible with reason.
- Human status up top; raw telemetry in Advanced.
- Recent Events collapsed normally, expanded in review/error.
- Recent Events preserves loaded events in bounded scroll.

## Run Plan

- Always visible before run.
- Shows selected suites in order.
- Future suites muted.
- Completed suites marked pass/fail/aborted.
- Steps collapsed by default; click suite to expand.
- Current Suite card shows expected outcome + debug hint collapsed.

## Actions

- Primary Run label: `Run Selected` always.
- Running primary: Abort Run as danger.
- No-save exit always visible in Utilities/Advanced.
- No-save exit confirms only when suite running or destructive selected.
- Relaunch creates fresh session directory.
- Relaunch visible in host error / Unity exited; advanced otherwise.

## Reset/timing

- Reset default: SceneReload.
- Per-suite override allowed.
- Step sleep timer default off.
- Optional 1.5s delay.
- Locked while running.

## Visual/design

- Dark sci-fi dashboard.
- Raised cards.
- Blue primary.
- Amber review/warning.
- Red danger.
- Phosphor icons.
- No broken decorative Unicode literals.
- Contrast/text clarity beats glow.

## Docs/tests

- Update `Tools/ci/visual-smoke-runner-plan.md` and wrapper help/docs.
- Rust GUI/model tests first.
- Unity host tests only when host behavior changes.
- Screenshot compare against reference after UI flow changes.
- Implement via small TDD vertical slices, not rewrite.
