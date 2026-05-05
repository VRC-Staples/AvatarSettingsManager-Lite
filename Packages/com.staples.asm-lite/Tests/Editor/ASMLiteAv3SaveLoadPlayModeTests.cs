using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteAv3SaveLoadPlayModeTests
    {
        private const string TestAvatarName = "ASMLite_AV3_SaveLoad_P0_Avatar";
        private const string MergedParamsPath = "Assets/ASMLiteTests_Temp/ASMLiteAv3SaveLoadP0MergedParams.asset";
        private const string VrcFuryPlayModeEditorPref = "com.vrcfury.playMode";

        private static readonly string[] SavedParameterNames =
        {
            "ASMTest_BoolSaved_A",
            "ASMTest_BoolSaved_B",
            "ASMTest_IntSaved_A",
            "ASMTest_IntSaved_B",
            "ASMTest_FloatSaved_A",
            "ASMTest_FloatSaved_B",
        };

        private static readonly string[] UnsavedParameterNames =
        {
            "ASMTest_BoolUnsaved_A",
            "ASMTest_BoolUnsaved_B",
            "ASMTest_IntUnsaved_A",
            "ASMTest_IntUnsaved_B",
            "ASMTest_FloatUnsaved_A",
            "ASMTest_FloatUnsaved_B",
        };

        private AsmLiteTestContext _ctx;
        private bool _hadVrcFuryPlayModePref;
        private bool _previousVrcFuryPlayMode;
        private bool _disabledVrcFuryPlayMode;

        [TearDown]
        public void TearDown()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                DestroyTestAvatar();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();

            DestroyTestAvatar();
        }

        [Test]
        public void P0_RuntimeBridge_MissingRuntimeDiagnostic_NamesExpectedAv3AssemblyAndType()
        {
            var result = ASMLiteAv3RuntimeBridge.ResolveRuntimeType(
                "Lyuma.Av3Emulator.Runtime.DoesNotExistForASMLiteP0",
                ASMLiteAv3RuntimeBridge.ExpectedRuntimeAssemblyName);

            Assert.IsFalse(result.IsAvailable, "P0: intentionally missing AV3 runtime type should not resolve.");
            StringAssert.Contains("Lyuma.Av3Emulator.Runtime.DoesNotExistForASMLiteP0", result.Diagnostic);
            StringAssert.Contains(ASMLiteAv3RuntimeBridge.ExpectedRuntimeAssemblyName, result.Diagnostic);
        }

        [UnityTest]
        public IEnumerator P0_Av3Runtime_ExposesSavedUnsavedAndControlParameters_AfterPlayModeStart()
        {
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            BuildAndWireAvatarFixture();
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return new EnterPlayMode();

            GameObject avatar = GameObject.Find(TestAvatarName);
            Assert.IsNotNull(avatar, "P0: test avatar should survive EnterPlayMode for AV3 runtime scanning.");

            string[] requiredNames = SavedParameterNames
                .Concat(UnsavedParameterNames)
                .Concat(new[] { ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName })
                .ToArray();

            ASMLiteAv3RuntimeBridge.ParameterSnapshot snapshot = default;
            string lastDiagnostic = string.Empty;
            double deadline = EditorApplication.timeSinceStartup + 10.0d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (ASMLiteAv3RuntimeBridge.TryCaptureVisibleParameters(avatar, out snapshot, out lastDiagnostic)
                    && requiredNames.All(snapshot.Contains))
                {
                    break;
                }

                yield return null;
            }

            Assert.IsTrue(requiredNames.All(snapshot.Contains),
                "P0: AV3 runtime did not expose every required saved/unsaved/control parameter before timeout. "
                + $"Missing=[{string.Join(", ", requiredNames.Where(name => !snapshot.Contains(name)))}]. "
                + $"LastDiagnostic={lastDiagnostic}. Visible=[{string.Join(", ", snapshot.AllNames.OrderBy(name => name, StringComparer.Ordinal))}]");
        }

        private void BuildAndWireAvatarFixture()
        {
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "P0: fixture creation returned null context.");
            _ctx.AvatarGo.name = TestAvatarName;
            _ctx.Comp.slotCount = 1;
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = UnsavedParameterNames.ToArray();

            AddVisibilityParameters();

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(SavedParameterNames.Length, buildResult,
                $"P0: Build should discover only saved/non-excluded parameters. got {buildResult}.");

            var generatedParams = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedParams, $"P0: generated expression params missing at {ASMLiteAssetPaths.ExprParams}.");
            Assert.IsTrue((generatedParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                    .Any(parameter => parameter != null && parameter.name == ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName),
                "P0: generated expression params must include ASMLite_Ctrl before AV3 visibility check.");

            var generatedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(generatedController, $"P0: generated FX controller missing at {ASMLiteAssetPaths.FXController}.");

            var mergedParams = BuildMergedParameters(_ctx.ParamsAsset, generatedParams);
            AssetDatabase.DeleteAsset(MergedParamsPath);
            AssetDatabase.CreateAsset(mergedParams, MergedParamsPath);
            AssetDatabase.SaveAssets();

            _ctx.AvDesc.expressionParameters = mergedParams;
            WireFxController(_ctx.AvDesc, generatedController);
            DisableVrcFuryPlayModeProcessing();
            EditorUtility.SetDirty(_ctx.AvDesc);
        }

        private void AddVisibilityParameters()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolSaved_A", VRCExpressionParameters.ValueType.Bool, 1f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolSaved_B", VRCExpressionParameters.ValueType.Bool, 0f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntSaved_A", VRCExpressionParameters.ValueType.Int, 1f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntSaved_B", VRCExpressionParameters.ValueType.Int, 2f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatSaved_A", VRCExpressionParameters.ValueType.Float, 0.25f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatSaved_B", VRCExpressionParameters.ValueType.Float, 0.75f, saved: true);

            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolUnsaved_A", VRCExpressionParameters.ValueType.Bool, 1f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolUnsaved_B", VRCExpressionParameters.ValueType.Bool, 0f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntUnsaved_A", VRCExpressionParameters.ValueType.Int, 3f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntUnsaved_B", VRCExpressionParameters.ValueType.Int, 4f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatUnsaved_A", VRCExpressionParameters.ValueType.Float, 0.33f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatUnsaved_B", VRCExpressionParameters.ValueType.Float, 0.66f, saved: false);
        }

        private static VRCExpressionParameters BuildMergedParameters(
            VRCExpressionParameters avatarParams,
            VRCExpressionParameters generatedParams)
        {
            var merged = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var byName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);

            foreach (var parameter in avatarParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
            {
                if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                    byName[parameter.name] = parameter;
            }

            foreach (var parameter in generatedParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
            {
                if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                    byName[parameter.name] = parameter;
            }

            merged.parameters = byName.Values.ToArray();
            return merged;
        }

        private static void WireFxController(VRCAvatarDescriptor descriptor, AnimatorController controller)
        {
            var layers = descriptor.baseAnimationLayers;
            if (layers == null || layers.Length < 5)
                Array.Resize(ref layers, 5);

            layers[0] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Base);
            layers[1] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Additive);
            layers[2] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Gesture);
            layers[3] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Action);
            layers[4] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.FX, controller);

            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = layers;
            descriptor.specialAnimationLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
        }

        private static VRCAvatarDescriptor.CustomAnimLayer CreateLayer(
            VRCAvatarDescriptor.AnimLayerType type,
            AnimatorController controller = null)
        {
            return new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = type,
                isDefault = controller == null,
                isEnabled = true,
                animatorController = controller,
            };
        }

        private void DisableVrcFuryPlayModeProcessing()
        {
            if (_disabledVrcFuryPlayMode)
                return;

            _hadVrcFuryPlayModePref = EditorPrefs.HasKey(VrcFuryPlayModeEditorPref);
            _previousVrcFuryPlayMode = EditorPrefs.GetBool(VrcFuryPlayModeEditorPref, true);
            EditorPrefs.SetBool(VrcFuryPlayModeEditorPref, false);
            _disabledVrcFuryPlayMode = true;
        }

        private void RestoreVrcFuryPlayModeProcessing()
        {
            if (!_disabledVrcFuryPlayMode)
                return;

            if (_hadVrcFuryPlayModePref)
                EditorPrefs.SetBool(VrcFuryPlayModeEditorPref, _previousVrcFuryPlayMode);
            else
                EditorPrefs.DeleteKey(VrcFuryPlayModeEditorPref);

            _disabledVrcFuryPlayMode = false;
        }

        private void DestroyTestAvatar()
        {
            RestoreVrcFuryPlayModeProcessing();
            var avatar = _ctx?.AvatarGo != null ? _ctx.AvatarGo : GameObject.Find(TestAvatarName);
            ASMLiteTestFixtures.TearDownTestAvatar(avatar);
            AssetDatabase.DeleteAsset(MergedParamsPath);
            _ctx = null;
        }
    }
}
