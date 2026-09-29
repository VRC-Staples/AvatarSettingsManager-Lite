#!/usr/bin/env python
"""Prepare a KikiASM smoke catalog without changing the canonical CI fixture."""

import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / "Tools/ci/smoke/suite-catalog.json"
TARGET = ROOT / "KikiASM/Tools/ci/smoke/suite-catalog-kikiasm.json"
OLD = {"scenePath": "Assets/Click ME.unity", "avatarName": "CanonicalDress"}
NEW = {"scenePath": "Assets/kikiasm.unity", "avatarName": "KikiASM"}


def suite_ids(catalog):
    return [suite["suiteId"] for group in catalog["groups"] for suite in group["suites"]]


def main():
    if not (ROOT / "KikiASM/Assets/kikiasm.unity").is_file():
        raise FileNotFoundError("KikiASM/Assets/kikiasm.unity is required")
    source = SOURCE.read_text(encoding="utf-8")
    canonical = json.loads(source)
    if canonical["fixture"] != OLD:
        raise ValueError("Canonical fixture changed; inspect the catalog before adapting it")

    adapted = source.replace(OLD["scenePath"], NEW["scenePath"]).replace(
        OLD["avatarName"], NEW["avatarName"]
    )
    local = json.loads(adapted)
    if local["fixture"] != NEW or suite_ids(local) != suite_ids(canonical):
        raise ValueError("Local catalog differs from the canonical suite inventory")
    if OLD["scenePath"] in adapted or OLD["avatarName"] in adapted:
        raise ValueError("Local catalog still names the canonical fixture")

    TARGET.parent.mkdir(parents=True, exist_ok=True)
    TARGET.write_text(adapted, encoding="utf-8", newline="\n")
    print(f"Prepared {TARGET}: {len(suite_ids(local))} suites, {NEW['scenePath']} / {NEW['avatarName']}")


if __name__ == "__main__":
    main()
