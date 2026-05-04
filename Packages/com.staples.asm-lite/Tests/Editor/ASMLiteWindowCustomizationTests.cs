using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace VF.Model.Feature
{
    [System.Serializable]
    internal class FullControllerLikeParamsRef
    {
        public VRCExpressionParameters objRef;
        public string id;
    }

    [System.Serializable]
    internal class FullControllerLikePrmsEntry
    {
        public FullControllerLikeParamsRef parameters = new FullControllerLikeParamsRef();
    }

    [System.Serializable]
    internal class FullControllerLike
    {
        public FullControllerLikePrmsEntry[] prms;
    }

    [System.Serializable]
    internal class MoveMenuItem
    {
        public string fromPath;
        public string toPath;
    }
}

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowCustomizationTests
    {
        private AsmLiteTestContext _ctx;
        private AsmLiteTestContext _ctxAlt;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            _ctxAlt = ASMLiteTestFixtures.CreateTestAvatar();
            _ctxAlt.AvatarGo.name = "TestAvatarAlt";
            Selection.activeGameObject = null;
            ASMLiteTestFixtures.ResetGeneratedExprParams();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeGameObject = null;
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            ASMLiteTestFixtures.TearDownTestAvatar(_ctxAlt?.AvatarGo);
            _ctx = null;
            _ctxAlt = null;
        }

        [Test]
        public void SelectingAvatar_LoadsPersistedCustomizationFromComponent()
        {
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Reopen Root  ";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "  ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { " HatVisible ", "", "HatVisible", "Mood" };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.AreSame(_ctx.AvDesc, snapshot.SelectedAvatar);
                Assert.IsTrue(snapshot.UseCustomRootIcon);
                Assert.IsTrue(snapshot.UseCustomRootName);
                Assert.IsTrue(snapshot.UseCustomInstallPath);
                Assert.IsTrue(snapshot.UseParameterExclusions);
                Assert.AreEqual("Reopen Root", snapshot.CustomRootName);
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath);
                CollectionAssert.AreEqual(new[] { "HatVisible", "Mood" }, snapshot.ExcludedParameterNames);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingDifferentAvatar_RefreshesCustomizationSnapshot()
        {
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "Primary";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "Packages/Primary";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "PrimaryParam" };

            _ctxAlt.Comp.useCustomRootIcon = false;
            _ctxAlt.Comp.useCustomRootName = true;
            _ctxAlt.Comp.customRootName = "  Alternate Root  ";
            _ctxAlt.Comp.useCustomInstallPath = true;
            _ctxAlt.Comp.customInstallPath = "   ";
            _ctxAlt.Comp.useParameterExclusions = true;
            _ctxAlt.Comp.excludedParameterNames = new[] { "Alt", "Alt", " Mood " };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SelectAvatarForAutomation(_ctxAlt.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.AreSame(_ctxAlt.AvDesc, snapshot.SelectedAvatar);
                Assert.IsFalse(snapshot.UseCustomRootIcon);
                Assert.IsTrue(snapshot.UseCustomRootName);
                Assert.AreEqual("Alternate Root", snapshot.CustomRootName);
                Assert.IsTrue(snapshot.UseCustomInstallPath);
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath);
                CollectionAssert.AreEqual(new[] { "Alt", "Mood" }, snapshot.ExcludedParameterNames);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(8)]
        public void SetSlotCountForAutomation_UpdatesPendingCustomizationSnapshot(int slotCount)
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetSlotCountForAutomation(slotCount);

                var snapshot = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.AreEqual(slotCount, snapshot.SlotCount,
                    "The automation slot-count seam should immediately surface min, max, and non-default middle values through the pending customization snapshot.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PendingCustomizationSnapshotForAutomation_ReportsSelectedAvatarSlotCount()
        {
            _ctx.Comp.slotCount = 7;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.AreEqual(7, snapshot.SlotCount,
                    "Selecting an attached avatar should copy the component slot count into the pending automation snapshot contract.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NamingAutomation_DefaultGateOff_LeavesEffectiveNamesAtDefaults()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetRootNameStateForAutomation(false, "  Ignored Root  ");

                AssertNamingSnapshotFields(
                    "default gate-off row",
                    window,
                    _ctx.Comp,
                    3,
                    useCustomRootName: false,
                    expectedRootName: string.Empty,
                    expectedPresetNames: EmptyStrings(3),
                    expectedSaveLabel: string.Empty,
                    expectedLoadLabel: string.Empty,
                    expectedClearLabel: string.Empty,
                    expectedConfirmLabel: string.Empty);
                AssertEffectiveNaming(
                    "default gate-off row",
                    _ctx.Comp,
                    3,
                    ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    DefaultPresetLabels(3),
                    ASMLite.Editor.ASMLiteBuilder.DefaultSaveLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultLoadLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultClearPresetLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultConfirmLabel);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NamingAutomation_RootOnly_UpdatesRootAcrossPendingAndAttachedSnapshots()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetRootNameStateForAutomation(true, "  Creator Settings  ");

                AssertNamingSnapshotFields(
                    "root-only row",
                    window,
                    _ctx.Comp,
                    3,
                    useCustomRootName: true,
                    expectedRootName: "Creator Settings",
                    expectedPresetNames: EmptyStrings(3),
                    expectedSaveLabel: string.Empty,
                    expectedLoadLabel: string.Empty,
                    expectedClearLabel: string.Empty,
                    expectedConfirmLabel: string.Empty);
                AssertEffectiveNaming(
                    "root-only row",
                    _ctx.Comp,
                    3,
                    "Creator Settings",
                    DefaultPresetLabels(3),
                    ASMLite.Editor.ASMLiteBuilder.DefaultSaveLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultLoadLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultClearPresetLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultConfirmLabel);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NamingAutomation_FirstPresetOnly_UpdatesOnlyFirstPresetName()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetRootNameStateForAutomation(true, ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName);
                window.SetPresetNameMaskForAutomation(new Dictionary<int, string>
                {
                    { 1, "  Casual Fit  " },
                }, clearExisting: true);

                AssertNamingSnapshotFields(
                    "first-preset-only row",
                    window,
                    _ctx.Comp,
                    3,
                    useCustomRootName: true,
                    expectedRootName: ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    expectedPresetNames: new[] { "Casual Fit", string.Empty, string.Empty },
                    expectedSaveLabel: string.Empty,
                    expectedLoadLabel: string.Empty,
                    expectedClearLabel: string.Empty,
                    expectedConfirmLabel: string.Empty);
                AssertEffectiveNaming(
                    "first-preset-only row",
                    _ctx.Comp,
                    3,
                    ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    new[] { "Casual Fit", "Preset 2", "Preset 3" },
                    ASMLite.Editor.ASMLiteBuilder.DefaultSaveLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultLoadLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultClearPresetLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultConfirmLabel);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NamingAutomation_AllPresets_UpdatesEveryPresetName()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 4;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetRootNameStateForAutomation(true, ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName);
                window.SetPresetNameMaskForAutomation(new[]
                {
                    "  Day  ",
                    "Night",
                    " Party ",
                    "Quest",
                }, clearExisting: true);

                var expectedPresets = new[] { "Day", "Night", "Party", "Quest" };
                AssertNamingSnapshotFields(
                    "all-presets row",
                    window,
                    _ctx.Comp,
                    4,
                    useCustomRootName: true,
                    expectedRootName: ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    expectedPresetNames: expectedPresets,
                    expectedSaveLabel: string.Empty,
                    expectedLoadLabel: string.Empty,
                    expectedClearLabel: string.Empty,
                    expectedConfirmLabel: string.Empty);
                AssertEffectiveNaming(
                    "all-presets row",
                    _ctx.Comp,
                    4,
                    ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    expectedPresets,
                    ASMLite.Editor.ASMLiteBuilder.DefaultSaveLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultLoadLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultClearPresetLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultConfirmLabel);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NamingAutomation_SaveOnlyActionLabel_UpdatesOnlySaveLabel()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetRootNameStateForAutomation(true, ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName);
                window.SetActionLabelMaskForAutomation(new Dictionary<string, string>
                {
                    { "save", "  Store  " },
                }, clearExisting: true);

                AssertNamingSnapshotFields(
                    "save-only action-label row",
                    window,
                    _ctx.Comp,
                    3,
                    useCustomRootName: true,
                    expectedRootName: ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    expectedPresetNames: EmptyStrings(3),
                    expectedSaveLabel: "Store",
                    expectedLoadLabel: string.Empty,
                    expectedClearLabel: string.Empty,
                    expectedConfirmLabel: string.Empty);
                AssertEffectiveNaming(
                    "save-only action-label row",
                    _ctx.Comp,
                    3,
                    ASMLite.Editor.ASMLiteBuilder.DefaultRootControlName,
                    DefaultPresetLabels(3),
                    "Store",
                    ASMLite.Editor.ASMLiteBuilder.DefaultLoadLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultClearPresetLabel,
                    ASMLite.Editor.ASMLiteBuilder.DefaultConfirmLabel);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NamingAutomation_FullPack_UpdatesRootPresetAndActionLabels()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetRootNameStateForAutomation(true, "  Creator Controls  ");
                window.SetPresetNameMaskForAutomation(new[]
                {
                    " Work ",
                    "Play",
                    " Sleep ",
                }, clearExisting: true);
                window.SetActionLabelMaskForAutomation(
                    saveLabel: " Commit ",
                    loadLabel: " Recall ",
                    clearLabel: " Reset Slot ",
                    confirmLabel: " Apply It ",
                    clearExisting: true);

                var expectedPresets = new[] { "Work", "Play", "Sleep" };
                AssertNamingSnapshotFields(
                    "full-pack row",
                    window,
                    _ctx.Comp,
                    3,
                    useCustomRootName: true,
                    expectedRootName: "Creator Controls",
                    expectedPresetNames: expectedPresets,
                    expectedSaveLabel: "Commit",
                    expectedLoadLabel: "Recall",
                    expectedClearLabel: "Reset Slot",
                    expectedConfirmLabel: "Apply It");
                AssertEffectiveNaming(
                    "full-pack row",
                    _ctx.Comp,
                    3,
                    "Creator Controls",
                    expectedPresets,
                    "Commit",
                    "Recall",
                    "Reset Slot",
                    "Apply It");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_MultiColorDefault_ReportsBuiltInModeWithNoCustomFixtures()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.MultiColor;
            _ctx.Comp.selectedGearIndex = 0;
            _ctx.Comp.useCustomSlotIcons = false;
            _ctx.Comp.customIcons = Array.Empty<Texture2D>();
            _ctx.Comp.actionIconMode = ActionIconMode.Default;
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = null;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                AssertBuiltinIconSnapshotFields(
                    "multicolor default row",
                    window,
                    _ctx.Comp,
                    4,
                    IconMode.MultiColor,
                    expectedGearIndex: 0);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(0, "same-color blue row")]
        [TestCase(7, "same-color yellow row")]
        public void IconAutomation_SameColor_ReportsSelectedGearIndexWithoutCustomFixtures(int gearIndex, string scenario)
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.SameColor;
            _ctx.Comp.selectedGearIndex = gearIndex;
            _ctx.Comp.useCustomSlotIcons = false;
            _ctx.Comp.customIcons = Array.Empty<Texture2D>();
            _ctx.Comp.actionIconMode = ActionIconMode.Default;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                AssertBuiltinIconSnapshotFields(
                    scenario,
                    window,
                    _ctx.Comp,
                    4,
                    IconMode.SameColor,
                    gearIndex);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_RootOnly_UpdatesOnlyRootIconFixture()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetIconFixturesForAutomation(
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.RootIconId,
                    EmptyFixtureIds(3),
                    saveIconFixtureId: null,
                    loadIconFixtureId: null,
                    clearIconFixtureId: null);

                AssertCustomIconSnapshotFields(
                    "root-only icon row",
                    window,
                    _ctx.Comp,
                    3,
                    expectedRootIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.RootIconId,
                    expectedSlotIconFixtureIds: EmptyFixtureIds(3),
                    expectedSaveIconFixtureId: string.Empty,
                    expectedLoadIconFixtureId: string.Empty,
                    expectedClearIconFixtureId: string.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_FirstSlotOnly_UpdatesOnlyFirstSlotIconFixture()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var expectedSlots = new[]
                {
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.Slot01IconId,
                    string.Empty,
                    string.Empty,
                };

                window.SetIconFixturesForAutomation(
                    rootIconFixtureId: null,
                    slotIconFixtureIds: expectedSlots,
                    saveIconFixtureId: null,
                    loadIconFixtureId: null,
                    clearIconFixtureId: null);

                AssertCustomIconSnapshotFields(
                    "first-slot-only icon row",
                    window,
                    _ctx.Comp,
                    3,
                    expectedRootIconFixtureId: string.Empty,
                    expectedSlotIconFixtureIds: expectedSlots,
                    expectedSaveIconFixtureId: string.Empty,
                    expectedLoadIconFixtureId: string.Empty,
                    expectedClearIconFixtureId: string.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_AllSlots_UpdatesEverySlotIconFixture()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 4;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var expectedSlots = SlotFixtureIds(4);

                window.SetIconFixturesForAutomation(
                    rootIconFixtureId: null,
                    slotIconFixtureIds: expectedSlots,
                    saveIconFixtureId: null,
                    loadIconFixtureId: null,
                    clearIconFixtureId: null);

                AssertCustomIconSnapshotFields(
                    "all-slot-icons row",
                    window,
                    _ctx.Comp,
                    4,
                    expectedRootIconFixtureId: string.Empty,
                    expectedSlotIconFixtureIds: expectedSlots,
                    expectedSaveIconFixtureId: string.Empty,
                    expectedLoadIconFixtureId: string.Empty,
                    expectedClearIconFixtureId: string.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_SaveOnlyAction_UpdatesOnlySaveActionIconFixture()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetIconFixturesForAutomation(
                    rootIconFixtureId: null,
                    slotIconFixtureIds: EmptyFixtureIds(3),
                    saveIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.SaveActionIconId,
                    loadIconFixtureId: null,
                    clearIconFixtureId: null);

                AssertCustomIconSnapshotFields(
                    "save-only action-icon row",
                    window,
                    _ctx.Comp,
                    3,
                    expectedRootIconFixtureId: string.Empty,
                    expectedSlotIconFixtureIds: EmptyFixtureIds(3),
                    expectedSaveIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.SaveActionIconId,
                    expectedLoadIconFixtureId: string.Empty,
                    expectedClearIconFixtureId: string.Empty);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_FullPack_UpdatesRootSlotAndActionIconFixtures()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var expectedSlots = SlotFixtureIds(3);

                window.SetIconFixturesForAutomation(
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.RootIconId,
                    expectedSlots,
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.SaveActionIconId,
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.LoadActionIconId,
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.ClearActionIconId);

                AssertCustomIconSnapshotFields(
                    "full-icon-pack row",
                    window,
                    _ctx.Comp,
                    3,
                    expectedRootIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.RootIconId,
                    expectedSlotIconFixtureIds: expectedSlots,
                    expectedSaveIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.SaveActionIconId,
                    expectedLoadIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.LoadActionIconId,
                    expectedClearIconFixtureId: ASMLite.Editor.ASMLiteIconFixtureRegistry.ClearActionIconId);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void IconAutomation_UnknownFixtureId_ThrowsBeforeChangingAttachedIconState()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.slotCount = 3;
                _ctx.Comp.iconMode = IconMode.MultiColor;
                _ctx.Comp.selectedGearIndex = 0;
                _ctx.Comp.useCustomSlotIcons = false;
                _ctx.Comp.customIcons = Array.Empty<Texture2D>();
                _ctx.Comp.actionIconMode = ActionIconMode.Default;
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                var ex = Assert.Throws<ArgumentException>(() => window.SetIconFixturesForAutomation(
                    ASMLite.Editor.ASMLiteIconFixtureRegistry.RootIconId,
                    new[] { "asm-lite-icon/not-real", string.Empty, string.Empty },
                    saveIconFixtureId: null,
                    loadIconFixtureId: null,
                    clearIconFixtureId: null));

                StringAssert.Contains("asm-lite-icon/not-real", ex.Message,
                    "The icon automation seam should report the unknown fixture ID without exposing raw asset-path dependencies.");
                AssertBuiltinIconSnapshotFields(
                    "unknown-fixture failure row",
                    window,
                    _ctx.Comp,
                    3,
                    IconMode.MultiColor,
                    expectedGearIndex: 0);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ParameterBackupAutomation_DefaultSelectionKeepsBackupCustomizationDisabled()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                AssertParameterBackupSnapshotFields(
                    "default disabled backup row",
                    window,
                    _ctx.Comp,
                    expectedEnabled: false,
                    expectedExcludedNames: Array.Empty<string>());
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ParameterBackupAutomation_NoneExcludedPreset_EnablesCustomizationWithEmptyExclusions()
        {
            var referencedParams = SeedParameterBackupPresetVisibleOptions(_ctx);
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                AssertParameterBackupPresetVisibleOptions(_ctx, "none-excluded backup row");
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetParameterBackupPresetForAutomation(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.NoneExcludedPresetId);

                AssertParameterBackupSnapshotFields(
                    "none-excluded backup row",
                    window,
                    _ctx.Comp,
                    expectedEnabled: true,
                    expectedExcludedNames: Array.Empty<string>());
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void ParameterBackupAutomation_SingleVisiblePreset_StoresFirstVisibleExclusion()
        {
            var referencedParams = SeedParameterBackupPresetVisibleOptions(_ctx);
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                AssertParameterBackupPresetVisibleOptions(_ctx, "single-visible backup row");
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetParameterBackupPresetForAutomation(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleVisiblePresetId);

                AssertParameterBackupSnapshotFields(
                    "single-visible backup row",
                    window,
                    _ctx.Comp,
                    expectedEnabled: true,
                    expectedExcludedNames: new[] { "Fixture/Referenced/A" });
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void ParameterBackupAutomation_FirstTwoVisiblePreset_StoresFirstTwoVisibleOptions()
        {
            var referencedParams = SeedParameterBackupPresetVisibleOptions(_ctx);
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                AssertParameterBackupPresetVisibleOptions(_ctx, "first-two-visible backup row");
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetParameterBackupPresetForAutomation(ASMLite.Editor.ASMLiteParameterBackupPresetResolver.FirstTwoVisiblePresetId);

                AssertParameterBackupSnapshotFields(
                    "first-two-visible backup row",
                    window,
                    _ctx.Comp,
                    expectedEnabled: true,
                    expectedExcludedNames: new[] { "Fixture/Referenced/A", "Fixture/Referenced/B" });
                CollectionAssert.DoesNotContain(_ctx.Comp.excludedParameterNames, "Fixture/Referenced/C",
                    "The first-two-visible preset should exclude only the first two sorted visible options, not every visible option under the same branch.");
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void ParameterBackupAutomation_ExactVisibleNames_NormalizesAndStoresSortedSnapshot()
        {
            var referencedParams = SeedParameterBackupPresetVisibleOptions(_ctx);
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                AssertParameterBackupPresetVisibleOptions(_ctx, "sorted exact-name backup row");
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetParameterBackupExclusionsForAutomation(
                    enabled: true,
                    exactVisibleNames: new[]
                    {
                        " Fixture\\Referenced//B ",
                        "Fixture/Source/One ",
                        "Fixture/ Referenced / A",
                    });

                AssertParameterBackupSnapshotFields(
                    "sorted exact-name backup row",
                    window,
                    _ctx.Comp,
                    expectedEnabled: true,
                    expectedExcludedNames: new[] { "Fixture/Referenced/A", "Fixture/Referenced/B", "Fixture/Source/One" });
            }
            finally
            {
                Object.DestroyImmediate(window);
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void VisibleParameterBackupOptions_IncludeAssignedPrefabToggleGlobals_PreBake()
        {
            var limbRoot = new GameObject("FixtureSource");
            limbRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var arms = new GameObject("Arms");
            arms.transform.SetParent(limbRoot.transform, false);

            var vf = arms.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Fixture/Source/One",
                name = "Fixture Source One",
                menuPath = "",
            };

            string[] backable = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.Contains(backable, "Fixture/Source/One",
                "Assigned VRCFury globals under nested prefab-style hierarchy should remain visible in the parameter backup checklist before bake.");
        }

        [Test]
        public void ParameterBackupAutomation_PresetId_EnablesExclusionsAndSurfacesNormalizedSnapshotFields()
        {
            var limbRoot = new GameObject("FixtureSource");
            limbRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var arms = new GameObject("Arms");
            arms.transform.SetParent(limbRoot.transform, false);

            var vf = arms.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Fixture/Source/One",
                name = "Fixture Source One",
                menuPath = string.Empty,
            };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                string[] visibleOptions = window.GetVisibleParameterBackupOptionsForAutomation();
                CollectionAssert.Contains(visibleOptions, "Fixture/Source/One",
                    "The automation seam should expose the same normalized visible backup options as the checklist/testing surface.");

                CollectionAssert.Contains(
                    window.GetParameterBackupPresetIdsForAutomation(),
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleVisiblePresetId,
                    "Automation should expose stable preset IDs from the shared parameter-backup preset resolver.");

                window.SetParameterBackupStateForAutomation(
                    true,
                    ASMLite.Editor.ASMLiteParameterBackupPresetResolver.SingleVisiblePresetId);

                Assert.IsTrue(_ctx.Comp.useParameterExclusions,
                    "Preset application should enable parameter backup customization on the attached component.");
                CollectionAssert.AreEqual(new[] { "Fixture/Source/One" }, _ctx.Comp.excludedParameterNames,
                    "Preset application should resolve to exact visible option names before writing component exclusions.");

                var pending = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(pending.UseParameterExclusions,
                    "The pending testing snapshot should expose parameter backup enablement.");
                CollectionAssert.AreEqual(new[] { "Fixture/Source/One" }, pending.ExcludedParameterNames,
                    "The pending testing snapshot should expose sorted, normalized excluded parameter names.");

                var automation = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.IsTrue(automation.UseParameterExclusions,
                    "The normalized automation snapshot should expose parameter backup enablement.");
                CollectionAssert.AreEqual(new[] { "Fixture/Source/One" }, automation.ExcludedParameterNames,
                    "The normalized automation snapshot should expose sorted, normalized excluded parameter names.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ParameterBackupAutomation_ExactNames_NormalizesSortsAndCanDisableDeterministically()
        {
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "Zed/Param",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "Alpha/Param",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetParameterBackupExclusionsForAutomation(new[]
                {
                    " Zed / Param ",
                    "Alpha/Param",
                    "Alpha/Param",
                });

                string[] expectedExcluded = { "Alpha/Param", "Zed/Param" };
                Assert.IsTrue(_ctx.Comp.useParameterExclusions,
                    "Exact-name application should enable parameter backup customization on the attached component.");
                CollectionAssert.AreEqual(expectedExcluded, _ctx.Comp.excludedParameterNames,
                    "Exact-name application should normalize, de-duplicate, and sort excluded parameter names before persistence.");
                CollectionAssert.AreEqual(expectedExcluded, window.GetPendingCustomizationSnapshotForAutomation().ExcludedParameterNames,
                    "Automation snapshots should keep excluded parameter names sorted for deterministic comparisons.");

                window.SetParameterBackupStateForAutomation(false);

                Assert.IsFalse(_ctx.Comp.useParameterExclusions,
                    "Disabling through automation should deterministically clear the component gate.");
                CollectionAssert.IsEmpty(_ctx.Comp.excludedParameterNames,
                    "Disabling through automation should clear stale excluded names for deterministic reruns.");

                var disabledSnapshot = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.IsFalse(disabledSnapshot.UseParameterExclusions,
                    "Disabled automation snapshots should expose the parameter backup gate as false.");
                CollectionAssert.IsEmpty(disabledSnapshot.ExcludedParameterNames,
                    "Disabled automation snapshots should not retain stale excluded-name entries.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void VisibleParameterBackupOptions_PreferDeterministicToggleAlias_OverLegacySourceName()
        {
            const string legacySource = "VF300_Clothing/Rezz";

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = legacySource,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = legacySource,
                menuPath = "Clothing/Rezz",
                name = "Rezz",
            };

            string[] backable = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

            string deterministic = ASMLite.Editor.ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                "Clothing/Rezz",
                _ctx.AvatarGo.name,
                new HashSet<string>(StringComparer.Ordinal));

            CollectionAssert.DoesNotContain(backable, legacySource,
                "The parameter backup checklist should hide stale legacy toggle names when ASM-Lite will rebind that toggle to a deterministic alias.");
            CollectionAssert.Contains(backable, deterministic,
                "The parameter backup checklist should show the deterministic alias that ASM-Lite will actually back up after enrollment.");
        }

        [Test]
        public void VisibleParameterBackupOptions_IncludeVrcFuryReferencedParameterAssets_PreBake()
        {
            var mediaRoot = new GameObject("Media");
            mediaRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = mediaRoot.AddComponent<VF.Model.VRCFury>();
            var referencedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            referencedParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "Fixture/Referenced/A",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "Fixture/Referenced/B",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
            };

            try
            {
                vf.content = new VF.Model.Feature.FullControllerLike
                {
                    prms = new[]
                    {
                        new VF.Model.Feature.FullControllerLikePrmsEntry
                        {
                            parameters = new VF.Model.Feature.FullControllerLikeParamsRef
                            {
                                objRef = referencedParams,
                                id = string.Empty,
                            },
                        },
                    },
                };

                string[] backable = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

                CollectionAssert.Contains(backable, "Fixture/Referenced/A",
                    "Referenced parameter assets should contribute visible parameter backup options before bake.");
                CollectionAssert.Contains(backable, "Fixture/Referenced/B",
                    "Supported parameter types from referenced parameter assets should remain visible before bake.");
            }
            finally
            {
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void VisibleInstallPathOptions_ReflectMoveMenuDestinations()
        {
            var rootMenu = CreateTempMenuAsset("MoveMenuRoot");
            var userSubmenu = CreateTempMenuAsset("MoveMenuUserSubmenu");
            _ctx.AvDesc.expressionsMenu = rootMenu;
            rootMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Unrelated",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = userSubmenu,
                },
            };
            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Source Bucket",
                toPath = "Destination/Source Bucket",
            };

            string[] paths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.DoesNotContain(paths, "Source Bucket",
                "The install-path picker should not offer stale move-menu source paths.");
            CollectionAssert.DoesNotContain(paths, "Source Bucket/Submenu",
                "The install-path picker should not offer stale descendants under a moved source path.");
            CollectionAssert.Contains(paths, "Destination",
                "The install-path picker should offer the destination parent created by a move-menu remap.");
            CollectionAssert.Contains(paths, "Destination/Source Bucket",
                "The install-path picker should offer the remapped destination path.");
            CollectionAssert.Contains(paths, "Unrelated",
                "The install-path picker should keep unrelated user submenu paths visible.");
        }

        [Test]
        public void VisibleInstallPathOptions_IncludeMoveMenuDestinationHierarchy()
        {
            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            const string destinationPath = "Root Bucket/Feature Node/Leaf Group";
            var expectedPrefixes = new[]
            {
                "Root Bucket",
                "Root Bucket/Feature Node",
                "Root Bucket/Feature Node/Leaf Group",
            };

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Legacy Bucket/Feature Node/Leaf Group",
                toPath = destinationPath,
            };

            string[] paths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            foreach (var expected in expectedPrefixes)
            {
                CollectionAssert.Contains(paths, expected,
                    "The install-path picker should expose each parent segment of a move-menu destination hierarchy.");
            }
        }

        [Test]
        public void VisibleInstallPathOptions_ExcludeAsmLitePresetsBranchAcrossRootNameReloads()
        {
            var rootMenu = CreateTempMenuAsset("RootMenu");
            _ctx.AvDesc.expressionsMenu = rootMenu;

            var userSubmenu = CreateTempMenuAsset("UserSubmenu");
            var nestedSubmenu = CreateTempMenuAsset("NestedSubmenu");
            Assert.IsNotNull(userSubmenu,
                "Regression setup requires a persisted user submenu asset.");
            Assert.IsNotNull(nestedSubmenu,
                "Regression setup requires a persisted nested submenu asset.");
            userSubmenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Hats",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = nestedSubmenu,
                },
            };
            nestedSubmenu.controls = new List<VRCExpressionsMenu.Control>();
            EditorUtility.SetDirty(userSubmenu);
            EditorUtility.SetDirty(nestedSubmenu);
            AssetDatabase.SaveAssets();

            var asmLitePresetsMenu = LoadAsmLitePresetsMenu();
            rootMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Creator Settings",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = asmLitePresetsMenu,
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Accessories",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = userSubmenu,
                },
            };
            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            string[] firstPaths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.DoesNotContain(firstPaths, "Creator Settings",
                "ASM-Lite's injected presets branch must not be offered as a custom install destination when the root menu has a custom name.");
            CollectionAssert.Contains(firstPaths, "Accessories",
                "Non-ASM-Lite submenu roots should remain available as install destinations.");
            CollectionAssert.Contains(firstPaths, "Accessories/Hats",
                "Non-ASM-Lite submenu descendants should remain available as install destinations.");

            rootMenu.controls[0] = new VRCExpressionsMenu.Control
            {
                name = "Settings Manager",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = asmLitePresetsMenu,
            };
            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            string[] reloadedPaths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.DoesNotContain(reloadedPaths, "Creator Settings",
                "Reloaded install-path options must not retain stale custom ASM-Lite root names after the root name is reset.");
            CollectionAssert.DoesNotContain(reloadedPaths, "Settings Manager",
                "Reloaded install-path options must also exclude the default ASM-Lite root branch itself.");
            CollectionAssert.AreEquivalent(new[] { "Accessories", "Accessories/Hats" }, reloadedPaths,
                "Reloaded install-path options should contain only real user menu paths after ASM-Lite branches are filtered out.");
        }

        [Test]
        public void SelectingAvatar_AdoptsMoveMenuInstallPathAndRemovesMoveComponent()
        {
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = string.Empty;
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = string.Empty;

            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Settings Manager",
                toPath = "Tools/Settings Manager",
            };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                Assert.IsTrue(_ctx.Comp.useCustomInstallPath,
                    "Move-menu migration should enable custom install path on the ASM-Lite component.");
                Assert.AreEqual("Tools", _ctx.Comp.customInstallPath,
                    "Move-menu migration should adopt destination parent as install prefix.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Visible customization state should mirror adopted install-path enablement.");
                Assert.AreEqual("Tools", snapshot.CustomInstallPath,
                    "Visible customization state should mirror the adopted install-path prefix.");

                int remainingMoveComponents = _ctx.AvatarGo
                    .GetComponentsInChildren<VF.Model.VRCFury>(true)
                    .Count(c => c != null
                        && c.content is VF.Model.Feature.MoveMenuItem move
                        && string.Equals(move.fromPath, "Settings Manager", StringComparison.Ordinal)
                        && string.Equals(move.toPath, "Tools/Settings Manager", StringComparison.Ordinal));

                Assert.AreEqual(0, remainingMoveComponents,
                    "Move-menu migration should remove the consumed MoveMenuItem component to avoid duplicate routing.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingAvatar_DoesNotAdoptMalformedMoveMenuDestinationOrRemoveHelper()
        {
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = string.Empty;
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = string.Empty;

            var moveMenuRoot = new GameObject("MalformedMoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Settings Manager",
                toPath = "Tools/BrokenDestination",
            };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                Assert.IsFalse(_ctx.Comp.useCustomInstallPath,
                    "Malformed move-menu destinations must fail closed without enabling custom install path on the ASM-Lite component.");
                Assert.AreEqual(string.Empty, _ctx.Comp.customInstallPath,
                    "Malformed move-menu destinations must fail closed without writing an adopted install prefix.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsFalse(snapshot.UseCustomInstallPath,
                    "Visible customization state should remain unchanged when move-menu adoption rejects a malformed destination.");
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath,
                    "Visible customization state should not surface any adopted prefix when move-menu adoption rejects a malformed destination.");
                Assert.IsTrue(moveMenuRoot.TryGetComponent<VF.Model.VRCFury>(out _),
                    "Malformed move-menu destinations must not remove the legacy helper when install-prefix resolution fails.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private static void AssertParameterBackupSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow window,
            ASMLite.ASMLiteComponent component,
            bool expectedEnabled,
            string[] expectedExcludedNames)
        {
            Assert.IsNotNull(window, $"{scenario}: expected an automation window.");
            Assert.IsNotNull(component, $"{scenario}: expected an attached ASM-Lite component.");
            expectedExcludedNames = expectedExcludedNames ?? Array.Empty<string>();

            Assert.AreEqual(expectedEnabled, component.useParameterExclusions,
                $"{scenario}: attached component should expose the parameter-backup customization gate.");
            CollectionAssert.AreEqual(expectedExcludedNames, component.excludedParameterNames ?? Array.Empty<string>(),
                $"{scenario}: attached component should store the exact sorted backup exclusion snapshot.");

            var pendingTesting = window.GetPendingCustomizationSnapshotForTesting();
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), pendingTesting.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the pending testing snapshot.");
            Assert.AreEqual(expectedEnabled, pendingTesting.UseParameterExclusions,
                $"{scenario}: pending testing snapshot should expose the parameter-backup customization gate.");
            CollectionAssert.AreEqual(expectedExcludedNames, pendingTesting.ExcludedParameterNames,
                $"{scenario}: pending testing snapshot should preserve sorted backup exclusions.");

            AssertParameterBackupAutomationSnapshotFields(
                scenario + " pending automation snapshot",
                window.GetPendingCustomizationSnapshotForAutomation(),
                component,
                expectedEnabled,
                expectedExcludedNames);
            AssertParameterBackupAutomationSnapshotFields(
                scenario + " attached automation snapshot",
                window.GetAttachedCustomizationSnapshotForAutomation(),
                component,
                expectedEnabled,
                expectedExcludedNames);
        }

        private static void AssertParameterBackupAutomationSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow.CustomizationAutomationSnapshot snapshot,
            ASMLite.ASMLiteComponent component,
            bool expectedEnabled,
            string[] expectedExcludedNames)
        {
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), snapshot.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the automation snapshot.");
            Assert.AreSame(component, snapshot.Component,
                $"{scenario}: automation snapshot should expose the attached ASM-Lite component.");
            Assert.IsTrue(snapshot.HasAttachedComponent,
                $"{scenario}: automation snapshot should identify the attached component branch.");
            Assert.AreEqual(expectedEnabled, snapshot.Customization.UseParameterExclusions,
                $"{scenario}: automation snapshot should expose the parameter-backup customization gate.");
            CollectionAssert.AreEqual(expectedExcludedNames, snapshot.Customization.ExcludedParameterNames,
                $"{scenario}: automation snapshot should preserve sorted backup exclusions.");
        }

        private static VRCExpressionParameters SeedParameterBackupPresetVisibleOptions(AsmLiteTestContext ctx)
        {
            var limbRoot = new GameObject("FixtureSource");
            limbRoot.transform.SetParent(ctx.AvatarGo.transform, false);

            var arms = new GameObject("Arms");
            arms.transform.SetParent(limbRoot.transform, false);

            var limbVf = arms.AddComponent<VF.Model.VRCFury>();
            limbVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Fixture/Source/One",
                name = "Fixture Source One",
                menuPath = string.Empty,
            };

            var mediaRoot = new GameObject("Media");
            mediaRoot.transform.SetParent(ctx.AvatarGo.transform, false);

            var mediaVf = mediaRoot.AddComponent<VF.Model.VRCFury>();
            var referencedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            referencedParams.parameters = new[]
            {
                CreateParameterBackupPresetParameter("Fixture/Referenced/A", VRCExpressionParameters.ValueType.Bool),
                CreateParameterBackupPresetParameter("Fixture/Referenced/B", VRCExpressionParameters.ValueType.Float),
                CreateParameterBackupPresetParameter("Fixture/Referenced/C", VRCExpressionParameters.ValueType.Int),
            };

            mediaVf.content = new VF.Model.Feature.FullControllerLike
            {
                prms = new[]
                {
                    new VF.Model.Feature.FullControllerLikePrmsEntry
                    {
                        parameters = new VF.Model.Feature.FullControllerLikeParamsRef
                        {
                            objRef = referencedParams,
                            id = string.Empty,
                        },
                    },
                },
            };

            return referencedParams;
        }

        private static VRCExpressionParameters.Parameter CreateParameterBackupPresetParameter(
            string name,
            VRCExpressionParameters.ValueType valueType)
        {
            return new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = valueType,
                defaultValue = 0f,
                saved = false,
                networkSynced = false,
            };
        }

        private static void AssertParameterBackupPresetVisibleOptions(AsmLiteTestContext ctx, string scenario)
        {
            string[] visibleOptions = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(ctx.AvDesc);

            CollectionAssert.Contains(visibleOptions, "Fixture/Source/One",
                $"{scenario}: assigned prefab toggle globals must be visible before resolving backup presets.");
            CollectionAssert.Contains(visibleOptions, "Fixture/Referenced/A",
                $"{scenario}: referenced fixture parameter A must be visible before resolving backup presets.");
            CollectionAssert.Contains(visibleOptions, "Fixture/Referenced/B",
                $"{scenario}: referenced fixture parameter B must be visible before resolving backup presets.");
            CollectionAssert.Contains(visibleOptions, "Fixture/Referenced/C",
                $"{scenario}: regression setup should include an extra visible nested sibling so preset tests prove subset behavior.");
        }

        private static void AssertNamingSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow window,
            ASMLite.ASMLiteComponent component,
            int expectedSlotCount,
            bool useCustomRootName,
            string expectedRootName,
            string[] expectedPresetNames,
            string expectedSaveLabel,
            string expectedLoadLabel,
            string expectedClearLabel,
            string expectedConfirmLabel)
        {
            Assert.IsNotNull(window, $"{scenario}: expected an automation window.");
            Assert.IsNotNull(component, $"{scenario}: expected an attached ASM-Lite component.");

            AssertNamingSnapshotFields(
                scenario + " pending testing snapshot",
                window.GetPendingCustomizationSnapshotForTesting(),
                component,
                expectedSlotCount,
                useCustomRootName,
                expectedRootName,
                expectedPresetNames,
                expectedSaveLabel,
                expectedLoadLabel,
                expectedClearLabel,
                expectedConfirmLabel);
            AssertNamingSnapshotFields(
                scenario + " pending automation snapshot",
                window.GetPendingCustomizationSnapshotForAutomation(),
                component,
                expectedSlotCount,
                useCustomRootName,
                expectedRootName,
                expectedPresetNames,
                expectedSaveLabel,
                expectedLoadLabel,
                expectedClearLabel,
                expectedConfirmLabel);
            AssertNamingSnapshotFields(
                scenario + " attached automation snapshot",
                window.GetAttachedCustomizationSnapshotForAutomation(),
                component,
                expectedSlotCount,
                useCustomRootName,
                expectedRootName,
                expectedPresetNames,
                expectedSaveLabel,
                expectedLoadLabel,
                expectedClearLabel,
                expectedConfirmLabel);
        }

        private static void AssertNamingSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow.PendingCustomizationSnapshot snapshot,
            ASMLite.ASMLiteComponent component,
            int expectedSlotCount,
            bool useCustomRootName,
            string expectedRootName,
            string[] expectedPresetNames,
            string expectedSaveLabel,
            string expectedLoadLabel,
            string expectedClearLabel,
            string expectedConfirmLabel)
        {
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), snapshot.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the pending testing snapshot.");
            Assert.AreEqual(useCustomRootName, snapshot.UseCustomRootName,
                $"{scenario}: UseCustomRootName should expose the root naming gate.");
            Assert.AreEqual(expectedRootName, snapshot.CustomRootName,
                $"{scenario}: CustomRootName should expose the normalized root-name text.");
            Assert.AreEqual(expectedSlotCount, snapshot.PresetNamesBySlot.Length,
                $"{scenario}: preset-name snapshot length should follow the selected slot count.");
            CollectionAssert.AreEqual(expectedPresetNames, snapshot.PresetNamesBySlot,
                $"{scenario}: PresetNamesBySlot should expose field-specific normalized preset names.");
            Assert.AreEqual(expectedSaveLabel, snapshot.SaveLabel,
                $"{scenario}: SaveLabel should expose the normalized Save action label field.");
            Assert.AreEqual(expectedLoadLabel, snapshot.LoadLabel,
                $"{scenario}: LoadLabel should expose the normalized Load action label field.");
            Assert.AreEqual(expectedClearLabel, snapshot.ClearLabel,
                $"{scenario}: ClearLabel should expose the normalized Clear action label field.");
            Assert.AreEqual(expectedConfirmLabel, snapshot.ConfirmLabel,
                $"{scenario}: ConfirmLabel should expose the normalized Confirm action label field.");
        }

        private static void AssertNamingSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow.CustomizationAutomationSnapshot snapshot,
            ASMLite.ASMLiteComponent component,
            int expectedSlotCount,
            bool useCustomRootName,
            string expectedRootName,
            string[] expectedPresetNames,
            string expectedSaveLabel,
            string expectedLoadLabel,
            string expectedClearLabel,
            string expectedConfirmLabel)
        {
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), snapshot.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the automation snapshot.");
            Assert.AreSame(component, snapshot.Component,
                $"{scenario}: automation snapshot should expose the attached ASMLiteComponent.");
            Assert.IsTrue(snapshot.HasAttachedComponent,
                $"{scenario}: automation snapshot should identify the attached component branch.");
            Assert.AreEqual(expectedSlotCount, snapshot.SlotCount,
                $"{scenario}: SlotCount should stay aligned with naming array normalization.");
            Assert.AreEqual(useCustomRootName, snapshot.UseCustomRootName,
                $"{scenario}: UseCustomRootName should expose the root naming gate.");
            Assert.AreEqual(expectedRootName, snapshot.CustomRootName,
                $"{scenario}: CustomRootName should expose the normalized root-name text.");
            CollectionAssert.AreEqual(expectedPresetNames, snapshot.PresetNamesBySlot,
                $"{scenario}: PresetNamesBySlot should expose field-specific normalized preset names.");
            Assert.AreEqual(expectedSaveLabel, snapshot.SaveLabel,
                $"{scenario}: SaveLabel should expose the normalized Save action label field.");
            Assert.AreEqual(expectedLoadLabel, snapshot.LoadLabel,
                $"{scenario}: LoadLabel should expose the normalized Load action label field.");
            Assert.AreEqual(expectedClearLabel, snapshot.ClearLabel,
                $"{scenario}: ClearLabel should expose the normalized Clear action label field.");
            Assert.AreEqual(expectedConfirmLabel, snapshot.ConfirmLabel,
                $"{scenario}: ConfirmLabel should expose the normalized Confirm action label field.");
        }

        private static void AssertEffectiveNaming(
            string scenario,
            ASMLite.ASMLiteComponent component,
            int slotCount,
            string expectedRootName,
            string[] expectedPresetLabels,
            string expectedSaveLabel,
            string expectedLoadLabel,
            string expectedClearLabel,
            string expectedConfirmLabel)
        {
            Assert.AreEqual(expectedRootName, ASMLite.Editor.ASMLiteBuilder.ResolveEffectiveRootControlName(component),
                $"{scenario}: effective root name should reflect the root naming gate and normalized root text.");
            for (int slot = 1; slot <= slotCount; slot++)
            {
                Assert.AreEqual(expectedPresetLabels[slot - 1], ASMLite.Editor.ASMLiteBuilder.ResolveEffectivePresetControlName(component, slot),
                    $"{scenario}: effective preset label mismatch for slot {slot}.");
            }

            Assert.AreEqual(expectedSaveLabel, ASMLite.Editor.ASMLiteBuilder.ResolveEffectiveSaveLabel(component),
                $"{scenario}: effective Save action label mismatch.");
            Assert.AreEqual(expectedLoadLabel, ASMLite.Editor.ASMLiteBuilder.ResolveEffectiveLoadLabel(component),
                $"{scenario}: effective Load action label mismatch.");
            Assert.AreEqual(expectedClearLabel, ASMLite.Editor.ASMLiteBuilder.ResolveEffectiveClearPresetLabel(component),
                $"{scenario}: effective Clear action label mismatch.");
            Assert.AreEqual(expectedConfirmLabel, ASMLite.Editor.ASMLiteBuilder.ResolveEffectiveConfirmLabel(component),
                $"{scenario}: effective Confirm action label mismatch.");
        }

        private static void AssertBuiltinIconSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow window,
            ASMLite.ASMLiteComponent component,
            int expectedSlotCount,
            IconMode expectedIconMode,
            int expectedGearIndex)
        {
            Assert.IsNotNull(window, $"{scenario}: expected an automation window.");
            Assert.IsNotNull(component, $"{scenario}: expected an attached ASM-Lite component.");

            Assert.AreEqual(expectedIconMode, component.iconMode,
                $"{scenario}: attached component icon mode should remain the selected built-in mode.");
            Assert.AreEqual(expectedGearIndex, component.selectedGearIndex,
                $"{scenario}: attached component selected gear index should expose the chosen built-in color.");
            Assert.IsFalse(component.useCustomSlotIcons,
                $"{scenario}: built-in icon rows should not enable per-slot custom icon overrides.");
            Assert.AreEqual(ActionIconMode.Default, component.actionIconMode,
                $"{scenario}: built-in icon rows should keep default action icons.");
            Assert.IsFalse(component.useCustomRootIcon,
                $"{scenario}: built-in icon rows should keep the root icon gate off.");
            Assert.IsNull(component.customRootIcon,
                $"{scenario}: built-in icon rows should not assign a custom root icon.");
            CollectionAssert.AreEqual(Array.Empty<string>(), FixtureIdsFromTextures(component.customIcons),
                $"{scenario}: attached component should not carry custom slot icon fixture references.");

            var pendingTesting = window.GetPendingCustomizationSnapshotForTesting();
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), pendingTesting.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the pending testing snapshot.");
            Assert.IsFalse(pendingTesting.UseCustomRootIcon,
                $"{scenario}: pending testing snapshot should keep the root icon gate off.");
            CollectionAssert.AreEqual(Array.Empty<string>(), FixtureIdsFromTextures(pendingTesting.CustomIcons),
                $"{scenario}: pending testing snapshot should not carry custom slot icon fixture references.");

            AssertIconAutomationSnapshotFields(
                scenario + " pending automation snapshot",
                window.GetPendingCustomizationSnapshotForAutomation(),
                component,
                expectedSlotCount,
                expectedIconMode,
                expectedGearIndex,
                useCustomSlotIcons: false,
                expectedRootIconFixtureId: string.Empty,
                expectedSlotIconFixtureIds: EmptyFixtureIds(expectedSlotCount),
                expectedActionIconMode: ActionIconMode.Default,
                expectedSaveIconFixtureId: string.Empty,
                expectedLoadIconFixtureId: string.Empty,
                expectedClearIconFixtureId: string.Empty);
            AssertIconAutomationSnapshotFields(
                scenario + " attached automation snapshot",
                window.GetAttachedCustomizationSnapshotForAutomation(),
                component,
                expectedSlotCount,
                expectedIconMode,
                expectedGearIndex,
                useCustomSlotIcons: false,
                expectedRootIconFixtureId: string.Empty,
                expectedSlotIconFixtureIds: EmptyFixtureIds(expectedSlotCount),
                expectedActionIconMode: ActionIconMode.Default,
                expectedSaveIconFixtureId: string.Empty,
                expectedLoadIconFixtureId: string.Empty,
                expectedClearIconFixtureId: string.Empty);
        }

        private static void AssertCustomIconSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow window,
            ASMLite.ASMLiteComponent component,
            int expectedSlotCount,
            string expectedRootIconFixtureId,
            string[] expectedSlotIconFixtureIds,
            string expectedSaveIconFixtureId,
            string expectedLoadIconFixtureId,
            string expectedClearIconFixtureId)
        {
            Assert.IsNotNull(window, $"{scenario}: expected an automation window.");
            Assert.IsNotNull(component, $"{scenario}: expected an attached ASM-Lite component.");
            expectedRootIconFixtureId = expectedRootIconFixtureId ?? string.Empty;
            expectedSlotIconFixtureIds = expectedSlotIconFixtureIds ?? Array.Empty<string>();
            expectedSaveIconFixtureId = expectedSaveIconFixtureId ?? string.Empty;
            expectedLoadIconFixtureId = expectedLoadIconFixtureId ?? string.Empty;
            expectedClearIconFixtureId = expectedClearIconFixtureId ?? string.Empty;

            var expectedActionIconMode = string.IsNullOrEmpty(expectedSaveIconFixtureId)
                && string.IsNullOrEmpty(expectedLoadIconFixtureId)
                && string.IsNullOrEmpty(expectedClearIconFixtureId)
                    ? ActionIconMode.Default
                    : ActionIconMode.Custom;

            Assert.AreEqual(IconMode.MultiColor, component.iconMode,
                $"{scenario}: applying icon fixtures should preserve the selected built-in slot icon mode.");
            Assert.IsTrue(component.useCustomSlotIcons,
                $"{scenario}: applying icon fixtures should enable per-slot custom icon overrides.");
            Assert.AreEqual(expectedActionIconMode, component.actionIconMode,
                $"{scenario}: action icon mode should follow whether action icon fixtures were supplied.");
            Assert.AreEqual(!string.IsNullOrEmpty(expectedRootIconFixtureId), component.useCustomRootIcon,
                $"{scenario}: root icon gate should follow whether a root fixture was supplied.");
            Assert.AreEqual(expectedRootIconFixtureId, ASMLite.Editor.ASMLiteIconFixtureRegistry.GetFixtureIdOrEmpty(component.customRootIcon),
                $"{scenario}: attached component customRootIcon should round-trip to the expected fixture ID.");
            CollectionAssert.AreEqual(expectedSlotIconFixtureIds, FixtureIdsFromTextures(component.customIcons),
                $"{scenario}: attached component customIcons should preserve field-specific slot fixture IDs.");
            Assert.AreEqual(expectedSaveIconFixtureId, ASMLite.Editor.ASMLiteIconFixtureRegistry.GetFixtureIdOrEmpty(component.customSaveIcon),
                $"{scenario}: attached component customSaveIcon should round-trip to the expected fixture ID.");
            Assert.AreEqual(expectedLoadIconFixtureId, ASMLite.Editor.ASMLiteIconFixtureRegistry.GetFixtureIdOrEmpty(component.customLoadIcon),
                $"{scenario}: attached component customLoadIcon should round-trip to the expected fixture ID.");
            Assert.AreEqual(expectedClearIconFixtureId, ASMLite.Editor.ASMLiteIconFixtureRegistry.GetFixtureIdOrEmpty(component.customClearIcon),
                $"{scenario}: attached component customClearIcon should round-trip to the expected fixture ID.");

            var pendingTesting = window.GetPendingCustomizationSnapshotForTesting();
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), pendingTesting.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the pending testing snapshot.");
            Assert.AreEqual(!string.IsNullOrEmpty(expectedRootIconFixtureId), pendingTesting.UseCustomRootIcon,
                $"{scenario}: pending testing snapshot root icon gate should follow whether a root fixture was supplied.");
            CollectionAssert.AreEqual(expectedSlotIconFixtureIds, FixtureIdsFromTextures(pendingTesting.CustomIcons),
                $"{scenario}: pending testing snapshot should expose field-specific slot fixture IDs.");

            AssertIconAutomationSnapshotFields(
                scenario + " pending automation snapshot",
                window.GetPendingCustomizationSnapshotForAutomation(),
                component,
                expectedSlotCount,
                IconMode.MultiColor,
                expectedGearIndex: 0,
                useCustomSlotIcons: true,
                expectedRootIconFixtureId: expectedRootIconFixtureId,
                expectedSlotIconFixtureIds: expectedSlotIconFixtureIds,
                expectedActionIconMode: expectedActionIconMode,
                expectedSaveIconFixtureId: expectedSaveIconFixtureId,
                expectedLoadIconFixtureId: expectedLoadIconFixtureId,
                expectedClearIconFixtureId: expectedClearIconFixtureId);
            AssertIconAutomationSnapshotFields(
                scenario + " attached automation snapshot",
                window.GetAttachedCustomizationSnapshotForAutomation(),
                component,
                expectedSlotCount,
                IconMode.MultiColor,
                expectedGearIndex: 0,
                useCustomSlotIcons: true,
                expectedRootIconFixtureId: expectedRootIconFixtureId,
                expectedSlotIconFixtureIds: expectedSlotIconFixtureIds,
                expectedActionIconMode: expectedActionIconMode,
                expectedSaveIconFixtureId: expectedSaveIconFixtureId,
                expectedLoadIconFixtureId: expectedLoadIconFixtureId,
                expectedClearIconFixtureId: expectedClearIconFixtureId);
        }

        private static void AssertIconAutomationSnapshotFields(
            string scenario,
            ASMLite.Editor.ASMLiteWindow.CustomizationAutomationSnapshot snapshot,
            ASMLite.ASMLiteComponent component,
            int expectedSlotCount,
            IconMode expectedIconMode,
            int expectedGearIndex,
            bool useCustomSlotIcons,
            string expectedRootIconFixtureId,
            string[] expectedSlotIconFixtureIds,
            ActionIconMode expectedActionIconMode,
            string expectedSaveIconFixtureId,
            string expectedLoadIconFixtureId,
            string expectedClearIconFixtureId)
        {
            Assert.AreSame(component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(), snapshot.SelectedAvatar,
                $"{scenario}: selected avatar should remain wired into the automation snapshot.");
            Assert.AreSame(component, snapshot.Component,
                $"{scenario}: automation snapshot should expose the attached ASMLiteComponent.");
            Assert.IsTrue(snapshot.HasAttachedComponent,
                $"{scenario}: automation snapshot should identify the attached component branch.");
            Assert.AreEqual(expectedSlotCount, snapshot.SlotCount,
                $"{scenario}: SlotCount should stay aligned with icon fixture arrays.");
            Assert.AreEqual(expectedIconMode, snapshot.Customization.IconMode,
                $"{scenario}: IconMode should expose the selected built-in/custom icon mode.");
            Assert.AreEqual(expectedGearIndex, snapshot.Customization.SelectedGearIndex,
                $"{scenario}: SelectedGearIndex should expose the selected gear color index.");
            Assert.AreEqual(useCustomSlotIcons, snapshot.Customization.UseCustomSlotIcons,
                $"{scenario}: UseCustomSlotIcons should expose the per-slot custom icon gate.");
            Assert.AreEqual(!string.IsNullOrEmpty(expectedRootIconFixtureId), snapshot.Customization.UseCustomRootIcon,
                $"{scenario}: UseCustomRootIcon should expose whether a root fixture was supplied.");
            Assert.AreEqual(expectedRootIconFixtureId, snapshot.CustomRootIconFixtureId,
                $"{scenario}: CustomRootIconFixtureId should expose the root fixture ID.");
            CollectionAssert.AreEqual(expectedSlotIconFixtureIds, snapshot.CustomSlotIconFixtureIds,
                $"{scenario}: CustomSlotIconFixtureIds should expose field-specific slot fixture IDs.");
            Assert.AreEqual(expectedActionIconMode, snapshot.Customization.ActionIconMode,
                $"{scenario}: ActionIconMode should expose the action-icon fixture gate.");
            Assert.AreEqual(expectedSaveIconFixtureId, snapshot.CustomSaveIconFixtureId,
                $"{scenario}: CustomSaveIconFixtureId should expose the Save action fixture ID.");
            Assert.AreEqual(expectedLoadIconFixtureId, snapshot.CustomLoadIconFixtureId,
                $"{scenario}: CustomLoadIconFixtureId should expose the Load action fixture ID.");
            Assert.AreEqual(expectedClearIconFixtureId, snapshot.CustomClearIconFixtureId,
                $"{scenario}: CustomClearIconFixtureId should expose the Clear action fixture ID.");
        }

        private static string[] FixtureIdsFromTextures(Texture2D[] textures)
        {
            return ASMLite.Editor.ASMLiteIconFixtureRegistry.GetFixtureIdsOrEmpty(textures);
        }

        private static string[] EmptyFixtureIds(int count)
        {
            return Enumerable.Repeat(string.Empty, count).ToArray();
        }

        private static string[] SlotFixtureIds(int count)
        {
            return Enumerable.Range(1, count)
                .Select(ASMLite.Editor.ASMLiteIconFixtureRegistry.ResolveSlotIconId)
                .ToArray();
        }

        private static string[] EmptyStrings(int count)
        {
            return Enumerable.Repeat(string.Empty, count).ToArray();
        }

        private static string[] DefaultPresetLabels(int count)
        {
            return Enumerable.Range(1, count)
                .Select(slot => ASMLite.Editor.ASMLiteBuilder.DefaultPresetNameFormat.Replace("{slot}", slot.ToString(), StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        private static VRCExpressionsMenu CreateTempMenuAsset(string name)
        {
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/ASMLiteTests_Temp/{name}.asset");
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = new List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menu, assetPath);
            AssetDatabase.SaveAssets();

            var persistedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(assetPath);
            Assert.IsNotNull(persistedMenu,
                $"Expected temporary VRCExpressionsMenu asset at '{assetPath}'.");
            return persistedMenu;
        }

        private static VRCExpressionsMenu LoadAsmLitePresetsMenu()
        {
            string presetsMenuPath = $"{ASMLite.Editor.ASMLiteAssetPaths.GeneratedDir}/ASMLite_Presets_Menu.asset";
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(presetsMenuPath);
            Assert.IsNotNull(menu,
                $"Expected generated ASM-Lite presets menu at '{presetsMenuPath}'.");
            return menu;
        }
    }
}
