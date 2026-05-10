using System;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteCustomizationDraftTests
    {
        private GameObject _avatarGo;
        private ASMLiteComponent _component;

        [SetUp]
        public void SetUp()
        {
            _avatarGo = new GameObject("DraftAvatar");
            _avatarGo.AddComponent<VRCAvatarDescriptor>();
            _component = new GameObject("ASM-Lite").AddComponent<ASMLiteComponent>();
            _component.transform.SetParent(_avatarGo.transform, false);
        }

        [TearDown]
        public void TearDown()
        {
            if (_avatarGo != null)
                UnityEngine.Object.DestroyImmediate(_avatarGo);
        }

        [Test]
        public void CaptureFromComponent_NormalizesSharedMigrationSnapshotState()
        {
            _component.slotCount = 4;
            _component.iconMode = IconMode.SameColor;
            _component.selectedGearIndex = 2;
            _component.useCustomRootName = true;
            _component.customRootName = "  Creator Menu  ";
            _component.customPresetNames = new[] { " One ", null, " Three ", " Four " };
            _component.customSaveLabel = " Save Me ";
            _component.useCustomInstallPath = true;
            _component.customInstallPath = "  Folder\\Sub  ";
            _component.useParameterExclusions = true;
            _component.excludedParameterNames = new[] { " Hat ", "", "Hat", " Mood " };

            var draft = ASMLite.Editor.ASMLiteCustomizationDraft.CaptureFromComponent(_component);
            var snapshot = draft.ToComponentSnapshot();
            var pending = draft.ToPendingSnapshot(selectedAvatar: null);

            Assert.AreEqual(4, snapshot.SlotCount);
            Assert.AreEqual(IconMode.SameColor, snapshot.IconMode);
            Assert.AreEqual(2, snapshot.SelectedGearIndex);
            Assert.AreEqual("Creator Menu", snapshot.CustomRootName);
            CollectionAssert.AreEqual(new[] { "One", string.Empty, "Three", "Four" }, snapshot.CustomPresetNames);
            Assert.AreEqual("Save Me", snapshot.CustomSaveLabel);
            Assert.AreEqual("Folder/Sub", snapshot.CustomInstallPath);
            CollectionAssert.AreEqual(new[] { "Hat", "Mood" }, snapshot.ExcludedParameterNames);
            CollectionAssert.AreEqual(snapshot.CustomPresetNames, pending.PresetNamesBySlot);
            CollectionAssert.AreEqual(snapshot.ExcludedParameterNames, pending.ExcludedParameterNames);
        }

        [Test]
        public void DraftMutators_ReconcilePendingAutomationAndMigrationSnapshots()
        {
            var draft = ASMLite.Editor.ASMLiteCustomizationDraft.CreateDefault();

            draft.SetSlotCount(5);
            draft.SetIconMode(IconMode.SameColor);
            draft.SetGearIndex(1);
            draft.SetRootNameState(enabled: true, value: "  Root  ");
            draft.SetPresetNames(new[] { " A ", "B", null, " D ", " E " }, clearLegacyFormat: true);
            draft.SetActionLabels(" Save ", " Load ", " Clear ", " Confirm ");
            draft.SetInstallPathState(useCustomInstallPath: true, customInstallPath: "  Menus\\ASM  ");
            draft.SetParameterExclusions(useParameterExclusions: true, excludedParameterNames: new[] { "Beta", " Alpha ", "Beta" });

            var migration = draft.ToComponentSnapshot();
            var pending = draft.ToPendingSnapshot(selectedAvatar: null);
            var automation = ASMLite.Editor.ASMLiteCustomizationDraft.CreateAutomationSnapshot(
                selectedAvatar: null,
                component: null,
                customization: migration,
                toolState: ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                actionHierarchy: ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                    ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                    hasComponent: false,
                    advancedDisclosureExpanded: false));

            Assert.AreEqual(5, migration.SlotCount);
            Assert.AreEqual(5, migration.CustomIcons.Length);
            Assert.AreEqual(5, pending.CustomIcons.Length);
            Assert.AreEqual(5, automation.SlotCount);
            Assert.AreEqual("sameColor", automation.IconMode);
            Assert.AreEqual("Root", pending.CustomRootName);
            CollectionAssert.AreEqual(new[] { "A", "B", string.Empty, "D", "E" }, pending.PresetNamesBySlot);
            Assert.AreEqual("Save", pending.SaveLabel);
            Assert.AreEqual("Menus/ASM", automation.CustomInstallPath);
            Assert.AreEqual("Menus/ASM", automation.EffectiveInstallPath);
            CollectionAssert.AreEqual(new[] { "Alpha", "Beta" }, automation.ExcludedParameterNames);
        }

        [Test]
        public void DraftMutators_ClearDisabledInstallPathAndParameterExclusions()
        {
            var draft = ASMLite.Editor.ASMLiteCustomizationDraft.CreateDefault();

            draft.SetInstallPathState(useCustomInstallPath: true, customInstallPath: "Custom/Menu");
            draft.SetInstallPathState(useCustomInstallPath: false, customInstallPath: "Ignored/Menu");
            draft.SetParameterExclusions(useParameterExclusions: true, excludedParameterNames: new[] { "Hat", "Mood" });
            draft.SetParameterExclusions(useParameterExclusions: false, excludedParameterNames: new[] { "Ignored" });

            var snapshot = draft.ToComponentSnapshot();
            var automation = ASMLite.Editor.ASMLiteCustomizationDraft.CreateAutomationSnapshot(
                selectedAvatar: null,
                component: null,
                customization: snapshot,
                toolState: ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                actionHierarchy: ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                    ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                    hasComponent: false,
                    advancedDisclosureExpanded: false));

            Assert.IsFalse(snapshot.UseCustomInstallPath);
            Assert.AreEqual(string.Empty, snapshot.CustomInstallPath);
            Assert.IsFalse(snapshot.UseParameterExclusions);
            CollectionAssert.IsEmpty(snapshot.ExcludedParameterNames);
            Assert.AreEqual(ASMLite.Editor.ASMLiteFullControllerInstallPathHelper.DefaultInstallPrefix, automation.EffectiveInstallPath);
            CollectionAssert.IsEmpty(automation.ExcludedParameterNames);
        }

        [Test]
        public void DraftIconFixtures_MapThroughAutomationSnapshotStableIds()
        {
            var draft = ASMLite.Editor.ASMLiteCustomizationDraft.CreateDefault();
            draft.SetSlotCount(4);
            draft.SetIconMode(IconMode.MultiColor);
            draft.SetIconFixtureTextures(
                ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveTexture(ASMLite.Editor.ASMLiteIconFixtureRegistry.RootIconId),
                new[]
                {
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveTexture(ASMLite.Editor.ASMLiteIconFixtureRegistry.Slot01IconId),
                    null,
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveTexture(ASMLite.Editor.ASMLiteIconFixtureRegistry.Slot03IconId),
                },
                ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveTexture(ASMLite.Editor.ASMLiteIconFixtureRegistry.SaveActionIconId),
                ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveTexture(ASMLite.Editor.ASMLiteIconFixtureRegistry.LoadActionIconId),
                ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveTexture(ASMLite.Editor.ASMLiteIconFixtureRegistry.ClearActionIconId));

            var automation = ASMLite.Editor.ASMLiteCustomizationDraft.CreateAutomationSnapshot(
                selectedAvatar: null,
                component: null,
                customization: draft.ToComponentSnapshot(),
                toolState: ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                actionHierarchy: ASMLite.Editor.ASMLiteWindow.BuildActionHierarchyContract(
                    ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
                    hasComponent: false,
                    advancedDisclosureExpanded: false));

            Assert.IsTrue(automation.UseCustomSlotIcons);
            Assert.AreEqual("asm-lite-icon/root", automation.RootIconFixtureId);
            CollectionAssert.AreEqual(
                new[] { "asm-lite-icon/slot-01", string.Empty, "asm-lite-icon/slot-03", string.Empty },
                automation.SlotIconFixtureIdsBySlot);
            Assert.AreEqual("custom", automation.ActionIconMode);
            Assert.AreEqual("asm-lite-icon/action-save", automation.SaveIconFixtureId);
            Assert.AreEqual("asm-lite-icon/action-load", automation.LoadIconFixtureId);
            Assert.AreEqual("asm-lite-icon/action-clear", automation.ClearIconFixtureId);
        }

        [Test]
        public void ApplyToComponent_UsesAdapterOnlyWhenDraftDiffersFromLiveComponent()
        {
            _component.slotCount = 3;
            _component.customPresetNames = new[] { "One", "Two", "Three" };

            var unchanged = ASMLite.Editor.ASMLiteCustomizationDraft.CaptureFromComponent(_component);
            var adapter = new CountingApplyAdapter();

            Assert.IsFalse(unchanged.HasDiffAgainst(_component));
            Assert.IsFalse(unchanged.ApplyToComponent(_component, adapter, "No-op"));
            Assert.AreEqual(0, adapter.RecordedObjects);
            Assert.AreEqual(0, adapter.DirtyObjects);

            unchanged.SetSlotCount(4);
            unchanged.SetPresetNames(new[] { "One", "Two", "Three", "Four" }, clearLegacyFormat: false);

            Assert.IsTrue(unchanged.HasDiffAgainst(_component));
            Assert.IsTrue(unchanged.ApplyToComponent(_component, adapter, "Apply Draft"));
            Assert.AreEqual(1, adapter.RecordedObjects);
            Assert.AreEqual(1, adapter.DirtyObjects);
            Assert.AreEqual(4, _component.slotCount);
            CollectionAssert.AreEqual(new[] { "One", "Two", "Three", "Four" }, _component.customPresetNames);
        }

        private sealed class CountingApplyAdapter : ASMLite.Editor.ASMLiteCustomizationDraft.IApplyAdapter
        {
            public int RecordedObjects { get; private set; }
            public int DirtyObjects { get; private set; }

            public void RecordObject(UnityEngine.Object target, string undoLabel)
            {
                RecordedObjects++;
            }

            public void SetDirty(UnityEngine.Object target)
            {
                DirtyObjects++;
            }
        }
    }
}
