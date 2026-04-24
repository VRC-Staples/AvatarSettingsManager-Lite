using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowTerminologyTests
    {
        private static readonly Type WindowType = typeof(ASMLite.Editor.ASMLiteWindow);
        private static readonly Type ToolStateType = WindowType.GetNestedType("AsmLiteToolState", BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SnapshotMethod = WindowType.GetMethod(
            "GetTerminologySnapshot",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        [Test]
        public void AlwaysVisibleCopy_UsesPresetTerminologyAcrossSettingsPreviewAndCustomizeSurfaces()
        {
            var copy = GetAllCopy("NotInstalled", hasComponent: false).ToArray();

            CollectionAssert.Contains(copy, "Preset Count");
            CollectionAssert.Contains(copy,
                "How many presets your avatar has. Each preset can hold a full snapshot of your settings.");
            CollectionAssert.Contains(copy,
                "How many presets to add. Each preset lets you save and load a full set of avatar settings.");
            CollectionAssert.Contains(copy,
                "Changed preset count? Click \"Rebuild ASM-Lite\" to apply it.");
            CollectionAssert.Contains(copy,
                "Each preset uses a different gear color for quick visual scanning.\nPresets 1 to 4: Blue, Red, Green, Purple\nPresets 5 to 8: Cyan, Orange, Pink, Yellow");
            CollectionAssert.Contains(copy,
                "Flow: Root Menu → Presets Menu → Action Submenu");
            CollectionAssert.Contains(copy, "Presets Menu");
            CollectionAssert.Contains(copy, "Preset Icons");
            CollectionAssert.Contains(copy, "Preset 1");
            CollectionAssert.Contains(copy, "Clear Preset");
            CollectionAssert.Contains(copy, "Preset 1 Icon");
            CollectionAssert.Contains(copy, "Clear Preset Icon");
            CollectionAssert.Contains(copy,
                "A preset icon set here overrides the selected Icon Mode for that preset only.\nEmpty presets keep the normal Icon Mode icon.");
            CollectionAssert.Contains(copy,
                "Keep your current in-game preset data working, but remove the ASM-Lite tool object from this avatar. Great for sharing a finished avatar. You won’t be able to tweak ASM-Lite settings unless you add it again.");

            AssertNoSlotTerms(copy);
        }

        [TestCase("PackageManaged", true, "Status: Ready to edit. ASM-Lite is attached to this avatar and can be updated here.")]
        [TestCase("Vendorized", true, "Status: Vendorized. ASM-Lite is still editable, and generated files are also copied to Assets/ASM-Lite.")]
        [TestCase("Vendorized", false, "Status: Vendorized. This avatar is using ASM-Lite files copied under Assets/ASM-Lite, but the editable ASM-Lite object is not attached.")]
        [TestCase("Detached", false, "Status: Baked only. This avatar has ASM-Lite data, but the editable ASM-Lite object is not attached.")]
        [TestCase("NotInstalled", false, "Status: Not installed. ASM-Lite has not been added to this avatar yet.")]
        public void StateSpecificStatusCopy_MatchesTerminologyContract(string toolStateName, bool hasComponent, string expectedStatus)
        {
            var copy = GetStateSpecificCopy(toolStateName, hasComponent);

            CollectionAssert.Contains(copy, expectedStatus);
            AssertNoSlotTerms(copy);
        }

        [Test]
        public void StateSpecificHelpCopy_CoversDetachedVendorizedAndNotInstalledBranches()
        {
            var detachedNoComponent = GetStateSpecificCopy("Detached", hasComponent: false);
            var vendorizedNoComponent = GetStateSpecificCopy("Vendorized", hasComponent: false);
            var notInstalledNoComponent = GetStateSpecificCopy("NotInstalled", hasComponent: false);

            CollectionAssert.Contains(detachedNoComponent,
                "ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode.");
            CollectionAssert.Contains(vendorizedNoComponent,
                "ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode.");
            CollectionAssert.Contains(notInstalledNoComponent,
                "ASM-Lite is not on this avatar yet.\nSet your options above, then click \"Add ASM-Lite Prefab\".");

            AssertNoSlotTerms(detachedNoComponent);
            AssertNoSlotTerms(vendorizedNoComponent);
            AssertNoSlotTerms(notInstalledNoComponent);
        }

        [TestCase("PackageManaged", true)]
        [TestCase("Vendorized", true)]
        [TestCase("Vendorized", false)]
        [TestCase("Detached", false)]
        [TestCase("NotInstalled", false)]
        public void TerminologySnapshot_AllCopySurfacesRejectMixedSlotPresetLanguage(string toolStateName, bool hasComponent)
        {
            var copy = GetAllCopy(toolStateName, hasComponent).ToArray();

            Assert.That(copy.Length, Is.GreaterThan(0),
                $"Expected terminology snapshot to expose copy for {toolStateName} (hasComponent={hasComponent}).");
            AssertNoSlotTerms(copy);
        }

        private static IEnumerable<string> GetAllCopy(string toolStateName, bool hasComponent)
        {
            var snapshot = GetSnapshot(toolStateName, hasComponent);
            var always = ReadStringArrayProperty(snapshot, "AlwaysVisibleCopy");
            var stateSpecific = ReadStringArrayProperty(snapshot, "StateSpecificCopy");

            return always.Concat(stateSpecific).Where(v => !string.IsNullOrWhiteSpace(v));
        }

        private static string[] GetStateSpecificCopy(string toolStateName, bool hasComponent)
        {
            var snapshot = GetSnapshot(toolStateName, hasComponent);
            return ReadStringArrayProperty(snapshot, "StateSpecificCopy");
        }

        private static object GetSnapshot(string toolStateName, bool hasComponent)
        {
            Assert.IsNotNull(ToolStateType, "Missing nested enum AsmLiteToolState on ASMLiteWindow.");
            Assert.IsNotNull(SnapshotMethod, "Missing GetTerminologySnapshot method on ASMLiteWindow.");

            object toolState = Enum.Parse(ToolStateType, toolStateName);
            return SnapshotMethod.Invoke(null, new[] { toolState, (object)hasComponent });
        }

        private static string[] ReadStringArrayProperty(object snapshot, string propertyName)
        {
            Assert.IsNotNull(snapshot, "Terminology snapshot invocation returned null.");

            PropertyInfo property = snapshot.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, $"Missing property '{propertyName}' on terminology snapshot type.");

            return (string[])property.GetValue(snapshot);
        }

        private static void AssertNoSlotTerms(IEnumerable<string> copy)
        {
            foreach (string line in copy)
            {
                Assert.False(
                    line.IndexOf("slot", StringComparison.OrdinalIgnoreCase) >= 0,
                    $"Detected mixed slot/preset terminology in copy: '{line}'");
            }
        }
    }
}
