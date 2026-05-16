#!/usr/bin/env python3
"""Validate the canonical test suite map against current batch and smoke catalogs."""

from __future__ import annotations

import argparse
import json
import os
import re
import sys
from pathlib import Path
from typing import Any


SUITES_PATH = "Tools/ci/test-suites/suites.json"
SMOKE_CATALOG_PATH = "Tools/ci/smoke/suite-catalog.json"
REMOVED_EDITMODE_BATCH_RUNS_PATHS = (
    "/".join(("Tools", "ci", "editmode-batch-runs.json")),
    "/".join(("Tools", "ci", "test-suites", "editmode-batch-runs.json")),
)

REFERENCE_SCAN_EXTENSIONS = {
    ".cs",
    ".py",
    ".ps1",
    ".sh",
    ".yaml",
    ".yml",
}
REFERENCE_SCAN_IGNORED_DIRS = {
    ".audits",
    ".git",
    ".planning",
    ".venv",
    "__pycache__",
    "artifacts",
    "build",
    "CodeCoverage",
    "dist",
    "Library",
    "Logs",
    "node_modules",
    "obj",
    "Temp",
    "target",
}
PATH_COMBINE_CALL_RE = re.compile(r"Path\.Combine\s*\((.*?)\)\s*;", re.DOTALL)
STRING_LITERAL_RE = re.compile(r'"((?:\\.|[^"\\])*)"')
CODED_PREFIX_TOKEN_RE = re.compile(r"(?<![A-Za-z0-9])[A-Z]{1,8}\d{2,4}[A-Z]?(?=\b|[_:-])", re.IGNORECASE)

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


def iter_reference_files(repo_root: Path) -> list[Path]:
    files: list[Path] = []
    for current_dir, dirnames, filenames in os.walk(repo_root):
        dirnames[:] = [name for name in dirnames if name not in REFERENCE_SCAN_IGNORED_DIRS]
        current_path = Path(current_dir)
        for filename in filenames:
            path = current_path / filename
            if path.suffix in REFERENCE_SCAN_EXTENSIONS:
                files.append(path)
    return sorted(files)


def line_number_at(text: str, offset: int) -> int:
    return text.count("\n", 0, offset) + 1


def removed_editmode_batch_path_from_path_combine(call_args: str) -> str | None:
    segments = [match.group(1) for match in STRING_LITERAL_RE.finditer(call_args)]
    removed_paths = (
        ["Tools", "ci", "editmode-batch-runs.json"],
        ["Tools", "ci", "test-suites", "editmode-batch-runs.json"],
    )
    for path_segments in removed_paths:
        for index in range(len(segments)):
            if segments[index:index + len(path_segments)] == path_segments:
                return "/".join(path_segments)
    return None


def assert_removed_editmode_batch_files_absent(repo_root: Path, errors: list[str]) -> None:
    for removed_path in REMOVED_EDITMODE_BATCH_RUNS_PATHS:
        candidate = repo_root / removed_path
        if candidate.exists():
            errors.append(f"removed EditMode batch plan file must not exist: {removed_path}; use {SUITES_PATH}")


def assert_no_removed_editmode_batch_path_references(repo_root: Path, errors: list[str]) -> None:
    for path in iter_reference_files(repo_root):
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            text = path.read_text(encoding="utf-8", errors="ignore")
        except OSError as exc:
            errors.append(f"failed to scan {path}: {exc}")
            continue

        relative = path.relative_to(repo_root).as_posix()
        if relative in {
            "Tools/ci/test-suites/validate-suites.py",
            "Tools/ci/tests/test_suites/test_validate_suites.py",
        }:
            continue
        for removed_path in REMOVED_EDITMODE_BATCH_RUNS_PATHS:
            exact_offset = text.find(removed_path)
            if exact_offset >= 0:
                errors.append(
                    f"removed EditMode batch plan path reference in {relative}:{line_number_at(text, exact_offset)}: "
                    f"{removed_path}; use {SUITES_PATH}"
                )

        if path.suffix != ".cs":
            continue
        for match in PATH_COMBINE_CALL_RE.finditer(text):
            removed_path = removed_editmode_batch_path_from_path_combine(match.group(1))
            if removed_path:
                errors.append(
                    f"removed EditMode batch plan path reference in {relative}:{line_number_at(text, match.start())}: "
                    f"{removed_path}; use {SUITES_PATH}"
                )


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


def iter_json_strings(value: Any, path: str = "$") -> list[tuple[str, str]]:
    if isinstance(value, dict):
        results: list[tuple[str, str]] = []
        for key, child in value.items():
            results.extend(iter_json_strings(child, f"{path}.{key}"))
        return results
    if isinstance(value, list):
        results: list[tuple[str, str]] = []
        for index, child in enumerate(value):
            results.extend(iter_json_strings(child, f"{path}[{index}]"))
        return results
    if isinstance(value, str):
        return [(path, value)]
    return []


def assert_no_coded_prefixes_in_smoke_catalog(catalog: dict[str, Any], errors: list[str]) -> None:
    for path, value in iter_json_strings(catalog):
        match = CODED_PREFIX_TOKEN_RE.search(value)
        if match:
            errors.append(f"smoke suite catalog coded prefix at {path}: {match.group(0)} in {value}")


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


def run_has_selector(run: dict[str, Any]) -> bool:
    selector_keys = ("testNames", "groupNames", "categoryNames", "assemblyNames")
    if any(string_list(run.get(key)) for key in selector_keys):
        return True
    filters = run.get("filters")
    if not isinstance(filters, list):
        return False
    return any(
        isinstance(filter_item, dict) and any(string_list(filter_item.get(key)) for key in selector_keys)
        for filter_item in filters
    )


def assert_default_batch_runs(
    suites: dict[str, Any],
    groups: dict[str, dict[str, Any]],
    errors: list[str],
) -> None:
    default_ids = default_group_ids(suites, groups, errors)
    seen_names: dict[str, str] = {}
    seen_result_files: dict[str, str] = {}
    for group_id in default_ids:
        group = groups.get(group_id)
        if group is None:
            continue
        run = group.get("batchRun")
        if not isinstance(run, dict):
            errors.append(f"default CI group {group_id} must define a batchRun object")
            continue
        run_name = run.get("name")
        result_file = run.get("resultFile")
        if not isinstance(run_name, str) or not run_name.strip():
            errors.append(f"default CI group {group_id} batchRun must define a non-empty name")
            continue
        if not isinstance(result_file, str) or not result_file.strip():
            errors.append(f"default CI batch run {group_id}/{run_name} must define a non-empty resultFile")
        if run_name in seen_names:
            errors.append(f"duplicate default CI batch run name {run_name}: {seen_names[run_name]} and {group_id}")
        else:
            seen_names[run_name] = group_id
        if isinstance(result_file, str) and result_file.strip():
            normalized_result_file = result_file.strip()
            if normalized_result_file in seen_result_files:
                errors.append(
                    f"duplicate default CI batch resultFile {normalized_result_file}: "
                    f"{seen_result_files[normalized_result_file]} and {group_id}"
                )
            else:
                seen_result_files[normalized_result_file] = group_id
        if not run.get("allowEmptySelection") and not run_has_selector(run):
            errors.append(f"default CI batch run {group_id}/{run_name} must declare at least one selector")


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


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=repo_root_from_script())
    parser.add_argument("--suites", type=Path, default=None)
    parser.add_argument("--smoke-catalog", type=Path, default=None)
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    repo_root = args.repo_root.resolve()
    suites_path = args.suites.resolve() if args.suites else repo_root / SUITES_PATH
    catalog_path = args.smoke_catalog.resolve() if args.smoke_catalog else repo_root / SMOKE_CATALOG_PATH

    errors: list[str] = []
    suites = load_json(suites_path, "suite map", errors)
    smoke_catalog = load_json(catalog_path, "smoke suite catalog", errors)
    if not suites or not smoke_catalog:
        for message in errors:
            print(f"suite map validation failed: {message}", file=sys.stderr)
        return 1

    if suites.get("schemaVersion") != 1:
        errors.append("suites.json schemaVersion must be 1")
    groups = groups_by_id(suites, errors)
    if groups:
        assert_smoke_membership(groups, errors)
        assert_default_batch_runs(suites, groups, errors)
        assert_smoke_catalog_parity(repo_root, suites, smoke_catalog, groups, errors)
    assert_no_coded_prefixes_in_smoke_catalog(smoke_catalog, errors)
    assert_removed_editmode_batch_files_absent(repo_root, errors)
    assert_no_removed_editmode_batch_path_references(repo_root, errors)

    if errors:
        for message in errors:
            print(f"suite map validation failed: {message}", file=sys.stderr)
        return 1

    print(f"suite map ok: {len(groups)} groups, {len(suites.get('defaultCiGroups', []))} default CI groups")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
