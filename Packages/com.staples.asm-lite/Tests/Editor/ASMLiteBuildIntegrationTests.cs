using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A46-A50: Build() integration coverage anchored on generated-assets output.
    /// Verifies slot bounds, return-path contracts, invalid-slot rejection, and
    /// repeated-build idempotency against generated FX/params/menu assets.
    /// </summary>
    [TestFixture]
    public class ASMLiteBuildIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteBuildIntegrationTests);
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A46: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A46: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A46: fixture did not create VRCAvatarDescriptor.");
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

        private static void RecordBuildDiagnosticFailure(string testName, ASMLiteBuildDiagnosticResult diagnostic, string resultsFile = null)
        {
            ASMLiteTestFixtures.RecordBuildDiagnosticFailure(SuiteName, testName, diagnostic, resultsFile);
        }

        private static void AssertDeterminismEqual<T>(
            string testName,
            string contextPath,
            T expected,
            T actual,
            string assertionMessage,
            string resultsFile = null)
        {
            if (Equals(expected, actual))
                return;

            string failureMessage = $"{assertionMessage} Expected: {FormatValue(expected)} Actual: {FormatValue(actual)}.";
            ASMLiteTestFixtures.RecordDeterminismFailure(SuiteName, testName, contextPath, failureMessage, resultsFile);
            Assert.Fail(failureMessage);
        }

        private static void AssertDeterminismSequenceEqual(
            string testName,
            string contextPath,
            string[] expected,
            string[] actual,
            string assertionMessage,
            string resultsFile = null)
        {
            var expectedValues = expected ?? Array.Empty<string>();
            var actualValues = actual ?? Array.Empty<string>();
            if (expectedValues.SequenceEqual(actualValues))
                return;

            string failureMessage = $"{assertionMessage} Expected: [{string.Join(", ", expectedValues)}] Actual: [{string.Join(", ", actualValues)}].";
            ASMLiteTestFixtures.RecordDeterminismFailure(SuiteName, testName, contextPath, failureMessage, resultsFile);
            Assert.Fail(failureMessage);
        }

        private static void AssertDeterminismCondition(
            string testName,
            string contextPath,
            bool condition,
            string assertionMessage,
            string resultsFile = null)
        {
            if (condition)
                return;

            ASMLiteTestFixtures.RecordDeterminismFailure(SuiteName, testName, contextPath, assertionMessage, resultsFile);
            Assert.Fail(assertionMessage);
        }

        private static void AssertDeterminismDifferent<T>(
            string testName,
            string contextPath,
            T first,
            T second,
            string assertionMessage,
            string resultsFile = null)
        {
            if (!Equals(first, second))
                return;

            string failureMessage = $"{assertionMessage} Value remained {FormatValue(second)}.";
            ASMLiteTestFixtures.RecordDeterminismFailure(SuiteName, testName, contextPath, failureMessage, resultsFile);
            Assert.Fail(failureMessage);
        }

        private static string FormatValue<T>(T value)
        {
            if (value == null)
                return "<null>";

            return value.ToString();
        }

        private static int BuildOrFail(AsmLiteTestContext ctx, string aid)
        {
            int buildResult = ASMLiteBuilder.Build(ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult} for slotCount={ctx.Comp.slotCount}.");
            return buildResult;
        }

        private static AnimatorController LoadGeneratedFxController()
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(ctrl,
                $"Generated FX controller missing at '{ASMLiteAssetPaths.FXController}'.");
            return ctrl;
        }

        private static VRCExpressionParameters LoadGeneratedExprParams()
        {
            var expr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(expr,
                $"Generated expression params missing at '{ASMLiteAssetPaths.ExprParams}'.");
            Assert.IsNotNull(expr.parameters, "Generated expression params list must not be null.");
            return expr;
        }

        private static VRCExpressionsMenu LoadGeneratedRootMenu()
        {
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(menu,
                $"Generated menu missing at '{ASMLiteAssetPaths.Menu}'.");
            Assert.IsNotNull(menu.controls, "Generated root menu controls must not be null.");
            return menu;
        }

        private static int CountASMLiteLayers(AnimatorController ctrl)
            => ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_"));

        private static int CountASMLiteFxParams(AnimatorController ctrl)
            => ctrl.parameters.Count(p => p.name != null
                && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"));

        private static int CountASMLiteExprParams(VRCExpressionParameters exprParams)
            => exprParams.parameters.Count(p => p != null
                && p.name != null
                && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"));

        private static int ExpectedGeneratedExprAsmParamCount(int slotCount, int discoveredParamCount)
            => 1 + discoveredParamCount + (slotCount * discoveredParamCount);

        private static int CountSettingsManagerControls(VRCExpressionsMenu rootMenu)
            => rootMenu.controls.Count(c => c != null
                && c.name == "Settings Manager"
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu);

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

        private static string[] ReadGeneratedBackupNames(ASMLiteGeneratedOutputSnapshot snapshot)
            => (snapshot?.FxParameterNames ?? new string[0])
                .Where(name => !string.IsNullOrEmpty(name) && name.StartsWith("ASMLite_Bak_"))
                .ToArray();

        private static void AssertUnchangedInputSnapshotMatches(
            string testName,
            ASMLiteGeneratedOutputSnapshot firstSnapshot,
            ASMLiteGeneratedOutputSnapshot secondSnapshot)
        {
            AssertDeterminismEqual(testName, "BuildResult", firstSnapshot.BuildResult, secondSnapshot.BuildResult,
                $"{testName}: repeated Build() return mismatch across generated-output snapshots.");
            AssertDeterminismEqual(testName, "ExprParamsText", firstSnapshot.ExprParamsText, secondSnapshot.ExprParamsText,
                $"{testName}: repeated Build() should keep generated expression params text byte-stable.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(testName, "MenuText", firstSnapshot.MenuText, secondSnapshot.MenuText,
                $"{testName}: repeated Build() should keep generated expressions menu text byte-stable.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(testName, "FxControllerMainObjectName", firstSnapshot.FxControllerMainObjectName, secondSnapshot.FxControllerMainObjectName,
                $"{testName}: repeated Build() should keep generated FX controller main object name stable.", ASMLiteAssetPaths.FXController);
            AssertDeterminismSequenceEqual(testName, "FxLayerNames", firstSnapshot.FxLayerNames, secondSnapshot.FxLayerNames,
                $"{testName}: repeated Build() should keep normalized FX layer names stable.", ASMLiteAssetPaths.FXController);
            AssertDeterminismSequenceEqual(testName, "FxParameterNames", firstSnapshot.FxParameterNames, secondSnapshot.FxParameterNames,
                $"{testName}: repeated Build() should keep normalized FX parameter names stable.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(testName, "FxDanglingLocalFileIdCount", firstSnapshot.FxDanglingLocalFileIdCount, secondSnapshot.FxDanglingLocalFileIdCount,
                $"{testName}: repeated Build() should not change dangling FX local fileID counts.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(testName, "SettingsManagerControlCount", firstSnapshot.SettingsManagerControlCount, secondSnapshot.SettingsManagerControlCount,
                $"{testName}: repeated Build() should keep Settings Manager menu control counts stable.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(testName, "LiveVrcFuryComponentCount", firstSnapshot.LiveVrcFuryComponentCount, secondSnapshot.LiveVrcFuryComponentCount,
                $"{testName}: repeated Build() should keep live VF.Model.VRCFury component counts stable.");
            AssertDeterminismEqual(testName, "ControllerReferencePath", firstSnapshot.ControllerReferencePath, secondSnapshot.ControllerReferencePath,
                $"{testName}: repeated Build() should keep FullController FX reference paths stable.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(testName, "MenuReferencePath", firstSnapshot.MenuReferencePath, secondSnapshot.MenuReferencePath,
                $"{testName}: repeated Build() should keep FullController menu reference paths stable.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(testName, "ParameterReferenceResolvedPath", firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                $"{testName}: repeated Build() should keep the resolved FullController parameter fallback path stable.");
            AssertDeterminismEqual(testName, "ParameterReferenceAssetPath", firstSnapshot.ParameterReferenceAssetPath, secondSnapshot.ParameterReferenceAssetPath,
                $"{testName}: repeated Build() should keep FullController parameter asset references stable.", ASMLiteAssetPaths.ExprParams);
        }

        // ── A46 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A46_Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts()
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

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();
            var generatedMenu = LoadGeneratedRootMenu();

            int asmLayerCount = CountASMLiteLayers(generatedCtrl);
            Assert.AreEqual(1, asmLayerCount,
                $"A46: expected 1 ASMLite_ layer for slotCount=1, got {asmLayerCount}.");

            int expectedFxAsmParams = 1 + (1 * discoveredExpected) + discoveredExpected;
            int asmFxParamCount = CountASMLiteFxParams(generatedCtrl);
            Assert.AreEqual(expectedFxAsmParams, asmFxParamCount,
                $"A46: generated FX ASMLite param count mismatch for slotCount=1. expected={expectedFxAsmParams}, got {asmFxParamCount}.");

            int expectedExprAsmParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(generatedExpr);
            Assert.AreEqual(expectedExprAsmParams, asmExprParamCount,
                $"A46: generated expression ASMLite param count mismatch for slotCount=1 after accounting for Clear Preset default keys. expected={expectedExprAsmParams}, got {asmExprParamCount}.");

            int settingsManagerCount = CountSettingsManagerControls(generatedMenu);
            Assert.AreEqual(1, settingsManagerCount,
                $"A46: expected one Settings Manager control in generated root menu. got {settingsManagerCount}.");
        }

        // ── A47 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A47_Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts()
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

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();
            var generatedMenu = LoadGeneratedRootMenu();

            int asmLayerCount = CountASMLiteLayers(generatedCtrl);
            Assert.AreEqual(8, asmLayerCount,
                $"A47: expected 8 ASMLite_ layers for slotCount=8, got {asmLayerCount}.");

            int expectedFxAsmParams = 1 + (8 * discoveredExpected) + discoveredExpected;
            int asmFxParamCount = CountASMLiteFxParams(generatedCtrl);
            Assert.AreEqual(expectedFxAsmParams, asmFxParamCount,
                $"A47: generated FX ASMLite param count mismatch for slotCount=8. expected={expectedFxAsmParams}, got {asmFxParamCount}.");

            int expectedExprAsmParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(generatedExpr);
            Assert.AreEqual(expectedExprAsmParams, asmExprParamCount,
                $"A47: generated expression ASMLite param count mismatch for slotCount=8 after accounting for Clear Preset default keys. expected={expectedExprAsmParams}, got {asmExprParamCount}.");

            int settingsManagerCount = CountSettingsManagerControls(generatedMenu);
            Assert.AreEqual(1, settingsManagerCount,
                $"A47: expected one Settings Manager control in generated root menu. got {settingsManagerCount}.");
        }

        // ── A48 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A48_Build_ReturnsDiscoveredNonASMLiteParamCount_AndWritesGeneratedSchema()
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

            var generatedExpr = LoadGeneratedExprParams();
            int expectedExprParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(generatedExpr);
            Assert.AreEqual(expectedExprParams, asmExprParamCount,
                $"A48: generated expression ASMLite param count mismatch for return-path contract validation after accounting for Clear Preset default keys. expected={expectedExprParams}, got {asmExprParamCount}.");
        }

        // ── A49 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A49_Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged()
        {
            _ctx.Comp.slotCount = 9;
            AddParam(_ctx, "A49_User", VRCExpressionParameters.ValueType.Int);

            string fxBefore = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprBefore = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuBefore = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            LogAssert.Expect(LogType.Error, "[ASM-Lite] slotCount must be between 1 and 8 (got 9).");
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(-1, buildResult,
                $"A49: Build() must reject slotCount outside [1..8] with -1. got {buildResult} for slotCount={_ctx.Comp.slotCount}.");

            var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
            RecordBuildDiagnosticFailure(nameof(A49_Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged), diagnostic);
            Assert.IsFalse(diagnostic.Success,
                "A49: invalid-slot Build() must expose failing diagnostics instead of returning -1 without context.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Build.ValidationFailed, diagnostic.Code,
                "A49: invalid slotCount must map to deterministic BUILD-301 validation diagnostics.");
            Assert.AreEqual("slotCount", diagnostic.ContextPath,
                "A49: BUILD-301 diagnostics should identify slotCount as the failing context.");

            string fxAfter = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprAfter = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuAfter = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            Assert.AreEqual(fxBefore, fxAfter,
                "A49: invalid-slot Build() should not mutate generated FX controller asset.");
            Assert.AreEqual(exprBefore, exprAfter,
                "A49: invalid-slot Build() should not mutate generated expression params asset.");
            Assert.AreEqual(menuBefore, menuAfter,
                "A49: invalid-slot Build() should not mutate generated menu asset.");
        }

        // ── A50 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces()
        {
            _ctx.Comp.slotCount = 3;
            AddParam(_ctx, "A50_Int", VRCExpressionParameters.ValueType.Int, 2f);
            AddParam(_ctx, "A50_Bool", VRCExpressionParameters.ValueType.Bool, 1f);

            int firstResult = BuildOrFail(_ctx, "A50");
            Assert.AreEqual(2, firstResult,
                $"A50: setup failure, first Build() should discover exactly 2 params, got {firstResult}.");

            var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, firstResult);
            Assert.Greater(firstSnapshot.FxLayerNames.Length, 0,
                "A50: setup failure, expected normalized FX layer names after first Build().");
            Assert.Greater(firstSnapshot.FxParameterNames.Length, 0,
                "A50: setup failure, expected normalized FX parameter names after first Build().");
            Assert.AreEqual(1, firstSnapshot.SettingsManagerControlCount,
                "A50: setup failure, expected exactly one Settings Manager control after first Build().");
            Assert.AreEqual(0, firstSnapshot.FxDanglingLocalFileIdCount,
                "A50: setup failure, generated FX controller should not contain dangling local fileID references after first Build().");
            Assert.AreEqual(1, firstSnapshot.LiveVrcFuryComponentCount,
                "A50: setup failure, first Build() should leave exactly one live VF.Model.VRCFury component.");

            int secondResult = BuildOrFail(_ctx, "A50");
            var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, secondResult);

            AssertUnchangedInputSnapshotMatches(nameof(A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), firstSnapshot, secondSnapshot);
            AssertDeterminismEqual(nameof(A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "ControllerReferencePath", ASMLiteAssetPaths.FXController, secondSnapshot.ControllerReferencePath,
                "A50: repeated Build() must keep the live FullController FX reference pointed at the canonical generated asset path.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "MenuReferencePath", ASMLiteAssetPaths.Menu, secondSnapshot.MenuReferencePath,
                "A50: repeated Build() must keep the live FullController menu reference pointed at the canonical generated asset path.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "ParameterReferenceAssetPath", ASMLiteAssetPaths.ExprParams, secondSnapshot.ParameterReferenceAssetPath,
                "A50: repeated Build() must keep the live FullController parameter reference pointed at the canonical generated asset path.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(nameof(A50_RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "FxControllerMainObjectName", "ASMLite_FX", secondSnapshot.FxControllerMainObjectName,
                "A50: generated FX controller main object name must stay normalized to the filename without the .controller extension to avoid Unity import warnings.", ASMLiteAssetPaths.FXController);
        }

        [Test, Category("Integration")]
        public void A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "A54_Toggle", VRCExpressionParameters.ValueType.Bool, 1f);
            AddParam(_ctx, "A54_Float", VRCExpressionParameters.ValueType.Float, 0.25f);

            var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, BuildOrFail(_ctx, "A54"));
            var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, BuildOrFail(_ctx, "A54"));

            AssertUnchangedInputSnapshotMatches(nameof(A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), firstSnapshot, secondSnapshot);
            AssertDeterminismEqual(nameof(A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "ControllerReferencePath", ASMLiteAssetPaths.FXController, firstSnapshot.ControllerReferencePath,
                "A54: unchanged-input builds must keep FullController controller.objRef wired to the canonical generated FX controller path.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "MenuReferencePath", ASMLiteAssetPaths.Menu, firstSnapshot.MenuReferencePath,
                "A54: unchanged-input builds must keep FullController menu.objRef wired to the canonical generated menu path.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "ParameterReferenceAssetPath", ASMLiteAssetPaths.ExprParams, firstSnapshot.ParameterReferenceAssetPath,
                "A54: unchanged-input builds must keep the resolved FullController parameter reference wired to the canonical generated expression-parameters path.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(nameof(A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "ParameterReferenceResolvedPath", firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                "A54: unchanged-input builds must keep the fallback-group-selected parameter reference path stable.");
            AssertDeterminismEqual(nameof(A54_Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "FxControllerMainObjectName", "ASMLite_FX", firstSnapshot.FxControllerMainObjectName,
                "A54: generated FX controller main object name must stay normalized on unchanged-input builds.", ASMLiteAssetPaths.FXController);
        }

        [Test, Category("Integration")]
        public void A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "A55_Base", VRCExpressionParameters.ValueType.Int, 3f);

            int firstResult = BuildOrFail(_ctx, "A55");
            var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, firstResult);
            int firstExprParamCount = CountASMLiteExprParams(LoadGeneratedExprParams());
            var firstBackupNames = ReadGeneratedBackupNames(firstSnapshot);

            AddParam(_ctx, "A55_NewUserParam", VRCExpressionParameters.ValueType.Bool, 1f);

            int secondResult = BuildOrFail(_ctx, "A55");
            var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, secondResult);
            int secondExprParamCount = CountASMLiteExprParams(LoadGeneratedExprParams());
            var secondBackupNames = ReadGeneratedBackupNames(secondSnapshot);

            AssertDeterminismDifferent(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "BuildResult", firstSnapshot.BuildResult, secondSnapshot.BuildResult,
                "A55: changed input should change the Build() discovered-parameter count captured in the snapshot.");
            AssertDeterminismDifferent(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "FxParameterNames.Length", firstSnapshot.FxParameterNames.Length, secondSnapshot.FxParameterNames.Length,
                "A55: changed input should change the normalized generated FX parameter count.", ASMLiteAssetPaths.FXController);
            AssertDeterminismDifferent(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ExprParamCount", firstExprParamCount, secondExprParamCount,
                "A55: changed input should change the generated expression-parameter count.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismCondition(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "BackupNames",
                !firstBackupNames.SequenceEqual(secondBackupNames),
                "A55: changed input should change generated backup-key names.",
                ASMLiteAssetPaths.FXController);

            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ControllerReferencePath", firstSnapshot.ControllerReferencePath, secondSnapshot.ControllerReferencePath,
                "A55: changed input should not retarget the canonical FullController FX asset reference.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "MenuReferencePath", firstSnapshot.MenuReferencePath, secondSnapshot.MenuReferencePath,
                "A55: changed input should not retarget the canonical FullController menu asset reference.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ParameterReferenceAssetPath", firstSnapshot.ParameterReferenceAssetPath, secondSnapshot.ParameterReferenceAssetPath,
                "A55: changed input should not retarget the canonical FullController parameter asset reference.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ParameterReferenceResolvedPath", firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                "A55: changed input should not change which fallback-group parameter path is populated.");
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "FxControllerMainObjectName", firstSnapshot.FxControllerMainObjectName, secondSnapshot.FxControllerMainObjectName,
                "A55: changed input should not change the canonical generated FX controller main-object name.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ControllerReferencePath", ASMLiteAssetPaths.FXController, secondSnapshot.ControllerReferencePath,
                "A55: canonical generated FX asset path should remain stable after legitimate schema changes.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "MenuReferencePath", ASMLiteAssetPaths.Menu, secondSnapshot.MenuReferencePath,
                "A55: canonical generated menu asset path should remain stable after legitimate schema changes.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(A55_Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ParameterReferenceAssetPath", ASMLiteAssetPaths.ExprParams, secondSnapshot.ParameterReferenceAssetPath,
                "A55: canonical generated parameter asset path should remain stable after legitimate schema changes.", ASMLiteAssetPaths.ExprParams);
        }

        [Test, Category("Integration")]
        public void A51_Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "A51_Keep", VRCExpressionParameters.ValueType.Int);
            AddParam(_ctx, "A51_Drop", VRCExpressionParameters.ValueType.Float);
            AddParam(_ctx, "A51_KeepTwo", VRCExpressionParameters.ValueType.Bool);

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "A51_Drop", "A51_Drop", "Missing_Name" };

            int buildResult = BuildOrFail(_ctx, "A51");
            Assert.AreEqual(2, buildResult,
                "A51: Build() return should count only non-excluded discovered params.");

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();

            int expectedFxAsmParams = 1 + (_ctx.Comp.slotCount * buildResult) + buildResult;
            int expectedExprAsmParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, buildResult);

            Assert.AreEqual(expectedFxAsmParams, CountASMLiteFxParams(generatedCtrl),
                "A51: FX parameter shape should reflect exclusion-pruned discovery count.");
            Assert.AreEqual(expectedExprAsmParams, CountASMLiteExprParams(generatedExpr),
                "A51: expression parameter shape should reflect exclusion-pruned discovery count plus Clear Preset default keys.");

            var fxNames = generatedCtrl.parameters.Select(p => p.name).ToHashSet();
            var exprNames = generatedExpr.parameters.Select(p => p.name).ToHashSet();

            Assert.IsFalse(fxNames.Contains("A51_Drop"), "A51: excluded live parameter must not be declared in FX controller.");
            Assert.IsFalse(fxNames.Contains("ASMLite_Def_A51_Drop"), "A51: excluded default key must not be generated in FX controller.");
            Assert.IsFalse(fxNames.Contains("ASMLite_Bak_S1_A51_Drop") || fxNames.Contains("ASMLite_Bak_S2_A51_Drop"),
                "A51: excluded backup keys must not be generated in FX controller.");

            Assert.IsFalse(exprNames.Contains("ASMLite_Bak_S1_A51_Drop") || exprNames.Contains("ASMLite_Bak_S2_A51_Drop"),
                "A51: excluded backup keys must not be generated in expression parameters.");
        }

        [Test, Category("Integration")]
        public void A52_RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "A52_Keep", VRCExpressionParameters.ValueType.Int, 1f);
            AddParam(_ctx, "A52_Drop", VRCExpressionParameters.ValueType.Float, 0.3f);

            _ctx.Comp.useParameterExclusions = false;
            int baselineResult = BuildOrFail(_ctx, "A52");
            Assert.AreEqual(2, baselineResult, "A52: baseline build must include both parameters before exclusion toggle.");

            var baselineExpr = LoadGeneratedExprParams();
            Assert.IsTrue(baselineExpr.parameters.Any(p => p.name == "ASMLite_Bak_S1_A52_Drop"),
                "A52: baseline generated expression schema must contain excluded candidate backup key before toggle.");

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "A52_Drop", " Ghost " };

            int firstExcludedResult = BuildOrFail(_ctx, "A52");
            Assert.AreEqual(1, firstExcludedResult,
                "A52: exclusion-enabled build should return only the non-excluded discovered param count.");

            var firstExcludedCtrl = LoadGeneratedFxController();
            int firstExcludedLayers = CountASMLiteLayers(firstExcludedCtrl);
            int firstExcludedFxParamCount = CountASMLiteFxParams(firstExcludedCtrl);
            string exprFirstExcluded = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);

            int secondExcludedResult = BuildOrFail(_ctx, "A52");
            Assert.AreEqual(firstExcludedResult, secondExcludedResult,
                "A52: repeated exclusion-enabled builds should keep Build() return deterministic.");

            string exprSecondExcluded = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            Assert.AreEqual(exprFirstExcluded, exprSecondExcluded,
                "A52: expression output should be text-idempotent across repeated exclusion-enabled builds.");

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();
            Assert.AreEqual(firstExcludedLayers, CountASMLiteLayers(generatedCtrl),
                "A52: repeated exclusion-enabled builds should keep FX layer count deterministic.");
            Assert.AreEqual(firstExcludedFxParamCount, CountASMLiteFxParams(generatedCtrl),
                "A52: repeated exclusion-enabled builds should keep FX ASMLite parameter count deterministic.");

            Assert.IsFalse(generatedCtrl.parameters.Any(p => p.name == "A52_Drop" || p.name == "ASMLite_Def_A52_Drop" || p.name == "ASMLite_Bak_S1_A52_Drop"),
                "A52: FX output must remove excluded live/default/backup keys after exclusion toggle.");
            Assert.IsFalse(generatedExpr.parameters.Any(p => p.name == "ASMLite_Bak_S1_A52_Drop"),
                "A52: expression output must remove previously generated excluded backup keys after exclusion toggle.");
        }

        [Test, Category("Integration")]
        public void A53_Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuild302WithNestedDrift203()
        {
            _ctx.Comp.slotCount = 3;
            AddParam(_ctx, "A53_User", VRCExpressionParameters.ValueType.Int, 1f);

            var vf = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.BrokenFullController
            {
                controllers = new[] { new VF.Model.Feature.ControllerEntry() },
                menus = new[] { new VF.Model.Feature.MenuEntryWithoutPrefix() },
                prms = new[] { new VF.Model.Feature.ParameterEntry() },
                globalParams = new[] { string.Empty },
            };

            LogAssert.Expect(LogType.Error,
                "[ASM-Lite] Build failed: could not ensure live VRCFury FullController asset wiring on 'ASMLite'.");

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(-1, buildResult,
                "A53: Build() must preserve legacy -1 behavior when critical FullController wiring fails.");

            var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
            RecordBuildDiagnosticFailure(nameof(A53_Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuild302WithNestedDrift203), diagnostic);
            Assert.IsFalse(diagnostic.Success,
                "A53: critical FullController drift must expose failing build diagnostics.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed, diagnostic.Code,
                "A53: FullController schema drift during build preflight must map to BUILD-302.");
            Assert.AreEqual("content", diagnostic.ContextPath,
                "A53: BUILD-302 wrapper diagnostics should identify FullController content wiring scope.");
            Assert.IsNotNull(diagnostic.InnerDiagnostic,
                "A53: BUILD-302 diagnostics should preserve nested DRIFT context for schema remediation.");

            var inner = diagnostic.InnerDiagnostic;
            Assert.IsFalse(inner.Success,
                "A53: nested diagnostic for BUILD-302 should be a failing DRIFT diagnostic.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath, inner.Code,
                "A53: missing FullController prefix path must be preserved as nested DRIFT-203.");
            Assert.AreEqual(ASMLiteDriftProbe.MenuPrefixPath, inner.ContextPath,
                "A53: nested DRIFT diagnostics should expose the exact failing schema path.");
        }

    }
}
