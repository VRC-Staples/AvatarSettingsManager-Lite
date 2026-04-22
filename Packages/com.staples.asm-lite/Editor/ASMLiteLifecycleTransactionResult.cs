using System;

namespace ASMLite.Editor
{
    internal enum ASMLiteLifecycleOperation
    {
        None = 0,
        AttachedVendorize = 1,
        AttachedReturnToPackageManaged = 2,
    }

    internal enum ASMLiteLifecycleTransactionStage
    {
        None = 0,
        Preflight = 1,
        Execute = 2,
        Verify = 3,
        Rollback = 4,
    }

    internal sealed class ASMLiteLifecycleTransactionResult
    {
        internal ASMLiteLifecycleTransactionResult(
            bool success,
            ASMLiteLifecycleOperation operation,
            ASMLiteLifecycleTransactionStage failedStage,
            bool rollbackAttempted,
            bool rollbackSucceeded,
            ASMLiteWindow.AsmLiteToolState beforeState,
            ASMLiteWindow.AsmLiteToolState afterState,
            ASMLiteWindow.AsmLiteToolState rollbackState,
            int discoveredParamCount,
            string message,
            string contextPath,
            string remediation,
            ASMLiteBuildDiagnosticResult diagnostic,
            ASMLiteGeneratedAssetMirrorResult mirrorResult)
        {
            Success = success;
            Operation = operation;
            FailedStage = failedStage;
            RollbackAttempted = rollbackAttempted;
            RollbackSucceeded = rollbackSucceeded;
            BeforeState = beforeState;
            AfterState = afterState;
            RollbackState = rollbackState;
            DiscoveredParamCount = discoveredParamCount;
            Message = message ?? string.Empty;
            ContextPath = contextPath ?? string.Empty;
            Remediation = remediation ?? string.Empty;
            Diagnostic = diagnostic;
            MirrorResult = mirrorResult;
        }

        internal bool Success { get; }
        internal ASMLiteLifecycleOperation Operation { get; }
        internal ASMLiteLifecycleTransactionStage FailedStage { get; }
        internal bool RollbackAttempted { get; }
        internal bool RollbackSucceeded { get; }
        internal ASMLiteWindow.AsmLiteToolState BeforeState { get; }
        internal ASMLiteWindow.AsmLiteToolState AfterState { get; }
        internal ASMLiteWindow.AsmLiteToolState RollbackState { get; }
        internal int DiscoveredParamCount { get; }
        internal string Message { get; }
        internal string ContextPath { get; }
        internal string Remediation { get; }
        internal ASMLiteBuildDiagnosticResult Diagnostic { get; }
        internal ASMLiteGeneratedAssetMirrorResult MirrorResult { get; }

        internal static ASMLiteLifecycleTransactionResult Pass(
            ASMLiteLifecycleOperation operation,
            ASMLiteWindow.AsmLiteToolState beforeState,
            ASMLiteWindow.AsmLiteToolState afterState,
            int discoveredParamCount,
            string message,
            ASMLiteGeneratedAssetMirrorResult mirrorResult = null)
        {
            return new ASMLiteLifecycleTransactionResult(
                success: true,
                operation: operation,
                failedStage: ASMLiteLifecycleTransactionStage.None,
                rollbackAttempted: false,
                rollbackSucceeded: false,
                beforeState: beforeState,
                afterState: afterState,
                rollbackState: afterState,
                discoveredParamCount: discoveredParamCount,
                message: message,
                contextPath: string.Empty,
                remediation: string.Empty,
                diagnostic: ASMLiteBuildDiagnosticResult.Pass(),
                mirrorResult: mirrorResult);
        }

        internal static ASMLiteLifecycleTransactionResult Fail(
            ASMLiteLifecycleOperation operation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteWindow.AsmLiteToolState beforeState,
            ASMLiteWindow.AsmLiteToolState afterState,
            ASMLiteWindow.AsmLiteToolState rollbackState,
            bool rollbackAttempted,
            bool rollbackSucceeded,
            string contextPath,
            string remediation,
            string message,
            ASMLiteBuildDiagnosticResult diagnostic = null,
            ASMLiteGeneratedAssetMirrorResult mirrorResult = null,
            int discoveredParamCount = -1)
        {
            return new ASMLiteLifecycleTransactionResult(
                success: false,
                operation: operation,
                failedStage: failedStage,
                rollbackAttempted: rollbackAttempted,
                rollbackSucceeded: rollbackSucceeded,
                beforeState: beforeState,
                afterState: afterState,
                rollbackState: rollbackState,
                discoveredParamCount: discoveredParamCount,
                message: message,
                contextPath: contextPath,
                remediation: remediation,
                diagnostic: diagnostic ?? ASMLiteBuildDiagnosticResult.Fail(
                    code: string.Empty,
                    contextPath: contextPath,
                    remediation: remediation,
                    message: message),
                mirrorResult: mirrorResult);
        }

        internal string ToLogString()
        {
            if (Success)
                return Message;

            string log = Message;
            if (!string.IsNullOrWhiteSpace(ContextPath))
                log += $" Context: '{ContextPath}'.";
            if (!string.IsNullOrWhiteSpace(Remediation))
                log += $" Remediation: {Remediation}";
            if (MirrorResult != null && !MirrorResult.Success)
                log += $" Mirror: {MirrorResult.ToLogString()}";
            if (Diagnostic != null && !Diagnostic.Success && !string.IsNullOrWhiteSpace(Diagnostic.Message))
                log += $" Diagnostic: {Diagnostic.ToLogString()}";

            return log;
        }
    }
}
