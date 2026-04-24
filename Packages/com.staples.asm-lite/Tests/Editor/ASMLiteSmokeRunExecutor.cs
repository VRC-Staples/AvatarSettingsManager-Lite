using System;
using System.Collections.Generic;

namespace ASMLite.Tests.Editor
{
    internal sealed class ASMLiteSmokeExecutionEventPayload
    {
        internal string EventType;
        internal string Message;
        internal string GroupId;
        internal string SuiteId;
        internal string CaseId;
        internal string StepId;
        internal string EffectiveResetPolicy;
    }

    internal sealed class ASMLiteSmokeExecutionFailure
    {
        internal string CaseId;
        internal string CaseLabel;
        internal string StepId;
        internal string StepLabel;
        internal string FailureMessage;
        internal string StackTrace;
    }

    internal sealed class ASMLiteSmokeSuiteExecutionResult
    {
        internal string RunId;
        internal string GroupId;
        internal string SuiteId;
        internal string SuiteLabel;
        internal string EffectiveResetPolicy;
        internal bool Succeeded;
        internal ASMLiteSmokeExecutionFailure Failure;
        internal List<ASMLiteSmokeExecutionEventPayload> Events = new List<ASMLiteSmokeExecutionEventPayload>();
    }

    internal static class ASMLiteSmokeRunExecutor
    {
        internal delegate bool StepExecutor(
            ASMLiteSmokeStepDefinition step,
            out string message,
            out string stackTrace);

        internal static ASMLiteSmokeSuiteExecutionResult Execute(
            ASMLiteSmokeCatalogDocument catalog,
            ASMLiteSmokeProtocolCommand command,
            string effectiveResetPolicy,
            StepExecutor stepExecutor)
        {
            if (catalog == null)
                throw new InvalidOperationException("Catalog document is required.");
            if (command == null)
                throw new InvalidOperationException("run-suite command is required.");
            if (command.runSuite == null)
                throw new InvalidOperationException("runSuite payload is required.");
            if (stepExecutor == null)
                throw new InvalidOperationException("Step executor callback is required.");

            if (!catalog.TryGetSuite(command.runSuite.suiteId, out ASMLiteSmokeSuiteDefinition suite) || suite == null)
                throw new InvalidOperationException($"Suite '{command.runSuite.suiteId}' does not exist in the canonical catalog.");

            string runId = $"run-{command.commandSeq:D4}-{ASMLiteSmokeSessionPaths.NormalizePortableIdentifier(suite.suiteId, nameof(suite.suiteId))}";
            var result = new ASMLiteSmokeSuiteExecutionResult
            {
                RunId = runId,
                GroupId = FindGroupId(catalog, suite.suiteId),
                SuiteId = suite.suiteId,
                SuiteLabel = suite.label,
                EffectiveResetPolicy = effectiveResetPolicy,
                Succeeded = false,
            };

            AddEvent(result, "suite-started", suite.suiteId, string.Empty, string.Empty, effectiveResetPolicy, $"Suite '{suite.suiteId}' started.");

            ASMLiteSmokeCaseDefinition[] cases = suite.cases ?? Array.Empty<ASMLiteSmokeCaseDefinition>();
            for (int caseIndex = 0; caseIndex < cases.Length; caseIndex++)
            {
                ASMLiteSmokeCaseDefinition suiteCase = cases[caseIndex];
                if (suiteCase == null)
                    continue;

                AddEvent(result, "case-started", suite.suiteId, suiteCase.caseId, string.Empty, effectiveResetPolicy, $"Case '{suiteCase.caseId}' started.");

                ASMLiteSmokeStepDefinition[] steps = suiteCase.steps ?? Array.Empty<ASMLiteSmokeStepDefinition>();
                for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
                {
                    ASMLiteSmokeStepDefinition step = steps[stepIndex];
                    if (step == null)
                        continue;

                    AddEvent(result, "step-started", suite.suiteId, suiteCase.caseId, step.stepId, effectiveResetPolicy, $"Step '{step.stepId}' started ({step.actionType}).");

                    bool stepPassed = stepExecutor(step, out string detail, out string stackTrace);
                    if (stepPassed)
                    {
                        string stepPassedMessage = string.IsNullOrWhiteSpace(detail)
                            ? $"Step '{step.stepId}' passed."
                            : detail.Trim();
                        AddEvent(result, "step-passed", suite.suiteId, suiteCase.caseId, step.stepId, effectiveResetPolicy, stepPassedMessage);
                        continue;
                    }

                    string failureMessage = string.IsNullOrWhiteSpace(detail)
                        ? $"Step '{step.stepId}' failed."
                        : detail.Trim();

                    AddEvent(result, "step-failed", suite.suiteId, suiteCase.caseId, step.stepId, effectiveResetPolicy, failureMessage);
                    AddEvent(result, "suite-failed", suite.suiteId, suiteCase.caseId, step.stepId, effectiveResetPolicy, $"Suite '{suite.suiteId}' failed at step '{step.stepId}'.");

                    result.Failure = new ASMLiteSmokeExecutionFailure
                    {
                        CaseId = suiteCase.caseId,
                        CaseLabel = suiteCase.label,
                        StepId = step.stepId,
                        StepLabel = step.label,
                        FailureMessage = failureMessage,
                        StackTrace = string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace.Trim(),
                    };

                    result.Succeeded = false;
                    return result;
                }
            }

            AddEvent(result, "suite-passed", suite.suiteId, string.Empty, string.Empty, effectiveResetPolicy, $"Suite '{suite.suiteId}' passed.");
            result.Succeeded = true;
            return result;
        }

        private static string FindGroupId(ASMLiteSmokeCatalogDocument catalog, string suiteId)
        {
            ASMLiteSmokeGroupDefinition[] groups = catalog.groups ?? Array.Empty<ASMLiteSmokeGroupDefinition>();
            for (int i = 0; i < groups.Length; i++)
            {
                ASMLiteSmokeGroupDefinition group = groups[i];
                if (group == null || group.suites == null)
                    continue;

                for (int j = 0; j < group.suites.Length; j++)
                {
                    ASMLiteSmokeSuiteDefinition suite = group.suites[j];
                    if (suite != null && string.Equals(suite.suiteId, suiteId, StringComparison.Ordinal))
                        return group.groupId;
                }
            }

            return string.Empty;
        }

        private static void AddEvent(
            ASMLiteSmokeSuiteExecutionResult result,
            string eventType,
            string suiteId,
            string caseId,
            string stepId,
            string effectiveResetPolicy,
            string message)
        {
            result.Events.Add(new ASMLiteSmokeExecutionEventPayload
            {
                EventType = eventType,
                Message = message,
                GroupId = result.GroupId,
                SuiteId = suiteId,
                CaseId = caseId,
                StepId = stepId,
                EffectiveResetPolicy = effectiveResetPolicy,
            });
        }
    }
}
