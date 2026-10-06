using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using ASMLite.Editor;
using ASMLite.Tests.Editor;

namespace ASMLite.Tests.PlayMode
{
    [TestFixture]
    [Category("PlayMode")]
    public sealed class ASMLiteFirstBuildPlayModeTests
    {
        private const string SavedBool = "FirstBuild_Bool";
        private const string DefaultTrueBool = "FirstBuild_DefaultTrueBool";
        private const string SavedFloat = "FirstBuild_Float";
        private const string LateInt = "FirstBuild_LateInt";
        private const string ExcludedFloat = "FirstBuild_ExcludedFloat";
        private AsmLiteTestContext _ctx;
        private GameObject _builtAvatar;
        private GameObject _emulator;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _generatedBaseline;

        [TearDown]
        public void TearDown()
        {
            ASMLiteBudgetLateAddition.Target = null;
            try
            {
                if (_emulator != null) UnityEngine.Object.DestroyImmediate(_emulator);
                if (_builtAvatar != null) UnityEngine.Object.DestroyImmediate(_builtAvatar);
                if (_ctx != null) ASMLiteTestFixtures.TearDownTestAvatar(_ctx.AvatarGo);
            }
            finally
            {
                _generatedBaseline?.Restore();
                _ctx = null;
                _generatedBaseline = null;
            }
        }

        [UnityTest]
        public IEnumerator FirstPlayModeBuild_PreservesCustomIcons_AndRunsSaveLoadClearWithoutASecondBuild()
        {
            Assert.IsTrue(Application.isPlaying, "Run this regression in the PlayMode test lane.");
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            Assert.IsTrue(runtimeResolution.IsAvailable, runtimeResolution.Diagnostic);
            _generatedBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(nameof(ASMLiteFirstBuildPlayModeTests));
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreatePlayModeTestAvatar();
            _ctx.AvatarGo.name = "ASMLite_FirstBuild_Source";
            _ctx.AvatarGo.AddComponent<Animator>();
            _ctx.AvDesc.customExpressions = true;
            ASMLiteAv3SaveLoadRuntimeTestBase.WireFxController(_ctx.AvDesc, _ctx.Ctrl);
            _ctx.Comp.slotCount = 2;
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { ExcludedFloat };
            ASMLiteTestFixtures.AddExpressionParam(_ctx, SavedBool, VRCExpressionParameters.ValueType.Bool);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, DefaultTrueBool, VRCExpressionParameters.ValueType.Bool, defaultValue: 1f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, SavedFloat, VRCExpressionParameters.ValueType.Float, defaultValue: 0.375f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, ExcludedFloat, VRCExpressionParameters.ValueType.Float, saved: false);
            _ctx.Ctrl.AddParameter(SavedBool, AnimatorControllerParameterType.Bool);
            _ctx.Ctrl.AddParameter(DefaultTrueBool, AnimatorControllerParameterType.Bool);
            _ctx.Ctrl.AddParameter(SavedFloat, AnimatorControllerParameterType.Float);
            _ctx.Ctrl.AddParameter(ExcludedFloat, AnimatorControllerParameterType.Float);

            // Distinct, persistent test textures cannot accidentally match bundled defaults or stale output.
            var icons = Enumerable.Range(0, 6).Select(CreateIcon).ToArray();
            _ctx.Comp.useCustomSlotIcons = true;
            _ctx.Comp.iconMode = IconMode.Custom;
            _ctx.Comp.customRootIcon = icons[0];
            _ctx.Comp.customIcons = new[] { icons[1], icons[2] };
            _ctx.Comp.actionIconMode = ActionIconMode.Custom;
            _ctx.Comp.customSaveIcon = icons[3];
            _ctx.Comp.customLoadIcon = icons[4];
            _ctx.Comp.customClearIcon = icons[5];

            // One authoring rebuild is allowed, matching the report. This is NOT an SDK build.
            // Do not use BuildAndWireAvatarFixture: it bypasses VRCFury and hand-merges the result.
            Assert.AreEqual(3, ASMLiteBuilder.Build(_ctx.Comp));
            var generatedFx = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(generatedFx);
            Assert.IsFalse(generatedFx.parameters.Any(parameter => parameter.name == "ASMLite_Bak_S1_" + LateInt));

            // Change the schema after authoring generation so a warm generated controller cannot hide lag.
            ASMLiteTestFixtures.AddExpressionParam(_ctx, LateInt, VRCExpressionParameters.ValueType.Int, defaultValue: 7f);
            _ctx.Ctrl.AddParameter(LateInt, AnimatorControllerParameterType.Int);
            EditorUtility.SetDirty(_ctx.Ctrl);
            AssetDatabase.SaveAssets();
            Assert.IsEmpty(_ctx.MenuAsset.controls, "The source descriptor must not already contain a hand-merged ASM-Lite menu.");

            _builtAvatar = UnityEngine.Object.Instantiate(_ctx.AvatarGo);
            _builtAvatar.name = "ASMLite_FirstBuild(Clone)";
            _ctx.AvatarGo.SetActive(false);
            var builtDescriptor = _builtAvatar.GetComponent<VRCAvatarDescriptor>();

            // Exactly one real SDK preprocessing pass, in PlayMode, is the first-upload proxy.
            // Keep the source inactive; never call Build/Reconcile or retry on the built clone.
            Assert.IsTrue(VRCBuildPipelineCallbacks.OnPreprocessAvatar(_builtAvatar), "First SDK preprocessing pass failed.");
            var builtFx = builtDescriptor.baseAnimationLayers
                .Single(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            Assert.IsNotNull(builtFx, "The pipeline must produce a merged FX controller.");
            Assert.AreNotSame(_ctx.Ctrl, builtFx);
            Assert.AreNotSame(generatedFx, builtFx, "Do not substitute the package controller for VRCFury's merged output.");
            Assert.AreNotSame(_ctx.MenuAsset, builtDescriptor.expressionsMenu);
            Assert.IsTrue(builtFx.parameters.Any(parameter => parameter.name == LateInt),
                "The first built FX controller is missing the late live source parameter.");
            Assert.IsFalse(builtFx.parameters.Any(parameter => parameter.name == "ASMLite_Bak_S1_" + LateInt),
                "The first built FX controller must not duplicate expression backup storage.");
            Assert.IsTrue(builtDescriptor.expressionParameters.parameters.Any(parameter => parameter.name == "ASMLite_Bak_S1_" + LateInt),
                "The first built expression schema is missing the late parameter's backup.");
            AssertBuiltMenu(builtDescriptor.expressionsMenu, icons);
            TestContext.WriteLine("First SDK preprocess passed: late-parameter backup schema, custom root/slot/action icons, and menu buttons verified. Starting AV3 Save/Load/Clear checks without another build.");

            // VRCFury queues an AV3 restart on delayCall. Let it finish before creating
            // the runtime, otherwise the harness keeps a reference to a destroyed runtime.
            bool callbacksFinished = false;
            void AfterPreprocessCallbacks() => callbacksFinished = true;
            EditorApplication.delayCall += AfterPreprocessCallbacks;
            try
            {
                double deadline = EditorApplication.timeSinceStartup + 10.0d;
                while (!callbacksFinished && EditorApplication.timeSinceStartup < deadline)
                    yield return null;
                Assert.IsTrue(callbacksFinished, "Timed out waiting for post-preprocess editor callbacks.");
            }
            finally
            {
                EditorApplication.delayCall -= AfterPreprocessCallbacks;
            }

            _builtAvatar.SetActive(true);
            _emulator = ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();
            var saved = new[]
            {
                ASMLiteAv3SaveLoadHarness.Descriptor(SavedBool, VRCExpressionParameters.ValueType.Bool),
                ASMLiteAv3SaveLoadHarness.Descriptor(DefaultTrueBool, VRCExpressionParameters.ValueType.Bool),
                ASMLiteAv3SaveLoadHarness.Descriptor(SavedFloat, VRCExpressionParameters.ValueType.Float),
                ASMLiteAv3SaveLoadHarness.Descriptor(LateInt, VRCExpressionParameters.ValueType.Int),
            };
            var unsaved = new[] { ASMLiteAv3SaveLoadHarness.Descriptor(ExcludedFloat, VRCExpressionParameters.ValueType.Float) };
            yield return new ASMLiteAv3SaveLoadHarness(saved, unsaved).RunCoreInvariant(_builtAvatar, 0xA5A50001u);
            yield return AssertClearResetsSavedAndPreservesExcluded();
        }

        private static Texture2D CreateIcon(int index)
        {
            var icon = new Texture2D(4, 4) { name = "FirstBuildIcon" + index };
            icon.SetPixels(Enumerable.Repeat(Color.HSVToRGB(index / 6f, 1f, 1f), 16).ToArray());
            icon.Apply();
            AssetDatabase.CreateAsset(icon, ASMLiteTestFixtures.TempDir + "/FirstBuildIcon" + index + ".asset");
            return icon;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FirstPlayModeBuild_ParameterBudget_RejectsLateAdditions(bool bakedOnly)
        {
            Assert.IsTrue(Application.isPlaying, "Run this regression in the PlayMode test lane.");
            _generatedBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(nameof(ASMLiteFirstBuildPlayModeTests));
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreatePlayModeTestAvatar();
            _ctx.AvatarGo.AddComponent<Animator>();
            _ctx.AvDesc.customExpressions = true;
            ASMLiteAv3SaveLoadRuntimeTestBase.WireFxController(_ctx.AvDesc, _ctx.Ctrl);
            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.AddExpressionParam(_ctx, SavedBool, VRCExpressionParameters.ValueType.Bool);
            _ctx.Ctrl.AddParameter(SavedBool, AnimatorControllerParameterType.Bool);
            Assert.AreEqual(1, ASMLiteBuilder.Build(_ctx.Comp));
            _builtAvatar = UnityEngine.Object.Instantiate(_ctx.AvatarGo);
            _ctx.AvatarGo.SetActive(false);
            var descriptor = _builtAvatar.GetComponent<VRCAvatarDescriptor>();
            VRCExpressionParameters bakedParameters = null;
            try
            {
                if (bakedOnly)
                {
                    // Model an already-baked payload; no authoring component is available
                    // to run a count check. The independent SDK callback must still guard it.
                    bakedParameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                    var generated = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
                    bakedParameters.parameters = descriptor.expressionParameters.parameters.Concat(generated.parameters).ToArray();
                    descriptor.expressionParameters = bakedParameters;
                    descriptor.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
                    UnityEngine.Object.DestroyImmediate(_builtAvatar.GetComponentInChildren<ASMLiteComponent>(true).gameObject);
                }
                ASMLiteBudgetLateAddition.Target = _builtAvatar;
                LogAssert.Expect(LogType.Error, new Regex("BUILD-304"));
                LogAssert.Expect(LogType.Error, new Regex("ASMLiteParameterBudgetFinalCallback.*reported a failure"));
                LogAssert.Expect(LogType.Assert, new Regex("Cancelling DisplayDialog: Preprocess Callback Failed"));
                Assert.IsFalse(VRCBuildPipelineCallbacks.OnPreprocessAvatar(_builtAvatar), "The first pass must reject final overflow without a second build.");
                Assert.AreEqual(8193, descriptor.expressionParameters.parameters.Length);
                Assert.AreEqual(1, _ctx.ParamsAsset.parameters.Length, "Late rejection must preserve authored source parameters.");
            }
            finally
            {
                ASMLiteBudgetLateAddition.Target = null;
                if (bakedParameters != null) UnityEngine.Object.DestroyImmediate(bakedParameters);
            }
        }

        private static void AssertBuiltMenu(VRCExpressionsMenu menu, Texture2D[] icons)
        {
            var root = RequireControl(menu, "Settings Manager");
            AssertIcon(icons[0], root.icon);
            for (int slot = 1; slot <= 2; slot++)
            {
                var preset = RequireControl(root.subMenu, "Preset " + slot);
                AssertIcon(icons[slot], preset.icon);
                var save = RequireControl(preset.subMenu, "Save");
                var load = RequireControl(preset.subMenu, "Load");
                var clear = RequireControl(preset.subMenu, "Clear Preset");
                AssertIcon(icons[3], save.icon);
                AssertIcon(icons[4], load.icon);
                AssertIcon(icons[5], clear.icon);
                var saveConfirm = RequireControl(save.subMenu, "Confirm");
                var clearConfirm = RequireControl(clear.subMenu, "Confirm");
                AssertIcon(icons[3], saveConfirm.icon);
                AssertIcon(icons[5], clearConfirm.icon);
                var buttons = new[] { saveConfirm, load, clearConfirm };
                for (int action = 0; action < buttons.Length; action++)
                {
                    Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, buttons[action].type);
                    Assert.AreEqual("ASMLite_Ctrl", buttons[action].parameter?.name);
                    Assert.AreEqual((slot - 1) * 3 + action + 1, buttons[action].value);
                }
            }
        }

        private static void AssertIcon(Texture2D expected, Texture2D actual)
        {
            Assert.IsNotNull(actual, "Missing custom icon " + expected.name + " on the first built avatar.");
            // VRCFury clones/compresses menu textures. Check image content, not object identity.
            var color = expected.GetPixel(0, 0);
            Assert.IsTrue(actual.GetPixels().All(pixel =>
                    Mathf.Abs(pixel.r - color.r) < 0.08f && Mathf.Abs(pixel.g - color.g) < 0.08f
                    && Mathf.Abs(pixel.b - color.b) < 0.08f && Mathf.Abs(pixel.a - color.a) < 0.08f),
                "Wrong custom icon content for " + expected.name + " on the first built avatar.");
        }

        private static VRCExpressionsMenu.Control RequireControl(VRCExpressionsMenu menu, string name)
        {
            Assert.IsNotNull(menu, "Missing built submenu for " + name);
            var matches = menu.controls.Where(control => control.name == name).ToArray();
            Assert.AreEqual(1, matches.Length, "Expected exactly one built menu control named " + name);
            return matches[0];
        }

        private IEnumerator AssertClearResetsSavedAndPreservesExcluded()
        {
            Assert.IsTrue(ASMLiteAv3RuntimeBridge.TryFindRuntime(_builtAvatar, out var runtime, out var diagnostic), diagnostic);
            Assert.IsTrue(ASMLiteAv3RuntimeBridge.TryReadParameter(runtime, ExcludedFloat, ASMLiteAv3ParameterType.Float,
                out var excludedBefore, out diagnostic), diagnostic);
            var defaults = new[]
            {
                (SavedBool, ASMLiteAv3ParameterValue.Bool(false)),
                (DefaultTrueBool, ASMLiteAv3ParameterValue.Bool(true)),
                (LateInt, ASMLiteAv3ParameterValue.Int(7)),
                (SavedFloat, ASMLiteAv3ParameterValue.Float(0.375f)),
            };
            var saved = new[]
            {
                (SavedBool, ASMLiteAv3ParameterValue.Bool(true)),
                (DefaultTrueBool, ASMLiteAv3ParameterValue.Bool(false)),
                (LateInt, ASMLiteAv3ParameterValue.Int(42)),
                (SavedFloat, ASMLiteAv3ParameterValue.Float(-0.625f)),
            };
            foreach (var (name, value) in saved)
                Assert.IsTrue(ASMLiteAv3RuntimeBridge.TryWriteParameter(runtime, name, value, out diagnostic), diagnostic);
            var slot1Saved = saved.Select(p => ("ASMLite_Bak_S1_" + p.Item1, p.Item2)).ToArray();
            var slot2Saved = saved.Select(p => ("ASMLite_Bak_S2_" + p.Item1, p.Item2)).ToArray();
            yield return RunControlAndWaitForValues(runtime, 1, slot1Saved);
            yield return RunControlAndWaitForValues(runtime, 4, slot2Saved);

            // Runtime default parameters may change; Clear still uses configured build-time defaults.
            foreach (var (name, value) in saved)
                Assert.IsTrue(ASMLiteAv3RuntimeBridge.TryWriteParameter(runtime, "ASMLite_Def_" + name, value, out diagnostic), diagnostic);
            var excluded = new[] { (ExcludedFloat, excludedBefore) };
            var slot1Defaults = defaults.Select(p => ("ASMLite_Bak_S1_" + p.Item1, p.Item2)).ToArray();
            yield return RunControlAndWaitForValues(runtime, 3, defaults.Concat(slot1Defaults).Concat(slot2Saved).Concat(excluded).ToArray());

            foreach (var (name, value) in saved)
                Assert.IsTrue(ASMLiteAv3RuntimeBridge.TryWriteParameter(runtime, name, value, out diagnostic), diagnostic);
            yield return RunControlAndWaitForValues(runtime, 2, defaults.Concat(excluded).ToArray());
            yield return RunControlAndWaitForValues(runtime, 5, saved.Concat(excluded).ToArray());
        }

        private static IEnumerator RunControlAndWaitForValues(
            object runtime, int controlValue, (string name, ASMLiteAv3ParameterValue value)[] expected)
        {
            Assert.IsTrue(ASMLiteAv3RuntimeBridge.TryWriteControl(runtime, controlValue, out var diagnostic), diagnostic);
            double deadline = EditorApplication.timeSinceStartup + 10.0d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                yield return null;
                if (ASMLiteAv3RuntimeBridge.TryReadControl(runtime, out int control, out diagnostic) && control == 0
                    && expected.All(p => ASMLiteAv3RuntimeBridge.TryReadParameter(runtime, p.name, p.value.Type, out var actual, out _)
                        && Mathf.Abs(actual.FloatValue - p.value.FloatValue) < 0.0001f))
                    yield break;
            }
            Assert.Fail("First built avatar did not reach expected values after control " + controlValue + ". " + diagnostic);
        }
    }
}
