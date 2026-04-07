using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A46-A50: Full Build() integration coverage across live injection surfaces.
    /// Verifies slot bounds, return-path contracts, invalid-slot rejection, and
    /// repeated-build idempotency against avatar FX/params/menu targets.
    /// </summary>
    [TestFixture]
    public class ASMLiteBuildIntegrationTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A46: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A46: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A46: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.Ctrl, "A46: fixture did not create FX AnimatorController.");
            Assert.IsNotNull(_ctx.AvDesc.expressionParameters,
                "A46: fixture did not assign expressionParameters.");
            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu,
                "A46: fixture did not assign expressionsMenu.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddParam(
            AsmLiteTestContext ctx,
            string name,
            VRCExpressionParameters.ValueType type,
            float defaultValue = 0f)
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

        private static int BuildOrFail(AsmLiteTestContext ctx, string aid)
        {
            int buildResult = ASMLiteBuilder.Build(ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult} for slotCount={ctx.Comp.slotCount}.");
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

        private static int CountDiscoveredNonASMLiteParams(VRCExpressionParameters exprParams)
        {
            var items = exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0];
            return items.Count(p => p != null
                && !string.IsNullOrEmpty(p.name)
                && !p.name.StartsWith("ASMLite_"));
        }

        private static int CountDuplicateFxParamNames(AnimatorController ctrl)
            => ctrl.parameters
                .Where(p => !string.IsNullOrEmpty(p.name)
                    && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"))
                .GroupBy(p => p.name)
                .Count(g => g.Count() > 1);

        private static int CountDuplicateExprParamNames(VRCExpressionParameters exprParams)
            => (exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Where(p => p != null
                    && !string.IsNullOrEmpty(p.name)
                    && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"))
                .GroupBy(p => p.name)
                .Count(g => g.Count() > 1);

        // ── A46 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A46_Build_SlotCountOne_InjectsExpectedLiveArtifacts()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "A46_Int", VRCExpressionParameters.ValueType.Int, 3f);
            AddParam(_ctx, "A46_Float", VRCExpressionParameters.ValueType.Float, 0.5f);

            int discoveredExpected = CountDiscoveredNonASMLiteParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(2, discoveredExpected,
                $"A46: setup failure, expected exactly 2 discovered params before Build(), got {discoveredExpected}.");

            int buildResult = BuildOrFail(_ctx, "A46");
            Assert.AreEqual(discoveredExpected, buildResult,
                $"A46: Build() return mismatch. expected discovered={discoveredExpected}, got {buildResult}.");

            int asmLayerCount = CountASMLiteLayers(_ctx.Ctrl);
            Assert.AreEqual(1, asmLayerCount,
                $"A46: expected 1 ASMLite_ layer for slotCount=1, got {asmLayerCount}.");

            int expectedFxParams = 1 + (1 * discoveredExpected) + discoveredExpected;
            int asmFxParamCount = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.AreEqual(expectedFxParams, asmFxParamCount,
                $"A46: FX ASMLite param count mismatch for slotCount=1. expected={expectedFxParams}, got {asmFxParamCount}.");

            int expectedExprParams = 1 + (1 * discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(expectedExprParams, asmExprParamCount,
                $"A46: expression ASMLite param count mismatch for slotCount=1. expected={expectedExprParams}, got {asmExprParamCount}.");

            int settingsManagerCount = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(1, settingsManagerCount,
                $"A46: expected one Settings Manager control after Build(). got {settingsManagerCount}.");
        }

        // ── A47 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A47_Build_SlotCountEight_InjectsExpectedLiveArtifacts()
        {
            _ctx.Comp.slotCount = 8;
            AddParam(_ctx, "A47_Int", VRCExpressionParameters.ValueType.Int, 7f);
            AddParam(_ctx, "A47_Bool", VRCExpressionParameters.ValueType.Bool, 1f);
            AddParam(_ctx, "A47_Float", VRCExpressionParameters.ValueType.Float, 0.25f);

            int discoveredExpected = CountDiscoveredNonASMLiteParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(3, discoveredExpected,
                $"A47: setup failure, expected exactly 3 discovered params before Build(), got {discoveredExpected}.");

            int buildResult = BuildOrFail(_ctx, "A47");
            Assert.AreEqual(discoveredExpected, buildResult,
                $"A47: Build() return mismatch. expected discovered={discoveredExpected}, got {buildResult}.");

            int asmLayerCount = CountASMLiteLayers(_ctx.Ctrl);
            Assert.AreEqual(8, asmLayerCount,
                $"A47: expected 8 ASMLite_ layers for slotCount=8, got {asmLayerCount}.");

            int expectedFxParams = 1 + (8 * discoveredExpected) + discoveredExpected;
            int asmFxParamCount = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.AreEqual(expectedFxParams, asmFxParamCount,
                $"A47: FX ASMLite param count mismatch for slotCount=8. expected={expectedFxParams}, got {asmFxParamCount}.");

            int expectedExprParams = 1 + (8 * discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(expectedExprParams, asmExprParamCount,
                $"A47: expression ASMLite param count mismatch for slotCount=8. expected={expectedExprParams}, got {asmExprParamCount}.");

            int settingsManagerCount = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(1, settingsManagerCount,
                $"A47: expected one Settings Manager control after Build(). got {settingsManagerCount}.");
        }

        // ── A48 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A48_Build_ReturnsDiscoveredNonASMLiteParamCount()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "A48_UserA", VRCExpressionParameters.ValueType.Int);
            AddParam(_ctx, "A48_UserB", VRCExpressionParameters.ValueType.Float, 0.1f);
            AddParam(_ctx, "ASMLite_A48_Skipped", VRCExpressionParameters.ValueType.Bool, 1f);

            int discoveredExpected = CountDiscoveredNonASMLiteParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(2, discoveredExpected,
                $"A48: setup failure, discovered count should ignore ASMLite_-prefixed params. expected=2, got {discoveredExpected}.");

            int buildResult = BuildOrFail(_ctx, "A48");
            Assert.AreEqual(discoveredExpected, buildResult,
                $"A48: Build() must return discovered non-ASMLite param count. expected={discoveredExpected}, got {buildResult}.");

            int expectedExprParams = 1 + (_ctx.Comp.slotCount * discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(expectedExprParams, asmExprParamCount,
                $"A48: expression ASMLite param count mismatch for return-path contract validation. expected={expectedExprParams}, got {asmExprParamCount}.");
        }

        // ── A49 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A49_Build_InvalidSlotCount_ReturnsMinusOne_AndInjectsNothing()
        {
            _ctx.Comp.slotCount = 9;
            AddParam(_ctx, "A49_User", VRCExpressionParameters.ValueType.Int);

            int beforeLayers = CountASMLiteLayers(_ctx.Ctrl);
            int beforeFxParams = CountASMLiteFxParams(_ctx.Ctrl);
            int beforeExprParams = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            int beforeMenuControls = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("slotCount must be between"));
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(-1, buildResult,
                $"A49: Build() must reject slotCount outside [1..8] with -1. got {buildResult} for slotCount={_ctx.Comp.slotCount}.");

            int afterLayers = CountASMLiteLayers(_ctx.Ctrl);
            int afterFxParams = CountASMLiteFxParams(_ctx.Ctrl);
            int afterExprParams = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            int afterMenuControls = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);

            Assert.AreEqual(beforeLayers, afterLayers,
                $"A49: invalid-slot Build() should not mutate FX layers. before={beforeLayers}, after={afterLayers}.");
            Assert.AreEqual(beforeFxParams, afterFxParams,
                $"A49: invalid-slot Build() should not mutate FX params. before={beforeFxParams}, after={afterFxParams}.");
            Assert.AreEqual(beforeExprParams, afterExprParams,
                $"A49: invalid-slot Build() should not mutate expression params. before={beforeExprParams}, after={afterExprParams}.");
            Assert.AreEqual(beforeMenuControls, afterMenuControls,
                $"A49: invalid-slot Build() should not mutate expression menu controls. before={beforeMenuControls}, after={afterMenuControls}.");
        }

        // ── A50 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A50_RepeatedBuild_IsIdempotentAcrossLiveInjectionSurfaces()
        {
            _ctx.Comp.slotCount = 3;
            AddParam(_ctx, "A50_Int", VRCExpressionParameters.ValueType.Int, 2f);
            AddParam(_ctx, "A50_Bool", VRCExpressionParameters.ValueType.Bool, 1f);

            int firstResult = BuildOrFail(_ctx, "A50");
            Assert.AreEqual(2, firstResult,
                $"A50: setup failure, first Build() should discover exactly 2 params, got {firstResult}.");

            int firstLayers = CountASMLiteLayers(_ctx.Ctrl);
            int firstFxParams = CountASMLiteFxParams(_ctx.Ctrl);
            int firstExprParams = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            int firstMenuControls = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);

            Assert.Greater(firstLayers, 0,
                $"A50: setup failure, expected ASMLite layers after first Build(), got {firstLayers}.");
            Assert.Greater(firstFxParams, 0,
                $"A50: setup failure, expected ASMLite FX params after first Build(), got {firstFxParams}.");
            Assert.Greater(firstExprParams, 0,
                $"A50: setup failure, expected ASMLite expression params after first Build(), got {firstExprParams}.");
            Assert.AreEqual(1, firstMenuControls,
                $"A50: setup failure, expected one Settings Manager control after first Build(), got {firstMenuControls}.");

            int secondResult = BuildOrFail(_ctx, "A50");
            Assert.AreEqual(firstResult, secondResult,
                $"A50: repeated Build() return mismatch. first={firstResult}, second={secondResult}.");

            int secondLayers = CountASMLiteLayers(_ctx.Ctrl);
            int secondFxParams = CountASMLiteFxParams(_ctx.Ctrl);
            int secondExprParams = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            int secondMenuControls = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);

            Assert.AreEqual(firstLayers, secondLayers,
                $"A50: ASMLite layer count changed across repeated Build(). first={firstLayers}, second={secondLayers}.");
            Assert.AreEqual(firstFxParams, secondFxParams,
                $"A50: ASMLite FX param count changed across repeated Build(). first={firstFxParams}, second={secondFxParams}.");
            Assert.AreEqual(firstExprParams, secondExprParams,
                $"A50: ASMLite expression param count changed across repeated Build(). first={firstExprParams}, second={secondExprParams}.");
            Assert.AreEqual(1, secondMenuControls,
                $"A50: repeated Build() must keep exactly one Settings Manager control. got {secondMenuControls}.");

            int duplicateFxNames = CountDuplicateFxParamNames(_ctx.Ctrl);
            int duplicateExprNames = CountDuplicateExprParamNames(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(0, duplicateFxNames,
                $"A50: repeated Build() introduced duplicate ASMLite FX param names. duplicateGroups={duplicateFxNames}.");
            Assert.AreEqual(0, duplicateExprNames,
                $"A50: repeated Build() introduced duplicate ASMLite expression param names. duplicateGroups={duplicateExprNames}.");
        }
    }
}
