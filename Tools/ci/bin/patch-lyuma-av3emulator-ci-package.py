#!/usr/bin/env python3
"""Patch Av3Emulator's editor asmdef for the hydrated CI SDK stubs."""

from __future__ import annotations

import json
import sys
from pathlib import Path

REQUIRED_REFERENCE = "VRC.SDKBase.Editor.BuildPipeline"
AFTER_REFERENCE = "VRC.SDKBase.Editor"


def patch_asmdef(path: Path) -> bool:
    data = json.loads(path.read_text(encoding="utf-8"))
    references = data.setdefault("references", [])
    if REQUIRED_REFERENCE in references:
        return False

    try:
        insert_at = references.index(AFTER_REFERENCE) + 1
    except ValueError:
        insert_at = len(references)

    references.insert(insert_at, REQUIRED_REFERENCE)
    path.write_text(json.dumps(data, indent=4) + "\n", encoding="utf-8")
    return True


def main() -> int:
    project_path = Path(sys.argv[1]) if len(sys.argv) > 1 else Path("Tools/ci/unity-project")
    package_cache = project_path / "Library" / "PackageCache"
    matches = sorted(package_cache.glob("lyuma.av3emulator@*/Editor/lyuma.av3emulator.Editor.asmdef"))
    if not matches:
        print(f"error: Av3Emulator editor asmdef not found under {package_cache}", file=sys.stderr)
        return 1

    changed = False
    for asmdef in matches:
        changed |= patch_asmdef(asmdef)
        print(f"lyuma-av3emulator-asmdef-ok: {asmdef}")

    if changed:
        print(f"patched Av3Emulator asmdef with {REQUIRED_REFERENCE}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
