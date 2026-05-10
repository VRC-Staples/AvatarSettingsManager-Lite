#!/usr/bin/env python3
"""Validate the canonical test suite map against current batch and smoke catalogs."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


SUITES_PATH = "Tools/ci/test-suites/suites.json"
EDITMODE_BATCH_RUNS_PATH = "Tools/ci/editmode-batch-runs.json"
SMOKE_CATALOG_PATH = "Tools/ci/smoke/suite-catalog.json"

REQUIRED_GROUP_IDS = (
    "contract",
    "core-headless",
    "integration-headless",
    "smoke-protocol-headless",
    "smoke-overlay-host-headless",
    "playmode-headless-review",
    "visible-manual",
)

MANUAL_OR_REVIEW_GROUP_IDS = {
    "playmode-headless-review",
    "visible-manual",
}

SMOKE_PROTOCOL_FILES = (
    "ASMLiteSmokeProtocolTests.cs",
    "ASMLiteSmokeProtocolCompatibilityTests.cs",
    "ASMLiteSmokeCatalogTests.cs",
    "ASMLiteSmokeRunExecutorTests.cs",
    "ASMLiteSmokeAtomicIoTests.cs",
    "ASMLiteSmokeArtifactPathsTests.cs",
)

SMOKE_OVERLAY_HOST_FILES = (
    "ASMLiteSmokeOverlayHostTests.cs",
    "ASMLiteSmokeSetupFixtureServiceTests.cs",
)


def repo_root_from_script() -> Path:
    return Path(__file__).resolve().parents[3]


def normalize_slashes(value: str) -> str:
    return value.replace("\\", "/")


def load_json(path: Path, label: str, errors: list[str]) -> dict[str, Any]:
    try:
        with path.open(encoding="utf-8") as handle:
            document = json.load(handle)
    except FileNotFoundError:
        errors.append(f"missing {label}: {path}")
        return {}
    except json.JSONDecodeError as exc:
        errors.append(f"invalid JSON in {label} {path}: {exc}")
        return {}
    except OSError as exc:
        errors.append(f"failed to read {label} {path}: {exc}")
        return {}
    if not isinstance(document, dict):
        errors.append(f"{label} root must be a JSON object")
        return {}
    return document


def string_list(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    out: list[str] = []
    for item in value:
        if isinstance(item, str) and item.strip():
            out.append(item.strip())
    return out


def canonical_json(value: Any) -> str:
    return json.dumps(value, sort_keys=True, separators=(",", ":"))


def groups_by_id(suites: dict[str, Any], errors: list[str]) -> dict[str, dict[str, Any]]:
    raw_groups = suites.get("groups")
    if not isinstance(raw_groups, list):
        errors.append("suites.json must contain a groups array")
        return {}

    groups: dict[str, dict[str, Any]] = {}
    for index, raw_group in enumerate(raw_groups):
        if not isinstance(raw_group, dict):
            errors.append(f"suite group at index {index} must be an object")
            continue
        group_id = raw_group.get("id")
        if not isinstance(group_id, str) or not group_id.strip():
            errors.append(f"suite group at index {index} must have a non-empty id")
            continue
        if group_id in groups:
            errors.append(f"duplicate suite group id: {group_id}")
            continue
        groups[group_id] = raw_group

    actual_ids = list(groups)
    if actual_ids != list(REQUIRED_GROUP_IDS):
        errors.append(
            "suite groups must be exactly "
            + ", ".join(REQUIRED_GROUP_IDS)
            + f" in order; found {actual_ids}"
        )
    return groups


def default_group_ids(suites: dict[str, Any], groups: dict[str, dict[str, Any]], errors: list[str]) -> list[str]:
    declared = string_list(suites.get("defaultCiGroups"))
    flagged = [group_id for group_id, group in groups.items() if group.get("defaultCi") is True]
    if declared != flagged:
        errors.append(
            "defaultCiGroups must match groups marked defaultCi=true; "
            f"declared={declared}, marked={flagged}"
        )
    for group_id in MANUAL_OR_REVIEW_GROUP_IDS:
        group = groups.get(group_id)
        if group_id in declared or (group and group.get("defaultCi") is True):
            errors.append(f"{group_id} must be excluded from default CI")
    for group_id in declared:
        if group_id not in groups:
            errors.append(f"defaultCiGroups references unknown group: {group_id}")
    return declared


def assert_smoke_membership(groups: dict[str, dict[str, Any]], errors: list[str]) -> None:
    expected_by_group = {
        "smoke-protocol-headless": list(SMOKE_PROTOCOL_FILES),
        "smoke-overlay-host-headless": list(SMOKE_OVERLAY_HOST_FILES),
    }
    for group_id, expected in expected_by_group.items():
        group = groups.get(group_id, {})
        actual = string_list(group.get("testFiles"))
        if actual != expected:
            errors.append(
                f"{group_id} testFiles must be {expected}; found {actual}"
            )

    for group_id, group in groups.items():
        for path in string_list(group.get("testFiles")):
            if normalize_slashes(path).startswith("Tools/ci/rust-overlay/"):
                errors.append(f"{group_id} must not include Rust overlay test path: {path}")


def expected_batch_runs(default_ids: list[str], groups: dict[str, dict[str, Any]], errors: list[str]) -> list[dict[str, Any]]:
    runs: list[dict[str, Any]] = []
    for group_id in default_ids:
        group = groups.get(group_id)
        if group is None:
            continue
        run = group.get("batchRun")
        if not isinstance(run, dict):
            errors.append(f"default CI group {group_id} must define a batchRun object")
            continue
        runs.append(run)
    return runs


def assert_batch_parity(
    suites: dict[str, Any],
    editmode_batch_runs: dict[str, Any],
    groups: dict[str, dict[str, Any]],
    errors: list[str],
) -> None:
    default_ids = default_group_ids(suites, groups, errors)
    expected = expected_batch_runs(default_ids, groups, errors)
    actual = editmode_batch_runs.get("runs")
    if not isinstance(actual, list):
        errors.append("editmode-batch-runs.json must contain a runs array")
        return
    if canonical_json(actual) != canonical_json(expected):
        expected_by_name = {run.get("name"): run for run in expected if isinstance(run, dict)}
        actual_by_name = {run.get("name"): run for run in actual if isinstance(run, dict)}
        for group_id in default_ids:
            run = groups.get(group_id, {}).get("batchRun")
            run_name = run.get("name") if isinstance(run, dict) else None
            if run_name not in actual_by_name:
                errors.append(f"default CI batch run drift for {group_id}: missing run {run_name}")
            elif canonical_json(actual_by_name[run_name]) != canonical_json(expected_by_name.get(run_name)):
                errors.append(f"default CI batch run drift for {group_id}: run {run_name} differs")
        expected_names = [run.get("name") for run in expected if isinstance(run, dict)]
        actual_names = [run.get("name") for run in actual if isinstance(run, dict)]
        if actual_names != expected_names:
            errors.append(
                "default CI batch run order/name drift: "
                f"expected {expected_names}, found {actual_names}"
            )


def catalog_suite_ids(catalog: dict[str, Any], errors: list[str]) -> tuple[set[str], set[str]]:
    ids: set[str] = set()
    playmode_ids: set[str] = set()
    groups = catalog.get("groups")
    if not isinstance(groups, list):
        errors.append("smoke suite catalog must contain a groups array")
        return ids, playmode_ids
    for group in groups:
        if not isinstance(group, dict):
            continue
        suites = group.get("suites")
        if not isinstance(suites, list):
            continue
        for suite in suites:
            if not isinstance(suite, dict):
                continue
            suite_id = suite.get("suiteId")
            if not isinstance(suite_id, str) or not suite_id.strip():
                errors.append("smoke suite catalog contains a suite without suiteId")
                continue
            if suite_id in ids:
                errors.append(f"smoke suite catalog contains duplicate suiteId: {suite_id}")
            ids.add(suite_id)
            if suite.get("requiresPlayMode") is True:
                playmode_ids.add(suite_id)
    return ids, playmode_ids


def assert_smoke_catalog_parity(
    repo_root: Path,
    suites: dict[str, Any],
    catalog: dict[str, Any],
    groups: dict[str, dict[str, Any]],
    errors: list[str],
) -> None:
    ids, playmode_ids = catalog_suite_ids(catalog, errors)
    referenced_ids: set[str] = set()
    for group_id, group in groups.items():
        for suite_id in string_list(group.get("smokeCatalogSuiteIds")):
            referenced_ids.add(suite_id)
            if suite_id not in ids:
                errors.append(f"{group_id} references unknown smoke catalog suiteId: {suite_id}")
    missing = sorted(ids - referenced_ids)
    if missing:
        errors.append(f"smoke catalog suites missing from suites.json: {missing}")

    playmode_group_ids = set(string_list(groups.get("playmode-headless-review", {}).get("smokeCatalogSuiteIds")))
    if playmode_group_ids != playmode_ids:
        errors.append(
            "playmode-headless-review smokeCatalogSuiteIds must match catalog requiresPlayMode suites; "
            f"expected {sorted(playmode_ids)}, found {sorted(playmode_group_ids)}"
        )

    visible_group = groups.get("visible-manual", {})
    if visible_group.get("smokeCatalogPath") != SMOKE_CATALOG_PATH:
        errors.append(f"visible-manual smokeCatalogPath must be {SMOKE_CATALOG_PATH}")

    protocol_group = groups.get("smoke-protocol-headless", {})
    parity_validator = protocol_group.get("parityValidator")
    if parity_validator != "Tools/ci/verify-smoke-protocol-parity.py":
        errors.append("smoke-protocol-headless must reference Tools/ci/verify-smoke-protocol-parity.py")
    elif not (repo_root / parity_validator).is_file():
        errors.append(f"missing smoke protocol parity validator: {parity_validator}")


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=repo_root_from_script())
    parser.add_argument("--suites", type=Path, default=None)
    parser.add_argument("--editmode-batch-runs", type=Path, default=None)
    parser.add_argument("--smoke-catalog", type=Path, default=None)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    repo_root = args.repo_root.resolve()
    suites_path = args.suites.resolve() if args.suites else repo_root / SUITES_PATH
    editmode_path = args.editmode_batch_runs.resolve() if args.editmode_batch_runs else repo_root / EDITMODE_BATCH_RUNS_PATH
    catalog_path = args.smoke_catalog.resolve() if args.smoke_catalog else repo_root / SMOKE_CATALOG_PATH

    errors: list[str] = []
    suites = load_json(suites_path, "suite map", errors)
    editmode_batch_runs = load_json(editmode_path, "EditMode batch runs", errors)
    smoke_catalog = load_json(catalog_path, "smoke suite catalog", errors)
    if not suites or not editmode_batch_runs or not smoke_catalog:
        for message in errors:
            print(f"suite map validation failed: {message}", file=sys.stderr)
        return 1

    if suites.get("schemaVersion") != 1:
        errors.append("suites.json schemaVersion must be 1")
    groups = groups_by_id(suites, errors)
    if groups:
        assert_smoke_membership(groups, errors)
        assert_batch_parity(suites, editmode_batch_runs, groups, errors)
        assert_smoke_catalog_parity(repo_root, suites, smoke_catalog, groups, errors)

    if errors:
        for message in errors:
            print(f"suite map validation failed: {message}", file=sys.stderr)
        return 1

    print(f"suite map ok: {len(groups)} groups, {len(suites.get('defaultCiGroups', []))} default CI groups")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
