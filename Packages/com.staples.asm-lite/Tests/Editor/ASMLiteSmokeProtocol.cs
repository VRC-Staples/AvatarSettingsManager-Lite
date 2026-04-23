using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [Serializable]
    internal sealed class ASMLiteSmokeProtocolCommand
    {
        public string protocolVersion;
        public string sessionId;
        public string commandId;
        public int commandSeq;
        public string commandType;
        public string createdAtUtc;
        public ASMLiteSmokeLaunchSessionPayload launchSession;
        public ASMLiteSmokeRunSuitePayload runSuite;
        public ASMLiteSmokeReviewDecisionPayload reviewDecision;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeLaunchSessionPayload
    {
        public int catalogVersion;
        public string catalogPath;
        public string catalogSnapshotPath;
        public string projectPath;
        public string packageVersion;
        public string unityVersion;
        public string overlayVersion;
        public string hostVersion;
        public string globalResetDefault;
        public string requestedBy;
        public string[] capabilities = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class ASMLiteSmokeRunSuitePayload
    {
        public string suiteId;
        public string requestedBy;
        public string requestedResetDefault;
        public string reason;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeReviewDecisionPayload
    {
        public string runId;
        public string suiteId;
        public string decision;
        public string requestedBy;
        public string notes;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeSessionDocument
    {
        public string sessionId;
        public string protocolVersion;
        public int catalogVersion;
        public string catalogPath;
        public string catalogSnapshotPath;
        public string projectPath;
        public string overlayVersion;
        public string hostVersion;
        public string packageVersion;
        public string unityVersion;
        public string[] capabilities = Array.Empty<string>();
        public string globalResetDefault;
        public string createdAtUtc;
        public string updatedAtUtc;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeHostStateDocument
    {
        public string sessionId;
        public string protocolVersion;
        public string state;
        public string hostVersion;
        public string unityVersion;
        public string heartbeatUtc;
        public int lastEventSeq;
        public int lastCommandSeq;
        public string activeRunId;
        public string message;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeProtocolEvent
    {
        public string protocolVersion;
        public string sessionId;
        public string eventId;
        public int eventSeq;
        public string eventType;
        public string timestampUtc;
        public string commandId;
        public string runId;
        public string groupId;
        public string suiteId;
        public string caseId;
        public string stepId;
        public string effectiveResetPolicy;
        public string hostState;
        public string message;
        public string[] reviewDecisionOptions = Array.Empty<string>();
        public string[] supportedCapabilities = Array.Empty<string>();
    }

    internal sealed class ASMLiteSmokeProtocolCompatibilityResult
    {
        internal ASMLiteSmokeProtocolCompatibilityResult(bool isCompatible, string message)
        {
            this.isCompatible = isCompatible;
            this.message = string.IsNullOrWhiteSpace(message) ? string.Empty : message.Trim();
        }

        public bool isCompatible;
        public string message;
    }

    internal static class ASMLiteSmokeProtocol
    {
        internal const string SupportedProtocolVersion = "1.0.0";

        internal const string HostStateReady = "ready";
        internal const string HostStateRunning = "running";
        internal const string HostStateReviewRequired = "review-required";
        internal const string HostStateIdle = "idle";
        internal const string HostStateProtocolError = "protocol-error";
        internal const string HostStateExiting = "exiting";
        internal const string HostStateStalled = "stalled";
        internal const string HostStateCrashed = "crashed";

        private static readonly HashSet<string> s_supportedCommandTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "launch-session",
            "run-suite",
            "review-decision",
            "shutdown-session",
        };

        private static readonly HashSet<string> s_supportedHostStates = new HashSet<string>(StringComparer.Ordinal)
        {
            HostStateReady,
            HostStateRunning,
            HostStateReviewRequired,
            HostStateIdle,
            HostStateProtocolError,
            HostStateExiting,
            HostStateStalled,
            HostStateCrashed,
        };

        internal static ASMLiteSmokeProtocolCommand LoadCommandFixture(string fileName)
        {
            string rawJson = LoadFixtureText(fileName);
            return LoadCommandFromJson(rawJson);
        }

        internal static ASMLiteSmokeProtocolCommand LoadCommandFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException("Smoke protocol command JSON is required.");

            var command = JsonUtility.FromJson<ASMLiteSmokeProtocolCommand>(rawJson);
            if (command == null)
                throw new InvalidOperationException("Smoke protocol command JSON did not deserialize.");

            NormalizeAndValidateCommand(command);
            return command;
        }

        internal static string ToJson(ASMLiteSmokeProtocolCommand command, bool prettyPrint)
        {
            NormalizeAndValidateCommand(command ?? throw new InvalidOperationException("Smoke protocol command is required."));
            return JsonUtility.ToJson(command, prettyPrint);
        }

        internal static ASMLiteSmokeSessionDocument LoadSessionFixture(string fileName)
        {
            string rawJson = LoadFixtureText(fileName);
            return LoadSessionFromJson(rawJson);
        }

        internal static ASMLiteSmokeSessionDocument LoadSessionFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException("Smoke session JSON is required.");

            var session = JsonUtility.FromJson<ASMLiteSmokeSessionDocument>(rawJson);
            if (session == null)
                throw new InvalidOperationException("Smoke session JSON did not deserialize.");

            NormalizeAndValidateSession(session);
            return session;
        }

        internal static string ToJson(ASMLiteSmokeSessionDocument session, bool prettyPrint)
        {
            NormalizeAndValidateSession(session ?? throw new InvalidOperationException("Smoke session document is required."));
            return JsonUtility.ToJson(session, prettyPrint);
        }

        internal static ASMLiteSmokeHostStateDocument LoadHostStateFixture(string fileName)
        {
            string rawJson = LoadFixtureText(fileName);
            return LoadHostStateFromJson(rawJson);
        }

        internal static ASMLiteSmokeHostStateDocument LoadHostStateFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException("Smoke host-state JSON is required.");

            var hostState = JsonUtility.FromJson<ASMLiteSmokeHostStateDocument>(rawJson);
            if (hostState == null)
                throw new InvalidOperationException("Smoke host-state JSON did not deserialize.");

            NormalizeAndValidateHostState(hostState);
            return hostState;
        }

        internal static string ToJson(ASMLiteSmokeHostStateDocument hostState, bool prettyPrint)
        {
            NormalizeAndValidateHostState(hostState ?? throw new InvalidOperationException("Smoke host-state document is required."));
            return JsonUtility.ToJson(hostState, prettyPrint);
        }

        internal static void WriteCommandDocumentAtomically(string commandPath, ASMLiteSmokeProtocolCommand command, bool prettyPrint)
        {
            string json = ToJson(command ?? throw new InvalidOperationException("Smoke protocol command is required."), prettyPrint);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(commandPath, json);
        }

        internal static void WriteSessionDocumentAtomically(string sessionPath, ASMLiteSmokeSessionDocument session, bool prettyPrint)
        {
            string json = ToJson(session ?? throw new InvalidOperationException("Smoke session document is required."), prettyPrint);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(sessionPath, json);
        }

        internal static void WriteHostStateDocumentAtomically(string hostStatePath, ASMLiteSmokeHostStateDocument hostState, bool prettyPrint)
        {
            string json = ToJson(hostState ?? throw new InvalidOperationException("Smoke host-state document is required."), prettyPrint);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(hostStatePath, json);
        }

        internal static ASMLiteSmokeProtocolCompatibilityResult EvaluateCompatibility(
            string hostSupportedProtocolVersion,
            ASMLiteSmokeSessionDocument session,
            string catalogProtocolVersion)
        {
            string hostToken = RequireNonBlank(hostSupportedProtocolVersion, "hostSupportedProtocolVersion");
            NormalizeAndValidateSession(session ?? throw new InvalidOperationException("Smoke session document is required for compatibility checks."));
            string catalogToken = RequireNonBlank(catalogProtocolVersion, "catalogProtocolVersion");

            if (!string.Equals(hostToken, session.protocolVersion, StringComparison.Ordinal)
                || !string.Equals(hostToken, catalogToken, StringComparison.Ordinal)
                || !string.Equals(session.protocolVersion, catalogToken, StringComparison.Ordinal))
            {
                string mismatchMessage =
                    $"Protocol version mismatch: host '{hostToken}', session '{session.protocolVersion}', catalog '{catalogToken}'. "
                    + "Update overlay and host to the same protocolVersion before accepting run-suite commands.";
                return new ASMLiteSmokeProtocolCompatibilityResult(false, mismatchMessage);
            }

            return new ASMLiteSmokeProtocolCompatibilityResult(true, "Protocol versions match exactly.");
        }

        internal static ASMLiteSmokeHostStateDocument BuildProtocolErrorHostState(
            ASMLiteSmokeSessionDocument session,
            string message,
            int lastEventSeq,
            int lastCommandSeq)
        {
            NormalizeAndValidateSession(session ?? throw new InvalidOperationException("Smoke session document is required for protocol-error state."));
            if (lastEventSeq < 0)
                throw new InvalidOperationException("lastEventSeq must be zero or greater.");
            if (lastCommandSeq < 0)
                throw new InvalidOperationException("lastCommandSeq must be zero or greater.");

            var state = new ASMLiteSmokeHostStateDocument
            {
                sessionId = session.sessionId,
                protocolVersion = session.protocolVersion,
                state = HostStateProtocolError,
                hostVersion = session.hostVersion,
                unityVersion = session.unityVersion,
                heartbeatUtc = DateTime.UtcNow.ToString("O"),
                lastEventSeq = lastEventSeq,
                lastCommandSeq = lastCommandSeq,
                activeRunId = string.Empty,
                message = RequireNonBlank(message, "message"),
            };

            NormalizeAndValidateHostState(state);
            return state;
        }

        internal static ASMLiteSmokeProtocolEvent BuildProtocolErrorEvent(
            ASMLiteSmokeSessionDocument session,
            string commandId,
            int eventSeq,
            string message)
        {
            NormalizeAndValidateSession(session ?? throw new InvalidOperationException("Smoke session document is required for protocol-error event."));
            if (eventSeq <= 0)
                throw new InvalidOperationException("eventSeq must be greater than zero.");

            var protocolEvent = new ASMLiteSmokeProtocolEvent
            {
                protocolVersion = session.protocolVersion,
                sessionId = session.sessionId,
                eventId = $"evt_{eventSeq:D6}_protocol-error",
                eventSeq = eventSeq,
                eventType = HostStateProtocolError,
                timestampUtc = DateTime.UtcNow.ToString("O"),
                commandId = RequireNonBlank(commandId, "commandId"),
                hostState = HostStateProtocolError,
                message = RequireNonBlank(message, "message"),
                reviewDecisionOptions = Array.Empty<string>(),
                supportedCapabilities = NormalizeCapabilities(session.capabilities),
            };

            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            int previousSeq = 0;
            NormalizeAndValidateEvent(protocolEvent, 1, eventIds, ref previousSeq);
            return protocolEvent;
        }

        internal static bool CanAcceptRunSuite(ASMLiteSmokeHostStateDocument hostState, out string reason)
        {
            reason = string.Empty;
            if (hostState == null)
            {
                reason = "Host state is required.";
                return false;
            }

            try
            {
                NormalizeAndValidateHostState(hostState);
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }

            if (string.Equals(hostState.state, HostStateProtocolError, StringComparison.Ordinal))
            {
                reason = string.IsNullOrWhiteSpace(hostState.message)
                    ? "run-suite rejected while host remains in protocol-error state."
                    : hostState.message;
                return false;
            }

            if (string.Equals(hostState.state, HostStateExiting, StringComparison.Ordinal))
            {
                reason = "run-suite rejected while host is exiting.";
                return false;
            }

            return true;
        }

        internal static ASMLiteSmokeProtocolEvent[] LoadEventFixture(string fileName)
        {
            string rawNdjson = LoadFixtureText(fileName);
            return LoadEventsFromNdjson(rawNdjson);
        }

        internal static ASMLiteSmokeProtocolEvent[] LoadEventsFromNdjson(string rawNdjson)
        {
            if (string.IsNullOrWhiteSpace(rawNdjson))
                throw new InvalidOperationException("Smoke protocol event NDJSON is required.");

            var lines = rawNdjson
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .ToArray();
            if (lines.Length == 0)
                throw new InvalidOperationException("Smoke protocol event NDJSON requires at least one line.");

            var events = new ASMLiteSmokeProtocolEvent[lines.Length];
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            int previousEventSeq = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var protocolEvent = JsonUtility.FromJson<ASMLiteSmokeProtocolEvent>(lines[i]);
                if (protocolEvent == null)
                    throw new InvalidOperationException($"Smoke protocol event line {i + 1} did not deserialize.");

                NormalizeAndValidateEvent(protocolEvent, i + 1, eventIds, ref previousEventSeq);
                events[i] = protocolEvent;
            }

            return events;
        }

        internal static string ToNdjson(IEnumerable<ASMLiteSmokeProtocolEvent> events)
        {
            var items = (events ?? throw new InvalidOperationException("Smoke protocol events are required."))
                .ToArray();
            if (items.Length == 0)
                throw new InvalidOperationException("Smoke protocol events must not be empty.");

            var builder = new StringBuilder();
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            int previousEventSeq = 0;
            for (int i = 0; i < items.Length; i++)
            {
                NormalizeAndValidateEvent(items[i] ?? throw new InvalidOperationException($"Smoke protocol event index {i} is required."), i + 1, eventIds, ref previousEventSeq);
                builder.Append(JsonUtility.ToJson(items[i], false));
                builder.Append('\n');
            }

            return builder.ToString();
        }

        internal static void AppendEventLine(string eventsLogPath, ASMLiteSmokeProtocolEvent protocolEvent)
        {
            if (string.IsNullOrWhiteSpace(eventsLogPath))
                throw new InvalidOperationException("eventsLogPath must not be blank.");

            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            int previousEventSeq = 0;
            NormalizeAndValidateEvent(protocolEvent ?? throw new InvalidOperationException("protocolEvent is required."), 1, eventIds, ref previousEventSeq);

            string fullPath = Path.GetFullPath(eventsLogPath.Trim());
            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("eventsLogPath must include a parent directory.");

            Directory.CreateDirectory(directory);
            string line = JsonUtility.ToJson(protocolEvent, false) + "\n";
            byte[] payload = Encoding.UTF8.GetBytes(line);
            using (var stream = new FileStream(fullPath, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                stream.Write(payload, 0, payload.Length);
                stream.Flush(flushToDisk: true);
            }
        }

        internal static ASMLiteSmokeProtocolEvent[] LoadEventsFromNdjsonFileTolerant(string eventsLogPath)
        {
            if (string.IsNullOrWhiteSpace(eventsLogPath))
                throw new InvalidOperationException("eventsLogPath must not be blank.");

            string fullPath = Path.GetFullPath(eventsLogPath.Trim());
            if (!File.Exists(fullPath))
                return Array.Empty<ASMLiteSmokeProtocolEvent>();

            string rawNdjson = File.ReadAllText(fullPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(rawNdjson))
                return Array.Empty<ASMLiteSmokeProtocolEvent>();

            return LoadEventsFromNdjsonTolerant(rawNdjson);
        }

        internal static ASMLiteSmokeProtocolEvent[] LoadEventsFromNdjsonTolerant(string rawNdjson)
        {
            if (string.IsNullOrWhiteSpace(rawNdjson))
                throw new InvalidOperationException("Smoke protocol event NDJSON is required.");

            bool hasTerminalNewLine = rawNdjson.EndsWith("\n", StringComparison.Ordinal)
                || rawNdjson.EndsWith("\r", StringComparison.Ordinal);
            string[] lines = rawNdjson.Split('\n');
            var events = new List<ASMLiteSmokeProtocolEvent>(lines.Length);
            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            int previousEventSeq = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                ASMLiteSmokeProtocolEvent protocolEvent;
                try
                {
                    protocolEvent = JsonUtility.FromJson<ASMLiteSmokeProtocolEvent>(line);
                }
                catch (Exception ex) when (i == lines.Length - 1 && !hasTerminalNewLine)
                {
                    _ = ex;
                    break;
                }

                if (protocolEvent == null)
                {
                    if (i == lines.Length - 1 && !hasTerminalNewLine)
                        break;

                    throw new InvalidOperationException($"Smoke protocol event line {i + 1} did not deserialize.");
                }

                NormalizeAndValidateEvent(protocolEvent, i + 1, eventIds, ref previousEventSeq);
                events.Add(protocolEvent);
            }

            if (events.Count == 0)
                throw new InvalidOperationException("Smoke protocol event NDJSON requires at least one complete event line.");

            return events.ToArray();
        }

        internal static HashSet<string> RecoverProcessedCommandIdsFromEventLog(string eventsLogPath)
        {
            var events = LoadEventsFromNdjsonFileTolerant(eventsLogPath);
            return RecoverProcessedCommandIds(events);
        }

        internal static HashSet<string> RecoverProcessedCommandIds(IEnumerable<ASMLiteSmokeProtocolEvent> events)
        {
            var items = (events ?? throw new InvalidOperationException("events are required.")).ToArray();
            if (items.Length == 0)
                return new HashSet<string>(StringComparer.Ordinal);

            var eventIds = new HashSet<string>(StringComparer.Ordinal);
            var processed = new HashSet<string>(StringComparer.Ordinal);
            int previousEventSeq = 0;
            for (int i = 0; i < items.Length; i++)
            {
                var protocolEvent = items[i] ?? throw new InvalidOperationException($"Smoke protocol event index {i} is required.");
                NormalizeAndValidateEvent(protocolEvent, i + 1, eventIds, ref previousEventSeq);

                if (string.Equals(protocolEvent.eventType, "command-rejected", StringComparison.Ordinal))
                    continue;

                if (!string.IsNullOrWhiteSpace(protocolEvent.commandId))
                    processed.Add(protocolEvent.commandId);
            }

            return processed;
        }

        private static string LoadFixtureText(string fileName)
        {
            string fixturePath = Path.Combine(ASMLiteSmokeContractPaths.GetProtocolFixtureDirectory(), fileName ?? string.Empty);
            return File.ReadAllText(fixturePath, Encoding.UTF8);
        }

        private static void NormalizeAndValidateCommand(ASMLiteSmokeProtocolCommand command)
        {
            command.protocolVersion = RequireNonBlank(command.protocolVersion, "protocolVersion");
            command.sessionId = RequireNonBlank(command.sessionId, "sessionId");
            command.commandId = RequireNonBlank(command.commandId, "commandId");
            if (command.commandSeq <= 0)
                throw new InvalidOperationException("commandSeq must be greater than zero.");
            command.commandType = RequireNonBlank(command.commandType, "commandType");
            if (!s_supportedCommandTypes.Contains(command.commandType))
                throw new InvalidOperationException($"commandType '{command.commandType}' is not supported.");
            command.createdAtUtc = RequireNonBlank(command.createdAtUtc, "createdAtUtc");

            switch (command.commandType)
            {
                case "launch-session":
                    if (!HasLaunchSessionPayload(command.launchSession))
                        throw new InvalidOperationException("launchSession payload is required for commandType 'launch-session'.");
                    if (HasRunSuitePayload(command.runSuite) || HasReviewDecisionPayload(command.reviewDecision))
                        throw new InvalidOperationException("launch-session command must not include unrelated payloads.");
                    NormalizeAndValidateLaunchSession(command.launchSession);
                    command.runSuite = null;
                    command.reviewDecision = null;
                    break;
                case "run-suite":
                    if (!HasRunSuitePayload(command.runSuite))
                        throw new InvalidOperationException("runSuite payload is required for commandType 'run-suite'.");
                    if (HasLaunchSessionPayload(command.launchSession) || HasReviewDecisionPayload(command.reviewDecision))
                        throw new InvalidOperationException("run-suite command must not include unrelated payloads.");
                    NormalizeAndValidateRunSuite(command.runSuite);
                    command.launchSession = null;
                    command.reviewDecision = null;
                    break;
                case "review-decision":
                    if (!HasReviewDecisionPayload(command.reviewDecision))
                        throw new InvalidOperationException("reviewDecision payload is required for commandType 'review-decision'.");
                    if (HasLaunchSessionPayload(command.launchSession) || HasRunSuitePayload(command.runSuite))
                        throw new InvalidOperationException("review-decision command must not include unrelated payloads.");
                    NormalizeAndValidateReviewDecision(command.reviewDecision);
                    command.launchSession = null;
                    command.runSuite = null;
                    break;
                case "shutdown-session":
                    if (HasLaunchSessionPayload(command.launchSession) || HasRunSuitePayload(command.runSuite) || HasReviewDecisionPayload(command.reviewDecision))
                        throw new InvalidOperationException("shutdown-session command must not include typed payloads.");
                    command.launchSession = null;
                    command.runSuite = null;
                    command.reviewDecision = null;
                    break;
            }
        }

        private static bool HasLaunchSessionPayload(ASMLiteSmokeLaunchSessionPayload payload)
        {
            if (payload == null)
                return false;

            return payload.catalogVersion > 0
                || !string.IsNullOrWhiteSpace(payload.catalogPath)
                || !string.IsNullOrWhiteSpace(payload.catalogSnapshotPath)
                || !string.IsNullOrWhiteSpace(payload.projectPath)
                || !string.IsNullOrWhiteSpace(payload.packageVersion)
                || !string.IsNullOrWhiteSpace(payload.unityVersion)
                || !string.IsNullOrWhiteSpace(payload.overlayVersion)
                || !string.IsNullOrWhiteSpace(payload.hostVersion)
                || !string.IsNullOrWhiteSpace(payload.globalResetDefault)
                || !string.IsNullOrWhiteSpace(payload.requestedBy)
                || HasAnyNonBlank(payload.capabilities);
        }

        private static bool HasRunSuitePayload(ASMLiteSmokeRunSuitePayload payload)
        {
            if (payload == null)
                return false;

            return !string.IsNullOrWhiteSpace(payload.suiteId)
                || !string.IsNullOrWhiteSpace(payload.requestedBy)
                || !string.IsNullOrWhiteSpace(payload.requestedResetDefault)
                || !string.IsNullOrWhiteSpace(payload.reason);
        }

        private static bool HasReviewDecisionPayload(ASMLiteSmokeReviewDecisionPayload payload)
        {
            if (payload == null)
                return false;

            return !string.IsNullOrWhiteSpace(payload.runId)
                || !string.IsNullOrWhiteSpace(payload.suiteId)
                || !string.IsNullOrWhiteSpace(payload.decision)
                || !string.IsNullOrWhiteSpace(payload.requestedBy)
                || !string.IsNullOrWhiteSpace(payload.notes);
        }

        private static bool HasAnyNonBlank(IEnumerable<string> values)
        {
            if (values == null)
                return false;

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return true;
            }

            return false;
        }

        private static void NormalizeAndValidateLaunchSession(ASMLiteSmokeLaunchSessionPayload payload)
        {
            if (payload.catalogVersion <= 0)
                throw new InvalidOperationException("launchSession.catalogVersion must be greater than zero.");

            payload.catalogPath = RequireNonBlank(payload.catalogPath, "launchSession.catalogPath");
            payload.catalogSnapshotPath = RequireNonBlank(payload.catalogSnapshotPath, "launchSession.catalogSnapshotPath");
            payload.projectPath = RequireNonBlank(payload.projectPath, "launchSession.projectPath");
            payload.packageVersion = RequireNonBlank(payload.packageVersion, "launchSession.packageVersion");
            payload.unityVersion = RequireNonBlank(payload.unityVersion, "launchSession.unityVersion");
            payload.overlayVersion = RequireNonBlank(payload.overlayVersion, "launchSession.overlayVersion");
            payload.hostVersion = RequireNonBlank(payload.hostVersion, "launchSession.hostVersion");
            payload.globalResetDefault = RequireNonBlank(payload.globalResetDefault, "launchSession.globalResetDefault");
            payload.requestedBy = RequireNonBlank(payload.requestedBy, "launchSession.requestedBy");
            payload.capabilities = NormalizeCapabilities(payload.capabilities);
        }

        private static void NormalizeAndValidateRunSuite(ASMLiteSmokeRunSuitePayload payload)
        {
            payload.suiteId = RequireNonBlank(payload.suiteId, "runSuite.suiteId");
            payload.requestedBy = RequireNonBlank(payload.requestedBy, "runSuite.requestedBy");
            payload.requestedResetDefault = RequireNonBlank(payload.requestedResetDefault, "runSuite.requestedResetDefault");
            payload.reason = RequireNonBlank(payload.reason, "runSuite.reason");
        }

        private static void NormalizeAndValidateReviewDecision(ASMLiteSmokeReviewDecisionPayload payload)
        {
            payload.runId = RequireNonBlank(payload.runId, "reviewDecision.runId");
            payload.suiteId = RequireNonBlank(payload.suiteId, "reviewDecision.suiteId");
            payload.decision = RequireNonBlank(payload.decision, "reviewDecision.decision");
            payload.requestedBy = RequireNonBlank(payload.requestedBy, "reviewDecision.requestedBy");
            payload.notes = RequireNonBlank(payload.notes, "reviewDecision.notes");
        }

        private static void NormalizeAndValidateSession(ASMLiteSmokeSessionDocument session)
        {
            session.sessionId = RequireNonBlank(session.sessionId, "sessionId");
            session.protocolVersion = RequireNonBlank(session.protocolVersion, "protocolVersion");
            if (session.catalogVersion <= 0)
                throw new InvalidOperationException("catalogVersion must be greater than zero.");
            session.catalogPath = RequireNonBlank(session.catalogPath, "catalogPath");
            session.catalogSnapshotPath = RequireNonBlank(session.catalogSnapshotPath, "catalogSnapshotPath");
            session.projectPath = RequireNonBlank(session.projectPath, "projectPath");
            session.overlayVersion = RequireNonBlank(session.overlayVersion, "overlayVersion");
            session.hostVersion = RequireNonBlank(session.hostVersion, "hostVersion");
            session.packageVersion = RequireNonBlank(session.packageVersion, "packageVersion");
            session.unityVersion = RequireNonBlank(session.unityVersion, "unityVersion");
            session.globalResetDefault = RequireNonBlank(session.globalResetDefault, "globalResetDefault");
            session.createdAtUtc = RequireNonBlank(session.createdAtUtc, "createdAtUtc");
            session.updatedAtUtc = NormalizeOptional(session.updatedAtUtc);
            session.capabilities = NormalizeCapabilities(session.capabilities);
        }

        private static void NormalizeAndValidateHostState(ASMLiteSmokeHostStateDocument hostState)
        {
            hostState.sessionId = RequireNonBlank(hostState.sessionId, "sessionId");
            hostState.protocolVersion = RequireNonBlank(hostState.protocolVersion, "protocolVersion");
            hostState.state = RequireNonBlank(hostState.state, "state");
            if (!s_supportedHostStates.Contains(hostState.state))
                throw new InvalidOperationException($"state '{hostState.state}' is not supported.");
            hostState.hostVersion = RequireNonBlank(hostState.hostVersion, "hostVersion");
            hostState.unityVersion = RequireNonBlank(hostState.unityVersion, "unityVersion");
            hostState.heartbeatUtc = RequireNonBlank(hostState.heartbeatUtc, "heartbeatUtc");
            if (hostState.lastEventSeq < 0)
                throw new InvalidOperationException("lastEventSeq must be zero or greater.");
            if (hostState.lastCommandSeq < 0)
                throw new InvalidOperationException("lastCommandSeq must be zero or greater.");
            hostState.activeRunId = NormalizeOptional(hostState.activeRunId);
            hostState.message = RequireNonBlank(hostState.message, "message");
        }

        private static void NormalizeAndValidateEvent(
            ASMLiteSmokeProtocolEvent protocolEvent,
            int lineNumber,
            HashSet<string> eventIds,
            ref int previousEventSeq)
        {
            string prefix = $"event line {lineNumber}";
            protocolEvent.protocolVersion = RequireNonBlank(protocolEvent.protocolVersion, prefix + " protocolVersion");
            protocolEvent.sessionId = RequireNonBlank(protocolEvent.sessionId, prefix + " sessionId");
            protocolEvent.eventId = RequireNonBlank(protocolEvent.eventId, prefix + " eventId");
            if (!eventIds.Add(protocolEvent.eventId))
                throw new InvalidOperationException(prefix + " eventId must be unique.");
            if (protocolEvent.eventSeq <= 0)
                throw new InvalidOperationException(prefix + " eventSeq must be greater than zero.");
            if (protocolEvent.eventSeq <= previousEventSeq)
                throw new InvalidOperationException(prefix + " eventSeq must be strictly increasing.");
            previousEventSeq = protocolEvent.eventSeq;
            protocolEvent.eventType = RequireNonBlank(protocolEvent.eventType, prefix + " eventType");
            protocolEvent.timestampUtc = RequireNonBlank(protocolEvent.timestampUtc, prefix + " timestampUtc");
            protocolEvent.commandId = RequireNonBlank(protocolEvent.commandId, prefix + " commandId");
            protocolEvent.message = RequireNonBlank(protocolEvent.message, prefix + " message");
            protocolEvent.hostState = NormalizeOptional(protocolEvent.hostState);
            if (!string.IsNullOrEmpty(protocolEvent.hostState) && !s_supportedHostStates.Contains(protocolEvent.hostState))
                throw new InvalidOperationException(prefix + $" hostState '{protocolEvent.hostState}' is not supported.");
            protocolEvent.runId = NormalizeOptional(protocolEvent.runId);
            protocolEvent.groupId = NormalizeOptional(protocolEvent.groupId);
            protocolEvent.suiteId = NormalizeOptional(protocolEvent.suiteId);
            protocolEvent.caseId = NormalizeOptional(protocolEvent.caseId);
            protocolEvent.stepId = NormalizeOptional(protocolEvent.stepId);
            protocolEvent.effectiveResetPolicy = NormalizeOptional(protocolEvent.effectiveResetPolicy);
            protocolEvent.reviewDecisionOptions = NormalizeStringArray(protocolEvent.reviewDecisionOptions);
            protocolEvent.supportedCapabilities = NormalizeCapabilities(protocolEvent.supportedCapabilities);
        }

        private static string[] NormalizeCapabilities(string[] values)
        {
            return NormalizeStringArray(values)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] NormalizeStringArray(string[] values)
        {
            return values == null
                ? Array.Empty<string>()
                : values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToArray();
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string RequireNonBlank(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(fieldName + " must not be blank.");

            return value.Trim();
        }
    }
}
