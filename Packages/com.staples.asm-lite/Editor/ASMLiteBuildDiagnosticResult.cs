using System;

namespace ASMLite.Editor
{
    internal sealed class ASMLiteBuildDiagnosticResult
    {
        internal ASMLiteBuildDiagnosticResult(
            bool success,
            string code,
            string message,
            string contextPath,
            string remediation,
            ASMLiteBuildDiagnosticResult innerDiagnostic = null)
        {
            Success = success;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            ContextPath = contextPath ?? string.Empty;
            Remediation = remediation ?? string.Empty;
            InnerDiagnostic = innerDiagnostic;
        }

        internal bool Success { get; }
        internal string Code { get; }
        internal string Message { get; }
        internal string ContextPath { get; }
        internal string Remediation { get; }
        internal ASMLiteBuildDiagnosticResult InnerDiagnostic { get; }

        internal static ASMLiteBuildDiagnosticResult Pass()
        {
            return new ASMLiteBuildDiagnosticResult(
                success: true,
                code: string.Empty,
                message: string.Empty,
                contextPath: string.Empty,
                remediation: string.Empty,
                innerDiagnostic: null);
        }

        internal static ASMLiteBuildDiagnosticResult Fail(
            string code,
            string contextPath,
            string remediation,
            string message = null,
            ASMLiteBuildDiagnosticResult innerDiagnostic = null)
        {
            return new ASMLiteBuildDiagnosticResult(
                success: false,
                code: code,
                message: string.IsNullOrWhiteSpace(message) ? ASMLiteDiagnosticCodes.GetMessage(code) : message,
                contextPath: contextPath,
                remediation: remediation,
                innerDiagnostic: innerDiagnostic);
        }

        internal string ToLogString()
        {
            if (Success)
                return "[ASM-Lite] Build diagnostic: success.";

            var log = $"[ASM-Lite] {Code}: {Message}";

            if (!string.IsNullOrWhiteSpace(ContextPath))
                log += $" Context: '{ContextPath}'.";

            if (!string.IsNullOrWhiteSpace(Remediation))
                log += $" Remediation: {Remediation}";

            if (InnerDiagnostic != null && !InnerDiagnostic.Success)
            {
                log += $" Inner: {InnerDiagnostic.Code}";
                if (!string.IsNullOrWhiteSpace(InnerDiagnostic.ContextPath))
                    log += $" ({InnerDiagnostic.ContextPath})";
                log += ".";

                if (!string.IsNullOrWhiteSpace(InnerDiagnostic.Remediation))
                    log += $" Inner Remediation: {InnerDiagnostic.Remediation}";
            }

            return log;
        }
    }
}
