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
            AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp),
                $"{aid}: setup should leave live FullController references on the avatar-owned working graph before vendorize.");
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

            internal static string ToFullPath(string assetPath)
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
        [TestCase(1)]
        [TestCase(8)]
        public void Build_TwoAvatars_KeepIndependentGraphsAndPackageBytes(int slotCount)
        {
            var packageBaseline = PackageOutputBytesSnapshot.Capture();
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "FirstAvatarValue", VRCExpressionParameters.ValueType.Float, 0.25f);
            _ctx.Comp.slotCount = slotCount;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "First avatar presets";
            Assert.GreaterOrEqual(ASMLiteBuilder.Build(_ctx.Comp), 0, ASMLiteBuilder.GetLatestBuildDiagnosticResult().ToLogString());
            string firstDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.IsOwnedWorkingFolder(_ctx.AvDesc, firstDir));
            AssertLiveFullControllerReferencesUnderPrefix(firstDir, "First avatar owns its generated graph.");
            var firstBytes = Directory.GetFiles(PackageOutputBytesSnapshot.ToFullPath(firstDir))
                .ToDictionary(path => path, File.ReadAllBytes);

            var second = UnityEngine.Object.Instantiate(_ctx.AvatarGo);
            second.name = "SecondFixtureAvatar";
            try
            {
                var secondDescriptor = second.GetComponent<VRCAvatarDescriptor>();
                var secondComponent = second.GetComponentInChildren<ASMLiteComponent>(true);
                secondComponent.slotCount = slotCount == 1 ? 8 : 1;
                secondComponent.customRootName = "Second avatar presets";
                Assert.GreaterOrEqual(ASMLiteBuilder.Build(secondComponent), 0, ASMLiteBuilder.GetLatestBuildDiagnosticResult().ToLogString());
                string secondDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(secondComponent);
                Assert.AreNotEqual(firstDir, secondDir);
                Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.IsOwnedWorkingFolder(secondDescriptor, secondDir));
                var secondParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(secondDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
                Assert.IsTrue(secondParameters.parameters.Any(parameter => parameter.name == "ASMLite_Bak_S" + secondComponent.slotCount + "_FirstAvatarValue"));
                var secondMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(secondDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
                Assert.AreEqual("Second avatar presets", secondMenu.controls[0].name);
                foreach (var file in firstBytes)
                    CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), file.Key);
                AssertLiveFullControllerReferencesUnderPrefix(firstDir, "Building a second avatar must not change the first avatar's wiring.");
                packageBaseline.AssertMatches(nameof(Build_TwoAvatars_KeepIndependentGraphsAndPackageBytes));
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(second);
            }
        }

        [Test]
        public void Build_StagingFailure_PreservesPreviousAvatarGraphAndPackageBytes()
        {
            Assert.GreaterOrEqual(ASMLiteBuilder.Build(_ctx.Comp), 0);
            string generatedDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            var files = Directory.GetFiles(PackageOutputBytesSnapshot.ToFullPath(generatedDir)).ToDictionary(path => path, File.ReadAllBytes);
            var packageBaseline = PackageOutputBytesSnapshot.Capture();
            ASMLiteBuildDiagnosticResult result;
            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.AfterStagedCopy))
                result = ASMLiteBuilder.TryBuildWithDiagnostics(_ctx.Comp, out _);
            Assert.IsFalse(result.Success);
            foreach (var file in files)
                CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), file.Key);
            AssertLiveFullControllerReferencesUnderPrefix(generatedDir, "Failed staging must preserve prior live references.");
            packageBaseline.AssertMatches(nameof(Build_StagingFailure_PreservesPreviousAvatarGraphAndPackageBytes));
        }

        [Test]
        public void FixtureSnapshot_PreservesModifiedOutputAndPreExistingUntrackedFile()
        {
            string output = PackageOutputBytesSnapshot.ToFullPath(ASMLiteAssetPaths.FXController);
            string extra = Path.Combine(Path.GetDirectoryName(output), "UserBaseline-" + Guid.NewGuid().ToString("N") + ".txt");
            File.AppendAllText(output, "\n# pre-existing local output edit\n");
            File.WriteAllText(extra, "pre-existing untracked work");
            AssetDatabase.Refresh();
            byte[] outputBytes = File.ReadAllBytes(output);
            byte[] extraBytes = File.ReadAllBytes(extra);
            var baseline = ASMLitePackageGeneratedOutputSnapshot.Capture();

            File.AppendAllText(output, "\n# fixture mutation\n");
            File.WriteAllText(extra, "fixture mutation");
            baseline.Restore();

            CollectionAssert.AreEqual(outputBytes, File.ReadAllBytes(output));
            CollectionAssert.AreEqual(extraBytes, File.ReadAllBytes(extra));
        }

        [Test]
        public void VendorizeAndDetach_ChildFailureAndOuterRestoreFailure_ReturnsRecoveryDiagnostic()
        {
            PreparePackageManagedAvatarForVendorize("NestedRestoreFailure");
            int restoresPrepared = 0;
            ASMLiteLifecycleTransactionResult result;
            using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterLiveFullControllerRetarget))
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
            {
                if (phase == "prepared" && ++restoresPrepared == 3)
                    throw new IOException("Injected outer restore failure.");
            }))
                result = ASMLiteLifecycleTransactionService.ExecuteVendorizeAndDetach(_ctx.Comp, _ctx.AvDesc);

            Assert.AreEqual(3, restoresPrepared, "The child restores package bytes before and after working-graph rollback; the third restore is the outer operation.");
            Assert.IsFalse(result.Success);
            Assert.AreEqual(ASMLiteLifecycleOperation.VendorizeAndDetach, result.Operation);
            Assert.IsFalse(result.RollbackSucceeded);
            Assert.AreEqual("ASMLITE_PACKAGE_OUTPUT_RESTORE_FAILED", result.Diagnostic.Code);
            Assert.IsNotNull(result.Diagnostic.InnerDiagnostic, "Preserve the failed child diagnostic.");
            Assert.IsFalse(result.Diagnostic.InnerDiagnostic.Success);
            StringAssert.Contains("Injected attached-vendorize failure after live FullController", result.Diagnostic.InnerDiagnostic.Message);
            StringAssert.Contains("Injected outer restore failure", result.Remediation);
            Assert.IsNotNull(_ctx.Comp);
        }

        [Test]
        public void DirectDeliveryVerification_MissingGeneratedSubmenu_RejectsPartialDelivery()
        {
            PreparePackageManagedAvatarForVendorize(nameof(DirectDeliveryVerification_MissingGeneratedSubmenu_RejectsPartialDelivery));
            var retarget = ASMLiteGeneratedAssetMirrorService.RetargetAvatarGeneratedAssetsToVendorized(_ctx.AvDesc,
                ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp));
            Assert.IsTrue(retarget.Success, retarget.ToLogString());
            Assert.IsTrue(ASMLiteLifecycleVerification.VerifyDirectDeliveryState(
                _ctx.Comp, _ctx.AvDesc, ASMLiteInstallationState.Detached, string.Empty,
                out string failureMessage, out string failureContext), failureMessage);

            _ctx.AvDesc.expressionsMenu.controls[0].subMenu = null;

            Assert.IsFalse(ASMLiteLifecycleVerification.VerifyDirectDeliveryState(
                _ctx.Comp, _ctx.AvDesc, ASMLiteInstallationState.Detached, string.Empty,
                out failureMessage, out failureContext));
            StringAssert.Contains("menu: False", failureMessage);
            Assert.AreEqual(_ctx.AvatarGo.name, failureContext);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void Detach_IncompleteMenu_FailsBeforeMutation(bool vendorize, bool missingMenu)
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "Inventory";
            if (missingMenu)
                _ctx.AvDesc.expressionsMenu = null;
            else
                for (int i = 0; i < 8; i++)
                    _ctx.MenuAsset.controls.Add(new VRCExpressionsMenu.Control
                    {
                        name = i == 0 ? "Inventory" : i == 1 ? ASMLiteBuilder.DefaultRootControlName : "User " + i,
                        type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = _ctx.MenuAsset,
                    });
            EditorUtility.SetDirty(_ctx.MenuAsset);
            AssetDatabase.SaveAssets();
            var packageBaseline = PackageOutputBytesSnapshot.Capture();
            string menuBefore = EditorJsonUtility.ToJson(_ctx.MenuAsset);
            string parametersBefore = EditorJsonUtility.ToJson(_ctx.ParamsAsset);
            string controllerBefore = EditorJsonUtility.ToJson(_ctx.Ctrl);

            var result = vendorize
                ? ASMLiteLifecycleTransactionService.ExecuteVendorizeAndDetach(_ctx.Comp, _ctx.AvDesc)
                : ASMLiteLifecycleTransactionService.ExecuteDetachToDirectDelivery(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ASMLiteLifecycleTransactionStage.Preflight, result.FailedStage);
            Assert.IsFalse(result.RollbackAttempted, "Reject the operation before touching any assets.");
            Assert.IsNotNull(_ctx.Comp);
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(menuBefore, EditorJsonUtility.ToJson(_ctx.MenuAsset));
            Assert.AreEqual(parametersBefore, EditorJsonUtility.ToJson(_ctx.ParamsAsset));
            Assert.AreEqual(controllerBefore, EditorJsonUtility.ToJson(_ctx.Ctrl));
            packageBaseline.AssertMatches(nameof(Detach_IncompleteMenu_FailsBeforeMutation));
        }

        [Test]
        public void AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget()
        {
            PreparePackageManagedAvatarForVendorize("AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget");
            string workingDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            string oldWorkingGuid = AssetDatabase.AssetPathToGUID(workingDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            var priorMirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(_ctx.AvDesc, workingDir);
            Assert.IsTrue(priorMirror.Success, priorMirror.ToLogString());
            Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(priorMirror).Success);
            string oldMirrorGuid = AssetDatabase.AssetPathToGUID(priorMirror.TargetPath + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget_AddedParam");

            int restoresPrepared = 0;
            int replacementsDeleted = 0;
            bool backupsObserved = false;
            ASMLiteLifecycleTransactionResult result;
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
            {
                if (phase == "prepared")
                {
                    if (++restoresPrepared != 1
                        || !AssetDatabase.GUIDToAssetPath(oldMirrorGuid).Contains("GeneratedAssets__backup__")
                        || !AssetDatabase.GUIDToAssetPath(oldWorkingGuid).Contains("GeneratedAssets__backup__"))
                        throw new IOException("Package restoration attempted twice or after graph backup retirement.");
                    backupsObserved = true;
                }
                if (phase == "replacement-deleted")
                    replacementsDeleted++;
            }))
                result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);

            Assert.IsTrue(result.Success,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: vendorize should succeed for a valid package-managed avatar.");
            Assert.AreEqual(1, restoresPrepared, "Successful attached vendorize must have one package restoration owner.");
            Assert.AreEqual(1, replacementsDeleted, "The real snapshot must replace package roots only once on success.");
            Assert.IsTrue(backupsObserved, "Keep both original graph identities recoverable through restoration.");
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(priorMirror.TargetPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            Assert.AreEqual(ASMLiteInstallationState.Vendorized, result.AfterState);
            Assert.IsFalse(result.RollbackAttempted);
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0);
            // Default GUID lookups include recently deleted assets; require a live GUID round trip.
            Assert.AreNotEqual(oldMirrorGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GUIDToAssetPath(oldMirrorGuid), AssetPathToGUIDOptions.OnlyExistingAssets), "Retire the prior mirror only after commit.");
            Assert.AreNotEqual(oldWorkingGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GUIDToAssetPath(oldWorkingGuid), AssetPathToGUIDOptions.OnlyExistingAssets), "Retire the prior working graph only after commit.");
            Assert.IsNotNull(result.MirrorResult,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: vendorize should report the avatar-local generated-assets mirror.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(result.MirrorResult.TargetPath),
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: vendorized mirror folder should exist after successful vendorize.");
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(result.MirrorResult.TargetPath,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: descriptor references should remain on avatar-local generated assets after package outputs are restored.");
            AssertLiveFullControllerReferencesUnderPrefix(result.MirrorResult.TargetPath,
                "AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget: live FullController references should remain on avatar-local generated assets after package outputs are restored.");
            var ownership = ASMLiteGeneratedAssetMirrorService.ValidateVendorizedFolderForDelete(_ctx.AvDesc, result.MirrorResult.TargetPath);
            Assert.IsTrue(ownership.Success, ownership.Message);
            baseline.AssertMatches("AttachedVendorize_RestoresPackageGeneratedOutputs_AfterRetarget");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AttachedVendorize_PackageRestoreFailureBeforeCommit_RecoversOrIdentifiesBothBackups(bool refuseRollback)
        {
            const string aid = "AttachedVendorize_PreCommitRestoreFailure";
            PreparePackageManagedAvatarForVendorize(aid);
            string workingDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            var priorMirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(_ctx.AvDesc, workingDir);
            Assert.IsTrue(priorMirror.Success, priorMirror.ToLogString());
            Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(priorMirror).Success);
            string mirrorDir = priorMirror.TargetPath;
            var graphFiles = new[] { workingDir, mirrorDir }
                .SelectMany(dir => Directory.GetFiles(Path.GetFullPath(dir), "*", SearchOption.AllDirectories)
                    .Append(Path.GetFullPath(dir + ".meta")))
                .ToDictionary(path => path, File.ReadAllBytes);
            var graphGuids = new[] { workingDir, mirrorDir }
                .SelectMany(dir => AssetDatabase.FindAssets(string.Empty, new[] { dir }))
                .ToDictionary(AssetDatabase.GUIDToAssetPath, guid => guid);
            string oldMirrorGuid = AssetDatabase.AssetPathToGUID(mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            string oldWorkingGuid = AssetDatabase.AssetPathToGUID(workingDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
            var oldMenu = _ctx.AvDesc.expressionsMenu;
            var oldParameters = _ctx.AvDesc.expressionParameters;
            var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf);
            Func<UnityEngine.Object[]> readLiveReferences = () => new[]
            {
                ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath).Reference,
                ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath).Reference,
                ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(vf,
                    ASMLiteDriftProbe.ParametersObjectRefPath, ASMLiteDriftProbe.ParameterObjectRefPath,
                    ASMLiteDriftProbe.ParameterLegacyObjectRefPath).Reference,
            };
            var liveBefore = readLiveReferences();
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");
            bool injected = false;
            bool selectedGraphObserved = false;
            string mirrorBackup = null;
            string workingBackup = null;
            IDisposable rollbackFailure = null;
            ASMLiteLifecycleTransactionResult result;
            try
            {
                using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
                {
                    if (phase != "replacement-deleted" || injected)
                        return;
                    injected = true;
                    selectedGraphObserved = _ctx.Comp.useVendorizedGeneratedAssets
                        && _ctx.Comp.vendorizedGeneratedAssetsPath == mirrorDir
                        && AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu) == mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu)
                        && readLiveReferences().All(reference => AssetDatabase.GetAssetPath(reference).StartsWith(mirrorDir + "/", StringComparison.Ordinal));
                    mirrorBackup = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(oldMirrorGuid)).Replace('\\', '/');
                    workingBackup = Path.GetDirectoryName(AssetDatabase.GUIDToAssetPath(oldWorkingGuid)).Replace('\\', '/');
                    if (refuseRollback)
                        rollbackFailure = ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringMirrorRollback);
                    throw new IOException("Injected attached-vendorize pre-commit package restore failure.");
                }))
                    result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);
            }
            finally
            {
                rollbackFailure?.Dispose();
            }

            Assert.IsTrue(injected);
            Assert.IsTrue(selectedGraphObserved, "Restoration must run after selecting the replacement descriptor/live/component graph.");
            StringAssert.Contains("GeneratedAssets__backup__", mirrorBackup);
            StringAssert.Contains("GeneratedAssets__backup__", workingBackup);
            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RollbackAttempted);
            Assert.AreEqual(!refuseRollback, result.RollbackSucceeded, result.ToLogString());
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0);
            Assert.AreEqual("ASMLITE_PACKAGE_OUTPUT_RESTORE_FAILED", result.Diagnostic.Code, "Preserve the typed package failure even if graph rollback also fails.");
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            foreach (var asset in graphGuids)
            {
                string actualPath = AssetDatabase.GUIDToAssetPath(asset.Value);
                string originalDir = ASMLiteGeneratedOwnershipPolicy.PathStartsWith(asset.Key, workingDir) ? workingDir : mirrorDir;
                string expectedDir = refuseRollback ? (originalDir == workingDir ? workingBackup : mirrorBackup) : originalDir;
                Assert.AreEqual(expectedDir + asset.Key.Substring(originalDir.Length), actualPath, "Preserve each old graph asset identity.");
            }
            foreach (var file in graphFiles)
            {
                string originalDir = file.Key.StartsWith(Path.GetFullPath(workingDir), StringComparison.Ordinal) ? workingDir : mirrorDir;
                string expectedDir = refuseRollback ? (originalDir == workingDir ? workingBackup : mirrorBackup) : originalDir;
                string restoredPath = Path.GetFullPath(expectedDir) + file.Key.Substring(Path.GetFullPath(originalDir).Length);
                CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(restoredPath), "Preserve original graph bytes and metadata: " + file.Key);
            }
            if (refuseRollback)
            {
                Assert.IsNotNull(result.Diagnostic.InnerDiagnostic, "Keep the rollback failure nested under the original package diagnostic.");
                StringAssert.Contains(mirrorBackup, result.ToLogString());
                StringAssert.Contains(workingBackup, result.ToLogString());
                Assert.IsTrue(AssetDatabase.IsValidFolder(mirrorBackup));
                Assert.IsTrue(AssetDatabase.IsValidFolder(workingBackup));
            }
            else
            {
                Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
                Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
                Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
                CollectionAssert.AreEqual(liveBefore, readLiveReferences(), "Restore actual independent FullController identities.");
            }
            var recovery = Regex.Match(result.Diagnostic.Remediation, @"Verified recovery copies retained at '([^']+)'");
            Assert.IsTrue(recovery.Success, result.ToLogString());
            string recoveryDir = recovery.Groups[1].Value;
            Assert.IsTrue(File.Exists(Path.Combine(recoveryDir, "RECOVERY.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(recoveryDir, "captured-baseline", "roots.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(recoveryDir, "pre-restore", "roots.txt")));
            baseline.AssertMatches(aid);
        }

        [Test]
        public void AttachedVendorize_CleanupFailureAfterCommit_RetainsMirrorWithoutRollbackOrSecondRestore()
        {
            const string aid = "AttachedVendorize_CommittedCleanupFailure";
            PreparePackageManagedAvatarForVendorize(aid);
            string workingDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            var priorMirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(_ctx.AvDesc, workingDir);
            Assert.IsTrue(priorMirror.Success, priorMirror.ToLogString());
            Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(priorMirror).Success);
            string mirrorDir = priorMirror.TargetPath;
            string menuPath = mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu);
            string oldMirrorGuid = AssetDatabase.AssetPathToGUID(menuPath);
            byte[] oldMirrorBytes = File.ReadAllBytes(Path.GetFullPath(menuPath));
            string oldWorkingGuid = AssetDatabase.AssetPathToGUID(workingDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");
            var logs = new List<string>();
            Application.LogCallback record = (message, stack, type) => logs.Add(message);
            IDisposable cleanupFailure = null;
            int restoresPrepared = 0;
            int replacementsDeleted = 0;
            LogAssert.Expect(LogType.Warning, new Regex(@"Attached vendorize committed; recovery backup cleanup incomplete at 'Assets/ASM-Lite/[^']+/GeneratedAssets__backup__[a-f0-9]{8}'\..*Injected failure before mirror finalization"));
            ASMLiteLifecycleTransactionResult result;
            Application.logMessageReceived += record;
            try
            {
                using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
                {
                    if (phase == "prepared")
                    {
                        if (++restoresPrepared != 1)
                            throw new IOException("Package restoration attempted again after commit/cleanup.");
                        cleanupFailure = ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize);
                    }
                    if (phase == "replacement-deleted")
                        replacementsDeleted++;
                }))
                    result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);
            }
            finally
            {
                cleanupFailure?.Dispose();
                Application.logMessageReceived -= record;
            }

            Assert.IsTrue(result.Success, result.ToLogString());
            Assert.IsFalse(result.RollbackAttempted, "Cleanup refusal must not claim rollback of a verified commit.");
            Assert.AreEqual(1, restoresPrepared);
            Assert.AreEqual(1, replacementsDeleted);
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0);
            Assert.AreEqual(ASMLiteInstallationState.Vendorized, result.AfterState);
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(mirrorDir, _ctx.Comp.vendorizedGeneratedAssetsPath);
            Assert.AreNotEqual(oldMirrorGuid, AssetDatabase.AssetPathToGUID(menuPath));
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(mirrorDir, aid);
            AssertLiveFullControllerReferencesUnderPrefix(mirrorDir, aid);
            string retainedMenu = AssetDatabase.GUIDToAssetPath(oldMirrorGuid);
            StringAssert.Contains("GeneratedAssets__backup__", retainedMenu);
            CollectionAssert.AreEqual(oldMirrorBytes, File.ReadAllBytes(Path.GetFullPath(retainedMenu)));
            string recoveryDir = Path.GetDirectoryName(retainedMenu).Replace('\\', '/');
            Assert.IsTrue(logs.Any(message => message.Contains(recoveryDir) && message.Contains("Recovery backup retained.")), "Name the actual retained delivery backup.");
            Assert.AreNotEqual(oldWorkingGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GUIDToAssetPath(oldWorkingGuid), AssetPathToGUIDOptions.OnlyExistingAssets), "Attempt independent working backup cleanup despite mirror cleanup refusal.");
            baseline.AssertMatches(aid);
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

        [TestCase(false)]
        [TestCase(true)]
        public void AttachedVendorize_FinalizeException_RestoresPackageOutputsAndPriorMirror(bool priorMirror)
        {
            const string aid = "Vendorize_FinalizeException";
            PreparePackageManagedAvatarForVendorize(aid);
            var baseline = PackageOutputBytesSnapshot.Capture();
            var oldMenu = _ctx.AvDesc.expressionsMenu;
            var oldParameters = _ctx.AvDesc.expressionParameters;
            var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
            var existing = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(_ctx.AvDesc);
            Assert.IsTrue(existing.Success, existing.Message);
            Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(existing).Success);
            string mirrorMenu = existing.TargetPath + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu);
            string guid = AssetDatabase.AssetPathToGUID(mirrorMenu);
            byte[] bytes = File.ReadAllBytes(Path.GetFullPath(mirrorMenu));
            if (!priorMirror)
                Assert.IsTrue(AssetDatabase.DeleteAsset(existing.TargetPath));
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");

            ASMLiteLifecycleTransactionResult result;
            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize))
                result = ASMLiteWindowOperations.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RollbackAttempted);
            Assert.IsTrue(result.RollbackSucceeded, result.ToLogString());
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
            Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
            AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp), aid);
            Assert.AreEqual(priorMirror, AssetDatabase.IsValidFolder(existing.TargetPath));
            if (priorMirror)
            {
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(mirrorMenu));
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.GetFullPath(mirrorMenu)));
            }
            baseline.AssertMatches(aid);
        }

        [Test]
        public void DirectDelivery_RollbackSnapshot_RestoresNestedMenuContent()
        {
            PreparePackageManagedAvatarForVendorize(nameof(DirectDelivery_RollbackSnapshot_RestoresNestedMenuContent));
            var vendorize = ASMLiteWindowOperations.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);
            Assert.IsTrue(vendorize.Success, vendorize.ToLogString());
            var nestedMenu = _ctx.AvDesc.expressionsMenu.controls[0].subMenu;
            Assert.IsNotNull(nestedMenu);
            Assert.Greater(nestedMenu.controls.Count, 0);
            string before = EditorJsonUtility.ToJson(nestedMenu);
            string path = AssetDatabase.GetAssetPath(nestedMenu);
            string guid = AssetDatabase.AssetPathToGUID(path);
            using (var snapshot = ASMLiteDirectDeliveryRollbackSnapshot.Capture(_ctx.AvDesc))
            {
                nestedMenu.controls.Clear();
                EditorUtility.SetDirty(nestedMenu);
                AssetDatabase.SaveAssetIfDirty(nestedMenu);
                Assert.IsTrue(snapshot.TryRestore(_ctx.AvDesc, out string context, out string remediation), context + ": " + remediation);
            }
            Assert.AreSame(nestedMenu, _ctx.AvDesc.expressionsMenu.controls[0].subMenu);
            Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(path));
            Assert.AreEqual(before, EditorJsonUtility.ToJson(nestedMenu), "Restoring only the root menu leaves mutated submenu payloads behind.");
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void DirectDelivery_Exception_RestoresAttachedAssetsAndPackageOutputs(bool vendorizeAndDetach, bool alreadyVendorized)
        {
            const string aid = "DirectDelivery_Exception";
            PreparePackageManagedAvatarForVendorize(aid);
            if (alreadyVendorized)
                Assert.IsTrue(ASMLiteWindowOperations.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc).Success);
            var baseline = PackageOutputBytesSnapshot.Capture();
            string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
            string expectedDir = alreadyVendorized ? previousPath : ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            var oldMenu = _ctx.AvDesc.expressionsMenu;
            var oldParameters = _ctx.AvDesc.expressionParameters;
            var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
            string parametersBefore = EditorJsonUtility.ToJson(oldParameters);
            string menuDirectory = Path.GetDirectoryName(AssetDatabase.GetAssetPath(oldMenu)).Replace('\\', '/');
            var menusBefore = AssetDatabase.FindAssets("t:VRCExpressionsMenu", new[] { menuDirectory })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToDictionary(path => path, path => new
                {
                    Guid = AssetDatabase.AssetPathToGUID(path),
                    Json = EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(path)),
                });
            var controllerBefore = (AnimatorController)oldFx;
            var fxBefore = controllerBefore.parameters.Select(p => (p.name, p.type, p.defaultBool, p.defaultFloat, p.defaultInt)).ToArray();
            var layerNamesBefore = controllerBefore.layers.Select(layer => layer.name).ToArray();
            var controllerHideFlags = controllerBefore.hideFlags;

            ASMLiteLifecycleTransactionResult result;
            using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.ThrowAfterDetachExecute))
                result = vendorizeAndDetach
                    ? ASMLiteWindowOperations.ExecuteVendorizeAndDetach(_ctx.Comp, _ctx.AvDesc)
                    : ASMLiteWindowOperations.ExecuteDetachToDirectDelivery(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(result.RollbackAttempted);
            Assert.IsTrue(result.RollbackSucceeded, result.ToLogString());
            Assert.AreEqual(alreadyVendorized, _ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
            Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
            Assert.AreEqual(parametersBefore, EditorJsonUtility.ToJson(oldParameters));
            foreach (var menu in menusBefore)
            {
                Assert.AreEqual(menu.Value.Guid, AssetDatabase.AssetPathToGUID(menu.Key), "Rollback must preserve submenu identity: " + menu.Key);
                Assert.AreEqual(menu.Value.Json, EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(menu.Key)),
                    "Rollback must restore the complete generated menu payload: " + menu.Key);
            }
            CollectionAssert.AreEqual(fxBefore, controllerBefore.parameters.Select(p => (p.name, p.type, p.defaultBool, p.defaultFloat, p.defaultInt)).ToArray());
            CollectionAssert.AreEqual(layerNamesBefore, controllerBefore.layers.Select(layer => layer.name).ToArray());
            Assert.AreEqual(controllerHideFlags, controllerBefore.hideFlags);
            AssertLiveFullControllerReferencesUnderPrefix(expectedDir, aid);
            baseline.AssertMatches(aid);
        }

        [TestCase("root")]
        [TestCase("unrelated")]
        [TestCase("sibling")]
        [TestCase("mixed")]
        [TestCase("nonowned")]
        [TestCase("menu")]
        public void AttachedReturn_RejectsUnownedDeletionWithoutChangingFilesOrReferences(string targetKind)
        {
            PreparePackageManagedAvatarForVendorize("Return_Unowned");
            Assert.IsTrue(ASMLiteWindowOperations.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc).Success);
            string target = _ctx.Comp.vendorizedGeneratedAssetsPath;
            var sibling = new GameObject("OtherAvatar");
            string siblingFolder = null;
            try
            {
                if (targetKind == "root")
                    target = ASMLiteGeneratedOwnershipPolicy.VendorizedGeneratedAssetsRoot;
                else if (targetKind == "unrelated")
                    target = Path.GetDirectoryName(AssetDatabase.GetAssetPath(_ctx.ParamsAsset)).Replace('\\', '/');
                else if (targetKind == "sibling")
                {
                    var mirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(sibling.AddComponent<VRCAvatarDescriptor>());
                    Assert.IsTrue(mirror.Success, mirror.Message);
                    siblingFolder = Path.GetDirectoryName(mirror.TargetPath).Replace('\\', '/');
                    Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(mirror).Success);
                    target = mirror.TargetPath;
                }
                else if (targetKind == "mixed")
                    AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<VRCExpressionsMenu>(), target + "/UserMenu.asset");
                else if (targetKind == "menu")
                {
                    _ctx.AvDesc.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
                    {
                        type = VRCExpressionsMenu.Control.ControlType.Button,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = "User" },
                    });
                    EditorUtility.SetDirty(_ctx.AvDesc.expressionsMenu);
                }
                else
                {
                    var parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(target + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
                    parameters.parameters = new[] { new VRCExpressionParameters.Parameter { name = "User" } };
                    EditorUtility.SetDirty(parameters);
                }
                AssetDatabase.SaveAssets();
                _ctx.Comp.vendorizedGeneratedAssetsPath = target;
                var oldMenu = _ctx.AvDesc.expressionsMenu;
                var oldParameters = _ctx.AvDesc.expressionParameters;
                var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
                var files = Directory.GetFiles(Path.GetFullPath(target), "*", SearchOption.AllDirectories)
                    .ToDictionary(path => path, File.ReadAllBytes);
                string assetPath = AssetDatabase.GetAssetPath(oldMenu);
                string guid = AssetDatabase.AssetPathToGUID(assetPath);

                var rejected = ASMLiteGeneratedAssetMirrorService.BackupVendorizedFolderForDelete(_ctx.AvDesc, target);
                Assert.IsFalse(rejected.Success);
                Assert.IsFalse(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedFolderDelete(_ctx.AvDesc, rejected).Success);
                var result = ASMLiteWindowOperations.ExecuteAttachedReturnToPackageManaged(_ctx.Comp, _ctx.AvDesc);

                Assert.IsFalse(result.Success);
                Assert.IsFalse(result.RollbackAttempted, "Ownership rejection must precede all mutations.");
                Assert.AreEqual(target, _ctx.Comp.vendorizedGeneratedAssetsPath);
                Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
                Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
                Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
                Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
                Assert.AreEqual(guid, AssetDatabase.AssetPathToGUID(assetPath));
                CollectionAssert.AreEquivalent(files.Keys, Directory.GetFiles(Path.GetFullPath(target), "*", SearchOption.AllDirectories));
                foreach (var file in files)
                    CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sibling);
                if (!string.IsNullOrEmpty(siblingFolder) && AssetDatabase.IsValidFolder(siblingFolder))
                    Assert.IsTrue(AssetDatabase.DeleteAsset(siblingFolder), "Remove only the sibling mirror created by this test.");
            }
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
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ASM-Lite\] BUILD-302: \[ASM-Lite\] Injected staged-copy failure before vendorized mirror promotion\. Context: 'Assets/ASM-Lite/[^']+/GeneratedAssets__stage__[a-f0-9]{8}'\. Remediation: Disable the staged-copy failure injection after validating rollback behavior\.$"));

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
        public void Rebuild_RollbackFailure_RetainsAndIdentifiesPreviousMirror()
        {
            PreparePackageManagedAvatarForVendorize("RollbackFailure");
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            var errors = new List<string>();
            Application.LogCallback record = (message, stack, type) => errors.Add(message);
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.VendorizeForAutomation();
                string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
                string oldGuid = AssetDatabase.AssetPathToGUID(previousPath + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
                LogAssert.Expect(LogType.Error, new Regex("Vendorized mirror rollback failed"));
                LogAssert.Expect(LogType.Exception, new Regex("Injected failure before mirror finalization"));
                Application.logMessageReceived += record;
                using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(
                    ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize | ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringMirrorRollback))
                    window.RebuildForAutomation();

                string retainedAsset = AssetDatabase.GUIDToAssetPath(oldGuid);
                StringAssert.Contains("GeneratedAssets__backup__", retainedAsset);
                Assert.IsTrue(File.Exists(Path.GetFullPath(retainedAsset)));
                string backupPath = Path.GetDirectoryName(retainedAsset).Replace('\\', '/');
                Assert.IsTrue(errors.Any(message => message.Contains(backupPath)), "Failure must identify the retained recovery folder.");
                Assert.IsFalse(errors.Any(message => message.Contains("Vendorized payload sync complete")));
                Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
                Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            }
            finally
            {
                Application.logMessageReceived -= record;
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public void Rebuild_Failure_RestoresAssetIdentityAndAllReferences(bool vendorized, bool duringStagedCopy)
        {
            PreparePackageManagedAvatarForVendorize("LateFailure");
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                if (vendorized)
                    window.VendorizeForAutomation();
                string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
                string assetDir = vendorized ? previousPath : ASMLiteAssetPaths.GeneratedDir;
                var baseline = PackageOutputBytesSnapshot.Capture();
                var oldMenu = _ctx.AvDesc.expressionsMenu;
                var oldParameters = _ctx.AvDesc.expressionParameters;
                var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
                var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
                Assert.IsNotNull(vf);
                Func<UnityEngine.Object[]> readLiveReferences = () => new[]
                {
                    ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath).Reference,
                    ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath).Reference,
                    ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(vf,
                        ASMLiteDriftProbe.ParametersObjectRefPath, ASMLiteDriftProbe.ParameterObjectRefPath,
                        ASMLiteDriftProbe.ParameterLegacyObjectRefPath).Reference,
                };
                var liveReferencesBefore = readLiveReferences();
                Assert.IsTrue(liveReferencesBefore.All(reference => reference != null));
                string menuGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(oldMenu));
                byte[] menuBytes = File.ReadAllBytes(Path.GetFullPath(AssetDatabase.GetAssetPath(oldMenu)));
                MutateGeneratedInputsBeforeVendorize("LateFailure_NewParam");
                if (duringStagedCopy)
                    LogAssert.Expect(LogType.Error, "[ASM-Lite] Vendorized mirror staging threw an exception.");
                LogAssert.Expect(duringStagedCopy ? LogType.Error : LogType.Exception,
                    new Regex(duringStagedCopy ? "Injected exception during staged copy" : "Injected failure before mirror finalization"));
                using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(duringStagedCopy
                    ? ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringStagedCopy
                    : ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize))
                    window.RebuildForAutomation();

                Assert.AreEqual(vendorized, _ctx.Comp.useVendorizedGeneratedAssets);
                Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
                Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu, "Descriptor must recover original menu identity.");
                Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
                Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
                AssertDescriptorGeneratedAssetReferencesUnderPrefix(assetDir, "Late rollback");
                CollectionAssert.AreEqual(liveReferencesBefore, readLiveReferences(),
                    "Late rollback must restore the actual pre-action FullController references, including mixed routing.");
                Assert.AreEqual(menuGuid, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)));
                CollectionAssert.AreEqual(menuBytes, File.ReadAllBytes(Path.GetFullPath(AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu))));
                baseline.AssertMatches("Late rollback");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

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

    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteLifecycleRebuildPackageOutputIsolationIntegrationTests : ASMLitePackageOutputIsolationIntegrationTestBase
    {
        [TestCase("Rebuild", false, false)]
        [TestCase("Rebuild", true, true)]
        [TestCase("Vendorize", false, false)]
        [TestCase("Vendorize", true, true)]
        [TestCase("Build", false, true)]
        [TestCase("Build", true, true)]
        public void FailedGeneration_RestoresIndependentFullControllerReferences(string operation, bool nullReferences, bool earlyFailure)
        {
            PreparePackageManagedAvatarForVendorize("IndependentReferenceRollback");
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf);
            var controller = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath).Reference;
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            var parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(controller);
            Assert.IsNotNull(menu);
            Assert.IsNotNull(parameters);
            var expected = new UnityEngine.Object[] { nullReferences ? null : controller, menu, nullReferences ? null : parameters };
            var serializedVf = new SerializedObject(vf);
            string[][] referencePaths =
            {
                new[] { ASMLiteDriftProbe.ControllerObjectRefPath, ASMLiteDriftProbe.ControllerMirrorPath },
                new[] { ASMLiteDriftProbe.MenuObjectRefPath, ASMLiteDriftProbe.MenuMirrorPath },
                new[] { ASMLiteDriftProbe.ParametersObjectRefPath, ASMLiteDriftProbe.ParametersMirrorPath },
            };
            for (int i = 0; i < referencePaths.Length; i++)
                foreach (string path in referencePaths[i])
                {
                    var property = serializedVf.FindProperty(path);
                    Assert.IsNotNull(property, "The native FullController schema must contain the seeded reference field.");
                    property.objectReferenceValue = expected[i];
                }
            serializedVf.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreNotEqual(AssetDatabase.GetAssetPath(menu),
                Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller)).Replace('\\', '/') + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu),
                "The regression must seed independently routed live assets.");
            var oldMenu = _ctx.AvDesc.expressionsMenu;
            var oldParameters = _ctx.AvDesc.expressionParameters;
            var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize("IndependentReferenceRollback_NewParam");

            if (earlyFailure && operation != "Build")
            {
                LogAssert.Expect(LogType.Error, "[ASM-Lite] Vendorized mirror staging threw an exception.");
                if (operation == "Rebuild")
                    LogAssert.Expect(LogType.Error, new Regex("BUILD-302.*Vendorized mirror staging threw an exception"));
            }
            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(earlyFailure
                ? ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringStagedCopy
                : ASMLiteGeneratedAssetMirrorTestFailurePoint.None))
            using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(operation == "Vendorize" && !earlyFailure
                ? ASMLiteLifecycleTransactionTestFailurePoint.AfterLiveFullControllerRetarget
                : ASMLiteLifecycleTransactionTestFailurePoint.None))
            {
                if (operation == "Rebuild")
                {
                    using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(earlyFailure
                        ? ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringStagedCopy
                        : ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize))
                        Assert.IsFalse(ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc).Completed);
                }
                else if (operation == "Vendorize")
                    Assert.IsFalse(ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc).Success);
                else
                    Assert.IsFalse(ASMLiteBuilder.TryBuildWithDiagnostics(_ctx.Comp, out _).Success);
            }

            vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            var actual = new[]
            {
                ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath).Reference,
                ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath).Reference,
                ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(vf, ASMLiteDriftProbe.ParametersObjectRefPath,
                    ASMLiteDriftProbe.ParameterObjectRefPath, ASMLiteDriftProbe.ParameterLegacyObjectRefPath).Reference,
            };
            CollectionAssert.AreEqual(expected, actual,
                "Failed generation must restore each admitted live reference independently, including explicit nulls.");
            for (int i = 0; i < referencePaths.Length; i++)
                Assert.AreEqual(expected[i], ASMLiteTestFixtures.ReadSerializedObjectReference(vf, referencePaths[i][1]).Reference);
            Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
            Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
            baseline.AssertMatches("Independent reference rollback");
        }

        [Test]
        public void LifecycleRebuild_StagesAvatarLocalGeneratedAssets_AndRestoresPackageOutputs()
        {
            const string aid = "LifecycleRebuild_Isolated";
            PreparePackageManagedAvatarForVendorize(aid);
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");

            var result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc);

            Assert.IsTrue(result.Completed, "Direct lifecycle rebuild should finish successfully.");
            Assert.IsNull(result.Exception);
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0);
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.IsTrue(AssetDatabase.IsValidFolder(_ctx.Comp.vendorizedGeneratedAssetsPath));
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(_ctx.Comp.vendorizedGeneratedAssetsPath, aid);
            AssertLiveFullControllerReferencesUnderPrefix(_ctx.Comp.vendorizedGeneratedAssetsPath, aid);
            baseline.AssertMatches(aid);
        }

        [Test]
        public void LifecycleRebuild_ValidationFailure_RestoresPackageOutputs_WithoutBuildCountOrException()
        {
            const string aid = "LifecycleRebuild_ValidationFailure";
            PreparePackageManagedAvatarForVendorize(aid);
            AddLegacyGeneratedRemnantsToPackageManagedGeneratedOutputs(aid);
            var baseline = PackageOutputBytesSnapshot.Capture();
            _ctx.Comp.slotCount = 0;
            LogAssert.Expect(LogType.Error,
                "[ASM-Lite] BUILD-305: [ASM-Lite] Select an avatar and a preset count between 1 and 8. Context: 'expressionParameters'. Remediation: Assign readable parameter assets and resolve name/type conflicts before generation.");

            var result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Completed);
            Assert.AreEqual(-1, result.DiscoveredParamCount);
            Assert.IsNull(result.Exception, "Build validation failure should keep the log-and-return path, not trigger the build-error dialog.");
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.IsTrue(string.IsNullOrEmpty(_ctx.Comp.vendorizedGeneratedAssetsPath));
            baseline.AssertMatches(aid);
        }

        [Test]
        public void LifecycleRebuild_LateFailure_RestoresIdentityAndReferences_AndRetainsBuildCount()
        {
            const string aid = "LifecycleRebuild_LateFailure";
            PreparePackageManagedAvatarForVendorize(aid);
            Assert.IsTrue(ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc).Success);
            string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
            var oldMenu = _ctx.AvDesc.expressionsMenu;
            var oldParameters = _ctx.AvDesc.expressionParameters;
            var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
            string menuPath = AssetDatabase.GetAssetPath(oldMenu);
            string menuGuid = AssetDatabase.AssetPathToGUID(menuPath);
            byte[] menuBytes = File.ReadAllBytes(Path.GetFullPath(menuPath));
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");

            ASMLiteRebuildResult result;
            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize))
                result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Completed);
            Assert.IsInstanceOf<InvalidOperationException>(result.Exception);
            StringAssert.Contains("Injected failure before mirror finalization", result.Exception.Message);
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0, "A later failure must not erase the successful build count.");
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
            Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
            Assert.AreEqual(menuGuid, AssetDatabase.AssetPathToGUID(menuPath));
            CollectionAssert.AreEqual(menuBytes, File.ReadAllBytes(Path.GetFullPath(menuPath)));
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(previousPath, aid);
            AssertLiveFullControllerReferencesUnderPrefix(previousPath, aid);
            baseline.AssertMatches(aid);
        }

        [Test]
        public void LifecycleRebuild_NoStaging_KeepsPackageManagedDelivery()
        {
            const string aid = "LifecycleRebuild_NoStaging";
            PreparePackageManagedAvatarForVendorize(aid);
            string controllerPath = Path.GetFullPath(ASMLiteAssetPaths.FXController);
            byte[] controllerBefore = File.ReadAllBytes(controllerPath);
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");

            var result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc, stagePackageManagedGeneratedAssets: false);

            Assert.IsTrue(result.Completed);
            Assert.IsNull(result.Exception);
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0);
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.IsTrue(string.IsNullOrEmpty(_ctx.Comp.vendorizedGeneratedAssetsPath));
            CollectionAssert.AreEqual(controllerBefore, File.ReadAllBytes(controllerPath), "Ordinary rebuilds must leave package templates read-only.");
            string generatedDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(generatedDir, aid);
            AssertLiveFullControllerReferencesUnderPrefix(generatedDir, aid);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LifecycleRebuild_PackageRestoreFailureAfterStaging_RestoresBothGraphsAndOriginalState(bool vendorized)
        {
            const string aid = "LifecycleRebuild_RestoreFailureAfterStaging";
            PreparePackageManagedAvatarForVendorize(aid);
            string mirrorDir;
            if (vendorized)
            {
                var vendorize = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);
                Assert.IsTrue(vendorize.Success, vendorize.ToLogString());
                mirrorDir = _ctx.Comp.vendorizedGeneratedAssetsPath;
            }
            else
            {
                // A package-managed avatar can still have an unreferenced prior delivery mirror.
                var priorMirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(
                    _ctx.AvDesc, ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp));
                Assert.IsTrue(priorMirror.Success, priorMirror.ToLogString());
                Assert.IsTrue(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(priorMirror).Success);
                mirrorDir = priorMirror.TargetPath;
            }

            string workingDir = ASMLiteAssetPaths.ResolveGeneratedDirectory(_ctx.Comp);
            string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
            var graphFiles = new[] { workingDir, mirrorDir }
                .SelectMany(dir => Directory.GetFiles(Path.GetFullPath(dir), "*", SearchOption.AllDirectories)
                    .Append(Path.GetFullPath(dir + ".meta")))
                .ToDictionary(path => path, File.ReadAllBytes);
            var graphGuids = new[] { workingDir, mirrorDir }
                .SelectMany(dir => AssetDatabase.FindAssets(string.Empty, new[] { dir }))
                .ToDictionary(AssetDatabase.GUIDToAssetPath, guid => guid);
            string oldMirrorMenuGuid = AssetDatabase.AssetPathToGUID(mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            string oldWorkingMenuGuid = AssetDatabase.AssetPathToGUID(workingDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            var oldMenu = _ctx.AvDesc.expressionsMenu;
            var oldParameters = _ctx.AvDesc.expressionParameters;
            var oldFx = _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController;
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf);
            Func<UnityEngine.Object[]> readLiveReferences = () => new[]
            {
                ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath).Reference,
                ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath).Reference,
                ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(vf,
                    ASMLiteDriftProbe.ParametersObjectRefPath, ASMLiteDriftProbe.ParameterObjectRefPath,
                    ASMLiteDriftProbe.ParameterLegacyObjectRefPath).Reference,
            };
            var liveBefore = readLiveReferences();
            Assert.IsTrue(liveBefore.All(reference => reference != null));
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");

            bool injected = false;
            bool stagedReferencesObserved = false;
            bool mirrorBackupObserved = false;
            bool workingBackupObserved = false;
            var logs = new List<string>();
            Application.LogCallback record = (message, stack, type) => logs.Add(message);
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ASM-Lite\] Failed to restore package generated outputs after rebuild\..*Injected post-staging package restore failure.*$"));
            ASMLiteRebuildResult result;
            Application.logMessageReceived += record;
            try
            {
                using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
                {
                    if (phase != "replacement-deleted" || injected)
                        return;
                    injected = true;
                    stagedReferencesObserved = AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu) == mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu)
                        && readLiveReferences().All(reference => AssetDatabase.GetAssetPath(reference).StartsWith(mirrorDir + "/", StringComparison.Ordinal));
                    mirrorBackupObserved = AssetDatabase.GUIDToAssetPath(oldMirrorMenuGuid).Contains("GeneratedAssets__backup__");
                    workingBackupObserved = AssetDatabase.GUIDToAssetPath(oldWorkingMenuGuid).Contains("GeneratedAssets__backup__");
                    throw new IOException("Injected post-staging package restore failure.");
                }))
                    result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc);
            }
            finally
            {
                Application.logMessageReceived -= record;
            }

            Assert.IsTrue(injected, "Exercise package restoration, not the earlier BeforeFinalize failure seam.");
            Assert.IsTrue(stagedReferencesObserved, "Descriptor and live references must reach the replacement mirror before restoration fails.");
            Assert.IsTrue(mirrorBackupObserved, "The original mirror identities must remain recoverable until package restoration succeeds.");
            Assert.IsTrue(workingBackupObserved, "The original working graph must remain recoverable at the same boundary.");
            Assert.IsFalse(result.Completed);
            Assert.IsNull(result.Exception, "Package restore failure keeps the existing log-and-return path.");
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0, "Do not erase a successful build count on restoration failure.");
            Assert.AreEqual(vendorized, _ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            Assert.AreEqual(oldMenu, _ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(oldParameters, _ctx.AvDesc.expressionParameters);
            Assert.AreEqual(oldFx, _ctx.AvDesc.baseAnimationLayers[FindFxLayerIndex(_ctx.AvDesc)].animatorController);
            CollectionAssert.AreEqual(liveBefore, readLiveReferences(), "Restore the actual pre-action FullController references, not a guessed common prefix.");
            foreach (var asset in graphGuids)
                Assert.AreEqual(asset.Value, AssetDatabase.AssetPathToGUID(asset.Key), "Restore each original working and delivery asset GUID: " + asset.Key);
            CollectionAssert.AreEquivalent(graphFiles.Keys, new[] { workingDir, mirrorDir }
                .SelectMany(dir => Directory.GetFiles(Path.GetFullPath(dir), "*", SearchOption.AllDirectories)
                    .Append(Path.GetFullPath(dir + ".meta"))), "Rollback must restore the original graph file set.");
            foreach (var file in graphFiles)
                CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), "Restore original graph bytes and metadata: " + file.Key);
            baseline.AssertMatches(aid);

            var recovery = logs.Select(message => Regex.Match(message, @"Verified recovery copies retained at '([^']+)'")).FirstOrDefault(match => match.Success);
            Assert.IsNotNull(recovery, "The package restore diagnostic must identify the retained recovery directory.");
            string recoveryDir = recovery.Groups[1].Value;
            Assert.IsTrue(Directory.Exists(recoveryDir), recoveryDir);
            Assert.IsTrue(File.Exists(Path.Combine(recoveryDir, "RECOVERY.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(recoveryDir, "captured-baseline", "roots.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(recoveryDir, "pre-restore", "roots.txt")));
            Assert.IsFalse(logs.Any(message => message.Contains("Vendorized payload sync complete")
                || message.Contains("Package-managed rebuild staged generated assets")), "An incomplete operation must not log commit success.");
        }

        [Test]
        public void LifecycleRebuild_CleanupFailureAfterCommit_RetainsMirrorWithoutRollback()
        {
            const string aid = "LifecycleRebuild_CommittedCleanupFailure";
            PreparePackageManagedAvatarForVendorize(aid);
            Assert.IsTrue(ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc).Success);
            string mirrorDir = _ctx.Comp.vendorizedGeneratedAssetsPath;
            string menuPath = mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu);
            string oldGuid = AssetDatabase.AssetPathToGUID(menuPath);
            byte[] oldBytes = File.ReadAllBytes(Path.GetFullPath(menuPath));
            var baseline = PackageOutputBytesSnapshot.Capture();
            MutateGeneratedInputsBeforeVendorize(aid + "_AddedParam");
            var logs = new List<string>();
            Application.LogCallback record = (message, stack, type) => logs.Add(message);
            IDisposable cleanupFailure = null;
            LogAssert.Expect(LogType.Warning, new Regex(@"Rebuild committed; recovery backup cleanup incomplete at 'Assets/ASM-Lite/[^']+/GeneratedAssets__backup__[a-f0-9]{8}'\..*Injected failure before mirror finalization"));
            ASMLiteRebuildResult result;
            Application.logMessageReceived += record;
            try
            {
                // Enable the existing seam only after its non-destructive pre-commit check passed.
                using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
                {
                    if (phase == "prepared" && cleanupFailure == null)
                        cleanupFailure = ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize);
                }))
                    result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc);
            }
            finally
            {
                cleanupFailure?.Dispose();
                Application.logMessageReceived -= record;
            }

            Assert.IsTrue(result.Completed, "Backup cleanup failure must not undo an already verified commit.");
            Assert.IsNull(result.Exception);
            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets);
            Assert.AreEqual(mirrorDir, _ctx.Comp.vendorizedGeneratedAssetsPath);
            Assert.AreNotEqual(oldGuid, AssetDatabase.AssetPathToGUID(menuPath));
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(mirrorDir, aid);
            AssertLiveFullControllerReferencesUnderPrefix(mirrorDir, aid);
            string retainedMenu = AssetDatabase.GUIDToAssetPath(oldGuid);
            StringAssert.Contains("GeneratedAssets__backup__", retainedMenu);
            CollectionAssert.AreEqual(oldBytes, File.ReadAllBytes(Path.GetFullPath(retainedMenu)));
            string recoveryDir = Path.GetDirectoryName(retainedMenu).Replace('\\', '/');
            Assert.IsTrue(logs.Any(message => message.Contains(recoveryDir)), "Identify the retained mirror recovery material, not an impossible rollback.");
            baseline.AssertMatches(aid);
        }

        [Test]
        public void LifecycleRebuild_PackageRestoreFailure_TakesPrecedenceOverRebuildException()
        {
            const string aid = "LifecycleRebuild_RestoreFailure";
            PreparePackageManagedAvatarForVendorize(aid);
            Assert.IsTrue(ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc).Success);
            string previousPath = _ctx.Comp.vendorizedGeneratedAssetsPath;
            LogAssert.Expect(LogType.Error, new Regex(@"^\[ASM-Lite\] Failed to restore package generated outputs after rebuild\..*Injected package restore failure\.$"));

            ASMLiteRebuildResult result;
            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.BeforeFinalize))
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestoreFailureForTesting("Injected package restore failure."))
                result = ASMLiteLifecycleTransactionService.ExecuteRebuild(_ctx.Comp, _ctx.AvDesc);

            Assert.IsFalse(result.Completed);
            Assert.IsNull(result.Exception, "Package restore failure should retain its existing log-and-return path, not trigger the build-error dialog.");
            Assert.GreaterOrEqual(result.DiscoveredParamCount, 0);
            Assert.AreEqual(previousPath, _ctx.Comp.vendorizedGeneratedAssetsPath);
            AssertDescriptorGeneratedAssetReferencesUnderPrefix(previousPath, aid);
            AssertLiveFullControllerReferencesUnderPrefix(previousPath, aid);
        }
    }
}
