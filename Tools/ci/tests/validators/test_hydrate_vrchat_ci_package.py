#!/usr/bin/env python3
"""Actual hydrator tests using synthetic archives, never an installed SDK/project.

Run with TMPDIR/TMP/TEMP set to a disposable scratch root. Synthetic archive
and legacy-ownership pins are substituted; validation and filesystem writes are real.
"""

from __future__ import annotations

import hashlib
import importlib.util
import io
import json
import os
import stat
import subprocess
import tempfile
import unittest
import warnings
import zipfile
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / "bin/hydrate-vrchat-ci-package.py"
SPEC = importlib.util.spec_from_file_location("hydrate_vrchat_ci", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
HYDRATOR = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HYDRATOR)
BASE = "com.vrchat.base"
AVATARS = "com.vrchat.avatars"


class HydrateStockSdkTests(unittest.TestCase):
    def setUp(self) -> None:
        self.tmp = tempfile.TemporaryDirectory(
            prefix="asmlite-r3-synthetic-", dir=os.environ.get("TMPDIR", tempfile.gettempdir())
        )
        self.addCleanup(self.tmp.cleanup)
        self.repo = Path(self.tmp.name) / "repo"
        self.project = self.repo / "Tools/ci/unity-project"
        self.packages = self.project / "Packages"
        self.packages.mkdir(parents=True)
        self.cache = Path(self.tmp.name) / "cache"
        self.cache.mkdir()
        self.manifest = {"dependencies": {name: "file:" + name for name in (BASE, AVATARS)}}
        self.lock = {"dependencies": {
            name: {"version": "file:" + name, "source": "embedded", "depth": 0}
            for name in (BASE, AVATARS)
        }}
        self.write_json(self.packages / "manifest.json", self.manifest)
        self.write_json(self.packages / "packages-lock.json", self.lock)
        self.payloads = {
            BASE: {
                "package.json": json.dumps({"name": BASE, "version": "3.10.2"}).encode(),
                "package.json.meta": b"base metadata\n",
                "Editor/StockCallback.cs": b"// synthetic official callback\n",
                "Editor/StockCallback.cs.meta": b"callback metadata\n",
                "Runtime/VRCSDK/Plugins/VRCSDKBase.dll": b"synthetic base DLL",
                "ShaderLibrary/VRCTime.cginc": b"// synthetic shader\n",
            },
            AVATARS: {
                "package.json": json.dumps({
                    "name": AVATARS, "version": "3.10.2",
                    "vpmDependencies": {BASE: "3.10.2"},
                }).encode(),
                "Editor/VRCExpressionsMenuEditor.cs": b"// synthetic stock editor\n",
                "Runtime/VRCSDK/Plugins/VRCSDK3A.dll": b"synthetic avatars DLL",
            },
        }
        # Include the old subset too: the regression must reach its wrong output,
        # not merely stop because a small synthetic archive lacks its old inputs.
        old_plugins = ["VRCCore-Standalone", "VRCCore-Editor", "Harmony/0Harmony"]
        old_plugins += ["VRC.SDK3.Dynamics." + kind for kind in ("PhysBone", "Contact", "Constraint")]
        for plugin in old_plugins:
            for suffix in (".dll", ".dll.meta"):
                self.payloads[BASE]["Runtime/VRCSDK/Plugins/" + plugin + suffix] = b"synthetic plugin"
        for kind in ("PhysBone", "Contact", "Constraint"):
            for suffix in (".dll", ".dll.meta"):
                self.payloads[BASE]["Editor/VRCSDK/Plugins/VRC.SDK3.Dynamics." + kind + ".Editor" + suffix] = b"synthetic editor plugin"
        for suffix in ("", ".meta"):
            self.payloads[BASE]["Editor/VRCSDK/Dependencies/VRChat/BuildPipeline/VRC.SDKBase.Editor.BuildPipeline.asmdef" + suffix] = b"synthetic assembly metadata"
        self.pins = {name: self.store_archive(name, payload) for name, payload in self.payloads.items()}
        self.set_pins()

    @staticmethod
    def write_json(path: Path, value: object) -> None:
        path.write_text(json.dumps(value), encoding="utf-8")

    def store_archive(self, name: str, files: dict[str, bytes], extra=None) -> str:
        stream = io.BytesIO()
        with zipfile.ZipFile(stream, "w") as archive:
            for path, content in files.items():
                archive.writestr(path, content)
            if extra:
                extra_name, content = extra
                # ZipInfo normalizes backslashes on Windows when writing. Patch
                # the stored filename bytes instead, so the parser sees the
                # malicious member rather than a sanitized test fixture.
                stored = extra_name.replace("\\", "!") if isinstance(extra_name, str) else extra_name
                with warnings.catch_warnings():
                    warnings.filterwarnings("ignore", message="Duplicate name:", category=UserWarning)
                    archive.writestr(stored, content)
        data = stream.getvalue()
        if extra and isinstance(extra[0], str) and "\\" in extra[0]:
            data = data.replace(extra[0].replace("\\", "!").encode(), extra[0].encode())
        (self.cache / (name + "-3.10.2.zip")).write_bytes(data)
        return hashlib.sha256(data).hexdigest()

    def set_pins(self) -> None:
        for name, constant in ((BASE, "BASE_SHA256"), (AVATARS, "AVATARS_SHA256")):
            pin = patch.object(HYDRATOR, constant, self.pins[name], create=True)
            pin.start()
            self.addCleanup(pin.stop)

    def snapshot(self) -> dict[str, bytes]:
        return {p.relative_to(self.repo).as_posix(): p.read_bytes()
                for p in self.repo.rglob("*") if p.is_file()}

    def test_complete_packages_are_byte_identical_and_idempotent(self) -> None:
        # A subset copier (or an injected SDK implementation) fails the exact set.
        HYDRATOR.hydrate(self.repo, self.cache)
        actual = {p.relative_to(self.packages).as_posix(): p.read_bytes()
                  for p in self.packages.rglob("*") if p.is_file()
                  and p.name not in ("manifest.json", "packages-lock.json")}
        expected = {name + "/" + path: content
                    for name, payload in self.payloads.items() for path, content in payload.items()}
        self.assertEqual(actual, expected)
        before = self.snapshot()
        self.assertEqual(HYDRATOR.hydrate(self.repo, self.cache), [])
        HYDRATOR.hydrate(self.repo, self.cache, check_only=True)
        self.assertEqual(self.snapshot(), before)

    def test_bad_checksum_never_changes_project(self) -> None:
        (self.cache / (AVATARS + "-3.10.2.zip")).write_bytes(b"corrupt archive")
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, "sha256"):
            HYDRATOR.hydrate(self.repo, self.cache)
        self.assertEqual(self.snapshot(), before)

    def test_archive_identity_and_version_are_checked_before_writes(self) -> None:
        for identity in ({"name": BASE, "version": "3.10.2"},
                         {"name": AVATARS, "version": "3.10.3"},
                         {"name": AVATARS, "version": "3.10.2",
                          "vpmDependencies": {BASE: "3.10.3"}}):
            with self.subTest(identity=identity):
                payload = dict(self.payloads[AVATARS], **{"package.json": json.dumps(identity).encode()})
                self.pins[AVATARS] = self.store_archive(AVATARS, payload)
                self.set_pins()
                before = self.snapshot()
                with self.assertRaises(RuntimeError):
                    HYDRATOR.hydrate(self.repo, self.cache)
                self.assertEqual(self.snapshot(), before)

    def test_unsafe_members_are_rejected_before_writes(self) -> None:
        symlink = zipfile.ZipInfo("Editor/link.cs")
        symlink.create_system = 3
        symlink.external_attr = (stat.S_IFLNK | 0o777) << 16
        extras = [(path, b"unsafe") for path in (
            "../outside.cs", "/absolute.cs", "C:/escape.cs", "Editor\\escape.cs",
            "Editor/./alias.cs", "Editor/trailing.cs.", "Editor/NUL.cs",
            "editor/stockcallback.cs", "package.json", "Editor/StockCallback.cs/child",
            "editor/Nested/alias.cs", "Editor//alias.cs", "Editor/invalid?.cs",
        )] + [(symlink, b"../../outside")]
        for extra in extras:
            with self.subTest(member=str(extra[0])):
                self.pins[BASE] = self.store_archive(BASE, self.payloads[BASE], extra)
                self.set_pins()
                before = self.snapshot()
                with self.assertRaises(RuntimeError):
                    HYDRATOR.hydrate(self.repo, self.cache)
                self.assertEqual(self.snapshot(), before)

    def test_manifest_and_lock_source_semantics_are_preserved(self) -> None:
        for path, value in (
            ("manifest.json", {"dependencies": {BASE: "3.10.2", AVATARS: "file:" + AVATARS}}),
            ("packages-lock.json", {"dependencies": {
                BASE: {"version": "3.10.2", "source": "registry"},
                AVATARS: self.lock["dependencies"][AVATARS],
            }}),
        ):
            with self.subTest(path=path):
                self.write_json(self.packages / "manifest.json", self.manifest)
                self.write_json(self.packages / "packages-lock.json", self.lock)
                self.write_json(self.packages / path, value)
                before = self.snapshot()
                with self.assertRaises(RuntimeError):
                    HYDRATOR.hydrate(self.repo, self.cache)
                self.assertEqual(self.snapshot(), before)

    def test_custom_sdk_files_and_modified_stock_bytes_are_preserved_on_rejection(self) -> None:
        HYDRATOR.hydrate(self.repo, self.cache)
        target = self.packages / AVATARS / "Editor/Custom.cs"
        target.write_bytes(b"user content")
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, "unexpected"):
            HYDRATOR.hydrate(self.repo, self.cache)
        self.assertEqual(self.snapshot(), before)
        target.unlink()
        target = self.packages / AVATARS / "Editor/VRCExpressionsMenuEditor.cs"
        target.write_bytes(b"customized stock source")
        before = self.snapshot()
        with self.assertRaises(RuntimeError):
            HYDRATOR.hydrate(self.repo, self.cache)
        self.assertEqual(self.snapshot(), before)

    def test_existing_sdk_identity_mismatch_never_changes_project(self) -> None:
        target = self.packages / BASE
        target.mkdir()
        self.write_json(target / "package.json", {"name": BASE, "version": "3.10.3"})
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, "identity"):
            HYDRATOR.hydrate(self.repo, self.cache)
        self.assertEqual(self.snapshot(), before)

    def test_check_only_detects_missing_files_without_hydrating(self) -> None:
        before = self.snapshot()
        with self.assertRaisesRegex(RuntimeError, "missing"):
            HYDRATOR.hydrate(self.repo, self.cache, check_only=True)
        self.assertEqual(self.snapshot(), before)

    def test_known_ci_cleanup_requires_opt_in_and_exact_ownership(self) -> None:
        relative = "Assets/Editor/VRC.SDKBase.Editor.BuildPipeline.dll"
        target = self.project / relative
        target.parent.mkdir(parents=True)
        target.write_bytes(b"synthetic known CI provider")
        before = self.snapshot()
        with self.assertRaises(RuntimeError):
            HYDRATOR.hydrate(self.repo, self.cache)
        self.assertEqual(self.snapshot(), before)
        with self.assertRaises(RuntimeError):
            HYDRATOR.hydrate(self.repo, self.cache, remove_known_ci_shims=True)
        self.assertEqual(self.snapshot(), before)
        with patch.object(HYDRATOR, "KNOWN_CI_FILES", {
            relative: hashlib.sha256(b"synthetic known CI provider").hexdigest(),
        }):
            HYDRATOR.hydrate(self.repo, self.cache, remove_known_ci_shims=True)
        self.assertFalse(target.exists())
        self.assertEqual((self.packages / AVATARS / "Editor/VRCExpressionsMenuEditor.cs").read_bytes(),
                         b"// synthetic stock editor\n")

    def test_package_symlink_cannot_escape_project(self) -> None:
        outside = Path(self.tmp.name) / "outside"
        outside.mkdir()
        sentinel = outside / "sentinel"
        sentinel.write_bytes(b"unrelated")
        link = self.packages / BASE
        try:
            link.symlink_to(outside, target_is_directory=True)
        except OSError as exc:
            if os.name != "nt":
                raise
            result = subprocess.run(["cmd.exe", "/c", "mklink", "/J", str(link), str(outside)],
                                    capture_output=True, text=True, check=False)
            if result.returncode:
                self.skipTest(f"symlink/junction unavailable: {exc}; {result.stderr}")
            self.addCleanup(link.rmdir)
        with self.assertRaises(RuntimeError):
            HYDRATOR.hydrate(self.repo, self.cache)
        self.assertEqual(sentinel.read_bytes(), b"unrelated")
        self.assertEqual(sorted(p.name for p in outside.iterdir()), ["sentinel"])


if __name__ == "__main__":
    unittest.main()
