# ASM-Lite maintenance branch — code review

**Baseline:** local `maintenance` at `bc091accb7b27fb2d29346246986085b8463e025` (same commit as `main` and release `1.1.0`).

**Verdict:** Changes requested before treating these failure paths as safe. This is a static, read-only code audit, not a reproduced Unity failure or remote PR review. No fixes were made.

## Findings

### High — Vendorized rebuild rollback can leave live FullController references behind

`Packages/com.staples.asm-lite/Editor/ASMLiteWindow.cs:6270–6335` refreshes live references, stages replacement assets, and retargets them. On a failure after staging, cleanup rolls back the mirror and descriptor but does not restore the live FullController references. The staging helper covers an earlier failure path (`:6407–6423`), not every later failure. A later retarget or prefix-sync failure could therefore leave live references pointing to removed replacement assets. This failure mode is inferred from control flow; it was not reproduced in Unity.

**Smallest strong remedy:** Restore the live FullController references along with descriptor and asset state on every failed rebuild. Add a failure-path test asserting all three states after rollback.

### High — Snapshot restore deletes original assets before replacement is secure

`Packages/com.staples.asm-lite/Editor/ASMLitePackageGeneratedOutputSnapshot.cs:49–60` deletes every captured root, then writes the copies held in memory. A write error or editor termination between these loops can leave generated assets and their `.meta` files missing. The injected restore failure at `:51–52` fires *before* deletion, so it does not cover this risk.

**Smallest strong remedy:** Stage recoverable copies on disk and restore with a rollback mechanism that preserves originals if writing fails. Exercise a failure after deletion has begun, not only the pre-delete hook.

### Medium — Install-path sync can report success without clearing stale prefix

For prefab instances, `Packages/com.staples.asm-lite/Editor/ASMLiteBuilder.cs:929–943` accepts successful MoveMenu routing even when `cleared` is false. `Packages/com.staples.asm-lite/Editor/ASMLiteFullControllerWiring.cs:150–184` shows prefix clearing can fail. This can report successful synchronization while a stale FullController prefix remains, including after a custom prefix is removed. The resulting menu layout depends on the pre-existing override; this was not reproduced in Unity.

**Smallest strong remedy:** Check that prefix clearing achieved the required final state before returning success. Cover the failed-clear branch in a prefab-instance test.

### Medium — Window combines UI, transactions, and automation contract

`Packages/com.staples.asm-lite/Editor/ASMLiteWindow.cs` holds editor UI, rebuild/rollback policy (`:6270–6450`), and a large automation surface (`:6880` onward). That coupling makes transactional invariants harder to audit: the adapter-boundary test checks source text rather than the rollback behavior. File size alone is not the finding; the mixed ownership is.

**Smallest strong remedy:** When fixing the rollback path, move transactional policy behind the existing operations/service boundary while keeping UI and automation behavior intact. Do not rewrite the whole window merely to reduce line count.

## Coverage and limits

Inspected the local branch, primary build and asset-lifecycle paths, associated tests, and CI/test-harness context. Repository inventory at review time: 514 tracked files, 113 C# files, 79 C# test files. This is not a line-by-line proof of every code path. No Unity tests or side-effecting test harnesses were run; reported failure paths are static inferences. No files were changed during the audit itself. This Markdown report is the sole subsequent repository addition requested for recordkeeping.
