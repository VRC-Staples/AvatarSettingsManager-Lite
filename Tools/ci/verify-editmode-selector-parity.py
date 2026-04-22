#!/usr/bin/env python3
"""verify-editmode-selector-parity.py: fail-closed parity checks for Wave 1 CI selectors."""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any

REQUIRED_RUN_NAMES = ("editmode-core", "editmode-integration")
REQUIRED_CORE_FIXTURE_TOKENS = (
    "ASMLiteAssetPathsTests",
    "ASMLiteBatchTestRunnerTests",
    "ASMLiteBuilderTests",
    "ASMLiteComponentTests",
    "ASMLiteCustomizationScaffoldTests",
    "ASMLiteGenerationWiringSummaryWriterTests",
    "ASMLiteInstallPathWiringTests",
    "ASMLiteParameterDiscoveryTests",
    "ASMLiteToggleBrokerTests",
    "ASMLiteVisibleAutomationCommandLineTests",
    "ASMLiteWindowActionHierarchyTests",
    "ASMLiteWindowCustomizationTests",
    "ASMLiteWindowNamingGroupingTests",
    "ASMLiteWindowRiskAffordanceTests",
    "ASMLiteWindowStatusPanelTests",
    "ASMLiteWindowTerminologyTests",
)

PREFAB_WIRING_ANCHOR = (
    "ASMLite.Tests.Editor.ASMLitePrefabWiringTests."
    "W02_HasStalePrmsEntry_DetectsLegacyPrmsNames_AndIgnoresOtherNames"
)

GENERATION_ANCHOR = "A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces"
WIRING_ANCHOR = "W08_BuildSync_PropagatesCustomInstallPathToLiveFullControllerPrefix"
MIGRATION_ANCHOR = "A55_RebuildPrep_MixedLegacyState_RemovesOnlyObsoleteArtifacts"
TOGGLE_BROKER_ANCHOR = (
    "TB13_Enrollment_PreReservedDescriptorNamesAreSkippedWithoutBlockingUniqueAssignments"
)
VISIBLE_SMOKE_FIXTURE = "ASMLiteVisibleEditorSmokeTests"
CANONICAL_PLAN_LITERAL = "Tools/ci/editmode-batch-runs.json"
CANONICAL_RESULTS_LITERAL = "artifacts/editmode-results.xml"
EDITMODE_JOB_NAME = "name: EditMode Tests"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Fail-closed parity validator for the canonical Wave 1 EditMode selector split."
        )
    )
    parser.add_argument("--plan", required=True, help="Path to Tools/ci/editmode-batch-runs.json")
    parser.add_argument("--ci-workflow", required=True, help="Path to .github/workflows/ci.yml")
    parser.add_argument(
        "--tests-root",
        required=True,
        help="Path to Packages/com.staples.asm-lite/Tests/Editor",
    )
    parser.add_argument(
        "--local-script",
        help="Optional path to Tools/ci/run-editmode-local.sh for Wave 2 parity checks",
    )
    parser.add_argument(
        "--expected-project",
        help="Optional expected default project path for the local runner",
    )
    args = parser.parse_args()

    if bool(args.local_script) != bool(args.expected_project):
        parser.error("--local-script and --expected-project must be supplied together")

    return args


def normalize_newlines(text: str) -> str:
    return text.replace("\r\n", "\n").replace("\r", "\n")


def read_text(path: Path, label: str, errors: list[str]) -> str:
    if not path.is_file():
        errors.append(f"Missing {label}: {path}")
        return ""
    try:
        return normalize_newlines(path.read_text(encoding="utf-8"))
    except OSError as exc:
        errors.append(f"Failed to read {label} {path}: {exc}")
        return ""


def load_json(path: Path, errors: list[str]) -> dict[str, Any]:
    if not path.is_file():
        errors.append(f"Missing plan file: {path}")
        return {}

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        errors.append(f"Plan JSON is invalid at {path}: {exc}")
        return {}
    except OSError as exc:
        errors.append(f"Failed to read plan JSON {path}: {exc}")
        return {}

    if not isinstance(payload, dict):
        errors.append("Plan root must be a JSON object")
        return {}

    return payload


def normalized_string_list(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []

    out: list[str] = []
    for item in value:
        if not isinstance(item, str):
            continue
        item = item.strip()
        if item:
            out.append(item)
    return out


def collect_selectors(run: dict[str, Any]) -> tuple[list[str], list[str], list[str]]:
    test_names = normalized_string_list(run.get("testNames"))
    group_names = normalized_string_list(run.get("groupNames"))
    category_names = normalized_string_list(run.get("categoryNames"))

    raw_filters = run.get("filters")
    if isinstance(raw_filters, list):
        for raw_filter in raw_filters:
            if not isinstance(raw_filter, dict):
                continue
            test_names.extend(normalized_string_list(raw_filter.get("testNames")))
            group_names.extend(normalized_string_list(raw_filter.get("groupNames")))
            category_names.extend(normalized_string_list(raw_filter.get("categoryNames")))

    return test_names, group_names, category_names


def has_selector_token(token: str, test_names: list[str], group_names: list[str]) -> bool:
    for selector in test_names + group_names:
        if token in selector:
            return True
    return False


def find_run_by_name(runs: list[Any], name: str) -> dict[str, Any] | None:
    for run in runs:
        if isinstance(run, dict) and run.get("name") == name:
            return run
    return None


def method_is_integration_tagged(text: str, method_name: str) -> bool:
    signature_pattern = re.compile(rf"\b{re.escape(method_name)}\s*\(")
    match = signature_pattern.search(text)
    if not match:
        return False

    lookback = text[max(0, match.start() - 600) : match.start()]
    return 'Category("Integration")' in lookback


def extract_assignment_value(script_text: str, variable: str) -> str | None:
    pattern = re.compile(rf"^\s*{re.escape(variable)}=\"([^\"]*)\"", re.MULTILINE)
    match = pattern.search(script_text)
    if not match:
        return None
    return match.group(1).strip()


def normalize_path_text(path_text: str) -> str:
    return path_text.replace("\\", "/").strip()


def verify_plan(plan: dict[str, Any], plan_text: str, errors: list[str]) -> None:
    runs = plan.get("runs")
    if not isinstance(runs, list):
        errors.append("Plan must contain a top-level 'runs' array")
        return

    run_names = []
    for run in runs:
        if not isinstance(run, dict):
            continue
        name = run.get("name")
        if isinstance(name, str) and name.strip():
            run_names.append(name.strip())

    if len(runs) != 2:
        errors.append(f"Plan must define exactly 2 runs; found {len(runs)}")

    if sorted(run_names) != sorted(REQUIRED_RUN_NAMES):
        errors.append(
            "Plan run names must be exactly editmode-core and editmode-integration; "
            f"found {run_names}"
        )

    core_run = find_run_by_name(runs, "editmode-core")
    if core_run is None:
        errors.append("Missing required run: editmode-core")
    else:
        test_names, group_names, _ = collect_selectors(core_run)
        for fixture_token in REQUIRED_CORE_FIXTURE_TOKENS:
            if not has_selector_token(fixture_token, test_names, group_names):
                errors.append(
                    f"editmode-core is missing required fixture selector token: {fixture_token}"
                )

        if not has_selector_token(PREFAB_WIRING_ANCHOR, test_names, group_names):
            errors.append(
                "editmode-core is missing required prefab wiring anchor selector: "
                f"{PREFAB_WIRING_ANCHOR}"
            )

    integration_run = find_run_by_name(runs, "editmode-integration")
    if integration_run is None:
        errors.append("Missing required run: editmode-integration")
    else:
        _, _, category_names = collect_selectors(integration_run)
        if "Integration" not in category_names:
            errors.append("editmode-integration must select Category(\"Integration\")")

    if VISIBLE_SMOKE_FIXTURE in plan_text:
        errors.append(
            "Canonical plan must not include ASMLiteVisibleEditorSmokeTests selectors"
        )


def verify_workflow(workflow_text: str, errors: list[str]) -> None:
    required_snippets = (
        EDITMODE_JOB_NAME,
        CANONICAL_PLAN_LITERAL,
        CANONICAL_RESULTS_LITERAL,
        "ASMLite.Tests.Editor.ASMLiteBatchTestRunner.RunFromCommandLine",
        "-asmliteBatchRunsJsonPath",
    )

    for snippet in required_snippets:
        if snippet not in workflow_text:
            errors.append(f"Workflow missing required snippet: {snippet}")

    forbidden_snippets = (
        "core_fixture_patterns",
        "payload = {",
    )
    for snippet in forbidden_snippets:
        if snippet in workflow_text:
            errors.append(f"Workflow still contains forbidden inline selector logic: {snippet}")


def verify_test_anchors(tests_root: Path, errors: list[str]) -> None:
    build_file = tests_root / "ASMLiteBuildIntegrationTests.cs"
    migration_file = tests_root / "ASMLiteMigrationTests.cs"
    wiring_file = tests_root / "ASMLiteInstallPathWiringTests.cs"
    toggle_file = tests_root / "ASMLiteToggleBrokerTests.cs"

    build_text = read_text(build_file, "build integration test file", errors)
    migration_text = read_text(migration_file, "migration test file", errors)
    wiring_text = read_text(wiring_file, "install-path wiring test file", errors)
    toggle_text = read_text(toggle_file, "toggle-broker test file", errors)

    if build_text:
        if GENERATION_ANCHOR not in build_text:
            errors.append(f"Missing representative generation anchor: {GENERATION_ANCHOR}")
        elif not method_is_integration_tagged(build_text, GENERATION_ANCHOR):
            errors.append(
                f"Representative generation anchor must remain Integration-tagged: {GENERATION_ANCHOR}"
            )

    if migration_text:
        if MIGRATION_ANCHOR not in migration_text:
            errors.append(f"Missing representative migration anchor: {MIGRATION_ANCHOR}")
        elif not method_is_integration_tagged(migration_text, MIGRATION_ANCHOR):
            errors.append(
                f"Representative migration anchor must remain Integration-tagged: {MIGRATION_ANCHOR}"
            )

    if wiring_text and WIRING_ANCHOR not in wiring_text:
        errors.append(f"Missing representative wiring anchor: {WIRING_ANCHOR}")

    if toggle_text and TOGGLE_BROKER_ANCHOR not in toggle_text:
        errors.append(f"Missing representative toggle-broker anchor: {TOGGLE_BROKER_ANCHOR}")


def verify_local_runner(
    local_script_path: Path,
    expected_project: str,
    errors: list[str],
) -> None:
    script_text = read_text(local_script_path, "local runner script", errors)
    if not script_text:
        return

    expected_project_norm = normalize_path_text(expected_project)

    project_rel = extract_assignment_value(script_text, "PROJECT_REL_PATH")
    project_path = extract_assignment_value(script_text, "PROJECT_PATH")

    if project_rel is not None:
        if normalize_path_text(project_rel) != expected_project_norm:
            errors.append(
                "Local runner default PROJECT_REL_PATH drifted: "
                f"expected '{expected_project}', found '{project_rel}'"
            )
    elif project_path is None or expected_project_norm not in normalize_path_text(project_path):
        errors.append(
            "Unable to prove local runner defaults to expected project path: "
            f"{expected_project}"
        )

    if CANONICAL_PLAN_LITERAL not in script_text:
        errors.append(
            "Local runner must reference the shared canonical batch plan path: "
            f"{CANONICAL_PLAN_LITERAL}"
        )

    default_match = re.search(
        r"ASMLITE_BATCH_RUNS_JSON_PATH:=([^}]*)}",
        script_text,
    )
    if not default_match:
        errors.append(
            "Local runner must declare a non-empty default for ASMLITE_BATCH_RUNS_JSON_PATH"
        )
    elif not default_match.group(1).strip().strip('"'):
        errors.append(
            "ASMLITE_BATCH_RUNS_JSON_PATH default is empty; expected canonical shared plan"
        )


def main() -> int:
    args = parse_args()
    errors: list[str] = []

    plan_path = Path(args.plan)
    workflow_path = Path(args.ci_workflow)
    tests_root = Path(args.tests_root)

    plan_text = read_text(plan_path, "canonical batch plan", errors)
    workflow_text = read_text(workflow_path, "CI workflow", errors)
    plan = load_json(plan_path, errors)

    if plan and plan_text:
        verify_plan(plan, plan_text, errors)

    if workflow_text:
        verify_workflow(workflow_text, errors)

    verify_test_anchors(tests_root, errors)

    if args.local_script and args.expected_project:
        verify_local_runner(Path(args.local_script), args.expected_project, errors)

    if errors:
        for message in errors:
            print(f"ERROR: {message}", file=sys.stderr)
        return 1

    print("PASS: verify-editmode-selector-parity.py checks passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
