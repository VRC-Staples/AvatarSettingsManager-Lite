using System;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteSmokeFailureReport
    {
        internal static ASMLiteSmokeFailureDocument Build(
            string sessionId,
            string commandId,
            ASMLiteSmokeSuiteExecutionResult result,
            string scenePath,
            string avatarName,
            ASMLiteSmokeArtifactReferences artifactPaths,
            int firstEventSeq,
            int lastEventSeq,
            string timestampUtc,
            string[] lastEvents)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            ASMLiteSmokeExecutionFailure failure = result.Failure ?? new ASMLiteSmokeExecutionFailure
            {
                CaseId = "unknown-case",
                CaseLabel = "Unknown case",
                StepId = "unknown-step",
                StepLabel = "Unknown step",
                FailureMessage = "Suite failed without detailed failure payload.",
                StackTrace = "(none)"
            };

            string normalizedTimestamp = string.IsNullOrWhiteSpace(timestampUtc)
                ? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                : timestampUtc.Trim();

            return new ASMLiteSmokeFailureDocument
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = sessionId,
                runId = result.RunId,
                commandId = commandId,
                suiteId = string.IsNullOrWhiteSpace(result.SuiteId) ? "unknown-suite" : result.SuiteId,
                suiteLabel = string.IsNullOrWhiteSpace(result.SuiteId) ? "Unknown suite" : result.SuiteId,
                caseId = string.IsNullOrWhiteSpace(failure.CaseId) ? "unknown-case" : failure.CaseId,
                caseLabel = string.IsNullOrWhiteSpace(failure.CaseLabel) ? "Unknown case" : failure.CaseLabel,
                stepId = string.IsNullOrWhiteSpace(failure.StepId) ? "unknown-step" : failure.StepId,
                stepLabel = string.IsNullOrWhiteSpace(failure.StepLabel) ? "Unknown step" : failure.StepLabel,
                effectiveResetPolicy = string.IsNullOrWhiteSpace(result.EffectiveResetPolicy)
                    ? "SceneReload"
                    : result.EffectiveResetPolicy,
                scenePath = string.IsNullOrWhiteSpace(scenePath) ? "unknown-scene" : scenePath,
                avatarName = string.IsNullOrWhiteSpace(avatarName) ? "unknown-avatar" : avatarName,
                failureMessage = string.IsNullOrWhiteSpace(failure.FailureMessage)
                    ? "Suite failed without failure message."
                    : failure.FailureMessage,
                stackTrace = string.IsNullOrWhiteSpace(failure.StackTrace) ? "(none)" : failure.StackTrace,
                eventSeqRange = new ASMLiteSmokeFailureEventSeqRange
                {
                    first = firstEventSeq,
                    last = lastEventSeq
                },
                lastEvents = lastEvents ?? Array.Empty<string>(),
                debugHint = string.IsNullOrWhiteSpace(failure.StepLabel)
                    ? "Inspect events.slice.ndjson and nunit.xml for step details."
                    : $"Inspect step '{failure.StepLabel}' and corresponding events for root cause.",
                artifactPaths = artifactPaths,
                timestampUtc = normalizedTimestamp
            };
        }
    }
}
