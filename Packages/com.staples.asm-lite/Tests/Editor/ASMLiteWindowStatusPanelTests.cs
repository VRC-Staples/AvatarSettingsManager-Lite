using System.Linq;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowStatusPanelTests
    {
        [Test]
        public void BuildStatusPanelSnapshot_MalformedPackageManagedWithoutComponent_HasNoExtraDetails()
        {
            var snapshot = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                hasComponent: false,
                slotCount: 0,
                discoveredParamCount: 0,
                backedUpCount: null,
                parameterImportPending: false,
                hasToggleBrokerReport: false,
                toggleBrokerPreReservedNameCount: 0,
                toggleBrokerPreflightCollisionAdjustments: 0,
                toggleBrokerCandidateCollisionAdjustments: 0));

            Assert.AreEqual(
                "Status: Ready to edit. ASM-Lite is attached to this avatar and can be updated here.",
                snapshot.SummaryText);
            Assert.AreEqual(0, snapshot.DetailEntries.Length,
                "Malformed component-missing package-managed input should not invent detail rows.");
            Assert.False(snapshot.ShowDetailsDisclosure,
                "No detail rows means no disclosure affordance should be shown.");
            Assert.False(snapshot.DetailsCollapsedByDefault,
                "No detail rows means collapsed-by-default should remain false.");
        }

        [Test]
        public void BuildStatusPanelSnapshot_DetachedOrVendorizedWithoutComponent_UsesBakedOnlyGuidance()
        {
            var detached = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Detached,
                hasComponent: false,
                slotCount: 0,
                discoveredParamCount: 0,
                backedUpCount: null,
                parameterImportPending: false,
                hasToggleBrokerReport: false,
                toggleBrokerPreReservedNameCount: 0,
                toggleBrokerPreflightCollisionAdjustments: 0,
                toggleBrokerCandidateCollisionAdjustments: 0));

            var vendorized = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Vendorized,
                hasComponent: false,
                slotCount: 0,
                discoveredParamCount: 0,
                backedUpCount: null,
                parameterImportPending: false,
                hasToggleBrokerReport: false,
                toggleBrokerPreReservedNameCount: 0,
                toggleBrokerPreflightCollisionAdjustments: 0,
                toggleBrokerCandidateCollisionAdjustments: 0));

            Assert.That(detached.DetailEntries.Select(d => d.Text),
                Contains.Item("ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode."));
            Assert.That(vendorized.DetailEntries.Select(d => d.Text),
                Contains.Item("ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode."));
            Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Info, detached.DetailEntries[0].Severity);
            Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Info, vendorized.DetailEntries[0].Severity);
            Assert.True(detached.ShowDetailsDisclosure);
            Assert.True(vendorized.ShowDetailsDisclosure);
            Assert.True(detached.DetailsCollapsedByDefault);
            Assert.True(vendorized.DetailsCollapsedByDefault);
        }

        [Test]
        public void BuildStatusPanelSnapshot_NotInstalledWithoutComponent_UsesWarningOnboardingGuidance()
        {
            var snapshot = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.NotInstalled,
                hasComponent: false,
                slotCount: 0,
                discoveredParamCount: 0,
                backedUpCount: null,
                parameterImportPending: false,
                hasToggleBrokerReport: false,
                toggleBrokerPreReservedNameCount: 0,
                toggleBrokerPreflightCollisionAdjustments: 0,
                toggleBrokerCandidateCollisionAdjustments: 0));

            Assert.That(snapshot.DetailEntries.Select(d => d.Text),
                Contains.Item("ASM-Lite is not on this avatar yet.\nSet your options above, then click \"Add ASM-Lite Prefab\"."));
            Assert.AreEqual(1, snapshot.DetailEntries.Length,
                "Not-installed branch should keep exactly one onboarding warning detail.");
            Assert.AreEqual(ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Warning, snapshot.DetailEntries[0].Severity);
            Assert.True(snapshot.ShowDetailsDisclosure);
            Assert.True(snapshot.DetailsCollapsedByDefault);
        }

        [Test]
        public void BuildStatusPanelSnapshot_AttachedMissingExpressionParameters_ExposesWarningDetail()
        {
            var snapshot = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                hasComponent: true,
                slotCount: 4,
                discoveredParamCount: -1,
                backedUpCount: null,
                parameterImportPending: false,
                hasToggleBrokerReport: false,
                toggleBrokerPreReservedNameCount: 0,
                toggleBrokerPreflightCollisionAdjustments: 0,
                toggleBrokerCandidateCollisionAdjustments: 0));

            Assert.That(snapshot.DetailEntries.Select(d => d.Text),
                Contains.Item("⚠ This avatar has no Expression Parameters asset assigned yet."));
            Assert.AreEqual(1, snapshot.DetailEntries.Count(d => d.Severity == ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Warning),
                "Missing-expression branch should provide one warning detail.");
            Assert.True(snapshot.ShowDetailsDisclosure);
            Assert.True(snapshot.DetailsCollapsedByDefault);
        }

        [Test]
        public void BuildStatusPanelSnapshot_AttachedImportPendingAndToggleCollisions_ExposesMultipleConditionalWarnings()
        {
            var snapshot = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                hasComponent: true,
                slotCount: 3,
                discoveredParamCount: 0,
                backedUpCount: null,
                parameterImportPending: true,
                hasToggleBrokerReport: true,
                toggleBrokerPreReservedNameCount: 5,
                toggleBrokerPreflightCollisionAdjustments: 2,
                toggleBrokerCandidateCollisionAdjustments: 1));

            Assert.That(snapshot.DetailEntries.Select(d => d.Text),
                Contains.Item("⚠ Avatar parameter data is still importing in Unity. Please wait a moment."));
            Assert.That(snapshot.DetailEntries.Select(d => d.Text),
                Contains.Item("[Toggle Broker] Last setup reserved 5 name(s) and auto-adjusted conflicting names: preflight=2, intra-candidate=1."));

            Assert.AreEqual(2, snapshot.DetailEntries.Count(d => d.Severity == ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Warning),
                "Import-pending plus collision branch should emit two warning details.");
            Assert.True(snapshot.ShowDetailsDisclosure);
            Assert.True(snapshot.DetailsCollapsedByDefault,
                "Any conditional details should default to collapsed disclosure for S03 compression behavior.");
        }

        [Test]
        public void BuildStatusDetailsDisclosureLabel_WithWarnings_ContainsWarningCount()
        {
            var snapshot = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                hasComponent: true,
                slotCount: 3,
                discoveredParamCount: 0,
                backedUpCount: null,
                parameterImportPending: true,
                hasToggleBrokerReport: true,
                toggleBrokerPreReservedNameCount: 5,
                toggleBrokerPreflightCollisionAdjustments: 2,
                toggleBrokerCandidateCollisionAdjustments: 1));

            string label = ASMLite.Editor.ASMLiteWindow.BuildStatusDetailsDisclosureLabel(snapshot);

            Assert.AreEqual("Details (2 warning(s))", label);
        }

        [Test]
        public void BuildStatusDetailsDisclosureLabel_InformationalOnly_UsesDetailCount()
        {
            var snapshot = BuildSnapshot(new ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.PackageManaged,
                hasComponent: true,
                slotCount: 3,
                discoveredParamCount: 8,
                backedUpCount: 8,
                parameterImportPending: false,
                hasToggleBrokerReport: false,
                toggleBrokerPreReservedNameCount: 0,
                toggleBrokerPreflightCollisionAdjustments: 0,
                toggleBrokerCandidateCollisionAdjustments: 0));

            string label = ASMLite.Editor.ASMLiteWindow.BuildStatusDetailsDisclosureLabel(snapshot);

            Assert.AreEqual("Details (2)", label);
        }

        private static ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshot BuildSnapshot(
            ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput input)
        {
            return ASMLite.Editor.ASMLiteWindow.BuildStatusPanelSnapshot(input);
        }
    }
}
