using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// VF delivery pipeline assertions that intentionally avoid direct descriptor-injection checks.
    /// These tests prove Build() writes generated assets for VRCFury pickup while leaving fixture
    /// descriptor surfaces untouched.
    /// </summary>
    [TestFixture]
    public class ASMLiteVRCFuryPipelineTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "VF01: fixture creation returned null context.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        private static AnimatorController LoadGeneratedController(string aid)
        {
            var generatedCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(generatedCtrl, $"{aid}: generated FX controller must exist.");
            return generatedCtrl;
        }

        private static VRCExpressionParameters LoadGeneratedParams(string aid)
        {
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedExpr, $"{aid}: generated expression params must exist.");
            Assert.IsNotNull(generatedExpr.parameters, $"{aid}: generated expression params list must not be null.");
            return generatedExpr;
        }

        [Test, Category("Integration")]
        public void VF01_Build_WritesGeneratedAssets_ButDoesNotMutateFixtureDescriptorSurfaces()
        {
            _ctx.Comp.slotCount = 2;
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "VF01_UserParam", VRCExpressionParameters.ValueType.Int);

            int liveFxAsmLayersBefore = _ctx.Ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_"));
            int liveExprAsmBefore = (_ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Count(p => p != null && p.name != null && p.name.StartsWith("ASMLite_"));
            int liveMenuSettingsBefore = (_ctx.AvDesc.expressionsMenu.controls ?? new System.Collections.Generic.List<VRCExpressionsMenu.Control>())
                .Count(c => c != null && c.name == "Settings Manager");

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                $"VF01: Build should discover exactly one user parameter. got {buildResult}.");

            int liveFxAsmLayersAfter = _ctx.Ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_"));
            int liveExprAsmAfter = (_ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Count(p => p != null && p.name != null && p.name.StartsWith("ASMLite_"));
            int liveMenuSettingsAfter = (_ctx.AvDesc.expressionsMenu.controls ?? new System.Collections.Generic.List<VRCExpressionsMenu.Control>())
                .Count(c => c != null && c.name == "Settings Manager");

            Assert.AreEqual(liveFxAsmLayersBefore, liveFxAsmLayersAfter,
                $"VF01: Build should not inject ASMLite layers into fixture descriptor FX controller. before={liveFxAsmLayersBefore}, after={liveFxAsmLayersAfter}.");
            Assert.AreEqual(liveExprAsmBefore, liveExprAsmAfter,
                $"VF01: Build should not inject ASMLite expression params into fixture descriptor asset. before={liveExprAsmBefore}, after={liveExprAsmAfter}.");
            Assert.AreEqual(liveMenuSettingsBefore, liveMenuSettingsAfter,
                $"VF01: Build should not inject Settings Manager into fixture descriptor root menu. before={liveMenuSettingsBefore}, after={liveMenuSettingsAfter}.");

            var generatedCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            var generatedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);

            Assert.IsNotNull(generatedCtrl, "VF01: generated FX controller must exist.");
            Assert.IsNotNull(generatedExpr, "VF01: generated expression params must exist.");
            Assert.IsNotNull(generatedMenu, "VF01: generated menu must exist.");

            Assert.AreEqual(2, generatedCtrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_")),
                "VF01: generated FX controller should carry one ASMLite layer per configured slot.");
            Assert.AreEqual(3,
                generatedExpr.parameters.Count(p => p != null && p.name != null && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl")),
                "VF01: generated expression params should contain ASMLite_Ctrl + one backup per slot.");
            Assert.AreEqual(1,
                generatedMenu.controls.Count(c => c != null && c.name == "Settings Manager"),
                "VF01: generated root menu should contain one Settings Manager wrapper.");
        }

        [Test, Category("Integration")]
        public void VF02_Regression_StaleFirstUploadSchemaLag_FirstRebuildUsesCurrentDescriptorParamSet()
        {
            _ctx.Comp.slotCount = 1;

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "OldSchemaParam",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                });
            int firstBuild = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, firstBuild,
                $"VF02: setup failure, first build should discover one stale param. got {firstBuild}.");

            var beforeCtrl = LoadGeneratedController("VF02");
            Assert.IsTrue(beforeCtrl.parameters.Any(p => p.name == "OldSchemaParam"),
                "VF02: setup failure, expected stale schema marker in generated FX controller before rebuild.");

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "VF135_Clothing/Rezz",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            int rebuildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, rebuildResult,
                $"VF02: rebuild should discover the current descriptor schema in one pass. got {rebuildResult}.");

            var rebuiltCtrl = LoadGeneratedController("VF02");
            var rebuiltExpr = LoadGeneratedParams("VF02");

            Assert.IsTrue(rebuiltCtrl.parameters.Any(p => p.name == "VF135_Clothing/Rezz"),
                "VF02 regression guard: first rebuild must include the current VF-scoped parameter in generated FX controller.");
            Assert.IsFalse(rebuiltCtrl.parameters.Any(p => p.name == "OldSchemaParam"),
                "VF02 regression guard: first rebuild must not require a second upload cycle to evict stale FX schema names.");
            Assert.IsTrue(rebuiltExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_VF135_Clothing/Rezz"),
                "VF02 regression guard: first rebuild must emit backup key for the current VF-scoped parameter.");
        }

        [Test, Category("Integration")]
        public void VF03_Regression_VFPickupDrift_OpaqueVFNamesRemainUnrenamedInGeneratedAssets()
        {
            _ctx.Comp.slotCount = 2;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "VF120_Clothing/Rezz",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.25f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "VF120_Menu/Hood",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, buildResult,
                $"VF03: setup failure, expected two discovered VF-scoped params. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("VF03");
            var generatedExpr = LoadGeneratedParams("VF03");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "VF120_Clothing/Rezz"),
                "VF03 regression guard: generated FX controller must preserve VF-scoped source names exactly.");
            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "VF120_Menu/Hood"),
                "VF03 regression guard: generated FX controller must preserve VF-scoped source names exactly.");
            Assert.IsFalse(generatedCtrl.parameters.Any(p => p.name == "Rezz" || p.name == "Hood"),
                "VF03 regression guard: generated FX controller must not strip VF prefixes or rename opaque source names.");

            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_VF120_Clothing/Rezz"),
                "VF03 regression guard: generated expression params must preserve VF-scoped backup key naming for slot 1.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S2_VF120_Menu/Hood"),
                "VF03 regression guard: generated expression params must preserve VF-scoped backup key naming for slot 2.");
        }

        [Test, Category("Integration")]
        public void VF04_Regression_DuplicateDescriptorParams_DoNotDuplicateGeneratedKeysOrBreakBuild()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "VF200_Mode/Outfit",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "VF200_Mode/Outfit",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "VF200_Mode/Accessory",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(3, buildResult,
                $"VF04: discovery should still observe all descriptor entries before generation dedupe. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("VF04");
            var generatedExpr = LoadGeneratedParams("VF04");

            int sourceDuplicates = generatedCtrl.parameters.Count(p => p.name == "VF200_Mode/Outfit");
            int backupDuplicates = generatedExpr.parameters.Count(p => p != null && p.name == "ASMLite_Bak_S1_VF200_Mode/Outfit");

            Assert.AreEqual(1, sourceDuplicates,
                "VF04 regression guard: duplicate descriptor names must be deduped in generated FX source parameter declarations.");
            Assert.AreEqual(1, backupDuplicates,
                "VF04 regression guard: duplicate descriptor names must be deduped in generated expression backup keys.");
            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "VF200_Mode/Accessory"),
                "VF04 regression guard: dedupe path must preserve distinct sibling parameters.");
        }

        [Test, Category("Integration")]
        public void VF05_Regression_BrokerDeterministicNames_AreConsumedAsOpaqueSourceParams()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "ASM_VF_Outfit_Hood__Avatar_ASM_Lite",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "ASM_VF_Outfit_Hat__Avatar_ASM_Lite",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.5f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, buildResult,
                $"VF05: build should discover broker-assigned deterministic names as regular source params. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("VF05");
            var generatedExpr = LoadGeneratedParams("VF05");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "ASM_VF_Outfit_Hood__Avatar_ASM_Lite"),
                "VF05 regression guard: generated FX controller must preserve broker deterministic source names exactly.");
            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "ASM_VF_Outfit_Hat__Avatar_ASM_Lite"),
                "VF05 regression guard: generated FX controller must preserve broker deterministic source names exactly.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_ASM_VF_Outfit_Hood__Avatar_ASM_Lite"),
                "VF05 regression guard: generated backup keys must include broker deterministic source names without rewriting.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_ASM_VF_Outfit_Hat__Avatar_ASM_Lite"),
                "VF05 regression guard: generated backup keys must include broker deterministic source names without rewriting.");
        }
    }
}
