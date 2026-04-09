using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A51-A53: Integration coverage for icon resolution through Build() output.
    /// Asserts icon references from avDesc.expressionsMenu graph, not private helpers.
    /// </summary>
    [TestFixture]
    public class ASMLiteIconTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A51: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A51: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A51: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu,
                "A51: fixture did not assign expressionsMenu.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private VRCExpressionsMenu BuildAndGetPresetsMenu(string aid)
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult}; slotCount={_ctx.Comp.slotCount}, iconMode={_ctx.Comp.iconMode}.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

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

        // ── A51 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A51_MultiColor_UsesPerSlotGearIconBySlotIndex()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.MultiColor;

            var presetsMenu = BuildAndGetPresetsMenu("A51");

            for (int slot = 1; slot <= _ctx.Comp.slotCount; slot++)
            {
                string expectedPath = ASMLiteAssetPaths.GearIconPaths[slot - 1];
                var expectedIcon = LoadIconOrFail(expectedPath, "A51");
                var actualIcon = GetPresetSlotIconOrFail(presetsMenu, slot, "A51");

                Assert.AreSame(expectedIcon, actualIcon,
                    $"A51: slot {slot} icon mismatch for MultiColor mode. expectedPath='{expectedPath}', expected='{expectedIcon?.name ?? "<null>"}', actual='{actualIcon?.name ?? "<null>"}'.");
            }
        }

        // ── A52 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A52_SameColor_UsesSelectedGearIconAcrossAllSlots()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.SameColor;
            _ctx.Comp.selectedGearIndex = 3;

            string expectedPath = ASMLiteAssetPaths.GearIconPaths[_ctx.Comp.selectedGearIndex];
            var expectedIcon = LoadIconOrFail(expectedPath, "A52");
            var presetsMenu = BuildAndGetPresetsMenu("A52");

            for (int slot = 1; slot <= _ctx.Comp.slotCount; slot++)
            {
                var actualIcon = GetPresetSlotIconOrFail(presetsMenu, slot, "A52");
                Assert.AreSame(expectedIcon, actualIcon,
                    $"A52: slot {slot} icon mismatch for SameColor mode. selectedGearIndex={_ctx.Comp.selectedGearIndex}, expectedPath='{expectedPath}', expected='{expectedIcon?.name ?? "<null>"}', actual='{actualIcon?.name ?? "<null>"}'.");
            }
        }

        // ── A53 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A53_Custom_UsesCustomIconAndFallsBackToPresetWhenNullOrOutOfRange()
        {
            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.Custom;

            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "A53");
            var customSlot1 = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[5], "A53");

            _ctx.Comp.customIcons = new Texture2D[]
            {
                customSlot1,
                null,
            };

            var presetsMenu = BuildAndGetPresetsMenu("A53");

            var slot1Icon = GetPresetSlotIconOrFail(presetsMenu, 1, "A53");
            var slot2Icon = GetPresetSlotIconOrFail(presetsMenu, 2, "A53");
            var slot4Icon = GetPresetSlotIconOrFail(presetsMenu, 4, "A53");

            Assert.AreSame(customSlot1, slot1Icon,
                $"A53: slot 1 should use custom icon reference. expected='{customSlot1?.name ?? "<null>"}', actual='{slot1Icon?.name ?? "<null>"}'.");
            Assert.AreSame(fallbackIcon, slot2Icon,
                $"A53: slot 2 should fall back when customIcons[1] is null. expectedFallback='{fallbackIcon?.name ?? "<null>"}', actual='{slot2Icon?.name ?? "<null>"}'.");
            Assert.AreSame(fallbackIcon, slot4Icon,
                $"A53: slot 4 should fall back when customIcons array is out-of-range. customIconsLength={_ctx.Comp.customIcons.Length}, expectedFallback='{fallbackIcon?.name ?? "<null>"}', actual='{slot4Icon?.name ?? "<null>"}'.");
        }
    }
}
