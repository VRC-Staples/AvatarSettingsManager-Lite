#!/usr/bin/env python3
"""Verify Unity EditMode NUnit XML and classify known teardown-only exits."""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


TEARDOWN_WARNING_MARKERS = (
    "Assertion failed on expression: 'm_ErrorCode == MDB_MAP_FULL || !HasAbortingErrors()'",
    "Assertion failed on expression: \"m_ErrorCode == MDB_MAP_FULL || !HasAbortingErrors()\"",
)

PRODUCT_FAILURE_MARKERS = (
    "Aborting batchmode due to failure",
    "Scripts have compiler errors",
    "error CS",
    "Unhandled managed exception",
    "Test run failed",
    "Cancelling DisplayDialog",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Verify Unity EditMode NUnit XML and handle teardown-only Unity exits."
    )
    parser.add_argument("--unity-exit-code", required=True, type=int)
    parser.add_argument("--results", required=True, type=Path)
    parser.add_argument("--log", required=True, type=Path)
    parser.add_argument(
        "--forbid-skipped-tests",
        action="store_true",
        help="Fail when NUnit XML reports skipped tests or skipped test cases.",
    )
    parser.add_argument(
        "--forbid-inconclusive-tests",
        action="store_true",
        help="Fail when NUnit XML reports inconclusive tests or test cases.",
    )
    return parser.parse_args()


def load_nunit_xml(path: Path) -> ET.Element:
    if not path.is_file():
        raise RuntimeError(f"Missing NUnit XML: {path}")
    try:
        return ET.parse(path).getroot()
    except ET.ParseError as exc:
        raise RuntimeError(f"Malformed NUnit XML '{path}': {exc}") from exc
    except OSError as exc:
        raise RuntimeError(f"Failed reading NUnit XML '{path}': {exc}") from exc


def int_attr(node: ET.Element, name: str, default: int = 0) -> int:
    raw = node.get(name)
    if raw is None or raw == "":
        return default
    try:
        return int(raw)
    except ValueError as exc:
        raise RuntimeError(f"NUnit XML has non-integer {name} value: {raw!r}") from exc


def test_case_names_with_result(root: ET.Element, result_name: str) -> list[str]:
    names: list[str] = []
    expected = result_name.lower()
    for case in root.iter("test-case"):
        result = (case.get("result") or "").lower()
        label = (case.get("label") or "").lower()
        if result == expected or label == expected:
            names.append(case.get("fullname") or case.get("name") or "<unnamed test-case>")
    return names


def failed_test_names(root: ET.Element) -> list[str]:
    return test_case_names_with_result(root, "failed")


def format_case_details(names: list[str]) -> str:
    if not names:
        return ""
    details = ": " + ", ".join(names[:20])
    if len(names) > 20:
        details += f", ... ({len(names)} total cases)"
    return details


def validate_passing_nunit_xml(
    root: ET.Element,
    *,
    forbid_skipped_tests: bool = False,
    forbid_inconclusive_tests: bool = False,
) -> tuple[int, int]:
    total = int_attr(root, "total", int_attr(root, "testcasecount", 0))
    failed = int_attr(root, "failed", 0)
    skipped = int_attr(root, "skipped", 0)
    inconclusive = int_attr(root, "inconclusive", 0)
    result = root.get("result", "")
    failed_names = failed_test_names(root)

    if total <= 0:
        raise RuntimeError("NUnit XML reports zero selected tests")
    if failed > 0 or failed_names or result.lower() == "failed":
        raise RuntimeError(
            f"NUnit XML reports failed tests (failed={failed})"
            f"{format_case_details(failed_names)}"
        )
    if result and result.lower() != "passed":
        raise RuntimeError(f"NUnit XML result is not passing: {result}")
    if forbid_skipped_tests:
        skipped_names = test_case_names_with_result(root, "skipped")
        if skipped > 0 or skipped_names:
            raise RuntimeError(
                f"NUnit XML reports skipped tests (skipped={skipped})"
                f"{format_case_details(skipped_names)}"
            )
    if forbid_inconclusive_tests:
        inconclusive_names = test_case_names_with_result(root, "inconclusive")
        if inconclusive > 0 or inconclusive_names:
            raise RuntimeError(
                f"NUnit XML reports inconclusive tests (inconclusive={inconclusive})"
                f"{format_case_details(inconclusive_names)}"
            )
    return total, failed


def read_log(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except FileNotFoundError:
        return ""
    except OSError as exc:
        raise RuntimeError(f"Failed reading Unity log '{path}': {exc}") from exc


def is_known_teardown_warning(exit_code: int, log_text: str) -> bool:
    if exit_code != 133:
        return False
    if not any(marker in log_text for marker in TEARDOWN_WARNING_MARKERS):
        return False
    return not any(marker in log_text for marker in PRODUCT_FAILURE_MARKERS)


def main() -> int:
    args = parse_args()
    try:
        root = load_nunit_xml(args.results)
        total, failed = validate_passing_nunit_xml(
            root,
            forbid_skipped_tests=args.forbid_skipped_tests,
            forbid_inconclusive_tests=args.forbid_inconclusive_tests,
        )
        if args.unity_exit_code == 0:
            print(
                f"unity-editmode-results-ok: total={total} failed={failed} results={args.results}"
            )
            return 0

        log_text = read_log(args.log)
        if is_known_teardown_warning(args.unity_exit_code, log_text):
            print(
                "::warning::unity-teardown-warning: "
                f"Unity exited {args.unity_exit_code} after writing passing NUnit XML; "
                f"total={total} failed={failed} results={args.results} log={args.log}"
            )
            return 0

        print(
            f"error: Unity exited {args.unity_exit_code} and did not match a known teardown-only warning. "
            f"Passing NUnit XML exists at {args.results}; inspect log {args.log}.",
            file=sys.stderr,
        )
        return args.unity_exit_code if 0 < args.unity_exit_code < 128 else 1
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        if args.unity_exit_code and args.unity_exit_code < 128:
            return args.unity_exit_code
        return 1


if __name__ == "__main__":
    sys.exit(main())
