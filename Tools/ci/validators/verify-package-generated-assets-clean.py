#!/usr/bin/env python3
"""Verify package generated outputs remain clean after test/project sync steps."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


PROTECTED_PATHS = (
    "Packages/com.staples.asm-lite/GeneratedAssets",
    "Packages/com.staples.asm-lite/GeneratedAssets.meta",
    "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab",
    "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab.meta",
)

RECOVERY_COMMANDS = (
    "git restore -- Packages/com.staples.asm-lite/GeneratedAssets "
    "Packages/com.staples.asm-lite/GeneratedAssets.meta "
    "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab "
    "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab.meta\n"
    "python3 /opt/data/skills/software-development/testing/scripts/"
    "asm-lite-refresh-unity-test-project.py"
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Verify generated package asset outputs are clean in git status."
    )
    parser.add_argument(
        "--repo",
        type=Path,
        default=Path.cwd(),
        help="Repository root to check; defaults to the current working directory.",
    )
    return parser.parse_args()


def run_git_status(repo: Path) -> list[str]:
    command = ["git", "status", "--porcelain", "--", *PROTECTED_PATHS]
    try:
        result = subprocess.run(
            command,
            cwd=repo,
            capture_output=True,
            text=True,
            check=False,
        )
    except OSError as exc:
        raise RuntimeError(f"failed to run git status: {exc}") from exc

    if result.returncode != 0:
        message = result.stderr.strip() or result.stdout.strip() or "git status failed"
        raise RuntimeError(message)

    return [line for line in result.stdout.splitlines() if line.strip()]


def porcelain_path(status_line: str) -> str:
    if len(status_line) <= 3:
        return status_line.strip()
    return status_line[3:].strip()


def main() -> int:
    args = parse_args()
    repo = args.repo.resolve()
    try:
        dirty_lines = run_git_status(repo)
    except RuntimeError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 1

    if not dirty_lines:
        print("package-generated-assets-clean-ok: protected package outputs are clean")
        return 0

    print("error: protected package generated outputs are dirty:", file=sys.stderr)
    for line in dirty_lines:
        print(f"  {porcelain_path(line)}", file=sys.stderr)
    print("", file=sys.stderr)
    print("Recovery:", file=sys.stderr)
    print(RECOVERY_COMMANDS, file=sys.stderr)
    return 1


if __name__ == "__main__":
    sys.exit(main())
