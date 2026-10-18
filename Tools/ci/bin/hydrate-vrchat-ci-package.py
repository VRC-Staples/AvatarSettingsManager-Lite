#!/usr/bin/env python3
"""Hydrate and assert complete, stock VRChat 3.10.2 packages in the CI project.

Pins come from https://vrchat.github.io/packages/index.json (Official / VRChat).
Existing SDK bytes are never overwritten: custom files or source/version drift
fail before any project write. Run only in a disposable checkout or fixture.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import stat
import sys
import tempfile
import urllib.request
import zipfile
from pathlib import Path, PurePosixPath

BASE_VERSION = "3.10.2"
BASE_SHA256 = "e4268b7677baedc50f15e22c5d7d73c8d173d39fa49d78821b3c23e1e9c6555e"
AVATARS_SHA256 = "c70b66cd42f1aae9352297a455177ca910cfcab7670c5717afad68f6818a2cf6"
PACKAGE_NAMES = ("com.vrchat.base", "com.vrchat.avatars")
MAX_ARCHIVE_BYTES = 64 * 1024 * 1024
MAX_EXPANDED_BYTES = 256 * 1024 * 1024

# Ownership receipts for the old helper's exact UTF-8 output and the workflow's
# exact copied DLL. Unknown variants (including generated DLL metadata) fail;
# they are not ours to delete. No replacement SDK implementation is retained.
KNOWN_CI_FILES = {
    "Packages/com.vrchat.base/Editor/VRCSDK/Dependencies/VRChat/BuildPipeline/VRCSDKBuildPipelineCIFallback.cs":
        "88daf02ebb18f8590986c3996296e06c40e99f0c17ed16550c12a9c7b45d9e8e",
    "Packages/com.vrchat.base/Editor/VRCSDK/Dependencies/VRChat/BuildPipeline/VRCSDKBuildPipelineCIFallback.cs.meta":
        "9ea935a4b24a9254710afe0d9fa54ddbc11bc8d46d31bab76ce7a805c7211c47",
    "Assets/Editor/VRC.SDKBase.Editor.BuildPipeline.dll":
        "57431782b3b16619f3337b14b71f1fc8b966a63464e862a2118b3ca9280fdb67",
}


def reject_links(path: Path) -> None:
    for candidate in (path, *path.parents):
        if candidate.is_symlink() or getattr(candidate, "is_junction", lambda: False)():
            raise RuntimeError(f"unsafe linked path: {candidate}")


def inventory(root: Path) -> tuple[dict[str, bytes], set[str]]:
    reject_links(root)
    files: dict[str, bytes] = {}
    directories: set[str] = set()
    pending = [root] if root.exists() else []
    while pending:
        for path in pending.pop().iterdir():
            reject_links(path)
            name = path.relative_to(root).as_posix()
            if path.is_dir():
                directories.add(name)
                pending.append(path)
            elif path.is_file():
                files[name] = path.read_bytes()
            else:
                raise RuntimeError(f"unexpected non-file SDK entry: {path}")
    return files, directories


def assert_identity(content: bytes, package: str) -> None:
    identity = json.loads(content)
    if identity.get("name") != package or identity.get("version") != BASE_VERSION:
        raise RuntimeError(f"SDK identity mismatch for {package}: expected {BASE_VERSION}")
    if package == "com.vrchat.avatars" and identity.get("vpmDependencies", {}).get("com.vrchat.base") != BASE_VERSION:
        raise RuntimeError("SDK identity mismatch: Avatars must depend on Base 3.10.2")


def read_cached_or_download(cache: Path, package: str) -> bytes:
    reject_links(cache)
    cache.mkdir(parents=True, exist_ok=True)
    filename = f"{package}-{BASE_VERSION}.zip"
    package_path = cache / filename
    reject_links(package_path)
    cached = package_path.exists()
    if cached:
        if package_path.stat().st_size > MAX_ARCHIVE_BYTES:
            raise RuntimeError(f"oversized SDK archive: {filename}")
        data = package_path.read_bytes()
    else:
        url = f"https://github.com/vrchat/packages/releases/download/{BASE_VERSION}/{filename}"
        request = urllib.request.Request(url, headers={"User-Agent": "ASM-Lite CI"})
        with urllib.request.urlopen(request, timeout=120) as response:
            data = response.read(MAX_ARCHIVE_BYTES + 1)
        if len(data) > MAX_ARCHIVE_BYTES:
            raise RuntimeError(f"oversized SDK archive: {filename}")
    expected = BASE_SHA256 if package == "com.vrchat.base" else AVATARS_SHA256
    digest = hashlib.sha256(data).hexdigest()
    if digest != expected:
        raise RuntimeError(f"unexpected {package} package sha256: {digest}; expected {expected}")
    if not cached:
        package_path.write_bytes(data)
    return data


def read_package(data: bytes, package: str) -> tuple[dict[str, bytes], set[str]]:
    files: dict[str, bytes] = {}
    directories: set[str] = set()
    seen: set[str] = set()
    spellings: dict[str, str] = {}
    reserved = {"CON", "PRN", "AUX", "NUL"} | {f"{prefix}{n}" for prefix in ("COM", "LPT") for n in range(1, 10)}
    with zipfile.ZipFile(io.BytesIO(data)) as archive:
        members = archive.infolist()
        if len(members) > 10000 or sum(member.file_size for member in members) > MAX_EXPANDED_BYTES:
            raise RuntimeError(f"oversized expanded SDK archive: {package}")
        for member in members:
            name = member.filename[:-1] if member.is_dir() else member.filename
            parts = name.split("/")
            kind = stat.S_IFMT(member.external_attr >> 16)
            if (member.orig_filename != member.filename or "\\" in name or ":" in name
                    or any(not part or part in (".", "..") or part.rstrip(" .") != part
                           or any(ord(char) < 32 or char in '<>"|?*' for char in part)
                           or part.split(".")[0].upper() in reserved for part in parts)
                    or name.casefold() in seen or member.flag_bits & 1
                    or kind not in (0, stat.S_IFREG, stat.S_IFDIR)
                    or (kind == stat.S_IFDIR and not member.is_dir())
                    or (kind == stat.S_IFREG and member.is_dir())):
                raise RuntimeError(f"unsafe SDK archive member: {member.orig_filename!r}")
            seen.add(name.casefold())
            parents = [str(parent) for parent in PurePosixPath(name).parents if str(parent) != "."]
            for spelling in [name, *parents]:
                if spellings.setdefault(spelling.casefold(), spelling) != spelling:
                    raise RuntimeError(f"unsafe SDK archive case alias: {name}")
            if member.is_dir():
                directories.add(name)
            else:
                files[name] = archive.read(member)
            directories.update(parents)
    if {name.casefold() for name in files} & {name.casefold() for name in directories}:
        raise RuntimeError("unsafe SDK archive: file/directory collision")
    if "package.json" not in files:
        raise RuntimeError(f"SDK identity missing for {package}")
    assert_identity(files["package.json"], package)
    return files, directories


def assert_sources(project: Path) -> None:
    reject_links(project / "Packages/manifest.json")
    manifest = json.loads((project / "Packages/manifest.json").read_bytes())
    for package in PACKAGE_NAMES:
        if manifest.get("dependencies", {}).get(package) != f"file:{package}":
            raise RuntimeError(f"unexpected SDK source for {package}; expected file:{package}")
    lock_path = project / "Packages/packages-lock.json"
    reject_links(lock_path)
    if lock_path.exists():
        lock = json.loads(lock_path.read_bytes())
        for package in PACKAGE_NAMES:
            entry = lock.get("dependencies", {}).get(package, {})
            if entry.get("version") != f"file:{package}" or entry.get("source") != "embedded":
                raise RuntimeError(f"unexpected SDK lock source for {package}")


def check_sdk(target: Path, package: str, expected: dict[str, bytes], directories: set[str],
              removable: set[Path]) -> list[Path]:
    files, current_dirs = inventory(target)
    for path in removable:
        if path.is_relative_to(target):
            files.pop(path.relative_to(target).as_posix(), None)
    if files:
        if "package.json" not in files:
            raise RuntimeError(f"SDK identity missing for existing {package}")
        assert_identity(files["package.json"], package)
    unexpected = (set(files) - set(expected)) | (current_dirs - directories)
    if unexpected:
        raise RuntimeError(f"unexpected custom SDK entries in {package}: {', '.join(sorted(unexpected))}")
    changed = [name for name in files if files[name] != expected[name]]
    if changed:
        raise RuntimeError(f"SDK stock identity mismatch in {package}: {', '.join(sorted(changed))}")
    return [target / name for name in sorted(set(expected) - set(files))]


def hydrate(repo: Path, cache: Path, *, check_only: bool = False,
            remove_known_ci_shims: bool = False) -> list[Path]:
    reject_links(repo)
    repo = repo.resolve()
    project = repo / "Tools/ci/unity-project"
    assert_sources(project)
    packages = {name: read_package(read_cached_or_download(cache, name), name) for name in PACKAGE_NAMES}
    removable: set[Path] = set()
    for relative, digest in KNOWN_CI_FILES.items():
        path = project / relative
        reject_links(path)
        if path.exists():
            if hashlib.sha256(path.read_bytes()).hexdigest() != digest:
                raise RuntimeError(f"unexpected CI shim ownership: {relative}")
            if check_only or not remove_known_ci_shims:
                raise RuntimeError(f"known CI shim present: {relative}; opt in with --remove-known-ci-shims")
            removable.add(path)
    duplicate_meta = project / "Assets/Editor/VRC.SDKBase.Editor.BuildPipeline.dll.meta"
    reject_links(duplicate_meta)
    if duplicate_meta.exists():
        raise RuntimeError("unexpected CI provider metadata; no verified ownership, refusing removal")
    missing: list[Path] = []
    for name, (files, directories) in packages.items():
        missing.extend(check_sdk(project / "Packages" / name, name, files, directories, removable))
    if check_only:
        if missing:
            raise RuntimeError(f"stock SDK files missing: {len(missing)}")
        return []
    # Preflight both packages and every deletion before touching the project.
    for path in sorted(removable):
        path.unlink()
    for name, (files, directories) in packages.items():
        target = project / "Packages" / name
        for directory in sorted(directories):
            (target / directory).mkdir(parents=True, exist_ok=True)
        for path in missing:
            if path.is_relative_to(target):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(files[path.relative_to(target).as_posix()])
        if check_sdk(target, name, files, directories, set()):
            raise RuntimeError(f"stock SDK verification failed: {name}")
    return [path.relative_to(repo) for path in sorted(removable) + missing]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd(), help="Disposable repository root.")
    parser.add_argument("--cache", type=Path, default=Path(tempfile.gettempdir()) / "asmlite-vrchat-package-cache")
    parser.add_argument("--check-only", action="store_true", help="Assert exact stock SDK bytes without project writes.")
    parser.add_argument("--remove-known-ci-shims", action="store_true", help="Remove only checksum-owned legacy CI shims.")
    args = parser.parse_args()
    try:
        written = hydrate(args.repo, args.cache, check_only=args.check_only,
                          remove_known_ci_shims=args.remove_known_ci_shims)
    except Exception as exc:  # noqa: BLE001 - CI helper should print concise failures.
        print(f"error: {exc}", file=sys.stderr)
        return 1
    for name, digest in zip(PACKAGE_NAMES, (BASE_SHA256, AVATARS_SHA256)):
        print(f"stock-sdk-ok: {name} {BASE_VERSION} archive-sha256={digest}")
    print(f"stock SDK project changes: {len(written)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())