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

    internal static class ASMLiteSmokeExpectedDiagnosticMatcher
    {
        internal static bool ExpectsStepFailure(ASMLiteSmokeStepDefinition step)
        {
            return step != null && step.args != null && step.args.expectStepFailure;
        }

        internal static bool MatchesExpectedDiagnostic(
            ASMLiteSmokeStepDefinition step,
            string detail,
            out string passMessage,
            out string mismatchMessage)
        {
            ASMLiteSmokeStepArgs args = step == null ? null : step.args;
            string diagnosticCode = args == null ? string.Empty : Normalize(args.expectedDiagnosticCode);
            string diagnosticContains = args == null ? string.Empty : Normalize(args.expectedDiagnosticContains);
            string actual = Normalize(detail);

            bool hasCode = !string.IsNullOrEmpty(diagnosticCode)
                && actual.IndexOf(diagnosticCode, StringComparison.Ordinal) >= 0;
            bool hasText = !string.IsNullOrEmpty(diagnosticContains)
                && actual.IndexOf(diagnosticContains, StringComparison.Ordinal) >= 0;

            if (hasCode && hasText)
            {
                passMessage = $"Expected diagnostic '{diagnosticCode}' matched for step '{step.stepId}'.";
                mismatchMessage = string.Empty;
                return true;
            }

            passMessage = string.Empty;
            mismatchMessage = $"Expected diagnostic code '{diagnosticCode}' containing '{diagnosticContains}', but observed: {actual}";
            return false;
        }

        internal static string BuildUnexpectedSuccessMessage(ASMLiteSmokeStepDefinition step)
        {
            ASMLiteSmokeStepArgs args = step == null ? null : step.args;
            string diagnosticCode = args == null ? string.Empty : Normalize(args.expectedDiagnosticCode);
            string diagnosticContains = args == null ? string.Empty : Normalize(args.expectedDiagnosticContains);
            string stepId = step == null ? "unknown-step" : step.stepId;
            return $"Step '{stepId}' was expected to fail with diagnostic code '{diagnosticCode}' containing '{diagnosticContains}', but it passed.";
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

}
