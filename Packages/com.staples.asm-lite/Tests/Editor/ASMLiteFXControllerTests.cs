using NUnit.Framework;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using ASMLite;
using ASMLite.Editor;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A05-A18: FX controller state machine invariants.
    /// Integration category: each test calls Build() and inspects the generated
    /// FX AnimatorController at ASMLiteAssetPaths.FXController.
    /// </summary>
    [TestFixture]
    public class ASMLiteFXControllerTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddParam(AsmLiteTestContext ctx, string name,
            VRCExpressionParameters.ValueType type, float defaultValue = 0f)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRCExpressionParameters.Parameter[0];
            var updated  = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name         = name,
                valueType    = type,
                defaultValue = defaultValue,
                saved        = true,
                networkSynced = true,
            };
            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        private static AnimatorController LoadGeneratedController(string aid)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(ctrl, $"{aid}: generated FX controller must exist at '{ASMLiteAssetPaths.FXController}'.");
            return ctrl;
        }

        private static AnimatorStateMachine GetLayerSM(AnimatorController ctrl, string layerName)
        {
            var layer = ctrl.layers.FirstOrDefault(l => l.name == layerName);
            Assert.IsNotNull(layer.stateMachine,
                $"Layer '{layerName}' not found in controller.");
            return layer.stateMachine;
        }

        private static AnimatorState FindState(AnimatorStateMachine sm, string stateName)
        {
            var child = sm.states.FirstOrDefault(s => s.state.name == stateName);
            Assert.IsNotNull(child.state, $"State '{stateName}' not found in state machine.");
            return child.state;
        }

        private static VRC_AvatarParameterDriver LoadSlotDriver(AnimatorController ctrl, int slot, string stateName)
        {
            var sm = GetLayerSM(ctrl, $"ASMLite_Slot{slot}");
            var state = FindState(sm, stateName);
            var driver = state.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();
            Assert.IsNotNull(driver, $"Slot {slot} state '{stateName}' must have a VRCAvatarParameterDriver.");
            return driver;
        }

        private static bool HasCopy(VRC_AvatarParameterDriver driver, string source, string destination)
            => driver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == source && p.name == destination);

        // ── A05 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A05_Build_CreatesCorrectNumberOfASMLiteLayers()
        {
            _ctx.Comp.slotCount = 2;
            int result = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(result, 0);

            var genCtrl = LoadGeneratedController("A05");
            int layerCount = genCtrl.layers.Count(l => l.name.StartsWith("ASMLite_"));
            Assert.AreEqual(2, layerCount,
                "Should create exactly slotCount ASMLite_ layers in the generated FX controller.");
        }

        // ── A06 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A06_EachSlotLayer_HasFourStates()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A06");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");
                Assert.AreEqual(4, sm.states.Length,
                    $"Slot {slot} layer should have exactly 4 states.");
            }
        }

        // ── A07 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A07_EachSlotLayer_DefaultStateIsIdle()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Float);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A07");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");
                Assert.IsNotNull(sm.defaultState, $"Slot {slot}: defaultState is null.");
                Assert.AreEqual("Idle", sm.defaultState.name,
                    $"Slot {slot}: defaultState should be named 'Idle'.");
            }
        }

        // ── A08 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A08_AllStates_WriteDefaultValuesIsFalse()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Bool);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A08");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");
                foreach (var childState in sm.states)
                {
                    Assert.IsFalse(childState.state.writeDefaultValues,
                        $"Slot {slot} state '{childState.state.name}': writeDefaultValues must be false.");
                }
            }
        }

        // ── A09 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A09_IdleTransitions_UseCorrectEncodedValues()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A09");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm        = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");
                var idleState = FindState(sm, "Idle");

                int saveValue  = (slot - 1) * 3 + 1;
                int loadValue  = (slot - 1) * 3 + 2;
                int clearValue = (slot - 1) * 3 + 3;

                Assert.AreEqual(3, idleState.transitions.Length,
                    $"Slot {slot} Idle should have 3 outgoing transitions.");

                var condValues = idleState.transitions
                    .Select(t => t.conditions[0])
                    .ToArray();

                foreach (var cond in condValues)
                {
                    Assert.AreEqual(AnimatorConditionMode.Equals, cond.mode,
                        $"Slot {slot}: all Idle transition conditions must use Equals mode.");
                    Assert.AreEqual("ASMLite_Ctrl", cond.parameter,
                        $"Slot {slot}: Idle transition parameter must be 'ASMLite_Ctrl'.");
                }

                var condValuesList = condValues.Select(c => (int)c.threshold).ToList();
                CollectionAssert.Contains(condValuesList, saveValue,
                    $"Slot {slot}: save value {saveValue} not found in Idle transitions.");
                CollectionAssert.Contains(condValuesList, loadValue,
                    $"Slot {slot}: load value {loadValue} not found in Idle transitions.");
                CollectionAssert.Contains(condValuesList, clearValue,
                    $"Slot {slot}: clear value {clearValue} not found in Idle transitions.");
            }
        }

        // ── A10 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A10_SaveDriver_HasCorrectCopyEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl   = LoadGeneratedController("A10");
            var sm        = GetLayerSM(genCtrl, "ASMLite_Slot1");
            var saveState = FindState(sm, "SaveSlot1");
            var driver    = saveState.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();

            Assert.IsNotNull(driver, "SaveSlot1 must have a VRCAvatarParameterDriver.");

            var copyEntries = driver.parameters
                .Where(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                .ToList();

            Assert.AreEqual(1, copyEntries.Count,
                "Save driver should have 1 Copy entry for 1 avatar param.");
            Assert.AreEqual("MyFloat", copyEntries[0].source,
                "Save Copy source should be the avatar param name.");
            Assert.AreEqual("ASMLite_Bak_S1_MyFloat", copyEntries[0].name,
                "Save Copy destination should be the backup param name.");
        }

        // ── A11 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A11_LoadDriver_HasCorrectCopyEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyInt", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl   = LoadGeneratedController("A11");
            var sm        = GetLayerSM(genCtrl, "ASMLite_Slot1");
            var loadState = FindState(sm, "LoadSlot1");
            var driver    = loadState.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();

            Assert.IsNotNull(driver, "LoadSlot1 must have a VRCAvatarParameterDriver.");

            var copyEntries = driver.parameters
                .Where(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                .ToList();

            Assert.AreEqual(1, copyEntries.Count,
                "Load driver should have 1 Copy entry for 1 avatar param.");
            Assert.AreEqual("ASMLite_Bak_S1_MyInt", copyEntries[0].source,
                "Load Copy source should be the backup param name.");
            Assert.AreEqual("MyInt", copyEntries[0].name,
                "Load Copy destination should be the avatar param name.");
        }

        // ── A12 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A12_ResetDriver_HasCorrectCopyEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyBool", VRCExpressionParameters.ValueType.Bool);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl    = LoadGeneratedController("A12");
            var sm         = GetLayerSM(genCtrl, "ASMLite_Slot1");
            var resetState = FindState(sm, "ResetSlot1");
            var driver     = resetState.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();

            Assert.IsNotNull(driver, "ResetSlot1 must have a VRCAvatarParameterDriver.");

            var copyEntries = driver.parameters
                .Where(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy)
                .ToList();

            Assert.AreEqual(1, copyEntries.Count,
                "Reset driver should have 1 Copy entry for 1 avatar param.");
            Assert.AreEqual("ASMLite_Def_MyBool", copyEntries[0].source,
                "Reset Copy source should be the default param name.");
            Assert.AreEqual("ASMLite_Bak_S1_MyBool", copyEntries[0].name,
                "Reset Copy destination should be the backup param name.");
        }

        // ── A13 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A13_AllDrivers_EndWithSetCtrlToZero()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A13");
            var sm = GetLayerSM(genCtrl, "ASMLite_Slot1");

            foreach (var stateName in new[] { "SaveSlot1", "LoadSlot1", "ResetSlot1" })
            {
                var state  = FindState(sm, stateName);
                var driver = state.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();
                Assert.IsNotNull(driver, $"{stateName} must have a VRCAvatarParameterDriver.");

                var last = driver.parameters[driver.parameters.Count - 1];
                Assert.AreEqual(VRC_AvatarParameterDriver.ChangeType.Set, last.type,
                    $"{stateName}: last driver entry must be a Set.");
                Assert.AreEqual("ASMLite_Ctrl", last.name,
                    $"{stateName}: last Set entry must target 'ASMLite_Ctrl'.");
                Assert.AreEqual(0f, last.value,
                    $"{stateName}: last Set entry value must be 0.");
            }
        }

        // ── A14 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A14_AllDrivers_HaveLocalOnlyTrue()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A14");
            var sm = GetLayerSM(genCtrl, "ASMLite_Slot1");

            foreach (var stateName in new[] { "SaveSlot1", "LoadSlot1", "ResetSlot1" })
            {
                var state  = FindState(sm, stateName);
                var driver = state.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();
                Assert.IsNotNull(driver, $"{stateName} must have a VRCAvatarParameterDriver.");
                Assert.IsTrue(driver.localOnly,
                    $"{stateName}: VRCAvatarParameterDriver.localOnly must be true.");
            }
        }

        // ── A15 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A15_CtrlParameters_ContainAllExpectedEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "X", VRCExpressionParameters.ValueType.Int);
            int result = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(result, 0);

            var genCtrl = LoadGeneratedController("A15");
            var paramNames = genCtrl.parameters.Select(p => p.name).ToHashSet();

            Assert.IsTrue(paramNames.Contains("ASMLite_Ctrl"),
                "Generated FX controller must contain 'ASMLite_Ctrl'.");
            Assert.IsTrue(paramNames.Contains("X"),
                "Generated FX controller must contain the avatar param 'X'.");
            Assert.IsTrue(paramNames.Contains("ASMLite_Bak_S1_X"),
                "Generated FX controller must contain 'ASMLite_Bak_S1_X'.");
            Assert.IsTrue(paramNames.Contains("ASMLite_Def_X"),
                "Generated FX controller must contain 'ASMLite_Def_X'.");
        }

        // ── A16 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A16_DuplicateAvatarParam_OnlyOneEntryInCtrlParameters()
        {
            _ctx.Comp.slotCount = 1;

            _ctx.ParamsAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter { name = "DupParam", valueType = VRCExpressionParameters.ValueType.Int },
                new VRCExpressionParameters.Parameter { name = "DupParam", valueType = VRCExpressionParameters.ValueType.Int },
            };
            EditorUtility.SetDirty(_ctx.ParamsAsset);
            AssetDatabase.SaveAssets();

            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A16");
            int count = genCtrl.parameters.Count(p => p.name == "DupParam");
            Assert.AreEqual(1, count,
                "Dedup guard: duplicate avatar param name must appear only once in generated FX controller parameters.");
        }

        // ── A17 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A17_PreExistingDuplicateASMLiteParams_DrainedBeforeBuild()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            // Pre-inject duplicates into the generated FX controller to simulate
            // a stale asset from a prior broken build.
            var genCtrlPre = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(genCtrlPre, "A17: generated FX controller must exist before pre-injection.");
            genCtrlPre.AddParameter("ASMLite_Bak_S1_MyParam", AnimatorControllerParameterType.Int);
            genCtrlPre.AddParameter("ASMLite_Bak_S1_MyParam", AnimatorControllerParameterType.Int);

            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A17");
            var asmParams = genCtrl.parameters
                .Where(p => p.name.StartsWith("ASMLite_"))
                .Select(p => p.name)
                .ToList();

            var duplicates = asmParams
                .GroupBy(n => n)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Assert.IsEmpty(duplicates,
                $"After Build(), no ASMLite_ param should be duplicated. Found duplicates: {string.Join(", ", duplicates)}");
        }

        // ── A18 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A18_DefParam_HasCorrectDefaultValues()
        {
            _ctx.Comp.slotCount = 1;

            _ctx.ParamsAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name         = "MyInt",
                    valueType    = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 7f,
                    saved        = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name         = "MyFloat",
                    valueType    = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.5f,
                    saved        = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name         = "MyBool",
                    valueType    = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved        = true,
                    networkSynced = true,
                },
            };
            EditorUtility.SetDirty(_ctx.ParamsAsset);
            AssetDatabase.SaveAssets();

            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A18");
            var paramsByName = genCtrl.parameters.ToDictionary(p => p.name);

            Assert.IsTrue(paramsByName.ContainsKey("ASMLite_Def_MyInt"), "ASMLite_Def_MyInt not found.");
            Assert.AreEqual(7, paramsByName["ASMLite_Def_MyInt"].defaultInt,
                "ASMLite_Def_MyInt.defaultInt should be 7.");

            Assert.IsTrue(paramsByName.ContainsKey("ASMLite_Def_MyFloat"), "ASMLite_Def_MyFloat not found.");
            Assert.AreEqual(0.5f, paramsByName["ASMLite_Def_MyFloat"].defaultFloat, 0.0001f,
                "ASMLite_Def_MyFloat.defaultFloat should be 0.5.");

            Assert.IsTrue(paramsByName.ContainsKey("ASMLite_Def_MyBool"), "ASMLite_Def_MyBool not found.");
            Assert.IsTrue(paramsByName["ASMLite_Def_MyBool"].defaultBool,
                "ASMLite_Def_MyBool.defaultBool should be true (defaultValue=1).");
        }

        // ── A19 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A19_FXController_UsesOnlyOneSharedCtrlParam_NoLegacyControlParams()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A19");
            var ctrlParamEntries = genCtrl.parameters
                .Where(p => p.name == "ASMLite_Ctrl")
                .ToList();
            Assert.AreEqual(1, ctrlParamEntries.Count,
                "Generated FX controller should contain exactly one shared ASMLite_Ctrl parameter.");
            Assert.AreEqual(AnimatorControllerParameterType.Int, ctrlParamEntries[0].type,
                "ASMLite_Ctrl must remain an Int parameter in the generated FX controller.");

            var forbiddenLegacyControlParams = new[]
            {
                "ASMLite_Save",
                "ASMLite_Load",
                "ASMLite_Clear",
                "ASMLite_S1_Save",
                "ASMLite_S1_Load",
                "ASMLite_S1_Clear",
            };

            foreach (var forbiddenName in forbiddenLegacyControlParams)
            {
                Assert.IsFalse(genCtrl.parameters.Any(p => p.name == forbiddenName),
                    $"Legacy control param '{forbiddenName}' must not be emitted in the generated FX controller.");
            }
        }

        // ── A20 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A20_SlotLayers_DoNotUseAnyStateSafeBoolBranches()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Bool);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("A20");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");

                Assert.AreEqual(0, sm.anyStateTransitions.Length,
                    $"Slot {slot} should not create Any State transitions (legacy SafeBool branch style).");

                foreach (var stateName in new[] { "SaveSlot" + slot, "LoadSlot" + slot, "ResetSlot" + slot })
                {
                    var state = FindState(sm, stateName);
                    foreach (var transition in state.transitions)
                    {
                        foreach (var condition in transition.conditions)
                        {
                            Assert.AreNotEqual(AnimatorConditionMode.If, condition.mode,
                                $"Slot {slot} state '{stateName}' transition must not use bool If conditions.");
                            Assert.AreNotEqual(AnimatorConditionMode.IfNot, condition.mode,
                                $"Slot {slot} state '{stateName}' transition must not use bool IfNot conditions.");
                        }
                    }
                }
            }
        }

        [Test, Category("Integration")]
        public void A21_ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "KeepA", VRCExpressionParameters.ValueType.Int);
            AddParam(_ctx, "DropB", VRCExpressionParameters.ValueType.Float);
            AddParam(_ctx, "DropC", VRCExpressionParameters.ValueType.Bool);

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "DropB", " DropC ", "DropB", "GhostMissing" };

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                "A21: Build() should return only non-excluded discovered params.");

            var genCtrl = LoadGeneratedController("A21");
            var allNames = genCtrl.parameters.Select(p => p.name).ToHashSet();

            Assert.IsTrue(allNames.Contains("KeepA"), "A21: non-excluded live param should remain declared in FX params.");
            Assert.IsTrue(allNames.Contains("ASMLite_Def_KeepA"), "A21: non-excluded default param should remain declared in FX params.");
            Assert.IsTrue(allNames.Contains("ASMLite_Bak_S1_KeepA"), "A21: non-excluded slot backup should remain declared in FX params.");
            Assert.IsTrue(allNames.Contains("ASMLite_Bak_S2_KeepA"), "A21: non-excluded slot backup should remain declared in FX params.");

            Assert.IsFalse(allNames.Contains("DropB"), "A21: excluded live param must not be declared in FX params.");
            Assert.IsFalse(allNames.Contains("DropC"), "A21: excluded live param must not be declared in FX params.");

            foreach (int slot in new[] { 1, 2 })
            {
                Assert.IsFalse(allNames.Contains($"ASMLite_Bak_S{slot}_DropB"), $"A21: excluded backup key ASMLite_Bak_S{slot}_DropB must be omitted.");
                Assert.IsFalse(allNames.Contains($"ASMLite_Bak_S{slot}_DropC"), $"A21: excluded backup key ASMLite_Bak_S{slot}_DropC must be omitted.");
            }
            Assert.IsFalse(allNames.Contains("ASMLite_Def_DropB"), "A21: excluded default key ASMLite_Def_DropB must be omitted.");
            Assert.IsFalse(allNames.Contains("ASMLite_Def_DropC"), "A21: excluded default key ASMLite_Def_DropC must be omitted.");

            for (int slot = 1; slot <= 2; slot++)
            {
                var saveDriver = LoadSlotDriver(genCtrl, slot, $"SaveSlot{slot}");
                var loadDriver = LoadSlotDriver(genCtrl, slot, $"LoadSlot{slot}");
                var resetDriver = LoadSlotDriver(genCtrl, slot, $"ResetSlot{slot}");

                Assert.IsTrue(HasCopy(saveDriver, "KeepA", $"ASMLite_Bak_S{slot}_KeepA"),
                    $"A21: Save driver for slot {slot} must keep non-excluded copy wiring.");
                Assert.IsTrue(HasCopy(loadDriver, $"ASMLite_Bak_S{slot}_KeepA", "KeepA"),
                    $"A21: Load driver for slot {slot} must keep non-excluded copy wiring.");
                Assert.IsTrue(HasCopy(resetDriver, "ASMLite_Def_KeepA", $"ASMLite_Bak_S{slot}_KeepA"),
                    $"A21: Reset driver for slot {slot} must keep non-excluded clear wiring.");

                Assert.IsFalse(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && (p.source == "DropB" || p.source == "DropC" || p.name.Contains("DropB") || p.name.Contains("DropC"))),
                    $"A21: Save driver for slot {slot} must not contain excluded-source copy entries.");
                Assert.IsFalse(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && (p.source.Contains("DropB") || p.source.Contains("DropC") || p.name == "DropB" || p.name == "DropC")),
                    $"A21: Load driver for slot {slot} must not contain excluded-source copy entries.");
                Assert.IsFalse(resetDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && (p.source.Contains("DropB") || p.source.Contains("DropC") || p.name.Contains("DropB") || p.name.Contains("DropC"))),
                    $"A21: Reset driver for slot {slot} must not contain excluded-source copy entries.");
            }
        }
    }
}
