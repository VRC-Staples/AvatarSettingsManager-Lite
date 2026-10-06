# First-build PlayMode regression

`ASMLite.Tests.PlayMode.ASMLiteFirstBuildPlayModeTests.FirstPlayModeBuild_PreservesCustomIcons_AndRunsSaveLoadClearWithoutASecondBuild`

## Boundary

PlayMode is the first-upload proxy, not proof of a VRChat upload or in-game behavior. The test creates a synthetic avatar, performs one authoring rebuild (as in the reported workflow), adds a saved parameter after that rebuild, and calls the real SDK preprocessing pipeline exactly once on a clone while in PlayMode. It does not upload anything or retry the build.

It inspects the resulting VRCFury-merged descriptor, not just generated package assets:

- The late parameter's backup exists in both merged FX and expression parameters.
- Custom root, two preset, Save, Load, Clear Preset, and confirmation icons retain their image content. VRCFury may clone/compress textures, so reference identity is not required.
- Menu buttons address `ASMLite_Ctrl` with the expected slot/action values.
- The existing AV3 invariant harness exercises slot 1 Save/Load for a saved bool and late int while preserving an excluded float. Clear Preset must restore saved defaults without changing the excluded value.

A failure stops subsequent checks. Passing the icon/schema assertions does not imply Save/Load/Clear passed. This fixture does not reproduce the reporter's complete avatar, verify every slot/type combination, or model changing custom icons after the authoring rebuild.

## Run locally

Use the isolated `Tools/ci/unity-project`, Unity 2022.3.22f1, and its pinned SDK/VRCFury/Av3Emulator dependencies. Do not run a second editor against an already-open project. In Unity's Test Runner, select **PlayMode**, then the class above.

Alternatively, from the repository root in Git Bash, use fresh absolute result/log paths in an existing artifact directory:

```bash
CI=false 'C:/Program Files/Unity/Hub/Editor/2022.3.22f1/Editor/Unity.exe' \
  -batchmode \
  -projectPath 'F:/Hermes/Projects/AvatarSettingsManager-Lite/Tools/ci/unity-project' \
  -runTests -testPlatform PlayMode \
  -testFilter ASMLite.Tests.PlayMode.ASMLiteFirstBuildPlayModeTests \
  -testResults "$RESULT_XML" -logFile "$UNITY_LOG"
```

Do **not** add `-nographics`: this VRCFury version opens an editor progress window during preprocessing. The new class is deliberately not included in the existing graphics-less CI Save/Load filter. It remains explicitly listed in `suites.json` for discovery.

If the isolated hydrated SDK produces `CS0246` for `VRCExpressionsMenuEditor`, use the existing `Tools/ci/bin/patch-lyuma-av3emulator-ci-package.py` after package resolution; do not modify SDK DLLs. The test snapshots/restores generated package outputs and uses the existing fixture teardown. Check `verify-package-generated-assets-clean.py` after running it.

## Actual-upload acceptance: manual only

1. On a disposable copy of the affected avatar, record package versions and configure distinctive custom root, preset, and action icons. Run the normal authoring rebuild once.
2. Perform one actual upload. Do not perform a second upload before checking the result; record whether any earlier PlayMode/build was performed.
3. In VRChat, inspect every configured icon, save distinctive settings, change them, load the preset, then clear it. Confirm excluded parameters remain unchanged and untouched presets retain their defaults.
4. Record first-upload pass/fail, logs, and screenshots. If a second upload is used as a workaround, record it separately; it cannot make the first-upload acceptance pass.

No automated result here authorizes or substitutes for that manual acceptance.
