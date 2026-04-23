using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeAtomicIoTests
    {
        [Test]
        public void AtomicWrite_replaces_existing_json_document_atomically()
        {
            WithSessionPaths(paths =>
            {
                var ready = ASMLiteSmokeProtocol.LoadHostStateFixture("host-state.ready.json");
                ASMLiteSmokeProtocol.WriteHostStateDocumentAtomically(paths.HostStatePath, ready, prettyPrint: false);

                var protocolError = ASMLiteSmokeProtocol.LoadHostStateFixture("host-state.protocol-error.json");
                ASMLiteSmokeProtocol.WriteHostStateDocumentAtomically(paths.HostStatePath, protocolError, prettyPrint: false);

                string persisted = File.ReadAllText(paths.HostStatePath, Encoding.UTF8);
                var roundTripped = ASMLiteSmokeProtocol.LoadHostStateFromJson(persisted);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateProtocolError, roundTripped.state);
            });
        }

        [Test]
        public void AtomicWrite_interrupted_promote_keeps_previous_json_visible()
        {
            WithSessionPaths(paths =>
            {
                const string baselineJson = "{\"state\":\"ready\"}";
                ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(paths.HostStatePath, baselineJson);

                var exception = Assert.Throws<InvalidOperationException>(() =>
                    ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(
                        paths.HostStatePath,
                        "{\"state\":\"running\"}",
                        _ => throw new InvalidOperationException("simulated interruption before promote")));

                StringAssert.Contains("simulated interruption", exception.Message);
                Assert.AreEqual(baselineJson, File.ReadAllText(paths.HostStatePath, Encoding.UTF8));

                string directory = Path.GetDirectoryName(paths.HostStatePath);
                Assert.That(directory, Is.Not.Null.And.Not.Empty);
                string fileName = Path.GetFileName(paths.HostStatePath);
                string[] tempFiles = Directory.GetFiles(directory, $"{fileName}.tmp-*");
                Assert.That(tempFiles, Is.Empty);
            });
        }

        [Test]
        public void EventLog_tolerates_truncated_final_ndjson_line()
        {
            WithSessionPaths(paths =>
            {
                var fixtureEvents = ASMLiteSmokeProtocol.LoadEventFixture("events.sample.ndjson");
                ASMLiteSmokeProtocol.AppendEventLine(paths.EventsLogPath, fixtureEvents[0]);
                ASMLiteSmokeProtocol.AppendEventLine(paths.EventsLogPath, fixtureEvents[1]);

                File.AppendAllText(paths.EventsLogPath, "{\"protocolVersion\":\"1.0.0\",\"sessionId\":\"session-20260423T043708Z-8f02f9b1\"", Encoding.UTF8);

                var parsed = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(paths.EventsLogPath);
                Assert.AreEqual(2, parsed.Length);
                Assert.AreEqual(2, parsed.Last().eventSeq);
            });
        }

        [Test]
        public void ReplayRecovery_derives_processed_command_ids_from_event_log_without_duplicates()
        {
            WithSessionPaths(paths =>
            {
                var fixtureEvents = ASMLiteSmokeProtocol.LoadEventFixture("events.sample.ndjson");
                foreach (var protocolEvent in fixtureEvents)
                    ASMLiteSmokeProtocol.AppendEventLine(paths.EventsLogPath, protocolEvent);

                ASMLiteSmokeProtocol.AppendEventLine(paths.EventsLogPath, new ASMLiteSmokeProtocolEvent
                {
                    protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                    sessionId = fixtureEvents[0].sessionId,
                    eventId = "evt_000012_command-rejected",
                    eventSeq = 12,
                    eventType = "command-rejected",
                    timestampUtc = "2026-04-23T05:00:00Z",
                    commandId = "cmd_000004_run-suite",
                    runId = string.Empty,
                    groupId = string.Empty,
                    suiteId = string.Empty,
                    caseId = string.Empty,
                    stepId = string.Empty,
                    effectiveResetPolicy = string.Empty,
                    hostState = ASMLiteSmokeProtocol.HostStateProtocolError,
                    message = "run-suite rejected while host remains in protocol-error state.",
                    reviewDecisionOptions = Array.Empty<string>(),
                    supportedCapabilities = new[]
                    {
                        "suiteCatalogV1",
                        "sessionArtifactPaths",
                        "reviewDecision",
                        "testFilterNamespaces",
                    },
                });

                HashSet<string> processed = ASMLiteSmokeProtocol.RecoverProcessedCommandIdsFromEventLog(paths.EventsLogPath);
                Assert.That(processed, Is.EquivalentTo(new[]
                {
                    "cmd_000001_launch-session",
                    "cmd_000002_run-suite",
                    "cmd_000003_review-decision",
                }));
                Assert.That(processed.Contains("cmd_000004_run-suite"), Is.False);
            });
        }

        private static void WithSessionPaths(Action<ASMLiteSmokeSessionPaths> action)
        {
            string sessionRoot = Path.Combine(Path.GetTempPath(), $"asmlite-smoke-atomic-io-{Guid.NewGuid():N}");
            var paths = ASMLiteSmokeArtifactPaths.FromSessionRoot(sessionRoot);
            paths.EnsureSessionLayout();

            try
            {
                action(paths);
            }
            finally
            {
                if (Directory.Exists(sessionRoot))
                    Directory.Delete(sessionRoot, recursive: true);
            }
        }
    }
}
