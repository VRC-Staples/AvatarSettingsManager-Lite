#!/usr/bin/env python3
"""Explore actual C# transaction control flow under finite dependency contracts."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
from uuid import uuid4

KIT = Path(__file__).resolve().parent
ROOT = KIT.parents[2]
SOURCE = ROOT / "Packages/com.staples.asm-lite/Editor/ASMLiteDirectDeliveryTransaction.cs"
EVIDENCE = ROOT / ".planning/formal-asset-transaction"
SDK = "8.0.426"
CHECKER_FILES = (
    "Checker.csproj", "global.json", "NuGet.Config", "Environment.cs", "Program.cs",
    "check.py", "coverage.json", "test_check.py",
)

MUTATIONS = (
    (
        "skip-failure-restore",
        "if (!completed && !snapshot.TryRestore(avatar, out string context, out string remediation))",
        "if (!completed && snapshot == null && !snapshot.TryRestore(avatar, out string context, out string remediation))",
        "failure-restores",
    ),
    (
        "accept-failed-verification",
        "|| !ASMLiteLifecycleVerification.VerifyDirectDeliveryState(",
        "|| ASMLiteLifecycleVerification.VerifyDirectDeliveryState(",
        "success-requires-verification",
    ),
    (
        "suppress-rollback-error",
        'throw new InvalidOperationException("Direct-delivery rollback failed at \'" + context + "\': " + remediation);',
        "System.GC.KeepAlive(context + remediation);",
        "rollback-error-escapes",
    ),
    (
        "omit-snapshot-disposal",
        "using (var snapshot = ASMLiteDirectDeliveryRollbackSnapshot.Capture(avatar))",
        "var snapshot = ASMLiteDirectDeliveryRollbackSnapshot.Capture(avatar);",
        "snapshot-disposed",
    ),
)


class CheckError(Exception):
    pass


def execute(command, cwd, timeout=90):
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1")
    return subprocess.run(command, cwd=cwd, env=env, capture_output=True, text=True, timeout=timeout)


def explore(dotnet, work, source):
    (work / SOURCE.name).write_bytes(source)
    build = execute([dotnet, "build", "Checker.csproj", "--configuration", "Release", "--nologo", "--verbosity", "quiet"], work)
    if build.returncode != 0:
        raise CheckError("C# compilation failed. This is not a counterexample.\n" + build.stdout + build.stderr)
    run = execute([dotnet, str(work / "bin/Release/net8.0/Checker.dll")], work)
    try:
        result = json.loads(run.stdout.strip())
    except json.JSONDecodeError as error:
        raise CheckError("Checker did not produce a result.\n" + run.stdout + run.stderr) from error
    if run.returncode not in (0, 1) or result.get("status") not in ("PASS", "COUNTEREXAMPLE"):
        raise CheckError("Checker exceeded its scope or failed.\n" + run.stdout + run.stderr)
    if (run.returncode == 0) != (result["status"] == "PASS"):
        raise CheckError("Checker status and exit code disagree.")
    return result


def save_receipt(receipt):
    runs = EVIDENCE / "runs"
    runs.mkdir(parents=True, exist_ok=True)
    latest = EVIDENCE / "latest-result.json"
    payload = (json.dumps(receipt, indent=2) + "\n").encode("utf-8")
    previous = [latest.read_bytes()] if latest.exists() else []
    for data in [*previous, payload]:
        path = runs / (hashlib.sha256(data).hexdigest() + ".json")
        try:
            with path.open("xb") as stream:
                stream.write(data)
        except FileExistsError:
            if path.read_bytes() != data:
                raise CheckError("An immutable receipt does not match its content hash: " + str(path))
    with tempfile.NamedTemporaryFile(dir=EVIDENCE, delete=False) as stream:
        temporary = Path(stream.name)
        stream.write(payload)
    try:
        os.replace(temporary, latest)
    finally:
        temporary.unlink(missing_ok=True)
    print("Receipt: " + str(path))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--self-test", action="store_true", help="Also reject four scratch mutations of the real source.")
    parser.add_argument("--allow-source-drift", action="store_true", help="Run after source changes without claiming the recorded source review is current.")
    args = parser.parse_args()
    if sys.version_info < (3, 11):
        raise CheckError("Python 3.11 or later is required.")
    dotnet = shutil.which("dotnet")
    if not dotnet:
        raise CheckError("Install .NET SDK 8.0.426. No Unity launch is required.")
    version = execute([dotnet, "--version"], KIT)
    if version.returncode != 0 or version.stdout.strip() != SDK:
        raise CheckError(".NET SDK 8.0.426 is required.\n" + version.stdout + version.stderr)
    checker_bytes = {name: (KIT / name).read_bytes() for name in CHECKER_FILES}
    coverage = json.loads(checker_bytes["coverage.json"])
    if coverage.get("source_hash_mode") != "crlf-to-lf":
        raise CheckError("The coverage record must use crlf-to-lf review hashes.")
    source_bytes = {path: (ROOT / path).read_bytes() for path in coverage["source_sha256"]}
    actual = {path: hashlib.sha256(data).hexdigest() for path, data in source_bytes.items()}
    review_hashes = {path: hashlib.sha256(data.replace(b"\r\n", b"\n")).hexdigest()
                     for path, data in source_bytes.items()}
    drift = [path for path, digest in review_hashes.items() if digest != coverage["source_sha256"][path]]
    source = source_bytes[SOURCE.relative_to(ROOT).as_posix()]
    EVIDENCE.mkdir(parents=True, exist_ok=True)
    receipt = {
        "run_id": uuid4().hex,
        "sdk": SDK,
        "sync_commit": coverage["sync_commit"],
        "source_sha256": actual,
        "source_review_sha256": review_hashes,
        "source_hash_mode": coverage["source_hash_mode"],
        "checker_sha256": {(KIT / name).relative_to(ROOT).as_posix(): hashlib.sha256(data).hexdigest()
                           for name, data in checker_bytes.items()},
        "source_drift": drift,
        "self_test": args.self_test,
        "allow_source_drift": args.allow_source_drift,
        "exit_code": 2,
        "baseline": None,
        "mutations": [],
    }
    try:
        with tempfile.TemporaryDirectory(prefix="checker-", dir=EVIDENCE) as directory:
            work = Path(directory)
            for name in ("Checker.csproj", "global.json", "NuGet.Config", "Environment.cs", "Program.cs"):
                (work / name).write_bytes(checker_bytes[name])
            receipt["baseline"] = explore(dotnet, work, source)
            print(json.dumps(receipt["baseline"]))
            if receipt["baseline"]["status"] != "PASS":
                receipt["exit_code"] = 1
                return 1
            if args.self_test:
                text = source.decode("utf-8")
                for name, before, after, property_name in MUTATIONS:
                    if text.count(before) != 1:
                        raise CheckError(f"Mutation {name} no longer has one source match. Review the mutation and coverage.")
                    mutant = text.replace(before, after, 1).encode("utf-8")
                    result = explore(dotnet, work, mutant)
                    if result.get("status") != "COUNTEREXAMPLE" or result.get("property") != property_name:
                        raise CheckError(f"Mutation {name} did not fail its intended property: {json.dumps(result)}")
                    receipt["mutations"].append({"mutation": name, "counterexample": result})
                    print(json.dumps({"mutation": name, **result}))
                receipt["restored_baseline"] = explore(dotnet, work, source)
                if receipt["restored_baseline"] != receipt["baseline"]:
                    raise CheckError("The restored baseline differs from its first run.")
                print("PASS: all four source mutations were rejected; the original source passed again.")
        if (any((ROOT / path).read_bytes() != data for path, data in source_bytes.items())
                or any((KIT / name).read_bytes() != data for name, data in checker_bytes.items())):
            raise CheckError("The source or checker changed during the check. Rerun after resolving concurrent edits.")
        if drift:
            print("SOURCE DRIFT: review these files against the dependency contracts: " + ", ".join(drift))
            if not args.allow_source_drift:
                return 2
            print("The bounded run passed, but the recorded source review is not current.")
        receipt["exit_code"] = 0
        return 0
    except (CheckError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        receipt["error"] = str(error)
        raise
    finally:
        save_receipt(receipt)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (CheckError, OSError, ValueError, subprocess.TimeoutExpired) as error:
        print(str(error), file=sys.stderr)
        raise SystemExit(2)
