using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteInstallationStateTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            CleanupStateTestAssets();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                CleanupStateTestAssets();
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            }
            finally
            {
                CleanupStateTestAssets();
                _ctx = null;
            }
        }

        [Test]
        public void Resolve_ComponentPresentVendorized_WinsOverAvatarHeuristics()
        {
            _ctx.Comp.useVendorizedGeneratedAssets = true;

            var state = ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, _ctx.Comp);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.Vendorized, state,
                "Component-present vendorized combinations should resolve explicitly to Vendorized state.");
        }

        [Test]
        public void Resolve_ComponentMissingDetachedDetectedFromRuntimeMarkers()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            ASMLiteTestFixtures.AddExpressionParam(
                _ctx,
                ASMLite.Editor.ASMLiteBuilder.CtrlParam,
                VRCExpressionParameters.ValueType.Int,
                defaultValue: 0f,
                saved: false,
                networkSynced: false);

            var state = ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.Detached, state,
                "Component-missing avatars with ASM-Lite runtime markers should resolve explicitly to Detached state.");
        }

        [Test]
        public void Resolve_ComponentMissingVendorizedReferences_ReturnsVendorizedBeforeDetachedMarkers()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            EnsureAssetFolder("Assets", "ASM-Lite");
            EnsureAssetFolder("Assets/ASM-Lite", "StateTests");
            DeleteAssetIfExists("Assets/ASM-Lite/StateTests/TestParams.asset");

            var vendorizedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            vendorizedParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = ASMLite.Editor.ASMLiteBuilder.CtrlParam,
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
            };
            AssetDatabase.CreateAsset(vendorizedParams, "Assets/ASM-Lite/StateTests/TestParams.asset");
            AssetDatabase.SaveAssets();
            _ctx.AvDesc.expressionParameters = vendorizedParams;

            var state = ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.Vendorized, state,
                "Descriptor references under Assets/ASM-Lite should preserve Vendorized ownership semantics even when runtime markers are present.");
        }

        [Test]
        public void Resolve_VendorizedReferencesWithoutRuntimeMarkers_DoesNotVerifyDirectDelivery()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;
            _ctx.AvDesc.baseAnimationLayers = null;

            EnsureAssetFolder("Assets", "ASM-Lite");
            EnsureAssetFolder("Assets/ASM-Lite", "StateTests");
            var vendorizedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            vendorizedParams.parameters = new VRCExpressionParameters.Parameter[0];
            AssetDatabase.CreateAsset(vendorizedParams, "Assets/ASM-Lite/StateTests/TestParams.asset");
            _ctx.AvDesc.expressionParameters = vendorizedParams;

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.Vendorized,
                ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, null));
            Assert.IsFalse(ASMLite.Editor.ASMLiteInstallationStateService.HasAsmLiteRuntimeMarkers(_ctx.AvDesc));

            bool verified = ASMLite.Editor.ASMLiteLifecycleVerification.VerifyDirectDeliveryState(
                _ctx.Comp, _ctx.AvDesc, ASMLite.Editor.ASMLiteInstallationState.Vendorized,
                "Assets/ASM-Lite/StateTests", out string failureMessage, out string failureContext);

            Assert.IsFalse(verified,
                "Vendorized classification alone must not prove that direct delivery installed runtime markers.");
            StringAssert.Contains("required direct-delivery content", failureMessage);
            Assert.AreEqual(_ctx.AvatarGo.name, failureContext);
        }

        [Test]
        public void Resolve_AvatarSelectedNotInstalled_RemainsExplicit()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var state = ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.NotInstalled, state,
                "Avatar-selected without component or runtime markers should resolve explicitly to NotInstalled state.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void Resolve_NestedVendorizedMenu_UsesOwnershipPolicyWithAuthoredAssets(bool cyclic, bool vendorized)
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;
            var root = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var child = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            try
            {
                root.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "User Tools", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = child,
                });
                if (cyclic)
                    child.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = "Back", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = root,
                    });
                if (vendorized)
                {
                    EnsureAssetFolder("Assets", "ASM-Lite");
                    EnsureAssetFolder("Assets/ASM-Lite", "StateTests");
                    var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    AssetDatabase.CreateAsset(menu, "Assets/ASM-Lite/StateTests/TestMenu.asset");
                    child.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = "Presets", type = VRCExpressionsMenu.Control.ControlType.SubMenu, subMenu = menu,
                    });
                }
                _ctx.AvDesc.expressionsMenu = root;
                Assert.IsFalse(ASMLite.Editor.ASMLiteGeneratedOwnershipPolicy.PathStartsWith(
                    AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters), "Assets/ASM-Lite"));
                Assert.AreEqual(vendorized, ASMLite.Editor.ASMLiteGeneratedOwnershipPolicy.HasVendorizedReferences(_ctx.AvDesc));
                Assert.AreEqual(vendorized ? ASMLite.Editor.ASMLiteInstallationState.Vendorized
                    : ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                    ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, null));
            }
            finally
            {
                _ctx.AvDesc.expressionsMenu = _ctx.MenuAsset;
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(root);
            }
        }

        private static void CleanupStateTestAssets()
        {
            DeleteAssetIfExists("Assets/ASM-Lite/StateTests/TestParams.asset");
            DeleteAssetIfExists("Assets/ASM-Lite/StateTests/TestMenu.asset");
            DeleteAssetIfExists("Assets/ASM-Lite/StateTests");
            DeleteAssetIfExists("Assets/ASM-Lite");
        }

        private static void EnsureAssetFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static void DeleteAssetIfExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path) && AssetDatabase.LoadAssetAtPath<Object>(path) == null)
                return;

            if (AssetDatabase.IsValidFolder(path)
                && AssetDatabase.FindAssets(string.Empty, new[] { path }).Length > 0)
                return;

            AssetDatabase.DeleteAsset(path);
        }
    }
}
