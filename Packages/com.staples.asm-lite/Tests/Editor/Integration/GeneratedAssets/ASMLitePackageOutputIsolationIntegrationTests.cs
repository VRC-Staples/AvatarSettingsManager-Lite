using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    public abstract class ASMLitePackageOutputIsolationIntegrationTestBase
    {
        protected AsmLiteTestContext _ctx;
        private ASMLitePackageGeneratedOutputSnapshot _packageOutputSnapshot;

        [SetUp]
        public void SetUp()
        {
            _packageOutputSnapshot = ASMLitePackageGeneratedOutputSnapshot.Capture();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "Package output isolation fixture setup should create a test context.");
            Assert.IsNotNull(_ctx.AvDesc, "Package output isolation fixture setup should create an avatar descriptor.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                _packageOutputSnapshot?.Restore();
            }
            finally
            {
                _packageOutputSnapshot = null;
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
                _ctx = null;
            }
        }

        protected void PreparePackageManagedAvatarForVendorize(string aid)
        {
            if (_ctx.Comp != null)
                UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.AddPrefabForAutomation();
                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(_ctx.Comp,
                    $"{aid}: setup should attach a package-managed ASM-Lite component before vendorize.");

                ASMLiteTestFixtures.AddExpressionParam(_ctx, aid + "_BaselineParam", VRCExpressionParameters.ValueType.Int);
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/" + aid;
                EditorUtility.SetDirty(_ctx.Comp);
                AssetDatabase.SaveAssets();
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
                Assert.GreaterOrEqual(buildResult, 0,
                    $"{aid}: setup package-generated asset build should succeed before vendorize.");
                AssignPackageGeneratedAssetsToDescriptor(aid);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            Assert.AreEqual(ASMLiteInstallationState.PackageManaged, ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp),
                $"{aid}: setup should leave the avatar in PackageManaged state before vendorize.");
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                $"{aid}: setup should leave descriptor references on package generated assets before vendorize.");
            AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                $"{aid}: setup should leave live FullController references on package generated assets before vendorize.");
        }

        protected void MutateGeneratedInputsBeforeVendorize(string paramName)
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, paramName, VRCExpressionParameters.ValueType.Float, 0.5f);
            _ctx.Comp.slotCount = Math.Max(_ctx.Comp.slotCount + 1, 2);
            EditorUtility.SetDirty(_ctx.Comp);
            AssetDatabase.SaveAssets();
        }

        protected void AssignPackageGeneratedAssetsToDescriptor(string aid)
        {
            var expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            var expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            var fxController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(expressionParameters,
                aid + ": setup should load package-generated expression parameters.");
            Assert.IsNotNull(expressionsMenu,
                aid + ": setup should load package-generated expressions menu.");
            Assert.IsNotNull(fxController,
                aid + ": setup should load package-generated FX controller.");

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                aid + ": setup should find the FX layer before assigning package-generated outputs.");
            var fxLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            fxLayer.animatorController = fxController;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = fxLayer;
            _ctx.AvDesc.expressionParameters = expressionParameters;
            _ctx.AvDesc.expressionsMenu = expressionsMenu;
            EditorUtility.SetDirty(_ctx.AvDesc);
            AssetDatabase.SaveAssets();
        }

        protected void AddLegacyGeneratedRemnantsToPackageManagedGeneratedOutputs(string aid)
        {
            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                aid + ": setup should find the package-managed FX layer before seeding legacy generated remnants.");
            var ctrl = _ctx.AvDesc.baseAnimationLayers[fxIndex].animatorController as AnimatorController;
            Assert.IsNotNull(ctrl,
                aid + ": setup should resolve the package-managed FX controller before seeding legacy generated remnants.");

            ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = "ASMLite_LegacyPrePrepLayer_" + aid,
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine { name = "ASMLite_LegacyPrePrepStateMachine_" + aid }
            });
            ctrl.AddParameter("ASMLite_LegacyPrePrepParam_" + aid, AnimatorControllerParameterType.Float);
            EditorUtility.SetDirty(ctrl);

            var expressionParameters = _ctx.AvDesc.expressionParameters;
            Assert.IsNotNull(expressionParameters,
                aid + ": setup should resolve package-managed expression parameters before seeding legacy generated remnants.");
            var existingParameters = expressionParameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>();
            var updatedParameters = new VRCExpressionParameters.Parameter[existingParameters.Length + 1];
            existingParameters.CopyTo(updatedParameters, 0);
            updatedParameters[existingParameters.Length] = new VRCExpressionParameters.Parameter
            {
                name = "ASMLite_LegacyPrePrepExpr_" + aid,
                valueType = VRCExpressionParameters.ValueType.Float,
                defaultValue = 0.25f,
                saved = true,
                networkSynced = true,
            };
            expressionParameters.parameters = updatedParameters;
            EditorUtility.SetDirty(expressionParameters);

            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu,
                aid + ": setup should resolve the package-managed expressions menu before seeding legacy generated remnants.");
            _ctx.AvDesc.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = ASMLiteBuilder.DefaultRootControlName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            });
            EditorUtility.SetDirty(_ctx.AvDesc.expressionsMenu);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        protected static int FindFxLayerIndex(VRCAvatarDescriptor avatar)
        {
            if (avatar == null || avatar.baseAnimationLayers == null)
                return -1;

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                if (avatar.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    return i;
            }

            return -1;
        }

        protected void AssertDescriptorGeneratedAssetReferencesUnderPrefix(string expectedPrefix, string assertionMessage)
        {
            string normalizedPrefix = NormalizeAssetPath(expectedPrefix);
            Assert.IsNotNull(_ctx.AvDesc.expressionParameters,
                assertionMessage + " Expected expression parameters to be assigned.");
            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu,
                assertionMessage + " Expected expressions menu to be assigned.");

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                assertionMessage + " Expected an FX layer on the avatar descriptor.");
            Assert.IsNotNull(_ctx.AvDesc.baseAnimationLayers[fxIndex].animatorController,
                assertionMessage + " Expected an FX controller to be assigned.");

            AssertPathStartsWith(AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters), normalizedPrefix,
                assertionMessage + " Expression parameters should point at the expected generated-assets prefix.");
            AssertPathStartsWith(AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu), normalizedPrefix,
                assertionMessage + " Expressions menu should point at the expected generated-assets prefix.");
            AssertPathStartsWith(AssetDatabase.GetAssetPath(_ctx.AvDesc.baseAnimationLayers[fxIndex].animatorController), normalizedPrefix,
                assertionMessage + " FX controller should point at the expected generated-assets prefix.");
        }

        protected void AssertLiveFullControllerReferencesUnderPrefix(string expectedPrefix, string assertionMessage)
        {
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp != null ? _ctx.Comp.gameObject : null);
            Assert.IsNotNull(vf,
                assertionMessage + " Expected a live FullController component on the ASM-Lite object.");

            string normalizedPrefix = NormalizeAssetPath(expectedPrefix);
            var controllerReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath);
            var menuReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath);
            var parametersReference = ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(
                vf,
                ASMLiteDriftProbe.ParametersObjectRefPath,
                ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath);

            Assert.IsTrue(controllerReference.HasReference,
                assertionMessage + " Expected a populated FullController FX controller reference.");
            Assert.IsTrue(menuReference.HasReference,
                assertionMessage + " Expected a populated FullController menu reference.");
            Assert.IsTrue(parametersReference.HasReference,
                assertionMessage + " Expected a populated FullController parameter reference.");
            AssertPathStartsWith(controllerReference.AssetPath, normalizedPrefix,
                assertionMessage + " FullController FX controller should point at the expected generated-assets prefix.");
            AssertPathStartsWith(menuReference.AssetPath, normalizedPrefix,
                assertionMessage + " FullController menu should point at the expected generated-assets prefix.");
            AssertPathStartsWith(parametersReference.AssetPath, normalizedPrefix,
                assertionMessage + " FullController parameters should point at the expected generated-assets prefix.");
        }

        protected static void AssertPathStartsWith(string assetPath, string expectedPrefix, string assertionMessage)
        {
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.PathStartsWith(NormalizeAssetPath(assetPath), expectedPrefix),
                assertionMessage + $" Expected prefix '{expectedPrefix}', actual path '{assetPath}'.");
        }

        protected static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        }

        protected sealed class PackageOutputBytesSnapshot
        {
            private readonly Dictionary<string, byte[]> _files;

            private PackageOutputBytesSnapshot(Dictionary<string, byte[]> files)
            {
                _files = files;
            }

            internal static PackageOutputBytesSnapshot Capture()
            {
                var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                CaptureAssetPath(ASMLiteAssetPaths.GeneratedDir, files);
                CaptureAssetPath(ASMLiteAssetPaths.GeneratedDir + ".meta", files);
                CaptureAssetPath(ASMLiteAssetPaths.Prefab, files);
                CaptureAssetPath(ASMLiteAssetPaths.Prefab + ".meta", files);
                Assert.Greater(files.Count, 0,
                    "PackageOutputBytesSnapshot: expected protected package outputs to exist before capturing baseline bytes.");
                return new PackageOutputBytesSnapshot(files);
            }

            internal void AssertMatches(string aid)
            {
                var actual = Capture()._files;
                CollectionAssert.AreEquivalent(_files.Keys, actual.Keys,
                    $"{aid}: protected package output file set should match the captured baseline after vendorize.");

                foreach (var pair in _files.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    CollectionAssert.AreEqual(pair.Value, actual[pair.Key],
                        $"{aid}: protected package output bytes should match baseline for '{pair.Key}'.");
                }
            }

            private static void CaptureAssetPath(string assetPath, Dictionary<string, byte[]> files)
            {
                string fullPath = ToFullPath(assetPath);
                if (Directory.Exists(fullPath))
                {
                    foreach (string filePath in Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories))
                        files[ToAssetKey(filePath)] = File.ReadAllBytes(filePath);
                    return;
                }

                if (File.Exists(fullPath))
                    files[ToAssetKey(fullPath)] = File.ReadAllBytes(fullPath);
            }

            private static string ToAssetKey(string fullPath)
            {
                return Path.GetFullPath(fullPath).Replace('\\', '/');
            }

            private static string ToFullPath(string assetPath)
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
                if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                {
                    string packagePrefix = $"Packages/{packageInfo.name}/";
                    if (assetPath.StartsWith(packagePrefix, StringComparison.Ordinal))
                    {
                        string relativePath = assetPath.Substring(packagePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
                        return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, relativePath));
                    }
                }

                return Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));
            }
        }
    }

    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteAttachedVendorizePackageOutputIsolationIntegrationTests : ASMLitePackageOutputIsolationIntegrationTestBase
    {
        [Test]
        public void AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget()
        {
            PreparePackageManagedAvatarForVendorize("AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget");
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget_AddedParam");

            var result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);

            Assert.IsTrue(result.Success,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: vendorize should succeed for a valid package-managed avatar.");
            Assert.IsNotNull(result.MirrorResult,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: vendorize should report the avatar-local generated-assets mirror.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(result.MirrorResult.TargetPath),
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: vendorized mirror folder should exist after successful vendorize.");
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(result.MirrorResult.TargetPath,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: descriptor references should remain on avatar-local generated assets after package outputs are restored.");
            AssertLiveFullControllerReferencesUnderPrefix(result.MirrorResult.TargetPath,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: live FullController references should remain on avatar-local generated assets after package outputs are restored.");
            baseline.AssertMatches("AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget");
        }

        [Test]
        public void AttachedVendorize_RestoreFailure_ReturnsFailure()
        {
            PreparePackageManagedAvatarForVendorize("AttachedVendorize_RestoreFailure_ReturnsFailure");
            MutateGeneratedInputsBeforeVendorize("AttachedVendorize_RestoreFailure_ReturnsFailure_AddedParam");

            ASMLiteLifecycleTransactionResult result;
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestoreFailureForTesting("Injected package-output restore failure for attached vendorize isolation coverage."))
                result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Success,
                "AttachedVendorize_RestoreFailure_ReturnsFailure: vendorize should fail closed when package-output restore fails.");
            Assert.AreEqual(ASMLiteLifecycleTransactionStage.Execute, result.FailedStage,
                "AttachedVendorize_RestoreFailure_ReturnsFailure: restore failure should surface as an execute-stage lifecycle failure.");
            StringAssert.Contains("package generated output", result.Message.ToLowerInvariant(),
                "AttachedVendorize_RestoreFailure_ReturnsFailure: failure message should identify package generated outputs.");
            StringAssert.Contains(ASMLiteAssetPaths.GeneratedDir, result.ContextPath,
                "AttachedVendorize_RestoreFailure_ReturnsFailure: failure context should name the generated-assets package path.");
            StringAssert.Contains(ASMLiteAssetPaths.Prefab, result.ContextPath,
                "AttachedVendorize_RestoreFailure_ReturnsFailure: failure context should name the package prefab path.");
        }

        [Test]
        public void AttachedVendorize_Rollback_RestoresPackageOutputs()
        {
            PreparePackageManagedAvatarForVendorize("AttachedVendorize_Rollback_RestoresPackageOutputs");
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("AttachedVendorize_Rollback_RestoresPackageOutputs_AddedParam");

            using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterLiveFullControllerRetarget))
            {
                var result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);
                Assert.IsFalse(result.Success,
                    "AttachedVendorize_Rollback_RestoresPackageOutputs: vendorize should fail closed when failure injection triggers after live retarget.");
                Assert.IsTrue(result.RollbackAttempted,
                    "AttachedVendorize_Rollback_RestoresPackageOutputs: vendorize should attempt rollback after live retarget failure.");
                Assert.IsTrue(result.RollbackSucceeded,
                    "AttachedVendorize_Rollback_RestoresPackageOutputs: vendorize rollback should restore package-managed references after live retarget failure.");
            }

            baseline.AssertMatches("AttachedVendorize_Rollback_RestoresPackageOutputs");
        }
    }

    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLitePackageManagedRebuildPackageOutputIsolationIntegrationTests : ASMLitePackageOutputIsolationIntegrationTestBase
    {
        [Test]
        public void PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs()
        {
            PreparePackageManagedAvatarForVendorize("PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs");
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs_AddedParam");

            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            Assert.IsNotNull(_ctx.Comp,
                "PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: rebuild should keep the editable ASM-Lite component attached.");
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets,
                "PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: package-managed direct rebuild should switch the component to avatar-local generated assets after package outputs are restored.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(_ctx.Comp.vendorizedGeneratedAssetsPath),
                "PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: rebuild should leave an avatar-local generated-assets mirror.");
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(_ctx.Comp.vendorizedGeneratedAssetsPath,
                "PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: descriptor references should point at the avatar-local generated-assets mirror.");
            AssertLiveFullControllerReferencesUnderPrefix(_ctx.Comp.vendorizedGeneratedAssetsPath,
                "PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: live FullController references should point at the avatar-local generated-assets mirror.");
            baseline.AssertMatches("PackageManagedRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs");
        }

        [Test]
        public void PackageManagedRebuild_LegacyGeneratedRemnants_RestoresPrePrepPackageOutputs()
        {
            const string aid = "PackageManagedRebuild_LegacyGeneratedRemnants_RestoresPrePrepPackageOutputs";
            PreparePackageManagedAvatarForVendorize(aid);
            AddLegacyGeneratedRemnantsToPackageManagedGeneratedOutputs(aid);
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");

            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            Assert.IsNotNull(_ctx.Comp,
                aid + ": rebuild should keep the editable ASM-Lite component attached.");
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets,
                aid + ": package-managed direct rebuild should switch the component to avatar-local generated assets after package outputs are restored.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(_ctx.Comp.vendorizedGeneratedAssetsPath),
                aid + ": rebuild should leave an avatar-local generated-assets mirror.");
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(_ctx.Comp.vendorizedGeneratedAssetsPath,
                aid + ": descriptor references should point at the avatar-local generated-assets mirror.");
            AssertLiveFullControllerReferencesUnderPrefix(_ctx.Comp.vendorizedGeneratedAssetsPath,
                aid + ": live FullController references should point at the avatar-local generated-assets mirror.");
            baseline.AssertMatches(aid);
        }

        [Test]
        public void PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs()
        {
            PreparePackageManagedAvatarForVendorize("PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs");
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs_AddedParam");
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ASM-Lite\] Injected staged-copy failure before vendorized mirror promotion\..*$"));

            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.AfterStagedCopy))
                    window.RebuildForAutomation();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            Assert.IsNotNull(_ctx.Comp,
                "PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs: failed rebuild should keep the editable component attached.");
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets,
                "PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs: failed mirror staging should not switch the component to vendorized generated-assets mode.");
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                "PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs: failed rebuild should leave descriptor references on package-managed generated assets.");
            baseline.AssertMatches("PackageManagedRebuild_MirrorFailure_RestoresPackageOutputs");
        }

        [Test]
        public void PackageManagedRebuild_InvalidDescriptor_WarnsAndRestoresPackageOutputs()
        {
            PreparePackageManagedAvatarForVendorize("PackageManagedRebuild_InvalidDescriptor_WarnsAndRestoresPackageOutputs");
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("PackageManagedRebuild_InvalidDescriptor_WarnsAndRestoresPackageOutputs_AddedParam");
            _ctx.AvDesc.baseAnimationLayers = null;
            EditorUtility.SetDirty(_ctx.AvDesc);
            AssetDatabase.SaveAssets();
            LogAssert.Expect(LogType.Warning, new Regex(@"^\[ASM-Lite\] Package-managed rebuild aborted because avatar descriptor generated-asset references could not be safely retargeted\..*$"));

            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            Assert.IsNotNull(_ctx.Comp,
                "PackageManagedRebuild_InvalidDescriptor_WarnsAndRestoresPackageOutputs: invalid descriptor rebuild should keep the editable component attached.");
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets,
                "PackageManagedRebuild_InvalidDescriptor_WarnsAndRestoresPackageOutputs: invalid descriptor rebuild should not switch generated-assets mode.");
            baseline.AssertMatches("PackageManagedRebuild_InvalidDescriptor_WarnsAndRestoresPackageOutputs");
        }
    }

    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteVendorizedRebuildPackageOutputIsolationIntegrationTests : ASMLitePackageOutputIsolationIntegrationTestBase
    {
        [Test]
        public void VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs()
        {
            PreparePackageManagedAvatarForVendorize("VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs");

            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.VendorizeForAutomation();
                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(_ctx.Comp,
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: vendorize setup should keep the editable component attached.");
                string vendorizedGeneratedAssetsPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
                Assert.IsTrue(AssetDatabase.IsValidFolder(vendorizedGeneratedAssetsPath),
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: vendorize setup should create the avatar-local generated-assets mirror.");

                var baseline = PackageOutputBytesSnapshot.Capture();
                MutateGeneratedInputsBeforeVendorize("VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs_AddedParam");

                window.RebuildForAutomation();

                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(_ctx.Comp,
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: rebuild should keep the editable component attached.");
                Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets,
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: rebuild should preserve vendorized generated-assets mode.");
                Assert.AreEqual(vendorizedGeneratedAssetsPath, _ctx.Comp.vendorizedGeneratedAssetsPath,
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: rebuild should preserve the canonical avatar-local generated-assets path.");
                AssertDescriptorGeneratedAssetReferencesUnderPrefix(vendorizedGeneratedAssetsPath,
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: descriptor references should stay on avatar-local generated assets.");
                AssertLiveFullControllerReferencesUnderPrefix(vendorizedGeneratedAssetsPath,
                    "VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs: live FullController references should stay on avatar-local generated assets.");
                baseline.AssertMatches("VendorizedRebuild_RefreshesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }
    }

}
