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
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
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
        public void BuildActionHierarchyContract_ComponentPresentVendorized_StaysExplicit()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Vendorized,
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
        }

        [Test]
        public void BuildActionHierarchyContract_ComponentPresentVendorized_ExpandedAdvancedMakesMaintenanceVisible()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Vendorized,
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
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Detached,
                hasComponent: false,
                advancedDisclosureExpanded: false);

            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged),
                "Detached avatars without an attached component must expose Return to Package Managed as primary recovery.");
            Assert.AreEqual(0, hierarchy.AdvancedActions.Length,
                "Detached recovery flow should not require advanced disclosure to access its only recovery action.");
            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Component-missing detached hierarchy");
        }

        [Test]
        public void BuildActionHierarchyContract_ComponentMissingVendorized_StaysExplicit()
        {
            var hierarchy = ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Vendorized,
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
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.NotInstalled,
                hasComponent: false,
                advancedDisclosureExpanded: false);

            Assert.True(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab),
                "Not-installed avatars must keep Add Prefab as the primary setup action.");
            Assert.AreEqual(0, hierarchy.AdvancedActions.Length,
                "Not-installed flow should not surface maintenance actions before setup occurs.");
            AssertPrimaryContainsNoMaintenanceActions(hierarchy,
                "Avatar-selected not-installed hierarchy");
        }

        [Test]
        public void GetAsmLiteToolState_ComponentPresentVendorized_WinsOverAvatarHeuristics()
        {
            _ctx.Comp.useVendorizedGeneratedAssets = true;

            var toolState = ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp);

            Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Vendorized, toolState,
                "Component-present vendorized combinations should resolve explicitly to Vendorized state.");
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

            Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Detached, toolState,
                "Component-missing avatars with ASM-Lite runtime markers should resolve explicitly to Detached state.");
        }

        [Test]
        public void GetAsmLiteToolState_AvatarSelectedNotInstalled_RemainsExplicit()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            var toolState = ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null);

            Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.NotInstalled, toolState,
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
