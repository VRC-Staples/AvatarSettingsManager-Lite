using System;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("GraphicsRequired")]
    public sealed class ASMLiteParameterBudgetPipelineTests
    {
        private AsmLiteTestContext _ctx;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _baseline;

        [SetUp]
        public void SetUp()
        {
            _baseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(nameof(ASMLiteParameterBudgetPipelineTests));
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteBudgetLateAddition.Target = null;
            if (_ctx != null) ASMLiteTestFixtures.TearDownTestAvatar(_ctx.AvatarGo);
            _ctx = null;
            _baseline?.Restore();
        }

        [Test]
        public void SdkPreprocess_GuardedAvatarWithinBudget_AcceptsReviewedLateCallbacks()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            ConfigurePipelineAvatar();
            Assert.IsTrue(VRCBuildPipelineCallbacks.OnPreprocessAvatar(_ctx.AvatarGo));
            Assert.LessOrEqual(_ctx.AvDesc.expressionParameters.parameters.Length, ASMLiteParameterBudget.Limit);
        }

        [TestCase("com.vrcfury.vrcfury", "1.1334.0", "VF.Hooks.PreProcessingFailureCheckHook+FailureCheckEnd", true)]
        [TestCase("com.vrcfury.vrcfury", "1.1430.0", "VF.Hooks.PreProcessingFailureCheckHook+FailureCheckEnd", true)]
        [TestCase("com.vrcfury.vrcfury", "1.1431.0", "VF.Hooks.PreProcessingFailureCheckHook+FailureCheckEnd", false)]
        [TestCase("com.vrcfury.vrcfury", "1.1430.0", "VF.Hooks.VrcsdkFixes.PlayModeContactFixHook+PlayerBuilt", false)]
        [TestCase("nadena.dev.ndmf", "1.14.8", "nadena.dev.ndmf.VRChat.ForceReinitVRCConstraintsHook", true)]
        [TestCase("nadena.dev.ndmf", "1.14.8", "nadena.dev.ndmf.VRChat.ForceReinitPhysBonesHook", true)]
        [TestCase("nadena.dev.ndmf", "1.14.9", "nadena.dev.ndmf.VRChat.ForceReinitPhysBonesHook", false)]
        [TestCase("nadena.dev.modular-avatar", "1.18.7", "nadena.dev.modular_avatar.core.editor.ReplacementRemoveIEditorOnly", true)]
        [TestCase("nadena.dev.modular-avatar", "1.18.8", "nadena.dev.modular_avatar.core.editor.ReplacementRemoveIEditorOnly", false)]
        [TestCase("unreviewed.package", "1.1430.0", "VF.Hooks.PreProcessingFailureCheckHook+FailureCheckEnd", false)]
        [TestCase("com.vrcfury.vrcfury", "1.1430.0", "UnknownLateProcessor", false)]
        [TestCase(null, null, "VF.Hooks.PreProcessingFailureCheckHook+FailureCheckEnd", false)]
        public void LateCallbackPolicy_RequiresReviewedPackageVersionAndType(string package, string version, string type, bool allowed)
        {
            Assert.AreEqual(allowed, ASMLiteParameterBudgetBuildGuard.IsReviewedNonSchemaCallback(package, version, type));
        }

        [Test]
        public void SdkPreprocess_BakedOnlyAvatar_RejectsParametersAddedAfterCompression()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            ConfigurePipelineAvatar();
            AssertLateProcessedCount(allowed: false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SdkPreprocess_AttachedOrVendorizedAvatar_RejectsLateOverflow(bool vendorized)
        {
            ConfigurePipelineAvatar();
            Assert.IsTrue(ASMLiteBuilder.TryBuildWithDiagnostics(_ctx.Comp, out _).Success);
            if (vendorized)
            {
                var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
                try
                {
                    window.SelectAvatarForAutomation(_ctx.AvDesc);
                    window.VendorizeForAutomation();
                    string dir = _ctx.Comp.vendorizedGeneratedAssetsPath;
                    _ctx.AvDesc.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(dir + "/ASMLite_Params.asset");
                    _ctx.AvDesc.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(dir + "/ASMLite_Menu.asset");
                    UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
                    Assert.IsTrue(ASMLiteParameterBudgetBuildGuard.HasClearBakedEvidence(_ctx.AvDesc), "Owned zero-backup payload must remain recognizable.");
                }
                finally { UnityEngine.Object.DestroyImmediate(window); }
            }
            AssertLateProcessedCount(allowed: false);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SdkPreprocess_UnrelatedOrAmbiguousAvatar_IsNotHardBlocked(bool ambiguous)
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            ConfigurePipelineAvatar();
            _ctx.ParamsAsset.parameters = Enumerable.Range(0, 3).Select(i => Parameter(
                (ambiguous ? "ASMLite_Unrelated_" : "User_") + i, VRCExpressionParameters.ValueType.Bool)).ToArray();
            _ctx.MenuAsset.controls.Clear();
            _ctx.MenuAsset.controls.Add(new VRCExpressionsMenu.Control { name = "Settings Manager", type = VRCExpressionsMenu.Control.ControlType.SubMenu });
            AssertLateProcessedCount(allowed: true);
        }

        [Test]
        public void Guard_NestedCapture_KeepsOuterEligibilityAndDoesNotGuardUnrelatedAvatar()
        {
            _ctx.ParamsAsset.parameters = Enumerable.Range(0, 8193).Select(i => Parameter("User_" + i, VRCExpressionParameters.ValueType.Bool)).ToArray();
            var unrelated = new GameObject("UnrelatedBudgetAvatar");
            try
            {
                unrelated.AddComponent<VRCAvatarDescriptor>().expressionParameters = _ctx.ParamsAsset;
                ASMLiteParameterBudgetBuildGuard.Capture(_ctx.AvatarGo);
                ASMLiteParameterBudgetBuildGuard.Capture(unrelated);
                Assert.IsTrue(ASMLiteParameterBudgetBuildGuard.Validate(unrelated));
                LogAssert.Expect(LogType.Error, new Regex("BUILD-304"));
                Assert.IsFalse(ASMLiteParameterBudgetBuildGuard.Validate(_ctx.AvatarGo));
                ASMLiteParameterBudgetBuildGuard.Capture(unrelated);
                Assert.IsTrue(ASMLiteParameterBudgetBuildGuard.Validate(unrelated), "A failed guarded build must not transfer its scope.");
            }
            finally { UnityEngine.Object.DestroyImmediate(unrelated); }
        }

        private void ConfigurePipelineAvatar()
        {
            _ctx.AvatarGo.AddComponent<Animator>();
            _ctx.AvDesc.customExpressions = true;
            _ctx.AvDesc.customizeAnimationLayers = true;
            _ctx.AvDesc.baseAnimationLayers = new[]
            {
                new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false, isEnabled = true, animatorController = _ctx.Ctrl,
                },
            };
            _ctx.AvDesc.specialAnimationLayers = Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
            _ctx.ParamsAsset.parameters = new[]
            {
                Parameter("ASMLite_Ctrl", VRCExpressionParameters.ValueType.Int),
                Parameter("ASMLite_Def_User", VRCExpressionParameters.ValueType.Bool),
                Parameter("ASMLite_Bak_S1_User", VRCExpressionParameters.ValueType.Bool),
            };
            _ctx.MenuAsset.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Load", type = VRCExpressionsMenu.Control.ControlType.Button,
                parameter = new VRCExpressionsMenu.Control.Parameter { name = "ASMLite_Ctrl" }, value = 2,
            });
        }

        [Test]
        public void Guard_UnreadableProcessedSchema_RejectsWithActionableDiagnostic()
        {
            ASMLiteParameterBudgetBuildGuard.Capture(_ctx.AvatarGo);
            _ctx.ParamsAsset.parameters = null;
            LogAssert.Expect(LogType.Error, new Regex("BUILD-305.*processed expression schema is unreadable"));
            Assert.IsFalse(ASMLiteParameterBudgetBuildGuard.Validate(_ctx.AvatarGo));
        }

        [Test]
        public void Guard_DestroyedCapturedAvatar_DoesNotBecomeUnguarded()
        {
            var avatar = new GameObject("DestroyedBudgetAvatar");
            avatar.AddComponent<VRCAvatarDescriptor>();
            avatar.AddComponent<ASMLiteComponent>();
            ASMLiteParameterBudgetBuildGuard.Capture(avatar);
            UnityEngine.Object.DestroyImmediate(avatar);
            LogAssert.Expect(LogType.Error, new Regex("BUILD-305.*processed expression schema is unreadable"));
            Assert.IsFalse(ASMLiteParameterBudgetBuildGuard.Validate(avatar));
        }

        private void AssertLateProcessedCount(bool allowed)
        {
            ASMLiteBudgetLateAddition.Target = _ctx.AvatarGo;
            if (!allowed)
            {
                LogAssert.Expect(LogType.Error, new Regex("BUILD-304"));
                LogAssert.Expect(LogType.Error, new Regex("ASMLiteParameterBudgetFinalCallback.*reported a failure"));
                LogAssert.Expect(LogType.Assert, new Regex("Cancelling DisplayDialog: Preprocess Callback Failed"));
            }
            Assert.AreEqual(allowed, VRCBuildPipelineCallbacks.OnPreprocessAvatar(_ctx.AvatarGo));
            Assert.AreEqual(8193, _ctx.AvDesc.expressionParameters.parameters.Length,
                "The late callback must run after VRCFury compression before final validation.");
        }

        internal static VRCExpressionParameters.Parameter Parameter(string name, VRCExpressionParameters.ValueType type)
            => new VRCExpressionParameters.Parameter { name = name, valueType = type, networkSynced = false };
    }

    // Test-only late processor: no work on any avatar outside this fixture's exact target.
    public sealed class ASMLiteBudgetLateAddition : IVRCSDKPreprocessAvatarCallback
    {
        public static GameObject Target;
        public int callbackOrder => int.MaxValue - 50;
        public bool OnPreprocessAvatar(GameObject avatar)
        {
            if (avatar != Target || Target == null) return true;
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            var copy = UnityEngine.Object.Instantiate(descriptor.expressionParameters);
            copy.parameters = descriptor.expressionParameters.parameters.Concat(
                Enumerable.Range(0, 8193 - copy.parameters.Length).Select(i => ASMLiteParameterBudgetPipelineTests.Parameter(
                    "LateBudget_" + i, VRCExpressionParameters.ValueType.Bool))).ToArray();
            descriptor.expressionParameters = copy;
            return true;
        }
    }
}
