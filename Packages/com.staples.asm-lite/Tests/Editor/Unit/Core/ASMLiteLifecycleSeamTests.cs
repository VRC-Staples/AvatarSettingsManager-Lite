using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    public sealed class ASMLiteLifecycleSeamTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test]
        public void DirectDeliveryRollbackSnapshot_RestoresDescriptorAssetsAfterMutation()
        {
            ASMLiteTestFixtures.SetExpressionParams(
                _ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "ASMLite_BeforeSnapshot",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    saved = true,
                    networkSynced = true,
                });
            var originalMenu = _ctx.AvDesc.expressionsMenu;
            var originalFx = (AnimatorController)_ctx.AvDesc.baseAnimationLayers[4].animatorController;

            using (var snapshot = ASMLiteDirectDeliveryRollbackSnapshot.Capture(_ctx.AvDesc))
            {
                _ctx.AvDesc.expressionParameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                    {
                        name = "ASMLite_AfterMutation",
                        valueType = VRCExpressionParameters.ValueType.Int,
                        saved = true,
                        networkSynced = true,
                    },
                };
                _ctx.AvDesc.expressionsMenu = null;
                var layers = _ctx.AvDesc.baseAnimationLayers;
                layers[4].animatorController = null;
                _ctx.AvDesc.baseAnimationLayers = layers;

                Assert.IsTrue(snapshot.TryRestore(_ctx.AvDesc, out string failureContext, out string failureRemediation),
                    $"Snapshot restore should succeed for a live avatar. context={failureContext}, remediation={failureRemediation}");
            }

            Assert.AreSame(originalMenu, _ctx.AvDesc.expressionsMenu,
                "Rollback snapshot should restore the descriptor expressions menu reference.");
            Assert.AreSame(originalFx, _ctx.AvDesc.baseAnimationLayers[4].animatorController,
                "Rollback snapshot should restore the descriptor FX controller reference.");
            Assert.AreEqual("ASMLite_BeforeSnapshot", _ctx.AvDesc.expressionParameters.parameters[0].name,
                "Rollback snapshot should restore the serialized parameter asset contents.");
        }

        [Test]
        public void DirectDeliveryVerification_ReportsMissingRuntimeMarkers()
        {
            bool verified = ASMLiteLifecycleVerification.VerifyDirectDeliveryState(
                _ctx.AvDesc,
                ASMLiteInstallationState.Detached,
                vendorizedDir: string.Empty,
                out string failureMessage,
                out string failureContext);

            Assert.IsFalse(verified,
                "A detached direct-delivery avatar without generated runtime markers should not verify.");
            StringAssert.Contains("runtime markers", failureMessage);
            Assert.AreEqual(_ctx.AvatarGo.name, failureContext);
        }

        [Test]
        public void GeneratedAssetBuildTransaction_ReportsBuilderDiagnosticsWithoutThrowing()
        {
            _ctx.Comp.slotCount = 0;
            LogAssert.Expect(LogType.Error, "[ASM-Lite] slotCount must be between 1 and 8 (got 0).");

            var result = ASMLiteGeneratedAssetBuildTransaction.Execute(_ctx.Comp);

            Assert.IsFalse(result.Success,
                "Lifecycle build planning should surface builder validation failures through a focused transaction result.");
            Assert.AreEqual(-1, result.DiscoveredParamCount);
            Assert.IsNotNull(result.Diagnostic);
            Assert.AreEqual(ASMLiteDiagnosticCodes.Build.ValidationFailed, result.Diagnostic.Code);
            Assert.AreEqual("slotCount", result.ContextPath);
            StringAssert.Contains("slotCount", result.Message);
        }
    }
}
