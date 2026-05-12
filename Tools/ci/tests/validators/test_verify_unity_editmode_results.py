#!/usr/bin/env python3
"""Tests for Unity EditMode result verification."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
VERIFIER = REPO_ROOT / "Tools/ci/validators/verify-unity-editmode-results.py"


PASSING_XML = """<?xml version=\"1.0\" encoding=\"utf-8\"?>
<test-run result=\"Passed\" total=\"3\" passed=\"3\" failed=\"0\" skipped=\"0\">
  <test-suite type=\"Assembly\" result=\"Passed\" total=\"3\" passed=\"3\" failed=\"0\" />
</test-run>
"""


FAILING_XML = """<?xml version=\"1.0\" encoding=\"utf-8\"?>
<test-run result=\"Failed\" total=\"3\" passed=\"2\" failed=\"1\" skipped=\"0\">
  <test-case fullname=\"Example.FailingTest\" result=\"Failed\" />
</test-run>
"""


TEARDOWN_LOG = """
Passed all tests
Assertion failed on expression: 'm_ErrorCode == MDB_MAP_FULL || !HasAbortingErrors()'
Receiving unhandled NULL exception
"""


class VerifyUnityEditmodeResultsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.results = self.root / "editmode-results.xml"
        self.log = self.root / "editmode.log"
        self.log.write_text("Unity completed normally\n", encoding="utf-8")

    def write_results(self, content: str) -> None:
        self.results.write_text(content, encoding="utf-8")

    def run_verifier(self, exit_code: int) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                "python3",
                str(VERIFIER),
                "--unity-exit-code",
                str(exit_code),
                "--results",
                str(self.results),
                "--log",
                str(self.log),
            ],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_accepts_zero_exit_with_passing_nunit_xml(self) -> None:
        self.write_results(PASSING_XML)

        result = self.run_verifier(0)

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        self.assertIn("unity-editmode-results-ok", result.stdout)

    def test_fails_zero_exit_when_nunit_xml_is_missing(self) -> None:
        result = self.run_verifier(0)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Missing NUnit XML", result.stderr)

    def test_fails_zero_exit_when_nunit_xml_is_malformed(self) -> None:
        self.write_results("<test-run")

        result = self.run_verifier(0)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Malformed NUnit XML", result.stderr)

    def test_fails_when_nunit_xml_reports_failed_tests(self) -> None:
        self.write_results(FAILING_XML)

        result = self.run_verifier(0)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("NUnit XML reports failed tests", result.stderr)
        self.assertIn("Example.FailingTest", result.stderr)

    def test_converts_unity_teardown_nonzero_to_warning_when_nunit_xml_passed(self) -> None:
        self.write_results(PASSING_XML)
        self.log.write_text(TEARDOWN_LOG, encoding="utf-8")

        result = self.run_verifier(133)

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        self.assertIn("unity-teardown-warning", result.stdout)
        self.assertIn("Unity exited 133", result.stdout)

    def test_fails_nonzero_exit_when_log_does_not_match_teardown_warning(self) -> None:
        self.write_results(PASSING_XML)
        self.log.write_text("Aborting batchmode due to fatal product failure\n", encoding="utf-8")

        result = self.run_verifier(1)

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Unity exited 1", result.stderr)


if __name__ == "__main__":
    unittest.main()
