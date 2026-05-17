using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Tests.PlayMode
{
    [TestFixture]
    [Category("PlayMode")]
    public sealed class ASMLiteAv3SaveLoadRuntimeTests : ASMLiteAv3SaveLoadRuntimeTestBase
    {
        [UnityTest]
        public IEnumerator Av3Runtime_AfterEnteringPlayMode_ExposesSavedUnsavedAndControlParameters()
        {
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            BuildAndWireAvatarFixture();
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return EnterPlayModeIfNeeded();

            GameObject avatar = GameObject.Find(TestAvatarName);
            Assert.IsNotNull(avatar, "Runtime: test avatar should survive EnterPlayMode for AV3 runtime scanning.");

            string[] requiredNames = SavedParameterNames
                .Concat(UnsavedParameterNames)
                .Concat(new[] { ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName })
                .ToArray();

            ASMLiteAv3RuntimeBridge.ParameterSnapshot snapshot = default;
            string lastDiagnostic = string.Empty;
            double deadline = EditorApplication.timeSinceStartup + 10.0d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (ASMLiteAv3RuntimeBridge.TryCaptureVisibleParameters(avatar, out snapshot, out lastDiagnostic)
                    && requiredNames.All(snapshot.Contains))
                {
                    break;
                }

                yield return null;
            }

            Assert.IsTrue(requiredNames.All(snapshot.Contains),
                "Runtime: AV3 runtime did not expose every required saved/unsaved/control parameter before timeout. "
                + $"Missing=[{string.Join(", ", requiredNames.Where(name => !snapshot.Contains(name)))}]. "
                + $"LastDiagnostic={lastDiagnostic}. Visible=[{string.Join(", ", snapshot.AllNames.OrderBy(name => name, StringComparer.Ordinal))}]");
        }

        [UnityTest]
        public IEnumerator Av3SaveLoadSlot1_RestoresVrcFuryBrokeredBoolToggle()
        {
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            const string brokeredRezzParam = "ASM_VF_Clothing_Rezz__ASMLite_AV3_SaveLoad_Runtime_Avatar_Clothing_Rezz";
            BuildAndWireAvatarFixture(new VRCExpressionParameters.Parameter
            {
                name = brokeredRezzParam,
                valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f,
                saved = true,
                networkSynced = true,
            });
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return EnterPlayModeIfNeeded();

            GameObject avatar = GameObject.Find(TestAvatarName);
            Assert.IsNotNull(avatar, "Runtime: test avatar should survive EnterPlayMode for brokered VRCFury toggle save/load coverage.");

            var savedDescriptors = SavedParameterDescriptors
                .Concat(new[] { ASMLiteAv3SaveLoadHarness.Descriptor(brokeredRezzParam, VRCExpressionParameters.ValueType.Bool) })
                .ToArray();
            var harness = new ASMLiteAv3SaveLoadHarness(savedDescriptors, UnsavedParameterDescriptors);
            yield return harness.RunCoreInvariant(avatar, 0xA5A5F017u);
        }

        [UnityTest]
        public IEnumerator Av3SaveLoadSlot2Untouched_ResetsVrcFurySurfaceToggleAndFullControllerParam()
        {
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            const string brokeredRezzParam = "ASM_VF_Clothing_Rezz__ASMLite_AV3_SaveLoad_Runtime_Avatar_Clothing_Rezz";
            const string fullControllerLollipopParam = "SPS/Lollipop";
            BuildAndWireExactAvatarFixture(
                2,
                new VRCExpressionParameters.Parameter
                {
                    name = brokeredRezzParam,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = fullControllerLollipopParam,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return EnterPlayModeIfNeeded();

            GameObject avatar = GameObject.Find(TestAvatarName);
            Assert.IsNotNull(avatar, "Runtime: test avatar should survive EnterPlayMode for two-slot VRCFury save/load coverage.");

            object runtime = null;
            yield return WaitForRuntimeWithBoolParameters(avatar, new[] { brokeredRezzParam, fullControllerLollipopParam }, resolved => runtime = resolved);

            WriteBool(runtime, brokeredRezzParam, true, "set-rezz-before-save");
            WriteBool(runtime, fullControllerLollipopParam, true, "set-lollipop-before-save");
            TriggerControl(runtime, 1, "save-slot-1");
            yield return WaitForControlIdle(runtime, "save-slot-1-settle");
            AssertBool(runtime, brokeredRezzParam, true, "rezz-after-save");
            AssertBool(runtime, fullControllerLollipopParam, true, "lollipop-after-save");

            TriggerControl(runtime, 5, "load-untouched-slot-2");
            yield return WaitForControlIdleAndBoolValues(
                runtime,
                "load-untouched-slot-2-settle",
                new[] { brokeredRezzParam, fullControllerLollipopParam },
                new[] { false, false });
        }

        [UnityTest]
        public IEnumerator Av3SaveLoadSlot1_RestoresSavedAndPreservesUnsavedParameters_ForSeed([ValueSource(nameof(SaveLoadSeedCases))] ASMLiteAv3SaveLoadSeedCase seedCase)
        {
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            BuildAndWireAvatarFixture();
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return EnterPlayModeIfNeeded();

            GameObject avatar = GameObject.Find(TestAvatarName);
            Assert.IsNotNull(avatar, $"Runtime invariant: test avatar should survive EnterPlayMode for AV3 save/load invariant. seed={seedCase}");

            var harness = new ASMLiteAv3SaveLoadHarness(SavedParameterDescriptors, UnsavedParameterDescriptors);
            yield return harness.RunCoreInvariant(avatar, seedCase.Seed);
        }

        private static IEnumerator WaitForRuntimeWithBoolParameters(GameObject avatar, string[] parameterNames, Action<object> setRuntime)
        {
            string lastDiagnostic = string.Empty;
            object runtime = null;
            double deadline = EditorApplication.timeSinceStartup + 10.0d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (ASMLiteAv3RuntimeBridge.TryFindRuntime(avatar, out runtime, out lastDiagnostic)
                    && parameterNames.All(name => ASMLiteAv3RuntimeBridge.HasParameter(runtime, name, ASMLiteAv3ParameterType.Bool))
                    && ASMLiteAv3RuntimeBridge.HasParameter(runtime, ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName, ASMLiteAv3ParameterType.Int))
                {
                    setRuntime(runtime);
                    yield break;
                }

                yield return null;
            }

            ASMLiteAv3RuntimeBridge.ParameterSnapshot visibleSnapshot = default;
            ASMLiteAv3RuntimeBridge.TryCaptureVisibleParameters(avatar, out visibleSnapshot, out _);
            Assert.Fail(
                "Runtime: AV3 runtime did not expose required VRCFury repro parameters before timeout. "
                + $"Missing=[{string.Join(", ", parameterNames.Where(name => !visibleSnapshot.Contains(name)).Concat(new[] { ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName }.Where(name => !visibleSnapshot.Contains(name))))}]. "
                + $"LastDiagnostic={lastDiagnostic}. Visible=[{string.Join(", ", visibleSnapshot.AllNames.OrderBy(name => name, StringComparer.Ordinal))}]");
        }

        private static void WriteBool(object runtime, string name, bool value, string phase)
        {
            Assert.IsTrue(
                ASMLiteAv3RuntimeBridge.TryWriteParameter(runtime, name, ASMLiteAv3ParameterValue.Bool(value), out var diagnostic),
                $"Runtime: phase={phase} param={name} expected={value} actual=<write-failed> diagnostic={diagnostic}");
        }

        private static void AssertBool(object runtime, string name, bool expected, string phase)
        {
            Assert.IsTrue(
                ASMLiteAv3RuntimeBridge.TryReadParameter(runtime, name, ASMLiteAv3ParameterType.Bool, out var value, out var diagnostic),
                $"Runtime: phase={phase} param={name} expected={expected} actual=<read-failed> diagnostic={diagnostic}");
            Assert.AreEqual(expected, value.BoolValue,
                $"Runtime: phase={phase} param={name} expected={expected} actual={value.BoolValue}");
        }

        private static void TriggerControl(object runtime, int value, string phase)
        {
            Assert.IsTrue(
                ASMLiteAv3RuntimeBridge.TryWriteControl(runtime, value, out var diagnostic),
                $"Runtime: phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} expected={value} actual=<write-failed> diagnostic={diagnostic}");
        }

        private static IEnumerator WaitForControlIdle(object runtime, string phase)
        {
            string lastDiagnostic = string.Empty;
            int lastActual = int.MinValue;
            double deadline = EditorApplication.timeSinceStartup + 10.0d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (ASMLiteAv3RuntimeBridge.TryReadControl(runtime, out lastActual, out lastDiagnostic) && lastActual == 0)
                    yield break;

                yield return null;
            }

            Assert.Fail($"Runtime: phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} expected=0 actual={(lastActual == int.MinValue ? "<read-failed>" : lastActual.ToString())} diagnostic={lastDiagnostic}");
        }

        private static IEnumerator WaitForControlIdleAndBoolValues(object runtime, string phase, string[] names, bool[] expectedValues)
        {
            string lastDiagnostic = string.Empty;
            int lastActual = int.MinValue;
            double deadline = EditorApplication.timeSinceStartup + 10.0d;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                bool controlIdle = ASMLiteAv3RuntimeBridge.TryReadControl(runtime, out lastActual, out lastDiagnostic) && lastActual == 0;
                bool allMatched = controlIdle;
                for (int i = 0; allMatched && i < names.Length && i < expectedValues.Length; i++)
                {
                    allMatched = ASMLiteAv3RuntimeBridge.TryReadParameter(runtime, names[i], ASMLiteAv3ParameterType.Bool, out var value, out _)
                        && value.BoolValue == expectedValues[i];
                }

                if (allMatched)
                    yield break;

                yield return null;
            }

            for (int i = 0; i < names.Length && i < expectedValues.Length; i++)
                AssertBool(runtime, names[i], expectedValues[i], phase);
            Assert.Fail($"Runtime: phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} expected=0 actual={(lastActual == int.MinValue ? "<read-failed>" : lastActual.ToString())} diagnostic={lastDiagnostic}");
        }
    }
}
