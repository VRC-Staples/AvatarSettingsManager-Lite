using NUnit.Framework;
using ASMLite;
using ASMLite.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Regression tests for ASMLiteBuilder public surface.
    /// These are EditMode tests: no Play mode or asset pipeline required.
    /// </summary>
    [TestFixture]
    public class ASMLiteBuilderTests
    {
        // ── Validate() ────────────────────────────────────────────────────────

        // P07: null component -- must not throw NullReferenceException
        [Test]
        public void P07_Validate_NullComponent_ReturnsError_NotNullReferenceException()
        {
            string result = null;
            Assert.DoesNotThrow(() => result = ASMLiteBuilder.Validate(null),
                "Validate(null) must not throw NullReferenceException");
            Assert.IsNotNull(result, "Validate(null) must return a non-null error string");
        }

        [Test]
        public void Validate_ReturnsNull_WhenSlotCountIsOne()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 1;

            Assert.IsNull(ASMLiteBuilder.Validate(component));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Validate_ReturnsNull_WhenSlotCountIsEight()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 8;

            Assert.IsNull(ASMLiteBuilder.Validate(component));

            Object.DestroyImmediate(go);
        }

        [Test]
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(8)]
        public void Validate_ReturnsNull_ForAllValidSlotCounts(int slotCount)
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = slotCount;

            Assert.IsNull(ASMLiteBuilder.Validate(component));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Validate_ReturnsError_WhenSlotCountIsZero()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 0;

            string result = ASMLiteBuilder.Validate(component);

            Assert.IsNotNull(result);
            StringAssert.Contains("slotCount", result);
            StringAssert.Contains("0", result);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Validate_ReturnsError_WhenSlotCountIsNine()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 9;

            string result = ASMLiteBuilder.Validate(component);

            Assert.IsNotNull(result);
            StringAssert.Contains("slotCount", result);
            StringAssert.Contains("9", result);

            Object.DestroyImmediate(go);
        }

        [Test]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(9)]
        [TestCase(100)]
        public void Validate_ReturnsError_ForAllInvalidSlotCounts(int slotCount)
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = slotCount;

            string result = ASMLiteBuilder.Validate(component);

            Assert.IsNotNull(result, $"Expected error for slotCount={slotCount}");
            StringAssert.Contains("[ASM-Lite]", result);

            Object.DestroyImmediate(go);
        }

        // ── CompactInt artifact coverage ─────────────────────────────────────

        [Test]
        public void CompactInt_MenuArtifacts_UseExpectedEncodedValues_ForFirstAndLastSlots()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                ctx.Comp.slotCount = 8;
                ASMLiteTestFixtures.AddExpressionParam(ctx, "Compact_Menu_Param", VRCExpressionParameters.ValueType.Int, 1f);

                int buildResult = ASMLiteBuilder.Build(ctx.Comp);
                Assert.GreaterOrEqual(buildResult, 0, "Compact-int artifact test setup build should succeed.");

                var rootMenu = LoadGeneratedRootMenuOrFail();
                var presetsMenu = rootMenu.controls[0].subMenu;
                Assert.IsNotNull(presetsMenu, "Generated root menu should point at the presets wrapper menu.");

                AssertSlotMenuEncoding(presetsMenu, slot: 1, expectedSave: 1f, expectedLoad: 2f, expectedClear: 3f);
                AssertSlotMenuEncoding(presetsMenu, slot: 8, expectedSave: 22f, expectedLoad: 23f, expectedClear: 24f);
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [Test]
        public void CompactInt_ControllerTransitions_UseExpectedEncodedValues_ForFirstAndLastSlots()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                ctx.Comp.slotCount = 8;
                ASMLiteTestFixtures.AddExpressionParam(ctx, "Compact_Fx_Param", VRCExpressionParameters.ValueType.Int, 1f);

                int buildResult = ASMLiteBuilder.Build(ctx.Comp);
                Assert.GreaterOrEqual(buildResult, 0, "Compact-int controller transition test setup build should succeed.");

                var controller = LoadGeneratedFxControllerOrFail();

                AssertLayerTransitionEncoding(controller, slot: 1, expectedSave: 1f, expectedLoad: 2f, expectedClear: 3f);
                AssertLayerTransitionEncoding(controller, slot: 8, expectedSave: 22f, expectedLoad: 23f, expectedClear: 24f);
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [Test]
        public void CompactInt_ArtifactValues_AreUniquePositive_AndUseSharedControlParam()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                ctx.Comp.slotCount = 8;
                ASMLiteTestFixtures.AddExpressionParam(ctx, "Compact_Unique_Param", VRCExpressionParameters.ValueType.Bool, 1f);

                int buildResult = ASMLiteBuilder.Build(ctx.Comp);
                Assert.GreaterOrEqual(buildResult, 0, "Compact-int uniqueness artifact test setup build should succeed.");

                var rootMenu = LoadGeneratedRootMenuOrFail();
                var presetsMenu = rootMenu.controls[0].subMenu;
                Assert.IsNotNull(presetsMenu, "Generated root menu should point at the presets wrapper menu.");

                var controller = LoadGeneratedFxControllerOrFail();
                var seenValues = new HashSet<float>();

                for (int slot = 1; slot <= 8; slot++)
                {
                    var slotMenu = presetsMenu.controls[slot - 1].subMenu;
                    Assert.IsNotNull(slotMenu, $"Generated slot menu for slot {slot} should exist.");

                    var saveConfirm = slotMenu.controls[0].subMenu.controls[0];
                    var loadControl = slotMenu.controls[1];
                    var clearConfirm = slotMenu.controls[2].subMenu.controls[0];

                    foreach (var control in new[] { saveConfirm, loadControl, clearConfirm })
                    {
                        Assert.AreEqual(ASMLiteBuilder.CtrlParam, control.parameter?.name,
                            $"Generated action control '{control.name}' for slot {slot} should use the shared ASM-Lite control parameter.");
                        Assert.Greater(control.value, 0f,
                            $"Generated action control '{control.name}' for slot {slot} should use a positive encoded value, leaving 0 as the idle state.");
                        Assert.IsTrue(seenValues.Add(control.value),
                            $"Generated action control value {control.value} for slot {slot} should be unique across all Save/Load/Clear buttons.");
                    }

                    var layer = FindSlotLayer(controller, slot);
                    var idleState = FindState(layer.stateMachine, "Idle");
                    Assert.IsNotNull(idleState, $"Generated slot layer {slot} should contain an Idle state.");
                    Assert.AreEqual(3, idleState.transitions.Length,
                        $"Generated slot layer {slot} should expose exactly three action transitions from Idle.");
                    Assert.IsFalse(idleState.transitions.Any(t => t.conditions.Any(c => c.threshold == 0f)),
                        $"Generated slot layer {slot} should not wire any action transition to encoded idle value 0.");
                }
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        // ── Shared control parameter naming ─────────────────────────────────

        [Test]
        public void ControlParam_Name_IsASMLiteCtrl()
        {
            // Verify the production constant has the value the runtime and menus depend on.
            Assert.AreEqual("ASMLite_Ctrl", ASMLiteBuilder.CtrlParam);
        }

        // ── Expression param schema preservation ─────────────────────────────

        [Test]
        public void BuildBackupParamNamesWithLegacyPreservation_KeepsLegacyBackups_OnSlotExpansion()
        {
            var avatarParamNames = new List<string> { "Hat", "Hue" };
            var existingNames = new[]
            {
                "ASMLite_Bak_S1_Hat",
                "ASMLite_Bak_S2_Hue",
                "ASMLite_Bak_S1_LegacyRemovedParam"
            };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 3,
                avatarParamNames: avatarParamNames,
                existingParamNames: existingNames);

            Assert.IsTrue(result.Contains("ASMLite_Bak_S3_Hat"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S3_Hue"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_Hat"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S2_Hue"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_LegacyRemovedParam"));

            var duplicateCount = result.GroupBy(n => n).Count(g => g.Count() > 1);
            Assert.AreEqual(0, duplicateCount);
        }

        [Test]
        public void BuildBackupParamNamesWithLegacyPreservation_IgnoresLegacyControlParams()
        {
            var avatarParamNames = new List<string> { "ToggleA" };
            var existingNames = new[]
            {
                "ASMLite_S1_Save",
                "ASMLite_Ctrl",
                "ASMLite_Bak_S1_Obsolete"
            };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 1,
                avatarParamNames: avatarParamNames,
                existingParamNames: existingNames);

            Assert.IsFalse(result.Contains("ASMLite_S1_Save"), "Legacy SafeBool control key should not be preserved");
            Assert.IsFalse(result.Contains("ASMLite_Ctrl"), "Legacy shared control key should not be preserved as a backup entry");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_Obsolete"), "Legacy backup key should be preserved");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_ToggleA"), "Current backup key should be present");
        }

        // ── P01-P06: BuildBackupParamNamesWithLegacyPreservation edge cases ──

        // P01: slot decrease -- S3/S4 legacy backups preserved when slotCount drops to 2
        [Test]
        public void P01_BuildBackupParamNames_SlotDecrease_LegacySlotBackupsPreserved()
        {
            var avatarParamNames = new List<string> { "Hat", "Hue" };
            var existingNames = new[]
            {
                "ASMLite_Bak_S1_Hat",
                "ASMLite_Bak_S1_Hue",
                "ASMLite_Bak_S2_Hat",
                "ASMLite_Bak_S2_Hue",
                "ASMLite_Bak_S3_Hat",
                "ASMLite_Bak_S3_Hue",
                "ASMLite_Bak_S4_Hat",
                "ASMLite_Bak_S4_Hue",
            };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 2,
                avatarParamNames: avatarParamNames,
                existingParamNames: existingNames);

            // Current schema (slots 1-2) must be present.
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_Hat"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_Hue"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S2_Hat"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S2_Hue"));

            // S3/S4 backups (now outside active slot range) preserved as legacy.
            Assert.IsTrue(result.Contains("ASMLite_Bak_S3_Hat"), "S3 legacy backup must be preserved");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S3_Hue"), "S3 legacy backup must be preserved");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S4_Hat"), "S4 legacy backup must be preserved");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S4_Hue"), "S4 legacy backup must be preserved");

            // No duplicates.
            var dupeCount = result.GroupBy(n => n).Count(g => g.Count() > 1);
            Assert.AreEqual(0, dupeCount, "Result must not contain duplicate names");
        }

        // P02: null existingParamNames -- first build, no prior asset
        [Test]
        public void P02_BuildBackupParamNames_NullExistingParamNames_ReturnsCurrentSchemaOnly()
        {
            var avatarParamNames = new List<string> { "ToggleHat", "HairHue" };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 2,
                avatarParamNames: avatarParamNames,
                existingParamNames: null);

            Assert.AreEqual(4, result.Count, "2 slots x 2 params = 4 entries");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_ToggleHat"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_HairHue"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S2_ToggleHat"));
            Assert.IsTrue(result.Contains("ASMLite_Bak_S2_HairHue"));
        }

        // P03: empty existingParamNames array -- same as first build, no crash
        [Test]
        public void P03_BuildBackupParamNames_EmptyExistingParamNames_ReturnsCurrentSchemaOnly()
        {
            var avatarParamNames = new List<string> { "ToggleHat" };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 1,
                avatarParamNames: avatarParamNames,
                existingParamNames: new string[0]);

            Assert.AreEqual(1, result.Count, "1 slot x 1 param = 1 entry");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_ToggleHat"));
        }

        // P04: duplicate names in existingParamNames -- result must deduplicate
        [Test]
        public void P04_BuildBackupParamNames_DuplicatesInExisting_ResultIsDeduped()
        {
            var avatarParamNames = new List<string> { "Hat" };
            // Stale asset contains the same entry twice.
            var existingNames = new[]
            {
                "ASMLite_Bak_S1_OldParam",
                "ASMLite_Bak_S1_OldParam",  // duplicate
            };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 1,
                avatarParamNames: avatarParamNames,
                existingParamNames: existingNames);

            var oldParamOccurrences = result.Count(n => n == "ASMLite_Bak_S1_OldParam");
            Assert.AreEqual(1, oldParamOccurrences, "Duplicate existing entry must appear exactly once");

            // No duplicates anywhere.
            var dupeCount = result.GroupBy(n => n).Count(g => g.Count() > 1);
            Assert.AreEqual(0, dupeCount, "Result must not contain any duplicate names");
        }

        // P05: empty avatarParamNames -- avatar has no expression params
        [Test]
        public void P05_BuildBackupParamNames_EmptyAvatarParamNames_ReturnsLegacyOnlyOrEmpty()
        {
            var avatarParamNames = new List<string>();
            var existingNames = new[]
            {
                "ASMLite_Bak_S1_OldParam",
            };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 2,
                avatarParamNames: avatarParamNames,
                existingParamNames: existingNames);

            // No current-schema entries (no avatar params to back up).
            Assert.AreEqual(0, result.Count(n => n.StartsWith("ASMLite_Bak_S1_") && n != "ASMLite_Bak_S1_OldParam"),
                "No current schema entries expected when avatarParamNames is empty");

            // Legacy backup still preserved.
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_OldParam"), "Legacy backup must be preserved even with empty avatar params");
        }

        // P06: mixed collision -- legacy backups that match the current schema are not duplicated;
        //      legacy backups for removed params (not in current schema) are appended.
        [Test]
        public void P06_BuildBackupParamNames_MixedCollision_CurrentSchemaWinsNoDuplicates()
        {
            var avatarParamNames = new List<string> { "ActiveParam" };
            var existingNames = new[]
            {
                // Collides with current schema -- must appear exactly once.
                "ASMLite_Bak_S1_ActiveParam",
                // Does not collide -- legacy removed param, must be appended.
                "ASMLite_Bak_S1_RemovedParam",
            };

            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 1,
                avatarParamNames: avatarParamNames,
                existingParamNames: existingNames);

            // Current schema entry present once.
            var activeCount = result.Count(n => n == "ASMLite_Bak_S1_ActiveParam");
            Assert.AreEqual(1, activeCount, "Current schema name must appear exactly once (no dup from existing)");

            // Legacy removed param appended.
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_RemovedParam"), "Legacy removed param must be preserved");

            // No duplicates.
            var dupeCount = result.GroupBy(n => n).Count(g => g.Count() > 1);
            Assert.AreEqual(0, dupeCount, "Result must not contain duplicate names");
        }

        [Test]
        public void P07_BuildBackupParamNames_MalformedLegacyBackups_AreExcluded()
        {
            var result = ASMLiteBuilder.BuildBackupParamNamesWithLegacyPreservation(
                slotCount: 1,
                avatarParamNames: new List<string> { "Current" },
                existingParamNames: new[]
                {
                    "ASMLite_Bak_S1_ValidLegacy",
                    "ASMLite_Bak_S_",
                    "ASMLite_Bak_SA_NotANumber",
                    "ASMLite_Bak_S2",
                    "ASMLite_Bak_S0_ZeroSlot",
                });

            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_Current"), "Current schema backup should be generated.");
            Assert.IsTrue(result.Contains("ASMLite_Bak_S1_ValidLegacy"), "Valid legacy backup should still be preserved.");
            Assert.IsFalse(result.Contains("ASMLite_Bak_S_"), "Malformed backup without slot/source should be dropped.");
            Assert.IsFalse(result.Contains("ASMLite_Bak_SA_NotANumber"), "Malformed backup with non-numeric slot should be dropped.");
            Assert.IsFalse(result.Contains("ASMLite_Bak_S2"), "Malformed backup without source segment should be dropped.");
            Assert.IsFalse(result.Contains("ASMLite_Bak_S0_ZeroSlot"), "Malformed backup with slot 0 should be dropped.");
        }

        private static VRCExpressionsMenu LoadGeneratedRootMenuOrFail()
        {
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(menu, $"Generated menu missing at '{ASMLiteAssetPaths.Menu}'.");
            Assert.IsNotNull(menu.controls, "Generated root menu controls must not be null.");
            Assert.IsNotEmpty(menu.controls, "Generated root menu should contain the ASM-Lite wrapper control.");
            return menu;
        }

        private static AnimatorController LoadGeneratedFxControllerOrFail()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(controller, $"Generated FX controller missing at '{ASMLiteAssetPaths.FXController}'.");
            return controller;
        }

        private static void AssertSlotMenuEncoding(
            VRCExpressionsMenu presetsMenu,
            int slot,
            float expectedSave,
            float expectedLoad,
            float expectedClear)
        {
            var slotControl = presetsMenu.controls[slot - 1];
            Assert.IsNotNull(slotControl, $"Generated presets menu should contain a slot control for slot {slot}.");
            Assert.IsNotNull(slotControl.subMenu, $"Generated slot control for slot {slot} should point at a slot submenu.");

            var slotMenu = slotControl.subMenu;
            Assert.AreEqual(3, slotMenu.controls.Count,
                $"Generated slot submenu for slot {slot} should expose Save, Load, and Clear actions.");

            var saveControl = slotMenu.controls[0];
            var loadControl = slotMenu.controls[1];
            var clearControl = slotMenu.controls[2];

            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, saveControl.type,
                $"Generated Save control for slot {slot} should remain a confirmation submenu.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.Button, loadControl.type,
                $"Generated Load control for slot {slot} should remain a direct trigger button.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, clearControl.type,
                $"Generated Clear control for slot {slot} should remain a confirmation submenu.");

            Assert.AreEqual(expectedSave, saveControl.subMenu.controls[0].value,
                $"Generated Save confirm button for slot {slot} should use the expected encoded control value.");
            Assert.AreEqual(expectedLoad, loadControl.value,
                $"Generated Load button for slot {slot} should use the expected encoded control value.");
            Assert.AreEqual(expectedClear, clearControl.subMenu.controls[0].value,
                $"Generated Clear confirm button for slot {slot} should use the expected encoded control value.");
        }

        private static AnimatorControllerLayer FindSlotLayer(AnimatorController controller, int slot)
        {
            var layer = controller.layers.FirstOrDefault(l => l.name == $"ASMLite_Slot{slot}");
            Assert.IsNotNull(layer.stateMachine, $"Generated FX controller should contain layer ASMLite_Slot{slot}.");
            return layer;
        }

        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            return stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state != null && state.name == stateName);
        }

        private static void AssertLayerTransitionEncoding(
            AnimatorController controller,
            int slot,
            float expectedSave,
            float expectedLoad,
            float expectedClear)
        {
            var layer = FindSlotLayer(controller, slot);
            var idleState = FindState(layer.stateMachine, "Idle");
            var saveState = FindState(layer.stateMachine, $"SaveSlot{slot}");
            var loadState = FindState(layer.stateMachine, $"LoadSlot{slot}");
            var clearState = FindState(layer.stateMachine, $"ResetSlot{slot}");

            Assert.IsNotNull(idleState, $"Generated FX slot layer {slot} should contain an Idle state.");
            Assert.IsNotNull(saveState, $"Generated FX slot layer {slot} should contain a SaveSlot{slot} state.");
            Assert.IsNotNull(loadState, $"Generated FX slot layer {slot} should contain a LoadSlot{slot} state.");
            Assert.IsNotNull(clearState, $"Generated FX slot layer {slot} should contain a ResetSlot{slot} state.");

            AssertTransitionCondition(idleState, saveState, expectedSave, slot, "Save");
            AssertTransitionCondition(idleState, loadState, expectedLoad, slot, "Load");
            AssertTransitionCondition(idleState, clearState, expectedClear, slot, "Clear");
        }

        private static void AssertTransitionCondition(
            AnimatorState sourceState,
            AnimatorState destinationState,
            float expectedValue,
            int slot,
            string actionLabel)
        {
            var transition = sourceState.transitions.FirstOrDefault(t => t.destinationState == destinationState);
            Assert.IsNotNull(transition,
                $"Generated Idle state for slot {slot} should transition to {destinationState.name} for the {actionLabel} action.");
            Assert.AreEqual(1, transition.conditions.Length,
                $"Generated {actionLabel} transition for slot {slot} should use exactly one control-parameter condition.");
            Assert.AreEqual(ASMLiteBuilder.CtrlParam, transition.conditions[0].parameter,
                $"Generated {actionLabel} transition for slot {slot} should use the shared ASM-Lite control parameter.");
            Assert.AreEqual(AnimatorConditionMode.Equals, transition.conditions[0].mode,
                $"Generated {actionLabel} transition for slot {slot} should use an Equals condition.");
            Assert.AreEqual(expectedValue, transition.conditions[0].threshold,
                $"Generated {actionLabel} transition for slot {slot} should use the expected encoded control value.");
        }
    }
}
