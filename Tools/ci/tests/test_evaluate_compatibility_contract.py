#!/usr/bin/env python3
"""Tests for Tools/ci/evaluate-compatibility-contract.py."""

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[3]
EVALUATOR = REPO_ROOT / "Tools/ci/evaluate-compatibility-contract.py"


def load_evaluator_module():
    spec = importlib.util.spec_from_file_location("compat_eval_test_module", EVALUATOR)
    if spec is None or spec.loader is None:
        raise RuntimeError("Unable to load evaluator module")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def write_json(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


class CompatibilityEvaluatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.module = load_evaluator_module()

    def _create_fixture_repo(self) -> Path:
        temp_dir = Path(tempfile.mkdtemp(prefix="compat-eval-test-"))

        project_version_path = temp_dir / "Tools/ci/unity-project/ProjectSettings/ProjectVersion.txt"
        project_version_path.parent.mkdir(parents=True, exist_ok=True)
        project_version_path.write_text("m_EditorVersion: 2022.3.22f1\n", encoding="utf-8")

        package_manifest = {
            "name": "com.staples.asm-lite",
            "unity": "2022.3",
            "vpmDependencies": {
                "com.vrchat.avatars": ">=3.7.0",
                "com.vrcfury.vrcfury": ">=1.999.0",
            },
        }
        write_json(temp_dir / "Packages/com.staples.asm-lite/package.json", package_manifest)

        shadow_manifest = {
            "name": "com.vrchat.avatars",
            "version": "3.10.2",
        }
        write_json(
            temp_dir / "Tools/ci/unity-project/Packages/com.vrchat.avatars/package.json",
            shadow_manifest,
        )

        contract = {
            "schemaVersion": "1.0.0",
            "compatibility": {
                "unity": {
                    "policy": "strict",
                    "version": "2022.3.22f1",
                    "sourceRef": "unityProjectVersion",
                },
                "vrchatSdk": {
                    "policy": "minimum",
                    "minVersion": "3.7.0",
                    "sourceRef": "packageManifest",
                },
                "vrcfury": {
                    "policy": "minimum",
                    "minVersion": "1.999.0",
                    "sourceRef": "packageManifest",
                },
            },
            "sources": {
                "unityProjectVersion": {
                    "path": "Tools/ci/unity-project/ProjectSettings/ProjectVersion.txt",
                    "kind": "unity-project-version",
                },
                "packageManifest": {
                    "path": "Packages/com.staples.asm-lite/package.json",
                    "kind": "package-manifest",
                },
                "shadowVrchatSdk": {
                    "path": "Tools/ci/unity-project/Packages/com.vrchat.avatars/package.json",
                    "kind": "package-manifest",
                },
                "compatibilitySummary": {
                    "path": ".planning/COMPATIBILITY.md",
                    "kind": "generated-summary",
                },
            },
            "observedBaselines": {
                "unityProjectVersion": {
                    "value": "2022.3.22f1",
                    "sourceRef": "unityProjectVersion",
                },
                "packageUnityLine": {
                    "value": "2022.3",
                    "sourceRef": "packageManifest",
                },
                "shadowVrchatSdkVersion": {
                    "value": "3.10.2",
                    "sourceRef": "shadowVrchatSdk",
                },
            },
        }

        contract_path = temp_dir / ".planning/compatibility.contract.json"
        write_json(contract_path, contract)

        summary = self.module.render_compatibility_markdown(
            contract, ".planning/compatibility.contract.json"
        )
        summary_path = temp_dir / ".planning/COMPATIBILITY.md"
        summary_path.parent.mkdir(parents=True, exist_ok=True)
        summary_path.write_text(summary, encoding="utf-8")

        return temp_dir

    def _run_eval(self, fixture_root: Path, mode: str, contract_rel: str = ".planning/compatibility.contract.json"):
        output_path = fixture_root / "compat-report.json"
        cmd = [
            "python3",
            str(EVALUATOR),
            "--mode",
            mode,
            "--contract",
            contract_rel,
            "--output-json",
            str(output_path),
        ]
        proc = subprocess.run(
            cmd,
            cwd=fixture_root,
            capture_output=True,
            text=True,
            check=False,
        )
        report = None
        if output_path.exists():
            report = json.loads(output_path.read_text(encoding="utf-8"))
        return proc, report

    def test_valid_contract_passes_in_both_modes(self):
        fixture = self._create_fixture_repo()

        ci_proc, ci_report = self._run_eval(fixture, "ci")
        release_proc, release_report = self._run_eval(fixture, "release")

        self.assertEqual(ci_proc.returncode, 0)
        self.assertEqual(release_proc.returncode, 0)
        self.assertIsNotNone(ci_report)
        self.assertIsNotNone(release_report)
        self.assertEqual(ci_report["verdict"], "pass")
        self.assertEqual(release_report["verdict"], "pass")
        self.assertEqual(ci_report["reasonCodes"], [])

    def test_missing_contract_is_comp_101_and_fails_all_modes(self):
        fixture = self._create_fixture_repo()

        ci_proc, ci_report = self._run_eval(fixture, "ci", "does/not/exist.json")
        release_proc, release_report = self._run_eval(fixture, "release", "does/not/exist.json")

        self.assertNotEqual(ci_proc.returncode, 0)
        self.assertNotEqual(release_proc.returncode, 0)
        self.assertIn("COMP-101", ci_report["reasonCodes"])
        self.assertIn("COMP-101", release_report["reasonCodes"])

    def test_malformed_contract_is_comp_102_and_fails_all_modes(self):
        fixture = self._create_fixture_repo()
        (fixture / ".planning/compatibility.contract.json").write_text("{not-json", encoding="utf-8")

        ci_proc, ci_report = self._run_eval(fixture, "ci")
        release_proc, release_report = self._run_eval(fixture, "release")

        self.assertNotEqual(ci_proc.returncode, 0)
        self.assertNotEqual(release_proc.returncode, 0)
        self.assertIn("COMP-102", ci_report["reasonCodes"])
        self.assertIn("COMP-102", release_report["reasonCodes"])

    def test_unity_mismatch_is_warn_in_ci_fail_in_release(self):
        fixture = self._create_fixture_repo()
        unity_path = fixture / "Tools/ci/unity-project/ProjectSettings/ProjectVersion.txt"
        unity_path.write_text("m_EditorVersion: 2022.3.25f1\n", encoding="utf-8")

        ci_proc, ci_report = self._run_eval(fixture, "ci")
        release_proc, release_report = self._run_eval(fixture, "release")

        self.assertEqual(ci_proc.returncode, 0)
        self.assertNotEqual(release_proc.returncode, 0)
        self.assertIn("COMP-103", ci_report["reasonCodes"])
        self.assertIn("COMP-103", release_report["reasonCodes"])
        self.assertEqual(ci_report["verdict"], "warn")
        self.assertEqual(release_report["verdict"], "fail")

    def test_vrchat_floor_mismatch_is_comp_104(self):
        fixture = self._create_fixture_repo()
        package_path = fixture / "Packages/com.staples.asm-lite/package.json"
        package_data = json.loads(package_path.read_text(encoding="utf-8"))
        package_data["vpmDependencies"]["com.vrchat.avatars"] = ">=3.8.0"
        write_json(package_path, package_data)

        ci_proc, ci_report = self._run_eval(fixture, "ci")
        release_proc, release_report = self._run_eval(fixture, "release")

        self.assertEqual(ci_proc.returncode, 0)
        self.assertNotEqual(release_proc.returncode, 0)
        self.assertIn("COMP-104", ci_report["reasonCodes"])
        self.assertIn("COMP-104", release_report["reasonCodes"])

    def test_vrcfury_floor_mismatch_is_comp_105(self):
        fixture = self._create_fixture_repo()
        package_path = fixture / "Packages/com.staples.asm-lite/package.json"
        package_data = json.loads(package_path.read_text(encoding="utf-8"))
        package_data["vpmDependencies"]["com.vrcfury.vrcfury"] = ">=2.0.0"
        write_json(package_path, package_data)

        ci_proc, ci_report = self._run_eval(fixture, "ci")
        release_proc, release_report = self._run_eval(fixture, "release")

        self.assertEqual(ci_proc.returncode, 0)
        self.assertNotEqual(release_proc.returncode, 0)
        self.assertIn("COMP-105", ci_report["reasonCodes"])
        self.assertIn("COMP-105", release_report["reasonCodes"])

    def test_stale_summary_is_warn_in_ci_fail_in_release_with_comp_106(self):
        fixture = self._create_fixture_repo()
        summary_path = fixture / ".planning/COMPATIBILITY.md"
        summary_path.write_text(summary_path.read_text(encoding="utf-8") + "\n<!-- stale -->\n", encoding="utf-8")

        ci_proc, ci_report = self._run_eval(fixture, "ci")
        release_proc, release_report = self._run_eval(fixture, "release")

        self.assertEqual(ci_proc.returncode, 0)
        self.assertNotEqual(release_proc.returncode, 0)
        self.assertIn("COMP-106", ci_report["reasonCodes"])
        self.assertIn("COMP-106", release_report["reasonCodes"])


if __name__ == "__main__":
    unittest.main()
