#!/usr/bin/env python3
"""Patch Av3Emulator package sources for the hydrated CI SDK stubs."""

from __future__ import annotations

import json
import sys
from pathlib import Path

REQUIRED_REFERENCE = "VRC.SDKBase.Editor.BuildPipeline"
AFTER_REFERENCE = "VRC.SDKBase.Editor"
APIUSER_REFLECTION_BLOCK = """\t\t\t\tSystem.Type apiusertype = System.Type.GetType(\"VRC.Core.APIUser, VRCCore-Editor\");
\t\t\t\tif (apiusertype != null) {
\t\t\t\t\tvar idprop = apiusertype.GetProperty(\"id\", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
\t\t\t\t\tvar prop = apiusertype.GetProperty(\"CurrentUser\", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public);
\t\t\t\t\t// Debug.Log(\"idprop \" + idprop);
\t\t\t\t\tif (idprop != null && prop != null) {
\t\t\t\t\t\tvar apiuserinst = prop.GetValue(null);
\t\t\t\t\t\tif (apiuserinst != null) {
\t\t\t\t\t\t\t// Debug.Log(\"apiuser \" + apiuserinst);
\t\t\t\t\t\t\tuserid = (string)idprop.GetValue(apiuserinst);
\t\t\t\t\t\t}
\t\t\t\t\t}
\t\t\t\t}
"""
SAFE_APIUSER_REFLECTION_BLOCK = """\t\t\t\ttry {
\t\t\t\t\tSystem.Type apiusertype = System.Type.GetType(\"VRC.Core.APIUser, VRCCore-Editor\");
\t\t\t\t\tif (apiusertype != null) {
\t\t\t\t\t\tvar idprop = apiusertype.GetProperty(\"id\", System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public);
\t\t\t\t\t\tvar prop = apiusertype.GetProperty(\"CurrentUser\", System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public);
\t\t\t\t\t\t// Debug.Log(\"idprop \" + idprop);
\t\t\t\t\t\tif (idprop != null && prop != null) {
\t\t\t\t\t\t\tvar apiuserinst = prop.GetValue(null);
\t\t\t\t\t\t\tif (apiuserinst != null) {
\t\t\t\t\t\t\t\t// Debug.Log(\"apiuser \" + apiuserinst);
\t\t\t\t\t\t\t\tuserid = (string)idprop.GetValue(apiuserinst);
\t\t\t\t\t\t\t}
\t\t\t\t\t\t}
\t\t\t\t\t}
\t\t\t\t} catch (TypeLoadException) {
\t\t\t\t\tuserid = null;
\t\t\t\t} catch (System.IO.FileNotFoundException) {
\t\t\t\t\tuserid = null;
\t\t\t\t}
"""


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


def patch_osc_configuration(path: Path) -> bool:
    text = path.read_text(encoding="utf-8")
    if SAFE_APIUSER_REFLECTION_BLOCK in text:
        return False
    if APIUSER_REFLECTION_BLOCK not in text:
        raise RuntimeError(f"expected APIUser reflection block not found in {path}")

    path.write_text(text.replace(APIUSER_REFLECTION_BLOCK, SAFE_APIUSER_REFLECTION_BLOCK), encoding="utf-8")
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

    osc_matches = sorted(package_cache.glob("lyuma.av3emulator@*/Runtime/Scripts/A3EOSCConfiguration.cs"))
    if not osc_matches:
        print(f"error: Av3Emulator OSC configuration source not found under {package_cache}", file=sys.stderr)
        return 1
    for source in osc_matches:
        changed |= patch_osc_configuration(source)
        print(f"lyuma-av3emulator-osc-config-ok: {source}")

    if changed:
        print(f"patched Av3Emulator package for hydrated CI SDK stubs")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
