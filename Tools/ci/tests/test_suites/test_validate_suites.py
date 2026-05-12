#!/usr/bin/env python3
"""Tests for Tools/ci/test-suites/validate-suites.py."""

from __future__ import annotations

import json
import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
VALIDATOR = REPO_ROOT / "Tools/ci/test-suites/validate-suites.py"


SMOKE_PROTOCOL_FILES = [
    "ASMLiteSmokeProtocolTests.cs",
    "ASMLiteSmokeProtocolCompatibilityTests.cs",
    "ASMLiteSmokeCatalogTests.cs",
    "ASMLiteSmokeRunExecutorTests.cs",
    "ASMLiteSmokeAtomicIoTests.cs",
    "ASMLiteSmokeArtifactPathsTests.cs",
]

SMOKE_OVERLAY_HOST_FILES = [
    "ASMLiteSmokeOverlayHostTests.cs",
    "ASMLiteSmokeSetupFixtureServiceTests.cs",
]


def batch_runs() -> dict[str, object]:
    return {
        "runs": [
            {
                "name": "editmode-contract",
                "resultFile": "editmode-contract-results.xml",
                "filters": [{"testNames": ["ASMLite.Tests.Editor.ContractTests.ContractMethod"]}],
            },
            {
                "name": "editmode-core",
                "resultFile": "editmode-core-results.xml",
                "filters": [{"groupNames": ["^ASMLite\\.Tests\\.Editor\\.CoreTests(?:\\.|$)"]}],
            },
            {
                "name": "editmode-integration",
                "resultFile": "editmode-integration-results.xml",
                "categoryNames": ["Integration"],
            },
            {
                "name": "editmode-smoke-protocol",
                "resultFile": "editmode-smoke-protocol-results.xml",
                "filters": [{"groupNames": class_selectors(SMOKE_PROTOCOL_FILES)}],
            },
            {
                "name": "editmode-smoke-overlay-host",
                "resultFile": "editmode-smoke-overlay-host-results.xml",
                "filters": [{"groupNames": class_selectors(SMOKE_OVERLAY_HOST_FILES)}],
            },
        ]
    }


def class_selectors(file_names: list[str]) -> list[str]:
    return [f"^ASMLite\\.Tests\\.Editor\\.{Path(file_name).stem}(?:\\.|$)" for file_name in file_names]


def suites_document() -> dict[str, object]:
    runs_by_name = {run["name"]: run for run in batch_runs()["runs"]}  # type: ignore[index]
    return {
        "schemaVersion": 1,
        "defaultCiGroups": [
            "contract",
            "core-headless",
            "integration-headless",
            "smoke-protocol-headless",
            "smoke-overlay-host-headless",
        ],
        "groups": [
            {
                "id": "contract",
                "defaultCi": True,
                "headless": "yes",
                "batchRun": runs_by_name["editmode-contract"],
            },
            {
                "id": "core-headless",
                "defaultCi": True,
                "headless": "yes",
                "batchRun": runs_by_name["editmode-core"],
            },
            {
                "id": "integration-headless",
                "defaultCi": True,
                "headless": "review",
                "batchRun": runs_by_name["editmode-integration"],
            },
            {
                "id": "smoke-protocol-headless",
                "defaultCi": True,
                "headless": "yes",
                "testFiles": SMOKE_PROTOCOL_FILES,
                "batchRun": runs_by_name["editmode-smoke-protocol"],
            },
            {
                "id": "smoke-overlay-host-headless",
                "defaultCi": True,
                "headless": "yes",
                "testFiles": SMOKE_OVERLAY_HOST_FILES,
                "batchRun": runs_by_name["editmode-smoke-overlay-host"],
            },
            {
                "id": "playmode-headless-review",
                "defaultCi": False,
                "headless": "review",
                "testFiles": ["ASMLiteAv3SaveLoadPlayModeTests.cs"],
                "smokeCatalogSuiteIds": ["playmode-runtime-validation"],
            },
            {
                "id": "visible-manual",
                "defaultCi": False,
                "headless": "no",
                "testFiles": ["ASMLiteVisibleEditorSmokeTests.cs"],
                "smokeCatalogPath": "Tools/ci/smoke/suite-catalog.json",
                "smokeCatalogSuiteIds": ["asm-lite-readiness-check", "setup-scene-avatar"],
            },
        ],
    }


class ValidateSuitesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        (self.root / "Tools/ci/test-suites").mkdir(parents=True)
        (self.root / "Tools/ci/smoke").mkdir(parents=True)
        self.suites_path = self.root / "Tools/ci/test-suites/suites.json"
        self.batch_path = self.root / "Tools/ci/test-suites/editmode-batch-runs.json"
        self.catalog_path = self.root / "Tools/ci/smoke/suite-catalog.json"
        self.write_json(self.suites_path, suites_document())
        self.write_json(self.batch_path, batch_runs())
        self.write_json(
            self.catalog_path,
            {
                "catalogVersion": 1,
                "protocolVersion": "1.0.0",
                "groups": [
                    {
                        "groupId": "preflight",
                        "suites": [{"suiteId": "asm-lite-readiness-check", "requiresPlayMode": False}],
                    },
                    {
                        "groupId": "editor-window",
                        "suites": [{"suiteId": "setup-scene-avatar", "requiresPlayMode": False}],
                    },
                    {
                        "groupId": "playmode-runtime",
                        "suites": [{"suiteId": "playmode-runtime-validation", "requiresPlayMode": True}],
                    },
                ],
            },
        )

    def write_json(self, path: Path, document: dict[str, object]) -> None:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")

    def run_validator(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                "python3",
                str(VALIDATOR),
                "--repo-root",
                str(self.root),
                "--suites",
                str(self.suites_path),
                "--editmode-batch-runs",
                str(self.batch_path),
                "--smoke-catalog",
                str(self.catalog_path),
            ],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_accepts_canonical_default_batches_and_excluded_manual_groups(self) -> None:
        result = self.run_validator()

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        self.assertIn("suite map ok", result.stdout)

    def test_fails_when_default_ci_batch_run_drifts_from_editmode_plan(self) -> None:
        document = suites_document()
        document["groups"][1]["batchRun"]["resultFile"] = "wrong.xml"  # type: ignore[index]
        self.write_json(self.suites_path, document)

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("default CI batch run drift", result.stderr)
        self.assertIn("core-headless", result.stderr)

    def test_fails_when_smoke_lane_membership_drops_required_host_file(self) -> None:
        document = suites_document()
        document["groups"][4]["testFiles"] = ["ASMLiteSmokeOverlayHostTests.cs"]  # type: ignore[index]
        self.write_json(self.suites_path, document)

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("smoke-overlay-host-headless testFiles", result.stderr)
        self.assertIn("ASMLiteSmokeSetupFixtureServiceTests.cs", result.stderr)

    def test_fails_when_manual_review_group_is_in_default_ci(self) -> None:
        document = suites_document()
        document["defaultCiGroups"] = [*document["defaultCiGroups"], "visible-manual"]  # type: ignore[index]
        document["groups"][6]["defaultCi"] = True  # type: ignore[index]
        self.write_json(self.suites_path, document)

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("must be excluded from default CI", result.stderr)


if __name__ == "__main__":
    unittest.main()
