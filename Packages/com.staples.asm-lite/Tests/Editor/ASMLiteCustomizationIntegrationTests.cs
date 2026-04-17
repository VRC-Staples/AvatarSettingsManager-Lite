using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteCustomizationIntegrationTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A53: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A53: fixture did not create ASMLiteComponent.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test, Category("Integration")]
        public void A53_CombinedCustomizationFixture_ComposesRootOverridesExclusionsAndInstallPrefix()
        {
            var customRootIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[4], "A53");

            _ctx.Comp.slotCount = 3;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Unified Presets  ";
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = customRootIcon;
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "  Avatars/Integrated  ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { " Drop_Float ", "Drop_Bool", null, "Drop_Float", "GhostMissing" };

            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Keep_Int", VRCExpressionParameters.ValueType.Int, 1f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Keep_Float", VRCExpressionParameters.ValueType.Float, 0.25f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Drop_Float", VRCExpressionParameters.ValueType.Float, 0.7f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Drop_Bool", VRCExpressionParameters.ValueType.Bool, 1f);

            LogAssert.Expect(LogType.Log,
                "[ASM-Lite] Parameter exclusions: requested=3 (raw=5), matched=2, ignored=3 (sanitized=2, stale=1).");

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, buildResult,
                "A53: Build() should include only non-excluded discovered parameters when all customization toggles are enabled.");

            var generatedMenu = LoadGeneratedRootMenu();
            Assert.AreEqual(1, generatedMenu.controls.Count,
                "A53: generated root menu must contain exactly one wrapper control under combined customization settings.");

            var rootControl = generatedMenu.controls[0];
            Assert.IsNotNull(rootControl, "A53: generated root wrapper control must exist.");
            Assert.AreEqual("Unified Presets", rootControl.name,
                "A53: combined fixture should trim and apply custom root name.");
            Assert.AreSame(customRootIcon, rootControl.icon,
                "A53: combined fixture should preserve exact custom root icon reference.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, rootControl.type,
                "A53: generated root wrapper control must remain a submenu.");
            Assert.IsNotNull(rootControl.subMenu,
                "A53: generated root wrapper control must keep submenu wiring.");

            var fxNames = LoadGeneratedFxController().parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToHashSet();

            var exprNames = LoadGeneratedExprParams().parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToHashSet();

            Assert.IsTrue(fxNames.Contains("Keep_Int"), "A53: non-excluded live parameter should remain in generated FX schema.");
            Assert.IsTrue(fxNames.Contains("Keep_Float"), "A53: non-excluded live parameter should remain in generated FX schema.");
            Assert.IsTrue(fxNames.Contains("ASMLite_Def_Keep_Int"), "A53: non-excluded default key should remain in generated FX schema.");
            Assert.IsTrue(fxNames.Contains("ASMLite_Def_Keep_Float"), "A53: non-excluded default key should remain in generated FX schema.");

            Assert.IsFalse(fxNames.Contains("Drop_Float"), "A53: excluded live parameter must be removed from generated FX schema.");
            Assert.IsFalse(fxNames.Contains("Drop_Bool"), "A53: excluded live parameter must be removed from generated FX schema.");
            Assert.IsFalse(fxNames.Contains("ASMLite_Def_Drop_Float"), "A53: excluded default key must be removed from generated FX schema.");
            Assert.IsFalse(fxNames.Contains("ASMLite_Def_Drop_Bool"), "A53: excluded default key must be removed from generated FX schema.");

            for (int slot = 1; slot <= _ctx.Comp.slotCount; slot++)
            {
                Assert.IsTrue(fxNames.Contains($"ASMLite_Bak_S{slot}_Keep_Int"),
                    $"A53: non-excluded backup key ASMLite_Bak_S{slot}_Keep_Int should remain in generated FX schema.");
                Assert.IsTrue(fxNames.Contains($"ASMLite_Bak_S{slot}_Keep_Float"),
                    $"A53: non-excluded backup key ASMLite_Bak_S{slot}_Keep_Float should remain in generated FX schema.");

                Assert.IsFalse(fxNames.Contains($"ASMLite_Bak_S{slot}_Drop_Float"),
                    $"A53: excluded backup key ASMLite_Bak_S{slot}_Drop_Float must be removed from generated FX schema.");
                Assert.IsFalse(fxNames.Contains($"ASMLite_Bak_S{slot}_Drop_Bool"),
                    $"A53: excluded backup key ASMLite_Bak_S{slot}_Drop_Bool must be removed from generated FX schema.");

                Assert.IsTrue(exprNames.Contains($"ASMLite_Bak_S{slot}_Keep_Int"),
                    $"A53: non-excluded backup key ASMLite_Bak_S{slot}_Keep_Int should remain in generated expression schema.");
                Assert.IsTrue(exprNames.Contains($"ASMLite_Bak_S{slot}_Keep_Float"),
                    $"A53: non-excluded backup key ASMLite_Bak_S{slot}_Keep_Float should remain in generated expression schema.");
                Assert.IsFalse(exprNames.Contains($"ASMLite_Bak_S{slot}_Drop_Float"),
                    $"A53: excluded backup key ASMLite_Bak_S{slot}_Drop_Float must be removed from generated expression schema.");
                Assert.IsFalse(exprNames.Contains($"ASMLite_Bak_S{slot}_Drop_Bool"),
                    $"A53: excluded backup key ASMLite_Bak_S{slot}_Drop_Bool must be removed from generated expression schema.");
            }

            LogAssert.Expect(LogType.Log, "[ASM-Lite] FullController menu prefix resolved to 'Avatars/Integrated'.");
            string serializedPrefix = ConfigureFullControllerAndReadPrefix("A53");
            Assert.AreEqual("Avatars/Integrated", serializedPrefix,
                "A53: combined fixture should serialize trimmed custom install path to FullController menu prefix.");
        }

        [Test, Category("Integration")]
        public void A54_CombinedCustomizationFixture_RepeatedBuildStaysDeterministicAndExclusionSafe()
        {
            _ctx.Comp.slotCount = 2;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Repeatable Root  ";
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[0], "A54");
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "  Avatars/Repeatable  ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "Drop_Param", "Drop_Param", " Ghost " };

            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Keep_A", VRCExpressionParameters.ValueType.Int, 2f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Keep_B", VRCExpressionParameters.ValueType.Bool, 1f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Drop_Param", VRCExpressionParameters.ValueType.Float, 0.1f);

            int firstResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, firstResult,
                "A54: first combined build should include only non-excluded discovered parameters.");

            var firstRootMenu = LoadGeneratedRootMenu();
            var firstFx = LoadGeneratedFxController();
            var firstExpr = LoadGeneratedExprParams();

            int firstWrapperCount = firstRootMenu.controls.Count;
            int firstFxSchemaCount = CountGeneratedFxSchemaKeys(firstFx);
            int firstExprSchemaCount = CountGeneratedExpressionSchemaKeys(firstExpr);

            int secondResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(firstResult, secondResult,
                $"A54: repeated combined build should keep Build() return stable. first={firstResult}, second={secondResult}.");

            var secondRootMenu = LoadGeneratedRootMenu();
            var secondFx = LoadGeneratedFxController();
            var secondExpr = LoadGeneratedExprParams();

            int secondWrapperCount = secondRootMenu.controls.Count;
            int secondFxSchemaCount = CountGeneratedFxSchemaKeys(secondFx);
            int secondExprSchemaCount = CountGeneratedExpressionSchemaKeys(secondExpr);

            Assert.AreEqual(1, firstWrapperCount,
                $"A54: first combined build should leave exactly one root wrapper control. got {firstWrapperCount}.");
            Assert.AreEqual(firstWrapperCount, secondWrapperCount,
                $"A54: repeated combined build should preserve root wrapper count. first={firstWrapperCount}, second={secondWrapperCount}.");
            Assert.AreEqual(firstFxSchemaCount, secondFxSchemaCount,
                $"A54: repeated combined build should preserve generated FX schema count. first={firstFxSchemaCount}, second={secondFxSchemaCount}.");
            Assert.AreEqual(firstExprSchemaCount, secondExprSchemaCount,
                $"A54: repeated combined build should preserve generated expression schema count. first={firstExprSchemaCount}, second={secondExprSchemaCount}.");

            var secondFxNames = secondFx.parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToList();

            var secondExprNames = secondExpr.parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToList();

            Assert.IsFalse(secondFxNames.Contains("Drop_Param"),
                "A54: repeated combined build must not reintroduce excluded live parameters in FX schema.");
            Assert.IsFalse(secondFxNames.Contains("ASMLite_Def_Drop_Param"),
                "A54: repeated combined build must not reintroduce excluded default keys in FX schema.");
            Assert.IsFalse(secondFxNames.Any(n => n.StartsWith("ASMLite_Bak_S", System.StringComparison.Ordinal) && n.EndsWith("_Drop_Param", System.StringComparison.Ordinal)),
                "A54: repeated combined build must not reintroduce excluded backup keys in FX schema.");
            Assert.IsFalse(secondExprNames.Any(n => n.StartsWith("ASMLite_Bak_S", System.StringComparison.Ordinal) && n.EndsWith("_Drop_Param", System.StringComparison.Ordinal)),
                "A54: repeated combined build must not reintroduce excluded backup keys in generated expression schema.");

            Assert.AreEqual(0, CountDuplicateNames(secondFxNames),
                "A54: repeated combined build must not duplicate generated FX parameter keys.");
            Assert.AreEqual(0, CountDuplicateNames(secondExprNames),
                "A54: repeated combined build must not duplicate generated expression parameter keys.");
        }

        [Test, Category("Integration")]
        public void A55_AllCustomizationTogglesDisabled_IgnoresStaleSerializedValuesAndMatchesBaselineContracts()
        {
            var staleIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[7], "A55");
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "A55");

            _ctx.Comp.slotCount = 2;
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = "  Stale Root Name  ";
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = staleIcon;
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = "  Avatars/StalePrefix  ";
            _ctx.Comp.useParameterExclusions = false;
            _ctx.Comp.excludedParameterNames = new[] { "Keep_A", "Drop_B", " Ghost " };

            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Keep_A", VRCExpressionParameters.ValueType.Int, 1f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Drop_B", VRCExpressionParameters.ValueType.Float, 0.3f);

            int descriptorFxAsmLayersBefore = _ctx.Ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_", System.StringComparison.Ordinal));
            int descriptorExprAsmBefore = (_ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Count(p => p != null && p.name != null && p.name.StartsWith("ASMLite_", System.StringComparison.Ordinal));
            int descriptorSettingsBefore = (_ctx.AvDesc.expressionsMenu.controls ?? new List<VRCExpressionsMenu.Control>())
                .Count(c => c != null && c.name == ASMLiteBuilder.DefaultRootControlName);

            LogAssert.Expect(LogType.Log, "[ASM-Lite] Parameter exclusions: disabled (requested=0, matched=0, ignored=0).");
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, buildResult,
                "A55: disabled exclusion toggle must ignore stale excluded names and include both discovered parameters.");

            var generatedRoot = LoadGeneratedRootMenu();
            Assert.AreEqual(1, generatedRoot.controls.Count,
                "A55: generated root should contain one wrapper control in disabled-toggle baseline mode.");

            var rootControl = generatedRoot.controls[0];
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, rootControl.name,
                "A55: disabled custom root name toggle must keep baseline Settings Manager wrapper name.");
            Assert.AreSame(fallbackIcon, rootControl.icon,
                "A55: disabled custom root icon toggle must keep bundled presets icon fallback.");

            var generatedFx = LoadGeneratedFxController();
            var generatedExpr = LoadGeneratedExprParams();

            var fxNames = generatedFx.parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToHashSet();
            var exprNames = generatedExpr.parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToHashSet();

            Assert.IsTrue(fxNames.Contains("Keep_A"),
                "A55: disabled exclusion toggle must keep discovered Keep_A source parameter in generated FX schema.");
            Assert.IsTrue(fxNames.Contains("Drop_B"),
                "A55: disabled exclusion toggle must keep discovered Drop_B source parameter in generated FX schema.");
            Assert.IsTrue(fxNames.Contains("ASMLite_Def_Keep_A") && fxNames.Contains("ASMLite_Def_Drop_B"),
                "A55: disabled exclusion toggle must keep default keys for all discovered parameters.");

            for (int slot = 1; slot <= _ctx.Comp.slotCount; slot++)
            {
                Assert.IsTrue(fxNames.Contains($"ASMLite_Bak_S{slot}_Keep_A"),
                    $"A55: disabled exclusion toggle must keep Keep_A backup key ASMLite_Bak_S{slot}_Keep_A in FX schema.");
                Assert.IsTrue(fxNames.Contains($"ASMLite_Bak_S{slot}_Drop_B"),
                    $"A55: disabled exclusion toggle must keep Drop_B backup key ASMLite_Bak_S{slot}_Drop_B in FX schema.");
                Assert.IsTrue(exprNames.Contains($"ASMLite_Bak_S{slot}_Keep_A"),
                    $"A55: disabled exclusion toggle must keep Keep_A backup key ASMLite_Bak_S{slot}_Keep_A in expression schema.");
                Assert.IsTrue(exprNames.Contains($"ASMLite_Bak_S{slot}_Drop_B"),
                    $"A55: disabled exclusion toggle must keep Drop_B backup key ASMLite_Bak_S{slot}_Drop_B in expression schema.");
            }

            int descriptorFxAsmLayersAfter = _ctx.Ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_", System.StringComparison.Ordinal));
            int descriptorExprAsmAfter = (_ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Count(p => p != null && p.name != null && p.name.StartsWith("ASMLite_", System.StringComparison.Ordinal));
            int descriptorSettingsAfter = (_ctx.AvDesc.expressionsMenu.controls ?? new List<VRCExpressionsMenu.Control>())
                .Count(c => c != null && c.name == ASMLiteBuilder.DefaultRootControlName);

            Assert.AreEqual(descriptorFxAsmLayersBefore, descriptorFxAsmLayersAfter,
                "A55: Build() should not inject ASMLite layers into fixture descriptor FX controller during disabled-toggle baseline path.");
            Assert.AreEqual(descriptorExprAsmBefore, descriptorExprAsmAfter,
                "A55: Build() should not inject ASMLite params into fixture descriptor expression parameters during disabled-toggle baseline path.");
            Assert.AreEqual(descriptorSettingsBefore, descriptorSettingsAfter,
                "A55: Build() should not inject Settings Manager controls into fixture descriptor root menu during disabled-toggle baseline path.");

            LogAssert.Expect(LogType.Log, "[ASM-Lite] FullController menu prefix resolved to empty (custom install path disabled or blank).");
            string serializedPrefix = ConfigureFullControllerAndReadPrefix("A55");
            Assert.AreEqual(string.Empty, serializedPrefix,
                "A55: disabled custom install path toggle must serialize empty FullController prefix even when stale path text exists.");
        }

        [Test, Category("Integration")]
        public void A56_DisabledToggleBaseline_RemainsDeterministicAfterPriorEnabledBuild()
        {
            _ctx.Comp.slotCount = 1;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = " Enabled First Pass ";
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[2], "A56");
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = " Avatars/EnabledFirstPass ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "Drop_First" };

            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Keep_First", VRCExpressionParameters.ValueType.Int, 2f);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "Drop_First", VRCExpressionParameters.ValueType.Bool, 1f);

            int enabledResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, enabledResult,
                "A56: setup build should honor enabled exclusion toggle and keep only one discovered parameter.");

            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = " stale disabled name ";
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[6], "A56");
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = " stale/disabled/prefix ";
            _ctx.Comp.useParameterExclusions = false;
            _ctx.Comp.excludedParameterNames = new[] { "Keep_First", "Drop_First" };

            LogAssert.Expect(LogType.Log, "[ASM-Lite] Parameter exclusions: disabled (requested=0, matched=0, ignored=0).");
            int firstDisabledResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, firstDisabledResult,
                "A56: first disabled-toggle build after enabled pass must include both discovered parameters.");

            var firstDisabledMenu = LoadGeneratedRootMenu();
            var firstDisabledFx = LoadGeneratedFxController();
            var firstDisabledExpr = LoadGeneratedExprParams();

            int firstDisabledRootCount = firstDisabledMenu.controls.Count;
            int firstDisabledFxSchemaCount = CountGeneratedFxSchemaKeys(firstDisabledFx);
            int firstDisabledExprSchemaCount = CountGeneratedExpressionSchemaKeys(firstDisabledExpr);

            LogAssert.Expect(LogType.Log, "[ASM-Lite] Parameter exclusions: disabled (requested=0, matched=0, ignored=0).");
            int secondDisabledResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(firstDisabledResult, secondDisabledResult,
                "A56: repeated disabled-toggle build must keep Build() return deterministic.");

            var secondDisabledMenu = LoadGeneratedRootMenu();
            var secondDisabledFx = LoadGeneratedFxController();
            var secondDisabledExpr = LoadGeneratedExprParams();

            Assert.AreEqual(firstDisabledRootCount, secondDisabledMenu.controls.Count,
                "A56: repeated disabled-toggle build must keep root wrapper count deterministic.");
            Assert.AreEqual(firstDisabledFxSchemaCount, CountGeneratedFxSchemaKeys(secondDisabledFx),
                "A56: repeated disabled-toggle build must keep generated FX schema count deterministic.");
            Assert.AreEqual(firstDisabledExprSchemaCount, CountGeneratedExpressionSchemaKeys(secondDisabledExpr),
                "A56: repeated disabled-toggle build must keep generated expression schema count deterministic.");

            var secondFxNames = secondDisabledFx.parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToList();
            var secondExprNames = secondDisabledExpr.parameters
                .Where(p => p != null && !string.IsNullOrEmpty(p.name))
                .Select(p => p.name)
                .ToList();

            Assert.IsTrue(secondFxNames.Contains("Drop_First") && secondFxNames.Contains("ASMLite_Def_Drop_First"),
                "A56: disabled exclusion toggle must keep previously excluded source/default keys in generated FX schema.");
            Assert.IsTrue(secondFxNames.Contains("ASMLite_Bak_S1_Drop_First") && secondExprNames.Contains("ASMLite_Bak_S1_Drop_First"),
                "A56: disabled exclusion toggle must restore previously excluded backup keys in generated FX and expression schemas.");
            Assert.AreEqual(0, CountDuplicateNames(secondFxNames),
                "A56: repeated disabled-toggle build must not introduce duplicate generated FX keys.");
            Assert.AreEqual(0, CountDuplicateNames(secondExprNames),
                "A56: repeated disabled-toggle build must not introduce duplicate generated expression keys.");
        }

        private string ConfigureFullControllerAndReadPrefix(string aid)
        {
            var configureMethod = typeof(ASMLitePrefabCreator).GetMethod(
                "ConfigureVRCFuryFullController",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.IsNotNull(configureMethod,
                $"{aid}: expected ASMLitePrefabCreator.ConfigureVRCFuryFullController private method was not found.");

            Assert.DoesNotThrow(() => configureMethod.Invoke(null, new object[] { _ctx.Comp.gameObject, _ctx.Comp }),
                $"{aid}: FullController wiring should complete without throwing for combined customization fixture.");

            var vf = _ctx.Comp.GetComponent<VF.Model.VRCFury>();
            Assert.IsNotNull(vf,
                $"{aid}: combined customization fixture should have VF.Model.VRCFury component after wiring refresh.");

            return ASMLiteTestFixtures.ReadSerializedMenuPrefix(vf);
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
            Assert.IsNotNull(expr.parameters,
                "Generated expression params list must not be null.");
            return expr;
        }

        private static VRCExpressionsMenu LoadGeneratedRootMenu()
        {
            var root = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(root,
                $"Generated root menu missing at '{ASMLiteAssetPaths.Menu}'.");
            Assert.IsNotNull(root.controls,
                "Generated root menu controls list must not be null.");
            return root;
        }

        private static Texture2D LoadIconOrFail(string path, string aid)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(icon,
                $"{aid}: expected icon asset at '{path}' but LoadAssetAtPath returned null.");
            return icon;
        }

        private static int CountGeneratedFxSchemaKeys(AnimatorController ctrl)
            => ctrl.parameters.Count(p => p != null && !string.IsNullOrEmpty(p.name)
                && (p.name == ASMLiteBuilder.CtrlParam || p.name.StartsWith("ASMLite_", System.StringComparison.Ordinal)));

        private static int CountGeneratedExpressionSchemaKeys(VRCExpressionParameters expr)
            => expr.parameters.Count(p => p != null && !string.IsNullOrEmpty(p.name)
                && (p.name == ASMLiteBuilder.CtrlParam || p.name.StartsWith("ASMLite_", System.StringComparison.Ordinal)));

        private static int CountDuplicateNames(IEnumerable<string> names)
            => names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .GroupBy(n => n)
                .Count(g => g.Count() > 1);
    }
}
