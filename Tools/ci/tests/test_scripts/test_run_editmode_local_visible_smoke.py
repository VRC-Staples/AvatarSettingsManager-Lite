#!/usr/bin/env python3
"""Static regression tests for the local visible smoke runner wiring."""

from __future__ import annotations

import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
RUNNER = REPO_ROOT / "Tools/ci/bin/run-editmode-local.sh"


class RunEditModeLocalVisibleSmokeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.script = RUNNER.read_text(encoding="utf-8")
        start = cls.script.index("run_local_visible_smoke_mode() {")
        end = cls.script.index("\nrun_local_batch_suite_mode() {", start)
        cls.visible_smoke_function = cls.script[start:end]

    def test_visible_smoke_does_not_create_legacy_external_overlay_transport(self) -> None:
        self.assertNotIn("initialize_visible_overlay_paths", self.script)
        self.assertNotIn("-asmliteVisibleAutomationExternalOverlayStatePath", self.visible_smoke_function)
        self.assertNotIn("-asmliteVisibleAutomationExternalOverlayAckPath", self.visible_smoke_function)

    def test_visible_smoke_does_not_promote_legacy_launch_unity_selector(self) -> None:
        self.assertNotIn('visible_mode="launch-unity"', self.visible_smoke_function)
        self.assertNotIn("*launch-unity*", self.visible_smoke_function)
        self.assertNotIn("*launchunity*", self.visible_smoke_function)


if __name__ == "__main__":
    unittest.main()
