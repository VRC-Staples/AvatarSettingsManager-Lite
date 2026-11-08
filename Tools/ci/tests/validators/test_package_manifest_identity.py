#!/usr/bin/env python3
"""Protect the package's stable ID and installed display name."""

import json
from pathlib import Path
import unittest


REPO_ROOT = Path(__file__).resolve().parents[4]
MANIFEST = REPO_ROOT / "Packages/com.staples.asm-lite/package.json"


class PackageManifestIdentityTests(unittest.TestCase):
    def test_package_id_stays_stable_and_display_name_includes_brand(self):
        manifest = json.loads(MANIFEST.read_text(encoding="utf-8-sig"))

        self.assertEqual(manifest["name"], "com.staples.asm-lite")
        self.assertEqual(
            manifest["displayName"], ".Staples. - Avatar Settings Manager - Lite"
        )


if __name__ == "__main__":
    unittest.main()
