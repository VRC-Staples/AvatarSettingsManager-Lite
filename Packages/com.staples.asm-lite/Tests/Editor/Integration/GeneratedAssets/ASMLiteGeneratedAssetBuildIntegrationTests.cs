using System;
using System.Collections.Generic;
using System.IO;
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
    /// Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts-RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: Build() integration coverage anchored on generated-assets output.
    /// Verifies slot bounds, return-path contracts, invalid-slot rejection, and
    /// repeated-build idempotency against generated FX/params/menu assets.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteGeneratedAssetBuildIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteGeneratedAssetBuildIntegrationTests);
        private static ASMLiteGeneratedAssetsFolderSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetsFolderSnapshot _testGeneratedAssetsBaseline;
        private AsmLiteTestContext _ctx;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            s_classGeneratedAssetsBaseline = ASMLiteGeneratedAssetsFolderSnapshot.Capture(SuiteName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            s_classGeneratedAssetsBaseline = null;
        }

        [SetUp]
        public void SetUp()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetsFolderSnapshot.Capture(SuiteName);
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.AvDesc.expressionParameters,
                "Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: fixture did not assign expressionParameters.");
            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu,
                "Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: fixture did not assign expressionsMenu.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            }
            finally
            {
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                _testGeneratedAssetsBaseline = null;
                _ctx = null;
            }
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
            => ctrl.layers.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxLayer);

        private static int CountASMLiteFxParams(AnimatorController ctrl)
            => ctrl.parameters.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxParameter);

        private static int CountASMLiteExprParams(VRCExpressionParameters exprParams)
            => exprParams.parameters.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter);

        private static int ExpectedGeneratedExprAsmParamCount(int slotCount, int discoveredParamCount)
            => 1 + discoveredParamCount + (slotCount * discoveredParamCount);

        private static int CountSettingsManagerControls(VRCExpressionsMenu rootMenu)
            => rootMenu.controls.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl);

        private static int CountDiscoveredNonASMLiteParams(VRCExpressionParameters exprParams)
        {
            var items = exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0];
            return items.Count(p => p != null
                && !string.IsNullOrEmpty(p.name)
                && !ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(p.name));
        }

        private static int CountDuplicateFxParamNames(AnimatorController ctrl)
            => ctrl.parameters
                .Where(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxParameter)
                .GroupBy(p => p.name)
                .Count(g => g.Count() > 1);

        private static int CountDuplicateExprParamNames(VRCExpressionParameters exprParams)
            => (exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Where(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter)
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

        [Test, Category("Integration")]
        public void Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "BuildSlotCountOne_Int", VRCExpressionParameters.ValueType.Int, 3f);
            AddParam(_ctx, "BuildSlotCountOne_Float", VRCExpressionParameters.ValueType.Float, 0.5f);

            int discoveredExpected = CountDiscoveredNonASMLiteParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(2, discoveredExpected,
                $"Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: setup failure, expected exactly 2 discovered params before Build(), got {discoveredExpected}.");

            int buildResult = BuildOrFail(_ctx, "Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts");
            Assert.AreEqual(discoveredExpected, buildResult,
                $"Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: Build() return mismatch. expected discovered={discoveredExpected}, got {buildResult}.");

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();
            var generatedMenu = LoadGeneratedRootMenu();

            int asmLayerCount = CountASMLiteLayers(generatedCtrl);
            Assert.AreEqual(1, asmLayerCount,
                $"Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: expected 1 ASMLite_ layer for slotCount=1, got {asmLayerCount}.");

            int expectedFxAsmParams = 1 + (1 * discoveredExpected) + discoveredExpected;
            int asmFxParamCount = CountASMLiteFxParams(generatedCtrl);
            Assert.AreEqual(expectedFxAsmParams, asmFxParamCount,
                $"Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: generated FX ASMLite param count mismatch for slotCount=1. expected={expectedFxAsmParams}, got {asmFxParamCount}.");

            int expectedExprAsmParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(generatedExpr);
            Assert.AreEqual(expectedExprAsmParams, asmExprParamCount,
                $"Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: generated expression ASMLite param count mismatch for slotCount=1 after accounting for Clear Preset default keys. expected={expectedExprAsmParams}, got {asmExprParamCount}.");

            int settingsManagerCount = CountSettingsManagerControls(generatedMenu);
            Assert.AreEqual(1, settingsManagerCount,
                $"Build_SlotCountOne_PopulatesExpectedGeneratedArtifacts: expected one Settings Manager control in generated root menu. got {settingsManagerCount}.");
        }

        [Test, Category("Integration")]
        public void Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts()
        {
            _ctx.Comp.slotCount = 8;
            AddParam(_ctx, "BuildSlotCountEight_Int", VRCExpressionParameters.ValueType.Int, 7f);
            AddParam(_ctx, "BuildSlotCountEight_Bool", VRCExpressionParameters.ValueType.Bool, 1f);
            AddParam(_ctx, "BuildSlotCountEight_Float", VRCExpressionParameters.ValueType.Float, 0.25f);

            int discoveredExpected = CountDiscoveredNonASMLiteParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(3, discoveredExpected,
                $"Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts: setup failure, expected exactly 3 discovered params before Build(), got {discoveredExpected}.");

            int buildResult = BuildOrFail(_ctx, "Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts");
            Assert.AreEqual(discoveredExpected, buildResult,
                $"Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts: Build() return mismatch. expected discovered={discoveredExpected}, got {buildResult}.");

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();
            var generatedMenu = LoadGeneratedRootMenu();

            int asmLayerCount = CountASMLiteLayers(generatedCtrl);
            Assert.AreEqual(8, asmLayerCount,
                $"Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts: expected 8 ASMLite_ layers for slotCount=8, got {asmLayerCount}.");

            int expectedFxAsmParams = 1 + (8 * discoveredExpected) + discoveredExpected;
            int asmFxParamCount = CountASMLiteFxParams(generatedCtrl);
            Assert.AreEqual(expectedFxAsmParams, asmFxParamCount,
                $"Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts: generated FX ASMLite param count mismatch for slotCount=8. expected={expectedFxAsmParams}, got {asmFxParamCount}.");

            int expectedExprAsmParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(generatedExpr);
            Assert.AreEqual(expectedExprAsmParams, asmExprParamCount,
                $"Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts: generated expression ASMLite param count mismatch for slotCount=8 after accounting for Clear Preset default keys. expected={expectedExprAsmParams}, got {asmExprParamCount}.");

            int settingsManagerCount = CountSettingsManagerControls(generatedMenu);
            Assert.AreEqual(1, settingsManagerCount,
                $"Build_SlotCountEight_PopulatesExpectedGeneratedArtifacts: expected one Settings Manager control in generated root menu. got {settingsManagerCount}.");
        }

        [Test, Category("Integration")]
        public void Build_ReturnsDiscoveredNonASMLiteParamCount_AndWritesGeneratedSchema()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "BuildReturnsDiscoveredNon_UserA", VRCExpressionParameters.ValueType.Int);
            AddParam(_ctx, "BuildReturnsDiscoveredNon_UserB", VRCExpressionParameters.ValueType.Float, 0.1f);
            AddParam(_ctx, "ASMLite_Skipped", VRCExpressionParameters.ValueType.Bool, 1f);

            int discoveredExpected = CountDiscoveredNonASMLiteParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(2, discoveredExpected,
                $"Build_ReturnsDiscoveredNonASMLiteParamCount_AndWritesGeneratedSchema: setup failure, discovered count should ignore ASMLite_-prefixed params. expected=2, got {discoveredExpected}.");

            int buildResult = BuildOrFail(_ctx, "Build_ReturnsDiscoveredNonASMLiteParamCount_AndWritesGeneratedSchema");
            Assert.AreEqual(discoveredExpected, buildResult,
                $"Build_ReturnsDiscoveredNonASMLiteParamCount_AndWritesGeneratedSchema: Build() must return discovered non-ASMLite param count. expected={discoveredExpected}, got {buildResult}.");

            var generatedExpr = LoadGeneratedExprParams();
            int expectedExprParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, discoveredExpected);
            int asmExprParamCount = CountASMLiteExprParams(generatedExpr);
            Assert.AreEqual(expectedExprParams, asmExprParamCount,
                $"Build_ReturnsDiscoveredNonASMLiteParamCount_AndWritesGeneratedSchema: generated expression ASMLite param count mismatch for return-path contract validation after accounting for Clear Preset default keys. expected={expectedExprParams}, got {asmExprParamCount}.");
        }

        [Test, Category("Integration")]
        public void Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged()
        {
            _ctx.Comp.slotCount = 9;
            AddParam(_ctx, "BuildInvalidSlotCount_User", VRCExpressionParameters.ValueType.Int);

            string fxBefore = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprBefore = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuBefore = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            LogAssert.Expect(LogType.Error, "[ASM-Lite] slotCount must be between 1 and 8 (got 9).");
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(-1, buildResult,
                $"Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: Build() must reject slotCount outside [1..8] with -1. got {buildResult} for slotCount={_ctx.Comp.slotCount}.");

            var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
            RecordBuildDiagnosticFailure(nameof(Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged), diagnostic);
            Assert.IsFalse(diagnostic.Success,
                "Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: invalid-slot Build() must expose failing diagnostics instead of returning -1 without context.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Build.ValidationFailed, diagnostic.Code,
                "Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: invalid slotCount must map to deterministic BUILD-301 validation diagnostics.");
            Assert.AreEqual("slotCount", diagnostic.ContextPath,
                "Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: BUILD-301 diagnostics should identify slotCount as the failing context.");

            string fxAfter = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprAfter = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuAfter = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            Assert.AreEqual(fxBefore, fxAfter,
                "Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: invalid-slot Build() should not mutate generated FX controller asset.");
            Assert.AreEqual(exprBefore, exprAfter,
                "Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: invalid-slot Build() should not mutate generated expression params asset.");
            Assert.AreEqual(menuBefore, menuAfter,
                "Build_InvalidSlotCount_ReturnsMinusOne_AndLeavesGeneratedAssetsUnchanged: invalid-slot Build() should not mutate generated menu asset.");
        }

        [Test, Category("Integration")]
        public void RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces()
        {
            _ctx.Comp.slotCount = 3;
            AddParam(_ctx, "RepeatedBuildIdempotentAcross_Int", VRCExpressionParameters.ValueType.Int, 2f);
            AddParam(_ctx, "RepeatedBuildIdempotentAcross_Bool", VRCExpressionParameters.ValueType.Bool, 1f);

            int firstResult = BuildOrFail(_ctx, "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces");
            Assert.AreEqual(2, firstResult,
                $"RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: setup failure, first Build() should discover exactly 2 params, got {firstResult}.");

            var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, firstResult);
            Assert.Greater(firstSnapshot.FxLayerNames.Length, 0,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: setup failure, expected normalized FX layer names after first Build().");
            Assert.Greater(firstSnapshot.FxParameterNames.Length, 0,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: setup failure, expected normalized FX parameter names after first Build().");
            Assert.AreEqual(1, firstSnapshot.SettingsManagerControlCount,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: setup failure, expected exactly one Settings Manager control after first Build().");
            Assert.AreEqual(0, firstSnapshot.FxDanglingLocalFileIdCount,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: setup failure, generated FX controller should not contain dangling local fileID references after first Build().");
            Assert.AreEqual(1, firstSnapshot.LiveVrcFuryComponentCount,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: setup failure, first Build() should leave exactly one live VF.Model.VRCFury component.");

            int secondResult = BuildOrFail(_ctx, "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces");
            var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, secondResult);

            AssertUnchangedInputSnapshotMatches(nameof(RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), firstSnapshot, secondSnapshot);
            AssertDeterminismEqual(nameof(RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "ControllerReferencePath", ASMLiteAssetPaths.FXController, secondSnapshot.ControllerReferencePath,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: repeated Build() must keep the live FullController FX reference pointed at the canonical generated asset path.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "MenuReferencePath", ASMLiteAssetPaths.Menu, secondSnapshot.MenuReferencePath,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: repeated Build() must keep the live FullController menu reference pointed at the canonical generated asset path.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "ParameterReferenceAssetPath", ASMLiteAssetPaths.ExprParams, secondSnapshot.ParameterReferenceAssetPath,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: repeated Build() must keep the live FullController parameter reference pointed at the canonical generated asset path.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(nameof(RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces), "FxControllerMainObjectName", "ASMLite_FX", secondSnapshot.FxControllerMainObjectName,
                "RepeatedBuild_IsIdempotentAcrossGeneratedAssetSurfaces: generated FX controller main object name must stay normalized to the filename without the .controller extension to avoid Unity import warnings.", ASMLiteAssetPaths.FXController);
        }

        [Test, Category("Integration")]
        public void Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "BuildUnchangedInputKeeps_Toggle", VRCExpressionParameters.ValueType.Bool, 1f);
            AddParam(_ctx, "BuildUnchangedInputKeeps_Float", VRCExpressionParameters.ValueType.Float, 0.25f);

            var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, BuildOrFail(_ctx, "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs"));
            var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, BuildOrFail(_ctx, "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs"));

            AssertUnchangedInputSnapshotMatches(nameof(Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), firstSnapshot, secondSnapshot);
            AssertDeterminismEqual(nameof(Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "ControllerReferencePath", ASMLiteAssetPaths.FXController, firstSnapshot.ControllerReferencePath,
                "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs: unchanged-input builds must keep FullController controller.objRef wired to the canonical generated FX controller path.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "MenuReferencePath", ASMLiteAssetPaths.Menu, firstSnapshot.MenuReferencePath,
                "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs: unchanged-input builds must keep FullController menu.objRef wired to the canonical generated menu path.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "ParameterReferenceAssetPath", ASMLiteAssetPaths.ExprParams, firstSnapshot.ParameterReferenceAssetPath,
                "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs: unchanged-input builds must keep the resolved FullController parameter reference wired to the canonical generated expression-parameters path.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(nameof(Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "ParameterReferenceResolvedPath", firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs: unchanged-input builds must keep the fallback-group-selected parameter reference path stable.");
            AssertDeterminismEqual(nameof(Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs), "FxControllerMainObjectName", "ASMLite_FX", firstSnapshot.FxControllerMainObjectName,
                "Build_UnchangedInput_KeepsStableGeneratedNamesAndFullControllerRefs: generated FX controller main object name must stay normalized on unchanged-input builds.", ASMLiteAssetPaths.FXController);
        }

        [Test, Category("Integration")]
        public void Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "BuildChangedInputProduces_Base", VRCExpressionParameters.ValueType.Int, 3f);

            int firstResult = BuildOrFail(_ctx, "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly");
            var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, firstResult);
            int firstExprParamCount = CountASMLiteExprParams(LoadGeneratedExprParams());
            var firstBackupNames = ReadGeneratedBackupNames(firstSnapshot);

            AddParam(_ctx, "BuildChangedInputProduces_NewUserParam", VRCExpressionParameters.ValueType.Bool, 1f);

            int secondResult = BuildOrFail(_ctx, "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly");
            var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(_ctx.Comp, secondResult);
            int secondExprParamCount = CountASMLiteExprParams(LoadGeneratedExprParams());
            var secondBackupNames = ReadGeneratedBackupNames(secondSnapshot);

            AssertDeterminismDifferent(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "BuildResult", firstSnapshot.BuildResult, secondSnapshot.BuildResult,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should change the Build() discovered-parameter count captured in the snapshot.");
            AssertDeterminismDifferent(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "FxParameterNames.Length", firstSnapshot.FxParameterNames.Length, secondSnapshot.FxParameterNames.Length,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should change the normalized generated FX parameter count.", ASMLiteAssetPaths.FXController);
            AssertDeterminismDifferent(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ExprParamCount", firstExprParamCount, secondExprParamCount,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should change the generated expression-parameter count.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismCondition(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "BackupNames",
                !firstBackupNames.SequenceEqual(secondBackupNames),
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should change generated backup-key names.",
                ASMLiteAssetPaths.FXController);

            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ControllerReferencePath", firstSnapshot.ControllerReferencePath, secondSnapshot.ControllerReferencePath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should not retarget the canonical FullController FX asset reference.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "MenuReferencePath", firstSnapshot.MenuReferencePath, secondSnapshot.MenuReferencePath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should not retarget the canonical FullController menu asset reference.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ParameterReferenceAssetPath", firstSnapshot.ParameterReferenceAssetPath, secondSnapshot.ParameterReferenceAssetPath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should not retarget the canonical FullController parameter asset reference.", ASMLiteAssetPaths.ExprParams);
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ParameterReferenceResolvedPath", firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should not change which fallback-group parameter path is populated.");
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "FxControllerMainObjectName", firstSnapshot.FxControllerMainObjectName, secondSnapshot.FxControllerMainObjectName,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: changed input should not change the canonical generated FX controller main-object name.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ControllerReferencePath", ASMLiteAssetPaths.FXController, secondSnapshot.ControllerReferencePath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: canonical generated FX asset path should remain stable after legitimate schema changes.", ASMLiteAssetPaths.FXController);
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "MenuReferencePath", ASMLiteAssetPaths.Menu, secondSnapshot.MenuReferencePath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: canonical generated menu asset path should remain stable after legitimate schema changes.", ASMLiteAssetPaths.Menu);
            AssertDeterminismEqual(nameof(Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly), "ParameterReferenceAssetPath", ASMLiteAssetPaths.ExprParams, secondSnapshot.ParameterReferenceAssetPath,
                "Build_ChangedInput_ProducesExpectedSnapshotDeltaOnly: canonical generated parameter asset path should remain stable after legitimate schema changes.", ASMLiteAssetPaths.ExprParams);
        }

        [Test, Category("Integration")]
        public void Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "BuildExclusionsUpdatesReturn_Keep", VRCExpressionParameters.ValueType.Int);
            AddParam(_ctx, "BuildExclusionsUpdatesReturn_Drop", VRCExpressionParameters.ValueType.Float);
            AddParam(_ctx, "BuildExclusionsUpdatesReturn_KeepTwo", VRCExpressionParameters.ValueType.Bool);

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "BuildExclusionsUpdatesReturn_Drop", "BuildExclusionsUpdatesReturn_Drop", "Missing_Name" };

            int buildResult = BuildOrFail(_ctx, "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema");
            Assert.AreEqual(2, buildResult,
                "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: Build() return should count only non-excluded discovered params.");

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();

            int expectedFxAsmParams = 1 + (_ctx.Comp.slotCount * buildResult) + buildResult;
            int expectedExprAsmParams = ExpectedGeneratedExprAsmParamCount(_ctx.Comp.slotCount, buildResult);

            Assert.AreEqual(expectedFxAsmParams, CountASMLiteFxParams(generatedCtrl),
                "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: FX parameter shape should reflect exclusion-pruned discovery count.");
            Assert.AreEqual(expectedExprAsmParams, CountASMLiteExprParams(generatedExpr),
                "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: expression parameter shape should reflect exclusion-pruned discovery count plus Clear Preset default keys.");

            var fxNames = generatedCtrl.parameters.Select(p => p.name).ToHashSet();
            var exprNames = generatedExpr.parameters.Select(p => p.name).ToHashSet();

            Assert.IsFalse(fxNames.Contains("BuildExclusionsUpdatesReturn_Drop"), "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: excluded live parameter must not be declared in FX controller.");
            Assert.IsFalse(fxNames.Contains("ASMLite_Def_ExcludedDrop"), "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: excluded default key must not be generated in FX controller.");
            Assert.IsFalse(fxNames.Contains("ASMLite_Bak_S1_ExcludedDrop") || fxNames.Contains("ASMLite_Bak_S2_ExcludedDrop"),
                "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: excluded backup keys must not be generated in FX controller.");

            Assert.IsFalse(exprNames.Contains("ASMLite_Bak_S1_ExcludedDrop") || exprNames.Contains("ASMLite_Bak_S2_ExcludedDrop"),
                "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: excluded backup keys must not be generated in expression parameters.");
        }

        [Test, Category("Integration")]
        public void RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "RepeatedBuildEnablingExclusions_Keep", VRCExpressionParameters.ValueType.Int, 1f);
            AddParam(_ctx, "RepeatedExcludedDrop", VRCExpressionParameters.ValueType.Float, 0.3f);

            _ctx.Comp.useParameterExclusions = false;
            int baselineResult = BuildOrFail(_ctx, "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups");
            Assert.AreEqual(2, baselineResult, "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: baseline build must include both parameters before exclusion toggle.");

            var baselineExpr = LoadGeneratedExprParams();
            Assert.IsTrue(baselineExpr.parameters.Any(p => p.name == "ASMLite_Bak_S1_RepeatedExcludedDrop"),
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: baseline generated expression schema must contain excluded candidate backup key before toggle.");

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "RepeatedExcludedDrop", " Ghost " };

            int firstExcludedResult = BuildOrFail(_ctx, "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups");
            Assert.AreEqual(1, firstExcludedResult,
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: exclusion-enabled build should return only the non-excluded discovered param count.");

            var firstExcludedCtrl = LoadGeneratedFxController();
            int firstExcludedLayers = CountASMLiteLayers(firstExcludedCtrl);
            int firstExcludedFxParamCount = CountASMLiteFxParams(firstExcludedCtrl);
            string exprFirstExcluded = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);

            int secondExcludedResult = BuildOrFail(_ctx, "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups");
            Assert.AreEqual(firstExcludedResult, secondExcludedResult,
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: repeated exclusion-enabled builds should keep Build() return deterministic.");

            string exprSecondExcluded = ASMLiteGeneratedOutputSnapshot.ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            Assert.AreEqual(exprFirstExcluded, exprSecondExcluded,
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: expression output should be text-idempotent across repeated exclusion-enabled builds.");

            var generatedCtrl = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();
            Assert.AreEqual(firstExcludedLayers, CountASMLiteLayers(generatedCtrl),
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: repeated exclusion-enabled builds should keep FX layer count deterministic.");
            Assert.AreEqual(firstExcludedFxParamCount, CountASMLiteFxParams(generatedCtrl),
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: repeated exclusion-enabled builds should keep FX ASMLite parameter count deterministic.");

            Assert.IsFalse(generatedCtrl.parameters.Any(p => p.name == "RepeatedExcludedDrop" || p.name == "ASMLite_Def_RepeatedExcludedDrop" || p.name == "ASMLite_Bak_S1_RepeatedExcludedDrop"),
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: FX output must remove excluded live/default/backup keys after exclusion toggle.");
            Assert.IsFalse(generatedExpr.parameters.Any(p => p.name == "ASMLite_Bak_S1_RepeatedExcludedDrop"),
                "RepeatedBuild_AfterEnablingExclusions_IsDeterministicAndRemovesLegacyExcludedBackups: expression output must remove previously generated excluded backup keys after exclusion toggle.");
        }

        [Test, Category("Integration")]
        public void Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic()
        {
            _ctx.Comp.slotCount = 3;
            AddParam(_ctx, "BuildFullControllerSchema_User", VRCExpressionParameters.ValueType.Int, 1f);

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
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: Build() must preserve legacy -1 behavior when critical FullController wiring fails.");

            var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
            RecordBuildDiagnosticFailure(nameof(Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic), diagnostic);
            Assert.IsFalse(diagnostic.Success,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: critical FullController drift must expose failing build diagnostics.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed, diagnostic.Code,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: FullController schema drift during build preflight must map to BUILD-302.");
            Assert.AreEqual("content", diagnostic.ContextPath,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: BUILD-302 wrapper diagnostics should identify FullController content wiring scope.");
            Assert.IsNotNull(diagnostic.InnerDiagnostic,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: BUILD-302 diagnostics should preserve nested DRIFT context for schema remediation.");

            var inner = diagnostic.InnerDiagnostic;
            Assert.IsFalse(inner.Success,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: nested diagnostic for BUILD-302 should be a failing DRIFT diagnostic.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath, inner.Code,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: missing FullController prefix path must be preserved as nested DRIFT-203.");
            Assert.AreEqual(ASMLiteDriftProbe.MenuPrefixPath, inner.ContextPath,
                "Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: nested DRIFT diagnostics should expose the exact failing schema path.");
        }

    }

    internal sealed class ASMLiteGeneratedAssetsFolderSnapshot
    {
        private readonly Dictionary<string, byte[]> _filesByRelativePath;

        private ASMLiteGeneratedAssetsFolderSnapshot(Dictionary<string, byte[]> filesByRelativePath)
        {
            _filesByRelativePath = filesByRelativePath;
        }

        internal static ASMLiteGeneratedAssetsFolderSnapshot Capture(string suiteName)
        {
            string fullFolderPath = ToFullFolderPath();
            Assert.IsTrue(Directory.Exists(fullFolderPath),
                $"{suiteName}: generated asset folder is missing at '{ASMLiteAssetPaths.GeneratedDir}'.");

            var filesByRelativePath = Directory
                .GetFiles(fullFolderPath, "*", SearchOption.AllDirectories)
                .ToDictionary(
                    filePath => ToRelativePath(fullFolderPath, filePath),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);

            return new ASMLiteGeneratedAssetsFolderSnapshot(filesByRelativePath);
        }

        internal void Restore()
        {
            string fullFolderPath = ToFullFolderPath();
            if (Directory.Exists(fullFolderPath))
            {
                foreach (string filePath in Directory.GetFiles(fullFolderPath, "*", SearchOption.AllDirectories))
                    File.Delete(filePath);

                foreach (string directoryPath in Directory.GetDirectories(fullFolderPath, "*", SearchOption.AllDirectories)
                             .OrderByDescending(path => path.Length))
                {
                    if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                        Directory.Delete(directoryPath);
                }
            }
            else
            {
                Directory.CreateDirectory(fullFolderPath);
            }

            foreach (var file in _filesByRelativePath)
            {
                string targetPath = Path.Combine(fullFolderPath, file.Key.Replace('/', Path.DirectorySeparatorChar));
                string targetDirectory = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDirectory))
                    Directory.CreateDirectory(targetDirectory);
                File.WriteAllBytes(targetPath, file.Value);
            }

            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private static string ToFullFolderPath()
            => Path.GetFullPath(ASMLiteAssetPaths.GeneratedDir);

        private static string ToRelativePath(string fullFolderPath, string filePath)
        {
            string normalizedFolder = Path.GetFullPath(fullFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedFile = Path.GetFullPath(filePath);
            return normalizedFile.Substring(normalizedFolder.Length).Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
