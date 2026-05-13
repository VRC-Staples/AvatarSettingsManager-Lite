using System;
using System.Collections;
using System.Globalization;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace ASMLite.Tests.PlayMode
{
    [TestFixture]
    [Category("PlayMode")]
    [Category("Manual")]
    public sealed class ASMLiteAv3SaveLoadManualTests : ASMLiteAv3SaveLoadRuntimeTestBase
    {
        [UnityTest]
        public IEnumerator ExternalUatAvatar_RestoresSavedSubsetAndPreservesUnsavedMetadata()
        {
            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            var selection = ReadRealUatSelection();
            if (!selection.IsConfigured)
            {
                Debug.Log(selection.Diagnostic);
                yield break;
            }

            var setup = BuildAndWireRealUatAvatarFixture(selection);
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return new EnterPlayMode();

            GameObject avatar = GameObject.Find(RealUatAvatarName);
            Assert.IsNotNull(avatar,
                $"External UAT: operator-selected real UAT avatar clone should survive EnterPlayMode. asset='{selection.AssetPath}' avatarSelector='{selection.AvatarName}'.");

            var harness = new ASMLiteAv3SaveLoadHarness(setup.SavedDescriptors, setup.UnsavedDescriptors);
            yield return harness.RunCoreInvariant(avatar, 0xA5A50013u);
        }

        [UnityTest]
        public IEnumerator FuzzReplay_SaveLoadSlot1_ExercisesReplayableScaleCoverage([ValueSource(nameof(FuzzCases))] ASMLiteAv3SaveLoadFuzzCase fuzzCase)
        {
            if (!fuzzCase.IsEnabled)
            {
                Debug.Log(fuzzCase.Diagnostic);
                yield break;
            }

            var runtimeResolution = ASMLiteAv3RuntimeBridge.ResolveRuntimeType();
            if (!runtimeResolution.IsAvailable)
                Assert.Inconclusive(runtimeResolution.Diagnostic);

            BuildAndWireAvatarFixture();
            ASMLiteAv3RuntimeBridge.EnsureEmulatorControlObject();

            yield return new EnterPlayMode();

            GameObject avatar = GameObject.Find(TestAvatarName);
            Assert.IsNotNull(avatar,
                $"Fuzz replay: test avatar should survive EnterPlayMode for replayable AV3 fuzz/scale coverage. {fuzzCase}");

            var harness = new ASMLiteAv3SaveLoadHarness(SavedParameterDescriptors, UnsavedParameterDescriptors);
            Debug.Log($"Fuzz replay: starting replayable AV3 save/load fuzz/scale coverage. {fuzzCase}. Re-run with {FuzzSeedEnvVar}={FormatSeed(fuzzCase.Seed)} {FuzzIterationsEnvVar}={fuzzCase.Iterations.ToString(CultureInfo.InvariantCulture)} or {FuzzSeedArg} {FormatSeed(fuzzCase.Seed)} {FuzzIterationsArg} {fuzzCase.Iterations.ToString(CultureInfo.InvariantCulture)}.");
            for (int iterationIndex = 0; iterationIndex < fuzzCase.Iterations; iterationIndex++)
            {
                uint iterationSeed = DeriveFuzzIterationSeed(fuzzCase.Seed, iterationIndex);
                var context = new ASMLiteAv3SaveLoadRunContext(fuzzCase.Seed, iterationSeed, iterationIndex, fuzzCase.Iterations);
                Debug.Log($"Fuzz replay: AV3 save/load fuzz iteration {context.ToDisplayString()}. Re-run with {FuzzSeedEnvVar}={FormatSeed(fuzzCase.Seed)} {FuzzIterationsEnvVar}={fuzzCase.Iterations.ToString(CultureInfo.InvariantCulture)}.");
                yield return harness.RunCoreInvariant(avatar, context);
            }
        }
    }
}
