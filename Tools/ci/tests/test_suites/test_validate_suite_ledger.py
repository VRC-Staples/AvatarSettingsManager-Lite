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
        self.audit_doc = self.root / "Tools/ci/docs/asmlite-tests-audit.md"

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

    def write_ledger_rows(self, rows: list[dict[str, object]]) -> None:
        self.ledger.parent.mkdir(parents=True, exist_ok=True)
        self.ledger.write_text(
            json.dumps({
                "schemaVersion": 1,
                "scope": {
                    "description": "Unity C# NUnit/EditMode tests inventoried under the ASM-Lite package tests tree.",
                    "include": ["Packages/com.staples.asm-lite/Tests/**/*.cs"],
                    "sort": ["file", "class", "method"],
                },
                "tests": rows,
            }, indent=2) + "\n",
            encoding="utf-8",
        )

    def write_audit_doc(self, mirror: str) -> None:
        self.audit_doc.parent.mkdir(parents=True, exist_ok=True)
        self.audit_doc.write_text(
            "# Audit\n\n"
            "## Method-level ledger mirror\n\n"
            "This table mirrors the classified ledger fields so the markdown artifact is reviewable without opening JSON.\n\n"
            f"{mirror}"
            "\n## Later section\n\n"
            "Keep this section after the generated mirror.\n",
            encoding="utf-8",
        )

    def mirror_row(self, *, file: str, class_name: str, method: str, line: int) -> dict[str, object]:
        return {
            "file": file,
            "class": class_name,
            "method": method,
            "line": line,
            "attributes": ["Test"],
            "categories": [],
            "lane": "core-headless",
            "headlessViability": "yes",
            "honesty": "unit-contract-coverage",
            "recommendation": "default-ci",
            "publicBehaviorClaim": method,
            "fixtureDependencies": [],
            "assetSceneMutations": "none",
            "externalProcessFilesystemEnvUsage": "none",
            "runnerFilter": f"{class_name}.{method}",
            "notes": "",
        }

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

    def test_check_detects_playmode_asmdef_that_unity_would_list_as_editmode(self) -> None:
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/PlayMode/Runtime/RuntimeDiscoveryTests.cs",
            """
            using System.Collections;
            using NUnit.Framework;
            using UnityEngine.TestTools;

            public sealed class RuntimeDiscoveryTests
            {
                [UnityTest]
                public IEnumerator AppearsInPlayModeRunner()
                {
                    yield break;
                }
            }
            """,
        )
        result = self.run_validator("--update")
        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        ledger = json.loads(self.ledger.read_text(encoding="utf-8"))
        for row in ledger["tests"]:
            row.update({
                "lane": "playmode-headless-review",
                "headlessViability": "conditional-playmode-not-default-ci",
                "honesty": "playmode-runtime-review",
                "recommendation": "separate-playmode-lane",
                "fixtureDependencies": [],
                "assetSceneMutations": "none",
                "externalProcessFilesystemEnvUsage": "Unity PlayMode runner",
                "notes": "",
            })
        self.write_ledger_rows(ledger["tests"])
        asmdef = self.root / "Packages/com.staples.asm-lite/Tests/PlayMode/ASMLite.Tests.PlayMode.asmdef"
        asmdef.parent.mkdir(parents=True, exist_ok=True)
        asmdef.write_text(json.dumps({
            "name": "ASMLite.Tests.PlayMode",
            "references": ["UnityEngine.TestRunner", "UnityEditor.TestRunner"],
            "includePlatforms": ["Editor"],
        }, indent=2) + "\n", encoding="utf-8")

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("includePlatforms=[]", result.stderr)
        self.assertIn("UnityEditor.TestRunner", result.stderr)

    def test_check_detects_playmode_source_using_editmode_fixture_isolation(self) -> None:
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/PlayMode/Runtime/RuntimeDiscoveryTests.cs",
            """
            using System.Collections;
            using NUnit.Framework;
            using UnityEngine.TestTools;
            using ASMLite.Tests.Editor;

            public sealed class RuntimeDiscoveryTests
            {
                [UnityTest]
                public IEnumerator AppearsInPlayModeRunner()
                {
                    ASMLiteTestFixtures.CreateTestAvatar();
                    yield break;
                }
            }
            """,
        )
        result = self.run_validator("--update")
        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        ledger = json.loads(self.ledger.read_text(encoding="utf-8"))
        for row in ledger["tests"]:
            row.update({
                "lane": "playmode-headless-review",
                "headlessViability": "conditional-playmode-not-default-ci",
                "honesty": "playmode-runtime-review",
                "recommendation": "separate-playmode-lane",
                "fixtureDependencies": [],
                "assetSceneMutations": "none",
                "externalProcessFilesystemEnvUsage": "Unity PlayMode runner",
                "notes": "",
            })
        self.write_ledger_rows(ledger["tests"])
        asmdef = self.root / "Packages/com.staples.asm-lite/Tests/PlayMode/ASMLite.Tests.PlayMode.asmdef"
        asmdef.parent.mkdir(parents=True, exist_ok=True)
        asmdef.write_text(json.dumps({
            "name": "ASMLite.Tests.PlayMode",
            "references": ["UnityEngine.TestRunner"],
            "optionalUnityReferences": ["TestAssemblies"],
            "includePlatforms": [],
            "overrideReferences": True,
            "precompiledReferences": ["VRCSDK3A.dll", "VRCSDK3A-Editor.dll", "VRCSDKBase.dll", "VRCSDKBase-Editor.dll"],
        }, indent=2) + "\n", encoding="utf-8")

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("CreatePlayModeTestAvatar", result.stderr)
        self.assertIn("RuntimeDiscoveryTests.cs", result.stderr)

    def test_check_rejects_coded_method_prefixes(self) -> None:
        coded_method = "TB" + "03_LoadsPresetWithoutLegacyPrefix"
        self.write_test_file(
            "Packages/com.staples.asm-lite/Tests/Editor/PrefixTests.cs",
            f"""
            using NUnit.Framework;
            public sealed class PrefixTests
            {{
                [Test]
                public void {coded_method}() {{}}
            }}
            """,
        )
        self.write_ledger_rows([
            self.mirror_row(
                file="Packages/com.staples.asm-lite/Tests/Editor/PrefixTests.cs",
                class_name="PrefixTests",
                method=coded_method,
                line=6,
            ),
        ])

        result = self.run_validator()

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("coded prefix", result.stderr)
        self.assertIn(coded_method, result.stderr)

    def test_update_mirror_writes_sorted_rows_from_ledger(self) -> None:
        zeta = self.mirror_row(
            file="Packages/com.staples.asm-lite/Tests/Editor/ZetaTests.cs",
            class_name="ZetaTests",
            method="ZetaBehavior",
            line=20,
        )
        alpha = self.mirror_row(
            file="Packages/com.staples.asm-lite/Tests/Editor/AlphaTests.cs",
            class_name="AlphaTests",
            method="AlphaBehavior",
            line=10,
        )
        self.write_ledger_rows([zeta, alpha])
        self.write_audit_doc("| stale |\n")

        result = self.run_validator(
            "--update-mirror",
            "--audit-doc",
            str(self.audit_doc),
        )

        self.assertEqual(result.returncode, 0, msg=result.stderr or result.stdout)
        audit = self.audit_doc.read_text(encoding="utf-8")
        alpha_index = audit.index("`AlphaTests`")
        zeta_index = audit.index("`ZetaTests`")
        self.assertLess(alpha_index, zeta_index)
        self.assertIn("| `AlphaTests` | `AlphaBehavior` | `core-headless` | `yes` |", audit)
        self.assertIn("`Packages/com.staples.asm-lite/Tests/Editor/AlphaTests.cs:10`", audit)
        self.assertIn("## Later section", audit)

    def test_check_mirror_fails_when_checked_in_mirror_is_stale(self) -> None:
        self.write_ledger_rows([
            self.mirror_row(
                file="Packages/com.staples.asm-lite/Tests/Editor/ExampleTests.cs",
                class_name="ExampleTests",
                method="CurrentBehavior",
                line=12,
            ),
        ])
        self.write_audit_doc(
            "| Class | Method | Lane | Headless | Honesty | Recommendation | Mutations / external usage | Reference |\n"
            "|---|---|---|---|---|---|---|---|\n"
            "| `ExampleTests` | `StaleBehavior` | `core-headless` | `yes` | `unit-contract-coverage` | `default-ci` | none / none | `Packages/com.staples.asm-lite/Tests/Editor/ExampleTests.cs:12` |\n"
        )

        result = self.run_validator(
            "--check-mirror",
            "--audit-doc",
            str(self.audit_doc),
        )

        self.assertNotEqual(result.returncode, 0)
        self.assertIn("method mirror drift", result.stderr)
        self.assertIn("--update-mirror", result.stderr)


if __name__ == "__main__":
    unittest.main()
