using NUnit.Framework;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A19-A25: Generated expression-parameter schema invariants.
    /// Integration category: each test calls Build() and inspects the managed
    /// generated stub asset (ASMLiteAssetPaths.ExprParams), which is the current
    /// delivery source for VRCFury FullController wiring.
    /// </summary>
    [TestFixture]
    public class ASMLiteExpressionParamsTests
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
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddParam(AsmLiteTestContext ctx, string name,
            VRCExpressionParameters.ValueType type, float defaultValue = 0f)
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

        private static VRCExpressionParameters.Parameter[] LoadGeneratedParams()
        {
            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "Generated VRCExpressionParameters asset must exist after Build().");
            Assert.IsNotNull(stubAsset.parameters, "Generated parameters must not be null after Build().");
            return stubAsset.parameters;
        }

        // ── A19 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A19_ASMLiteCtrl_HasCorrectTypeFlags_AndSingleInstance_InGeneratedAsset()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();

            int ctrlCount = generated.Count(p => p.name == "ASMLite_Ctrl");
            Assert.AreEqual(1, ctrlCount,
                "Generated expression params must contain exactly one ASMLite_Ctrl entry.");

            var ctrl = generated.First(p => p.name == "ASMLite_Ctrl");
            Assert.AreEqual(VRCExpressionParameters.ValueType.Int, ctrl.valueType,
                "ASMLite_Ctrl must have valueType=Int.");
            Assert.IsFalse(ctrl.saved,
                "ASMLite_Ctrl must have saved=false.");
            Assert.IsFalse(ctrl.networkSynced,
                "ASMLite_Ctrl must have networkSynced=false.");
        }

        // ── A20 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A20_BackupParams_HaveLocalOnlyFlags_InGeneratedAsset()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float, 0.75f);
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();
            var bak = generated.FirstOrDefault(p => p.name == "ASMLite_Bak_S1_MyFloat");

            Assert.IsNotNull(bak, "ASMLite_Bak_S1_MyFloat must be present in generated expression params.");
            Assert.AreEqual(VRCExpressionParameters.ValueType.Float, bak.valueType,
                "Backup param valueType must mirror the source avatar param.");
            Assert.AreEqual(0.75f, bak.defaultValue,
                "Backup param defaultValue must mirror the source avatar param.");
            Assert.IsTrue(bak.saved,
                "Backup params must have saved=true so preset values persist across sessions.");
            Assert.IsFalse(bak.networkSynced,
                "Backup params must have networkSynced=false (zero synced budget impact).");
        }

        // ── A21 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A21_LegacyPreservation_HigherSlotBackupsKeptInGeneratedAsset()
        {
            // Build with 2 slots + 1 avatar param so ASMLite_Bak_S2_X is written.
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "X", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            // Build again with 1 slot -- legacy preservation should keep ASMLite_Bak_S2_X
            // in the generated asset.
            _ctx.Comp.slotCount = 1;
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();
            var legacyBak = generated.FirstOrDefault(p => p.name == "ASMLite_Bak_S2_X");
            Assert.IsNotNull(legacyBak,
                "Legacy backup 'ASMLite_Bak_S2_X' must survive in generated params when slotCount decreases from 2 to 1.");
            Assert.IsTrue(legacyBak.saved, "Preserved legacy backups must remain saved=true.");
            Assert.IsFalse(legacyBak.networkSynced, "Preserved legacy backups must remain networkSynced=false.");
        }

        // ── A22 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A22_GeneratedExpressionParams_NoDuplicateASMLiteEntries_AfterRebuild()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            ASMLiteBuilder.Build(_ctx.Comp);
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();

            var duplicates = generated
                .Where(p => p.name != null && p.name.StartsWith("ASMLite_"))
                .GroupBy(p => p.name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.IsEmpty(duplicates,
                $"No ASMLite_ entry should be duplicated in generated params after two consecutive Build() calls. Found: {string.Join(", ", duplicates)}");
        }

        // ── A23 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A23_GeneratedSchema_UsesOnlySharedCtrl_NoLegacySafeBoolControls()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "Param", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();

            var nonBackupAsmParams = generated
                .Where(p => p.name != null && p.name.StartsWith("ASMLite_") && !p.name.StartsWith("ASMLite_Bak_"))
                .Select(p => p.name)
                .ToList();

            CollectionAssert.AreEquivalent(new[] { "ASMLite_Ctrl" }, nonBackupAsmParams,
                "Generated expression params must emit ASMLite_Ctrl as the only control key.");

            Assert.IsFalse(generated.Any(p => p.name != null && p.name.StartsWith("ASMLite_S", System.StringComparison.Ordinal)),
                "Legacy SafeBool-style control params (ASMLite_S*) must not be emitted in generated expression params.");
        }

        // ── A24 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A24_StaleGeneratedCtrlShape_IsNormalizedOnBuild()
        {
            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "Generated VRCExpressionParameters asset must exist for stale-shape setup.");

            // Seed an intentionally stale/invalid control shape to verify build normalization.
            stubAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "ASMLite_Ctrl",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "ASMLite_Ctrl",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.5f,
                    saved = true,
                    networkSynced = true,
                }
            };
            EditorUtility.SetDirty(stubAsset);
            AssetDatabase.SaveAssets();

            _ctx.Comp.slotCount = 1;
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();
            int ctrlCount = generated.Count(p => p.name == "ASMLite_Ctrl");
            Assert.AreEqual(1, ctrlCount,
                "Build must normalize stale generated control shape to exactly one ASMLite_Ctrl.");

            var ctrl = generated.First(p => p.name == "ASMLite_Ctrl");
            Assert.AreEqual(VRCExpressionParameters.ValueType.Int, ctrl.valueType,
                "Normalized ASMLite_Ctrl must be Int.");
            Assert.IsFalse(ctrl.saved, "Normalized ASMLite_Ctrl must set saved=false.");
            Assert.IsFalse(ctrl.networkSynced, "Normalized ASMLite_Ctrl must set networkSynced=false.");
        }

        // ── A25 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A25_LegacyBackupFlags_AreForcedToLocalOnly_WhenPreserved()
        {
            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "Generated VRCExpressionParameters asset must exist for legacy setup.");

            stubAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "ASMLite_Bak_S2_Legacy",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.33f,
                    saved = false,
                    networkSynced = true,
                }
            };
            EditorUtility.SetDirty(stubAsset);
            AssetDatabase.SaveAssets();

            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "Current", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();
            var legacy = generated.FirstOrDefault(p => p.name == "ASMLite_Bak_S2_Legacy");
            Assert.IsNotNull(legacy,
                "Legacy backup key must remain preserved in generated params even when outside current slot range.");
            Assert.IsTrue(legacy.saved,
                "Preserved legacy backups must be forced to saved=true.");
            Assert.IsFalse(legacy.networkSynced,
                "Preserved legacy backups must be forced to networkSynced=false.");
        }
    }
}
