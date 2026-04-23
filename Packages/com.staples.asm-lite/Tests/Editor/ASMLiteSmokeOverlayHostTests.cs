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

                Assert.That(events.Length, Is.EqualTo(2));
                Assert.AreEqual("cmd_000001_review-decision", events[0].commandId);
                Assert.AreEqual("cmd_000002_run-suite", events[1].commandId);
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
        public void CommandPolling_RunSuiteBeforeExecutor_IsRejectedWithoutExiting()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildRunSuiteCommand(3, "cmd_000003_run-suite"));
                context.Runtime.AdvanceSeconds(1);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, hostState.state);
                StringAssert.Contains("Phase 08 suite executor", hostState.message);
                Assert.That(context.Runtime.ExitCodes, Is.Empty);

                var rejected = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, "cmd_000003_run-suite", StringComparison.Ordinal));
                Assert.AreEqual("command-rejected", rejected.eventType);
            }
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
                File.WriteAllText(catalogPath, "{}");

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

            internal void AdvanceSeconds(double seconds)
            {
                CurrentTimeSeconds += seconds;
                _tick?.Invoke();
            }
        }
    }
}
