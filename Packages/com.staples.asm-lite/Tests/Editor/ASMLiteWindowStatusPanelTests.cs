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
            Assert.AreEqual(
                "Status: Ready to edit. ASM-Lite is attached to this avatar and can be updated here.",
                ASMLite.Editor.ASMLiteWindow.BuildCombinedStatusMessage(snapshot));
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Info,
                ASMLite.Editor.ASMLiteWindow.GetCombinedStatusSeverity(snapshot));
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

            StringAssert.Contains("• ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode.",
                ASMLite.Editor.ASMLiteWindow.BuildCombinedStatusMessage(detached));
            StringAssert.Contains("• ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode.",
                ASMLite.Editor.ASMLiteWindow.BuildCombinedStatusMessage(vendorized));
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
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Warning,
                ASMLite.Editor.ASMLiteWindow.GetCombinedStatusSeverity(snapshot));
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
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Warning,
                ASMLite.Editor.ASMLiteWindow.GetCombinedStatusSeverity(snapshot));
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
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Warning,
                ASMLite.Editor.ASMLiteWindow.GetCombinedStatusSeverity(snapshot));
        }

        [Test]
        public void BuildCombinedStatusMessage_WithWarnings_ContainsSummaryAndBulletLines()
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

            string message = ASMLite.Editor.ASMLiteWindow.BuildCombinedStatusMessage(snapshot);

            StringAssert.StartsWith(
                "Status: Ready to edit. ASM-Lite is attached to this avatar and can be updated here.",
                message);
            StringAssert.Contains("• ✓ ASM-Lite is attached to this avatar.", message);
            StringAssert.Contains("• ⚠ Avatar parameter data is still importing in Unity. Please wait a moment.", message);
            StringAssert.Contains("• [Toggle Broker] Last setup reserved 5 name(s) and auto-adjusted conflicting names: preflight=2, intra-candidate=1.", message);
        }

        [Test]
        public void BuildCombinedStatusMessage_InformationalOnly_UsesSummaryAndDetails()
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

            string message = ASMLite.Editor.ASMLiteWindow.BuildCombinedStatusMessage(snapshot);

            StringAssert.StartsWith(
                "Status: Ready to edit. ASM-Lite is attached to this avatar and can be updated here.",
                message);
            StringAssert.Contains("• ✓ ASM-Lite is attached to this avatar.", message);
            StringAssert.Contains("• ✓ 8 custom parameter(s) are being saved across 3 preset(s).", message);
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.StatusDetailSeverity.Info,
                ASMLite.Editor.ASMLiteWindow.GetCombinedStatusSeverity(snapshot));
        }

        private static ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshot BuildSnapshot(
            ASMLite.Editor.ASMLiteWindow.StatusPanelSnapshotInput input)
        {
            return ASMLite.Editor.ASMLiteWindow.BuildStatusPanelSnapshot(input);
        }
    }
}
