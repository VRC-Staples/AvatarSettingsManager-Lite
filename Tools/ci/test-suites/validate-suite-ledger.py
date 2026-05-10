#!/usr/bin/env python3
"""Generate and validate the Unity C# test suite ledger."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any


SCHEMA_VERSION = 1
DEFAULT_INCLUDE = "Packages/com.staples.asm-lite/Tests/**/*.cs"
RUST_OVERLAY_EXCLUDE = "Tools/ci/rust-overlay/**"
LEDGER_PATH = "Tools/ci/test-suite-ledger.json"
PLACEHOLDER = {
    "lane": "needs-classification",
    "headlessViability": "review",
    "honesty": "needs-review",
    "recommendation": "review",
}
HUMAN_FIELDS = (
    "lane",
    "headlessViability",
    "honesty",
    "recommendation",
    "publicBehaviorClaim",
    "fixtureDependencies",
    "assetSceneMutations",
    "externalProcessFilesystemEnvUsage",
    "runnerFilter",
    "notes",
)
IDENTITY_FIELDS = ("file", "class", "method")
TEST_ATTRIBUTE_NAMES = {"Test", "UnityTest", "TestCase", "TestCaseSource"}
SMOKE_PROTOCOL_FILES = {
    "ASMLiteSmokeProtocolTests.cs",
    "ASMLiteSmokeProtocolCompatibilityTests.cs",
    "ASMLiteSmokeCatalogTests.cs",
    "ASMLiteSmokeRunExecutorTests.cs",
    "ASMLiteSmokeAtomicIoTests.cs",
    "ASMLiteSmokeArtifactPathsTests.cs",
}
SMOKE_OVERLAY_HOST_FILES = {
    "ASMLiteSmokeOverlayHostTests.cs",
    "ASMLiteSmokeSetupFixtureServiceTests.cs",
}


@dataclass(frozen=True)
class TestIdentity:
    file: str
    class_name: str
    method: str
    line: int
    attributes: tuple[str, ...]
    categories: tuple[str, ...]

    @property
    def key(self) -> tuple[str, str, str]:
        return (self.file, self.class_name, self.method)


CLASS_RE = re.compile(
    r"(?P<attrs>(?:\s*\[[^\]]+\]\s*)*)"
    r"\s*(?:(?:public|internal|private|protected|sealed|static|abstract|partial|new)\s+)*"
    r"class\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b",
    re.MULTILINE,
)
METHOD_LINE_RE = re.compile(
    r"^(?:(?:public|internal|private|protected|static|async|virtual|override|sealed|new)\s+)*"
    r"(?:[A-Za-z_][A-Za-z0-9_<>\[\],.?]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_<>\[\],.?]*)*\s+)+"
    r"(?P<method>[A-Za-z_][A-Za-z0-9_]*)\s*"
    r"(?:<[^>]+>\s*)?\([^;{}]*\)"
)
ATTRIBUTE_RE = re.compile(r"\[([^\]]+)\]", re.DOTALL)
CATEGORY_RE = re.compile(r"\bCategory\s*\(\s*\"([^\"]+)\"\s*\)")


def repo_root_from_script() -> Path:
    return Path(__file__).resolve().parents[3]


def fail(message: str) -> int:
    print(f"suite ledger validation failed: {message}", file=sys.stderr)
    return 1


def normalize_slashes(path: Path | str) -> str:
    return str(path).replace("\\", "/")


def brace_end(source: str, open_brace_index: int) -> int:
    depth = 0
    in_line_comment = False
    in_block_comment = False
    in_string = False
    in_char = False
    verbatim_string = False
    i = open_brace_index
    while i < len(source):
        char = source[i]
        nxt = source[i + 1] if i + 1 < len(source) else ""
        if in_line_comment:
            if char in "\r\n":
                in_line_comment = False
            i += 1
            continue
        if in_block_comment:
            if char == "*" and nxt == "/":
                in_block_comment = False
                i += 2
            else:
                i += 1
            continue
        if in_string:
            if verbatim_string and char == '"' and nxt == '"':
                i += 2
                continue
            if char == '"' and (verbatim_string or source[i - 1] != "\\"):
                in_string = False
                verbatim_string = False
            i += 1
            continue
        if in_char:
            if char == "'" and source[i - 1] != "\\":
                in_char = False
            i += 1
            continue
        if char == "/" and nxt == "/":
            in_line_comment = True
            i += 2
            continue
        if char == "/" and nxt == "*":
            in_block_comment = True
            i += 2
            continue
        if char == "@" and nxt == '"':
            in_string = True
            verbatim_string = True
            i += 2
            continue
        if char == '"':
            in_string = True
            i += 1
            continue
        if char == "'":
            in_char = True
            i += 1
            continue
        if char == "{":
            depth += 1
        elif char == "}":
            depth -= 1
            if depth == 0:
                return i + 1
        i += 1
    return len(source)


def split_attributes(attribute_block: str) -> list[str]:
    attributes: list[str] = []
    for match in ATTRIBUTE_RE.finditer(attribute_block):
        content = " ".join(match.group(1).split())
        for part in split_attribute_list(content):
            attributes.append(part)
    return attributes


def split_attribute_list(content: str) -> list[str]:
    parts: list[str] = []
    start = 0
    depth = 0
    in_string = False
    i = 0
    while i < len(content):
        char = content[i]
        if char == '"' and (i == 0 or content[i - 1] != "\\"):
            in_string = not in_string
        elif not in_string:
            if char == "(":
                depth += 1
            elif char == ")" and depth:
                depth -= 1
            elif char == "," and depth == 0:
                parts.append(content[start:i].strip())
                start = i + 1
        i += 1
    tail = content[start:].strip()
    if tail:
        parts.append(tail)
    return parts


def attribute_name(attribute: str) -> str:
    name = attribute.split("(", 1)[0].strip()
    name = name.rsplit(".", 1)[-1]
    return name.removesuffix("Attribute")


def categories_from_attributes(attributes: list[str]) -> list[str]:
    categories: list[str] = []
    for attribute in attributes:
        for category in CATEGORY_RE.findall(attribute):
            if category not in categories:
                categories.append(category)
    return categories


def inventory_tests(repo_root: Path) -> list[TestIdentity]:
    package_tests = repo_root / "Packages/com.staples.asm-lite/Tests"
    if not package_tests.exists():
        return []
    rows: list[TestIdentity] = []
    for path in sorted(package_tests.rglob("*.cs")):
        if RUST_OVERLAY_EXCLUDE.removesuffix("/**") in normalize_slashes(path.relative_to(repo_root)):
            continue
        source = path.read_text(encoding="utf-8-sig")
        rel = normalize_slashes(path.relative_to(repo_root))
        rows.extend(inventory_tests_in_source(rel, source))
    return sorted(rows, key=lambda row: row.key)


def inventory_tests_in_source(rel: str, source: str) -> list[TestIdentity]:
    rows: list[TestIdentity] = []
    pending_attrs: list[str] = []
    current_class = ""
    current_class_attrs: list[str] = []
    class_depth = -1
    brace_depth = 0
    for line_number, line in enumerate(source.splitlines(), start=1):
        stripped = line.strip()
        class_match = CLASS_RE.match(line)
        if class_match:
            current_class = class_match.group("name")
            current_class_attrs = pending_attrs + split_attributes(class_match.group("attrs"))
            class_depth = brace_depth + line.count("{") - line.count("}")
            pending_attrs = []
        elif stripped.startswith("["):
            pending_attrs.extend(split_attributes(stripped))
        elif current_class and pending_attrs:
            method_match = METHOD_LINE_RE.match(stripped)
            if method_match:
                method_attrs = pending_attrs
                method_attr_names = {attribute_name(attribute) for attribute in method_attrs}
                if method_attr_names.intersection(TEST_ATTRIBUTE_NAMES):
                    all_attrs = current_class_attrs + method_attrs
                    rows.append(TestIdentity(
                        file=rel,
                        class_name=current_class,
                        method=method_match.group("method"),
                        line=line_number,
                        attributes=tuple(all_attrs),
                        categories=tuple(categories_from_attributes(all_attrs)),
                    ))
            pending_attrs = []
        elif stripped and not stripped.startswith("//"):
            pending_attrs = []

        brace_depth += line.count("{") - line.count("}")
        if current_class and class_depth >= 0 and brace_depth < class_depth:
            current_class = ""
            current_class_attrs = []
            class_depth = -1
    return rows


def default_human_fields(identity: TestIdentity) -> dict[str, Any]:
    fields: dict[str, Any] = {
        "lane": PLACEHOLDER["lane"],
        "headlessViability": PLACEHOLDER["headlessViability"],
        "honesty": PLACEHOLDER["honesty"],
        "recommendation": PLACEHOLDER["recommendation"],
        "publicBehaviorClaim": humanize_method(identity.method),
        "fixtureDependencies": [],
        "assetSceneMutations": "review",
        "externalProcessFilesystemEnvUsage": "review",
        "runnerFilter": f"{identity.class_name}.{identity.method}",
        "notes": "",
    }
    classify_mechanical(identity, fields)
    return fields


def classify_mechanical(identity: TestIdentity, fields: dict[str, Any]) -> None:
    categories = set(identity.categories)
    file_name = Path(identity.file).name
    if "Manual" in categories:
        fields.update({
            "lane": "manual",
            "headlessViability": "no",
            "recommendation": "manual-review",
        })
    if "VisibleEditorAutomation" in categories:
        fields.update({
            "lane": "visible-manual",
            "headlessViability": "no",
            "recommendation": "visible-manual-review",
        })
    if "Integration" in categories:
        fields.update({
            "lane": "integration-headless",
            "headlessViability": "review",
            "recommendation": "default-ci-review",
        })
    if file_name in SMOKE_PROTOCOL_FILES:
        fields.update({
            "lane": "smoke-protocol-headless",
            "headlessViability": "yes",
            "recommendation": "default-ci",
        })
    if file_name in SMOKE_OVERLAY_HOST_FILES:
        fields.update({
            "lane": "smoke-overlay-host-headless",
            "headlessViability": "yes",
            "recommendation": "default-ci",
        })


def humanize_method(method: str) -> str:
    name = re.sub(r"^[A-Z]\d+[a-z]?_", "", method)
    name = name.replace("_", " ")
    name = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", " ", name)
    return " ".join(name.split())


def identity_to_row(identity: TestIdentity, previous: dict[str, Any] | None) -> dict[str, Any]:
    row: dict[str, Any] = {
        "file": identity.file,
        "class": identity.class_name,
        "method": identity.method,
        "line": identity.line,
        "attributes": list(identity.attributes),
        "categories": list(identity.categories),
    }
    if previous is None:
        row.update(default_human_fields(identity))
    else:
        defaults = default_human_fields(identity)
        for field in HUMAN_FIELDS:
            row[field] = previous.get(field, defaults[field])
    return row


def expected_document(rows: list[dict[str, Any]]) -> dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "scope": {
            "description": "Unity C# NUnit/EditMode tests inventoried under the ASM-Lite package tests tree.",
            "include": [DEFAULT_INCLUDE],
            "exclude": [RUST_OVERLAY_EXCLUDE],
            "sort": ["file", "class", "method"],
        },
        "tests": rows,
    }


def load_ledger(path: Path) -> dict[str, Any]:
    try:
        with path.open(encoding="utf-8") as handle:
            document = json.load(handle)
    except FileNotFoundError:
        return {"schemaVersion": SCHEMA_VERSION, "scope": {}, "tests": []}
    except json.JSONDecodeError as exc:
        raise ValueError(f"invalid JSON in {path}: {exc}") from exc
    if not isinstance(document, dict):
        raise ValueError("ledger root must be a JSON object")
    if not isinstance(document.get("tests"), list):
        raise ValueError("ledger must contain a tests array")
    return document


def row_key(row: dict[str, Any]) -> tuple[str, str, str]:
    return (str(row.get("file", "")), str(row.get("class", "")), str(row.get("method", "")))


def build_updated_document(repo_root: Path, ledger_path: Path) -> dict[str, Any]:
    previous_document = load_ledger(ledger_path)
    previous_rows = {row_key(row): row for row in previous_document.get("tests", []) if isinstance(row, dict)}
    rows = [identity_to_row(identity, previous_rows.get(identity.key)) for identity in inventory_tests(repo_root)]
    return expected_document(rows)


def write_ledger(path: Path, document: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def drift_messages(actual: dict[str, Any], expected: dict[str, Any]) -> list[str]:
    messages: list[str] = []
    actual_rows = {row_key(row): row for row in actual.get("tests", []) if isinstance(row, dict)}
    expected_rows = {row_key(row): row for row in expected.get("tests", []) if isinstance(row, dict)}
    for key in sorted(expected_rows.keys() - actual_rows.keys()):
        messages.append(f"missing test row: {format_key(key)}")
    for key in sorted(actual_rows.keys() - expected_rows.keys()):
        messages.append(f"stale test row: {format_key(key)}")
    for key in sorted(expected_rows.keys() & actual_rows.keys()):
        actual_row = actual_rows[key]
        expected_row = expected_rows[key]
        for field in ("line", "attributes", "categories"):
            if actual_row.get(field) != expected_row.get(field):
                messages.append(f"generated field drift for {format_key(key)}: {field}")
    if actual.get("schemaVersion") != SCHEMA_VERSION:
        messages.append(f"schemaVersion must be {SCHEMA_VERSION}")
    if actual.get("scope") != expected.get("scope"):
        messages.append("scope metadata drift")
    actual_order = [row_key(row) for row in actual.get("tests", []) if isinstance(row, dict)]
    expected_order = [row_key(row) for row in expected.get("tests", []) if isinstance(row, dict)]
    if actual_order != expected_order:
        messages.append("tests array is not sorted by file/class/method")
    return messages


def placeholder_messages(document: dict[str, Any]) -> list[str]:
    messages: list[str] = []
    for row in document.get("tests", []):
        if not isinstance(row, dict):
            messages.append("test row is not an object")
            continue
        key = format_key(row_key(row))
        for field, placeholder_value in PLACEHOLDER.items():
            if row.get(field) == placeholder_value:
                messages.append(f"placeholder classification for {key}: {field}={placeholder_value}")
        for field in ("assetSceneMutations", "externalProcessFilesystemEnvUsage"):
            if row.get(field) == "review":
                messages.append(f"placeholder classification for {key}: {field}=review")
    return messages


def format_key(key: tuple[str, str, str]) -> str:
    file, class_name, method = key
    return f"{file}::{class_name}.{method}"


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo-root", type=Path, default=repo_root_from_script(), help="Repository root to inventory.")
    parser.add_argument("--ledger", type=Path, default=None, help="Ledger path. Defaults under the repository root.")
    parser.add_argument("--update", action="store_true", help="Rewrite the ledger with current generated inventory rows.")
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    args = parse_args(sys.argv[1:] if argv is None else argv)
    repo_root = args.repo_root.resolve()
    ledger_path = args.ledger.resolve() if args.ledger is not None else repo_root / LEDGER_PATH
    try:
        expected = build_updated_document(repo_root, ledger_path)
        if args.update:
            before = load_ledger(ledger_path)
            write_ledger(ledger_path, expected)
            changes = drift_messages(before, expected)
            print(f"suite ledger updated: {len(expected['tests'])} Unity C# test methods inventoried at {ledger_path.relative_to(repo_root) if ledger_path.is_relative_to(repo_root) else ledger_path}")
            if changes:
                print(f"generated inventory changes: {len(changes)}")
            return 0
        actual = load_ledger(ledger_path)
    except ValueError as exc:
        return fail(str(exc))
    drift = drift_messages(actual, expected)
    placeholders = placeholder_messages(actual)
    if drift:
        for message in drift[:25]:
            print(f"ledger drift: {message}", file=sys.stderr)
        if len(drift) > 25:
            print(f"ledger drift: ... {len(drift) - 25} more", file=sys.stderr)
        print("Run Tools/ci/test-suites/validate-suite-ledger.py --update after reviewing inventory changes.", file=sys.stderr)
    if placeholders:
        for message in placeholders[:25]:
            print(message, file=sys.stderr)
        if len(placeholders) > 25:
            print(f"placeholder classification: ... {len(placeholders) - 25} more", file=sys.stderr)
    if drift or placeholders:
        return 1
    print(f"suite ledger ok: {len(actual['tests'])} Unity C# test methods classified")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
