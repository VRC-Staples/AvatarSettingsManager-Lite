using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowActionHierarchyTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test]
        public void FreshWindow_DefaultsAdvancedDisclosureToCollapsed_ForAttachedAvatar()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);

                var hierarchy = window.GetActionHierarchyContract();

                Assert.False(hierarchy.AdvancedDisclosureExpanded,
                    "Fresh window instances should default Advanced maintenance disclosure to collapsed.");
                Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild),
                    "Attached avatars must keep Rebuild in the primary action set.");
                Assert.True(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab),
                    "Attached avatars must keep Remove in advanced actions.");
                Assert.True(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach),
                    "Attached avatars must keep Detach in advanced actions.");
                Assert.True(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize),
                    "Attached avatars must keep Vendorize in advanced actions.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void BuildActionHierarchyContract_PackageManagedAttached_SplitsPrimaryAndAdvancedCorrectly()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.PackageManaged,
                hasComponent: true,
                advancedDisclosureExpanded: false);

            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Package-managed attached hierarchy");
            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild),
                "Package-managed attached avatars must expose Rebuild as a primary action.");
            Assert.False(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab),
                "Remove must never appear in primary actions.");
            Assert.False(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach),
                "Detach must never appear in primary actions.");
            Assert.False(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize),
                "Vendorize must never appear in primary actions.");

            Assert.False(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab),
                "Closed Advanced disclosure must hide Remove from visible action controls.");
            Assert.False(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach),
                "Closed Advanced disclosure must hide Detach from visible action controls.");
            Assert.False(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize),
                "Closed Advanced disclosure must hide Vendorize from visible action controls.");
        }

        [Test]
        public void BuildActionHierarchyContract_PackageManagedAttached_ExposesRichActionDescriptors()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.PackageManaged,
                hasComponent: true,
                advancedDisclosureExpanded: false);

            Assert.AreEqual(1, hierarchy.PrimaryDescriptors.Length,
                "Package-managed attached avatars should expose one recommended descriptor.");
            AssertDescriptor(
                hierarchy.PrimaryDescriptors[0],
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild,
                ASMLite.Editor.AsmLiteWindowActionGroup.Primary,
                ASMLite.Editor.AsmLiteWindowActionExecution.Rebuild,
                "Rebuild ASM-Lite",
                isMaintenance: false,
                isDestructive: false,
                isVisible: true,
                isEnabled: true,
                confirmationRequired: false,
                supportsVisibleAutomation: true);

            Assert.AreEqual(new[]
            {
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize,
            }, Array.ConvertAll(hierarchy.AdvancedDescriptors, descriptor => descriptor.Action),
                "Advanced descriptors should preserve maintenance action order.");

            var remove = hierarchy.GetDescriptor(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab);
            AssertDescriptor(
                remove,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab,
                ASMLite.Editor.AsmLiteWindowActionGroup.Advanced,
                ASMLite.Editor.AsmLiteWindowActionExecution.RemovePrefab,
                "Remove Prefab",
                isMaintenance: true,
                isDestructive: true,
                isVisible: false,
                isEnabled: true,
                confirmationRequired: true,
                supportsVisibleAutomation: false);
            Assert.AreEqual("Remove ASM-Lite Prefab", remove.Confirmation.Title);
            Assert.AreEqual("Remove", remove.Confirmation.ConfirmLabel);
            Assert.AreEqual("Cancel", remove.Confirmation.CancelLabel);
            StringAssert.Contains("remove the ASM-Lite prefab", remove.Confirmation.Message);

            var detach = hierarchy.GetDescriptor(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach);
            AssertDescriptor(
                detach,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach,
                ASMLite.Editor.AsmLiteWindowActionGroup.Advanced,
                ASMLite.Editor.AsmLiteWindowActionExecution.Detach,
                "Detach ASM-Lite",
                isMaintenance: true,
                isDestructive: false,
                isVisible: false,
                isEnabled: true,
                confirmationRequired: false,
                supportsVisibleAutomation: true);
            Assert.AreEqual("Detach ASM-Lite (Runtime-safe)", detach.Heading);
            StringAssert.Contains("remove the ASM-Lite tool object", detach.Description);

            var vendorize = hierarchy.GetDescriptor(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize);
            AssertDescriptor(
                vendorize,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize,
                ASMLite.Editor.AsmLiteWindowActionGroup.Advanced,
                ASMLite.Editor.AsmLiteWindowActionExecution.Vendorize,
                "Vendorize (Keep Attached)",
                isMaintenance: true,
                isDestructive: false,
                isVisible: false,
                isEnabled: true,
                confirmationRequired: false,
                supportsVisibleAutomation: true);
            Assert.AreEqual("Vendorize ASM-Lite Payload", vendorize.Heading);
            StringAssert.Contains("mirror generated payload files", vendorize.Description);
        }

        [Test]
        public void BuildActionHierarchyContract_ComponentPresentVendorized_StaysExplicit()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.Vendorized,
                hasComponent: true,
                advancedDisclosureExpanded: false);

            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Component-present vendorized hierarchy");
            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild),
                "Vendorized attached avatars must keep Rebuild as the primary action.");
            Assert.True(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged),
                "Vendorized attached avatars must keep package-managed return in advanced actions.");
            Assert.False(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged),
                "Closed Advanced disclosure must hide attached vendorized return controls.");

            var attachedReturn = hierarchy.GetDescriptor(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged);
            AssertDescriptor(
                attachedReturn,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged,
                ASMLite.Editor.AsmLiteWindowActionGroup.Advanced,
                ASMLite.Editor.AsmLiteWindowActionExecution.ReturnToPackageManaged,
                "Return This Avatar to Package Managed",
                isMaintenance: true,
                isDestructive: false,
                isVisible: false,
                isEnabled: true,
                confirmationRequired: false,
                supportsVisibleAutomation: true);
            Assert.AreEqual("Return This Avatar to Package Managed", attachedReturn.Heading);
            StringAssert.Contains("vendorized payload folder", attachedReturn.Description);
        }

        [Test]
        public void BuildActionHierarchyContract_ComponentPresentVendorized_ExpandedAdvancedMakesMaintenanceVisible()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.Vendorized,
                hasComponent: true,
                advancedDisclosureExpanded: true);

            Assert.True(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab),
                "Expanded Advanced disclosure must surface Remove for attached avatars.");
            Assert.True(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach),
                "Expanded Advanced disclosure must surface Detach for attached avatars.");
            Assert.True(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize),
                "Expanded Advanced disclosure must surface Vendorize for attached avatars.");
            Assert.True(IsActionVisible(hierarchy, ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged),
                "Expanded Advanced disclosure must surface attached vendorized return controls.");
        }

        [Test]
        public void BuildActionHierarchyContract_ComponentMissingDetached_StaysExplicit()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.Detached,
                hasComponent: false,
                advancedDisclosureExpanded: false);

            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged),
                "Detached avatars without an attached component must expose Return to Package Managed as primary recovery.");
            Assert.AreEqual(0, hierarchy.AdvancedActions.Length,
                "Detached recovery flow should not require advanced disclosure to access its only recovery action.");
            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Component-missing detached hierarchy");

            var recovery = hierarchy.GetDescriptor(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged);
            AssertDescriptor(
                recovery,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged,
                ASMLite.Editor.AsmLiteWindowActionGroup.Primary,
                ASMLite.Editor.AsmLiteWindowActionExecution.ReturnToPackageManaged,
                "Return to Package Managed",
                isMaintenance: false,
                isDestructive: false,
                isVisible: true,
                isEnabled: true,
                confirmationRequired: false,
                supportsVisibleAutomation: true);
            Assert.AreEqual("Return to Package Managed Mode", recovery.Heading);
        }

        [Test]
        public void BuildActionHierarchyContract_ComponentMissingVendorized_StaysExplicit()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.Vendorized,
                hasComponent: false,
                advancedDisclosureExpanded: false);

            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged),
                "Vendorized avatars without an attached component must expose Return to Package Managed as primary recovery.");
            Assert.AreEqual(0, hierarchy.AdvancedActions.Length,
                "Vendorized recovery flow should not hide the only recovery action behind advanced disclosure.");
            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Component-missing vendorized hierarchy");
        }

        [Test]
        public void BuildActionHierarchyContract_AvatarSelectedNotInstalled_StaysExplicit()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                hasComponent: false,
                advancedDisclosureExpanded: false);

            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab),
                "Not-installed avatars must keep Add Prefab as the primary setup action.");
            Assert.AreEqual(0, hierarchy.AdvancedActions.Length,
                "Not-installed flow should not surface maintenance actions before setup occurs.");
            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Avatar-selected not-installed hierarchy");

            AssertDescriptor(
                hierarchy.GetDescriptor(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab),
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab,
                ASMLite.Editor.AsmLiteWindowActionGroup.Primary,
                ASMLite.Editor.AsmLiteWindowActionExecution.AddPrefab,
                "Add ASM-Lite Prefab",
                isMaintenance: false,
                isDestructive: false,
                isVisible: true,
                isEnabled: true,
                confirmationRequired: false,
                supportsVisibleAutomation: true);
        }

        [Test]
        public void GetAttachedCustomizationSnapshotForAutomation_ExposesActionDescriptorsFromSharedModel()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);

                var snapshot = window.GetAttachedCustomizationSnapshotForAutomation();

                Assert.True(snapshot.HasAttachedComponent,
                    "Attached automation snapshots should keep reporting component presence.");
                Assert.True(snapshot.HasPrimaryAction,
                    "Attached automation snapshots should keep reporting a primary action for smoke overlays.");
                Assert.AreEqual(1, snapshot.PrimaryActionDescriptors.Length,
                    "Automation snapshots should expose the same recommended descriptor contract as the window hierarchy.");
                Assert.AreEqual(snapshot.PrimaryActions[0], snapshot.PrimaryActionDescriptors[0].Action,
                    "Legacy primary action arrays should be derived from the descriptor contract.");
                Assert.AreEqual(snapshot.PrimaryAction, snapshot.PrimaryActionDescriptor.Action,
                    "Legacy primary action should mirror the primary descriptor action.");
                AssertDescriptor(
                    snapshot.PrimaryActionDescriptor,
                    ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild,
                    ASMLite.Editor.AsmLiteWindowActionGroup.Primary,
                    ASMLite.Editor.AsmLiteWindowActionExecution.Rebuild,
                    "Rebuild ASM-Lite",
                    isMaintenance: false,
                    isDestructive: false,
                    isVisible: true,
                    isEnabled: true,
                    confirmationRequired: false,
                    supportsVisibleAutomation: true);
                Assert.AreEqual(new[]
                {
                    ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab,
                    ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach,
                    ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize,
                }, Array.ConvertAll(snapshot.AdvancedActionDescriptors, descriptor => descriptor.Action),
                    "Automation snapshots should expose advanced action descriptors for smoke tooling without duplicating metadata.");
                Assert.AreEqual(snapshot.AdvancedActions, Array.ConvertAll(snapshot.AdvancedActionDescriptors, descriptor => descriptor.Action),
                    "Legacy advanced action arrays should be derived from the descriptor contract.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GetAsmLiteToolState_ComponentPresentVendorized_WinsOverAvatarHeuristics()
        {
            _ctx.Comp.useVendorizedGeneratedAssets = true;

            var toolState = ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.Vendorized, toolState,
                "Component-present vendorized combinations should resolve explicitly to Vendorized state.");
        }

        [Test]
        public void GetActionHierarchyContract_NoComponent_ReusesCachedToolStateUntilExplicitInvalidation()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);

                var initial = window.GetActionHierarchyContract();
                Assert.True(initial.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab),
                    "Initial no-component draw should expose Add ASM-Lite Prefab while the avatar is still not installed.");

                ASMLiteTestFixtures.AddExpressionParam(
                    _ctx,
                    ASMLite.Editor.ASMLiteBuilder.CtrlParam,
                    VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int,
                    defaultValue: 0f,
                    saved: false,
                    networkSynced: false);

                var cached = window.GetActionHierarchyContract();
                Assert.True(cached.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab),
                    "Repeated pre-add redraws should reuse cached tool-state detection instead of rescanning the avatar after every unrelated editor change or keypress.");
                Assert.False(cached.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged),
                    "Detached recovery should not replace the cached pre-add action set until the window explicitly invalidates tool-state detection.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GetAsmLiteToolState_ComponentMissingDetachedDetectedFromRuntimeMarkers()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            ASMLiteTestFixtures.AddExpressionParam(
                _ctx,
                ASMLite.Editor.ASMLiteBuilder.CtrlParam,
                VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int,
                defaultValue: 0f,
                saved: false,
                networkSynced: false);

            var toolState = ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.Detached, toolState,
                "Component-missing avatars with ASM-Lite runtime markers should resolve explicitly to Detached state.");
        }

        [Test]
        public void GetAsmLiteToolState_AvatarSelectedNotInstalled_RemainsExplicit()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var toolState = ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteInstallationState.NotInstalled, toolState,
                "Avatar-selected without component or runtime markers should resolve explicitly to NotInstalled state.");
        }

        private static void AssertPrimaryContainsNoMaintenanceActions(
            ASMLite.Editor.ASMLiteWindow.AsmLiteActionHierarchy hierarchy,
            string scenario)
        {
            for (int i = 0; i < hierarchy.PrimaryActions.Length; i++)
            {
                var action = hierarchy.PrimaryActions[i];
                Assert.False(ASMLite.Editor.ASMLiteWindow.IsMaintenanceAction(action),
                    $"{scenario} should not include maintenance action '{action}' in primary actions.");
            }
        }

        private static void AssertDescriptor(
            ASMLite.Editor.AsmLiteWindowActionDescriptor descriptor,
            ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction action,
            ASMLite.Editor.AsmLiteWindowActionGroup group,
            ASMLite.Editor.AsmLiteWindowActionExecution execution,
            string label,
            bool isMaintenance,
            bool isDestructive,
            bool isVisible,
            bool isEnabled,
            bool confirmationRequired,
            bool supportsVisibleAutomation)
        {
            Assert.AreEqual(action, descriptor.Action);
            Assert.AreEqual(group, descriptor.Group);
            Assert.AreEqual(execution, descriptor.Execution);
            Assert.AreEqual(label, descriptor.Label);
            Assert.AreEqual(isMaintenance, descriptor.IsMaintenance);
            Assert.AreEqual(isDestructive, descriptor.IsDestructive);
            Assert.AreEqual(isVisible, descriptor.IsVisible);
            Assert.AreEqual(isEnabled, descriptor.IsEnabled);
            Assert.AreEqual(confirmationRequired, descriptor.Confirmation.Required);
            Assert.AreEqual(supportsVisibleAutomation, descriptor.SupportsVisibleAutomation);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static bool IsActionVisible(
            ASMLite.Editor.ASMLiteWindow.AsmLiteActionHierarchy hierarchy,
            ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction action)
        {
            if (hierarchy.HasPrimaryAction(action))
                return true;

            if (!hierarchy.AdvancedDisclosureExpanded)
                return false;

            return hierarchy.HasAdvancedAction(action);
        }
    }
}
