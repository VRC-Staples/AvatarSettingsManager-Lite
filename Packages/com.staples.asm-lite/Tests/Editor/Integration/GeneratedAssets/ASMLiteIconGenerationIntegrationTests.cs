using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema-Build_FullControllerSchemaDrift_ReturnsMinusOne_AndExposesBuildDiagnosticWithNestedDriftDiagnostic: Integration coverage for icon resolution through Build() output.
    /// Asserts icon references from avDesc.expressionsMenu graph, not private helpers.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteIconGenerationIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteIconGenerationIntegrationTests);
        private static ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _testGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.SourceAssetsSnapshot _sourceIconAssetsBaseline;
        private AsmLiteTestContext _ctx;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = null;
        }

        [SetUp]
        public void SetUp()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _sourceIconAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(
                SuiteName,
                ASMLiteGeneratedAssetTestIsolation.BuiltInIconFixturePaths());
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu,
                "Build_WithExclusions_UpdatesReturnCountAndGeneratedSchema: fixture did not assign expressionsMenu.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
                _sourceIconAssetsBaseline?.AssertUnchanged(SuiteName);
            }
            finally
            {
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
                _testGeneratedAssetsBaseline = null;
                _sourceIconAssetsBaseline = null;
                _ctx = null;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private VRCExpressionsMenu BuildAndGetPresetsMenu(string aid)
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult}; slotCount={_ctx.Comp.slotCount}, iconMode={_ctx.Comp.iconMode}.");

            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(rootMenu, $"{aid}: generated root menu is null after Build() at '{ASMLiteAssetPaths.Menu}'.");
            Assert.IsNotNull(rootMenu.controls, $"{aid}: root menu controls list is null after Build().");
            Assert.GreaterOrEqual(rootMenu.controls.Count, 1,
                $"{aid}: root menu controls are empty after Build().");

            var settingsControl = rootMenu.controls[0];
            Assert.IsNotNull(settingsControl, $"{aid}: root control[0] is null.");
            Assert.AreEqual("Settings Manager", settingsControl.name,
                $"{aid}: expected root control name 'Settings Manager', got '{settingsControl.name ?? "<null>"}'.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, settingsControl.type,
                $"{aid}: Settings Manager control type mismatch.");
            Assert.IsNotNull(settingsControl.subMenu,
                $"{aid}: Settings Manager control does not reference a submenu.");
            Assert.IsNotNull(settingsControl.subMenu.controls,
                $"{aid}: Settings Manager submenu controls list is null.");

            return settingsControl.subMenu;
        }

        private static Texture2D GetPresetSlotIconOrFail(VRCExpressionsMenu presetsMenu, int slotIndex, string aid)
        {
            Assert.IsNotNull(presetsMenu, $"{aid}: presets menu is null.");
            Assert.IsNotNull(presetsMenu.controls, $"{aid}: presets menu controls list is null.");
            Assert.GreaterOrEqual(presetsMenu.controls.Count, slotIndex,
                $"{aid}: presets menu has {presetsMenu.controls.Count} controls, expected at least {slotIndex}.");

            var presetControl = presetsMenu.controls[slotIndex - 1];
            Assert.IsNotNull(presetControl,
                $"{aid}: preset control entry for slot {slotIndex} is null.");
            Assert.AreEqual($"Preset {slotIndex}", presetControl.name,
                $"{aid}: preset control name mismatch for slot {slotIndex}. got '{presetControl.name ?? "<null>"}'.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, presetControl.type,
                $"{aid}: preset control for slot {slotIndex} must be SubMenu.");
            Assert.IsNotNull(presetControl.subMenu,
                $"{aid}: preset control for slot {slotIndex} must reference a slot submenu.");

            return presetControl.icon;
        }

        private static Texture2D LoadIconOrFail(string path, string aid)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(icon, $"{aid}: expected icon asset at '{path}' but LoadAssetAtPath returned null.");
            return icon;
        }

        private static Texture2D CopyIconFixtureOrFail(string path, string aid, string label)
            => ASMLiteGeneratedAssetTestIsolation.CopyIconFixtureOrFail(path, aid, label);

        [Test, Category("Integration")]
        public void MultiColor_UsesPerSlotGearIconBySlotIndex()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.MultiColor;

            var presetsMenu = BuildAndGetPresetsMenu("MultiColor_UsesPerSlotGearIconBySlotIndex");

            for (int slot = 1; slot <= _ctx.Comp.slotCount; slot++)
            {
                string expectedPath = ASMLiteAssetPaths.GearIconPaths[slot - 1];
                var expectedIcon = LoadIconOrFail(expectedPath, "MultiColor_UsesPerSlotGearIconBySlotIndex");
                var actualIcon = GetPresetSlotIconOrFail(presetsMenu, slot, "MultiColor_UsesPerSlotGearIconBySlotIndex");

                Assert.AreSame(expectedIcon, actualIcon,
                    $"MultiColor_UsesPerSlotGearIconBySlotIndex: slot {slot} icon mismatch for MultiColor mode. expectedPath='{expectedPath}', expected='{expectedIcon?.name ?? "<null>"}', actual='{actualIcon?.name ?? "<null>"}'.");
            }
        }

        [Test, Category("Integration")]
        public void SameColor_UsesSelectedGearIconAcrossAllSlots()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.SameColor;
            _ctx.Comp.selectedGearIndex = 3;

            string expectedPath = ASMLiteAssetPaths.GearIconPaths[_ctx.Comp.selectedGearIndex];
            var expectedIcon = LoadIconOrFail(expectedPath, "SameColor_UsesSelectedGearIconAcrossAllSlots");
            var presetsMenu = BuildAndGetPresetsMenu("SameColor_UsesSelectedGearIconAcrossAllSlots");

            for (int slot = 1; slot <= _ctx.Comp.slotCount; slot++)
            {
                var actualIcon = GetPresetSlotIconOrFail(presetsMenu, slot, "SameColor_UsesSelectedGearIconAcrossAllSlots");
                Assert.AreSame(expectedIcon, actualIcon,
                    $"SameColor_UsesSelectedGearIconAcrossAllSlots: slot {slot} icon mismatch for SameColor mode. selectedGearIndex={_ctx.Comp.selectedGearIndex}, expectedPath='{expectedPath}', expected='{expectedIcon?.name ?? "<null>"}', actual='{actualIcon?.name ?? "<null>"}'.");
            }
        }

        [Test, Category("Integration")]
        public void Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.Custom;
            _ctx.Comp.useCustomSlotIcons = true;

            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange");
            var customSlot1 = CopyIconFixtureOrFail(ASMLiteAssetPaths.GearIconPaths[5], "Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange", "CustomSlot1");

            _ctx.Comp.customIcons = new Texture2D[]
            {
                customSlot1,
                null,
            };

            var presetsMenu = BuildAndGetPresetsMenu("Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange");

            var slot1Icon = GetPresetSlotIconOrFail(presetsMenu, 1, "Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange");
            var slot2Icon = GetPresetSlotIconOrFail(presetsMenu, 2, "Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange");
            var slot4Icon = GetPresetSlotIconOrFail(presetsMenu, 4, "Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange");

            Assert.AreSame(customSlot1, slot1Icon,
                $"Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange: slot 1 should use custom icon reference. expected='{customSlot1?.name ?? "<null>"}', actual='{slot1Icon?.name ?? "<null>"}'.");
            Assert.AreSame(fallbackIcon, slot2Icon,
                $"Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange: slot 2 should fall back when customIcons[1] is null. expectedFallback='{fallbackIcon?.name ?? "<null>"}', actual='{slot2Icon?.name ?? "<null>"}'.");
            Assert.AreSame(fallbackIcon, slot4Icon,
                $"Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange: slot 4 should fall back when customIcons array is out-of-range. customIconsLength={_ctx.Comp.customIcons.Length}, expectedFallback='{fallbackIcon?.name ?? "<null>"}', actual='{slot4Icon?.name ?? "<null>"}'.");
        }
    }
}
