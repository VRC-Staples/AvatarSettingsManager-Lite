# ASM-Lite maintenance bugfix decisions

Date: 2026-09-29

## Scope and status

The user accepted all three behavioral contracts below during `/grill-with-docs`. Scope is the three suspected bugs in `MAINTENANCE-CODE-REVIEW.md`; structural changes are limited to what these fixes require. The original audit remains unchanged.

The later explicit `/implement` request superseded the discussion-only pause. All three fixes are implemented and focused Unity checks pass in an isolated synthetic project. Changes remain uncommitted; no push or publication occurred. The original audit is preserved as historical evidence, not rewritten to imply it was a runtime test.

## Accepted contracts

### Failed rebuild recovery

Restore the previous generated assets and their descriptor and live FullController references after a failed rebuild. Do not silently substitute package-managed assets for the previous avatar-local state. If recovery itself fails, preserve available recovery backups and report failure explicitly.

Original static evidence: `Packages/com.staples.asm-lite/Editor/ASMLiteWindow.cs:6387–6495` in the audited version. The late failure cleanup restored descriptor references before recovering asset identity and omitted live FullController reference restoration. The fix restores the mirror first, then rebinds descriptor and live references. Pre-build cleanup is redirected away from the previous mirror so it cannot mutate the assets that rollback must preserve.

Focused acceptance evidence should exercise a failure after replacement assets have been promoted or referenced, then check restored assets, descriptor references, live FullController references, and unchanged ownership settings. If rollback fails, the result must not claim success and must identify retained recovery material.

### Package-output restore protection

Handle caught restore errors with automatic rollback while Unity remains running. Keep durable recovery copies for manual recovery if Unity terminates during the restore. Automatic recovery on the next editor launch is out of scope.

The recovery target is now explicit: if restoring the captured package baseline fails after replacement begins, automatic recovery attempts to put back the package outputs present immediately before the restore attempt. Retain the captured package baseline for manual recovery and report the failed restore even if this rollback succeeds. If rollback also fails, retain available recovery copies and identify them in the failure diagnostic. Do not substitute a second attempt to finish restoring the captured baseline for this rollback policy.

Original static evidence: `Packages/com.staples.asm-lite/Editor/ASMLitePackageGeneratedOutputSnapshot.cs:49–60` in the audited version. It deleted captured roots before writing in-memory copies. The fix flushes and verifies both recovery states on disk before deletion; new failure injection exercises deletion and failed rollback.

Prepare and verify disk recovery material before destructive replacement. Preserve asset bytes and `.meta` identity together, and do not remove the only recoverable copy on a failed restore. Recovery instructions must distinguish the captured pre-build package state from the state immediately before the restore attempt. This is not a promise of atomic multi-file updates or protection against physical storage failure.

Focused acceptance evidence should inject a failure after replacement has begun, verify automatic recovery where possible, and verify durable, identifiable recovery material when recovery cannot finish. A controlled interrupted-operation fixture can demonstrate retained recovery data without terminating the user's editor.

### Prefab-instance install-path synchronization

Success requires both correct MoveMenu routing and a confirmed empty direct FullController menu prefix. Successful routing alone must not mask an uncleared or unreadable prefix. Return a specific failure diagnostic when the final prefix state cannot be confirmed; no new repair/retry policy is requested.

Original static evidence: `Packages/com.staples.asm-lite/Editor/ASMLiteBuilder.cs:1047–1061` and `Packages/com.staples.asm-lite/Editor/ASMLiteFullControllerWiring.cs:150–184` in the audited version. The caller now requires both routing success and confirmed prefix clearing.

Focused acceptance evidence should cover routing success with prefix-clear failure both when a custom path is enabled and when it is removed. Existing prefix-writing helpers already attempt corrective writes; reuse the shared boundary rather than adding separate caller-specific patches.

## Implementation and verification boundaries

- Preserve all pre-existing uncommitted work, including post-VRCFury reconciliation and test-harness fixes.
- Preserve `KikiASM/Assets/kikiasm.unity`, its pose-system assets, and existing ignored backups. Do not use the original avatar scene for destructive regression tests.
- Reuse existing operations, wiring, and asset-lifecycle services where they fit. Do not refactor the whole editor window or introduce a generic transaction framework.
- Use synthetic fixtures and the separate CI Unity project for scoped regression verification, subject to authorization. Inspect setup, teardown, and shared package-output side effects before running tests; preserve and verify any affected shared assets.
- Previously passing broad suites remain historical evidence and were not rerun merely to refresh status. The later implementation request authorized focused regression execution.
- No commits, pushes, releases, publication, discretionary subagents, or independent review campaign are authorized by these decisions.

## Implementation evidence — 2026-09-29

Tests used Unity 2022.3.22f1, `CI=true`, EditMode, and the isolated project `F:/Hermes/Home/cache/scratch/asmlite-bugfix-unity`. Commands used `python F:/Hermes/Home/cache/scratch/asmlite-run-bugfix-tests.py <label> <filter>` from the repository root. XML and logs are `F:/Hermes/Home/cache/scratch/asmlite-<label>.xml` and `.log`.

| Label | Filter | Result |
|---|---|---|
| `prefix-green-live` | `ASMLiteInstallPathWiringTests` | 14/14 passed |
| `affected-final` | `PackageOutputIsolationIntegrationTests;ASMLitePackageGeneratedOutputSnapshotTests` | 18/18 passed |
| `staging-final` | `Rebuild_Failure_RestoresAssetIdentityAndAllReferences` | 4/4 passed after the final staging guard |

Selections overlap; do not sum them as unique tests. Final rebuild cases cover vendorized and package-managed ownership with failure before promotion and before finalization. Failed rollback retains the old mirror and reports its path. Snapshot tests cover normal restore, pre-restore byte/metadata recovery, both disk copies existing at the destructive checkpoint, double failure, and corrupt-copy rejection before deletion.

Rollback and snapshot failures were reproduced before their fixes. Prefix diagnosis is supported by inspected control flow and the final passing fixture, but the corrected live-component fixture was not rerun against old production code; earlier red runs had fixture ambiguity. No clean prefix red/green claim is made.

Recovery material lives under the Unity project's `.artifacts/asm-lite-recovery/<unique-id>/`, outside imported assets. It contains `captured-baseline`, `pre-restore`, root-presence manifests, and `RECOVERY.txt`. Success removes that attempt's directory; failure retains available material and reports its path. Incomplete preparation is identified as incomplete and never starts replacement. Tests inject interruption in a running Unity process; they do not kill Unity, simulate physical disk failure, or establish atomic multi-file updates. Automatic next-launch recovery remains out of scope.

## Closeout review and preservation

Local task-diff self-review: no blocking findings in the reviewed scope. Seven C# diffs were checked against pre-task copies, with relevant callers and cleanup paths inspected. Review caught another instance of the rollback defect: a staging-copy exception could delete an untouched mirror before any backup existed. The final guard and four-case rebuild regression cover this path. No independent reviewer or subagent was used.

Existing snapshot, mirror, and wiring ownership was reused; no transaction framework, dependency upgrade, or window rewrite was added. Logical line counts (pre-task to final): builder 2466 to 2475; mirror service 1083 to 1116; snapshot 196 to 306; window 8429 to 8460; integration fixture 562 to 644; wiring fixture 521 to 573; snapshot fixture 92 to 191. Already-large files are pre-existing constraints; these narrow fixes do not justify a broad extraction.

- All 378 backup copies in `.artifacts/pre-maintenance-bugfix-20260929T200403Z/manifest.json` still match their recorded hashes. In the working tree, 369 remain byte-identical; nine differences are exactly the seven task C# files and test ledger/mirror. Pre-existing builder reconciliation is unchanged outside the narrow prefix diff.
- Original package generated outputs, prefab and metadata, CI project assets/settings, and `KikiASM/Assets/kikiasm.unity` and metadata match the pre-task manifest.
- The older `KikiASM/LocalBackups/pre-av3-pose-20260929T182353Z/manifest.json` verifies 201 of 202 current files, including all 130 pose-system files. The sole difference is the previously synchronized smoke host; its bytes equal the verified pre-task repository copy. Previously absent `Assets/ASM-Lite` and metadata remain absent. No original avatar scene was used for these tests.
- Ledger and generated mirror validate at 538 classified methods. `git -c core.whitespace=cr-at-eol diff --check` passes. Ordinary whitespace checking flags existing CRLF; no line-ending cleanup was attempted.
- Suite-map validation encounters obsolete `editmode-batch-runs.json` text inside the ignored preservation backup. This is incidental traversal of retained evidence, not a changed suite-selection result. Backup and validator were left intact; no new suite-map pass is claimed.

No broad CI rerun, Linux validation, external-avatar UAT, release certification, or real-process crash test is claimed. Earlier passing broad results remain historical evidence.
