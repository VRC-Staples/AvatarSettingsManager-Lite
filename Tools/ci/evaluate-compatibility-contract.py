#!/usr/bin/env python3
"""Evaluate ASM-Lite compatibility contract against repository source-of-truth files."""

from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import asdict, dataclass, field
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


REASON_DESCRIPTIONS = {
    "COMP-101": "Compatibility contract file is missing.",
    "COMP-102": "Compatibility contract or source metadata is malformed.",
    "COMP-103": "Unity strict pin mismatch.",
    "COMP-104": "VRChat SDK minimum floor mismatch.",
    "COMP-105": "VRCFury minimum floor mismatch.",
    "COMP-106": "Generated compatibility summary is missing or stale.",
}

ALWAYS_FAIL_CODES = {"COMP-101", "COMP-102"}
POLICY_VIOLATION_CODES = {"COMP-103", "COMP-104", "COMP-105", "COMP-106"}


@dataclass
class Reason:
    code: str
    message: str
    details: dict[str, Any] = field(default_factory=dict)


@dataclass
class Comparison:
    component: str
    policy: str
    contract_value: str
    observed_value: str
    source_path: str


@dataclass
class EvaluationReport:
    mode: str
    contract_path: str
    generated_at_utc: str
    verdict: str
    policy_compliant: bool
    should_fail: bool
    reasons: list[Reason]
    comparisons: list[Comparison]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Evaluate compatibility.contract.json using deterministic COMP-1xx reason codes."
        )
    )
    parser.add_argument("--mode", required=True, choices=("ci", "release"))
    parser.add_argument("--contract", required=True, help="Path to compatibility contract JSON file")
    parser.add_argument("--output-json", required=True, help="Path to write machine-readable evaluation JSON")
    return parser.parse_args()


def normalize_newlines(value: str) -> str:
    return value.replace("\r\n", "\n").replace("\r", "\n")


def parse_semver(version: str) -> tuple[int, ...]:
    text = version.strip()
    if not text:
        raise ValueError("empty version")
    if not re.fullmatch(r"\d+(?:\.\d+)*", text):
        raise ValueError(f"not a numeric dotted version: '{version}'")
    return tuple(int(part) for part in text.split("."))


def compare_semver(left: str, right: str) -> int:
    left_parts = parse_semver(left)
    right_parts = parse_semver(right)
    width = max(len(left_parts), len(right_parts))
    left_norm = left_parts + (0,) * (width - len(left_parts))
    right_norm = right_parts + (0,) * (width - len(right_parts))
    if left_norm == right_norm:
        return 0
    return 1 if left_norm > right_norm else -1


def resolve_path(path_value: str, base_dir: Path | None = None) -> Path:
    candidate = Path(path_value)
    if candidate.is_absolute():
        return candidate
    root = base_dir if base_dir is not None else Path.cwd()
    return root / candidate


def load_json_file(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8"))


def extract_unity_editor_version(path: Path) -> str:
    content = path.read_text(encoding="utf-8")
    match = re.search(r"^m_EditorVersion:\s*(.+)$", normalize_newlines(content), flags=re.MULTILINE)
    if not match:
        raise ValueError("m_EditorVersion not found")
    return match.group(1).strip()


def parse_min_dependency(requirement: str, package_name: str) -> str:
    value = requirement.strip()
    if not value.startswith(">="):
        raise ValueError(
            f"{package_name} dependency must use minimum-floor syntax '>=x.y.z', got '{value}'"
        )
    floor = value[2:].strip()
    parse_semver(floor)
    return floor


def validate_contract_schema(contract: Any) -> None:
    if not isinstance(contract, dict):
        raise ValueError("contract root must be a JSON object")

    required_top_level = ["schemaVersion", "compatibility", "sources", "observedBaselines"]
    for key in required_top_level:
        if key not in contract:
            raise ValueError(f"missing top-level key '{key}'")

    compatibility = contract["compatibility"]
    if not isinstance(compatibility, dict):
        raise ValueError("compatibility must be an object")

    for key in ("unity", "vrchatSdk", "vrcfury"):
        if key not in compatibility or not isinstance(compatibility[key], dict):
            raise ValueError(f"compatibility.{key} must be an object")

    unity = compatibility["unity"]
    if unity.get("policy") != "strict":
        raise ValueError("compatibility.unity.policy must be 'strict'")
    if not isinstance(unity.get("version"), str) or not unity.get("version", "").strip():
        raise ValueError("compatibility.unity.version must be a non-empty string")

    for key in ("vrchatSdk", "vrcfury"):
        entry = compatibility[key]
        if entry.get("policy") != "minimum":
            raise ValueError(f"compatibility.{key}.policy must be 'minimum'")
        min_version = entry.get("minVersion")
        if not isinstance(min_version, str) or not min_version.strip():
            raise ValueError(f"compatibility.{key}.minVersion must be a non-empty string")
        parse_semver(min_version)

    sources = contract["sources"]
    if not isinstance(sources, dict):
        raise ValueError("sources must be an object")

    for required_source in (
        "unityProjectVersion",
        "packageManifest",
        "shadowVrchatSdk",
        "compatibilitySummary",
    ):
        if required_source not in sources or not isinstance(sources[required_source], dict):
            raise ValueError(f"sources.{required_source} must be an object")
        source_path = sources[required_source].get("path")
        if not isinstance(source_path, str) or not source_path.strip():
            raise ValueError(f"sources.{required_source}.path must be a non-empty string")


def render_compatibility_markdown(contract: dict[str, Any], contract_path: str) -> str:
    compatibility = contract["compatibility"]
    sources = contract["sources"]
    observed = contract["observedBaselines"]

    rows = [
        (
            "Unity",
            compatibility["unity"]["policy"],
            compatibility["unity"]["version"],
            compatibility["unity"].get("sourceRef", "unityProjectVersion"),
        ),
        (
            "VRChat SDK (Avatars)",
            compatibility["vrchatSdk"]["policy"],
            compatibility["vrchatSdk"]["minVersion"],
            compatibility["vrchatSdk"].get("sourceRef", "packageManifest"),
        ),
        (
            "VRCFury",
            compatibility["vrcfury"]["policy"],
            compatibility["vrcfury"]["minVersion"],
            compatibility["vrcfury"].get("sourceRef", "packageManifest"),
        ),
    ]

    lines: list[str] = []
    lines.append("# Compatibility Contract Summary")
    lines.append("")
    lines.append(
        f"Generated from `{contract_path}`. Do not edit this file manually; regenerate from the contract."
    )
    lines.append("")
    lines.append("## Policy Semantics")
    lines.append("")
    lines.append("| Component | Policy | Contract Value | Source Ref |")
    lines.append("|---|---|---|---|")
    for component, policy, value, source_ref in rows:
        lines.append(f"| {component} | {policy} | `{value}` | `{source_ref}` |")

    lines.append("")
    lines.append("## Source Provenance")
    lines.append("")
    lines.append("| Source Ref | Path | Kind |")
    lines.append("|---|---|---|")
    for source_ref in sorted(sources.keys()):
        source = sources[source_ref]
        lines.append(
            f"| `{source_ref}` | `{source.get('path', '')}` | `{source.get('kind', 'unknown')}` |"
        )

    lines.append("")
    lines.append("## Observed Baselines")
    lines.append("")
    lines.append("| Baseline | Value | Source Ref |")
    lines.append("|---|---|---|")
    for baseline_name in sorted(observed.keys()):
        baseline = observed[baseline_name]
        value = baseline.get("value", "") if isinstance(baseline, dict) else ""
        source_ref = baseline.get("sourceRef", "") if isinstance(baseline, dict) else ""
        lines.append(f"| `{baseline_name}` | `{value}` | `{source_ref}` |")

    lines.append("")
    lines.append("## COMP-1xx Reason Code Legend")
    lines.append("")
    lines.append("| Code | Meaning |")
    lines.append("|---|---|")
    for code in sorted(REASON_DESCRIPTIONS.keys()):
        lines.append(f"| `{code}` | {REASON_DESCRIPTIONS[code]} |")

    lines.append("")
    return "\n".join(lines)


def emit_annotation(mode: str, reason: Reason) -> None:
    annotation_level = "warning" if mode == "ci" else "error"
    print(f"::{annotation_level}::{reason.code} {reason.message}", file=sys.stderr)


def build_markdown_report(report: EvaluationReport) -> str:
    lines: list[str] = []
    lines.append("## Compatibility Contract Evaluation")
    lines.append("")
    lines.append(f"- Mode: `{report.mode}`")
    lines.append(f"- Contract: `{report.contract_path}`")
    lines.append(f"- Verdict: `{report.verdict}`")
    lines.append("")

    lines.append("### Compared Values")
    lines.append("")
    lines.append("| Component | Policy | Contract | Observed | Source |")
    lines.append("|---|---|---|---|---|")
    for comparison in report.comparisons:
        lines.append(
            "| {component} | {policy} | `{contract}` | `{observed}` | `{source}` |".format(
                component=comparison.component,
                policy=comparison.policy,
                contract=comparison.contract_value,
                observed=comparison.observed_value,
                source=comparison.source_path,
            )
        )
    lines.append("")

    if report.reasons:
        lines.append("### Reasons")
        lines.append("")
        for reason in report.reasons:
            lines.append(f"- `{reason.code}`: {reason.message}")
    else:
        lines.append("### Reasons")
        lines.append("")
        lines.append("- None")

    lines.append("")
    return "\n".join(lines)


def evaluate(contract_path: Path, mode: str) -> EvaluationReport:
    reasons: list[Reason] = []
    comparisons: list[Comparison] = []

    contract_display_path = str(contract_path)

    if not contract_path.exists():
        reasons.append(
            Reason(
                code="COMP-101",
                message=f"Contract file not found: {contract_display_path}",
                details={"contractPath": contract_display_path},
            )
        )
        verdict = "error"
        return EvaluationReport(
            mode=mode,
            contract_path=contract_display_path,
            generated_at_utc=datetime.now(timezone.utc).isoformat(),
            verdict=verdict,
            policy_compliant=False,
            should_fail=True,
            reasons=reasons,
            comparisons=comparisons,
        )

    try:
        contract = load_json_file(contract_path)
        validate_contract_schema(contract)

        sources = contract["sources"]
        contract_dir = contract_path.parent
        source_base_dir = (
            contract_dir.parent if contract_dir.name == ".planning" else contract_dir
        )

        unity_path = resolve_path(sources["unityProjectVersion"]["path"], source_base_dir)
        package_manifest_path = resolve_path(sources["packageManifest"]["path"], source_base_dir)
        shadow_vrchat_manifest_path = resolve_path(sources["shadowVrchatSdk"]["path"], source_base_dir)
        summary_path = resolve_path(sources["compatibilitySummary"]["path"], source_base_dir)

        if not unity_path.exists():
            raise ValueError(f"missing source file: {unity_path}")
        if not package_manifest_path.exists():
            raise ValueError(f"missing source file: {package_manifest_path}")
        if not shadow_vrchat_manifest_path.exists():
            raise ValueError(f"missing source file: {shadow_vrchat_manifest_path}")

        unity_expected = contract["compatibility"]["unity"]["version"].strip()
        unity_observed = extract_unity_editor_version(unity_path)
        comparisons.append(
            Comparison(
                component="Unity",
                policy="strict",
                contract_value=unity_expected,
                observed_value=unity_observed,
                source_path=str(unity_path),
            )
        )
        if unity_expected != unity_observed:
            reasons.append(
                Reason(
                    code="COMP-103",
                    message=(
                        "Unity strict pin mismatch: "
                        f"contract requires {unity_expected}, source reports {unity_observed}."
                    ),
                    details={
                        "expected": unity_expected,
                        "observed": unity_observed,
                        "sourcePath": str(unity_path),
                    },
                )
            )

        package_manifest = load_json_file(package_manifest_path)
        if not isinstance(package_manifest, dict):
            raise ValueError("package manifest must be a JSON object")
        dependencies = package_manifest.get("vpmDependencies")
        if not isinstance(dependencies, dict):
            raise ValueError("package manifest vpmDependencies must be an object")

        vrchat_expected = contract["compatibility"]["vrchatSdk"]["minVersion"].strip()
        vrchat_declared = dependencies.get("com.vrchat.avatars")
        if not isinstance(vrchat_declared, str):
            raise ValueError("package manifest missing vpmDependencies.com.vrchat.avatars string")
        vrchat_observed = parse_min_dependency(vrchat_declared, "com.vrchat.avatars")
        comparisons.append(
            Comparison(
                component="VRChat SDK",
                policy="minimum",
                contract_value=vrchat_expected,
                observed_value=vrchat_observed,
                source_path=str(package_manifest_path),
            )
        )
        if compare_semver(vrchat_observed, vrchat_expected) != 0:
            reasons.append(
                Reason(
                    code="COMP-104",
                    message=(
                        "VRChat SDK floor mismatch: "
                        f"contract requires >= {vrchat_expected}, source declares >= {vrchat_observed}."
                    ),
                    details={
                        "expected": vrchat_expected,
                        "observed": vrchat_observed,
                        "sourcePath": str(package_manifest_path),
                    },
                )
            )

        vrcfury_expected = contract["compatibility"]["vrcfury"]["minVersion"].strip()
        vrcfury_declared = dependencies.get("com.vrcfury.vrcfury")
        if not isinstance(vrcfury_declared, str):
            raise ValueError("package manifest missing vpmDependencies.com.vrcfury.vrcfury string")
        vrcfury_observed = parse_min_dependency(vrcfury_declared, "com.vrcfury.vrcfury")
        comparisons.append(
            Comparison(
                component="VRCFury",
                policy="minimum",
                contract_value=vrcfury_expected,
                observed_value=vrcfury_observed,
                source_path=str(package_manifest_path),
            )
        )
        if compare_semver(vrcfury_observed, vrcfury_expected) != 0:
            reasons.append(
                Reason(
                    code="COMP-105",
                    message=(
                        "VRCFury floor mismatch: "
                        f"contract requires >= {vrcfury_expected}, source declares >= {vrcfury_observed}."
                    ),
                    details={
                        "expected": vrcfury_expected,
                        "observed": vrcfury_observed,
                        "sourcePath": str(package_manifest_path),
                    },
                )
            )

        shadow_manifest = load_json_file(shadow_vrchat_manifest_path)
        shadow_version = ""
        if isinstance(shadow_manifest, dict):
            shadow_version = str(shadow_manifest.get("version", "")).strip()
        comparisons.append(
            Comparison(
                component="VRChat SDK (shadow baseline)",
                policy="observed",
                contract_value=str(
                    contract.get("observedBaselines", {})
                    .get("shadowVrchatSdkVersion", {})
                    .get("value", "")
                ),
                observed_value=shadow_version,
                source_path=str(shadow_vrchat_manifest_path),
            )
        )

        try:
            contract_ref = str(contract_path.relative_to(source_base_dir))
        except ValueError:
            contract_ref = str(contract_path)

        expected_summary = render_compatibility_markdown(contract, contract_ref)
        summary_exists = summary_path.exists()
        summary_matches = False
        if summary_exists:
            observed_summary = normalize_newlines(summary_path.read_text(encoding="utf-8"))
            summary_matches = observed_summary == normalize_newlines(expected_summary)

        comparisons.append(
            Comparison(
                component="Compatibility summary",
                policy="generated-from-contract",
                contract_value="generated content",
                observed_value="fresh" if summary_matches else "stale-or-missing",
                source_path=str(summary_path),
            )
        )

        if not summary_exists or not summary_matches:
            reasons.append(
                Reason(
                    code="COMP-106",
                    message=(
                        "Generated compatibility summary is stale or missing. "
                        "Regenerate compatibility summary from the contract."
                    ),
                    details={
                        "summaryPath": str(summary_path),
                        "exists": summary_exists,
                        "matchesGenerated": summary_matches,
                    },
                )
            )

    except json.JSONDecodeError as exc:
        reasons.append(
            Reason(
                code="COMP-102",
                message=f"Malformed JSON while evaluating compatibility contract: {exc}",
                details={"contractPath": contract_display_path},
            )
        )
    except (KeyError, TypeError, ValueError, OSError) as exc:
        reasons.append(
            Reason(
                code="COMP-102",
                message=f"Malformed compatibility schema or source data: {exc}",
                details={"contractPath": contract_display_path},
            )
        )

    codes = {reason.code for reason in reasons}
    has_always_fail = any(code in ALWAYS_FAIL_CODES for code in codes)
    has_policy_violation = any(code in POLICY_VIOLATION_CODES for code in codes)

    should_fail = has_always_fail or (mode == "release" and has_policy_violation)
    if should_fail:
        verdict = "fail"
    elif has_policy_violation:
        verdict = "warn"
    else:
        verdict = "pass"

    return EvaluationReport(
        mode=mode,
        contract_path=contract_display_path,
        generated_at_utc=datetime.now(timezone.utc).isoformat(),
        verdict=verdict,
        policy_compliant=not has_policy_violation and not has_always_fail,
        should_fail=should_fail,
        reasons=reasons,
        comparisons=comparisons,
    )


def write_json_report(path: Path, report: EvaluationReport) -> None:
    payload = {
        "mode": report.mode,
        "contractPath": report.contract_path,
        "generatedAtUtc": report.generated_at_utc,
        "verdict": report.verdict,
        "policyCompliant": report.policy_compliant,
        "shouldFail": report.should_fail,
        "reasonCodes": [reason.code for reason in report.reasons],
        "reasons": [asdict(reason) for reason in report.reasons],
        "comparisons": [asdict(comparison) for comparison in report.comparisons],
    }
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")


def main() -> int:
    args = parse_args()
    contract_path = Path(args.contract)
    output_json_path = Path(args.output_json)

    report = evaluate(contract_path, args.mode)

    for reason in report.reasons:
        emit_annotation(args.mode, reason)

    markdown = build_markdown_report(report)
    print(markdown)

    try:
        write_json_report(output_json_path, report)
    except OSError as exc:
        print(f"::error::Failed to write JSON report: {exc}", file=sys.stderr)
        return 2

    return 1 if report.should_fail else 0


if __name__ == "__main__":
    sys.exit(main())
