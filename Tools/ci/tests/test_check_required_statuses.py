#!/usr/bin/env python3
"""Tests for Tools/ci/check-required-statuses.py."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
CHECKER = REPO_ROOT / "Tools/ci/check-required-statuses.py"
REQUIRED_CHECKS = REPO_ROOT / "Tools/ci/release-required-checks.json"


def load_checker_module():
    spec = importlib.util.spec_from_file_location("check_required_statuses_test_module", CHECKER)
    if spec is None or spec.loader is None:
        raise RuntimeError("Unable to load required-check status helper module")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RequiredCheckStatusesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.module = load_checker_module()
        self.required = {"compile": ["A", "B"], "lint": ["L"], "test": ["T"]}

    def test_prefers_success_alias_when_earlier_alias_pending(self):
        observed = {"A": "pending", "B": "success", "L": "success", "T": "success"}
        pending, blocking = self.module.classify(self.required, observed)
        self.assertEqual(pending, [])
        self.assertEqual(blocking, [])

    def test_blocks_when_no_success_alias_and_terminal_failure_present(self):
        observed = {"A": "pending", "B": "failure", "L": "success", "T": "success"}
        pending, blocking = self.module.classify(self.required, observed)
        self.assertEqual(pending, [])
        self.assertTrue(blocking)
        self.assertIn("required check B is failure, expected success", blocking)

    def test_pending_only_aliases_remain_pending(self):
        observed = {"A": "pending", "B": "in_progress", "L": "success", "T": "success"}
        pending, blocking = self.module.classify(self.required, observed)
        self.assertTrue(pending)
        self.assertIn("required check A is pending", pending)
        self.assertEqual(blocking, [])

    def test_missing_aliases_remain_pending(self):
        observed = {"L": "success", "T": "success"}
        pending, blocking = self.module.classify(self.required, observed)
        self.assertTrue(pending)
        self.assertIn(
            "waiting for required check for compile: expected one of ['A', 'B']",
            pending,
        )
        self.assertEqual(blocking, [])

    def test_validate_config_only_accepts_release_required_checks(self):
        proc = subprocess.run(
            [
                "python3",
                str(CHECKER),
                "--required-checks",
                str(REQUIRED_CHECKS),
                "--validate-config-only",
            ],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(proc.returncode, 0, msg=proc.stderr or proc.stdout)
        self.assertIn("Required-check configuration is valid", proc.stdout)


if __name__ == "__main__":
    unittest.main()
