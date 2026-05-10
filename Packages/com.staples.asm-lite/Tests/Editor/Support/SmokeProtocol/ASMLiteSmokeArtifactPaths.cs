using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [Serializable]
    internal sealed class ASMLiteSmokeArtifactReferences
    {
        public string resultPath;
        public string failurePath;
        public string eventsSlicePath;
        public string nunitPath;
        public string debugSummaryPath;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeRunResultDocument
    {
        public string protocolVersion;
        public string sessionId;
        public string runId;
        public string suiteId;
        public string suiteLabel;
        public string groupId;
        public string groupLabel;
        public string result;
        public string startedAtUtc;
        public string endedAtUtc;
        public double durationSeconds;
        public string effectiveResetPolicy;
        public int firstEventSeq;
        public int lastEventSeq;
        public ASMLiteSmokeArtifactReferences artifactPaths;
        public string catalogSnapshotPath;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeFailureEventSeqRange
    {
        public int first;
        public int last;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeFailureDocument
    {
        public string protocolVersion;
        public string sessionId;
        public string runId;
        public string suiteId;
        public string suiteLabel;
        public string caseId;
        public string caseLabel;
        public string stepId;
        public string stepLabel;
        public string failureMessage;
        public string stackTrace;
        public string effectiveResetPolicy;
        public string scenePath;
        public string avatarName;
        public string commandId;
        public ASMLiteSmokeFailureEventSeqRange eventSeqRange;
        public string[] lastEvents = Array.Empty<string>();
        public string debugHint;
        public ASMLiteSmokeArtifactReferences artifactPaths;
        public string timestampUtc;
    }

    internal sealed class ASMLiteSmokeSessionPaths
    {
        private static readonly HashSet<string> s_windowsReservedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

        private const string SessionMetadataFileName = "session.json";
        private const string CatalogSnapshotFileName = "suite-catalog.snapshot.json";
        private const string CommandsDirectoryName = "commands";
        private const string EventsDirectoryName = "events";
        private const string EventsLogFileName = "events.ndjson";
        private const string HostStateFileName = "host-state.json";
        private const string RunsDirectoryName = "runs";
        private const string ResultFileName = "result.json";
        private const string FailureFileName = "failure.json";
        private const string EventsSliceFileName = "events.slice.ndjson";
        private const string NUnitFileName = "nunit.xml";
        private const string DebugSummaryFileName = "debug-summary.txt";

        internal ASMLiteSmokeSessionPaths(string sessionRootPath)
        {
            SessionRootPath = Path.GetFullPath(RequireNonBlank(sessionRootPath, nameof(sessionRootPath)));
        }

        internal string SessionRootPath { get; }

        internal string SessionMetadataPath => Path.Combine(SessionRootPath, SessionMetadataFileName);

        internal string CatalogSnapshotPath => Path.Combine(SessionRootPath, CatalogSnapshotFileName);

        internal string CommandsDirectoryPath => Path.Combine(SessionRootPath, CommandsDirectoryName);

        internal string EventsDirectoryPath => Path.Combine(SessionRootPath, EventsDirectoryName);

        internal string EventsLogPath => Path.Combine(EventsDirectoryPath, EventsLogFileName);

        internal string HostStatePath => Path.Combine(SessionRootPath, HostStateFileName);

        internal string RunsDirectoryPath => Path.Combine(SessionRootPath, RunsDirectoryName);

        internal void EnsureSessionLayout()
        {
            Directory.CreateDirectory(CommandsDirectoryPath);
            Directory.CreateDirectory(EventsDirectoryPath);
            Directory.CreateDirectory(RunsDirectoryPath);
        }

        internal string BuildCommandFileName(int commandSeq, string commandType, string commandId)
        {
            if (commandSeq <= 0)
                throw new InvalidOperationException("commandSeq must be greater than zero.");

            string normalizedType = NormalizePortableIdentifier(commandType, nameof(commandType));
            string normalizedCommandId = NormalizePortableIdentifier(commandId, nameof(commandId));
            string fileName = $"{commandSeq:D6}-{normalizedType}-{normalizedCommandId}.json";
            ValidatePortablePathSegment(fileName, "command file name");
            return fileName;
        }

        internal string GetCommandPath(int commandSeq, string commandType, string commandId)
        {
            return Path.Combine(CommandsDirectoryPath, BuildCommandFileName(commandSeq, commandType, commandId));
        }

        internal string BuildRunDirectoryName(int runOrdinal, string suiteId)
        {
            if (runOrdinal <= 0)
                throw new InvalidOperationException("runOrdinal must be greater than zero.");

            string normalizedSuiteId = NormalizePortableIdentifier(suiteId, nameof(suiteId));
            string runDirectoryName = $"run-{runOrdinal:D4}-{normalizedSuiteId}";
            ValidatePortablePathSegment(runDirectoryName, "run directory name");
            return runDirectoryName;
        }

        internal string GetRunDirectoryPath(int runOrdinal, string suiteId)
        {
            return Path.Combine(RunsDirectoryPath, BuildRunDirectoryName(runOrdinal, suiteId));
        }

        internal string GetResultPath(int runOrdinal, string suiteId)
        {
            return Path.Combine(GetRunDirectoryPath(runOrdinal, suiteId), ResultFileName);
        }

        internal string GetFailurePath(int runOrdinal, string suiteId)
        {
            return Path.Combine(GetRunDirectoryPath(runOrdinal, suiteId), FailureFileName);
        }

        internal string GetEventsSlicePath(int runOrdinal, string suiteId)
        {
            return Path.Combine(GetRunDirectoryPath(runOrdinal, suiteId), EventsSliceFileName);
        }

        internal string GetNUnitPath(int runOrdinal, string suiteId)
        {
            return Path.Combine(GetRunDirectoryPath(runOrdinal, suiteId), NUnitFileName);
        }

        internal string GetDebugSummaryPath(int runOrdinal, string suiteId)
        {
            return Path.Combine(GetRunDirectoryPath(runOrdinal, suiteId), DebugSummaryFileName);
        }

        internal string ResolveSessionRelativePath(string relativePath, string fieldName)
        {
            string normalizedRelativePath = NormalizeRelativeArtifactPath(relativePath, fieldName);
            string combinedPath = Path.Combine(SessionRootPath, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            string fullPath = Path.GetFullPath(combinedPath);
            string rootWithSeparator = SessionRootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? SessionRootPath
                : SessionRootPath + Path.DirectorySeparatorChar;

            if (!string.Equals(fullPath, SessionRootPath, StringComparison.Ordinal)
                && !fullPath.StartsWith(rootWithSeparator, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(fieldName + " must stay under the session root.");
            }

            return fullPath;
        }

        internal static string NormalizeRelativeArtifactPath(string relativePath, string fieldName)
        {
            string normalized = RequireNonBlank(relativePath, fieldName).Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal)
                || normalized.StartsWith("~/", StringComparison.Ordinal)
                || Path.IsPathRooted(normalized))
            {
                throw new InvalidOperationException(fieldName + " must be session-relative.");
            }

            string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                throw new InvalidOperationException(fieldName + " must include at least one path segment.");

            for (int i = 0; i < segments.Length; i++)
            {
                if (string.Equals(segments[i], ".", StringComparison.Ordinal)
                    || string.Equals(segments[i], "..", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(fieldName + " must not include relative traversal segments.");
                }

                ValidatePortablePathSegment(segments[i], fieldName + " segment");
            }

            return string.Join("/", segments);
        }

        internal static string NormalizePortableIdentifier(string rawValue, string fieldName)
        {
            string normalized = RequireNonBlank(rawValue, fieldName);
            var builder = new StringBuilder(normalized.Length);
            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                    builder.Append(c);
                else
                    builder.Append('-');
            }

            string compact = builder.ToString().Trim('-');
            while (compact.IndexOf("--", StringComparison.Ordinal) >= 0)
                compact = compact.Replace("--", "-", StringComparison.Ordinal);

            compact = RequireNonBlank(compact, fieldName + " normalized");
            ValidatePortablePathSegment(compact, fieldName + " normalized");
            return compact;
        }

        internal static void ValidatePortablePathSegment(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(fieldName + " must not be blank.");

            string normalized = value.Trim();
            if (normalized.EndsWith(".", StringComparison.Ordinal) || normalized.EndsWith(" ", StringComparison.Ordinal))
                throw new InvalidOperationException(fieldName + " must not end with '.' or space.");

            if (s_windowsReservedFileNames.Contains(normalized))
                throw new InvalidOperationException(fieldName + " must not use a reserved Windows device name.");

            for (int i = 0; i < normalized.Length; i++)
            {
                char c = normalized[i];
                if (c > 127)
                    throw new InvalidOperationException(fieldName + " must be ASCII-only for cross-platform portability.");

                if (c == '<' || c == '>' || c == ':' || c == '"' || c == '/' || c == '\\' || c == '|' || c == '?' || c == '*')
                    throw new InvalidOperationException(fieldName + " contains a Windows-reserved file-name character.");
            }
        }

        private static string RequireNonBlank(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(fieldName + " must not be blank.");

            return value.Trim();
        }
    }

    internal static class ASMLiteSmokeAtomicFileIo
    {
        private static readonly UTF8Encoding s_utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        internal static void WriteJsonAtomically(string targetPath, string jsonContent)
        {
            WriteJsonAtomically(targetPath, jsonContent, beforePromote: null);
        }

        internal static void WriteJsonAtomically(string targetPath, string jsonContent, Action<string> beforePromote)
        {
            if (string.IsNullOrWhiteSpace(targetPath))
                throw new InvalidOperationException("targetPath must not be blank.");
            if (string.IsNullOrWhiteSpace(jsonContent))
                throw new InvalidOperationException("jsonContent must not be blank.");

            string fullTargetPath = Path.GetFullPath(targetPath.Trim());
            string directory = Path.GetDirectoryName(fullTargetPath);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("targetPath must include a parent directory.");

            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(
                directory,
                Path.GetFileName(fullTargetPath) + ".tmp-" + Guid.NewGuid().ToString("N"));

            try
            {
                byte[] payload = s_utf8WithoutBom.GetBytes(jsonContent);
                using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(payload, 0, payload.Length);
                    stream.Flush(flushToDisk: true);
                }

                beforePromote?.Invoke(tempPath);

                PromoteTempFileWithRetry(tempPath, fullTargetPath);
            }
            finally
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
        }

        private static void PromoteTempFileWithRetry(string tempPath, string fullTargetPath)
        {
            const int maxAttempts = 8;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    if (File.Exists(fullTargetPath))
                        File.Replace(tempPath, fullTargetPath, null);
                    else
                        File.Move(tempPath, fullTargetPath);

                    return;
                }
                catch (Exception ex) when (IsTransientPromoteException(ex) && attempt < maxAttempts - 1)
                {
                    Thread.Sleep(GetPromoteRetryDelayMilliseconds(attempt));
                }
            }
        }

        private static bool IsTransientPromoteException(Exception exception)
        {
            if (exception == null)
                return false;

            const int sharingViolation = unchecked((int)0x80070020);
            const int lockViolation = unchecked((int)0x80070021);
            int hResult = exception.HResult;
            if (hResult == sharingViolation || hResult == lockViolation)
                return true;

            string message = exception.Message ?? string.Empty;
            return message.IndexOf("Unable to remove the file to be replaced", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int GetPromoteRetryDelayMilliseconds(int attempt)
        {
            int boundedAttempt = Math.Max(0, Math.Min(6, attempt));
            return 25 * (boundedAttempt + 1);
        }
    }

    internal static class ASMLiteSmokeArtifactPaths
    {
        private const string OverlayRootRelativePath = "artifacts/smoke-overlay";

        internal static string GetOverlayRootPath()
        {
            return Path.Combine(ASMLiteSmokeContractPaths.GetRepositoryRootPath(), OverlayRootRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static ASMLiteSmokeSessionPaths CreateSessionPaths(string sessionId)
        {
            string normalizedSessionId = ASMLiteSmokeSessionPaths.NormalizePortableIdentifier(sessionId, nameof(sessionId));
            string sessionRoot = Path.Combine(GetOverlayRootPath(), "session-" + normalizedSessionId);
            return new ASMLiteSmokeSessionPaths(sessionRoot);
        }

        internal static ASMLiteSmokeSessionPaths FromSessionRoot(string sessionRootPath)
        {
            return new ASMLiteSmokeSessionPaths(sessionRootPath);
        }

        internal static void WriteCatalogSnapshotAtomically(string catalogSnapshotPath, string catalogSnapshotJson)
        {
            if (string.IsNullOrWhiteSpace(catalogSnapshotJson))
                throw new InvalidOperationException("catalogSnapshotJson must not be blank.");

            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(catalogSnapshotPath, catalogSnapshotJson);
        }

        internal static ASMLiteSmokeRunResultDocument LoadResultFixture(string fileName)
        {
            string fixturePath = Path.Combine(ASMLiteSmokeContractPaths.GetProtocolFixtureDirectory(), fileName ?? string.Empty);
            string rawJson = File.ReadAllText(fixturePath, Encoding.UTF8);
            return LoadResultFromJson(rawJson);
        }

        internal static ASMLiteSmokeRunResultDocument LoadResultFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException("Smoke run result JSON is required.");

            var document = JsonUtility.FromJson<ASMLiteSmokeRunResultDocument>(rawJson);
            if (document == null)
                throw new InvalidOperationException("Smoke run result JSON did not deserialize.");

            NormalizeAndValidateResultDocument(document);
            return document;
        }

        internal static ASMLiteSmokeFailureDocument LoadFailureFixture(string fileName)
        {
            string fixturePath = Path.Combine(ASMLiteSmokeContractPaths.GetProtocolFixtureDirectory(), fileName ?? string.Empty);
            string rawJson = File.ReadAllText(fixturePath, Encoding.UTF8);
            return LoadFailureFromJson(rawJson);
        }

        internal static ASMLiteSmokeFailureDocument LoadFailureFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException("Smoke failure JSON is required.");

            var document = JsonUtility.FromJson<ASMLiteSmokeFailureDocument>(rawJson);
            if (document == null)
                throw new InvalidOperationException("Smoke failure JSON did not deserialize.");

            NormalizeAndValidateFailureDocument(document);
            return document;
        }

        internal static void NormalizeAndValidateResultDocument(ASMLiteSmokeRunResultDocument document)
        {
            if (document == null)
                throw new InvalidOperationException("Smoke run result document is required.");

            document.protocolVersion = RequireNonBlank(document.protocolVersion, "protocolVersion");
            document.sessionId = RequireNonBlank(document.sessionId, "sessionId");
            document.runId = RequireNonBlank(document.runId, "runId");
            document.suiteId = RequireNonBlank(document.suiteId, "suiteId");
            document.suiteLabel = RequireNonBlank(document.suiteLabel, "suiteLabel");
            document.groupId = RequireNonBlank(document.groupId, "groupId");
            document.groupLabel = RequireNonBlank(document.groupLabel, "groupLabel");
            document.result = RequireNonBlank(document.result, "result");
            document.startedAtUtc = RequireNonBlank(document.startedAtUtc, "startedAtUtc");
            document.endedAtUtc = RequireNonBlank(document.endedAtUtc, "endedAtUtc");
            if (document.durationSeconds < 0d)
                throw new InvalidOperationException("durationSeconds must be zero or greater.");
            document.effectiveResetPolicy = RequireNonBlank(document.effectiveResetPolicy, "effectiveResetPolicy");
            if (document.firstEventSeq <= 0)
                throw new InvalidOperationException("firstEventSeq must be greater than zero.");
            if (document.lastEventSeq < document.firstEventSeq)
                throw new InvalidOperationException("lastEventSeq must be greater than or equal to firstEventSeq.");
            document.catalogSnapshotPath = ASMLiteSmokeSessionPaths.NormalizeRelativeArtifactPath(document.catalogSnapshotPath, "catalogSnapshotPath");
            NormalizeAndValidateArtifactPaths(document.artifactPaths);
        }

        internal static void WriteResultDocumentAtomically(string resultPath, ASMLiteSmokeRunResultDocument document, bool prettyPrint)
        {
            NormalizeAndValidateResultDocument(document ?? throw new InvalidOperationException("Smoke run result document is required."));
            string json = JsonUtility.ToJson(document, prettyPrint);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(resultPath, json);
        }

        internal static void NormalizeAndValidateFailureDocument(ASMLiteSmokeFailureDocument document)
        {
            if (document == null)
                throw new InvalidOperationException("Smoke failure document is required.");

            document.protocolVersion = RequireNonBlank(document.protocolVersion, "protocolVersion");
            document.sessionId = RequireNonBlank(document.sessionId, "sessionId");
            document.runId = RequireNonBlank(document.runId, "runId");
            document.suiteId = RequireNonBlank(document.suiteId, "suiteId");
            document.suiteLabel = RequireNonBlank(document.suiteLabel, "suiteLabel");
            document.caseId = RequireNonBlank(document.caseId, "caseId");
            document.caseLabel = RequireNonBlank(document.caseLabel, "caseLabel");
            document.stepId = RequireNonBlank(document.stepId, "stepId");
            document.stepLabel = RequireNonBlank(document.stepLabel, "stepLabel");
            document.failureMessage = RequireNonBlank(document.failureMessage, "failureMessage");
            document.stackTrace = RequireNonBlank(document.stackTrace, "stackTrace");
            document.effectiveResetPolicy = RequireNonBlank(document.effectiveResetPolicy, "effectiveResetPolicy");
            document.scenePath = RequireNonBlank(document.scenePath, "scenePath");
            document.avatarName = RequireNonBlank(document.avatarName, "avatarName");
            document.commandId = RequireNonBlank(document.commandId, "commandId");
            document.eventSeqRange = document.eventSeqRange ?? throw new InvalidOperationException("eventSeqRange is required.");
            if (document.eventSeqRange.first <= 0)
                throw new InvalidOperationException("eventSeqRange.first must be greater than zero.");
            if (document.eventSeqRange.last < document.eventSeqRange.first)
                throw new InvalidOperationException("eventSeqRange.last must be greater than or equal to eventSeqRange.first.");
            document.debugHint = RequireNonBlank(document.debugHint, "debugHint");
            document.timestampUtc = RequireNonBlank(document.timestampUtc, "timestampUtc");
            document.lastEvents = (document.lastEvents ?? Array.Empty<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray();
            NormalizeAndValidateArtifactPaths(document.artifactPaths);
        }

        internal static void WriteFailureDocumentAtomically(string failurePath, ASMLiteSmokeFailureDocument document, bool prettyPrint)
        {
            NormalizeAndValidateFailureDocument(document ?? throw new InvalidOperationException("Smoke failure document is required."));
            string json = JsonUtility.ToJson(document, prettyPrint);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(failurePath, json);
        }

        private static void NormalizeAndValidateArtifactPaths(ASMLiteSmokeArtifactReferences artifactPaths)
        {
            if (artifactPaths == null)
                throw new InvalidOperationException("artifactPaths is required.");

            artifactPaths.resultPath = ASMLiteSmokeSessionPaths.NormalizeRelativeArtifactPath(artifactPaths.resultPath, "artifactPaths.resultPath");
            artifactPaths.failurePath = NormalizeOptionalRelativeArtifactPath(artifactPaths.failurePath, "artifactPaths.failurePath");
            artifactPaths.eventsSlicePath = ASMLiteSmokeSessionPaths.NormalizeRelativeArtifactPath(artifactPaths.eventsSlicePath, "artifactPaths.eventsSlicePath");
            artifactPaths.nunitPath = ASMLiteSmokeSessionPaths.NormalizeRelativeArtifactPath(artifactPaths.nunitPath, "artifactPaths.nunitPath");
            artifactPaths.debugSummaryPath = NormalizeOptionalRelativeArtifactPath(artifactPaths.debugSummaryPath, "artifactPaths.debugSummaryPath");
        }

        private static string NormalizeOptionalRelativeArtifactPath(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return ASMLiteSmokeSessionPaths.NormalizeRelativeArtifactPath(value, fieldName);
        }

        private static string RequireNonBlank(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(fieldName + " must not be blank.");

            return value.Trim();
        }
    }
}
