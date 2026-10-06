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
    /// FX controller state machine invariants.
    /// Integration category: each test calls Build() and inspects the generated
    /// FX AnimatorController at ASMLiteAssetPaths.FXController.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteFxControllerGenerationIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteFxControllerGenerationIntegrationTests);
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
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
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

        [Test, Category("Integration")]
        public void Build_CreatesCorrectNumberOfASMLiteLayers()
        {
            _ctx.Comp.slotCount = 2;
            int result = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(result, 0);

            var genCtrl = LoadGeneratedController("Build_CreatesCorrectNumberOfASMLiteLayers");
            int layerCount = genCtrl.layers.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxLayer);
            Assert.AreEqual(2, layerCount,
                "Should create exactly slotCount ASMLite_ layers in the generated FX controller.");
        }

        [Test, Category("Integration")]
        public void EachSlotLayer_HasFourStates()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("EachSlotLayer_HasFourStates");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");
                Assert.AreEqual(4, sm.states.Length,
                    $"Slot {slot} layer should have exactly 4 states.");
            }
        }

        [Test, Category("Integration")]
        public void EachSlotLayer_DefaultStateIsIdle()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Float);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("EachSlotLayer_DefaultStateIsIdle");
            for (int slot = 1; slot <= 2; slot++)
            {
                var sm = GetLayerSM(genCtrl, $"ASMLite_Slot{slot}");
                Assert.IsNotNull(sm.defaultState, $"Slot {slot}: defaultState is null.");
                Assert.AreEqual("Idle", sm.defaultState.name,
                    $"Slot {slot}: defaultState should be named 'Idle'.");
            }
        }

        [Test, Category("Integration")]
        public void AllStates_WriteDefaultValuesIsFalse()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Bool);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("AllStates_WriteDefaultValuesIsFalse");
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

        [Test, Category("Integration")]
        public void IdleTransitions_UseCorrectEncodedValues()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("IdleTransitions_UseCorrectEncodedValues");
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

        [Test, Category("Integration")]
        public void SaveDriver_HasCorrectCopyEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl   = LoadGeneratedController("SaveDriver_HasCorrectCopyEntries");
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

        [Test, Category("Integration")]
        public void LoadDriver_HasCorrectCopyEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyInt", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl   = LoadGeneratedController("LoadDriver_HasCorrectCopyEntries");
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

        [Test, Category("Integration")]
        public void ResetDriver_SetsConfiguredTypedDefaultsOncePerDestination()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "FalseBool", VRCExpressionParameters.ValueType.Bool, 0f);
            AddParam(_ctx, "TrueBool", VRCExpressionParameters.ValueType.Bool, -2f);
            AddParam(_ctx, "MyInt", VRCExpressionParameters.ValueType.Int, 7.75f);
            AddParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float, 0.375f);
            Assert.AreEqual(4, ASMLiteBuilder.Build(_ctx.Comp));

            var genCtrl = LoadGeneratedController(nameof(ResetDriver_SetsConfiguredTypedDefaultsOncePerDestination));
            var driver = LoadSlotDriver(genCtrl, 1, "ResetSlot1");
            Assert.IsTrue(driver.localOnly);
            Assert.AreEqual(9, driver.parameters.Count, "Four settings need two destinations each, then control reset.");
            Assert.IsTrue(driver.parameters.All(entry => entry.type == VRC_AvatarParameterDriver.ChangeType.Set));
            CollectionAssert.AreEquivalent(
                new[]
                {
                    ("FalseBool", 0f), ("ASMLite_Bak_S1_FalseBool", 0f),
                    ("TrueBool", 1f), ("ASMLite_Bak_S1_TrueBool", 1f),
                    ("MyInt", 7f), ("ASMLite_Bak_S1_MyInt", 7f),
                    ("MyFloat", 0.375f), ("ASMLite_Bak_S1_MyFloat", 0.375f),
                    ("ASMLite_Ctrl", 0f),
                },
                driver.parameters.Select(entry => (entry.name, entry.value)).ToArray());
            Assert.AreEqual("ASMLite_Ctrl", driver.parameters.Last().name);
        }

        [Test, Category("Integration")]
        public void AllDrivers_EndWithSetCtrlToZero()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("AllDrivers_EndWithSetCtrlToZero");
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

        [Test, Category("Integration")]
        public void AllDrivers_HaveLocalOnlyTrue()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("AllDrivers_HaveLocalOnlyTrue");
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

        [Test, Category("Integration")]
        public void CtrlParameters_ContainAllExpectedEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "X", VRCExpressionParameters.ValueType.Int);
            int result = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(result, 0);

            var genCtrl = LoadGeneratedController("CtrlParameters_ContainAllExpectedEntries");
            var paramNames = genCtrl.parameters.Select(p => p.name).ToHashSet();

            Assert.IsTrue(paramNames.Contains("ASMLite_Ctrl"),
                "Generated FX controller must contain 'ASMLite_Ctrl'.");
            Assert.IsTrue(paramNames.Contains("X"),
                "Generated FX controller must contain the avatar param 'X'.");
            Assert.IsFalse(paramNames.Contains("ASMLite_Bak_S1_X"),
                "Backup-only storage must not be duplicated in the FX parameter table.");
            Assert.IsTrue(paramNames.Contains("ASMLite_Def_X"),
                "Generated FX controller must contain 'ASMLite_Def_X'.");
        }

        [TestCase(1, false)]
        [TestCase(3, false)]
        [TestCase(8, false)]
        [TestCase(3, true)]
        [TestCase(8, true)]
        public void BackupStorage_RemainsExpressionOnly_WithAllSlotDrivers(int slotCount, bool directDelivery)
        {
            _ctx.Comp.slotCount = slotCount;
            AddParam(_ctx, "KeepInt", VRCExpressionParameters.ValueType.Int, 7f);
            AddParam(_ctx, "KeepFloat", VRCExpressionParameters.ValueType.Float, 0.25f);
            AddParam(_ctx, "KeepBool", VRCExpressionParameters.ValueType.Bool, 1f);

            var sources = _ctx.ParamsAsset.parameters.ToArray();
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.AreEqual(sources.Length, ASMLiteBuilder.Build(_ctx.Comp));
            AnimatorController ctrl = LoadGeneratedController(nameof(BackupStorage_RemainsExpressionOnly_WithAllSlotDrivers));
            VRCExpressionParameters expression = generatedExpr;
            if (directDelivery)
            {
                // Exercise removal of old injected declarations, not only fresh generation.
                _ctx.Ctrl.AddParameter("ASMLite_Bak_S3_KeepFloat", AnimatorControllerParameterType.Float);
                Assert.IsTrue(ASMLiteBuilder.TryDetachToDirectDelivery(_ctx.Comp, out string detail), detail);
                ctrl = _ctx.Ctrl;
                expression = _ctx.ParamsAsset;
            }
            Assert.IsFalse(ctrl.parameters.Any(p => p.name.StartsWith("ASMLite_Bak_")),
                "No active or stale slot backup may grow the FX parameter table.");
            Assert.IsTrue(ctrl.parameters.Any(p => p.name == "ASMLite_Ctrl"));
            foreach (var source in sources)
            {
                Assert.IsTrue(ctrl.parameters.Any(p => p.name == source.name), "Live sources must retain global FX bindings.");
                Assert.IsTrue(ctrl.parameters.Any(p => p.name == "ASMLite_Def_" + source.name));
            }

            for (int slot = 1; slot <= slotCount; slot++)
            {
                var save = LoadSlotDriver(ctrl, slot, "SaveSlot" + slot);
                var load = LoadSlotDriver(ctrl, slot, "LoadSlot" + slot);
                var reset = LoadSlotDriver(ctrl, slot, "ResetSlot" + slot);
                Assert.IsTrue(save.localOnly && load.localOnly && reset.localOnly);
                foreach (var source in sources)
                {
                    string backup = $"ASMLite_Bak_S{slot}_{source.name}";
                    var stored = expression.parameters.Single(p => p.name == backup);
                    Assert.AreEqual(source.valueType, stored.valueType);
                    Assert.IsTrue(stored.saved);
                    Assert.IsFalse(stored.networkSynced);
                    Assert.IsTrue(HasCopy(save, source.name, backup));
                    Assert.IsTrue(HasCopy(load, backup, source.name));
                    Assert.AreEqual(source.defaultValue, reset.parameters.Single(p => p.name == backup).value);
                    Assert.AreEqual(source.defaultValue, reset.parameters.Single(p => p.name == source.name).value);
                }
                foreach (var driver in new[] { save, load, reset })
                {
                    Assert.AreEqual("ASMLite_Ctrl", driver.parameters.Last().name);
                    Assert.AreEqual(0f, driver.parameters.Last().value);
                }
            }
        }

        [Test, Category("Integration")]
        public void DuplicateAvatarParam_OnlyOneEntryInCtrlParameters()
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

            var genCtrl = LoadGeneratedController("DuplicateAvatarParam_OnlyOneEntryInCtrlParameters");
            int count = genCtrl.parameters.Count(p => p.name == "DupParam");
            Assert.AreEqual(1, count,
                "Dedup guard: duplicate avatar param name must appear only once in generated FX controller parameters.");
        }

        [Test, Category("Integration")]
        public void PreExistingDuplicateASMLiteParams_DrainedBeforeBuild()
        {
            _ctx.Comp.slotCount = 1;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            // Pre-inject duplicates into the generated FX controller to simulate
            // a stale asset from a prior broken build.
            var genCtrlPre = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(genCtrlPre, "PreExistingDuplicateASMLiteParams_DrainedBeforeBuild: generated FX controller must exist before pre-injection.");
            genCtrlPre.AddParameter("ASMLite_Bak_S1_MyParam", AnimatorControllerParameterType.Int);
            genCtrlPre.AddParameter("ASMLite_Bak_S1_MyParam", AnimatorControllerParameterType.Int);

            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("PreExistingDuplicateASMLiteParams_DrainedBeforeBuild");
            var asmParams = genCtrl.parameters
                .Where(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxParameter)
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

        [Test, Category("Integration")]
        public void DefParam_HasCorrectDefaultValues()
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

            var genCtrl = LoadGeneratedController("DefParam_HasCorrectDefaultValues");
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

        [Test, Category("Integration")]
        public void FXController_UsesOnlyOneSharedCtrlParam_NoLegacyControlParams()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("FXController_UsesOnlyOneSharedCtrlParam_NoLegacyControlParams");
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

        [Test, Category("Integration")]
        public void SlotLayers_DoNotUseAnyStateSafeBoolBranches()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Bool);
            ASMLiteBuilder.Build(_ctx.Comp);

            var genCtrl = LoadGeneratedController("SlotLayers_DoNotUseAnyStateSafeBoolBranches");
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
        public void ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries()
        {
            _ctx.Comp.slotCount = 2;
            AddParam(_ctx, "KeepA", VRCExpressionParameters.ValueType.Int);
            AddParam(_ctx, "DropB", VRCExpressionParameters.ValueType.Float);
            AddParam(_ctx, "DropC", VRCExpressionParameters.ValueType.Bool);

            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "DropB", " DropC ", "DropB", "GhostMissing" };

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Build() should return only non-excluded discovered params.");

            var genCtrl = LoadGeneratedController("ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries");
            var allNames = genCtrl.parameters.Select(p => p.name).ToHashSet();

            Assert.IsTrue(allNames.Contains("KeepA"), "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: non-excluded live param should remain declared in FX params.");
            Assert.IsTrue(allNames.Contains("ASMLite_Def_KeepA"), "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: non-excluded default param should remain declared in FX params.");
            Assert.IsFalse(allNames.Contains("ASMLite_Bak_S1_KeepA"), "Backup storage must remain expression-only.");
            Assert.IsFalse(allNames.Contains("ASMLite_Bak_S2_KeepA"), "Backup storage must remain expression-only.");

            Assert.IsFalse(allNames.Contains("DropB"), "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: excluded live param must not be declared in FX params.");
            Assert.IsFalse(allNames.Contains("DropC"), "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: excluded live param must not be declared in FX params.");

            foreach (int slot in new[] { 1, 2 })
            {
                Assert.IsFalse(allNames.Contains($"ASMLite_Bak_S{slot}_DropB"), $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: excluded backup key ASMLite_Bak_S{slot}_DropB must be omitted.");
                Assert.IsFalse(allNames.Contains($"ASMLite_Bak_S{slot}_DropC"), $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: excluded backup key ASMLite_Bak_S{slot}_DropC must be omitted.");
            }
            Assert.IsFalse(allNames.Contains("ASMLite_Def_DropB"), "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: excluded default key ASMLite_Def_DropB must be omitted.");
            Assert.IsFalse(allNames.Contains("ASMLite_Def_DropC"), "ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: excluded default key ASMLite_Def_DropC must be omitted.");

            for (int slot = 1; slot <= 2; slot++)
            {
                var saveDriver = LoadSlotDriver(genCtrl, slot, $"SaveSlot{slot}");
                var loadDriver = LoadSlotDriver(genCtrl, slot, $"LoadSlot{slot}");
                var resetDriver = LoadSlotDriver(genCtrl, slot, $"ResetSlot{slot}");

                Assert.IsTrue(HasCopy(saveDriver, "KeepA", $"ASMLite_Bak_S{slot}_KeepA"),
                    $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Save driver for slot {slot} must keep non-excluded copy wiring.");
                Assert.IsTrue(HasCopy(loadDriver, $"ASMLite_Bak_S{slot}_KeepA", "KeepA"),
                    $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Load driver for slot {slot} must keep non-excluded copy wiring.");
                Assert.IsTrue(resetDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Set && p.name == $"ASMLite_Bak_S{slot}_KeepA" && p.value == 0f),
                    $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Reset driver for slot {slot} must keep non-excluded clear wiring.");

                Assert.IsFalse(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && (p.source == "DropB" || p.source == "DropC" || p.name.Contains("DropB") || p.name.Contains("DropC"))),
                    $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Save driver for slot {slot} must not contain excluded-source copy entries.");
                Assert.IsFalse(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && (p.source.Contains("DropB") || p.source.Contains("DropC") || p.name == "DropB" || p.name == "DropC")),
                    $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Load driver for slot {slot} must not contain excluded-source copy entries.");
                Assert.IsFalse(resetDriver.parameters.Any(p => p.name.Contains("DropB") || p.name.Contains("DropC")),
                    $"ExclusionsEnabled_OmitsExcludedFXParamsAndDriverCopyEntries: Reset driver for slot {slot} must not write excluded destinations.");
            }
        }
    }
}
