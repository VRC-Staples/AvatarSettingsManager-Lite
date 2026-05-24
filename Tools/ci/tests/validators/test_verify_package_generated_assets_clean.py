#!/usr/bin/env python3
"""Tests for package generated asset cleanliness verification."""

from __future__ import annotations

import subprocess
import tempfile
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
VERIFIER = REPO_ROOT / "Tools/ci/validators/verify-package-generated-assets-clean.py"
PROTECTED_GENERATED_ASSET = Path(
    "Packages/com.staples.asm-lite/GeneratedAssets/example.asset"
)
PROTECTED_GENERATED_ASSETS_META = Path("Packages/com.staples.asm-lite/GeneratedAssets.meta")
PROTECTED_PREFAB = Path("Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab")
PROTECTED_PREFAB_META = Path("Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab.meta")
RECOVERY_COMMANDS = (
    "git restore -- Packages/com.staples.asm-lite/GeneratedAssets "
    "Packages/com.staples.asm-lite/GeneratedAssets.meta "
    "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab "
    "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab.meta\n"
    "python3 /opt/data/skills/software-development/testing/scripts/"
    "asm-lite-refresh-unity-test-project.py"
)


class VerifyPackageGeneratedAssetsCleanTests(unittest.TestCase):
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
        self.write_file(PROTECTED_GENERATED_ASSET, "asset-v1\n")
        self.write_file(PROTECTED_GENERATED_ASSETS_META, "fileFormatVersion: 2\n")
        self.write_file(PROTECTED_PREFAB, "prefab-v1\n")
        self.write_file(PROTECTED_PREFAB_META, "fileFormatVersion: 2\n")
        subprocess.run(["git", "add", "."], cwd=self.repo, check=True)
        subprocess.run(
            ["git", "commit", "-m", "seed protected outputs"],
            cwd=self.repo,
            check=True,
            capture_output=True,
        )

    def write_file(self, relative_path: Path, content: str) -> None:
        path = self.repo / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")

    def run_verifier(self) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            ["python3", str(VERIFIER)],
            cwd=self.repo,
            capture_output=True,
            text=True,
            check=False,
        )

    def test_exits_zero_when_protected_outputs_are_clean(self) -> None:
        result = self.run_verifier()

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        self.assertIn("package-generated-assets-clean-ok", result.stdout)

    def test_reports_dirty_paths_and_recovery_commands_when_protected_outputs_change(self) -> None:
        self.write_file(PROTECTED_GENERATED_ASSET, "asset-v2\n")
        self.write_file(PROTECTED_PREFAB, "prefab-v2\n")

        result = self.run_verifier()
        output = result.stdout + result.stderr

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Packages/com.staples.asm-lite/GeneratedAssets/example.asset", output)
        self.assertIn("Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab", output)
        self.assertIn(RECOVERY_COMMANDS, output)

    def test_reports_dirty_paths_and_recovery_commands_when_protected_meta_files_change(self) -> None:
        self.write_file(PROTECTED_GENERATED_ASSETS_META, "fileFormatVersion: 2\ndirty: true\n")
        self.write_file(PROTECTED_PREFAB_META, "fileFormatVersion: 2\ndirty: true\n")

        result = self.run_verifier()
        output = result.stdout + result.stderr

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Packages/com.staples.asm-lite/GeneratedAssets.meta", output)
        self.assertIn("Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab.meta", output)
        self.assertIn(RECOVERY_COMMANDS, output)

    def test_ignores_dirty_paths_outside_package_generated_outputs(self) -> None:
        self.write_file(Path("README.md"), "unrelated\n")

        result = self.run_verifier()

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)


if __name__ == "__main__":
    unittest.main()
