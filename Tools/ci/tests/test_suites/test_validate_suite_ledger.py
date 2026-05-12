#!/usr/bin/env python3
"""Tests for Tools/ci/test-suites/validate-suite-ledger.py."""

from __future__ import annotations

import json
import subprocess
import tempfile
import textwrap
import unittest
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[4]
VALIDATOR = REPO_ROOT / "Tools/ci/test-suites/validate-suite-ledger.py"


class SuiteLedgerValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.tests_dir = self.root / "Packages/com.staples.asm-lite/Tests/Editor"
        self.tests_dir.mkdir(parents=True)
        self.ledger = self.root / "Tools/ci/test-suites/test-suite-ledger.json"

    def run_validator(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [
                "python3",
                str(VALIDATOR),
                "--repo-root",
                str(self.root),
                "--ledger",
                str(self.ledger),
                *args,
            ],
            cwd=REPO_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )

    def write_test_file(self, relative_path: str, source: str) -> None:
        path = self.root / relative_path
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(textwrap.dedent(source), encoding="utf-8")

    def test_update_writes_sorted_identity_rows_without_inventing_human_classification(self) -> None:
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/Editor/ZetaTests.cs",
            """
            using NUnit.Framework;

            public sealed class ZetaTests
            {
                [Test]
                public void AlphaCreatesGeneratedMenuContract()
                {
                }

                [Test]
                [Category("Manual")]
                public void ManualAvatarFixtureRequiresOperator()
                {
                }
            }
            """,
        )
        result = self.run_validator("--update")

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        ledger = json.loads(self.ledger.read_text(encoding="utf-8"))
        self.assertEqual(ledger["schemaVersion"], 1)
        self.assertEqual(ledger["scope"]["include"], ["Packages/com.staples.asm-lite/Tests/**/*.cs"])
        self.assertNotIn("exclude", ledger["scope"])
        identities = [(row["file"], row["class"], row["method"]) for row in ledger["tests"]]
        self.assertEqual(identities, sorted(identities))
        self.assertEqual([row["method"] for row in ledger["tests"]], [
            "AlphaCreatesGeneratedMenuContract",
            "ManualAvatarFixtureRequiresOperator",
        ])
        generated = ledger["tests"][0]
        self.assertEqual(generated["lane"], "needs-classification")
        self.assertEqual(generated["headlessViability"], "review")
        self.assertEqual(generated["honesty"], "needs-review")
        self.assertEqual(generated["recommendation"], "review")
        manual = ledger["tests"][1]
        self.assertEqual(manual["lane"], "manual")
        self.assertEqual(manual["headlessViability"], "no")
        self.assertEqual(manual["recommendation"], "manual-review")

    def test_check_fails_when_placeholders_remain(self) -> None:
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/Editor/ExampleTests.cs",
            """
            using NUnit.Framework;
            public sealed class ExampleTests
            {
                [Test]
                public void RequiresReview() {}
            }
            """,
        )
        self.assertEqual(self.run_validator("--update").returncode, 0)

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("placeholder classification", result.stderr)
        self.assertIn("RequiresReview", result.stderr)

    def test_update_replaces_placeholder_classification_with_category_defaults(self) -> None:
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/Editor/CategoryDefaultTests.cs",
            """
            using NUnit.Framework;

            [TestFixture]
            [Category("Headless")]
            public sealed class CategoryDefaultTests
            {
                [Test]
                public void HeadlessContract() {}

                [Test]
                [Category("Integration")]
                public void MutatesUnityAssets() {}
            }
            """,
        )
        self.ledger.parent.mkdir(parents=True)
        self.ledger.write_text(
            json.dumps({
                "schemaVersion": 1,
                "scope": {},
                "tests": [
                    {
                        "file": "Packages/com.staples.asm-lite/Tests/Editor/CategoryDefaultTests.cs",
                        "class": "CategoryDefaultTests",
                        "method": "HeadlessContract",
                        "line": 9,
                        "attributes": ["TestFixture", 'Category("Headless")', "Test"],
                        "categories": ["Headless"],
                        "lane": "needs-classification",
                        "headlessViability": "review",
                        "honesty": "needs-review",
                        "recommendation": "review",
                        "publicBehaviorClaim": "Headless Contract",
                        "fixtureDependencies": [],
                        "assetSceneMutations": "review",
                        "externalProcessFilesystemEnvUsage": "review",
                        "runnerFilter": "CategoryDefaultTests.HeadlessContract",
                        "notes": "",
                    },
                    {
                        "file": "Packages/com.staples.asm-lite/Tests/Editor/CategoryDefaultTests.cs",
                        "class": "CategoryDefaultTests",
                        "method": "MutatesUnityAssets",
                        "line": 13,
                        "attributes": ["TestFixture", 'Category("Headless")', "Test", 'Category("Integration")'],
                        "categories": ["Headless", "Integration"],
                        "lane": "needs-classification",
                        "headlessViability": "review",
                        "honesty": "needs-review",
                        "recommendation": "review",
                        "publicBehaviorClaim": "Mutates Unity Assets",
                        "fixtureDependencies": [],
                        "assetSceneMutations": "review",
                        "externalProcessFilesystemEnvUsage": "review",
                        "runnerFilter": "CategoryDefaultTests.MutatesUnityAssets",
                        "notes": "",
                    },
                ],
            }, indent=2) + "\n",
            encoding="utf-8",
        )

        result = self.run_validator("--update")

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        ledger = json.loads(self.ledger.read_text(encoding="utf-8"))
        headless, integration = ledger["tests"]
        self.assertEqual(headless["lane"], "core-headless")
        self.assertEqual(headless["headlessViability"], "yes")
        self.assertEqual(headless["recommendation"], "default-ci")
        self.assertEqual(integration["lane"], "integration-headless")
        self.assertEqual(integration["headlessViability"], "review")
        self.assertEqual(integration["recommendation"], "default-ci-review")

    def test_check_detects_inventory_drift_without_update(self) -> None:
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/Editor/ExampleTests.cs",
            """
            using NUnit.Framework;
            public sealed class ExampleTests
            {
                [Test]
                public void AddedAfterLedger() {}
            }
            """,
        )
        self.ledger.parent.mkdir(parents=True)
        self.ledger.write_text(
            json.dumps({"schemaVersion": 1, "scope": {}, "tests": []}, indent=2) + "\n",
            encoding="utf-8",
        )

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("ledger drift", result.stderr)
        self.assertIn("AddedAfterLedger", result.stderr)
        self.assertIn("--update", result.stderr)


if __name__ == "__main__":
    unittest.main()
