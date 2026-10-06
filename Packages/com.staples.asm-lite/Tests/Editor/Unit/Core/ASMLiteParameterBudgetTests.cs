using System.Collections;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public sealed class ASMLiteParameterBudgetTests
    {
        private AsmLiteTestContext _ctx;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _baseline;

        [SetUp]
        public void SetUp()
        {
            _baseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(nameof(ASMLiteParameterBudgetTests));
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _baseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
        }

        [Test]
        public void DirectBuild_OverBudgetOriginalsWithAllBackupsExcluded_RejectsBeforeWriting()
        {
            _ctx.ParamsAsset.parameters = Enumerable.Range(0, 8192).Select(i =>
                new VRCExpressionParameters.Parameter
                {
                    name = "Original_" + i,
                    valueType = (VRCExpressionParameters.ValueType)(i % 3),
                    networkSynced = false,
                }).ToArray();
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = _ctx.ParamsAsset.parameters.Select(p => p.name).ToArray();
            var generated = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            string before = EditorJsonUtility.ToJson(generated);
            var result = ASMLiteBuilder.TryBuildWithDiagnostics(_ctx.Comp, out int backedUp);
            Assert.IsFalse(result.Success, "Originals still consume capacity when backup is excluded.");
            Assert.AreEqual(-1, backedUp);
            StringAssert.Contains("8,193", result.Message);
            StringAssert.Contains("Customize parameter backup", result.Remediation);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(generated), "Rejected build must not rewrite generated assets.");
            Assert.AreSame(_ctx.ParamsAsset, _ctx.AvDesc.expressionParameters);
        }

        [Test]
        public void Window_OverBudget_DisablesRebuildButLeavesRemoveAvailable()
        {
            ConfigureOverBudgetOriginals();
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var hierarchy = window.GetActionHierarchyContract();
                Assert.IsFalse(hierarchy.Descriptors.Single(d => d.Action == ASMLiteWindow.AsmLiteWindowAction.Rebuild).IsEnabled);
                Assert.IsTrue(hierarchy.Descriptors.Single(d => d.Action == ASMLiteWindow.AsmLiteWindowAction.RemovePrefab).IsEnabled);
                Undo.RecordObject(_ctx.ParamsAsset, "Budget test source edit");
                _ctx.ParamsAsset.parameters = _ctx.ParamsAsset.parameters.Take(10).ToArray();
                EditorUtility.SetDirty(_ctx.ParamsAsset);
                Undo.FlushUndoRecordObjects();
                Assert.IsTrue(window.GetActionHierarchyContract().PrimaryDescriptors[0].IsEnabled, "Action must re-evaluate changed source assets.");
                Undo.PerformUndo();
                Assert.IsFalse(window.GetActionHierarchyContract().PrimaryDescriptors[0].IsEnabled, "Undo must restore the over-limit decision.");
                var other = new GameObject("BudgetSwitchAvatar");
                try
                {
                    window.SelectAvatarForAutomation(other.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>());
                    Assert.IsTrue(window.GetActionHierarchyContract().PrimaryDescriptors[0].IsEnabled, "Avatar switching must not retain the previous decision.");
                }
                finally { Object.DestroyImmediate(other); }
            }
            finally { Undo.ClearUndo(_ctx.ParamsAsset); Object.DestroyImmediate(window); }
        }

        [Test]
        public void DetachedRecovery_OverBudget_RejectsBeforeCleanupOrReattachment()
        {
            _ctx.ParamsAsset.parameters = Enumerable.Range(0, 1024).Select(i =>
                new VRCExpressionParameters.Parameter { name = "User_" + i, networkSynced = false }).ToArray();
            _ctx.Comp.slotCount = 8;
            var settings = ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp);
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.MenuAsset.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Settings Manager", type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            });
            string parametersBefore = EditorJsonUtility.ToJson(_ctx.ParamsAsset);
            string menuBefore = EditorJsonUtility.ToJson(_ctx.MenuAsset);
            var result = ASMLiteLifecycleTransactionService.ExecuteDetachedReturnToPackageManagedRecovery(_ctx.AvDesc, settings);
            Assert.IsFalse(result.Success);
            Assert.IsFalse(result.CleanupAttempted, "Budget rejection must precede baked-only cleanup.");
            Assert.IsFalse(result.ReattachAttempted);
            Assert.AreEqual(parametersBefore, EditorJsonUtility.ToJson(_ctx.ParamsAsset));
            Assert.AreEqual(menuBefore, EditorJsonUtility.ToJson(_ctx.MenuAsset));
            Assert.IsNull(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(true));
            var draft = ASMLiteCustomizationDraft.FromSnapshot(settings);
            draft.UseParameterExclusions = true;
            draft.ExcludedParameterNames = _ctx.ParamsAsset.parameters.Select(p => p.name).ToArray();
            var recovered = ASMLiteLifecycleTransactionService.ExecuteDetachedReturnToPackageManagedRecovery(_ctx.AvDesc, draft.ToComponentSnapshot());
            Assert.IsTrue(recovered.Success, recovered.Message);
            Assert.IsNotNull(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(true));
        }

        private void ConfigureOverBudgetOriginals()
        {
            _ctx.ParamsAsset.parameters = Enumerable.Range(0, 8192).Select(i =>
                new VRCExpressionParameters.Parameter { name = "Original_" + i, networkSynced = false }).ToArray();
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = _ctx.ParamsAsset.parameters.Select(p => p.name).ToArray();
        }

        [Test]
        public void Window_DisplayBudget_ReusesEstimateButKeepsValidationFresh()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "User", VRCExpressionParameters.ValueType.Bool);
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                int initial = window.GetDisplayParameterBudget().Total;
                var payload = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
                payload.parameters = payload.parameters.Concat(new[]
                {
                    new VRCExpressionParameters.Parameter { name = "ASMLite_Bak_S9_Old", networkSynced = false },
                }).ToArray();

                Assert.AreEqual(initial, window.GetDisplayParameterBudget().Total, "Drawing should reuse its estimate until an input change is published.");
                Assert.AreEqual(initial + 1, window.GetParameterBudget().Total, "Safety checks must bypass the display cache.");
                window.HandleParameterBudgetInputChanged(payload);
                Assert.AreEqual(initial + 1, window.GetDisplayParameterBudget().Total, "Generated parameter changes must invalidate the estimate.");

                _ctx.Comp.slotCount++;
                Assert.AreEqual(initial + 2, window.GetDisplayParameterBudget().Total, "Preset edits must refresh immediately.");
                _ctx.Comp.useParameterExclusions = true;
                _ctx.Comp.excludedParameterNames = new[] { "User" };
                int excluded = window.GetDisplayParameterBudget().Total;
                Assert.Less(excluded, initial);
                _ctx.Comp.excludedParameterNames[0] = "Other";
                Assert.Greater(window.GetDisplayParameterBudget().Total, excluded, "In-place exclusion edits must not alias the cache key.");

                ConfigureOverBudgetOriginals();
                Assert.IsTrue(window.GetDisplayParameterBudget().BlocksGeneration, "Replaced source schemas must refresh immediately.");
                _ctx.AvatarGo.AddComponent<ASMLiteBudgetOpaqueProcessor>();
                window.HandleParameterBudgetInputChanged(_ctx.AvatarGo);
                Assert.IsFalse(window.GetDisplayParameterBudget().Complete, "Avatar processor changes must invalidate completeness.");
            }
            finally { Object.DestroyImmediate(window); }
        }

        [UnityTest]
        public IEnumerator Window_DisplayBudget_InspectorUndoAndAvatarSwitchRefresh()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "User", VRCExpressionParameters.ValueType.Bool);
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                yield return null; // Let fixture creation/hierarchy notifications settle first.
                int initial = window.GetDisplayParameterBudget().Total;
                Undo.RecordObject(_ctx.ParamsAsset, "Edit budget source in place");
                _ctx.ParamsAsset.parameters[0].name = string.Empty;
                EditorUtility.SetDirty(_ctx.ParamsAsset);
                Undo.FlushUndoRecordObjects();
                yield return null;
                Assert.IsFalse(window.GetDisplayParameterBudget().Readable, "Native asset-property notifications must invalidate cached estimates.");
                Undo.PerformUndo();
                yield return null;
                Assert.AreEqual(initial, window.GetDisplayParameterBudget().Total);

                window.SelectAvatarForAutomation(null);
                Assert.IsFalse(window.GetDisplayParameterBudget().Readable, "Switching away must not retain a green estimate.");
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                Assert.AreEqual(initial, window.GetDisplayParameterBudget().Total);
            }
            finally { Undo.ClearUndo(_ctx.ParamsAsset); Object.DestroyImmediate(window); }
        }

        [TestCase(8191, true)]
        [TestCase(8192, true)]
        [TestCase(8193, false)]
        public void ProcessedCount_MixedTypesAndSyncFlags_CountEntriesNotBits(int count, bool allowed)
        {
            var parameters = Enumerable.Range(0, count).Select(i => new VRCExpressionParameters.Parameter
            {
                name = "Boundary_" + i, valueType = (VRCExpressionParameters.ValueType)(i % 3), networkSynced = i % 2 == 0,
            }).ToArray();
            var budget = ASMLiteBuilder.MeasureParameterBudget(parameters);
            Assert.AreEqual(count, budget.Total);
            Assert.AreEqual(allowed, budget.ToDiagnostic().Success);
            if (count == 8192) StringAssert.Contains("At the limit", budget.Message);
        }

        [Test]
        public void Projection_RepeatedBuildAndRetainedOlderSlots_MatchGeneratedSchema()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "User", VRCExpressionParameters.ValueType.Bool);
            _ctx.Comp.slotCount = 4;
            AssertProjectionMatchesBuild();
            _ctx.Comp.slotCount = 1;
            int retained = AssertProjectionMatchesBuild();
            var generated = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsTrue(generated.parameters.Any(p => p.name == "ASMLite_Bak_S4_User"), "Reducing presets must not silently prune retained slots.");
            Assert.AreEqual(retained, AssertProjectionMatchesBuild(), "Repeated rebuild must not count a second payload.");
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "User" };
            Assert.Less(AssertProjectionMatchesBuild(), retained);
            Assert.AreEqual(1, ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc,
                ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp)).OriginalCount);
        }

        [Test]
        public void PendingPreview_WithoutComponent_IsReadOnlyAndUsesProposedSettings()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "User", VRCExpressionParameters.ValueType.Float);
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            var draft = ASMLiteCustomizationDraft.CreateDefault();
            draft.SlotCount = 2;
            string before = EditorJsonUtility.ToJson(_ctx.ParamsAsset);
            var budget = ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc, draft.ToComponentSnapshot());
            Assert.AreEqual(1, budget.OriginalCount);
            Assert.AreEqual(4, budget.Contribution);
            draft.UseParameterExclusions = true;
            draft.ExcludedParameterNames = new[] { "User" };
            Assert.AreEqual(2, ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc, draft.ToComponentSnapshot()).Total);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(_ctx.ParamsAsset));
            Assert.IsNull(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(true));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UncertainPreview_AboveLimit_DoesNotDisableGeneration(bool underAsmLite)
        {
            ConfigureOverBudgetOriginals();
            (underAsmLite ? _ctx.Comp.gameObject : _ctx.AvatarGo).AddComponent<ASMLiteBudgetOpaqueProcessor>();
            var budget = ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc,
                ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp));
            Assert.Greater(budget.Total, ASMLiteParameterBudget.Limit);
            Assert.IsFalse(budget.Complete);
            Assert.IsTrue(budget.ToDiagnostic().Success);
            StringAssert.Contains("Estimate", budget.Message);
            Assert.IsTrue(ASMLiteWindow.BuildActionHierarchyContract(ASMLiteInstallationState.PackageManaged,
                true, false, budget).PrimaryDescriptors[0].IsEnabled);
        }

        [Test]
        public void UnreadableSource_DisablesGenerationInsteadOfReportingGreenZero()
        {
            _ctx.ParamsAsset.parameters = null;
            var budget = ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc,
                ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp));
            Assert.IsFalse(budget.Readable);
            Assert.IsTrue(budget.BlocksGeneration);
            StringAssert.Contains("Calculating", budget.Message);
        }

        [Test]
        public void LifecycleRebuild_OverBudget_RejectsBeforePreparation()
        {
            ConfigureOverBudgetOriginals();
            string before = EditorJsonUtility.ToJson(_ctx.ParamsAsset);
            LogAssert.Expect(LogType.Error, new Regex("BUILD-304"));
            Assert.IsFalse(ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc).Completed);
            Assert.AreEqual(before, EditorJsonUtility.ToJson(_ctx.ParamsAsset));
        }

        private int AssertProjectionMatchesBuild()
        {
            var budget = ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc,
                ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp));
            Assert.IsTrue(budget.Readable, budget.Message);
            Assert.IsTrue(ASMLiteBuilder.TryBuildWithDiagnostics(_ctx.Comp, out _).Success);
            var generated = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.AreEqual(generated.parameters.Length, budget.Contribution);
            Assert.AreEqual(_ctx.ParamsAsset.parameters.Length + generated.parameters.Length, budget.Total);
            return budget.Total;
        }

        [Test]
        public void VendorizedProjection_ExclusionsMatchRegeneratedMirror()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "User", VRCExpressionParameters.ValueType.Bool);
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.VendorizeForAutomation();
                Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
                _ctx.Comp.useParameterExclusions = true;
                _ctx.Comp.excludedParameterNames = new[] { "User" };
                var budget = window.GetParameterBudget();
                window.RebuildForAutomation();
                var mirror = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(_ctx.Comp.vendorizedGeneratedAssetsPath + "/ASMLite_Params.asset");
                Assert.IsNotNull(mirror);
                Assert.AreEqual(1, budget.OriginalCount);
                Assert.AreEqual(mirror.parameters.Length, budget.Contribution);
                Assert.AreEqual(2, budget.Total);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void Projection_RetainsUnmatchedLegacyBackupButRejectsTypeConflicts()
        {
            var payload = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            payload.parameters = new[] { new VRCExpressionParameters.Parameter { name = "ASMLite_Bak_S9_Old", networkSynced = false } };
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "User", VRCExpressionParameters.ValueType.Int);
            var budget = ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc,
                ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp));
            Assert.AreEqual(1 + _ctx.Comp.slotCount + 1 + 1, budget.Contribution, "Retained legacy entry consumes capacity even when its source is absent.");
            _ctx.ParamsAsset.parameters = _ctx.ParamsAsset.parameters.Concat(new[]
            {
                new VRCExpressionParameters.Parameter { name = "User", valueType = VRCExpressionParameters.ValueType.Float },
            }).ToArray();
            var invalid = ASMLiteBuilder.CalculateParameterBudget(_ctx.AvDesc,
                ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp));
            Assert.IsTrue(invalid.BlocksGeneration);
            StringAssert.Contains("conflicting types", invalid.Message);
        }

        [Test]
        public void Window_ReducingBackups_ReenablesRebuildWithoutRemovingOriginals()
        {
            _ctx.ParamsAsset.parameters = Enumerable.Range(0, 1024).Select(i =>
                new VRCExpressionParameters.Parameter { name = "User_" + i, networkSynced = false }).ToArray();
            _ctx.Comp.slotCount = 8;
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                Assert.IsFalse(window.GetActionHierarchyContract().PrimaryDescriptors[0].IsEnabled);
                _ctx.Comp.useParameterExclusions = true;
                _ctx.Comp.excludedParameterNames = _ctx.ParamsAsset.parameters.Select(p => p.name).ToArray();
                Assert.IsTrue(window.GetActionHierarchyContract().PrimaryDescriptors[0].IsEnabled);
                Assert.AreEqual(1024, window.GetParameterBudget().OriginalCount);
            }
            finally { Object.DestroyImmediate(window); }
        }

        [Test]
        public void Window_AddAutomation_RejectsBeforePrefabCreation()
        {
            ConfigureOverBudgetOriginals();
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            var generated = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            string before = EditorJsonUtility.ToJson(generated);
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                Assert.IsFalse(window.GetActionHierarchyContract().PrimaryDescriptors[0].IsEnabled);
                LogAssert.Expect(LogType.Error, new Regex("BUILD-304"));
                window.AddPrefabForAutomation();
                Assert.IsNull(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(true));
                Assert.AreEqual(before, EditorJsonUtility.ToJson(generated));
            }
            finally { Object.DestroyImmediate(window); }
        }
    }

    public sealed class ASMLiteBudgetOpaqueProcessor : MonoBehaviour, VRC.SDKBase.IEditorOnly { }
}
