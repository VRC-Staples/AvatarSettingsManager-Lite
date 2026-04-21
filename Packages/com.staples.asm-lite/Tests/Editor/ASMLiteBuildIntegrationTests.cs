using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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

        private static string ReadPackageAssetText(string assetPath)
        {
            var fullPath = Path.GetFullPath(assetPath);
            Assert.IsTrue(File.Exists(fullPath), $"Expected generated asset file at '{fullPath}'.");
            return File.ReadAllText(fullPath);
        }

        private static int CountDanglingLocalFileIds(string controllerText)
        {
            Assert.IsNotNull(controllerText, "Controller text should not be null when scanning for dangling file IDs.");

            var definedIds = Regex.Matches(controllerText, @"^--- !u!\d+ &(-?\d+)$", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            int danglingCount = 0;
            foreach (Match match in Regex.Matches(controllerText, @"\{fileID: (-?\d+)\}"))
            {
                string fileId = match.Groups[1].Value;
                if (fileId == "0" || fileId == "9100000")
                    continue;
                if (definedIds.Contains(fileId))
                    continue;

                danglingCount++;
            }

            return danglingCount;
        }

        private static string ReadAnimatorControllerMainObjectName(string controllerText)
        {
            Assert.IsNotNull(controllerText, "Controller text should not be null when reading AnimatorController main object name.");

            var match = Regex.Match(controllerText,
                @"--- !u!91 &9100000\s+AnimatorController:\s+[\s\S]*?  m_Name: ([^\r\n]*)",
                RegexOptions.Multiline);
            Assert.IsTrue(match.Success,
                "Expected generated FX controller text to contain the AnimatorController main-object block.");
            return match.Groups[1].Value;
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

            string fxBefore = ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprBefore = ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuBefore = ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            LogAssert.Expect(LogType.Error, "[ASM-Lite] slotCount must be between 1 and 8 (got 9).");
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(-1, buildResult,
                $"A49: Build() must reject slotCount outside [1..8] with -1. got {buildResult} for slotCount={_ctx.Comp.slotCount}.");

            var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
            Assert.IsFalse(diagnostic.Success,
                "A49: invalid-slot Build() must expose failing diagnostics instead of returning -1 without context.");
            Assert.AreEqual(ASMLiteDiagnosticCodes.Build.ValidationFailed, diagnostic.Code,
                "A49: invalid slotCount must map to deterministic BUILD-301 validation diagnostics.");
            Assert.AreEqual("slotCount", diagnostic.ContextPath,
                "A49: BUILD-301 diagnostics should identify slotCount as the failing context.");

            string fxAfter = ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprAfter = ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuAfter = ReadPackageAssetText(ASMLiteAssetPaths.Menu);

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

            string fxFirst = ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprFirst = ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuFirst = ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            var firstCtrl = LoadGeneratedFxController();
            var firstExpr = LoadGeneratedExprParams();
            var firstMenu = LoadGeneratedRootMenu();

            int firstLayers = CountASMLiteLayers(firstCtrl);
            int firstFxParams = CountASMLiteFxParams(firstCtrl);
            int firstExprParams = CountASMLiteExprParams(firstExpr);
            int firstMenuControls = CountSettingsManagerControls(firstMenu);

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

            string fxSecond = ReadPackageAssetText(ASMLiteAssetPaths.FXController);
            string exprSecond = ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
            string menuSecond = ReadPackageAssetText(ASMLiteAssetPaths.Menu);

            Assert.AreEqual(exprFirst, exprSecond,
                "A50: repeated Build() should be text-idempotent for generated expression params asset.");
            Assert.AreEqual(menuFirst, menuSecond,
                "A50: repeated Build() should be text-idempotent for generated menu asset.");

            var secondCtrl = LoadGeneratedFxController();
            var secondExpr = LoadGeneratedExprParams();
            var secondMenu = LoadGeneratedRootMenu();

            int secondLayers = CountASMLiteLayers(secondCtrl);
            int secondFxParams = CountASMLiteFxParams(secondCtrl);
            int secondExprParams = CountASMLiteExprParams(secondExpr);
            int secondMenuControls = CountSettingsManagerControls(secondMenu);

            Assert.AreEqual(firstLayers, secondLayers,
                $"A50: ASMLite layer count changed across repeated Build(). first={firstLayers}, second={secondLayers}.");
            Assert.AreEqual(firstFxParams, secondFxParams,
                $"A50: ASMLite FX param count changed across repeated Build(). first={firstFxParams}, second={secondFxParams}.");
            Assert.AreEqual(firstExprParams, secondExprParams,
                $"A50: ASMLite expression param count changed across repeated Build(). first={firstExprParams}, second={secondExprParams}.");
            Assert.AreEqual(1, secondMenuControls,
                $"A50: repeated Build() must keep exactly one Settings Manager control. got {secondMenuControls}.");

            int duplicateFxNames = CountDuplicateFxParamNames(secondCtrl);
            int duplicateExprNames = CountDuplicateExprParamNames(secondExpr);
            Assert.AreEqual(0, duplicateFxNames,
                $"A50: repeated Build() introduced duplicate ASMLite FX param names. duplicateGroups={duplicateFxNames}.");
            Assert.AreEqual(0, duplicateExprNames,
                $"A50: repeated Build() introduced duplicate ASMLite expression param names. duplicateGroups={duplicateExprNames}.");
            Assert.AreEqual(0, CountDanglingLocalFileIds(fxSecond),
                "A50: repeated Build() must not leave dangling local fileID references in the generated FX controller text.");
            Assert.AreEqual("ASMLite_FX", ReadAnimatorControllerMainObjectName(fxSecond),
                "A50: generated FX controller main object name must stay normalized to the filename without the .controller extension to avoid Unity import warnings.");
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
            string exprFirstExcluded = ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);

            int secondExcludedResult = BuildOrFail(_ctx, "A52");
            Assert.AreEqual(firstExcludedResult, secondExcludedResult,
                "A52: repeated exclusion-enabled builds should keep Build() return deterministic.");

            string exprSecondExcluded = ReadPackageAssetText(ASMLiteAssetPaths.ExprParams);
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
