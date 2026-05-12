#!/usr/bin/env python3
"""Poll GitHub checks/statuses for exact-SHA required release gates."""

from __future__ import annotations

import argparse
import json
import os
import socket
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path
from typing import Any


EXPECTED_REQUIRED_KEYS = ("compile", "lint", "test")
PENDING_STATES = {
    "queued",
    "in_progress",
    "pending",
    "requested",
    "waiting",
}


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Fail-closed exact-SHA checker for required compile/lint/test status aliases."
        )
    )
    parser.add_argument("--repo", help="owner/repo to query")
    parser.add_argument("--sha", help="Exact commit SHA to evaluate")
    parser.add_argument(
        "--required-checks",
        required=True,
        help="Path to required-check alias JSON (must contain compile/lint/test keys)",
    )
    parser.add_argument(
        "--github-token-env",
        help="Environment variable name containing GitHub token",
    )
    parser.add_argument(
        "--max-wait-seconds",
        type=int,
        default=1800,
        help="Maximum polling duration before failing closed",
    )
    parser.add_argument(
        "--poll-interval-seconds",
        type=int,
        default=20,
        help="Polling interval in seconds while checks are pending",
    )
    parser.add_argument(
        "--validate-config-only",
        action="store_true",
        help="Validate required-check configuration and exit",
    )
    return parser.parse_args()


def fail(message: str) -> "NoReturn":
    print(f"::error::{message}")
    raise SystemExit(1)


def load_required_checks(path: Path) -> dict[str, list[str]]:
    if not path.exists():
        fail(f"Required-checks file not found: {path}")

    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Failed to read required-checks JSON at {path}: {exc}")

    if not isinstance(payload, dict):
        fail("Required-checks JSON root must be an object.")

    payload_keys = tuple(payload.keys())
    expected_set = set(EXPECTED_REQUIRED_KEYS)
    payload_set = set(payload_keys)
    if payload_set != expected_set:
        missing = sorted(expected_set - payload_set)
        extra = sorted(payload_set - expected_set)
        details: list[str] = []
        if missing:
            details.append(f"missing keys: {missing}")
        if extra:
            details.append(f"unexpected keys: {extra}")
        fail(
            "Required-checks JSON must contain exactly compile/lint/test keys; "
            + "; ".join(details)
        )

    normalized: dict[str, list[str]] = {}
    for key in EXPECTED_REQUIRED_KEYS:
        aliases_raw = payload.get(key)
        if not isinstance(aliases_raw, list) or len(aliases_raw) == 0:
            fail(f"Required-checks key '{key}' must map to a non-empty list of aliases.")

        aliases: list[str] = []
        for index, value in enumerate(aliases_raw):
            if not isinstance(value, str) or not value.strip():
                fail(
                    f"Required-checks key '{key}' contains invalid alias at index {index}; "
                    "expected non-empty string."
                )
            aliases.append(value.strip())

        normalized[key] = aliases

    return normalized


def require_nonempty(value: str | None, name: str) -> str:
    text = (value or "").strip()
    if not text:
        fail(f"{name} is required unless --validate-config-only is used.")
    return text


def github_get_json(repo: str, token: str, path: str) -> Any:
    url = f"https://api.github.com/repos/{repo}{path}"
    request = urllib.request.Request(
        url,
        headers={
            "Authorization": f"Bearer {token}",
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )

    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            body = response.read().decode("utf-8")
    except urllib.error.HTTPError as exc:
        details = exc.read().decode("utf-8", errors="replace")
        fail(f"GitHub API HTTP {exc.code} for {path}: {details[:800]}")
    except (urllib.error.URLError, socket.timeout, TimeoutError) as exc:
        fail(f"GitHub API timeout or transport error for {path}: {exc}")

    try:
        return json.loads(body)
    except json.JSONDecodeError as exc:
        fail(f"Malformed JSON response for {path}: {exc}")


def read_observed_checks(repo: str, sha: str, token: str) -> dict[str, str]:
    status_payload = github_get_json(repo, token, f"/commits/{sha}/status")
    checks_payload = github_get_json(repo, token, f"/commits/{sha}/check-runs?per_page=100")

    statuses = status_payload.get("statuses") if isinstance(status_payload, dict) else None
    if not isinstance(statuses, list):
        fail("Malformed commit status payload: missing statuses array.")

    check_runs = checks_payload.get("check_runs") if isinstance(checks_payload, dict) else None
    if not isinstance(check_runs, list):
        fail("Malformed check-runs payload: missing check_runs array.")

    observed: dict[str, str] = {}

    for entry in statuses:
        if not isinstance(entry, dict):
            continue
        context = entry.get("context")
        state = entry.get("state")
        if isinstance(context, str) and isinstance(state, str):
            observed[context] = state

    for run in check_runs:
        if not isinstance(run, dict):
            continue
        name = run.get("name")
        conclusion = run.get("conclusion")
        status = run.get("status")
        if not isinstance(name, str):
            continue
        if isinstance(conclusion, str):
            observed[name] = conclusion
        elif isinstance(status, str):
            observed[name] = status

    return observed


def classify(
    required_checks: dict[str, list[str]],
    observed: dict[str, str],
) -> tuple[list[str], list[str]]:
    pending: list[str] = []
    blocking: list[str] = []

    for key, aliases in required_checks.items():
        matched_aliases: list[tuple[str, str]] = []

        for alias in aliases:
            value = observed.get(alias)
            if isinstance(value, str):
                matched_aliases.append((alias, value))

        if not matched_aliases:
            pending.append(f"waiting for required check for {key}: expected one of {aliases}")
            continue

        normalized = [(alias, raw, raw.strip().lower()) for alias, raw in matched_aliases]

        if any(state == "success" for _, _, state in normalized):
            continue

        terminal_non_success = next(
            ((alias, raw) for alias, raw, state in normalized if state not in PENDING_STATES),
            None,
        )
        if terminal_non_success is not None:
            alias, raw = terminal_non_success
            blocking.append(f"required check {alias} is {raw}, expected success")
            continue

        alias, raw = matched_aliases[0]
        pending.append(f"required check {alias} is {raw}")

    return pending, blocking


def run_poll_loop(
    repo: str,
    sha: str,
    token: str,
    required_checks: dict[str, list[str]],
    max_wait_seconds: int,
    poll_interval_seconds: int,
) -> None:
    print(f"Target SHA: {sha}")
    print("Required checks:")
    for key in EXPECTED_REQUIRED_KEYS:
        aliases = required_checks[key]
        print(f"  {key}: {aliases[0]}")

    deadline = time.time() + max_wait_seconds
    attempt = 0

    while True:
        attempt += 1
        observed = read_observed_checks(repo, sha, token)

        print(f"Check poll attempt #{attempt}")
        print("Observed check conclusions:")
        if observed:
            for name in sorted(observed.keys()):
                print(f"  {name}: {observed[name]}")
        else:
            print("  <none>")

        pending, blocking = classify(required_checks, observed)

        if not pending and not blocking:
            print("Gate passed: required compile, lint, and test checks are success for target SHA.")
            return

        if blocking:
            print("::error::Release gate blocked.")
            for reason in blocking:
                print(f"::error::{reason}")
            raise SystemExit(1)

        remaining = int(deadline - time.time())
        if remaining <= 0:
            print("::error::Release gate timed out waiting for required checks.")
            for reason in pending:
                print(f"::error::{reason}")
            raise SystemExit(1)

        sleep_for = poll_interval_seconds if remaining > poll_interval_seconds else remaining
        print(f"Pending checks still running; waiting {sleep_for}s ({remaining}s remaining).")
        time.sleep(sleep_for)


def main() -> int:
    args = parse_args()

    if args.max_wait_seconds <= 0:
        fail("--max-wait-seconds must be greater than zero.")
    if args.poll_interval_seconds <= 0:
        fail("--poll-interval-seconds must be greater than zero.")

    required_checks = load_required_checks(Path(args.required_checks))

    if args.validate_config_only:
        print(
            f"Required-check configuration is valid: {args.required_checks} "
            f"(keys: {', '.join(EXPECTED_REQUIRED_KEYS)})"
        )
        return 0

    repo = require_nonempty(args.repo, "--repo")
    sha = require_nonempty(args.sha, "--sha")
    token_env_name = require_nonempty(args.github_token_env, "--github-token-env")
    token = require_nonempty(os.environ.get(token_env_name), token_env_name)

    run_poll_loop(
        repo=repo,
        sha=sha,
        token=token,
        required_checks=required_checks,
        max_wait_seconds=args.max_wait_seconds,
        poll_interval_seconds=args.poll_interval_seconds,
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
