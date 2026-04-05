using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A19-A25: Expression Parameters injection invariants.
    /// Integration category: each test calls Build() and inspects
    /// avDesc.expressionParameters (live injection target).
    /// A21 additionally inspects the stub asset for legacy preservation behavior.
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
            var updated  = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name          = name,
                valueType     = type,
                defaultValue  = defaultValue,
                saved         = true,
                networkSynced = true,
            };
            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        // ── A19 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A19_ASMLiteCtrl_HasCorrectTypeAndFlags()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteBuilder.Build(_ctx.Comp);

            var exprParams = _ctx.AvDesc.expressionParameters.parameters;
            Assert.IsNotNull(exprParams, "expressionParameters.parameters must not be null after Build().");

            var ctrl = exprParams.FirstOrDefault(p => p.name == "ASMLite_Ctrl");
            Assert.IsNotNull(ctrl, "ASMLite_Ctrl must be present in avDesc.expressionParameters after Build().");
            Assert.AreEqual(VRCExpressionParameters.ValueType.Int, ctrl.valueType,
                "ASMLite_Ctrl must have valueType=Int.");
            Assert.IsFalse(ctrl.saved,
                "ASMLite_Ctrl must have saved=false (zero network-bits guarantee).");
            Assert.IsFalse(ctrl.networkSynced,
                "ASMLite_Ctrl must have networkSynced=false (zero network-bits guarantee).");
        }

        // ── A20 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A20_BackupParams_HaveCorrectFlags()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float);
            ASMLiteBuilder.Build(_ctx.Comp);

            var exprParams = _ctx.AvDesc.expressionParameters.parameters;
            var bak = exprParams.FirstOrDefault(p => p.name == "ASMLite_Bak_S1_MyFloat");

            Assert.IsNotNull(bak, "ASMLite_Bak_S1_MyFloat must be present in avDesc.expressionParameters after Build().");
            Assert.IsTrue(bak.saved,
                "Backup param must have saved=true so preset values persist across sessions.");
            Assert.IsFalse(bak.networkSynced,
                "Backup param must have networkSynced=false (zero network-bits guarantee).");
        }

        // ── A21 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A21_LegacyPreservation_HigherSlotBackupsKeptInStubAsset()
        {
            // Build with 2 slots + 1 avatar param so ASMLite_Bak_S2_X is written to stub.
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "X", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            // Build again with 1 slot -- legacy preservation should keep ASMLite_Bak_S2_X
            // in the managed stub asset (not avDesc.expressionParameters which strips all ASMLite_).
            _ctx.Comp.slotCount = 1;
            ASMLiteBuilder.Build(_ctx.Comp);

            // Verify in the stub asset (legacy preservation is in PopulateExpressionParams)
            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(
                ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "Stub VRCExpressionParameters asset must exist after Build().");

            var stubParams = stubAsset.parameters;
            Assert.IsNotNull(stubParams, "Stub parameters must not be null after Build().");

            var legacyBak = stubParams.FirstOrDefault(p => p.name == "ASMLite_Bak_S2_X");
            Assert.IsNotNull(legacyBak,
                "Legacy backup 'ASMLite_Bak_S2_X' must survive in the stub asset when slotCount decreases from 2 to 1.");
        }

        // ── A22 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A22_InjectExpressionParams_NoDuplicatesAfterRebuild()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            // Build twice to exercise the strip-then-re-inject idempotency path.
            ASMLiteBuilder.Build(_ctx.Comp);
            ASMLiteBuilder.Build(_ctx.Comp);

            var exprParams = _ctx.AvDesc.expressionParameters.parameters;
            Assert.IsNotNull(exprParams, "expressionParameters.parameters must not be null after second Build().");

            var asmEntries = exprParams
                .Where(p => p.name != null && p.name.StartsWith("ASMLite_"))
                .Select(p => p.name)
                .ToList();

            var duplicates = asmEntries
                .GroupBy(n => n)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.IsEmpty(duplicates,
                $"No ASMLite_ entry should be duplicated after two consecutive Build() calls. Found: {string.Join(", ", duplicates)}");
        }

        // ── A23 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A23_NonASMLiteParams_SurvivedAfterBuild()
        {
            _ctx.Comp.slotCount = 1;

            // Add a non-ASMLite param directly into avDesc.expressionParameters
            // before Build() to verify InjectExpressionParams preserves it.
            var preExisting = new VRCExpressionParameters.Parameter
            {
                name          = "UserCustomParam",
                valueType     = VRCExpressionParameters.ValueType.Float,
                defaultValue  = 0.5f,
                saved         = true,
                networkSynced = true,
            };
            _ctx.AvDesc.expressionParameters.parameters = new[] { preExisting };
            EditorUtility.SetDirty(_ctx.AvDesc.expressionParameters);
            AssetDatabase.SaveAssets();

            ASMLiteBuilder.Build(_ctx.Comp);

            var exprParams = _ctx.AvDesc.expressionParameters.parameters;
            var userParam = exprParams.FirstOrDefault(p => p.name == "UserCustomParam");
            Assert.IsNotNull(userParam,
                "Non-ASMLite param 'UserCustomParam' must survive Build() -- InjectExpressionParams must not strip it.");
        }

        // ── A24 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A24_DuplicateASMLiteCtrlInAvDesc_DeduplicatedAfterBuild()
        {
            _ctx.Comp.slotCount = 1;

            // Manually inject a duplicate ASMLite_Ctrl into avDesc.expressionParameters
            // before Build() to exercise the dedup guard in InjectExpressionParams.
            var duplicate = new VRCExpressionParameters.Parameter
            {
                name          = "ASMLite_Ctrl",
                valueType     = VRCExpressionParameters.ValueType.Int,
                defaultValue  = 0f,
                saved         = false,
                networkSynced = false,
            };
            _ctx.AvDesc.expressionParameters.parameters = new[] { duplicate };
            EditorUtility.SetDirty(_ctx.AvDesc.expressionParameters);
            AssetDatabase.SaveAssets();

            ASMLiteBuilder.Build(_ctx.Comp);

            var exprParams = _ctx.AvDesc.expressionParameters.parameters;
            int ctrlCount = exprParams.Count(p => p.name == "ASMLite_Ctrl");
            Assert.AreEqual(1, ctrlCount,
                "Exactly one ASMLite_Ctrl entry must exist in avDesc.expressionParameters after Build(), regardless of pre-existing duplicates.");
        }

        // ── A25 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A25_DecreasingSlotCount_DropsHigherSlotBackupsFromAvDesc()
        {
            // Build with 2 slots so ASMLite_Bak_S2_* entries are injected into avDesc.
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "Param", VRCExpressionParameters.ValueType.Float);
            ASMLiteBuilder.Build(_ctx.Comp);

            // Confirm S2 backup is present after first build.
            var afterFirstBuild = _ctx.AvDesc.expressionParameters.parameters;
            Assert.IsNotNull(afterFirstBuild.FirstOrDefault(p => p.name == "ASMLite_Bak_S2_Param"),
                "ASMLite_Bak_S2_Param must be present after Build() with slotCount=2.");

            // Rebuild with 1 slot -- InjectExpressionParams strips all ASMLite_ and
            // only re-adds entries for the current schema, so S2 backups must vanish.
            _ctx.Comp.slotCount = 1;
            ASMLiteBuilder.Build(_ctx.Comp);

            var afterSecondBuild = _ctx.AvDesc.expressionParameters.parameters;
            var s2Entries = afterSecondBuild
                .Where(p => p.name != null && p.name.StartsWith("ASMLite_Bak_S2_"))
                .ToList();

            Assert.IsEmpty(s2Entries,
                $"No ASMLite_Bak_S2_* entries should remain in avDesc.expressionParameters after rebuilding with slotCount=1. Found: {string.Join(", ", s2Entries.Select(p => p.name))}");
        }
    }
}
