using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ASMLite.Editor;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteEditorWorkflowAutomationTests
    {
        private const string SuiteName = nameof(ASMLiteEditorWorkflowAutomationTests);
        private AsmLiteTestContext _ctx;
        private string _vendorizedAvatarFolder;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_vendorizedAvatarFolder)
                && AssetDatabase.IsValidFolder(_vendorizedAvatarFolder))
            {
                AssetDatabase.DeleteAsset(_vendorizedAvatarFolder);
            }

            if (AssetDatabase.IsValidFolder("Assets/ASM-Lite")
                && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/ASM-Lite" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/ASM-Lite");
            }

            AssetDatabase.Refresh();
            _vendorizedAvatarFolder = null;
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test]
        public void Automation_AddPrefabAndBake_AppliesPendingCustomizationDeterministically()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                SetPrivateField(window, "_pendingSlotCount", 4);
                SetPrivateField(window, "_pendingUseCustomRootName", true);
                SetPrivateField(window, "_pendingCustomRootName", "  Tools Root  ");
                SetPrivateField(window, "_pendingUseCustomInstallPath", true);
                SetPrivateField(window, "_pendingCustomInstallPath", "  Tools/Automation  ");
                SetPrivateField(window, "_pendingUseParameterExclusions", true);
                SetPrivateField(window, "_pendingExcludedParameterNames", new[] { "Hat", " Hat ", "Mood" });

                window.AddPrefabForAutomation();

                var component = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(component,
                    "Automation add flow should create an ASM-Lite component under the selected avatar.");
                Assert.AreEqual(4, component.slotCount,
                    "Automation add flow should copy pending slot count onto the created prefab.");
                Assert.IsTrue(component.useCustomRootName,
                    "Automation add flow should preserve the pending custom root-name toggle.");
                Assert.AreEqual("Tools Root", component.customRootName,
                    "Automation add flow should normalize pending custom root name before serialization.");
                Assert.IsTrue(component.useCustomInstallPath,
                    "Automation add flow should preserve the pending install-path toggle.");
                Assert.AreEqual("Tools/Automation", component.customInstallPath,
                    "Automation add flow should normalize pending install path before serialization.");
                CollectionAssert.AreEqual(new[] { "Hat", "Mood" }, component.excludedParameterNames,
                    "Automation add flow should sanitize pending parameter exclusions deterministically.");

                var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(component.gameObject);
                Assert.IsNotNull(vf,
                    "Automation add flow should leave a live FullController payload on the created prefab.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(vf),
                    "Automation add flow should clear prefab-instance FullController prefix overrides after syncing install-path routing through the helper object.");
                AssertInstallPathRoutingHelper(_ctx.AvDesc, "Tools Root", "Tools/Automation/Tools Root",
                    "Automation add flow should persist deterministic install-path routing through the avatar helper object.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Automation_QueuedVisibleAction_UsesAvatarCapturedAtQueueTime()
        {
            Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var secondaryAvatarGo = new GameObject("SecondaryTestAvatar");
            var secondaryAvatar = secondaryAvatarGo.AddComponent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
            secondaryAvatar.baseAnimationLayers = (VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.CustomAnimLayer[])_ctx.AvDesc.baseAnimationLayers.Clone();
            secondaryAvatar.expressionParameters = _ctx.ParamsAsset;
            secondaryAvatar.expressionsMenu = _ctx.MenuAsset;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.QueueVisibleAutomationAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab);

                window.SelectAvatarForAutomation(secondaryAvatar);
                InvokePrivateMethod(window, "ExecuteQueuedVisibleAutomationAction");

                var primaryComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                var secondaryComponent = secondaryAvatar.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(primaryComponent,
                    "Queued visible automation should keep targeting the avatar that was selected when the action was queued.");
                Assert.IsNull(secondaryComponent,
                    "Queued visible automation should not drift onto a newly selected avatar before the delayed action executes.");
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(secondaryAvatarGo);
            }
        }

        [Test]
        public void Automation_AddPrefabForAutomation_WithExistingComponent_FailsClosedWithoutPromptOrDuplicate()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                int beforeCount = _ctx.AvDesc.GetComponentsInChildren<ASMLiteComponent>(true).Length;
                Assert.AreEqual(1, beforeCount,
                    "Regression setup should start with exactly one ASM-Lite component already attached to the avatar.");

                LogAssert.Expect(LogType.Warning,
                    new Regex(@"^\[ASM-Lite\].*duplicate add.*already attached.*$"));
                Assert.DoesNotThrow(() => window.AddPrefabForAutomation(),
                    "Automation add should fail closed without surfacing batch-mode confirmation dialogs when ASM-Lite is already attached.");

                int afterCount = _ctx.AvDesc.GetComponentsInChildren<ASMLiteComponent>(true).Length;
                Assert.AreEqual(beforeCount, afterCount,
                    "Automation add should not create a duplicate ASM-Lite instance when dialog prompts are suppressed.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Automation_Rebuild_RefreshesInstallPathRoutingDeterministically()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "  Tools/Initial  ";
                window.RebuildForAutomation();
                Assert.AreEqual("Tools/Initial", ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(_ctx.Comp)),
                    "Automation rebuild should write the first normalized install-path prefix deterministically.");

                _ctx.Comp.customInstallPath = "  Tools/Updated  ";
                window.RebuildForAutomation();
                Assert.AreEqual("Tools/Updated", ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(_ctx.Comp)),
                    "Automation rebuild should overwrite the live FullController prefix when settings change.");

                _ctx.Comp.useCustomInstallPath = false;
                window.RebuildForAutomation();
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(_ctx.Comp)),
                    "Automation rebuild should clear the live FullController prefix when custom install path is disabled.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Automation_LifecycleFlow_VendorizeDetachAndReturnToPackageManaged_RemainsDeterministic()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/Lifecycle";
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.VendorizeForAutomation();

                var attachedVendorized = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(attachedVendorized,
                    "Attached vendorize flow should keep ASM-Lite present on the avatar.");
                Assert.IsTrue(attachedVendorized.useVendorizedGeneratedAssets,
                    "Attached vendorize flow should toggle vendorized generated-assets mode on the component.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(attachedVendorized.vendorizedGeneratedAssetsPath),
                    "Attached vendorize flow should record the mirrored GeneratedAssets folder path.");
                Assert.IsTrue(AssetDatabase.IsValidFolder(attachedVendorized.vendorizedGeneratedAssetsPath),
                    "Attached vendorize flow should create the mirrored GeneratedAssets folder on disk.");
                _vendorizedAvatarFolder = Path.GetDirectoryName(attachedVendorized.vendorizedGeneratedAssetsPath)?.Replace('\\', '/');
                Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Vendorized,
                    ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, attachedVendorized),
                    "Attached vendorize flow should resolve to Vendorized tool state.");

                window.ReturnToPackageManagedForAutomation();

                var packageManaged = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(packageManaged,
                    "Return-to-package-managed should keep ASM-Lite attached after attached vendorized recovery.");
                Assert.IsFalse(packageManaged.useVendorizedGeneratedAssets,
                    "Attached vendorized recovery should clear vendorized mode on the component.");
                Assert.AreEqual(string.Empty, packageManaged.vendorizedGeneratedAssetsPath,
                    "Attached vendorized recovery should clear the tracked mirrored path.");
                Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                    ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, packageManaged),
                    "Attached vendorized recovery should restore package-managed tool state.");

                window.DetachForAutomation();

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "Detach automation should remove the editable ASM-Lite prefab from the avatar.");
                Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Detached,
                    ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "Detach automation should leave the avatar in detached tool state.");

                window.ReturnToPackageManagedForAutomation();

                var reattached = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(reattached,
                    "Detached recovery should re-attach ASM-Lite in package-managed mode.");
                Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                    ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, reattached),
                    "Detached recovery should restore package-managed tool state.");
                Assert.AreEqual("Tools/Lifecycle", reattached.customInstallPath,
                    "Detached recovery should preserve the component install-path setting across re-attachment.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(reattached)),
                    "Detached recovery should clear prefab-instance FullController prefix overrides after re-attachment.");
                AssertInstallPathRoutingHelper(_ctx.AvDesc, "Settings Manager", "Tools/Lifecycle/Settings Manager",
                    "Detached recovery should preserve deterministic install-path routing through the avatar helper object.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test, Category("Integration")]
        public void Automation_RebuildForAutomation_Twice_DoesNotDuplicateFullControllerEntries()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/RebuildTwice";

                ExecuteAutomationActionAndRecordLatestBuildDiagnostic(
                    nameof(Automation_RebuildForAutomation_Twice_DoesNotDuplicateFullControllerEntries),
                    window.RebuildForAutomation);

                var firstComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                var firstVf = EnsureLiveFullControllerPayload(firstComponent);
                var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(firstComponent, 0);
                var firstGlobalParams = ASMLiteTestFixtures.ReadSerializedStringArray(firstVf, "content.globalParams");
                AssertSingleCriticalFullControllerEntries(firstVf, "Automation rebuild first pass");

                ExecuteAutomationActionAndRecordLatestBuildDiagnostic(
                    nameof(Automation_RebuildForAutomation_Twice_DoesNotDuplicateFullControllerEntries),
                    window.RebuildForAutomation);

                var secondComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                var secondVf = EnsureLiveFullControllerPayload(secondComponent);
                var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(secondComponent, 0);
                var secondGlobalParams = ASMLiteTestFixtures.ReadSerializedStringArray(secondVf, "content.globalParams");
                AssertSingleCriticalFullControllerEntries(secondVf, "Automation rebuild second pass");

                Assert.AreEqual(1, secondSnapshot.LiveVrcFuryComponentCount,
                    "Repeated automation rebuilds should keep exactly one live VF.Model.VRCFury component on the ASM-Lite object.");
                Assert.AreEqual(firstSnapshot.ControllerReferencePath, secondSnapshot.ControllerReferencePath,
                    "Repeated automation rebuilds should keep the FullController FX reference stable.");
                Assert.AreEqual(firstSnapshot.MenuReferencePath, secondSnapshot.MenuReferencePath,
                    "Repeated automation rebuilds should keep the FullController menu reference stable.");
                Assert.AreEqual(firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                    "Repeated automation rebuilds should keep the selected parameter fallback path stable.");
                Assert.AreEqual(firstSnapshot.ParameterReferenceAssetPath, secondSnapshot.ParameterReferenceAssetPath,
                    "Repeated automation rebuilds should keep the FullController parameter asset reference stable.");
                CollectionAssert.AreEqual(firstGlobalParams, secondGlobalParams,
                    "Repeated automation rebuilds should keep wildcard global parameter enrollment stable.");
                CollectionAssert.AreEqual(new[] { "*" }, secondGlobalParams,
                    "Repeated automation rebuilds should keep exactly one wildcard global parameter enrollment entry.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test, Category("Integration")]
        public void Automation_VendorizeReturnTwice_KeepsRefsAndRoutingStable()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                Object.DestroyImmediate(_ctx.Comp.gameObject);
                _ctx.Comp = null;

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.AddPrefabForAutomation();

                var prefabInstanceComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(prefabInstanceComponent,
                    "Repeated vendorize/return characterization requires AddPrefabForAutomation() to attach ASM-Lite first.");
                prefabInstanceComponent.useCustomInstallPath = true;
                prefabInstanceComponent.customInstallPath = "Tools/VendorizeTwice";
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                ExecuteAutomationActionAndRecordLatestBuildDiagnostic(
                    nameof(Automation_VendorizeReturnTwice_KeepsRefsAndRoutingStable),
                    window.RebuildForAutomation);

                var baselineComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                var baselineSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(baselineComponent, 0);
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(baselineComponent)),
                    "Baseline prefab-instance rebuild should clear direct FullController prefix overrides before vendorize/return cycling.");
                AssertInstallPathRoutingHelper(_ctx.AvDesc, "Settings Manager", "Tools/VendorizeTwice/Settings Manager",
                    "Baseline prefab-instance rebuild should establish deterministic install-path routing before vendorize/return cycling.");

                for (int cycle = 1; cycle <= 2; cycle++)
                {
                    window.SelectAvatarForAutomation(_ctx.AvDesc);
                    ExecuteAutomationActionAndRecordLatestBuildDiagnostic(
                        nameof(Automation_VendorizeReturnTwice_KeepsRefsAndRoutingStable),
                        window.VendorizeForAutomation);

                    var vendorizedComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                    Assert.IsNotNull(vendorizedComponent,
                        $"Vendorize cycle {cycle} should keep ASM-Lite attached to the avatar.");
                    Assert.IsTrue(vendorizedComponent.useVendorizedGeneratedAssets,
                        $"Vendorize cycle {cycle} should toggle vendorized generated-assets mode on the component.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(vendorizedComponent.vendorizedGeneratedAssetsPath),
                        $"Vendorize cycle {cycle} should record the mirrored GeneratedAssets folder path.");
                    _vendorizedAvatarFolder = Path.GetDirectoryName(vendorizedComponent.vendorizedGeneratedAssetsPath)?.Replace('\\', '/');
                    AssertSingleCriticalFullControllerEntries(EnsureLiveFullControllerPayload(vendorizedComponent),
                        $"Vendorize cycle {cycle}");

                    ExecuteAutomationActionAndRecordLatestBuildDiagnostic(
                        nameof(Automation_VendorizeReturnTwice_KeepsRefsAndRoutingStable),
                        window.ReturnToPackageManagedForAutomation);

                    var returnedComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                    Assert.IsNotNull(returnedComponent,
                        $"Return cycle {cycle} should keep ASM-Lite attached to the avatar.");
                    Assert.IsFalse(returnedComponent.useVendorizedGeneratedAssets,
                        $"Return cycle {cycle} should clear vendorized generated-assets mode.");
                    Assert.AreEqual(string.Empty, returnedComponent.vendorizedGeneratedAssetsPath,
                        $"Return cycle {cycle} should clear the tracked mirrored GeneratedAssets folder path.");

                    var returnSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(returnedComponent, 0);
                    AssertSingleCriticalFullControllerEntries(EnsureLiveFullControllerPayload(returnedComponent),
                        $"Return cycle {cycle}");
                    Assert.AreEqual(baselineSnapshot.ControllerReferencePath, returnSnapshot.ControllerReferencePath,
                        $"Return cycle {cycle} should restore the canonical FullController FX reference path.");
                    Assert.AreEqual(baselineSnapshot.MenuReferencePath, returnSnapshot.MenuReferencePath,
                        $"Return cycle {cycle} should restore the canonical FullController menu reference path.");
                    Assert.AreEqual(baselineSnapshot.ParameterReferenceResolvedPath, returnSnapshot.ParameterReferenceResolvedPath,
                        $"Return cycle {cycle} should preserve the selected parameter fallback path.");
                    Assert.AreEqual(baselineSnapshot.ParameterReferenceAssetPath, returnSnapshot.ParameterReferenceAssetPath,
                        $"Return cycle {cycle} should restore the canonical FullController parameter asset reference path.");
                    Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(returnedComponent)),
                        $"Return cycle {cycle} should keep prefab-instance FullController prefix overrides cleared.");
                }
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void Automation_VendorizeForAutomation_StagedCopyFailure_RollsBackWithoutPartialAttachedMutation()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                Object.DestroyImmediate(_ctx.Comp.gameObject);
                _ctx.Comp = null;

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.AddPrefabForAutomation();
                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(_ctx.Comp,
                    "Automation staged-copy rollback validation requires AddPrefabForAutomation() to attach ASM-Lite before vendorize failure injection.");
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/AutomationRollback";
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                ExecuteAutomationActionAndRecordLatestBuildDiagnostic(
                    nameof(Automation_VendorizeForAutomation_StagedCopyFailure_RollsBackWithoutPartialAttachedMutation),
                    window.RebuildForAutomation);

                var baselineComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(baselineComponent,
                    "Automation staged-copy rollback validation requires ASM-Lite to stay attached after the baseline rebuild.");
                var baselineSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(baselineComponent, 0);
                AssertInstallPathRoutingHelper(_ctx.AvDesc, "Settings Manager", "Tools/AutomationRollback/Settings Manager",
                    "Automation staged-copy rollback setup should establish deterministic install-path routing before vendorize failure injection.");

                using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.AfterStagedCopy))
                {
                    LogAssert.Expect(LogType.Error, new Regex(@"^\[ASM-Lite\] Injected staged-copy failure before vendorized mirror promotion\..*$"));
                    window.VendorizeForAutomation();
                }

                var rolledBackComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(rolledBackComponent,
                    "Automation vendorize rollback should keep ASM-Lite attached after staged-copy failure.");
                Assert.IsFalse(rolledBackComponent.useVendorizedGeneratedAssets,
                    "Automation vendorize rollback should restore package-managed mode on the component after staged-copy failure.");
                Assert.AreEqual(string.Empty, rolledBackComponent.vendorizedGeneratedAssetsPath,
                    "Automation vendorize rollback should clear the tracked vendorized generated-assets path after staged-copy failure.");
                Assert.IsFalse(AssetDatabase.IsValidFolder("Assets/ASM-Lite/TestAvatar/GeneratedAssets"),
                    "Automation vendorize rollback should leave no vendorized generated-assets folder behind after staged-copy failure.");

                var rollbackSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(rolledBackComponent, 0);
                Assert.AreEqual(baselineSnapshot.ControllerReferencePath, rollbackSnapshot.ControllerReferencePath,
                    "Automation vendorize rollback should restore the canonical FullController FX reference path after staged-copy failure.");
                Assert.AreEqual(baselineSnapshot.MenuReferencePath, rollbackSnapshot.MenuReferencePath,
                    "Automation vendorize rollback should restore the canonical FullController menu reference path after staged-copy failure.");
                Assert.AreEqual(baselineSnapshot.ParameterReferenceResolvedPath, rollbackSnapshot.ParameterReferenceResolvedPath,
                    "Automation vendorize rollback should preserve the selected parameter fallback path after staged-copy failure.");
                Assert.AreEqual(baselineSnapshot.ParameterReferenceAssetPath, rollbackSnapshot.ParameterReferenceAssetPath,
                    "Automation vendorize rollback should restore the canonical FullController parameter asset reference path after staged-copy failure.");
                AssertSingleCriticalFullControllerEntries(EnsureLiveFullControllerPayload(rolledBackComponent),
                    nameof(Automation_VendorizeForAutomation_StagedCopyFailure_RollsBackWithoutPartialAttachedMutation));
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(rolledBackComponent)),
                    "Automation vendorize rollback should keep prefab-instance FullController prefix overrides cleared after staged-copy failure.");
                Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                    ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, rolledBackComponent),
                    "Automation vendorize rollback should resolve the attached avatar back to PackageManaged tool state after staged-copy failure.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private static void AssertInstallPathRoutingHelper(
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar,
            string expectedFromPath,
            string expectedToPath,
            string assertionMessage)
        {
            var routingTransform = avatar != null ? avatar.transform.Find("ASM-Lite Install Path Routing") : null;
            Assert.IsNotNull(routingTransform,
                assertionMessage + " Expected the ASM-Lite install-path routing helper object to exist on the avatar.");

            var routingVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(routingTransform.gameObject);
            Assert.IsNotNull(routingVf,
                assertionMessage + " Expected the routing helper object to carry a VF.Model.VRCFury component.");

            var serializedRouting = new SerializedObject(routingVf);
            serializedRouting.Update();

            var fromPathProperty = serializedRouting.FindProperty("content.fromPath");
            var toPathProperty = serializedRouting.FindProperty("content.toPath");
            Assert.IsNotNull(fromPathProperty,
                assertionMessage + " Expected MoveMenuItem fromPath to be serialized on the routing helper.");
            Assert.IsNotNull(toPathProperty,
                assertionMessage + " Expected MoveMenuItem toPath to be serialized on the routing helper.");
            Assert.AreEqual(expectedFromPath, fromPathProperty.stringValue,
                assertionMessage + " Unexpected routing helper source path.");
            Assert.AreEqual(expectedToPath, toPathProperty.stringValue,
                assertionMessage + " Unexpected routing helper destination path.");
        }

        private static void ExecuteAutomationActionAndRecordLatestBuildDiagnostic(string testName, Action automationAction)
        {
            automationAction?.Invoke();
            ASMLiteTestFixtures.RecordBuildDiagnosticFailure(
                SuiteName,
                testName,
                ASMLiteBuilder.GetLatestBuildDiagnosticResult());
        }

        private static MonoBehaviour EnsureLiveFullControllerPayload(ASMLiteComponent component)
        {
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(component != null ? component.gameObject : null);
            Assert.IsNotNull(vf,
                "Expected the workflow under test to leave a live VF.Model.VRCFury component on the ASM-Lite object.");
            return vf;
        }

        private static void AssertSingleCriticalFullControllerEntries(MonoBehaviour vf, string aid)
        {
            Assert.IsNotNull(vf, aid + " Expected a live VF.Model.VRCFury component before checking serialized FullController entry counts.");

            var serializedVf = new SerializedObject(vf);
            serializedVf.Update();

            var controllerArray = serializedVf.FindProperty(ASMLiteDriftProbe.ControllersArrayPath);
            var menuArray = serializedVf.FindProperty(ASMLiteDriftProbe.MenuArrayPath);
            var parameterArray = serializedVf.FindProperty(ASMLiteDriftProbe.ParametersArrayPath);
            Assert.IsNotNull(controllerArray,
                aid + " Expected the FullController controllers array to exist.");
            Assert.IsNotNull(menuArray,
                aid + " Expected the FullController menus array to exist.");
            Assert.IsNotNull(parameterArray,
                aid + " Expected the FullController prms array to exist.");
            Assert.AreEqual(1, controllerArray.arraySize,
                aid + " Expected exactly one FullController controller entry.");
            Assert.AreEqual(1, menuArray.arraySize,
                aid + " Expected exactly one FullController menu entry.");
            Assert.AreEqual(1, parameterArray.arraySize,
                aid + " Expected exactly one FullController parameter entry.");

            var controllerReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath);
            var menuReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath);
            Assert.IsTrue(controllerReference.HasReference,
                aid + " Expected a populated FullController FX controller reference.");
            Assert.IsTrue(menuReference.HasReference,
                aid + " Expected a populated FullController menu reference.");

            int populatedParameterFallbackMembers = new[]
            {
                ASMLiteDriftProbe.ParametersObjectRefPath,
                ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath,
            }.Count(path => ASMLiteTestFixtures.ReadSerializedObjectReference(vf, path).HasReference);
            Assert.AreEqual(1, populatedParameterFallbackMembers,
                aid + " Expected exactly one populated FullController parameter fallback-group member.");
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Missing private method '{methodName}' on {target.GetType().Name}.");
            method.Invoke(target, null);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }
    }
}
