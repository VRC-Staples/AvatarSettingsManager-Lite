using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeOverlayHostTests
    {
        [Test]
        public void CommandLine_ParsesRequiredSessionAndCatalogPaths()
        {
            string sessionRoot = Path.Combine(Path.GetTempPath(), "asmlite-smoke-host-session", "..", "asmlite-smoke-host-session-normalized");
            string catalogPath = Path.Combine(Path.GetTempPath(), "asmlite-smoke-host-catalog", "..", "asmlite-smoke-host-catalog", "catalog.json");

            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeSessionRoot", sessionRoot,
                "-asmliteSmokeCatalogPath", catalogPath,
            };

            ASMLiteSmokeOverlayHostConfiguration configuration = ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args);

            Assert.AreEqual(Path.GetFullPath(sessionRoot), configuration.SessionRootPath);
            Assert.AreEqual(Path.GetFullPath(catalogPath), configuration.CatalogPath);
        }

        [Test]
        public void CommandLine_DefaultsToClickMeSceneAndOct25DressAvatar()
        {
            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeSessionRoot", Path.Combine(Path.GetTempPath(), "asmlite-smoke-defaults-session"),
                "-asmliteSmokeCatalogPath", Path.Combine(Path.GetTempPath(), "asmlite-smoke-defaults-session", "catalog.json"),
            };

            ASMLiteSmokeOverlayHostConfiguration configuration = ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args);

            Assert.AreEqual("Assets/Click ME.unity", configuration.ScenePath);
            Assert.AreEqual("Oct25_Dress", configuration.AvatarName);
            Assert.AreEqual(120, configuration.StartupTimeoutSeconds);
            Assert.AreEqual(5, configuration.HeartbeatSeconds);
            Assert.That(configuration.ExitOnReady, Is.False);
        }

        [Test]
        public void CommandLine_RejectsMissingSessionRoot()
        {
            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeCatalogPath", Path.Combine(Path.GetTempPath(), "asmlite-smoke-missing-session", "catalog.json"),
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args));

            StringAssert.Contains("-asmliteSmokeSessionRoot", exception.Message);
        }

        [Test]
        public void CommandLine_RejectsInvalidTimeouts()
        {
            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeSessionRoot", Path.Combine(Path.GetTempPath(), "asmlite-smoke-invalid-timeout"),
                "-asmliteSmokeCatalogPath", Path.Combine(Path.GetTempPath(), "asmlite-smoke-invalid-timeout", "catalog.json"),
                "-asmliteSmokeStartupTimeoutSeconds", "0",
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args));

            StringAssert.Contains("-asmliteSmokeStartupTimeoutSeconds", exception.Message);
        }

        [Test]
        public void UnityRuntime_NormalizesPlayModeCloneAvatarNames()
        {
            Assert.AreEqual("Oct25_Dress", ASMLiteSmokeOverlayHostUnityRuntime.NormalizeUnityRuntimeName("Oct25_Dress (Clone)"));
            Assert.AreEqual("Oct25_Dress", ASMLiteSmokeOverlayHostUnityRuntime.NormalizeUnityRuntimeName(" Oct25_Dress (Clone) (Clone) "));
        }

        [Test]
        public void UnityRuntime_AssertsPackageResourcesAndCanonicalCatalogLoad()
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-package-resource-present",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out string packageDetail,
                    out string packageStackTrace),
                Is.True);
            StringAssert.Contains("ASM-Lite.prefab", packageDetail);
            Assert.That(packageStackTrace, Is.Empty);

            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-catalog-loads",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out string catalogDetail,
                    out string catalogStackTrace),
                Is.True);
            StringAssert.Contains("canonical smoke catalog", catalogDetail);
            Assert.That(catalogStackTrace, Is.Empty);
        }

        [Test]
        public void UnityRuntime_AssertsExpectedPrimaryActionFromStepArgs()
        {
            var avatarObject = new GameObject("Phase06_AddPrefabAvatar");
            avatarObject.AddComponent<VRCAvatarDescriptor>();

            try
            {
                Selection.activeObject = avatarObject;
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));

                bool success = ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-primary-action",
                    new ASMLiteSmokeStepArgs { expectedPrimaryAction = "Add Prefab" },
                    string.Empty,
                    avatarObject.name,
                    out string detail,
                    out string stackTrace);

                Assert.That(success, Is.True, detail + "\n" + stackTrace);
                Assert.That(detail, Is.EqualTo("Primary action is Add Prefab."));
            }
            finally
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    string.Empty,
                    avatarObject.name,
                    out _,
                    out _);
                UnityEngine.Object.DestroyImmediate(avatarObject);
                Selection.activeObject = null;
            }
        }

        [Test]
        public void UnityRuntime_RejectsMissingAndNonScenePathsWithPhase04Diagnostics()
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "open-scene",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Missing.unity",
                    "Oct25_Dress",
                    out string missingDetail,
                    out string missingStackTrace),
                Is.False);
            StringAssert.Contains("SETUP_SCENE_MISSING", missingDetail);
            StringAssert.Contains("scene could not be found", missingDetail);
            Assert.That(missingStackTrace, Is.Empty);

            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "open-scene",
                    new ASMLiteSmokeStepArgs(),
                    "Packages/com.staples.asm-lite/package.json",
                    "Oct25_Dress",
                    out string invalidDetail,
                    out string invalidStackTrace),
                Is.False);
            StringAssert.Contains("SETUP_SCENE_PATH_INVALID", invalidDetail);
            StringAssert.Contains("not a Unity scene", invalidDetail);
            Assert.That(invalidStackTrace, Is.Empty);
        }

        [Test]
        public void UnityRuntime_ClosesOpensAndFocusesAutomationWindow()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out _,
                    out _);

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "open-window",
                        new ASMLiteSmokeStepArgs(),
                        "Assets/Click ME.unity",
                        "Oct25_Dress",
                        out string openDetail,
                        out string openStackTrace),
                    Is.True);
                StringAssert.Contains("opened", openDetail);
                Assert.That(openStackTrace, Is.Empty);

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "assert-window-focused",
                        new ASMLiteSmokeStepArgs(),
                        "Assets/Click ME.unity",
                        "Oct25_Dress",
                        out string focusDetail,
                        out string focusStackTrace),
                    Is.True);
                StringAssert.Contains("automation", focusDetail);
                Assert.That(focusStackTrace, Is.Empty);
            }
            finally
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out _,
                    out _);
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void UnityRuntime_FindsSceneAvatarWithRuntimeComponent_WhenDuplicateNameExists()
        {
            GameObject avatarWithoutComponentObject = null;
            GameObject avatarWithComponentObject = null;
            try
            {
                avatarWithoutComponentObject = new GameObject("Oct25_Dress");
                avatarWithoutComponentObject.AddComponent<VRCAvatarDescriptor>();

                avatarWithComponentObject = new GameObject("Oct25_Dress (Clone)");
                VRCAvatarDescriptor avatarWithComponent = avatarWithComponentObject.AddComponent<VRCAvatarDescriptor>();
                var componentChild = new GameObject("ASM-Lite Component");
                componentChild.transform.SetParent(avatarWithComponentObject.transform);
                componentChild.AddComponent<ASMLite.ASMLiteComponent>();

                VRCAvatarDescriptor resolved = ASMLiteSmokeOverlayHostUnityRuntime.Instance.FindAvatarByName("Oct25_Dress");

                Assert.That(resolved, Is.SameAs(avatarWithComponent));
            }
            finally
            {
                if (avatarWithoutComponentObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarWithoutComponentObject);
                if (avatarWithComponentObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarWithComponentObject);
            }
        }

        [Test]
        public void UnityRuntime_AcceptsEditorOnlyComponentStrippedDuringPlaymode()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("Oct25_Dress");
                VRCAvatarDescriptor avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();

                bool valid = ASMLiteSmokeOverlayHostUnityRuntime.ValidateRuntimeComponentState(
                    "Oct25_Dress",
                    avatar,
                    isPlaying: true,
                    out string detail);

                Assert.That(valid, Is.True);
                StringAssert.Contains("stripped for playmode", detail);
            }
            finally
            {
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_RejectsMissingComponentOutsidePlaymode()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("Oct25_Dress");
                VRCAvatarDescriptor avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();

                bool valid = ASMLiteSmokeOverlayHostUnityRuntime.ValidateRuntimeComponentState(
                    "Oct25_Dress",
                    avatar,
                    isPlaying: false,
                    out string detail);

                Assert.That(valid, Is.False);
                StringAssert.Contains("component was not found", detail);
            }
            finally
            {
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_UsesSelectedSceneAvatarBeforeNameLookup()
        {
            GameObject canonical = null;
            GameObject alternate = null;
            try
            {
                canonical = new GameObject("FixtureAvatar");
                canonical.AddComponent<VRCAvatarDescriptor>();
                alternate = new GameObject("AlternateAvatar");
                VRCAvatarDescriptor alternateDescriptor = alternate.AddComponent<VRCAvatarDescriptor>();
                Selection.activeObject = alternate;

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.True, detail);
                Assert.That(avatar, Is.SameAs(alternateDescriptor));
                StringAssert.Contains("selected avatar 'AlternateAvatar'", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (canonical != null)
                    UnityEngine.Object.DestroyImmediate(canonical);
                if (alternate != null)
                    UnityEngine.Object.DestroyImmediate(alternate);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_FindsSingleActiveAvatarByFixtureNameWhenNothingSelected()
        {
            GameObject avatarObject = null;
            try
            {
                Selection.activeObject = null;
                avatarObject = new GameObject("FixtureAvatar");
                VRCAvatarDescriptor descriptor = avatarObject.AddComponent<VRCAvatarDescriptor>();

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.True, detail);
                Assert.That(avatar, Is.SameAs(descriptor));
                StringAssert.Contains("found by fixture name", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_ReportsWrongSelectedObjectDiagnostic()
        {
            GameObject wrongObject = null;
            try
            {
                wrongObject = new GameObject("FixtureAvatar");
                Selection.activeObject = wrongObject;

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_SELECTED_OBJECT_NOT_AVATAR", detail);
                StringAssert.Contains("selected object is not a valid avatar", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (wrongObject != null)
                    UnityEngine.Object.DestroyImmediate(wrongObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_ReportsAmbiguousDuplicateNamesWithoutSelectedDisambiguator()
        {
            GameObject first = null;
            GameObject second = null;
            try
            {
                Selection.activeObject = null;
                first = new GameObject("FixtureAvatar");
                first.AddComponent<VRCAvatarDescriptor>();
                second = new GameObject("FixtureAvatar");
                second.AddComponent<VRCAvatarDescriptor>();

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_AMBIGUOUS", detail);
                StringAssert.Contains("Multiple avatar descriptors", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (first != null)
                    UnityEngine.Object.DestroyImmediate(first);
                if (second != null)
                    UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_AllowsSelectedInactiveAvatarButDoesNotFindUnselectedInactiveAvatar()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("FixtureAvatar");
                VRCAvatarDescriptor descriptor = avatarObject.AddComponent<VRCAvatarDescriptor>();
                avatarObject.SetActive(false);
                Selection.activeObject = null;

                bool unselectedResolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor unselectedAvatar,
                    out string unselectedDetail);

                Assert.That(unselectedResolved, Is.False);
                Assert.That(unselectedAvatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_NOT_FOUND", unselectedDetail);

                Selection.activeObject = avatarObject;
                bool selectedResolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor selectedAvatar,
                    out string selectedDetail);

                Assert.That(selectedResolved, Is.True, selectedDetail);
                Assert.That(selectedAvatar, Is.SameAs(descriptor));
            }
            finally
            {
                Selection.activeObject = null;
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_IgnoresSameNameNonAvatarObjectsDuringNameLookup()
        {
            GameObject nonAvatar = null;
            try
            {
                Selection.activeObject = null;
                nonAvatar = new GameObject("FixtureAvatar");

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_NOT_FOUND", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (nonAvatar != null)
                    UnityEngine.Object.DestroyImmediate(nonAvatar);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_ReportsPrefabAssetSelectionDiagnostic()
        {
            const string folderPath = "Assets/ASMLiteAvatarSelectionTests_Temp";
            const string prefabPath = folderPath + "/FixturePrefabAvatar.prefab";
            GameObject source = null;
            try
            {
                if (!AssetDatabase.IsValidFolder(folderPath))
                    AssetDatabase.CreateFolder("Assets", "ASMLiteAvatarSelectionTests_Temp");
                source = new GameObject("FixtureAvatar");
                source.AddComponent<VRCAvatarDescriptor>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                UnityEngine.Object.DestroyImmediate(source);
                source = null;
                Selection.activeObject = prefab;

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_PREFAB_ASSET", detail);
                StringAssert.Contains("prefab asset", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (source != null)
                    UnityEngine.Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(prefabPath);
                if (AssetDatabase.IsValidFolder(folderPath))
                    AssetDatabase.DeleteAsset(folderPath);
            }
        }

        [Test]
        public void Runner_RemainsRegisteredAfterReady_WhenExitOnReadyIsFalse()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                Assert.That(context.Runner.IsRunningForTesting, Is.True);
                Assert.That(context.Runtime.RegisterUpdateCount, Is.EqualTo(1));
                Assert.That(context.Runtime.UnregisterUpdateCount, Is.EqualTo(0));
                Assert.That(context.Runtime.OpenedScenes, Is.Empty);
                Assert.That(context.Runtime.SelectedAvatars, Is.Empty);
                Assert.That(context.Runtime.CloseAutomationWindowIfOpenCount, Is.EqualTo(1));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, hostState.state);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .ToArray();
                Assert.That(events.Select(item => item.eventType), Is.EquivalentTo(new[] { "session-started", "unity-ready" }));
                Assert.That(events.Select(item => item.eventSeq).ToArray(), Is.EqualTo(new[] { 1, 2 }));
                StringAssert.DoesNotContain("loaded Assets/Click ME.unity", events[1].message);
                StringAssert.Contains("ready for suite commands", events[1].message);
            }
        }

        [Test]
        public void SetupSuite_OpensSceneSelectsAvatarAndAddsPrefabOnlyWhenRunAsSuiteStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                Assert.That(context.Runtime.OpenedScenes, Is.Empty);
                Assert.That(context.Runtime.SelectedAvatars, Is.Empty);

                WriteCommand(context.Paths, BuildRunSuiteCommand(1, "cmd_000001_run-suite", "setup-scene-avatar"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.OpenedScenes, Is.EqualTo(new[] { "Assets/Click ME.unity" }));
                Assert.That(context.Runtime.SelectedAvatars, Is.EqualTo(new[] { "Oct25_Dress" }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "open-scene", "open-window", "select-avatar", "add-prefab", "assert-primary-action" }));
            }
        }

        [Test]
        public void SetupSuite_AppliesFixtureMutationAndStepTargetOverridesBeforeAction()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildRunSuiteCommand(11, "cmd_000011_run-suite", "fixture-mutation-host"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[] { ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection }));
                Assert.That(context.Runtime.OpenedScenes, Is.EqualTo(new[] { "Assets/FixtureOverride.unity" }));
                Assert.That(context.Runtime.LastMutationObjectName, Is.EqualTo("Wrong Target"));
            }
        }

        [Test]
        public void SetupSuite_TreatsExpectedDiagnosticStepFailureAsPassed()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000012_run-suite";
                context.Runtime.FailAction("open-scene", "SETUP_SCENE_MISSING: configured scene could not be found at Assets/Missing.unity");

                WriteCommand(context.Paths, BuildRunSuiteCommand(12, commandId, "expected-diagnostic-host"));
                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && item.message.Contains("Expected diagnostic 'SETUP_SCENE_MISSING' matched")), Is.True);
            }
        }

        [Test]
        public void Runner_DoesNotRegisterAfterReady_WhenExitOnReadyIsTrue()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: true))
            {
                Assert.That(context.Runner.IsRunningForTesting, Is.False);
                Assert.That(context.Runtime.RegisterUpdateCount, Is.EqualTo(0));
                Assert.That(context.Runtime.ExitCodes, Is.EquivalentTo(new[] { 0 }));
            }
        }

        [Test]
        public void Heartbeat_RewritesHostStateWithoutChangingReadyState()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                var before = ReadHostState(context.Paths.HostStatePath);
                context.Runtime.AdvanceSeconds(6);
                var after = ReadHostState(context.Paths.HostStatePath);

                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, after.state);
                Assert.AreEqual(before.lastEventSeq, after.lastEventSeq);
                Assert.AreEqual(before.lastCommandSeq, after.lastCommandSeq);
                Assert.AreNotEqual(before.heartbeatUtc, after.heartbeatUtc);
            }
        }

        [Test]
        public void CommandPolling_ProcessesCommandFilesInSequenceOrder()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildRunSuiteCommand(2, "cmd_000002_run-suite"));
                WriteCommand(context.Paths, BuildReviewDecisionCommand(1, "cmd_000001_review-decision"));

                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Length, Is.EqualTo(1));
                Assert.AreEqual("cmd_000001_review-decision", events[0].commandId);
            }
        }

        [Test]
        public void CommandPolling_DoesNotReprocessCommandIdsRecoveredFromEvents()
        {
            using (var context = RunnerTestContext.CreateWithoutStart(exitOnReady: false))
            {
                ASMLiteSmokeProtocol.AppendEventLine(context.Paths.EventsLogPath, new ASMLiteSmokeProtocolEvent
                {
                    protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                    sessionId = "seed-session",
                    eventId = "evt_000001_seed",
                    eventSeq = 1,
                    eventType = "session-started",
                    timestampUtc = "2026-04-23T00:00:00Z",
                    commandId = "cmd_000010_run-suite",
                    runId = string.Empty,
                    groupId = string.Empty,
                    suiteId = string.Empty,
                    caseId = string.Empty,
                    stepId = string.Empty,
                    effectiveResetPolicy = string.Empty,
                    hostState = ASMLiteSmokeProtocol.HostStateIdle,
                    message = "seed",
                    reviewDecisionOptions = Array.Empty<string>(),
                    supportedCapabilities = new[] { "suiteCatalogV1" },
                });

                context.StartRunner();
                WriteCommand(context.Paths, BuildRunSuiteCommand(10, "cmd_000010_run-suite"));
                AdvanceUntilIdleAfterRun(context);

                var rejectedEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal))
                    .ToArray();

                Assert.That(rejectedEvents.Any(item => item.commandId == "cmd_000010_run-suite"), Is.False);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_ExecutesLifecycleRoundtripInOrderWithoutRejecting()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000003_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(3, commandId));
                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Select(item => item.eventType), Is.EqualTo(new[]
                {
                    "suite-started",
                    "case-started",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "suite-passed",
                    "session-idle",
                }));

                ASMLiteSmokeProtocolEvent sessionIdleEvent = events.Single(item => string.Equals(item.eventType, "session-idle", StringComparison.Ordinal));
                Assert.That(sessionIdleEvent.hostState, Is.EqualTo(ASMLiteSmokeProtocol.HostStateIdle));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateIdle, hostState.state);
                StringAssert.Contains("returned to suite selection", hostState.message);

                Assert.That(events.Select(item => item.eventSeq), Is.Ordered.Ascending);
                Assert.That(events.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[]
                    {
                        "rebuild",
                        "hygiene-cleanup-after-rebuild",
                        "vendorize",
                        "hygiene-cleanup-after-vendorize",
                        "detach",
                        "hygiene-cleanup-after-detach",
                        "return-to-package-managed",
                    }));
                Assert.That(
                    events.Any(item => !string.IsNullOrWhiteSpace(item.effectiveResetPolicy)),
                    Is.True,
                    "expected at least one lifecycle event to include effectiveResetPolicy");
                Assert.That(
                    events
                        .Where(item => !string.IsNullOrWhiteSpace(item.effectiveResetPolicy))
                        .All(item => string.Equals(item.effectiveResetPolicy, "FullPackageRebuild", StringComparison.Ordinal)),
                    Is.True);

                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[]
                {
                    "rebuild",
                    "lifecycle-hygiene-cleanup",
                    "vendorize",
                    "lifecycle-hygiene-cleanup",
                    "detach",
                    "lifecycle-hygiene-cleanup",
                    "return-to-package-managed",
                }));

                string resultPath = context.Paths.GetResultPath(1, "lifecycle-roundtrip");
                string eventsSlicePath = context.Paths.GetEventsSlicePath(1, "lifecycle-roundtrip");
                string nunitPath = context.Paths.GetNUnitPath(1, "lifecycle-roundtrip");
                string failurePath = context.Paths.GetFailurePath(1, "lifecycle-roundtrip");

                Assert.That(File.Exists(resultPath), Is.True);
                Assert.That(File.Exists(eventsSlicePath), Is.True);
                Assert.That(File.Exists(nunitPath), Is.True);
                Assert.That(File.Exists(failurePath), Is.False);

                var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFromJson(File.ReadAllText(resultPath));
                Assert.AreEqual("passed", resultDocument.result);
                Assert.AreEqual("FullPackageRebuild", resultDocument.effectiveResetPolicy);
                Assert.That(resultDocument.firstEventSeq, Is.GreaterThan(0));
                Assert.That(resultDocument.lastEventSeq, Is.GreaterThanOrEqualTo(resultDocument.firstEventSeq));
                Assert.That(resultDocument.artifactPaths.resultPath, Does.StartWith("runs/"));
                Assert.That(resultDocument.artifactPaths.eventsSlicePath, Does.StartWith("runs/"));
                Assert.That(resultDocument.artifactPaths.nunitPath, Does.StartWith("runs/"));
                Assert.That(resultDocument.artifactPaths.failurePath, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void CommandPolling_RunSuite_ExecutesPlaymodeSuiteInSingleSession()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000008_run-suite";
                var command = BuildRunSuiteCommand(8, commandId);
                command.runSuite.suiteId = "playmode-runtime-validation";
                command.runSuite.requestedResetDefault = "SceneReload";
                command.runSuite.reason = "playmode-suite-test";

                WriteCommand(context.Paths, command);
                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Last().eventType, Is.EqualTo("session-idle"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("enter-playmode"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("assert-runtime-component-valid"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("exit-playmode"));
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_StreamsEventsAcrossTicksBeforeArtifactsExist()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000070_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(70, commandId));

                context.Runtime.AdvanceSeconds(1);
                var afterDispatchState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateRunning, afterDispatchState.state);
                Assert.AreEqual("run-0070-lifecycle-roundtrip", afterDispatchState.activeRunId);
                var afterDispatchEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(afterDispatchEvents, Is.Empty);

                context.Runtime.AdvanceSeconds(1);
                var streamedEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(streamedEvents.Select(item => item.eventType), Is.EqualTo(new[] { "suite-started" }));
                Assert.That(File.Exists(context.Paths.GetResultPath(1, "lifecycle-roundtrip")), Is.False);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_StepSleepSecondsDelaysStepExecution()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000071_run-suite";
                var command = BuildRunSuiteCommand(71, commandId);
                command.runSuite.stepSleepSeconds = 1.5d;
                WriteCommand(context.Paths, command);

                context.Runtime.AdvanceSeconds(1);
                context.Runtime.AdvanceSeconds(1);
                context.Runtime.AdvanceSeconds(1);
                context.Runtime.AdvanceSeconds(1);
                Assert.That(context.Runtime.ExecutedActions, Is.Empty);

                context.Runtime.AdvanceSeconds(1);
                Assert.That(context.Runtime.ExecutedActions, Is.Empty);

                context.Runtime.AdvanceSeconds(0.5d);
                Assert.That(context.Runtime.ExecutedActions, Is.Not.Empty);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_WaitsBrieflyAfterLifecycleMutationBeforeNextStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000072_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(72, commandId));

                context.Runtime.AdvanceSeconds(1); // command accepted
                context.Runtime.AdvanceSeconds(1); // suite-started
                context.Runtime.AdvanceSeconds(1); // case-started
                context.Runtime.AdvanceSeconds(1); // rebuild step-started
                context.Runtime.AdvanceSeconds(1); // rebuild executes and passes
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild" }));

                context.Runtime.AdvanceSeconds(0.1d);
                var eventsBeforeSettle = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(eventsBeforeSettle.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild" }));

                context.Runtime.AdvanceSeconds(0.15d);
                context.Runtime.AdvanceSeconds(0d);
                var eventsAfterSettle = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(eventsAfterSettle.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild", "hygiene-cleanup-after-rebuild" }));
            }
        }

        [Test]
        public void CommandPolling_AbortRun_WritesAbortedArtifactsAndReviewRequiredState()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000080_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(80, runCommandId));
                context.Runtime.AdvanceSeconds(1);

                var runningState = ReadHostState(context.Paths.HostStatePath);
                const string abortCommandId = "cmd_000081_abort-run";
                WriteCommand(context.Paths, BuildAbortRunCommand(81, abortCommandId, runningState.activeRunId, "lifecycle-roundtrip"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, abortCommandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(events.Select(item => item.eventType), Is.EqualTo(new[]
                {
                    "abort-requested",
                    "run-aborted",
                    "review-required",
                }));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
                Assert.AreEqual(runningState.activeRunId, hostState.activeRunId);

                string resultPath = context.Paths.GetResultPath(1, "lifecycle-roundtrip");
                Assert.That(File.Exists(resultPath), Is.True);
                var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFromJson(File.ReadAllText(resultPath));
                Assert.AreEqual("aborted", resultDocument.result);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_TreatsConsoleErrorsAsStepFailures()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runtime.EmitConsoleErrorDuringAction("vendorize", "Injected Unity console error.");

                const string commandId = "cmd_000012_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(12, commandId));
                AdvanceUntilReviewRequired(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild", "lifecycle-hygiene-cleanup", "vendorize" }));

                string failurePath = context.Paths.GetFailurePath(1, "lifecycle-roundtrip");
                Assert.That(File.Exists(failurePath), Is.True);
                var failureDocument = ASMLiteSmokeArtifactPaths.LoadFailureFromJson(File.ReadAllText(failurePath));
                Assert.AreEqual("vendorize", failureDocument.stepId);
                Assert.That(failureDocument.failureMessage, Does.Contain("Unity console error"));
                Assert.That(failureDocument.failureMessage, Does.Contain("Injected Unity console error"));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_FailsFastAfterFirstFailedStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");

                const string commandId = "cmd_000011_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(11, commandId));
                AdvanceUntilReviewRequired(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild", "hygiene-cleanup-after-rebuild", "vendorize" }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild", "lifecycle-hygiene-cleanup", "vendorize" }));

                string resultPath = context.Paths.GetResultPath(1, "lifecycle-roundtrip");
                string failurePath = context.Paths.GetFailurePath(1, "lifecycle-roundtrip");
                string eventsSlicePath = context.Paths.GetEventsSlicePath(1, "lifecycle-roundtrip");
                Assert.That(File.Exists(resultPath), Is.True);
                Assert.That(File.Exists(failurePath), Is.True);
                Assert.That(File.Exists(eventsSlicePath), Is.True);

                var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFromJson(File.ReadAllText(resultPath));
                Assert.AreEqual("failed", resultDocument.result);
                Assert.AreEqual("FullPackageRebuild", resultDocument.effectiveResetPolicy);

                var failureDocument = ASMLiteSmokeArtifactPaths.LoadFailureFromJson(File.ReadAllText(failurePath));
                Assert.AreEqual("vendorize", failureDocument.stepId);
                Assert.That(failureDocument.failureMessage, Does.Contain("Injected vendorize failure"));
                Assert.AreEqual("FullPackageRebuild", failureDocument.effectiveResetPolicy);
                Assert.That(failureDocument.artifactPaths.failurePath, Does.StartWith("runs/"));
                Assert.That(failureDocument.eventSeqRange.first, Is.GreaterThan(0));
                Assert.That(failureDocument.eventSeqRange.last, Is.GreaterThanOrEqualTo(failureDocument.eventSeqRange.first));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
                StringAssert.Contains("waiting for operator review", hostState.message);

                ASMLiteSmokeProtocolEvent reviewRequiredEvent = events.Single(item => string.Equals(item.eventType, "review-required", StringComparison.Ordinal));
                Assert.That(reviewRequiredEvent.reviewDecisionOptions, Is.EqualTo(new[] { "return-to-suite-list", "rerun-suite", "exit" }));
            }
        }

        [Test]
        public void CommandPolling_RunSuiteIsRejectedWhileReviewDecisionIsPending()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(20, "cmd_000020_run-suite"));
                AdvanceUntilReviewRequired(context);

                int executedActionCount = context.Runtime.ExecutedActions.Count;
                WriteCommand(context.Paths, BuildRunSuiteCommand(21, "cmd_000021_run-suite"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, "cmd_000021_run-suite", StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.True);
                Assert.That(context.Runtime.ExecutedActions.Count, Is.EqualTo(executedActionCount));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_ReturnToSuiteList_TransitionsToIdleWithoutRejecting()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000030_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(30, runCommandId));
                AdvanceUntilReviewRequired(context);

                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000031_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        31,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "return-to-suite-list"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "session-idle", StringComparison.Ordinal)), Is.True);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateIdle, hostState.state);
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_RerunSuite_ReexecutesWithoutRestart()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000040_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(40, runCommandId));
                AdvanceUntilReviewRequired(context);

                int initialActionCount = context.Runtime.ExecutedActions.Count;
                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000041_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        41,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "rerun-suite"));
                AdvanceUntilReviewRequired(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);

                ASMLiteSmokeProtocolEvent[] rerunEvents = events
                    .Where(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(rerunEvents.Any(item => string.Equals(item.eventType, "suite-started", StringComparison.Ordinal)), Is.True);
                Assert.That(rerunEvents.Any(item => string.Equals(item.eventType, "review-required", StringComparison.Ordinal)), Is.True);
                Assert.That(context.Runtime.ExecutedActions.Count, Is.GreaterThan(initialActionCount));
                Assert.That(context.Runtime.ExitCodes, Is.Empty);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_Exit_ShutsDownSession()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000050_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(50, runCommandId));
                AdvanceUntilReviewRequired(context);

                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000051_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        51,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "exit"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "session-exiting", StringComparison.Ordinal)), Is.True);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateExiting, hostState.state);
                Assert.That(context.Runtime.ExitWithoutSavingCodes, Is.EquivalentTo(new[] { 0 }));
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_InvalidDecision_IsRejectedAndKeepsReviewRequiredState()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000060_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(60, runCommandId));
                AdvanceUntilReviewRequired(context);

                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000061_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        61,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "continue"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.True);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
            }
        }

        [TestCase("SceneReload", "Inherit", "SceneReload")]
        [TestCase("SceneReload", "FullPackageRebuild", "FullPackageRebuild")]
        [TestCase("FullPackageRebuild", "Inherit", "FullPackageRebuild")]
        [TestCase("FullPackageRebuild", "SceneReload", "SceneReload")]
        public void ResetService_ResolvesEffectiveResetPolicyDeterministically(string globalDefault, string suiteOverride, string expected)
        {
            string resolved = ASMLiteSmokeResetService.ResolveEffectivePolicy(globalDefault, suiteOverride);
            Assert.AreEqual(expected, resolved);
        }

        [Test]
        public void StallSignal_WritesStalledHostStateAndEvent()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runner.PublishStalledForTesting("Host heartbeat timeout.");

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateStalled, hostState.state);
                Assert.AreEqual("Host heartbeat timeout.", hostState.message);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.AreEqual("host-stalled", events.Last().eventType);
            }
        }

        [Test]
        public void CrashSignal_WritesCrashedHostStateAndEvent()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runner.PublishCrashedForTesting(new InvalidOperationException("boom"));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateCrashed, hostState.state);
                StringAssert.Contains("InvalidOperationException", hostState.message);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.AreEqual("host-crashed", events.Last().eventType);
            }
        }

        [Test]
        public void ShutdownSession_WritesExitingStateAndUnregistersRunner()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildShutdownCommand(4, "cmd_000004_shutdown-session"));
                context.Runtime.AdvanceSeconds(1);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateExiting, hostState.state);
                Assert.That(context.Runner.IsRunningForTesting, Is.False);
                Assert.That(context.Runtime.UnregisterUpdateCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(context.Runtime.ExitCodes, Is.EquivalentTo(new[] { 0 }));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.eventType, "session-exiting", StringComparison.Ordinal)), Is.True);
            }
        }

        private static ASMLiteSmokeHostStateDocument ReadHostState(string hostStatePath)
        {
            string raw = File.ReadAllText(hostStatePath);
            return ASMLiteSmokeProtocol.LoadHostStateFromJson(raw);
        }

        private static void WriteCommand(ASMLiteSmokeSessionPaths paths, ASMLiteSmokeProtocolCommand command)
        {
            string filePath = paths.GetCommandPath(command.commandSeq, command.commandType, command.commandId);
            string json = ASMLiteSmokeProtocol.ToJson(command, prettyPrint: true);
            File.WriteAllText(filePath, json);
        }

        private static void AdvanceUntilReviewRequired(RunnerTestContext context, int maxTicks = 64)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                context.Runtime.AdvanceSeconds(1);
                var hostState = ReadHostState(context.Paths.HostStatePath);
                if (string.Equals(hostState.state, ASMLiteSmokeProtocol.HostStateReviewRequired, StringComparison.Ordinal))
                    return;
            }

            Assert.Fail("Runner did not reach review-required within the expected tick budget.");
        }

        private static void AdvanceUntilIdleAfterRun(RunnerTestContext context, int maxTicks = 64)
        {
            ASMLiteSmokeHostStateDocument lastState = null;
            for (int i = 0; i < maxTicks; i++)
            {
                context.Runtime.AdvanceSeconds(1);
                lastState = ReadHostState(context.Paths.HostStatePath);
                if (string.Equals(lastState.state, ASMLiteSmokeProtocol.HostStateIdle, StringComparison.Ordinal)
                    && lastState.lastEventSeq > 2)
                    return;
            }

            string eventSummary = string.Join(", ", ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                .Select(item => $"{item.eventSeq}:{item.eventType}:{item.message}"));
            string stateSummary = lastState == null
                ? "<no host state>"
                : $"state={lastState.state}; lastEventSeq={lastState.lastEventSeq}; lastCommandSeq={lastState.lastCommandSeq}; message={lastState.message}";
            Assert.Fail($"Runner did not return to idle within the expected tick budget. {stateSummary}. Events: {eventSummary}");
        }

        private static ASMLiteSmokeProtocolCommand BuildRunSuiteCommand(int commandSeq, string commandId, string suiteId = "lifecycle-roundtrip")
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "run-suite",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = new ASMLiteSmokeRunSuitePayload
                {
                    suiteId = suiteId,
                    requestedBy = "operator",
                    requestedResetDefault = "SceneReload",
                    reason = "sequence-order-test",
                    stepSleepSeconds = 0d,
                },
                reviewDecision = null,
                abortRun = null,
            };
        }

        private static ASMLiteSmokeProtocolCommand BuildReviewDecisionCommand(
            int commandSeq,
            string commandId,
            string runId = "run-0001-lifecycle-roundtrip",
            string suiteId = "lifecycle-roundtrip",
            string decision = "continue",
            string notes = "sequence-order-test")
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "review-decision",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = null,
                reviewDecision = new ASMLiteSmokeReviewDecisionPayload
                {
                    runId = runId,
                    suiteId = suiteId,
                    decision = decision,
                    requestedBy = "operator",
                    notes = notes,
                },
                abortRun = null,
            };
        }

        private static ASMLiteSmokeProtocolCommand BuildAbortRunCommand(
            int commandSeq,
            string commandId,
            string runId,
            string suiteId,
            string reason = "operator-abort")
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "abort-run",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = null,
                reviewDecision = null,
                abortRun = new ASMLiteSmokeAbortRunPayload
                {
                    runId = runId,
                    suiteId = suiteId,
                    requestedBy = "operator",
                    reason = reason,
                },
            };
        }

        private static string BuildTestCatalogJson()
        {
            var catalog = new ASMLiteSmokeCatalogDocument
            {
                catalogVersion = 1,
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                fixture = new ASMLiteSmokeFixtureDefinition
                {
                    scenePath = "Assets/Click ME.unity",
                    avatarName = "Oct25_Dress",
                },
                groups = new[]
                {
                    new ASMLiteSmokeGroupDefinition
                    {
                        groupId = "phase08-host-tests",
                        label = "Phase 08 Host Tests",
                        description = "Local catalog used by ASMLiteSmokeOverlayHostTests.",
                        suites = new[]
                        {
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "setup-scene-avatar",
                                label = "Setup Scene / Avatar / Prefab",
                                description = "Loads the canonical scene, selects the target avatar, and adds the prefab scaffold on request.",
                                resetOverride = "Inherit",
                                speed = "quick",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "The scene is open, Oct25_Dress is selected, and the prefab scaffold is ready.",
                                debugHint = "Check scene/avatar fixture names, prefab prerequisites, and window focus if setup stalls.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "setup-scene-avatar",
                                        label = "Scene, avatar, and prefab setup",
                                        description = "Run setup and prefab scaffold steps only when explicitly requested.",
                                        expectedOutcome = "Scene/avatar/window/prefab setup completes.",
                                        debugHint = "Inspect host event stream for setup step boundaries.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition { stepId = "open-scene", label = "Open Scene", description = "Open canonical smoke scene.", actionType = "open-scene", expectedOutcome = "Scene opens.", debugHint = "Inspect scene path." },
                                            new ASMLiteSmokeStepDefinition { stepId = "open-window", label = "Open Window", description = "Open ASM-Lite window.", actionType = "open-window", expectedOutcome = "Window opens.", debugHint = "Inspect window state." },
                                            new ASMLiteSmokeStepDefinition { stepId = "select-avatar", label = "Select Avatar", description = "Select Oct25_Dress.", actionType = "select-avatar", expectedOutcome = "Avatar selected.", debugHint = "Inspect avatar lookup." },
                                            new ASMLiteSmokeStepDefinition { stepId = "add-prefab", label = "Add Prefab", description = "Add the ASM-Lite prefab scaffold.", actionType = "add-prefab", expectedOutcome = "Prefab scaffold is attached.", debugHint = "Inspect avatar prerequisites." },
                                            new ASMLiteSmokeStepDefinition { stepId = "assert-primary-action", label = "Assert Primary Action", description = "Confirm rebuild action is visible.", actionType = "assert-primary-action", expectedOutcome = "Primary action is visible.", debugHint = "Inspect window refresh state." },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "lifecycle-roundtrip",
                                label = "Lifecycle Roundtrip",
                                description = "Validates rebuild/vendorize/detach/return lifecycle.",
                                resetOverride = "FullPackageRebuild",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-lifecycle" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "All lifecycle steps pass in order.",
                                debugHint = "Check lifecycle step ordering and action mapping.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "lifecycle-case-01",
                                        label = "Lifecycle Case",
                                        description = "Run lifecycle sequence from package-managed state.",
                                        expectedOutcome = "Rebuild/vendorize/detach/return all pass.",
                                        debugHint = "Inspect host event stream for lifecycle step boundaries.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition { stepId = "rebuild", label = "Rebuild", description = "Rebuild package state.", actionType = "rebuild", expectedOutcome = "Rebuild succeeds.", debugHint = "Inspect rebuild logs." },
                                            new ASMLiteSmokeStepDefinition { stepId = "hygiene-cleanup-after-rebuild", label = "Hygiene Cleanup", description = "Reset known lifecycle drift after rebuild.", actionType = "lifecycle-hygiene-cleanup", expectedOutcome = "Package-managed baseline restored.", debugHint = "Inspect cleanup after rebuild." },
                                            new ASMLiteSmokeStepDefinition { stepId = "vendorize", label = "Vendorize", description = "Vendorize generated assets.", actionType = "vendorize", expectedOutcome = "Vendorize succeeds.", debugHint = "Inspect vendorized assets." },
                                            new ASMLiteSmokeStepDefinition { stepId = "hygiene-cleanup-after-vendorize", label = "Hygiene Cleanup", description = "Reset known lifecycle drift after vendorize.", actionType = "lifecycle-hygiene-cleanup", expectedOutcome = "Package-managed baseline restored.", debugHint = "Inspect cleanup after vendorize." },
                                            new ASMLiteSmokeStepDefinition { stepId = "detach", label = "Detach", description = "Detach from package-managed state.", actionType = "detach", expectedOutcome = "Detach succeeds.", debugHint = "Confirm detached workspace state." },
                                            new ASMLiteSmokeStepDefinition { stepId = "hygiene-cleanup-after-detach", label = "Hygiene Cleanup", description = "Reset known lifecycle drift after detach.", actionType = "lifecycle-hygiene-cleanup", expectedOutcome = "Package-managed baseline restored.", debugHint = "Inspect cleanup after detach." },
                                            new ASMLiteSmokeStepDefinition { stepId = "return-to-package-managed", label = "Return", description = "Return to package-managed baseline.", actionType = "return-to-package-managed", expectedOutcome = "Return succeeds.", debugHint = "Confirm package-managed state restored." },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "fixture-mutation-host",
                                label = "Fixture Mutation Host",
                                description = "Validates fixture mutation dispatch and step target overrides.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Fixture mutation runs before the catalog action.",
                                debugHint = "Inspect fixture mutation dispatch and step args.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "fixture-mutation-host-case",
                                        label = "Fixture mutation host case",
                                        description = "Apply a wrong-object fixture mutation, then run a step with overridden target paths.",
                                        expectedOutcome = "Mutation and target override both execute.",
                                        debugHint = "Inspect fake runtime mutation and opened scene lists.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "mutate-then-open-scene",
                                                label = "Mutate then open scene",
                                                description = "Apply wrong-object mutation and open override scene.",
                                                actionType = "open-scene",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection,
                                                    scenePath = "Assets/FixtureOverride.unity",
                                                    avatarName = "FixtureOverrideAvatar",
                                                    objectName = "Wrong Target",
                                                },
                                                expectedOutcome = "Mutation runs and override scene opens.",
                                                debugHint = "Inspect fixture mutation args.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "expected-diagnostic-host",
                                label = "Expected Diagnostic Host",
                                description = "Validates active run expected diagnostic handling.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Expected diagnostic failures are reported as passing steps.",
                                debugHint = "Inspect expected diagnostic matching in the active run path.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "expected-diagnostic-host-case",
                                        label = "Expected diagnostic host case",
                                        description = "Run one expected-failure step through active run execution.",
                                        expectedOutcome = "The expected diagnostic is treated as a passed step.",
                                        debugHint = "Inspect expected diagnostic args.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "open-missing-scene",
                                                label = "Open missing scene",
                                                description = "Attempt to open an intentionally missing scene.",
                                                actionType = "open-scene",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    scenePath = "Assets/Missing.unity",
                                                    expectStepFailure = true,
                                                    expectedDiagnosticCode = "SETUP_SCENE_MISSING",
                                                    expectedDiagnosticContains = "scene could not be found",
                                                },
                                                expectedOutcome = "Expected scene diagnostic matches.",
                                                debugHint = "Inspect expected diagnostic matching.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "playmode-runtime-validation",
                                label = "Playmode Runtime Validation",
                                description = "Validates playmode enter/assert/exit flow.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-playmode" },
                                requiresPlayMode = true,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Playmode runtime checks complete successfully.",
                                debugHint = "Inspect playmode transitions and runtime assertion output.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "playmode-case-01",
                                        label = "Playmode Case",
                                        description = "Enter playmode, assert runtime component validity, exit playmode.",
                                        expectedOutcome = "All playmode steps pass.",
                                        debugHint = "Check runtime assertion details and playmode transitions.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition { stepId = "enter-playmode", label = "Enter Playmode", description = "Enter playmode session.", actionType = "enter-playmode", expectedOutcome = "Entered playmode.", debugHint = "Inspect playmode enter diagnostics." },
                                            new ASMLiteSmokeStepDefinition { stepId = "assert-runtime-component-valid", label = "Assert Runtime", description = "Assert runtime component validity.", actionType = "assert-runtime-component-valid", expectedOutcome = "Runtime component is valid.", debugHint = "Check runtime assertion metadata." },
                                            new ASMLiteSmokeStepDefinition { stepId = "exit-playmode", label = "Exit Playmode", description = "Exit playmode session.", actionType = "exit-playmode", expectedOutcome = "Exited playmode.", debugHint = "Inspect playmode exit diagnostics." },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            return JsonUtility.ToJson(catalog, prettyPrint: true);
        }

        private static ASMLiteSmokeProtocolCommand BuildShutdownCommand(int commandSeq, string commandId)
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "shutdown-session",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = null,
                reviewDecision = null,
                abortRun = null,
            };
        }

        private sealed class RunnerTestContext : IDisposable
        {
            private readonly GameObject _avatarObject;
            private bool _disposed;

            private RunnerTestContext(
                string sessionRoot,
                string catalogPath,
                ASMLiteSmokeSessionPaths paths,
                FakeRuntime runtime,
                ASMLiteSmokeOverlayHostConfiguration configuration,
                ASMLiteSmokeOverlayHostRunner runner,
                GameObject avatarObject)
            {
                SessionRoot = sessionRoot;
                CatalogPath = catalogPath;
                Paths = paths;
                Runtime = runtime;
                Configuration = configuration;
                Runner = runner;
                _avatarObject = avatarObject;
            }

            internal string SessionRoot { get; }
            internal string CatalogPath { get; }
            internal ASMLiteSmokeSessionPaths Paths { get; }
            internal FakeRuntime Runtime { get; }
            internal ASMLiteSmokeOverlayHostConfiguration Configuration { get; }
            internal ASMLiteSmokeOverlayHostRunner Runner { get; private set; }

            internal static RunnerTestContext Create(bool exitOnReady)
            {
                RunnerTestContext context = CreateWithoutStart(exitOnReady);
                context.StartRunner();
                return context;
            }

            internal static RunnerTestContext CreateWithoutStart(bool exitOnReady)
            {
                string sessionRoot = Path.Combine(Path.GetTempPath(), $"asmlite-smoke-host-{Guid.NewGuid():N}");
                Directory.CreateDirectory(sessionRoot);

                string catalogPath = Path.Combine(sessionRoot, "catalog.json");
                File.WriteAllText(catalogPath, BuildTestCatalogJson());

                var paths = ASMLiteSmokeArtifactPaths.FromSessionRoot(sessionRoot);
                paths.EnsureSessionLayout();

                var runtime = new FakeRuntime();
                var avatarObject = new GameObject("Oct25_Dress");
                var avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();
                runtime.RegisterAvatar(avatar);

                var configuration = new ASMLiteSmokeOverlayHostConfiguration
                {
                    SessionRootPath = sessionRoot,
                    CatalogPath = catalogPath,
                    ScenePath = "Assets/Click ME.unity",
                    AvatarName = "Oct25_Dress",
                    StartupTimeoutSeconds = 120,
                    HeartbeatSeconds = 5,
                    ExitOnReady = exitOnReady,
                };

                var runner = ASMLiteSmokeOverlayHost.CreateRunnerForTesting(configuration, runtime);
                return new RunnerTestContext(sessionRoot, catalogPath, paths, runtime, configuration, runner, avatarObject);
            }

            internal void StartRunner()
            {
                Runner.Start();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                try
                {
                    Runner?.StopForTesting();
                }
                catch
                {
                    // Ignore teardown errors.
                }

                if (_avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(_avatarObject);

                if (Directory.Exists(SessionRoot))
                    Directory.Delete(SessionRoot, recursive: true);
            }
        }

        private sealed class FakeRuntime : IASMLiteSmokeOverlayHostRuntime
        {
            private readonly Dictionary<string, VRCAvatarDescriptor> _avatars = new Dictionary<string, VRCAvatarDescriptor>(StringComparer.Ordinal);
            private UnityEditor.EditorApplication.CallbackFunction _tick;
            private readonly DateTime _baseUtc = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc);
            private string _activeScenePath = "Assets/Untitled.unity";

            internal int RegisterUpdateCount { get; private set; }
            internal int UnregisterUpdateCount { get; private set; }
            internal int CloseAutomationWindowIfOpenCount { get; private set; }
            internal List<int> ExitWithoutSavingCodes { get; } = new List<int>();
            internal List<int> ExitCodes => ExitWithoutSavingCodes;
            internal List<string> ExecutedActions { get; } = new List<string>();
            internal List<string> OpenedScenes { get; } = new List<string>();
            internal List<string> SelectedAvatars { get; } = new List<string>();
            internal List<string> AppliedFixtureMutations { get; } = new List<string>();
            internal string LastMutationObjectName { get; private set; } = string.Empty;

            private readonly Dictionary<string, string> _forcedFailuresByAction = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly List<Tuple<string, string>> _consoleErrors = new List<Tuple<string, string>>();
            private readonly Dictionary<string, string> _consoleErrorsByAction = new Dictionary<string, string>(StringComparer.Ordinal);
            private bool _consoleErrorCaptureActive;

            internal double CurrentTimeSeconds { get; private set; }

            public string GetActiveScenePath()
            {
                return _activeScenePath;
            }

            public void OpenScene(string scenePath)
            {
                OpenedScenes.Add(scenePath);
                _activeScenePath = scenePath;
            }

            public VRCAvatarDescriptor FindAvatarByName(string avatarName)
            {
                _avatars.TryGetValue(avatarName ?? string.Empty, out VRCAvatarDescriptor avatar);
                return avatar;
            }

            public void SelectAvatarForAutomation(VRCAvatarDescriptor avatar)
            {
                SelectedAvatars.Add(avatar == null || avatar.gameObject == null ? string.Empty : avatar.gameObject.name);
            }

            public void CloseAutomationWindowIfOpen()
            {
                CloseAutomationWindowIfOpenCount++;
            }

            public double GetTimeSinceStartup()
            {
                return CurrentTimeSeconds;
            }

            public string GetUtcNowIso()
            {
                return _baseUtc.AddSeconds(CurrentTimeSeconds).ToString("O", CultureInfo.InvariantCulture);
            }

            public string GetUnityVersion()
            {
                return "2022.3.22f1";
            }

            public void RegisterUpdate(UnityEditor.EditorApplication.CallbackFunction tick)
            {
                RegisterUpdateCount++;
                _tick = tick;
            }

            public void UnregisterUpdate(UnityEditor.EditorApplication.CallbackFunction tick)
            {
                UnregisterUpdateCount++;
                if (_tick == tick)
                    _tick = null;
            }

            public string[] EnumerateCommandFiles(string commandsDirectoryPath)
            {
                if (!Directory.Exists(commandsDirectoryPath))
                    return Array.Empty<string>();

                return Directory.GetFiles(commandsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly);
            }

            public string ReadAllText(string path)
            {
                return File.ReadAllText(path);
            }

            public bool ApplySetupFixtureMutation(
                ASMLiteSmokeStepArgs args,
                string defaultScenePath,
                string defaultAvatarName,
                string evidenceRootPath,
                out string detail,
                out string stackTrace)
            {
                string mutation = args == null || string.IsNullOrWhiteSpace(args.fixtureMutation)
                    ? string.Empty
                    : args.fixtureMutation.Trim();
                AppliedFixtureMutations.Add(mutation);
                LastMutationObjectName = args == null || string.IsNullOrWhiteSpace(args.objectName)
                    ? string.Empty
                    : args.objectName.Trim();
                detail = $"{mutation} fixture mutation applied.";
                stackTrace = string.Empty;
                return true;
            }

            public bool ExecuteCatalogStep(
                string actionType,
                ASMLiteSmokeStepArgs args,
                string scenePath,
                string avatarName,
                out string detail,
                out string stackTrace)
            {
                string normalizedAction = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim();
                ExecutedActions.Add(normalizedAction);

                if (_forcedFailuresByAction.TryGetValue(normalizedAction, out string failureMessage))
                {
                    detail = failureMessage;
                    stackTrace = string.Empty;
                    return false;
                }

                if (string.Equals(normalizedAction, "open-scene", StringComparison.Ordinal))
                    OpenScene(scenePath);
                else if (string.Equals(normalizedAction, "select-avatar", StringComparison.Ordinal))
                    SelectAvatarForAutomation(FindAvatarByName(avatarName));

                if (_consoleErrorCaptureActive && _consoleErrorsByAction.TryGetValue(normalizedAction, out string consoleErrorMessage))
                    _consoleErrors.Add(Tuple.Create(consoleErrorMessage, "Injected Unity console stack trace."));

                detail = $"{normalizedAction} completed.";
                stackTrace = string.Empty;
                return true;
            }

            public void StartConsoleErrorCapture()
            {
                _consoleErrors.Clear();
                _consoleErrorCaptureActive = true;
            }

            public void StopConsoleErrorCapture()
            {
                _consoleErrorCaptureActive = false;
            }

            public int GetConsoleErrorCheckpoint()
            {
                return _consoleErrors.Count;
            }

            public bool TryGetConsoleErrorsSince(int checkpoint, out string detail, out string stackTrace)
            {
                int startIndex = Math.Max(0, Math.Min(checkpoint, _consoleErrors.Count));
                var errors = _consoleErrors.Skip(startIndex).ToArray();
                if (errors.Length == 0)
                {
                    detail = string.Empty;
                    stackTrace = string.Empty;
                    return false;
                }

                detail = string.Join("\n", errors.Select(item => $"Unity console error: {item.Item1}"));
                stackTrace = string.Join("\n\n", errors.Select(item => item.Item2));
                return true;
            }

            public void ExitEditorWithoutSaving(int exitCode)
            {
                ExitWithoutSavingCodes.Add(exitCode);
            }

            internal void RegisterAvatar(VRCAvatarDescriptor avatar)
            {
                if (avatar == null || avatar.gameObject == null)
                    throw new InvalidOperationException("A live avatar descriptor is required.");

                _avatars[avatar.gameObject.name] = avatar;
            }

            internal void FailAction(string actionType, string failureMessage)
            {
                if (string.IsNullOrWhiteSpace(actionType))
                    throw new InvalidOperationException("actionType is required.");

                _forcedFailuresByAction[actionType.Trim()] = string.IsNullOrWhiteSpace(failureMessage)
                    ? "Injected action failure."
                    : failureMessage.Trim();
            }

            internal void EmitConsoleErrorDuringAction(string actionType, string message)
            {
                if (string.IsNullOrWhiteSpace(actionType))
                    throw new InvalidOperationException("actionType is required.");

                _consoleErrorsByAction[actionType.Trim()] = string.IsNullOrWhiteSpace(message)
                    ? "Injected Unity console error."
                    : message.Trim();
            }

            internal void AdvanceSeconds(double seconds)
            {
                CurrentTimeSeconds += seconds;
                _tick?.Invoke();
            }
        }
    }
}
