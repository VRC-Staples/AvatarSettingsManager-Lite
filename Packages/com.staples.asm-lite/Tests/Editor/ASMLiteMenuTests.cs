using NUnit.Framework;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A26-A30: Expression menu hierarchy integration invariants.
    /// These tests call Build() and assert menu graph shape through control/subMenu references.
    /// </summary>
    [TestFixture]
    public class ASMLiteMenuTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A26: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A26: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A26: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.AvDesc.expressionsMenu, "A26: fixture did not assign expressionsMenu.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private VRCExpressionsMenu BuildAndGetRootMenu(int slotCount, string aid)
        {
            _ctx.Comp.slotCount = slotCount;
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult} for slotCount={slotCount}.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var rootMenu = _ctx.AvDesc.expressionsMenu;
            Assert.IsNotNull(rootMenu, $"{aid}: avDesc.expressionsMenu is null after Build().");
            Assert.IsNotNull(rootMenu.controls, $"{aid}: root menu controls list is null after Build().");
            return rootMenu;
        }

        private static VRCExpressionsMenu.Control GetSettingsManagerControl(VRCExpressionsMenu rootMenu, string aid)
        {
            Assert.AreEqual(1, rootMenu.controls.Count,
                $"{aid}: root menu must contain exactly one control.");

            var settingsControl = rootMenu.controls[0];
            Assert.AreEqual("Settings Manager", settingsControl.name,
                $"{aid}: root control name must be 'Settings Manager'.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, settingsControl.type,
                $"{aid}: root Settings Manager control must be SubMenu.");
            Assert.IsNotNull(settingsControl.subMenu,
                $"{aid}: Settings Manager control must reference a submenu.");
            return settingsControl;
        }

        private static VRCExpressionsMenu GetPresetsMenu(VRCExpressionsMenu rootMenu, string aid)
        {
            var settingsControl = GetSettingsManagerControl(rootMenu, aid);
            var presetsMenu = settingsControl.subMenu;
            Assert.IsNotNull(presetsMenu.controls,
                $"{aid}: presets submenu controls list is null.");
            return presetsMenu;
        }

        private static VRCExpressionsMenu.Control GetPresetControl(VRCExpressionsMenu presetsMenu, int slotIndex, string aid)
        {
            Assert.GreaterOrEqual(presetsMenu.controls.Count, slotIndex,
                $"{aid}: presets menu must contain at least {slotIndex} entries.");

            var presetControl = presetsMenu.controls[slotIndex - 1];
            Assert.AreEqual($"Preset {slotIndex}", presetControl.name,
                $"{aid}: preset control name mismatch for slot {slotIndex}.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, presetControl.type,
                $"{aid}: Preset {slotIndex} must be a SubMenu control.");
            Assert.IsNotNull(presetControl.subMenu,
                $"{aid}: Preset {slotIndex} must reference a slot submenu.");
            return presetControl;
        }

        private enum TriggerActionKind
        {
            SaveConfirm = 1,
            Load = 2,
            ClearConfirm = 3,
        }

        private static float ExpectedCompactIntValue(int slotIndex, TriggerActionKind actionKind)
            // CompactInt encoding contract: (slot-1)*3+N where N=1 SaveConfirm, 2 Load, 3 ClearConfirm.
            => (slotIndex - 1) * 3 + (int)actionKind;

        private static VRCExpressionsMenu.Control GetControlOrFail(
            VRCExpressionsMenu menu,
            string controlName,
            VRCExpressionsMenu.Control.ControlType controlType,
            string aid,
            int slotIndex,
            string actionKind)
        {
            Assert.IsNotNull(menu, $"{aid}: slot {slotIndex} {actionKind} lookup menu is null.");
            Assert.IsNotNull(menu.controls,
                $"{aid}: slot {slotIndex} {actionKind} lookup menu controls are null.");

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var candidate = menu.controls[i];
                if (candidate.name == controlName && candidate.type == controlType)
                    return candidate;
            }

            Assert.Fail(
                $"{aid}: slot {slotIndex} could not resolve {actionKind} control '{controlName}' with type {controlType}.");
            return null;
        }

        // ── A26 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A26_RootContainsSingleSettingsManagerSubmenu()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "A26");
            _ = GetSettingsManagerControl(rootMenu, "A26");
        }

        // ── A27 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A27_PresetsMenuCountAndNamesMatchSlotCount()
        {
            const int slotCount = 2;
            var rootMenu = BuildAndGetRootMenu(slotCount, "A27");
            var presetsMenu = GetPresetsMenu(rootMenu, "A27");

            Assert.AreEqual(slotCount, presetsMenu.controls.Count,
                "A27: presets menu control count must equal slotCount.");

            for (int slot = 1; slot <= slotCount; slot++)
                _ = GetPresetControl(presetsMenu, slot, "A27");
        }

        // ── A28 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A28_EachSlotMenuHasSaveLoadClearWithExpectedControlTypes()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "A28");
            var presetsMenu = GetPresetsMenu(rootMenu, "A28");
            var slotMenu = GetPresetControl(presetsMenu, 1, "A28").subMenu;

            Assert.IsNotNull(slotMenu.controls, "A28: slot menu controls list is null.");
            Assert.AreEqual(3, slotMenu.controls.Count,
                "A28: slot menu must contain exactly three controls (Save, Load, Clear Preset).");

            var save = slotMenu.controls[0];
            var load = slotMenu.controls[1];
            var clear = slotMenu.controls[2];

            Assert.AreEqual("Save", save.name, "A28: first slot control must be Save.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, save.type,
                "A28: Save must be SubMenu.");
            Assert.IsNotNull(save.subMenu, "A28: Save must link to confirm submenu.");

            Assert.AreEqual("Load", load.name, "A28: second slot control must be Load.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, load.type,
                "A28: Load must be Button.");

            Assert.AreEqual("Clear Preset", clear.name, "A28: third slot control must be Clear Preset.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, clear.type,
                "A28: Clear Preset must be SubMenu.");
            Assert.IsNotNull(clear.subMenu, "A28: Clear Preset must link to confirm submenu.");
        }

        // ── A29 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A29_SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "A29");
            var presetsMenu = GetPresetsMenu(rootMenu, "A29");
            var slotMenu = GetPresetControl(presetsMenu, 1, "A29").subMenu;

            var save = slotMenu.controls[0];
            Assert.IsNotNull(save.subMenu, "A29: Save submenu reference is null.");
            Assert.IsNotNull(save.subMenu.controls, "A29: Save confirm submenu controls are null.");
            Assert.AreEqual(1, save.subMenu.controls.Count,
                "A29: Save confirm submenu must contain exactly one control.");

            var confirm = save.subMenu.controls[0];
            Assert.AreEqual("Confirm", confirm.name,
                "A29: save confirm submenu control must be named Confirm.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, confirm.type,
                "A29: Confirm control must be Button.");
            Assert.IsNull(confirm.subMenu,
                "A29: Confirm control must not be a submenu.");
            Assert.IsNotNull(confirm.parameter,
                "A29: Confirm control parameter payload must exist.");
            Assert.AreEqual("ASMLite_Ctrl", confirm.parameter.name,
                "A29: Confirm control must target ASMLite_Ctrl.");
            Assert.AreEqual(1f, confirm.value,
                "A29: Confirm value for slot 1 save must be encoded as 1.");
        }

        // ── A30 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A30_LoadControlIsDirectButtonNotSubmenu()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "A30");
            var presetsMenu = GetPresetsMenu(rootMenu, "A30");
            var slotMenu = GetPresetControl(presetsMenu, 1, "A30").subMenu;

            var load = slotMenu.controls[1];
            Assert.AreEqual("Load", load.name,
                "A30: second slot control must be Load.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, load.type,
                "A30: Load control must be Button (direct action)." );
            Assert.IsNull(load.subMenu,
                "A30: Load control must not reference a submenu.");
            Assert.IsNotNull(load.parameter,
                "A30: Load control parameter payload must exist.");
            Assert.AreEqual("ASMLite_Ctrl", load.parameter.name,
                "A30: Load control must target ASMLite_Ctrl.");
            Assert.AreEqual(2f, load.value,
                "A30: Load value for slot 1 must be encoded as 2.");
        }

        // ── A31 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A31_AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots()
        {
            const int slotCount = 2;
            var rootMenu = BuildAndGetRootMenu(slotCount, "A31");
            var presetsMenu = GetPresetsMenu(rootMenu, "A31");

            for (int slot = 1; slot <= slotCount; slot++)
            {
                var slotMenu = GetPresetControl(presetsMenu, slot, "A31").subMenu;

                var load = GetControlOrFail(slotMenu, "Load", VRCExpressionsMenu.Control.ControlType.Button,
                    "A31", slot, "Load");
                Assert.IsNotNull(load.parameter,
                    $"A31: slot {slot} Load parameter payload must exist.");
                Assert.AreEqual("ASMLite_Ctrl", load.parameter.name,
                    $"A31: slot {slot} Load must target ASMLite_Ctrl, got '{load.parameter.name ?? "<null>"}'.");

                var save = GetControlOrFail(slotMenu, "Save", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "A31", slot, "Save");
                Assert.IsNotNull(save.subMenu,
                    $"A31: slot {slot} Save submenu reference is null.");
                var saveConfirm = GetControlOrFail(save.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "A31", slot, "SaveConfirm");
                Assert.IsNotNull(saveConfirm.parameter,
                    $"A31: slot {slot} SaveConfirm parameter payload must exist.");
                Assert.AreEqual("ASMLite_Ctrl", saveConfirm.parameter.name,
                    $"A31: slot {slot} SaveConfirm must target ASMLite_Ctrl, got '{saveConfirm.parameter.name ?? "<null>"}'.");

                var clear = GetControlOrFail(slotMenu, "Clear Preset", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "A31", slot, "Clear");
                Assert.IsNotNull(clear.subMenu,
                    $"A31: slot {slot} Clear Preset submenu reference is null.");
                var clearConfirm = GetControlOrFail(clear.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "A31", slot, "ClearConfirm");
                Assert.IsNotNull(clearConfirm.parameter,
                    $"A31: slot {slot} ClearConfirm parameter payload must exist.");
                Assert.AreEqual("ASMLite_Ctrl", clearConfirm.parameter.name,
                    $"A31: slot {slot} ClearConfirm must target ASMLite_Ctrl, got '{clearConfirm.parameter.name ?? "<null>"}'.");
            }
        }

        // ── A32 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A32_AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots()
        {
            const int slotCount = 2;
            var rootMenu = BuildAndGetRootMenu(slotCount, "A32");
            var presetsMenu = GetPresetsMenu(rootMenu, "A32");

            for (int slot = 1; slot <= slotCount; slot++)
            {
                var slotMenu = GetPresetControl(presetsMenu, slot, "A32").subMenu;

                var load = GetControlOrFail(slotMenu, "Load", VRCExpressionsMenu.Control.ControlType.Button,
                    "A32", slot, "Load");
                float expectedLoad = ExpectedCompactIntValue(slot, TriggerActionKind.Load);
                Assert.AreEqual(expectedLoad, load.value,
                    $"A32: slot {slot} Load value mismatch. Expected {expectedLoad}, got {load.value}.");

                var save = GetControlOrFail(slotMenu, "Save", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "A32", slot, "Save");
                Assert.IsNotNull(save.subMenu,
                    $"A32: slot {slot} Save submenu reference is null.");
                var saveConfirm = GetControlOrFail(save.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "A32", slot, "SaveConfirm");
                float expectedSave = ExpectedCompactIntValue(slot, TriggerActionKind.SaveConfirm);
                Assert.AreEqual(expectedSave, saveConfirm.value,
                    $"A32: slot {slot} SaveConfirm value mismatch. Expected {expectedSave}, got {saveConfirm.value}.");

                var clear = GetControlOrFail(slotMenu, "Clear Preset", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "A32", slot, "Clear");
                Assert.IsNotNull(clear.subMenu,
                    $"A32: slot {slot} Clear Preset submenu reference is null.");
                var clearConfirm = GetControlOrFail(clear.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "A32", slot, "ClearConfirm");
                float expectedClear = ExpectedCompactIntValue(slot, TriggerActionKind.ClearConfirm);
                Assert.AreEqual(expectedClear, clearConfirm.value,
                    $"A32: slot {slot} ClearConfirm value mismatch. Expected {expectedClear}, got {clearConfirm.value}.");
            }
        }

        // ── A33 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A33_InjectExpressionMenuIsIdempotentAfterRepeatedBuild()
        {
            _ctx.Comp.slotCount = 1;

            int firstResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(firstResult, 0,
                $"A33: first Build() failed with result {firstResult}.");

            int secondResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(secondResult, 0,
                $"A33: second Build() failed with result {secondResult}.");

            var rootMenu = _ctx.AvDesc.expressionsMenu;
            Assert.IsNotNull(rootMenu, "A33: avDesc.expressionsMenu is null after repeated Build().");
            Assert.IsNotNull(rootMenu.controls, "A33: root menu controls list is null after repeated Build().");

            int settingsManagerCount = 0;
            for (int i = 0; i < rootMenu.controls.Count; i++)
            {
                var control = rootMenu.controls[i];
                if (control != null
                    && control.name == "Settings Manager"
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    settingsManagerCount++;
                }
            }

            Assert.AreEqual(1, settingsManagerCount,
                $"A33: expected exactly one Settings Manager root control after two Build() calls; rootCount={rootMenu.controls.Count}, settingsManagerCount={settingsManagerCount}.");
        }

        // ── A34 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A34_DoesNotInjectWhenRootMenuAlreadyHasEightControls()
        {
            var rootMenu = _ctx.AvDesc.expressionsMenu;
            Assert.IsNotNull(rootMenu, "A34: avDesc.expressionsMenu is null before prefill.");

            rootMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            for (int i = 0; i < 8; i++)
            {
                rootMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = $"PreExisting_{i + 1}",
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                });
            }

            Assert.IsNotNull(rootMenu.controls, "A34: prefill produced null controls list.");
            Assert.AreEqual(8, rootMenu.controls.Count,
                $"A34: setup failure, expected exactly 8 baseline controls before Build(), got {rootMenu.controls.Count}.");

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"A34: Build() failed with result {buildResult} in saturated-root scenario.");

            var liveRootMenu = _ctx.AvDesc.expressionsMenu;
            Assert.IsNotNull(liveRootMenu, "A34: avDesc.expressionsMenu is null after Build().");
            Assert.IsNotNull(liveRootMenu.controls, "A34: root menu controls list is null after Build().");

            int settingsManagerCount = 0;
            for (int i = 0; i < liveRootMenu.controls.Count; i++)
            {
                var control = liveRootMenu.controls[i];
                if (control != null
                    && control.name == "Settings Manager"
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu)
                {
                    settingsManagerCount++;
                }
            }

            Assert.AreEqual(8, liveRootMenu.controls.Count,
                $"A34: root menu size changed unexpectedly. before=8, after={liveRootMenu.controls.Count}.");
            Assert.AreEqual(0, settingsManagerCount,
                $"A34: Settings Manager should not be injected when root is already full (8 controls). afterCount={liveRootMenu.controls.Count}, settingsManagerCount={settingsManagerCount}.");
        }
    }
}
