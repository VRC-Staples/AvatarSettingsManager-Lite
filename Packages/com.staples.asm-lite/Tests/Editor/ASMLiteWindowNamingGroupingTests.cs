using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowNamingGroupingTests
    {
        private const string ExpectedFallbackGuidance =
            "Leave any name field blank to use ASM-Lite's default menu name for that item.";

        private static readonly Type WindowType = typeof(ASMLite.Editor.ASMLiteWindow);
        private static readonly Type NamingFlowStateType = WindowType.GetNestedType("NamingGroupFlowState", BindingFlags.Public | BindingFlags.NonPublic);

        private static readonly MethodInfo SnapshotMethod = WindowType.GetMethod(
            "GetNamingGroupSnapshot",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        [TestCase("Attached")]
        [TestCase("PendingInstall")]
        public void NamingGroupSnapshot_SectionsAndOrderMatchContract(string flowStateName)
        {
            object snapshot = GetSnapshot(flowStateName, presetCount: 3);

            CollectionAssert.AreEqual(
                new[] { "Root Menu Name", "Preset Names", "Action Labels" },
                ReadSectionHeaders(snapshot),
                "Naming section headers should remain stable before IMGUI regrouping work starts.");

            CollectionAssert.AreEqual(
                new[]
                {
                    "Root Menu",
                    "Preset 1",
                    "Preset 2",
                    "Preset 3",
                    "Save",
                    "Load",
                    "Clear Preset",
                    "Confirm",
                },
                ReadOrderedFieldLabels(snapshot),
                "Top-to-bottom naming field order should stay deterministic for this flow state.");

            Assert.AreEqual(ExpectedFallbackGuidance, ReadStringProperty(snapshot, "FallbackGuidance"));
        }

        [Test]
        public void NamingGroupSnapshot_AttachedAndPendingRemainInLockstep()
        {
            object attached = GetSnapshot("Attached", presetCount: 5);
            object pending = GetSnapshot("PendingInstall", presetCount: 5);

            CollectionAssert.AreEqual(
                ReadSectionHeaders(attached),
                ReadSectionHeaders(pending),
                "Section headers should not drift between attached and pending naming flows.");

            CollectionAssert.AreEqual(
                ReadOrderedFieldLabels(attached),
                ReadOrderedFieldLabels(pending),
                "Field order should not drift between attached and pending naming flows.");
        }

        [TestCase("Attached", 0, 1)]
        [TestCase("PendingInstall", 0, 1)]
        [TestCase("Attached", 1, 1)]
        [TestCase("PendingInstall", 8, 8)]
        public void NamingGroupSnapshot_PresetRowsScaleWithoutDropOrDuplicate(string flowStateName, int presetCount, int expectedPresetRows)
        {
            object snapshot = GetSnapshot(flowStateName, presetCount);
            string[] presetRows = ReadSectionFieldLabels(snapshot, "Preset Names");

            Assert.AreEqual(expectedPresetRows, presetRows.Length,
                "Preset-name rows should scale with preset count and clamp to at least one row.");

            for (int i = 0; i < expectedPresetRows; i++)
                Assert.AreEqual($"Preset {i + 1}", presetRows[i], "Preset rows should preserve deterministic numbering.");

            Assert.AreEqual(expectedPresetRows, presetRows.Distinct(StringComparer.Ordinal).Count(),
                "Preset-name rows should not duplicate labels within a snapshot.");
        }

        [Test]
        public void NamingGroupSnapshot_SectionMembershipIsExplicit()
        {
            object snapshot = GetSnapshot("Attached", presetCount: 4);

            CollectionAssert.AreEqual(new[] { "Root Menu" }, ReadSectionFieldLabels(snapshot, "Root Menu Name"));
            CollectionAssert.AreEqual(new[] { "Preset 1", "Preset 2", "Preset 3", "Preset 4" }, ReadSectionFieldLabels(snapshot, "Preset Names"));
            CollectionAssert.AreEqual(new[] { "Save", "Load", "Clear Preset", "Confirm" }, ReadSectionFieldLabels(snapshot, "Action Labels"));

            CollectionAssert.DoesNotContain(ReadSectionFieldLabels(snapshot, "Preset Names"), "Save");
            CollectionAssert.DoesNotContain(ReadSectionFieldLabels(snapshot, "Preset Names"), "Load");
            CollectionAssert.DoesNotContain(ReadSectionFieldLabels(snapshot, "Preset Names"), "Clear Preset");
            CollectionAssert.DoesNotContain(ReadSectionFieldLabels(snapshot, "Preset Names"), "Confirm");
        }

        private static object GetSnapshot(string flowStateName, int presetCount)
        {
            Assert.IsNotNull(NamingFlowStateType, "Missing nested enum NamingGroupFlowState on ASMLiteWindow.");
            Assert.IsNotNull(SnapshotMethod, "Missing GetNamingGroupSnapshot method on ASMLiteWindow.");

            object flowState = Enum.Parse(NamingFlowStateType, flowStateName);
            object snapshot = SnapshotMethod.Invoke(null, new[] { flowState, (object)presetCount });
            Assert.IsNotNull(snapshot, "GetNamingGroupSnapshot returned null.");

            return snapshot;
        }

        private static string[] ReadOrderedFieldLabels(object snapshot)
            => ReadStringArrayProperty(snapshot, "OrderedFieldLabels");

        private static string[] ReadSectionHeaders(object snapshot)
        {
            object[] sections = ReadSections(snapshot);
            var headers = new string[sections.Length];

            for (int i = 0; i < sections.Length; i++)
                headers[i] = ReadStringProperty(sections[i], "Header");

            return headers;
        }

        private static string[] ReadSectionFieldLabels(object snapshot, string sectionHeader)
        {
            object[] sections = ReadSections(snapshot);
            for (int i = 0; i < sections.Length; i++)
            {
                string header = ReadStringProperty(sections[i], "Header");
                if (string.Equals(header, sectionHeader, StringComparison.Ordinal))
                    return ReadStringArrayProperty(sections[i], "OrderedFieldLabels");
            }

            Assert.Fail($"Section '{sectionHeader}' was not found in naming snapshot.");
            return Array.Empty<string>();
        }

        private static object[] ReadSections(object snapshot)
        {
            object rawSections = ReadProperty(snapshot, "Sections");
            Assert.IsInstanceOf<IEnumerable>(rawSections, "Sections property should be enumerable.");

            return ((IEnumerable)rawSections).Cast<object>().ToArray();
        }

        private static string[] ReadStringArrayProperty(object instance, string propertyName)
        {
            object value = ReadProperty(instance, propertyName);
            Assert.IsInstanceOf<string[]>(value, $"Expected '{propertyName}' to be a string array.");
            return (string[])value;
        }

        private static string ReadStringProperty(object instance, string propertyName)
        {
            object value = ReadProperty(instance, propertyName);
            Assert.IsInstanceOf<string>(value, $"Expected '{propertyName}' to be a string.");
            return (string)value;
        }

        private static object ReadProperty(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, $"Missing property '{propertyName}' on {instance.GetType().Name}.");

            return property.GetValue(instance);
        }
    }
}
