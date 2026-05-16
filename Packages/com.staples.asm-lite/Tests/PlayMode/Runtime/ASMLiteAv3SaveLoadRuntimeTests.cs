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
    }
}
