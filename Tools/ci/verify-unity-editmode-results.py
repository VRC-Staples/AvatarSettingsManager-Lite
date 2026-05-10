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


def failed_test_names(root: ET.Element) -> list[str]:
    names: list[str] = []
    for case in root.iter("test-case"):
        result = (case.get("result") or "").lower()
        if result == "failed":
            names.append(case.get("fullname") or case.get("name") or "<unnamed test-case>")
    return names


def validate_passing_nunit_xml(root: ET.Element) -> tuple[int, int]:
    total = int_attr(root, "total", int_attr(root, "testcasecount", 0))
    failed = int_attr(root, "failed", 0)
    result = root.get("result", "")
    failed_names = failed_test_names(root)

    if total <= 0:
        raise RuntimeError("NUnit XML reports zero selected tests")
    if failed > 0 or failed_names or result.lower() == "failed":
        details = ""
        if failed_names:
            details = ": " + ", ".join(failed_names[:20])
            if len(failed_names) > 20:
                details += f", ... ({len(failed_names)} total failed cases)"
        raise RuntimeError(f"NUnit XML reports failed tests (failed={failed}){details}")
    if result and result.lower() != "passed":
        raise RuntimeError(f"NUnit XML result is not passing: {result}")
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
        total, failed = validate_passing_nunit_xml(root)
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
