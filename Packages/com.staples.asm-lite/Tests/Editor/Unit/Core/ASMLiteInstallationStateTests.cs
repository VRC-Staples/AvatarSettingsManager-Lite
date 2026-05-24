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
        public void Resolve_AvatarSelectedNotInstalled_RemainsExplicit()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var state = ASMLite.Editor.ASMLiteInstallationStateService.Resolve(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.NotInstalled, state,
                "Avatar-selected without component or runtime markers should resolve explicitly to NotInstalled state.");
        }

        private static void CleanupStateTestAssets()
        {
            DeleteAssetIfExists("Assets/ASM-Lite/StateTests/TestParams.asset");
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
