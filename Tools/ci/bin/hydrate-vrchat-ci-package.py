#!/usr/bin/env python3
"""Hydrate filtered VRChat SDK files needed by CI Unity test packages."""

from __future__ import annotations

import argparse
import hashlib
import io
import sys
import urllib.request
import zipfile
from pathlib import Path

BASE_VERSION = "3.10.2"
BASE_URL = (
    "https://github.com/vrchat/packages/releases/download/"
    f"{BASE_VERSION}/com.vrchat.base-{BASE_VERSION}.zip"
)
BASE_SHA256 = "e4268b7677baedc50f15e22c5d7d73c8d173d39fa49d78821b3c23e1e9c6555e"

REQUIRED_BASE_FILES = (
    "Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.PhysBone.dll",
    "Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.PhysBone.dll.meta",
    "Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.Contact.dll",
    "Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.Contact.dll.meta",
    "Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.Constraint.dll",
    "Runtime/VRCSDK/Plugins/VRC.SDK3.Dynamics.Constraint.dll.meta",
    "Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics.PhysBone.Editor.dll",
    "Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics.PhysBone.Editor.dll.meta",
    "Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics.Contact.Editor.dll",
    "Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics.Contact.Editor.dll.meta",
    "Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics.Constraint.Editor.dll",
    "Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics.Constraint.Editor.dll.meta",
)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Download the official VRChat Base package and copy CI-required DLLs."
    )
    parser.add_argument(
        "--repo",
        type=Path,
        default=Path.cwd(),
        help="Repository root; defaults to cwd.",
    )
    parser.add_argument(
        "--cache",
        type=Path,
        default=Path("/tmp/asmlite-vrchat-package-cache"),
        help="Download cache directory.",
    )
    return parser.parse_args()


def read_cached_or_download(cache: Path) -> bytes:
    cache.mkdir(parents=True, exist_ok=True)
    package_path = cache / f"com.vrchat.base-{BASE_VERSION}.zip"
    if package_path.exists():
        data = package_path.read_bytes()
    else:
        req = urllib.request.Request(BASE_URL, headers={"User-Agent": "ASM-Lite CI"})
        with urllib.request.urlopen(req, timeout=120) as response:
            data = response.read()
        package_path.write_bytes(data)

    digest = hashlib.sha256(data).hexdigest()
    if digest != BASE_SHA256:
        package_path.unlink(missing_ok=True)
        raise RuntimeError(
            f"unexpected com.vrchat.base package sha256: {digest}; expected {BASE_SHA256}"
        )
    return data


def hydrate(repo: Path, cache: Path) -> list[Path]:
    project_base = repo / "Tools/ci/unity-project/Packages/com.vrchat.base"
    data = read_cached_or_download(cache)
    written: list[Path] = []

    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        names = set(archive.namelist())
        missing = [name for name in REQUIRED_BASE_FILES if name not in names]
        if missing:
            raise RuntimeError("VRChat base package missing expected files: " + ", ".join(missing))

        for name in REQUIRED_BASE_FILES:
            destination = project_base / name
            destination.parent.mkdir(parents=True, exist_ok=True)
            content = archive.read(name)
            if destination.exists() and destination.read_bytes() == content:
                continue
            destination.write_bytes(content)
            written.append(destination.relative_to(repo))

    return written


def main() -> int:
    args = parse_args()
    repo = args.repo.resolve()
    try:
        written = hydrate(repo, args.cache)
    except Exception as exc:  # noqa: BLE001 - CI helper should print concise failures.
        print(f"error: {exc}", file=sys.stderr)
        return 1

    if written:
        print("hydrated VRChat CI package files:")
        for path in written:
            print(f"  {path.as_posix()}")
    else:
        print("VRChat CI package hydration already up to date")
    return 0


if __name__ == "__main__":
    sys.exit(main())
