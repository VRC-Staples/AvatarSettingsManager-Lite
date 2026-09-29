# ASM-Lite

ASM-Lite provides avatar settings backup and restoration through generated avatar assets. This glossary distinguishes the assets and references involved in maintenance recovery decisions.

## Language

**Package generated outputs**:
The ASM-Lite generated assets and package prefab, together with their Unity metadata, belonging to the package rather than an individual avatar's local generated-asset set.
_Avoid_: Avatar backup, settings slot

**Avatar-local generated assets**:
An avatar-specific generated-asset set outside the package output location; the code also calls this a vendorized mirror. It is distinct from the avatar's authored assets and from copies retained solely for recovery.
_Avoid_: Recovery backup

**Live FullController references**:
The generated controller, menu, and expression-parameter asset references held by the attached ASM-Lite VRCFury FullController. These are distinct from references held by the avatar descriptor.
_Avoid_: Descriptor references

**Package-output recovery copy**:
An on-disk copy of package output state retained to recover an interrupted or failed asset restoration. It is not an ASM-Lite settings slot containing avatar parameter values.
_Avoid_: Settings backup slot

**Captured package baseline**:
The package generated outputs, including their Unity metadata, as they existed when the snapshot was captured. They may differ from the outputs present when restoration begins.
_Avoid_: Previous assets

**Pre-restore package outputs**:
The package generated outputs, including their Unity metadata, present immediately before a snapshot restore attempt. This is a distinct state from the captured package baseline.
_Avoid_: Previous assets

**Expected diagnostic check**:
A smoke negative check satisfied when the observed failure diagnostic contains both the configured diagnostic code and text. Satisfying this check does not mean the underlying action succeeded or the whole suite passed; required cleanup can still fail the run.
_Avoid_: Expected success, ignored failure

## Maintenance recovery implementation

Package-output restore prepares verified disk copies under the Unity project's `.artifacts/asm-lite-recovery/<unique-id>/`. `captured-baseline` and `pre-restore` are deliberately different recovery choices. A caught failed restore attempts to recover `pre-restore`, retains the captured baseline, and reports the failure even if recovery succeeds. Read the retained directory's `RECOVERY.txt` before manual recovery; incomplete preparation is not a verified copy. No automatic next-launch recovery is implemented. See `BUGFIX-DECISIONS.md` for verification and limits.

## Smoke execution ownership

`ASMLiteSmokeOverlayHostRunner` is the sole smoke execution owner. Execution checks use the existing `CreateRunnerForTesting` command/tick seam and the `smoke-overlay-host-headless` CI lane; protocol contracts remain in `smoke-protocol-headless`. Shared execution payload/result/failure types and `ASMLiteSmokeExpectedDiagnosticMatcher` remain in `ASMLiteSmokeRunExecutor.cs`; the duplicate synchronous executor and its fixture are retired.

Expected diagnostic coverage checks host events and persisted result/failure artifacts for matching code and text, unexpected action success, separate wrong-code and wrong-text failures, and ordinary failure without expected-diagnostic arguments. Required cleanup remains independently able to fail the run after a diagnostic match.

Diagnostic matching remains text-based. This consolidation adds no diagnostic-origin policy; whether console-origin diagnostic text may satisfy a negative check remains outside this change.
