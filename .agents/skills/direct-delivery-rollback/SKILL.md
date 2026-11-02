---
name: direct-delivery-rollback
description: Check the Detach and Vendorize + Detach rollback boundary after changes to ASMLiteDirectDeliveryTransaction, its snapshot, builder, verification, mirror, or lifecycle callers. Exhaust finite dependency outcomes against the actual C# transaction without Unity.
---

# Direct-delivery rollback

An unsuccessful direct-delivery attempt must restore its captured avatar state before the caller observes the exit, or throw a rollback error.
Otherwise, outer rollback can inspect partially changed assets and fail its ownership checks.

Use this skill after changes to the files in the coverage record below.
Read the repository `AGENTS.md` before execution.
Run commands from the repository root, three directories above this skill.

## Run the check

Use Python 3.11 or later and .NET SDK 8.0.426.
The checker pins that SDK in `Tools/formal/direct-delivery-rollback/global.json`.
It has no NuGet package dependencies and clears package sources.
It does not launch Unity, synchronize packages, edit product source, or publish anything.

Run the bounded check:

```bash
python Tools/formal/direct-delivery-rollback/check.py
```

If you change this checker or its dependency contracts, run its mutation checks too:

```bash
python Tools/formal/direct-delivery-rollback/check.py --self-test
```

The runner compiles a byte-for-byte copy of `ASMLiteDirectDeliveryTransaction.cs` with the checker environment.
It explores every terminal branch under the contracts below.
It preserves each execution receipt under `.planning/formal-asset-transaction/runs/` with a unique run ID and content-hash filename.
Each receipt records the product-source hashes, checker-file hashes, selected flags, and exit code.
The runner also archives any previous `latest-result.json` before replacing that convenience copy.
It removes only its temporary build directory after execution.

CI runs the receipt regression tests and the mutation check in the `Direct Delivery Rollback Check` job.
The job uses the pinned SDK and uploads the immutable receipts.
To run only the receipt regression tests locally, use:

```bash
python -B Tools/formal/direct-delivery-rollback/test_check.py
```

Interpret the exit codes as follows:

- `0`: The bounded check passed. Read any source-drift warning before claiming current coverage.
- `1`: The checker found a counterexample. Read the property, choices, and events in its JSON output.
- `2`: Source review is stale, a prerequisite failed, compilation failed, or exploration exceeded its limits.

The reviewed baseline explored 24,360 terminal traces, including successful commits, successful restoration, and rollback failures.
A changed trace count is not itself a regression.
Make sure that changed counts follow from the intended control flow or scope.

## Coverage record

The source review is synchronized at commit `677b96c98b9a551632040770bece3fb0629653ab`.
`Tools/formal/direct-delivery-rollback/coverage.json` records review hashes for six source files.
The review hashes convert CRLF line endings to LF so Windows and Linux checkouts agree.
Each execution receipt retains the exact byte hashes alongside those normalized review hashes.
Only the transaction file is compiled as production code.
The other five files define the reviewed dependency contracts and callers.

All paths in this table start at `Packages/com.staples.asm-lite/Editor/`.

| File | Functions | Coverage |
|---|---|---|
| `ASMLiteDirectDeliveryTransaction.cs` | `Execute`, `VerificationFailureContext`, plan and result constructors | Actual C# control flow under finite dependency contracts. |
| `ASMLiteDirectDeliveryRollbackSnapshot.cs` | `Capture`, `TryRestore`, `Dispose` | Assumed dependency behavior, not compiled or proved. |
| `ASMLiteBuilder.cs` | `TryDetachToDirectDelivery` | Success, false, and exception outcomes with partial payload changes. |
| `ASMLiteGeneratedAssetMirrorService.cs` | `RetargetAvatarGeneratedAssetsToVendorized` | Success, false, and exception outcomes with partial payload changes. |
| `ASMLiteLifecycleVerification.cs` | `VerifyDirectDeliveryState` | Read-only pass, failure, and exception outcomes. |
| `ASMLiteLifecycleTransactionService.cs` | `ExecuteDetachToDirectDelivery`, `ExecuteVendorizeAndDetach` | Callers establish valid component and avatar inputs. Outer rollback is outside the executable scope. |

`Tools/formal/direct-delivery-rollback/Environment.cs` defines dependency outcomes.
`Tools/formal/direct-delivery-rollback/Program.cs` explores branches and checks four properties:

- `failure-restores`: Every unsuccessful exit attempts restoration once. Successful restoration recovers the baseline before caller observation and preserves an existing dependency exception.
- `success-requires-verification`: Success requires successful detach, required retargeting, and verification. Success retains the selected payload without rollback.
- `rollback-error-escapes`: A false restore result raises a rollback error. A thrown restore error reaches the caller.
- `snapshot-disposed`: Each captured snapshot is disposed once before caller observation.

## Bounds and assumptions

This is bounded exhaustive execution of real C# control flow, not an unbounded proof or Unity acceptance test.
The payload has three tokens for menu content, expression parameters, and FX content.
Distinct tokens identify the baseline, detached payload, and retargeted payload.
Tokens do not represent individual Unity objects, GUIDs, or serialized bytes.

The explored environment includes these choices:

- Both expected installation states, `PackageManaged` and `Vendorized`.
- Both values of the retarget and failure-context flags.
- A null failure callback, or all eight stable combinations of the three failure-injection flags.
- Successful detach or retarget, or false and exception outcomes after each of eight partial-mutation masks.
- Read-only verification that passes, fails, or throws.
- Successful restoration, or false and exception outcomes after each of eight partial-restoration masks.

Capture succeeds with valid component and avatar inputs.
Successful detach and retarget update all three tokens.
Successful restoration recovers all captured tokens.
Snapshot disposal succeeds and does not mutate payload tokens.
Calls run synchronously without concurrent Unity edits, reentrancy, process termination, or retries.
The explorer rejects runs above 16 dependency choices per trace or one million terminal traces.
The runner also imposes a 90-second timeout per compilation or checker execution.

Do not claim coverage of snapshot cloning, Unity serialization, ownership checks, filesystem operations, package bytes, or outer lifecycle rollback.
Those dependencies still need their existing Unity tests when separately authorized.

## Diagnose a failure

Read the events in order.
For example, skipped restoration produces this sequence:

1. `Capture` records the baseline.
2. `TryDetachToDirectDelivery=0` applies the detached payload.
3. `VerifyDirectDeliveryState=1` rejects the payload.
4. The caller observes failure without `TryRestore`.

The counterexample shows different baseline and observed tokens.
The scratch source mutation that skips restoration produces this failure.
The self-test also rejects accepted failed verification, suppressed rollback errors, and missing snapshot disposal.
Each mutation changes a temporary copy of the real production file.
The self-test then compiles the original bytes again and requires the original result.

If the real source breaks a property under a valid contract, report a product finding with its trace.
Do not weaken the property or edit product code without authorization.
If a dependency contract no longer describes the source, identify that mismatch before changing the environment.
A compiler error or unmatched mutation is a checker maintenance problem, not proof of a product defect.

## Review source drift

Compare changed covered functions with the coverage record and `Environment.cs`.
Review the full changed functions, not only the matching source fingerprints.
If the contracts still apply, run the check against the changed source:

```bash
python Tools/formal/direct-delivery-rollback/check.py --allow-source-drift --self-test
```

This flag permits execution but does not update the review record.
If behavior changes intentionally, update the contracts, properties, and scratch mutations together.
Require the original and restored baseline runs to agree.
Require each scratch mutation to fail its intended property, rather than compilation or a fingerprint comparison.

After a reviewed code commit, update the six hashes in `coverage.json` and its `sync_commit`.
Update the synchronization commit in this skill to the same commit.
Advance those records only after confirming that the contracts describe the code at that commit.
If covered source remains uncommitted, retain the previous synchronization commit and report the drift.
Run the default command again after updating the record.
Use `/maintain-formal-verification-skill` for a broader synchronization review after merges or unchecked changes.
