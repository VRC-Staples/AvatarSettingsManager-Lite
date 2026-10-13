using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using ASMLite.Editor;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// ASMLiteCtrl_HasCorrectTypeFlags_AndSingleInstance_InGeneratedAsset-LegacyBackupFlags_AreForcedToLocalOnly_WhenPreserved: Generated expression-parameter schema invariants.
    /// Integration category: each test calls Build() and inspects the managed
    /// generated stub asset (ASMLiteAssetPaths.ExprParams), which is the current
    /// delivery source for VRCFury FullController wiring.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteExpressionParameterGenerationIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteExpressionParameterGenerationIntegrationTests);
        private static ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _testGeneratedAssetsBaseline;
        private AsmLiteTestContext _ctx;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = null;
        }

        [SetUp]
        public void SetUp()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            ASMLiteToggleNameBroker.ResetLatestEnrollmentStateForTests();
            ASMLiteToggleNameBroker.ClearPendingRestoreState();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteToggleNameBroker.ClearPendingRestoreState();
                ASMLiteToggleNameBroker.ResetLatestEnrollmentStateForTests();
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            }
            finally
            {
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
                _testGeneratedAssetsBaseline = null;
                _ctx = null;
            }
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

        private VRCExpressionParameters.Parameter[] LoadGeneratedParams()
        {
            var stubAsset = ASMLiteTestFixtures.ReadGeneratedAsset<VRCExpressionParameters>(_ctx.Comp,
                ASMLiteDriftProbe.ParametersObjectRefPath, ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath);
            Assert.IsNotNull(stubAsset, "Generated VRCExpressionParameters asset must exist after Build().");
            Assert.IsNotNull(stubAsset.parameters, "Generated parameters must not be null after Build().");
            return stubAsset.parameters;
        }

        private AnimatorController LoadGeneratedController()
        {
            var ctrl = ASMLiteTestFixtures.ReadGeneratedAsset<AnimatorController>(_ctx.Comp,
                ASMLiteDriftProbe.ControllerObjectRefPath);
            Assert.IsNotNull(ctrl, "Generated FX controller must exist after Build().");
            return ctrl;
        }

        private static VRC_AvatarParameterDriver LoadSlotDriver(AnimatorController ctrl, string layerName, string stateName)
        {
            var layer = ctrl.layers.FirstOrDefault(l => l.name == layerName);
            Assert.IsNotNull(layer.stateMachine, $"Expected layer '{layerName}' in generated controller.");

            var state = layer.stateMachine.states.FirstOrDefault(s => s.state.name == stateName).state;
            Assert.IsNotNull(state, $"Expected state '{stateName}' in layer '{layerName}'.");

            var driver = state.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();
            Assert.IsNotNull(driver, $"Expected one VRCAvatarParameterDriver on state '{stateName}'.");
            return driver;
        }

        [Test, Category("Integration")]
        public void ASMLiteCtrl_HasCorrectTypeFlags_AndSingleInstance_InGeneratedAsset()
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

        [Test, Category("Integration")]
        public void BackupParams_HaveLocalOnlyFlags_InGeneratedAsset()
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

        [Test, Category("Integration")]
        public void LegacyPreservation_HigherSlotBackupsKeptInGeneratedAsset()
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

        [Test, Category("Integration")]
        public void GeneratedExpressionParams_NoDuplicateASMLiteEntries_AfterRebuild()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            ASMLiteBuilder.Build(_ctx.Comp);
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();

            var duplicates = generated
                .Where(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter)
                .GroupBy(p => p.name)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.IsEmpty(duplicates,
                $"No ASMLite_ entry should be duplicated in generated params after two consecutive Build() calls. Found: {string.Join(", ", duplicates)}");
        }

        [Test, Category("Integration")]
        public void GeneratedSchema_UsesSharedCtrlAndLocalDefaultKeys_NoLegacySafeBoolControls()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "Param", VRCExpressionParameters.ValueType.Int, 4f);
            ASMLiteBuilder.Build(_ctx.Comp);

            var generated = LoadGeneratedParams();

            var nonBackupAsmParams = generated
                .Where(p => ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter(p) && !p.name.StartsWith("ASMLite_Bak_"))
                .Select(p => p.name)
                .ToList();

            CollectionAssert.AreEquivalent(new[] { "ASMLite_Ctrl" }, nonBackupAsmParams,
                "Literal Clear defaults require no runtime default storage.");
            Assert.IsTrue(generated.All(p => !p.networkSynced), "Removing local defaults must retain zero synced bits.");
            foreach (int slot in new[] { 1, 2 })
            {
                var backup = generated.Single(p => p.name == $"ASMLite_Bak_S{slot}_Param");
                Assert.AreEqual(4f, backup.defaultValue);
                Assert.AreEqual(VRCExpressionParameters.ValueType.Int, backup.valueType);
                Assert.IsTrue(backup.saved);
            }

            Assert.IsFalse(generated.Any(p => p.name != null && p.name.StartsWith("ASMLite_S", System.StringComparison.Ordinal)),
                "Legacy SafeBool-style control params (ASMLite_S*) must not be emitted in generated expression params.");
        }

        [Test, Category("Integration")]
        public void StaleGeneratedCtrlShape_IsNormalizedOnBuild()
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

        [Test, Category("Integration")]
        public void LegacyBackupFlags_AreForcedToLocalOnly_WhenPreserved()
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

        [Test, Category("Integration")]
        public void LegacyAliasDiagnostics_CountUnmatchedAndMalformedWithoutWiringWrongLoadPaths()
        {
            const string deterministicSource = "ASM_VF_Menu_Cape__TestAvatar_ASMLite";
            const string unmatchedLegacy = "ASMLite_Bak_S1_LegacyBrokered_Menu/Cape";

            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "Generated expression parameters asset must exist for unmatched alias setup.");
            stubAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = unmatchedLegacy, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.1f, saved = true, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = "ASMLite_Bak_S_", valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.2f, saved = true, networkSynced = true },
            };
            EditorUtility.SetDirty(stubAsset);
            AssetDatabase.SaveAssets();

            AddParam(_ctx, deterministicSource, VRCExpressionParameters.ValueType.Float, 0.5f);
            _ctx.Comp.slotCount = 1;

            ASMLiteBuilder.Build(_ctx.Comp);

            var generatedCtrl = LoadGeneratedController();
            var loadDriver = LoadSlotDriver(generatedCtrl, "ASMLite_Slot1", "LoadSlot1");
            Assert.IsFalse(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == unmatchedLegacy && p.name == deterministicSource),
                "Unmatched legacy backup aliases must not be wired into deterministic load path.");

            var generatedParams = LoadGeneratedParams();
            Assert.IsTrue(generatedParams.Any(p => p.name == unmatchedLegacy),
                "Unmatched but well-formed legacy backup should remain preserved.");
            Assert.IsFalse(generatedParams.Any(p => p.name == "ASMLite_Bak_S_"),
                "Malformed legacy backup names must be excluded from regenerated expression params.");

            var report = ASMLiteBuilder.GetLatestLegacyAliasContinuityReport();
            Assert.GreaterOrEqual(report.UnmatchedCount, 1, "Unmatched continuity counter must include aliases with no broker mapping.");
            Assert.GreaterOrEqual(report.MalformedCount, 1, "Malformed continuity counter must include invalid legacy backup names.");
        }

        [Test, Category("Integration")]
        public void LegacyAliasDiagnostics_KeepGeneratedExpressionParamsDistinctWhilePreservingUnmatchedState()
        {
            const string deterministicSource = "ASM_VF_Menu_Cape__TestAvatar_ASMLite";
            const string unmatchedLegacy = "ASMLite_Bak_S1_LegacyBrokered_Menu/Cape";

            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "Generated expression parameters asset must exist for duplicate-preservation validation.");
            stubAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = unmatchedLegacy, valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.1f, saved = true, networkSynced = true },
                new VRCExpressionParameters.Parameter { name = $"ASMLite_Bak_S1_{deterministicSource}", valueType = VRCExpressionParameters.ValueType.Float, defaultValue = 0.2f, saved = true, networkSynced = true },
            };
            EditorUtility.SetDirty(stubAsset);
            AssetDatabase.SaveAssets();

            AddParam(_ctx, deterministicSource, VRCExpressionParameters.ValueType.Float, 0.5f);
            _ctx.Comp.slotCount = 1;

            ASMLiteBuilder.Build(_ctx.Comp);

            var generatedParams = LoadGeneratedParams();
            var duplicateNames = generatedParams
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.name))
                .GroupBy(p => p.name)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            CollectionAssert.Contains(generatedParams.Select(p => p.name).ToArray(), unmatchedLegacy,
                "Unmatched but well-formed legacy aliases must still survive in the generated expression parameter asset.");
            Assert.AreEqual(0, duplicateNames.Length,
                "Legacy preservation must not emit duplicate expression-parameter names when current deterministic backups overlap with preserved legacy keys.");
        }

        [Test, Category("Integration")]
        public void ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "KeepA", VRCExpressionParameters.ValueType.Int, 3f);
            AddParam(_ctx, "DropB", VRCExpressionParameters.ValueType.Float, 0.75f);

            _ctx.Comp.useParameterExclusions = false;
            int baselineBuild = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, baselineBuild, "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: baseline build should discover both params before exclusions are enabled.");

            var baselineGenerated = LoadGeneratedParams();
            Assert.IsTrue(baselineGenerated.Any(p => p.name == "ASMLite_Bak_S1_DropB"),
                "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: baseline build must include backup key for DropB before exclusions are enabled.");
            Assert.IsTrue(baselineGenerated.Any(p => p.name == "ASMLite_Bak_S2_DropB"),
                "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: baseline build must include backup key for DropB before exclusions are enabled.");

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "DropB", "DropB", " GhostMissing " };

            int excludedBuild = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, excludedBuild,
                "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: exclusion-enabled build should return only non-excluded discovered params.");

            var generated = LoadGeneratedParams();
            var names = generated.Select(p => p.name).ToHashSet();

            Assert.IsTrue(names.Contains("ASMLite_Ctrl"), "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: control key must remain present in generated expression params.");
            Assert.IsTrue(names.Contains("ASMLite_Bak_S1_KeepA"), "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: non-excluded backup key should remain generated.");
            Assert.IsTrue(names.Contains("ASMLite_Bak_S2_KeepA"), "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: non-excluded backup key should remain generated.");

            Assert.IsFalse(names.Contains("ASMLite_Bak_S1_DropB"),
                "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: excluded backup key must be removed from generated expression params after exclusions are enabled.");
            Assert.IsFalse(names.Contains("ASMLite_Bak_S2_DropB"),
                "ExclusionsEnabled_RemovePreviouslyGeneratedExcludedBackupsFromExpressionAsset: excluded backup key must be removed from generated expression params after exclusions are enabled.");
        }
    }
}
