#!/usr/bin/env python3
"""Fail-closed verifier for milestone M010/S04 anchor NUnit cases.

This script intentionally enforces only the contract-critical case IDs
(TB13-TB19, A26, A27, VF06) against a filtered EditMode NUnit XML output.
"""

from __future__ import annotations

import argparse
import re
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path


PASS_VALUES = {"passed", "success"}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Verify required contract test IDs exist and pass in an NUnit XML results file. "
            "Fails closed on missing file, malformed XML, duplicate --require values, "
            "missing IDs, or non-passing required cases."
        )
    )
    parser.add_argument("--results", required=True, help="Path to NUnit XML results file.")
    parser.add_argument(
        "--require",
        action="append",
        default=[],
        help="Required case ID token (repeatable), e.g. TB13.",
    )
    return parser.parse_args()


def load_xml(path: Path) -> ET.Element:
    if not path.exists():
        raise RuntimeError(f"Missing NUnit XML artifact: {path}")

    try:
        tree = ET.parse(path)
    except ET.ParseError as exc:
        raise RuntimeError(f"Malformed NUnit XML '{path}': {exc}") from exc
    except OSError as exc:
        raise RuntimeError(f"Failed reading NUnit XML '{path}': {exc}") from exc

    return tree.getroot()


def collect_cases(root: ET.Element) -> list[dict[str, str]]:
    cases: list[dict[str, str]] = []
    for node in root.findall(".//test-case"):
        name = (node.get("name") or "").strip()
        fullname = (node.get("fullname") or "").strip()
        classname = (node.get("classname") or "").strip()
        methodname = (node.get("methodname") or "").strip()
        result = (node.get("result") or node.get("outcome") or "").strip()

        searchable = " | ".join(part for part in (name, fullname, classname, methodname) if part)
        cases.append(
            {
                "name": name or "<unnamed>",
                "searchable": searchable,
                "result": result,
            }
        )
    return cases


def is_case_token_match(token: str, searchable: str) -> bool:
    # Token must stand alone (e.g. TB13_, TB13:), not as TB130.
    pattern = re.compile(rf"(?<![A-Za-z0-9]){re.escape(token)}(?![A-Za-z0-9])")
    return bool(pattern.search(searchable))


def is_pass(result: str) -> bool:
    return result.strip().lower() in PASS_VALUES


def main() -> int:
    args = parse_args()
    results_path = Path(args.results)

    if not args.require:
        print("::error::No required IDs provided. Use --require at least once.")
        return 1

    dupes = [token for token, count in Counter(args.require).items() if count > 1]
    if dupes:
        print(
            "::error::Duplicate --require IDs are not allowed: "
            + ", ".join(sorted(dupes))
        )
        return 1

    try:
        root = load_xml(results_path)
    except RuntimeError as exc:
        print(f"::error::{exc}")
        return 1

    cases = collect_cases(root)
    if not cases:
        print(f"::error::No <test-case> nodes found in NUnit XML '{results_path}'.")
        return 1

    missing: list[str] = []
    failed: list[str] = []

    for required_id in args.require:
        matches = [case for case in cases if is_case_token_match(required_id, case["searchable"])]

        if not matches:
            missing.append(required_id)
            continue

        non_passing = [case for case in matches if not is_pass(case["result"])]
        if non_passing:
            for case in non_passing:
                failed.append(
                    f"{required_id}: {case['name']} -> result='{case['result'] or '<empty>'}'"
                )

    if missing:
        print("::error::Missing required test IDs in NUnit XML: " + ", ".join(missing))
    if failed:
        print("::error::Required test IDs have non-passing results:")
        for item in failed:
            print(f"::error::  - {item}")

    if missing or failed:
        print(f"::error::Contract verification failed for '{results_path}'.")
        return 1

    print(
        "Contract verification passed for required IDs: "
        + ", ".join(args.require)
    )
    print(f"Verified results file: {results_path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
