#!/usr/bin/env python3
"""Verify C# and Rust smoke protocol registry parity."""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CS_REGISTRY = ROOT / "Packages/com.staples.asm-lite/Tests/Editor/ASMLiteSmokeAutomationContracts.cs"
RS_PROTOCOL = ROOT / "Tools/ci/rust-overlay/src/protocol.rs"
FIXTURES = ROOT / "Tools/ci/smoke/protocol-fixtures"


@dataclass(frozen=True)
class CommandDefinition:
    command_type: str
    payload_field: str
    dispatch_mode: str


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except FileNotFoundError:
        fail(f"missing required file: {path.relative_to(ROOT)}")


def fail(message: str) -> None:
    print(f"smoke protocol parity failed: {message}", file=sys.stderr)
    raise SystemExit(1)


def csharp_command_definitions(source: str) -> list[CommandDefinition]:
    pattern = re.compile(
        r"new\s+ASMLiteSmokeCommandDefinition\(\s*"
        r"(?P<command>ASMLiteSmokeCommandRegistry\.[A-Za-z0-9_]+|[A-Za-z0-9_]+|\"[^\"]+\")\s*,\s*"
        r"(?P<payload>\"[^\"]*\"|string\.Empty)\s*,\s*"
        r"ASMLiteSmokeCommandDispatchMode\.(?P<mode>[A-Za-z0-9_]+)\s*\)",
        re.MULTILINE,
    )
    constants = dict(re.findall(r"internal\s+const\s+string\s+(\w+)\s*=\s*\"([^\"]+)\";", source))
    definitions: list[CommandDefinition] = []
    for match in pattern.finditer(source):
        command = match.group("command")
        if command.startswith("ASMLiteSmokeCommandRegistry."):
            constant_name = command.rsplit(".", 1)[1]
            command_type = constants.get(constant_name)
            if command_type is None:
                fail(f"C# command registry references unknown constant {constant_name}")
        elif command.startswith('"'):
            command_type = command.strip('"')
        else:
            command_type = constants.get(command)
            if command_type is None:
                fail(f"C# command registry references unknown constant {command}")
        definitions.append(
            CommandDefinition(
                command_type=command_type,
                payload_field="" if match.group("payload") == "string.Empty" else match.group("payload").strip('"'),
                dispatch_mode=match.group("mode"),
            )
        )
    if not definitions:
        fail("no C# command definitions found")
    return definitions


def rust_command_definitions(source: str) -> list[CommandDefinition]:
    constants = dict(re.findall(r"pub\s+const\s+(COMMAND_TYPE_\w+)\s*:\s*&str\s*=\s*\"([^\"]+)\";", source))
    block_match = re.search(r"const\s+COMMAND_DEFINITIONS\s*:\s*&\[CommandDefinition\]\s*=\s*&\[(?P<body>.*?)\];", source, re.DOTALL)
    if not block_match:
        fail("Rust COMMAND_DEFINITIONS block not found")
    entry_pattern = re.compile(
        r"CommandDefinition\s*\{\s*"
        r"command_type:\s*(?P<command>COMMAND_TYPE_\w+),\s*"
        r"payload_field_name:\s*(?P<payload>Some\(\"[^\"]+\"\)|None),\s*"
        r"dispatch_mode:\s*CommandDispatchMode::(?P<mode>[A-Za-z0-9_]+),\s*\}",
        re.DOTALL,
    )
    definitions: list[CommandDefinition] = []
    for match in entry_pattern.finditer(block_match.group("body")):
        constant_name = match.group("command")
        command_type = constants.get(constant_name)
        if command_type is None:
            fail(f"Rust command registry references unknown constant {constant_name}")
        payload = match.group("payload")
        payload_field = "" if payload == "None" else payload.removeprefix('Some("').removesuffix('")')
        definitions.append(CommandDefinition(command_type, payload_field, match.group("mode")))
    if not definitions:
        fail("no Rust command definitions found")
    return definitions


def assert_same_definitions(cs: list[CommandDefinition], rs: list[CommandDefinition]) -> None:
    if cs != rs:
        print("C# definitions:", cs, file=sys.stderr)
        print("Rust definitions:", rs, file=sys.stderr)
        fail("C# and Rust command registries differ")


PAYLOAD_FIELDS = {
    "launch-session": "launchSession",
    "run-suite": "runSuite",
    "review-decision": "reviewDecision",
    "abort-run": "abortRun",
    "shutdown-session": "",
}


def fixture_names() -> list[Path]:
    paths = [path for path in FIXTURES.glob("*.json") if path.name in {
        "launch-session.json",
        "run-suite.json",
        "review-decision.json",
        "abort-run.json",
    }]
    if len(paths) != 4:
        fail("expected command fixtures launch-session/run-suite/review-decision/abort-run")
    return sorted(paths)


def assert_fixtures_match_registry(definitions: list[CommandDefinition]) -> None:
    by_type = {definition.command_type: definition for definition in definitions}
    for fixture_path in fixture_names():
        document = json.loads(fixture_path.read_text(encoding="utf-8"))
        command_type = document.get("commandType", "")
        definition = by_type.get(command_type)
        if definition is None:
            fail(f"{fixture_path.name} commandType '{command_type}' is not registered")
        expected_payload = PAYLOAD_FIELDS.get(command_type)
        if expected_payload is None:
            fail(f"{fixture_path.name} commandType '{command_type}' has no payload parity rule")
        if expected_payload and expected_payload not in document:
            fail(f"{fixture_path.name} is missing payload field '{expected_payload}'")
        for payload_field in PAYLOAD_FIELDS.values():
            if payload_field and payload_field != expected_payload and payload_field in document:
                fail(f"{fixture_path.name} includes wrong payload field '{payload_field}'")


def main() -> int:
    cs = csharp_command_definitions(read(CS_REGISTRY))
    rs = rust_command_definitions(read(RS_PROTOCOL))
    assert_same_definitions(cs, rs)
    assert_fixtures_match_registry(cs)
    launch = next(definition for definition in cs if definition.command_type == "launch-session")
    if launch.dispatch_mode != "StartupOnly":
        fail("launch-session must remain StartupOnly to avoid file-poll dispatch drift")
    print(f"smoke protocol parity ok: {len(cs)} command definitions, {len(fixture_names())} shared command fixtures")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
