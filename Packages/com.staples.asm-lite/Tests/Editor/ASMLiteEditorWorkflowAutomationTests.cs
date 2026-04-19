using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteEditorWorkflowAutomationTests
    {
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

        private static MonoBehaviour EnsureLiveFullControllerPayload(ASMLiteComponent component)
        {
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(component != null ? component.gameObject : null);
            Assert.IsNotNull(vf,
                "Expected the workflow under test to leave a live VF.Model.VRCFury component on the ASM-Lite object.");
            return vf;
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
