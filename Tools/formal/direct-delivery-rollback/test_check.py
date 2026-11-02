#!/usr/bin/env python3
"""Regression checks for receipt preservation and checker provenance."""

from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


SPEC = importlib.util.spec_from_file_location("rollback_check", Path(__file__).with_name("check.py"))
CHECK = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(CHECK)


class ReceiptTests(unittest.TestCase):
    def run_checker(self, evidence, result):
        with patch.object(CHECK, "EVIDENCE", evidence), \
                patch.object(CHECK.shutil, "which", return_value="dotnet"), \
                patch.object(CHECK, "execute", return_value=subprocess.CompletedProcess([], 0, "8.0.426\n", "")), \
                patch.object(CHECK, "explore", return_value=result,
                             side_effect=result if isinstance(result, Exception) else None), \
                patch.object(sys, "argv", ["check.py"]), redirect_stdout(io.StringIO()):
            self.assertEqual(CHECK.main(), 0)

    def test_rerun_preserves_legacy_and_each_new_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory)
            legacy = b'{"baseline":{"status":"PASS"},"mutations":[{"old":"evidence"}]}\n'
            (evidence / "latest-result.json").write_bytes(legacy)
            self.run_checker(evidence, {"status": "PASS", "terminalTraces": 1})
            first = (evidence / "latest-result.json").read_bytes()
            self.run_checker(evidence, {"status": "PASS", "terminalTraces": 2})
            second = (evidence / "latest-result.json").read_bytes()
            preserved = [path.read_bytes() for path in (evidence / "runs").glob("*.json")]
            self.assertIn(legacy, preserved, "The first rerun discarded the existing mutation evidence.")
            self.assertIn(first, preserved, "The next rerun discarded its predecessor.")
            self.assertIn(second, preserved, "The latest run has no immutable receipt.")
            self.assertNotEqual(json.loads(first)["run_id"], json.loads(second)["run_id"])

    def test_lf_checkout_has_no_source_drift(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            kit = root / CHECK.KIT.relative_to(CHECK.ROOT)
            kit.mkdir(parents=True)
            for path in CHECK.KIT.iterdir():
                if path.is_file():
                    shutil.copyfile(path, kit / path.name)
            coverage = json.loads((kit / "coverage.json").read_text(encoding="utf-8"))
            for name in coverage["source_sha256"]:
                target = root / name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((CHECK.ROOT / name).read_bytes().replace(b"\r\n", b"\n"))
            source = root / CHECK.SOURCE.relative_to(CHECK.ROOT)
            with patch.object(CHECK, "ROOT", root), patch.object(CHECK, "KIT", kit), patch.object(CHECK, "SOURCE", source):
                self.run_checker(root / ".planning/formal-asset-transaction", {"status": "PASS"})

    def test_checker_error_preserves_a_failure_receipt(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory)
            with self.assertRaisesRegex(CHECK.CheckError, "compilation failed"):
                self.run_checker(evidence, CHECK.CheckError("compilation failed"))
            receipt = json.loads((evidence / "latest-result.json").read_text(encoding="utf-8"))
            self.assertEqual(receipt["exit_code"], 2)
            self.assertEqual(receipt["error"], "compilation failed")
            self.assertIsNone(receipt["baseline"])
            self.assertEqual(len(list((evidence / "runs").glob("*.json"))), 1)

    def test_receipt_identifies_the_checker_files_that_ran(self):
        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory)
            self.run_checker(evidence, {"status": "PASS"})
            receipt = json.loads((evidence / "latest-result.json").read_text(encoding="utf-8"))
            hashes = receipt.get("checker_sha256", {})
            for name in ("Program.cs", "Environment.cs", "check.py", "Checker.csproj", "global.json", "NuGet.Config", "coverage.json"):
                path = CHECK.KIT / name
                self.assertEqual(hashes.get(path.relative_to(CHECK.ROOT).as_posix()),
                                 hashlib.sha256(path.read_bytes()).hexdigest(),
                                 f"The receipt does not identify {name}.")


if __name__ == "__main__":
    unittest.main()
