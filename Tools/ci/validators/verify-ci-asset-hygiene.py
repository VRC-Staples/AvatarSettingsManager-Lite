#!/usr/bin/env python3
"""Verify CI never tracks local Unity project payloads or generated outputs."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


ALLOWED_CI_ASSET_PATHS = {
    "Tools/ci/unity-project/Assets/.gitkeep",
}

BLOCKED_TRACKED_PATTERNS = (
    "Test Project/**",
    "Tools/ci/unity-project/Assets/**",
    "Tools/ci/unity-project/bin/**",
    "Tools/ci/unity-project/obj/**",
    "Tools/ci/unity-project/Library/**",
    "Tools/ci/unity-project/Temp/**",
    "Tools/ci/unity-project/Logs/**",
    "Tools/ci/unity-project/UserSettings/**",
    "Tools/ci/unity-project/*.csproj",
)

RECOVERY_HINT = "\n".join(
    (
        "Remove generated/local-project payloads from git, then keep only intentional CI fixtures tracked.",
        "Typical cleanup:",
        "  git rm -r --cached -- Tools/ci/unity-project/bin Tools/ci/unity-project/obj",
        "  git rm -r --cached -- 'Test Project' Tools/ci/unity-project/Assets",
        "  git add Tools/ci/unity-project/Assets/.gitkeep",
    )
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Fail when local Unity test assets or generated CI outputs are tracked."
    )
    parser.add_argument(
        "--repo",
        type=Path,
        default=Path.cwd(),
        help="Repository root to check; defaults to the current working directory.",
    )
    return parser.parse_args()


def run_git_ls_files(repo: Path, patterns: tuple[str, ...]) -> list[str]:
    command = ["git", "ls-files", "-z", "--", *patterns]
    try:
        result = subprocess.run(
            command,
            cwd=repo,
            capture_output=True,
            text=False,
            check=False,
        )
    except OSError as exc:
        raise RuntimeError(f"failed to run git ls-files: {exc}") from exc

    if result.returncode != 0:
        stderr = result.stderr.decode("utf-8", errors="replace").strip()
        stdout = result.stdout.decode("utf-8", errors="replace").strip()
        raise RuntimeError(stderr or stdout or "git ls-files failed")

    return [
        entry.decode("utf-8", errors="replace")
        for entry in result.stdout.split(b"\0")
        if entry
    ]


def classify_blocked_path(path: str) -> str | None:
    normalized = path.replace("\\", "/")

    if normalized.startswith("Test Project/"):
        return "local TestUnityProject payload must not be tracked by CI"

    if normalized.startswith("Tools/ci/unity-project/Assets/"):
        if normalized in ALLOWED_CI_ASSET_PATHS:
            return None
        return "CI Unity Assets payload must stay runtime-generated except .gitkeep"

    generated_prefixes = (
        "Tools/ci/unity-project/bin/",
        "Tools/ci/unity-project/obj/",
        "Tools/ci/unity-project/Library/",
        "Tools/ci/unity-project/Temp/",
        "Tools/ci/unity-project/Logs/",
        "Tools/ci/unity-project/UserSettings/",
    )
    if normalized.startswith(generated_prefixes):
        return "CI Unity generated output must not be tracked"

    if normalized.startswith("Tools/ci/unity-project/") and normalized.endswith(".csproj"):
        return "generated CI compile project must not be tracked"

    return None


def blocked_tracked_paths(paths: list[str]) -> list[tuple[str, str]]:
    blocked: list[tuple[str, str]] = []
    for path in paths:
        reason = classify_blocked_path(path)
        if reason:
            blocked.append((path, reason))
    return blocked


def main() -> int:
    args = parse_args()
    repo = args.repo.resolve()

    try:
        tracked_paths = run_git_ls_files(repo, BLOCKED_TRACKED_PATTERNS)
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    blocked = blocked_tracked_paths(tracked_paths)
    if not blocked:
        print("ci-asset-hygiene-ok: no local Unity assets or generated CI outputs are tracked")
        return 0

    print("error: forbidden tracked CI/local Unity paths found:", file=sys.stderr)
    for path, reason in blocked:
        print(f"  {path}: {reason}", file=sys.stderr)
    print("", file=sys.stderr)
    print(RECOVERY_HINT, file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
