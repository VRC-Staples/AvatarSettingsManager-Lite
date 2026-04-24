using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
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
        public void Runner_RemainsRegisteredAfterReady_WhenExitOnReadyIsFalse()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                Assert.That(context.Runner.IsRunningForTesting, Is.True);
                Assert.That(context.Runtime.RegisterUpdateCount, Is.EqualTo(1));
                Assert.That(context.Runtime.UnregisterUpdateCount, Is.EqualTo(0));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, hostState.state);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Select(item => item.eventType), Is.EquivalentTo(new[] { "session-started", "unity-ready" }));
                Assert.That(events.Select(item => item.eventSeq).ToArray(), Is.EqualTo(new[] { 1, 2 }));
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

                context.Runtime.AdvanceSeconds(1);

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
                context.Runtime.AdvanceSeconds(1);

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
                context.Runtime.AdvanceSeconds(1);

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
                    "suite-passed",
                }));

                Assert.That(events.Select(item => item.eventSeq), Is.Ordered.Ascending);
                Assert.That(events.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild", "vendorize", "detach", "return-to-package-managed" }));
                Assert.That(events.All(item => string.Equals(item.effectiveResetPolicy, "FullPackageRebuild", StringComparison.Ordinal)), Is.True);

                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[]
                {
                    "rebuild",
                    "vendorize",
                    "detach",
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
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Last().eventType, Is.EqualTo("suite-passed"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("enter-playmode"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("assert-runtime-component-valid"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("exit-playmode"));
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
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
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild", "vendorize" }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild", "vendorize" }));

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
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, hostState.state);
                StringAssert.Contains("failed", hostState.message);
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

        private static ASMLiteSmokeProtocolCommand BuildRunSuiteCommand(int commandSeq, string commandId)
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
                    suiteId = "lifecycle-roundtrip",
                    requestedBy = "operator",
                    requestedResetDefault = "SceneReload",
                    reason = "sequence-order-test",
                },
                reviewDecision = null,
            };
        }

        private static ASMLiteSmokeProtocolCommand BuildReviewDecisionCommand(int commandSeq, string commandId)
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
                    runId = "run-0001",
                    suiteId = "lifecycle-roundtrip",
                    decision = "continue",
                    requestedBy = "operator",
                    notes = "sequence-order-test",
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
                                suiteId = "lifecycle-roundtrip",
                                label = "Lifecycle Roundtrip",
                                description = "Validates rebuild/vendorize/detach/return lifecycle.",
                                resetOverride = "FullPackageRebuild",
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
                                            new ASMLiteSmokeStepDefinition { stepId = "vendorize", label = "Vendorize", description = "Vendorize generated assets.", actionType = "vendorize", expectedOutcome = "Vendorize succeeds.", debugHint = "Inspect vendorized assets." },
                                            new ASMLiteSmokeStepDefinition { stepId = "detach", label = "Detach", description = "Detach from package-managed state.", actionType = "detach", expectedOutcome = "Detach succeeds.", debugHint = "Confirm detached workspace state." },
                                            new ASMLiteSmokeStepDefinition { stepId = "return-to-package-managed", label = "Return", description = "Return to package-managed baseline.", actionType = "return-to-package-managed", expectedOutcome = "Return succeeds.", debugHint = "Confirm package-managed state restored." },
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

            internal int RegisterUpdateCount { get; private set; }
            internal int UnregisterUpdateCount { get; private set; }
            internal List<int> ExitCodes { get; } = new List<int>();
            internal List<string> ExecutedActions { get; } = new List<string>();

            private readonly Dictionary<string, string> _forcedFailuresByAction = new Dictionary<string, string>(StringComparer.Ordinal);

            internal double CurrentTimeSeconds { get; private set; }

            public string GetActiveScenePath()
            {
                return "Assets/Click ME.unity";
            }

            public void OpenScene(string scenePath)
            {
                // No-op in tests.
            }

            public VRCAvatarDescriptor FindAvatarByName(string avatarName)
            {
                _avatars.TryGetValue(avatarName ?? string.Empty, out VRCAvatarDescriptor avatar);
                return avatar;
            }

            public void SelectAvatarForAutomation(VRCAvatarDescriptor avatar)
            {
                // No-op in tests.
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

            public bool ExecuteCatalogStep(string actionType, string avatarName, out string detail, out string stackTrace)
            {
                string normalizedAction = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim();
                ExecutedActions.Add(normalizedAction);

                if (_forcedFailuresByAction.TryGetValue(normalizedAction, out string failureMessage))
                {
                    detail = failureMessage;
                    stackTrace = string.Empty;
                    return false;
                }

                detail = $"{normalizedAction} completed.";
                stackTrace = string.Empty;
                return true;
            }

            public void ExitEditor(int exitCode)
            {
                ExitCodes.Add(exitCode);
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

            internal void AdvanceSeconds(double seconds)
            {
                CurrentTimeSeconds += seconds;
                _tick?.Invoke();
            }
        }
    }
}
