#!/usr/bin/env python3
"""Tests for CI asset hygiene verification."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
VERIFIER = REPO_ROOT / "Tools/ci/validators/verify-ci-asset-hygiene.py"


class VerifyCiAssetHygieneTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name)
        subprocess.run(["git", "init"], cwd=self.repo, check=True, capture_output=True)
        subprocess.run(
            ["git", "config", "user.email", "tests@example.invalid"],
            cwd=self.repo,
            check=True,
        )
        subprocess.run(
            ["git", "config", "user.name", "Tests"], cwd=self.repo, check=True
        )

    def write_file(self, relative_path: str, content: str = "test\n") -> None:
        path = self.repo / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def commit_all(self) -> None:
        subprocess.run(["git", "add", "."], cwd=self.repo, check=True)
        subprocess.run(
            ["git", "commit", "-m", "seed files"],
            cwd=self.repo,
            check=True,
            capture_output=True,
        )

    def run_verifier(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["python3", str(VERIFIER)],
            cwd=self.repo,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_accepts_only_ci_assets_gitkeep(self) -> None:
        self.write_file("Tools/ci/unity-project/Assets/.gitkeep", "")
        self.write_file("Tools/ci/unity-project/ProjectSettings/ProjectVersion.txt")
        self.commit_all()

        result = self.run_verifier()

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        self.assertIn("ci-asset-hygiene-ok", result.stdout)

    def test_rejects_tracked_local_test_project_payload(self) -> None:
        self.write_file("Test Project/TestUnityProject/Assets/BunnyEars.prefab")
        self.commit_all()

        result = self.run_verifier()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Test Project/TestUnityProject/Assets/BunnyEars.prefab", result.stderr)
        self.assertIn("local TestUnityProject payload", result.stderr)

    def test_rejects_tracked_ci_assets_payload_except_gitkeep(self) -> None:
        self.write_file("Tools/ci/unity-project/Assets/.gitkeep", "")
        self.write_file("Tools/ci/unity-project/Assets/Editor/runtime-copy.dll")
        self.commit_all()

        result = self.run_verifier()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Tools/ci/unity-project/Assets/Editor/runtime-copy.dll", result.stderr)
        self.assertIn("runtime-generated except .gitkeep", result.stderr)

    def test_rejects_tracked_ci_generated_outputs(self) -> None:
        self.write_file("Tools/ci/unity-project/bin/Debug/ASMLite.Editor.dll")
        self.write_file("Tools/ci/unity-project/obj/project.assets.json")
        self.write_file("Tools/ci/unity-project/ASMLite.Editor.csproj")
        self.commit_all()

        result = self.run_verifier()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Tools/ci/unity-project/bin/Debug/ASMLite.Editor.dll", result.stderr)
        self.assertIn("Tools/ci/unity-project/obj/project.assets.json", result.stderr)
        self.assertIn("Tools/ci/unity-project/ASMLite.Editor.csproj", result.stderr)


if __name__ == "__main__":
    unittest.main()
