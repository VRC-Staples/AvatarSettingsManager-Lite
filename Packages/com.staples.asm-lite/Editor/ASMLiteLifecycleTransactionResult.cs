using System;

namespace ASMLite.Editor
{
    internal enum ASMLiteLifecycleOperation
    {
        None = 0,
        AttachedVendorize = 1,
        AttachedReturnToPackageManaged = 2,
        DetachToDirectDelivery = 3,
        VendorizeAndDetach = 4,
        DetachedReturnToPackageManagedRecovery = 5,
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
            ASMLiteWindow.AsmLiteToolState recoveredState,
            int discoveredParamCount,
            string message,
            string contextPath,
            string remediation,
            ASMLiteBuildDiagnosticResult diagnostic,
            ASMLiteGeneratedAssetMirrorResult mirrorResult,
            ASMLiteMigrationOutcomeReport? migrationOutcomeReport,
            bool cleanupAttempted,
            bool cleanupSucceeded,
            bool reattachAttempted,
            bool reattachSucceeded,
            bool installPathAdoptionAttempted,
            bool installPathAdoptionSucceeded)
        {
            Success = success;
            Operation = operation;
            FailedStage = failedStage;
            RollbackAttempted = rollbackAttempted;
            RollbackSucceeded = rollbackSucceeded;
            BeforeState = beforeState;
            AfterState = afterState;
            RollbackState = rollbackState;
            RecoveredState = recoveredState;
            DiscoveredParamCount = discoveredParamCount;
            Message = message ?? string.Empty;
            ContextPath = contextPath ?? string.Empty;
            Remediation = remediation ?? string.Empty;
            Diagnostic = diagnostic;
            MirrorResult = mirrorResult;
            MigrationOutcomeReport = migrationOutcomeReport;
            CleanupAttempted = cleanupAttempted;
            CleanupSucceeded = cleanupSucceeded;
            ReattachAttempted = reattachAttempted;
            ReattachSucceeded = reattachSucceeded;
            InstallPathAdoptionAttempted = installPathAdoptionAttempted;
            InstallPathAdoptionSucceeded = installPathAdoptionSucceeded;
        }

        internal bool Success { get; }
        internal ASMLiteLifecycleOperation Operation { get; }
        internal ASMLiteLifecycleTransactionStage FailedStage { get; }
        internal bool RollbackAttempted { get; }
        internal bool RollbackSucceeded { get; }
        internal ASMLiteWindow.AsmLiteToolState BeforeState { get; }
        internal ASMLiteWindow.AsmLiteToolState AfterState { get; }
        internal ASMLiteWindow.AsmLiteToolState RollbackState { get; }
        internal ASMLiteWindow.AsmLiteToolState RecoveredState { get; }
        internal int DiscoveredParamCount { get; }
        internal string Message { get; }
        internal string ContextPath { get; }
        internal string Remediation { get; }
        internal ASMLiteBuildDiagnosticResult Diagnostic { get; }
        internal ASMLiteGeneratedAssetMirrorResult MirrorResult { get; }
        internal ASMLiteMigrationOutcomeReport? MigrationOutcomeReport { get; }
        internal bool CleanupAttempted { get; }
        internal bool CleanupSucceeded { get; }
        internal bool ReattachAttempted { get; }
        internal bool ReattachSucceeded { get; }
        internal bool InstallPathAdoptionAttempted { get; }
        internal bool InstallPathAdoptionSucceeded { get; }

        internal static ASMLiteLifecycleTransactionResult Pass(
            ASMLiteLifecycleOperation operation,
            ASMLiteWindow.AsmLiteToolState beforeState,
            ASMLiteWindow.AsmLiteToolState afterState,
            int discoveredParamCount,
            string message,
            ASMLiteGeneratedAssetMirrorResult mirrorResult = null,
            ASMLiteMigrationOutcomeReport? migrationOutcomeReport = null,
            bool cleanupAttempted = false,
            bool cleanupSucceeded = false,
            bool reattachAttempted = false,
            bool reattachSucceeded = false,
            bool installPathAdoptionAttempted = false,
            bool installPathAdoptionSucceeded = false,
            ASMLiteWindow.AsmLiteToolState? recoveredState = null)
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
                recoveredState: recoveredState ?? afterState,
                discoveredParamCount: discoveredParamCount,
                message: message,
                contextPath: string.Empty,
                remediation: string.Empty,
                diagnostic: ASMLiteBuildDiagnosticResult.Pass(),
                mirrorResult: mirrorResult,
                migrationOutcomeReport: migrationOutcomeReport,
                cleanupAttempted: cleanupAttempted,
                cleanupSucceeded: cleanupSucceeded,
                reattachAttempted: reattachAttempted,
                reattachSucceeded: reattachSucceeded,
                installPathAdoptionAttempted: installPathAdoptionAttempted,
                installPathAdoptionSucceeded: installPathAdoptionSucceeded);
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
            int discoveredParamCount = -1,
            ASMLiteMigrationOutcomeReport? migrationOutcomeReport = null,
            bool cleanupAttempted = false,
            bool cleanupSucceeded = false,
            bool reattachAttempted = false,
            bool reattachSucceeded = false,
            bool installPathAdoptionAttempted = false,
            bool installPathAdoptionSucceeded = false,
            ASMLiteWindow.AsmLiteToolState? recoveredState = null)
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
                recoveredState: recoveredState ?? rollbackState,
                discoveredParamCount: discoveredParamCount,
                message: message,
                contextPath: contextPath,
                remediation: remediation,
                diagnostic: diagnostic ?? ASMLiteBuildDiagnosticResult.Fail(
                    code: string.Empty,
                    contextPath: contextPath,
                    remediation: remediation,
                    message: message),
                mirrorResult: mirrorResult,
                migrationOutcomeReport: migrationOutcomeReport,
                cleanupAttempted: cleanupAttempted,
                cleanupSucceeded: cleanupSucceeded,
                reattachAttempted: reattachAttempted,
                reattachSucceeded: reattachSucceeded,
                installPathAdoptionAttempted: installPathAdoptionAttempted,
                installPathAdoptionSucceeded: installPathAdoptionSucceeded);
        }

        internal ASMLiteLifecycleTransactionResult WithOperation(ASMLiteLifecycleOperation operation, string message = null)
        {
            return new ASMLiteLifecycleTransactionResult(
                success: Success,
                operation: operation,
                failedStage: FailedStage,
                rollbackAttempted: RollbackAttempted,
                rollbackSucceeded: RollbackSucceeded,
                beforeState: BeforeState,
                afterState: AfterState,
                rollbackState: RollbackState,
                recoveredState: RecoveredState,
                discoveredParamCount: DiscoveredParamCount,
                message: message ?? Message,
                contextPath: ContextPath,
                remediation: Remediation,
                diagnostic: Diagnostic,
                mirrorResult: MirrorResult,
                migrationOutcomeReport: MigrationOutcomeReport,
                cleanupAttempted: CleanupAttempted,
                cleanupSucceeded: CleanupSucceeded,
                reattachAttempted: ReattachAttempted,
                reattachSucceeded: ReattachSucceeded,
                installPathAdoptionAttempted: InstallPathAdoptionAttempted,
                installPathAdoptionSucceeded: InstallPathAdoptionSucceeded);
        }

        internal string ToLogString()
        {
            if (Success)
            {
                string successLog = Message;
                AppendRecoveryDetails(ref successLog);
                return successLog;
            }

            string log = Message;
            if (!string.IsNullOrWhiteSpace(ContextPath))
                log += $" Context: '{ContextPath}'.";
            if (!string.IsNullOrWhiteSpace(Remediation))
                log += $" Remediation: {Remediation}";
            if (MirrorResult != null && !MirrorResult.Success)
                log += $" Mirror: {MirrorResult.ToLogString()}";
            if (Diagnostic != null && !Diagnostic.Success && !string.IsNullOrWhiteSpace(Diagnostic.Message))
                log += $" Diagnostic: {Diagnostic.ToLogString()}";
            AppendRecoveryDetails(ref log);

            return log;
        }

        private void AppendRecoveryDetails(ref string log)
        {
            if (MigrationOutcomeReport.HasValue)
                log += $" Migration outcome: {MigrationOutcomeReport.Value.ToCompactSummary()}";

            if (CleanupAttempted || ReattachAttempted || InstallPathAdoptionAttempted)
            {
                log += $" Recovery: cleanup={(CleanupAttempted ? (CleanupSucceeded ? "ok" : "failed") : "skipped")}";
                log += $", reattach={(ReattachAttempted ? (ReattachSucceeded ? "ok" : "failed") : "skipped")}";
                log += $", installPathAdoption={(InstallPathAdoptionAttempted ? (InstallPathAdoptionSucceeded ? "ok" : "failed") : "skipped")}";
                log += $", recoveredState={RecoveredState}";
            }
        }
    }
}
