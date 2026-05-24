using NUnit.Framework;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// MappedLegacyAlias_RemainsLoadCompatible_AndIsMirroredForSaveAndReset-Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: Generated expression-menu hierarchy invariants.
    /// These tests call Build() and assert menu graph shape through the managed
    /// generated menu asset (ASMLiteAssetPaths.Menu).
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteMenuGenerationIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteMenuGenerationIntegrationTests);
        private static ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _testGeneratedAssetsBaseline;
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
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "MappedLegacyAlias_RemainsLoadCompatible_AndIsMirroredForSaveAndReset: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "MappedLegacyAlias_RemainsLoadCompatible_AndIsMirroredForSaveAndReset: fixture did not create ASMLiteComponent.");

            var generatedRoot = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(generatedRoot,
                $"MappedLegacyAlias_RemainsLoadCompatible_AndIsMirroredForSaveAndReset: generated root menu asset missing at '{ASMLiteAssetPaths.Menu}'.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            }
            finally
            {
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
                _testGeneratedAssetsBaseline = null;
                _ctx = null;
            }
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private VRCExpressionsMenu BuildAndGetRootMenu(int slotCount, string aid)
        {
            _ctx.Comp.slotCount = slotCount;
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult} for slotCount={slotCount}.");

            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(rootMenu, $"{aid}: generated root menu is null after Build().");
            Assert.IsNotNull(rootMenu.controls, $"{aid}: generated root menu controls list is null after Build().");
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

        [Test, Category("Integration")]
        public void RootContainsSingleSettingsManagerSubmenu()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "RootContainsSingleSettingsManagerSubmenu");
            _ = GetSettingsManagerControl(rootMenu, "RootContainsSingleSettingsManagerSubmenu");
        }

        [Test, Category("Integration")]
        public void PresetsMenuCountAndNamesMatchSlotCount()
        {
            const int slotCount = 2;
            var rootMenu = BuildAndGetRootMenu(slotCount, "PresetsMenuCountAndNamesMatchSlotCount");
            var presetsMenu = GetPresetsMenu(rootMenu, "PresetsMenuCountAndNamesMatchSlotCount");

            Assert.AreEqual(slotCount, presetsMenu.controls.Count,
                "PresetsMenuCountAndNamesMatchSlotCount: presets menu control count must equal slotCount.");

            for (int slot = 1; slot <= slotCount; slot++)
                _ = GetPresetControl(presetsMenu, slot, "PresetsMenuCountAndNamesMatchSlotCount");
        }

        [Test, Category("Integration")]
        public void EachSlotMenuHasSaveLoadClearWithExpectedControlTypes()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes");
            var presetsMenu = GetPresetsMenu(rootMenu, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes");
            var slotMenu = GetPresetControl(presetsMenu, 1, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes").subMenu;

            Assert.IsNotNull(slotMenu.controls, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: slot menu controls list is null.");
            Assert.AreEqual(3, slotMenu.controls.Count,
                "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: slot menu must contain exactly three controls (Save, Load, Clear Preset).");

            var save = slotMenu.controls[0];
            var load = slotMenu.controls[1];
            var clear = slotMenu.controls[2];

            Assert.AreEqual("Save", save.name, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: first slot control must be Save.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, save.type,
                "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: Save must be SubMenu.");
            Assert.IsNotNull(save.subMenu, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: Save must link to confirm submenu.");

            Assert.AreEqual("Load", load.name, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: second slot control must be Load.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, load.type,
                "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: Load must be Button.");

            Assert.AreEqual("Clear Preset", clear.name, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: third slot control must be Clear Preset.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, clear.type,
                "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: Clear Preset must be SubMenu.");
            Assert.IsNotNull(clear.subMenu, "EachSlotMenuHasSaveLoadClearWithExpectedControlTypes: Clear Preset must link to confirm submenu.");
        }

        [Test, Category("Integration")]
        public void SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue");
            var presetsMenu = GetPresetsMenu(rootMenu, "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue");
            var slotMenu = GetPresetControl(presetsMenu, 1, "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue").subMenu;

            var save = slotMenu.controls[0];
            Assert.IsNotNull(save.subMenu, "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Save submenu reference is null.");
            Assert.IsNotNull(save.subMenu.controls, "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Save confirm submenu controls are null.");
            Assert.AreEqual(1, save.subMenu.controls.Count,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Save confirm submenu must contain exactly one control.");

            var confirm = save.subMenu.controls[0];
            Assert.AreEqual("Confirm", confirm.name,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: save confirm submenu control must be named Confirm.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, confirm.type,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Confirm control must be Button.");
            Assert.IsNull(confirm.subMenu,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Confirm control must not be a submenu.");
            Assert.IsNotNull(confirm.parameter,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Confirm control parameter payload must exist.");
            Assert.AreEqual("ASMLite_Ctrl", confirm.parameter.name,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Confirm control must target ASMLite_Ctrl.");
            Assert.AreEqual(1f, confirm.value,
                "SaveConfirmSubmenuHasSingleConfirmButtonWithEncodedValue: Confirm value for slot 1 save must be encoded as 1.");
        }

        [Test, Category("Integration")]
        public void LoadControlIsDirectButtonNotSubmenu()
        {
            var rootMenu = BuildAndGetRootMenu(slotCount: 1, aid: "LoadControlIsDirectButtonNotSubmenu");
            var presetsMenu = GetPresetsMenu(rootMenu, "LoadControlIsDirectButtonNotSubmenu");
            var slotMenu = GetPresetControl(presetsMenu, 1, "LoadControlIsDirectButtonNotSubmenu").subMenu;

            var load = slotMenu.controls[1];
            Assert.AreEqual("Load", load.name,
                "LoadControlIsDirectButtonNotSubmenu: second slot control must be Load.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, load.type,
                "LoadControlIsDirectButtonNotSubmenu: Load control must be Button (direct action).");
            Assert.IsNull(load.subMenu,
                "LoadControlIsDirectButtonNotSubmenu: Load control must not reference a submenu.");
            Assert.IsNotNull(load.parameter,
                "LoadControlIsDirectButtonNotSubmenu: Load control parameter payload must exist.");
            Assert.AreEqual("ASMLite_Ctrl", load.parameter.name,
                "LoadControlIsDirectButtonNotSubmenu: Load control must target ASMLite_Ctrl.");
            Assert.AreEqual(2f, load.value,
                "LoadControlIsDirectButtonNotSubmenu: Load value for slot 1 must be encoded as 2.");
        }

        [Test, Category("Integration")]
        public void AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots()
        {
            const int slotCount = 2;
            var rootMenu = BuildAndGetRootMenu(slotCount, "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots");
            var presetsMenu = GetPresetsMenu(rootMenu, "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots");

            for (int slot = 1; slot <= slotCount; slot++)
            {
                var slotMenu = GetPresetControl(presetsMenu, slot, "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots").subMenu;

                var load = GetControlOrFail(slotMenu, "Load", VRCExpressionsMenu.Control.ControlType.Button,
                    "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots", slot, "Load");
                Assert.IsNotNull(load.parameter,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} Load parameter payload must exist.");
                Assert.AreEqual("ASMLite_Ctrl", load.parameter.name,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} Load must target ASMLite_Ctrl, got '{load.parameter.name ?? "<null>"}'.");

                var save = GetControlOrFail(slotMenu, "Save", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots", slot, "Save");
                Assert.IsNotNull(save.subMenu,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} Save submenu reference is null.");
                var saveConfirm = GetControlOrFail(save.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots", slot, "SaveConfirm");
                Assert.IsNotNull(saveConfirm.parameter,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} SaveConfirm parameter payload must exist.");
                Assert.AreEqual("ASMLite_Ctrl", saveConfirm.parameter.name,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} SaveConfirm must target ASMLite_Ctrl, got '{saveConfirm.parameter.name ?? "<null>"}'.");

                var clear = GetControlOrFail(slotMenu, "Clear Preset", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots", slot, "Clear");
                Assert.IsNotNull(clear.subMenu,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} Clear Preset submenu reference is null.");
                var clearConfirm = GetControlOrFail(clear.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots", slot, "ClearConfirm");
                Assert.IsNotNull(clearConfirm.parameter,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} ClearConfirm parameter payload must exist.");
                Assert.AreEqual("ASMLite_Ctrl", clearConfirm.parameter.name,
                    $"AllTriggerButtonsBindASMLiteCtrlParameterAcrossSlots: slot {slot} ClearConfirm must target ASMLite_Ctrl, got '{clearConfirm.parameter.name ?? "<null>"}'.");
            }
        }

        [Test, Category("Integration")]
        public void AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots()
        {
            const int slotCount = 2;
            var rootMenu = BuildAndGetRootMenu(slotCount, "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots");
            var presetsMenu = GetPresetsMenu(rootMenu, "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots");

            for (int slot = 1; slot <= slotCount; slot++)
            {
                var slotMenu = GetPresetControl(presetsMenu, slot, "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots").subMenu;

                var load = GetControlOrFail(slotMenu, "Load", VRCExpressionsMenu.Control.ControlType.Button,
                    "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots", slot, "Load");
                float expectedLoad = ExpectedCompactIntValue(slot, TriggerActionKind.Load);
                Assert.AreEqual(expectedLoad, load.value,
                    $"AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots: slot {slot} Load value mismatch. Expected {expectedLoad}, got {load.value}.");

                var save = GetControlOrFail(slotMenu, "Save", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots", slot, "Save");
                Assert.IsNotNull(save.subMenu,
                    $"AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots: slot {slot} Save submenu reference is null.");
                var saveConfirm = GetControlOrFail(save.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots", slot, "SaveConfirm");
                float expectedSave = ExpectedCompactIntValue(slot, TriggerActionKind.SaveConfirm);
                Assert.AreEqual(expectedSave, saveConfirm.value,
                    $"AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots: slot {slot} SaveConfirm value mismatch. Expected {expectedSave}, got {saveConfirm.value}.");

                var clear = GetControlOrFail(slotMenu, "Clear Preset", VRCExpressionsMenu.Control.ControlType.SubMenu,
                    "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots", slot, "Clear");
                Assert.IsNotNull(clear.subMenu,
                    $"AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots: slot {slot} Clear Preset submenu reference is null.");
                var clearConfirm = GetControlOrFail(clear.subMenu, "Confirm", VRCExpressionsMenu.Control.ControlType.Button,
                    "AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots", slot, "ClearConfirm");
                float expectedClear = ExpectedCompactIntValue(slot, TriggerActionKind.ClearConfirm);
                Assert.AreEqual(expectedClear, clearConfirm.value,
                    $"AllTriggerButtonsUseExpectedCompactIntValueAcrossSlots: slot {slot} ClearConfirm value mismatch. Expected {expectedClear}, got {clearConfirm.value}.");
            }
        }

        [Test, Category("Integration")]
        public void GeneratedExpressionMenu_IsIdempotentAfterRepeatedBuild()
        {
            _ctx.Comp.slotCount = 1;

            int firstResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(firstResult, 0,
                $"GeneratedExpressionMenu_IsIdempotentAfterRepeatedBuild: first Build() failed with result {firstResult}.");

            int secondResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(secondResult, 0,
                $"GeneratedExpressionMenu_IsIdempotentAfterRepeatedBuild: second Build() failed with result {secondResult}.");

            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(rootMenu, "GeneratedExpressionMenu_IsIdempotentAfterRepeatedBuild: generated root menu is null after repeated Build().");
            Assert.IsNotNull(rootMenu.controls, "GeneratedExpressionMenu_IsIdempotentAfterRepeatedBuild: generated root menu controls list is null after repeated Build().");

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
                $"GeneratedExpressionMenu_IsIdempotentAfterRepeatedBuild: expected exactly one Settings Manager root control after two Build() calls; rootCount={rootMenu.controls.Count}, settingsManagerCount={settingsManagerCount}.");
        }

        [Test, Category("Integration")]
        public void Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper()
        {
            var generatedRoot = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(generatedRoot, "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: generated root menu is null before stale prefill.");

            generatedRoot.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            for (int i = 0; i < 8; i++)
            {
                generatedRoot.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = $"StalePreExisting_{i + 1}",
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                });
            }
            EditorUtility.SetDirty(generatedRoot);
            AssetDatabase.SaveAssets();

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: Build() failed with result {buildResult} in stale-root normalization scenario.");

            var refreshedRoot = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(refreshedRoot, "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: generated root menu is null after Build().");
            Assert.IsNotNull(refreshedRoot.controls, "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: generated root menu controls list is null after Build().");
            Assert.AreEqual(1, refreshedRoot.controls.Count,
                $"Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: saturated-root regression guard failed; generated root should be normalized to a single wrapper control. got {refreshedRoot.controls.Count}.");
            Assert.IsFalse(refreshedRoot.controls.Exists(c => c != null && c.name != null && c.name.StartsWith("StalePreExisting_")),
                "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: saturated-root regression guard failed; stale pre-existing controls must be removed on rebuild.");

            var settingsControl = refreshedRoot.controls[0];
            Assert.AreEqual("Settings Manager", settingsControl.name,
                "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: normalized generated root control must be named 'Settings Manager'.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, settingsControl.type,
                "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: normalized generated root control must be a SubMenu.");
            Assert.IsNotNull(settingsControl.subMenu,
                "Regression_SaturatedGeneratedRoot_DropsStaleControlsAndRestoresSingleWrapper: normalized generated root Settings Manager control must reference the presets submenu.");
        }
    }
}
