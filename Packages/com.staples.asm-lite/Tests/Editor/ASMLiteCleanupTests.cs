using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A35-A41: Cleanup integration invariants for Remove Prefab flow.
    /// These tests seed avatar assets via Build(), then verify CleanUpAvatarAssets()
    /// removes only ASM-Lite generated content while preserving user-owned artifacts.
    /// </summary>
    [TestFixture]
    public class ASMLiteCleanupTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A35: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A35: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A35: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.Ctrl, "A35: fixture did not create FX AnimatorController.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddAvatarParam(AsmLiteTestContext ctx, string name, VRCExpressionParameters.ValueType type, float defaultValue = 0f)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRCExpressionParameters.Parameter[0];
            var updated = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = true,
                networkSynced = true,
            };
            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteLayers(AsmLiteTestContext ctx, int slotCount)
        {
            for (int slot = 1; slot <= slotCount; slot++)
            {
                ctx.Ctrl.AddLayer(new AnimatorControllerLayer
                {
                    name = $"ASMLite_Slot{slot}",
                    defaultWeight = 1f,
                    stateMachine = new AnimatorStateMachine { name = $"ASMLite_Slot{slot}_SM" }
                });
            }
            EditorUtility.SetDirty(ctx.Ctrl);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteFxParams(AsmLiteTestContext ctx, params string[] paramNames)
        {
            ctx.Ctrl.AddParameter("ASMLite_Ctrl", AnimatorControllerParameterType.Int);
            foreach (var name in paramNames)
                ctx.Ctrl.AddParameter($"ASMLite_Bak_S1_{name}", AnimatorControllerParameterType.Float);
            EditorUtility.SetDirty(ctx.Ctrl);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteExprParams(AsmLiteTestContext ctx, params string[] paramNames)
        {
            var existing = ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0];
            var list = new System.Collections.Generic.List<VRCExpressionParameters.Parameter>(existing);
            list.Add(new VRCExpressionParameters.Parameter { name = "ASMLite_Ctrl", valueType = VRCExpressionParameters.ValueType.Int });
            foreach (var name in paramNames)
                list.Add(new VRCExpressionParameters.Parameter { name = $"ASMLite_Bak_S1_{name}", valueType = VRCExpressionParameters.ValueType.Float });
            ctx.AvDesc.expressionParameters.parameters = list.ToArray();
            EditorUtility.SetDirty(ctx.AvDesc.expressionParameters);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteMenu(AsmLiteTestContext ctx)
        {
            ctx.AvDesc.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Settings Manager",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            });
            EditorUtility.SetDirty(ctx.AvDesc.expressionsMenu);
            AssetDatabase.SaveAssets();
        }

        private static int BuildOrFail(AsmLiteTestContext ctx, string aid)
        {
            int buildResult = ASMLiteBuilder.Build(ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult}.");
            return buildResult;
        }

        private static int CountASMLiteLayers(AnimatorController ctrl)
            => ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_"));

        private static int CountASMLiteFxParams(AnimatorController ctrl)
            => ctrl.parameters.Count(p => p.name != null
                && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"));

        private static int CountASMLiteExprParams(VRCExpressionParameters exprParams)
        {
            var items = exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0];
            return items.Count(p => p != null
                && p.name != null
                && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"));
        }

        private static int CountSettingsManagerControls(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu?.controls == null) return 0;
            return rootMenu.controls.Count(c => c != null
                && c.name == "Settings Manager"
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu);
        }

        private static int FindFxLayerIndex(VRCAvatarDescriptor avDesc)
        {
            for (int i = 0; i < avDesc.baseAnimationLayers.Length; i++)
            {
                if (avDesc.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    return i;
            }
            return -1;
        }

        // ── A35 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A35_Cleanup_RemovesASMLiteFxlayers()
        {
            _ctx.Comp.slotCount = 2;
            AddAvatarParam(_ctx, "MyInt", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteLayers(_ctx, 2);

            int before = CountASMLiteLayers(_ctx.Ctrl);
            Assert.Greater(before, 0,
                $"A35: setup failure, expected seeded ASMLite_ FX layers before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteLayers(_ctx.Ctrl);
            Assert.AreEqual(0, after,
                $"A35: cleanup must remove all ASMLite_ FX layers. before={before}, after={after}.");
        }

        // ── A36 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A36_Cleanup_RemovesASMLiteFxParametersAndCtrlParam()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float);
            SeedLegacyAsmLiteFxParams(_ctx, "MyFloat");

            int before = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.Greater(before, 0,
                $"A36: setup failure, expected seeded ASMLite_ FX params before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.AreEqual(0, after,
                $"A36: cleanup must remove ASMLite_ FX params and ASMLite_Ctrl. before={before}, after={after}.");
        }

        // ── A37 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A37_Cleanup_RemovesASMLiteExpressionParametersAndCtrlParam()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyBool", VRCExpressionParameters.ValueType.Bool, 1f);
            SeedLegacyAsmLiteExprParams(_ctx, "MyBool");

            int before = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.Greater(before, 0,
                $"A37: setup failure, expected seeded ASMLite_ expression params before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(0, after,
                $"A37: cleanup must remove ASMLite_ expression params and ASMLite_Ctrl. before={before}, after={after}.");
        }

        // ── A38 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A38_Cleanup_RemovesSettingsManagerRootControl()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteMenu(_ctx);

            int before = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.Greater(before, 0,
                $"A38: setup failure, expected Settings Manager control before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(0, after,
                $"A38: cleanup must remove Settings Manager root control. before={before}, after={after}.");
        }

        // ── A39 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A39_Cleanup_PreservesUserOwnedFxParamsAndMenuEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "SeedParam", VRCExpressionParameters.ValueType.Float, 0.25f);
            BuildOrFail(_ctx, "A39");

            // Seed user-owned FX layer and parameter (non-ASMLite namespace)
            _ctx.Ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = "User_CustomLayer",
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine { name = "UserSM" }
            });
            _ctx.Ctrl.AddParameter("User_CustomParam", AnimatorControllerParameterType.Float);

            // Seed user-owned expression parameter
            var existingExpr = _ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0];
            var mergedExpr = new VRCExpressionParameters.Parameter[existingExpr.Length + 1];
            existingExpr.CopyTo(mergedExpr, 0);
            mergedExpr[existingExpr.Length] = new VRCExpressionParameters.Parameter
            {
                name = "UserExprParam",
                valueType = VRCExpressionParameters.ValueType.Float,
                defaultValue = 0.75f,
                saved = true,
                networkSynced = true,
            };
            _ctx.AvDesc.expressionParameters.parameters = mergedExpr;

            // Seed user-owned root menu control
            _ctx.AvDesc.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "User Control",
                type = VRCExpressionsMenu.Control.ControlType.Button,
            });

            EditorUtility.SetDirty(_ctx.Ctrl);
            EditorUtility.SetDirty(_ctx.AvDesc.expressionParameters);
            EditorUtility.SetDirty(_ctx.AvDesc.expressionsMenu);
            AssetDatabase.SaveAssets();

            // Preconditions must be explicit to avoid vacuous pass
            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "A39: setup failure, user FX layer missing before cleanup.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "A39: setup failure, user FX param missing before cleanup.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "A39: setup failure, user expression param missing before cleanup.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "A39: setup failure, user menu control missing before cleanup.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "A39: cleanup removed user FX layer unexpectedly.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "A39: cleanup removed user FX param unexpectedly.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "A39: cleanup removed user expression param unexpectedly.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "A39: cleanup removed user menu control unexpectedly.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "A39: cleanup must still remove Settings Manager while preserving user controls.");
        }

        // ── A40 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A40_Cleanup_NullAvatarDescriptor_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(null),
                "A40: cleanup must no-op when avatar descriptor is null.");
        }

        // ── A41 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A41_Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteExprParams(_ctx, "MyParam");
            SeedLegacyAsmLiteMenu(_ctx);

            Assert.Greater(CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), 0,
                "A41: setup failure, expected ASMLite expression params before cleanup.");
            Assert.Greater(CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu), 0,
                "A41: setup failure, expected Settings Manager before cleanup.");

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "A41: setup failure, FX layer not found in avatar descriptor.");

            // Scenario 1: null controller
            var nullCtrlLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            nullCtrlLayer.animatorController = null;
            nullCtrlLayer.isDefault = false;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = nullCtrlLayer;

            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc),
                "A41: cleanup must not throw when FX controller is null.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters),
                "A41: cleanup should still remove ASMLite expression params when FX controller is null.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "A41: cleanup should still remove Settings Manager when FX controller is null.");

            // Re-seed for scenario 2: default FX layer flag with null controller
            SeedLegacyAsmLiteExprParams(_ctx, "MyParam");
            SeedLegacyAsmLiteMenu(_ctx);
            Assert.Greater(CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), 0,
                "A41: re-seed failure, expected ASMLite expression params before default-FX cleanup.");

            var defaultLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            defaultLayer.isDefault = true;
            defaultLayer.animatorController = null;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = defaultLayer;

            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc),
                "A41: cleanup must not throw when FX layer is default/unassigned.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters),
                "A41: cleanup should still remove ASMLite expression params when FX layer is default/unassigned.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "A41: cleanup should still remove Settings Manager when FX layer is default/unassigned.");
        }

        // ── A54 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A54_Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps()
        {
            _ctx.Comp.slotCount = 2;
            AddAvatarParam(_ctx, "A54_Int", VRCExpressionParameters.ValueType.Int);
            AddAvatarParam(_ctx, "A54_Float", VRCExpressionParameters.ValueType.Float, 0.5f);
            SeedLegacyAsmLiteLayers(_ctx, 2);
            SeedLegacyAsmLiteFxParams(_ctx, "A54_Int", "A54_Float");
            SeedLegacyAsmLiteExprParams(_ctx, "A54_Int", "A54_Float");
            SeedLegacyAsmLiteMenu(_ctx);

            var first = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_ctx.AvDesc);
            int firstTotalRemoved = first.FxLayersRemoved + first.FxParamsRemoved + first.ExprParamsRemoved + first.MenuControlsRemoved;
            Assert.Greater(firstTotalRemoved, 0,
                $"A54: first cleanup should remove legacy direct-injection-era state. fxLayers={first.FxLayersRemoved}, fxParams={first.FxParamsRemoved}, expr={first.ExprParamsRemoved}, menu={first.MenuControlsRemoved}.");

            var second = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_ctx.AvDesc);
            Assert.AreEqual(0, second.FxLayersRemoved,
                $"A54: second cleanup should be a deterministic no-op for FX layers. removed={second.FxLayersRemoved}.");
            Assert.AreEqual(0, second.FxParamsRemoved,
                $"A54: second cleanup should be a deterministic no-op for FX params. removed={second.FxParamsRemoved}.");
            Assert.AreEqual(0, second.ExprParamsRemoved,
                $"A54: second cleanup should be a deterministic no-op for expression params. removed={second.ExprParamsRemoved}.");
            Assert.AreEqual(0, second.MenuControlsRemoved,
                $"A54: second cleanup should be a deterministic no-op for menu controls. removed={second.MenuControlsRemoved}.");

            Assert.AreEqual(0, CountASMLiteLayers(_ctx.Ctrl), "A54: ASMLite FX layers must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountASMLiteFxParams(_ctx.Ctrl), "A54: ASMLite FX params must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), "A54: ASMLite expression params must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu), "A54: Settings Manager control must remain absent after repeated cleanup.");
        }
    }
}
