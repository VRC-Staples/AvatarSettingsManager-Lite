using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public sealed class ASMLiteSharedResetBuildTests
    {
        private AnimatorController _controller;
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            if (_controller != null)
                _owned.AddRange(_controller.layers.SelectMany(layer => layer.stateMachine.states)
                    .SelectMany(child => child.state.behaviours));
            foreach (var value in _owned.Distinct().Reverse().ToArray())
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _owned.Clear();
            _controller = null;
        }

        [TestCase(2)]
        [TestCase(8)]
        public void SharedReset_PreservesTopologyPayloadsAndLegacyBackups(int slots)
        {
            CreateController(slots);
            var layers = _controller.layers;
            var states = layers.SelectMany(layer => layer.stateMachine.states).Select(child => child.state).ToArray();
            var transitions = states.SelectMany(state => state.transitions).ToArray();
            var copies = states.Where(state => state.name.StartsWith("SaveSlot", StringComparison.Ordinal)
                || state.name.StartsWith("LoadSlot", StringComparison.Ordinal))
                .Select(state => EditorJsonUtility.ToJson(state.behaviours[0])).ToArray();
            var resets = ResetStates();
            var expectedLive = Driver(resets[0]).parameters.Where(parameter =>
                !parameter.name.StartsWith("ASMLite_Bak_S1_", StringComparison.Ordinal)
                && parameter.name != ASMLiteBuilder.CtrlParam).Select(JsonUtility.ToJson).ToArray();
            var expectedPhysical = resets.Select((state, index) => Driver(state).parameters.Where(parameter =>
                parameter.name.StartsWith("ASMLite_Bak_S" + (index + 1) + "_", StringComparison.Ordinal)
                || parameter.name == ASMLiteBuilder.CtrlParam).Select(JsonUtility.ToJson).ToArray()).ToArray();

            Assert.IsTrue(ASMLiteSharedResetBuildCallback.TryShareLiveResetDrivers(_controller));
            CollectionAssert.AreEqual(layers.Select(layer => layer.stateMachine), _controller.layers.Select(layer => layer.stateMachine));
            CollectionAssert.AreEqual(states, _controller.layers.SelectMany(layer => layer.stateMachine.states).Select(child => child.state));
            CollectionAssert.AreEqual(transitions, states.SelectMany(state => state.transitions));
            CollectionAssert.AreEqual(copies, states.Where(state => state.name.StartsWith("SaveSlot", StringComparison.Ordinal)
                || state.name.StartsWith("LoadSlot", StringComparison.Ordinal)).Select(state => EditorJsonUtility.ToJson(state.behaviours[0])));
            var shared = (VRCAvatarParameterDriver)resets[0].behaviours[1];
            Assert.IsTrue(shared.localOnly);
            CollectionAssert.AreEqual(expectedLive, shared.parameters.Select(JsonUtility.ToJson));
            Assert.IsFalse(shared.parameters.Any(parameter => parameter.name == ASMLiteBuilder.CtrlParam));
            for (int i = 0; i < slots; i++)
            {
                Assert.AreEqual(2, resets[i].behaviours.Length);
                Assert.AreSame(shared, resets[i].behaviours[1]);
                CollectionAssert.AreEqual(expectedPhysical[i], Driver(resets[i]).parameters.Select(JsonUtility.ToJson));
            }
        }

        [TestCase("01", "")]
        [TestCase("+1", "")]
        [TestCase("01", "[VF42] ")]
        [TestCase("+1", "[VF42] ")]
        public void MappedLegacySlotSpelling_RemainsPhysicalAndKeepsExactName(string slotText, string prefix)
        {
            string legacyName = "ASMLite_Bak_S" + slotText + "_OldFloat";
            var plan = ASMLiteMigrationContinuityService.BuildBackupNamePlan(
                2,
                new List<VRCExpressionParameters.Parameter>
                {
                    new VRCExpressionParameters.Parameter { name = "Float", valueType = VRCExpressionParameters.ValueType.Float },
                },
                new[] { legacyName },
                new[] { new ASMLiteToggleNameBroker.GlobalParamMapping("OldFloat", "Float") },
                null);
            var binding = plan.LegacyAliasBindings.Single();
            Assert.AreEqual(1, binding.Slot);
            Assert.AreEqual(legacyName, binding.LegacyBackupName);
            CollectionAssert.Contains(plan.Names, legacyName);

            CreateController(2);
            foreach (var driver in _controller.layers.SelectMany(layer => layer.stateMachine.states)
                .SelectMany(child => child.state.behaviours).OfType<VRCAvatarParameterDriver>())
                foreach (var parameter in driver.parameters)
                {
                    parameter.name = prefix + parameter.name;
                    if (!string.IsNullOrEmpty(parameter.source)) parameter.source = prefix + parameter.source;
                }
            var resets = ResetStates();
            var alias = Set(prefix + binding.LegacyBackupName, 0.375f);
            Driver(resets[0]).parameters.Insert(Driver(resets[0]).parameters.Count - 1, alias);

            Assert.IsTrue(ASMLiteSharedResetBuildCallback.TryShareLiveResetDrivers(_controller));
            Assert.AreSame(alias, Driver(resets[0]).parameters.Single(parameter => parameter.name == prefix + legacyName));
            Assert.AreEqual(0.375f, alias.value);
            var shared = (VRCAvatarParameterDriver)resets[0].behaviours[1];
            Assert.IsFalse(shared.parameters.Any(parameter => parameter.name == alias.name));
            Assert.AreSame(shared, resets[1].behaviours[1]);
            Assert.AreEqual(prefix + ASMLiteBuilder.CtrlParam, Driver(resets[0]).parameters.Last().name);
        }

        [Test]
        public void DifferentLiveDefaults_RejectsBeforeChangingAnySlot()
        {
            CreateController(8);
            Driver(ResetStates()[7]).parameters.Single(parameter => parameter.name == "Float").value = 0.75f;
            var before = ResetStates().Select(state => EditorJsonUtility.ToJson(Driver(state))).ToArray();
            Assert.IsFalse(ASMLiteSharedResetBuildCallback.TryShareLiveResetDrivers(_controller));
            CollectionAssert.AreEqual(before, ResetStates().Select(state => EditorJsonUtility.ToJson(Driver(state))));
            Assert.IsTrue(ResetStates().All(state => state.behaviours.Length == 1));
        }

        [Test]
        public void NonSetReset_RejectsBeforeChangingAnySlot()
        {
            CreateController(2);
            Driver(ResetStates()[1]).parameters[0].type = VRC_AvatarParameterDriver.ChangeType.Add;
            Assert.IsFalse(ASMLiteSharedResetBuildCallback.TryShareLiveResetDrivers(_controller));
            Assert.IsTrue(ResetStates().All(state => state.behaviours.Length == 1));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void FewerThanTwoSlots_DoesNotAddSharedDriver(int slots)
        {
            CreateController(slots);
            Assert.IsTrue(ASMLiteSharedResetBuildCallback.TryShareLiveResetDrivers(_controller));
            Assert.IsTrue(ResetStates().All(state => state.behaviours.Length == 1));
        }

        private void CreateController(int slots)
        {
            _controller = new AnimatorController { name = "SharedResetTest", hideFlags = HideFlags.HideAndDontSave };
            _owned.Add(_controller);
            _controller.AddParameter(ASMLiteBuilder.CtrlParam, AnimatorControllerParameterType.Int);
            for (int slot = 1; slot <= slots; slot++)
            {
                var machine = new AnimatorStateMachine { name = "ASMLite_Slot" + slot, hideFlags = HideFlags.HideAndDontSave };
                _owned.Add(machine);
                _controller.AddLayer(new AnimatorControllerLayer { name = machine.name, defaultWeight = 1f, stateMachine = machine });
                var idle = machine.AddState("Idle");
                machine.defaultState = idle;
                _owned.Add(idle);
                foreach (string verb in new[] { "SaveSlot", "LoadSlot", "ResetSlot" })
                {
                    var state = machine.AddState(verb + slot);
                    state.writeDefaultValues = false;
                    _owned.Add(state);
                    _owned.Add(idle.AddTransition(state));
                    _owned.Add(state.AddTransition(idle));
                    var driver = state.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
                    driver.localOnly = true;
                    _owned.Add(driver);
                    driver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
                    string[] names = { "Bool", "Int", "Float", "Custom_ASMLite_Bak_S1_Float" };
                    float[] defaults = { 1f, 2f, 0.375f, 0.25f };
                    for (int i = 0; i < names.Length; i++)
                    {
                        string backup = "ASMLite_Bak_S" + slot + "_" + names[i];
                        if (verb == "ResetSlot")
                        {
                            driver.parameters.Add(Set(backup, defaults[i]));
                            driver.parameters.Add(Set(names[i], defaults[i]));
                        }
                        else driver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                        {
                            type = VRC_AvatarParameterDriver.ChangeType.Copy,
                            source = verb == "SaveSlot" ? names[i] : backup,
                            name = verb == "SaveSlot" ? backup : names[i],
                        });
                    }
                    if (verb == "ResetSlot" && slot == 1)
                        driver.parameters.Add(Set("ASMLite_Bak_S1_LegacyAlias", 0.375f));
                    driver.parameters.Add(Set(ASMLiteBuilder.CtrlParam, 0f));
                }
            }
        }

        private AnimatorState[] ResetStates() => _controller.layers.SelectMany(layer => layer.stateMachine.states)
            .Select(child => child.state).Where(state => state.name.StartsWith("ResetSlot", StringComparison.Ordinal)).ToArray();
        private static VRCAvatarParameterDriver Driver(AnimatorState state) => (VRCAvatarParameterDriver)state.behaviours[0];
        private static VRC_AvatarParameterDriver.Parameter Set(string name, float value)
            => new VRC_AvatarParameterDriver.Parameter { type = VRC_AvatarParameterDriver.ChangeType.Set, name = name, value = value };
    }
}
