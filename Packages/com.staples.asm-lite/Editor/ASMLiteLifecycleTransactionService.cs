using System;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Editor
{
    internal enum ASMLiteLifecycleTransactionTestFailurePoint
    {
        None = 0,
        AfterDescriptorRetarget = 1,
        AfterLiveFullControllerRetarget = 2,
        DuringVendorizeVerify = 3,
        DuringReturnVerify = 4,
    }

    internal static class ASMLiteLifecycleTransactionService
    {
        private static ASMLiteLifecycleTransactionTestFailurePoint s_testFailurePoint;

        internal static IDisposable PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint failurePoint)
        {
            var previous = s_testFailurePoint;
            s_testFailurePoint = failurePoint;
            return new ScopedFailurePoint(() => s_testFailurePoint = previous);
        }

        internal static ASMLiteLifecycleTransactionResult ExecuteAttachedVendorize(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            var beforeState = ResolveToolState(avatar, component);
            if (component == null || avatar == null)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedVendorize,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: component == null ? "component" : "avatar",
                    remediation: "Pass a valid ASM-Lite component and avatar descriptor before vendorizing.",
                    message: "[ASM-Lite] Attached vendorize transaction failed because component or avatar context was missing.");
            }

            if (beforeState != ASMLiteWindow.AsmLiteToolState.PackageManaged)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedVendorize,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "toolState",
                    remediation: "Run attached vendorize only when ASM-Lite is attached in package-managed mode.",
                    message: $"[ASM-Lite] Attached vendorize transaction expected PackageManaged state but found {beforeState}.");
            }

            var originalComponentState = CaptureComponentVendorizedState(component);
            ASMLiteGeneratedAssetMirrorResult mirrorResult = null;
            int discoveredParamCount = -1;

            if (!TryRefreshLiveInstallPathRouting(component, "Vendorize Transaction", out string installPathFailure))
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedVendorize,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: ResolveToolState(avatar, component),
                    rollbackState: ResolveToolState(avatar, component),
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: component.gameObject.name,
                    remediation: "Repair live install-path routing before vendorizing attached generated assets.",
                    message: installPathFailure);
            }

            discoveredParamCount = ASMLiteBuilder.Build(component);
            if (discoveredParamCount < 0)
            {
                var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedVendorize,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: ResolveToolState(avatar, component),
                    rollbackState: ResolveToolState(avatar, component),
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: diagnostic?.ContextPath ?? ASMLiteAssetPaths.GeneratedDir,
                    remediation: diagnostic?.Remediation ?? "Fix the build diagnostic before retrying attached vendorize.",
                    message: diagnostic?.Message ?? "[ASM-Lite] Attached vendorize transaction failed because Build() did not succeed.",
                    diagnostic: diagnostic,
                    discoveredParamCount: discoveredParamCount);
            }

            mirrorResult = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(avatar);
            if (!mirrorResult.Success)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedVendorize,
                    failedStage: MapMirrorStage(mirrorResult.FailedStage),
                    beforeState: beforeState,
                    afterState: ResolveToolState(avatar, component),
                    rollbackState: ResolveToolState(avatar, component),
                    rollbackAttempted: mirrorResult.RollbackAttempted,
                    rollbackSucceeded: mirrorResult.RollbackSucceeded,
                    contextPath: mirrorResult.ContextPath,
                    remediation: mirrorResult.Remediation,
                    message: mirrorResult.Message,
                    mirrorResult: mirrorResult,
                    discoveredParamCount: discoveredParamCount);
            }

            var descriptorResult = ASMLiteGeneratedAssetMirrorService.RetargetAvatarGeneratedAssetsToVendorized(avatar, mirrorResult.TargetPath);
            if (!descriptorResult.Success)
            {
                return FailAttachedVendorizeAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    mirrorResult,
                    descriptorResult.Message,
                    descriptorResult.ContextPath,
                    descriptorResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    discoveredParamCount,
                    diagnostic: null,
                    mirrorDetail: descriptorResult);
            }

            if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterDescriptorRetarget))
            {
                return FailAttachedVendorizeAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    mirrorResult,
                    "[ASM-Lite] Injected attached-vendorize failure after descriptor references were retargeted to the staged mirror.",
                    mirrorResult.TargetPath,
                    "Disable the descriptor-retarget failure injection after validating rollback behavior.",
                    ASMLiteLifecycleTransactionStage.Execute,
                    discoveredParamCount,
                    diagnostic: null,
                    mirrorDetail: descriptorResult);
            }

            var liveRetargetResult = ASMLitePrefabCreator.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(component, mirrorResult.TargetPath, "Vendorize Transaction Live Retarget");
            if (!liveRetargetResult.Success)
            {
                return FailAttachedVendorizeAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    mirrorResult,
                    liveRetargetResult.Message,
                    liveRetargetResult.ContextPath,
                    liveRetargetResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    discoveredParamCount,
                    diagnostic: liveRetargetResult);
            }

            if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterLiveFullControllerRetarget))
            {
                return FailAttachedVendorizeAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    mirrorResult,
                    "[ASM-Lite] Injected attached-vendorize failure after live FullController references were retargeted to vendorized assets.",
                    mirrorResult.TargetPath,
                    "Disable the live-FullController failure injection after validating rollback behavior.",
                    ASMLiteLifecycleTransactionStage.Execute,
                    discoveredParamCount,
                    diagnostic: null);
            }

            ApplyComponentVendorizedState(component, useVendorizedGeneratedAssets: true, vendorizedGeneratedAssetsPath: mirrorResult.TargetPath);
            var afterMutationState = ResolveToolState(avatar, component);
            string verifyFailureMessage = string.Empty;
            string verifyFailureContext = string.Empty;
            if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeVerify)
                || !VerifyAttachedVendorizeState(component, avatar, mirrorResult.TargetPath, out verifyFailureMessage, out verifyFailureContext))
            {
                return FailAttachedVendorizeAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    mirrorResult,
                    ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeVerify)
                        ? "[ASM-Lite] Injected attached-vendorize verification failure after component state switched to vendorized mode."
                        : verifyFailureMessage,
                    ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeVerify)
                        ? mirrorResult.TargetPath
                        : verifyFailureContext,
                    ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeVerify)
                        ? "Disable the vendorize verification failure injection after validating rollback behavior."
                        : "Re-run attached vendorize after fixing the failing verification surface.",
                    ASMLiteLifecycleTransactionStage.Verify,
                    discoveredParamCount,
                    diagnostic: null,
                    expectedAfterFailureState: afterMutationState);
            }

            var finalizeMirrorResult = ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(mirrorResult);
            if (!finalizeMirrorResult.Success)
            {
                return FailAttachedVendorizeAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    mirrorResult,
                    finalizeMirrorResult.Message,
                    finalizeMirrorResult.ContextPath,
                    finalizeMirrorResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    discoveredParamCount,
                    diagnostic: null,
                    mirrorDetail: finalizeMirrorResult,
                    expectedAfterFailureState: afterMutationState);
            }

            return ASMLiteLifecycleTransactionResult.Pass(
                operation: ASMLiteLifecycleOperation.AttachedVendorize,
                beforeState: beforeState,
                afterState: ResolveToolState(avatar, component),
                discoveredParamCount: discoveredParamCount,
                message: $"[ASM-Lite] Attached vendorize transaction completed successfully for '{avatar.gameObject.name}'.",
                mirrorResult: mirrorResult);
        }

        internal static ASMLiteLifecycleTransactionResult ExecuteAttachedReturnToPackageManaged(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            var beforeState = ResolveToolState(avatar, component);
            if (component == null || avatar == null)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedReturnToPackageManaged,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: component == null ? "component" : "avatar",
                    remediation: "Pass a valid ASM-Lite component and avatar descriptor before restoring package-managed mode.",
                    message: "[ASM-Lite] Attached return transaction failed because component or avatar context was missing.");
            }

            if (beforeState != ASMLiteWindow.AsmLiteToolState.Vendorized)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedReturnToPackageManaged,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "toolState",
                    remediation: "Run attached return only when ASM-Lite is attached in vendorized mode.",
                    message: $"[ASM-Lite] Attached return transaction expected Vendorized state but found {beforeState}.");
            }

            string vendorizedDir = NormalizeOptionalPath(component.vendorizedGeneratedAssetsPath);
            if (string.IsNullOrWhiteSpace(vendorizedDir))
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedReturnToPackageManaged,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "component.vendorizedGeneratedAssetsPath",
                    remediation: "Persist the vendorized generated-assets path before restoring package-managed mode.",
                    message: "[ASM-Lite] Attached return transaction failed because the component did not track a vendorized generated-assets path.");
            }

            var originalComponentState = CaptureComponentVendorizedState(component);
            ASMLiteGeneratedAssetMirrorResult deleteBackupResult = null;

            var refreshResult = ASMLitePrefabCreator.TryRefreshLiveFullControllerWiringWithDiagnostics(component.gameObject, component, "Return To Package Managed Transaction Refresh");
            if (!refreshResult.Success)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedReturnToPackageManaged,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: ResolveToolState(avatar, component),
                    rollbackState: ResolveToolState(avatar, component),
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: refreshResult.ContextPath,
                    remediation: refreshResult.Remediation,
                    message: refreshResult.Message,
                    diagnostic: refreshResult);
            }

            var descriptorResult = ASMLiteGeneratedAssetMirrorService.RestoreAvatarGeneratedAssetsToPackageManaged(avatar, vendorizedDir);
            if (!descriptorResult.Success)
            {
                return FailAttachedReturnAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    vendorizedDir,
                    deleteBackupResult,
                    descriptorResult.Message,
                    descriptorResult.ContextPath,
                    descriptorResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    diagnostic: null,
                    mirrorDetail: descriptorResult);
            }

            var liveRetargetResult = ASMLitePrefabCreator.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(component, ASMLiteAssetPaths.GeneratedDir, "Return To Package Managed Transaction Live Retarget");
            if (!liveRetargetResult.Success)
            {
                return FailAttachedReturnAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    vendorizedDir,
                    deleteBackupResult,
                    liveRetargetResult.Message,
                    liveRetargetResult.ContextPath,
                    liveRetargetResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    diagnostic: liveRetargetResult);
            }

            deleteBackupResult = ASMLiteGeneratedAssetMirrorService.BackupVendorizedFolderForDelete(vendorizedDir);
            if (!deleteBackupResult.Success)
            {
                return FailAttachedReturnAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    vendorizedDir,
                    deleteBackupResult,
                    deleteBackupResult.Message,
                    deleteBackupResult.ContextPath,
                    deleteBackupResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    diagnostic: null,
                    mirrorDetail: deleteBackupResult);
            }

            ApplyComponentVendorizedState(component, useVendorizedGeneratedAssets: false, vendorizedGeneratedAssetsPath: string.Empty);
            if (!TryRefreshLiveInstallPathRouting(component, "Return To Package Managed Transaction Verify", out string returnInstallPathFailure))
            {
                return FailAttachedReturnAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    vendorizedDir,
                    deleteBackupResult,
                    returnInstallPathFailure,
                    component != null ? component.gameObject.name : "component",
                    "Rebuild install-path routing before completing attached return.",
                    ASMLiteLifecycleTransactionStage.Verify,
                    diagnostic: null,
                    expectedAfterFailureState: ResolveToolState(avatar, component));
            }

            var afterMutationState = ResolveToolState(avatar, component);
            string verifyFailureMessage = string.Empty;
            string verifyFailureContext = string.Empty;
            if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringReturnVerify)
                || !VerifyAttachedReturnState(component, avatar, vendorizedDir, out verifyFailureMessage, out verifyFailureContext))
            {
                return FailAttachedReturnAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    vendorizedDir,
                    deleteBackupResult,
                    ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringReturnVerify)
                        ? "[ASM-Lite] Injected attached-return verification failure after component state switched back to package-managed mode."
                        : verifyFailureMessage,
                    ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringReturnVerify)
                        ? vendorizedDir
                        : verifyFailureContext,
                    ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringReturnVerify)
                        ? "Disable the return verification failure injection after validating rollback behavior."
                        : "Re-run attached return after fixing the failing verification surface.",
                    ASMLiteLifecycleTransactionStage.Verify,
                    diagnostic: null,
                    expectedAfterFailureState: afterMutationState);
            }

            var finalizeDeleteResult = ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedFolderDelete(deleteBackupResult);
            if (!finalizeDeleteResult.Success)
            {
                return FailAttachedReturnAndRollback(
                    component,
                    avatar,
                    beforeState,
                    originalComponentState,
                    vendorizedDir,
                    deleteBackupResult,
                    finalizeDeleteResult.Message,
                    finalizeDeleteResult.ContextPath,
                    finalizeDeleteResult.Remediation,
                    ASMLiteLifecycleTransactionStage.Execute,
                    diagnostic: null,
                    mirrorDetail: finalizeDeleteResult,
                    expectedAfterFailureState: afterMutationState);
            }

            return ASMLiteLifecycleTransactionResult.Pass(
                operation: ASMLiteLifecycleOperation.AttachedReturnToPackageManaged,
                beforeState: beforeState,
                afterState: ResolveToolState(avatar, component),
                discoveredParamCount: -1,
                message: $"[ASM-Lite] Attached return transaction restored package-managed generated assets for '{avatar.gameObject.name}'.",
                mirrorResult: deleteBackupResult);
        }

        private static ASMLiteLifecycleTransactionResult FailAttachedVendorizeAndRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteWindow.AsmLiteToolState beforeState,
            ComponentVendorizedStateSnapshot originalComponentState,
            ASMLiteGeneratedAssetMirrorResult mirrorResult,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            int discoveredParamCount,
            ASMLiteBuildDiagnosticResult diagnostic,
            ASMLiteGeneratedAssetMirrorResult mirrorDetail = null,
            ASMLiteWindow.AsmLiteToolState? expectedAfterFailureState = null)
        {
            var afterFailureState = expectedAfterFailureState ?? ResolveToolState(avatar, component);
            bool rollbackAttempted = true;

            var restoreDescriptorResult = ASMLiteGeneratedAssetMirrorService.RestoreAvatarGeneratedAssetsToPackageManaged(avatar, mirrorResult?.TargetPath ?? string.Empty);
            var restoreLiveResult = ASMLitePrefabCreator.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(component, ASMLiteAssetPaths.GeneratedDir, "Vendorize Transaction Rollback Live Retarget");
            ApplyComponentVendorizedState(component, originalComponentState.UseVendorizedGeneratedAssets, originalComponentState.VendorizedGeneratedAssetsPath);
            var rollbackMirrorResult = mirrorResult != null
                ? ASMLiteGeneratedAssetMirrorService.RollbackVendorizedMirror(mirrorResult)
                : null;
            bool routingRollbackSucceeded = component != null && ASMLiteBuilder.TrySyncInstallPathRouting(component);

            bool rollbackSucceeded = restoreDescriptorResult.Success
                && restoreLiveResult.Success
                && routingRollbackSucceeded
                && (rollbackMirrorResult == null || rollbackMirrorResult.Success)
                && VerifyPackageManagedRollbackState(component, avatar, out _, out _);
            var rollbackState = ResolveToolState(avatar, component);

            return ASMLiteLifecycleTransactionResult.Fail(
                operation: ASMLiteLifecycleOperation.AttachedVendorize,
                failedStage: failedStage,
                beforeState: beforeState,
                afterState: afterFailureState,
                rollbackState: rollbackState,
                rollbackAttempted: rollbackAttempted,
                rollbackSucceeded: rollbackSucceeded,
                contextPath: contextPath,
                remediation: remediation,
                message: message,
                diagnostic: BuildRollbackDiagnostic(diagnostic, restoreLiveResult, rollbackSucceeded),
                mirrorResult: mirrorDetail ?? rollbackMirrorResult ?? mirrorResult,
                discoveredParamCount: discoveredParamCount);
        }

        private static ASMLiteLifecycleTransactionResult FailAttachedReturnAndRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteWindow.AsmLiteToolState beforeState,
            ComponentVendorizedStateSnapshot originalComponentState,
            string vendorizedDir,
            ASMLiteGeneratedAssetMirrorResult deleteBackupResult,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteBuildDiagnosticResult diagnostic,
            ASMLiteGeneratedAssetMirrorResult mirrorDetail = null,
            ASMLiteWindow.AsmLiteToolState? expectedAfterFailureState = null)
        {
            var afterFailureState = expectedAfterFailureState ?? ResolveToolState(avatar, component);
            bool rollbackAttempted = true;

            ApplyComponentVendorizedState(component, originalComponentState.UseVendorizedGeneratedAssets, originalComponentState.VendorizedGeneratedAssetsPath);
            var rollbackDeleteResult = deleteBackupResult != null && deleteBackupResult.Success
                ? ASMLiteGeneratedAssetMirrorService.RollbackVendorizedFolderDelete(deleteBackupResult)
                : null;
            var restoreDescriptorResult = ASMLiteGeneratedAssetMirrorService.RestoreAvatarGeneratedAssetsToVendorized(avatar, vendorizedDir);
            var restoreLiveResult = ASMLitePrefabCreator.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(component, vendorizedDir, "Return To Package Managed Transaction Rollback Live Retarget");

            bool rollbackSucceeded = (rollbackDeleteResult == null || rollbackDeleteResult.Success)
                && restoreDescriptorResult.Success
                && restoreLiveResult.Success
                && VerifyVendorizedRollbackState(component, avatar, vendorizedDir, out _, out _);
            var rollbackState = ResolveToolState(avatar, component);

            return ASMLiteLifecycleTransactionResult.Fail(
                operation: ASMLiteLifecycleOperation.AttachedReturnToPackageManaged,
                failedStage: failedStage,
                beforeState: beforeState,
                afterState: afterFailureState,
                rollbackState: rollbackState,
                rollbackAttempted: rollbackAttempted,
                rollbackSucceeded: rollbackSucceeded,
                contextPath: contextPath,
                remediation: remediation,
                message: message,
                diagnostic: BuildRollbackDiagnostic(diagnostic, restoreLiveResult, rollbackSucceeded),
                mirrorResult: mirrorDetail ?? rollbackDeleteResult ?? deleteBackupResult,
                discoveredParamCount: -1);
        }

        private static bool VerifyAttachedVendorizeState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            string vendorizedDir,
            out string failureMessage,
            out string failureContext)
        {
            if (!VerifyComponentVendorizedState(component, expectedUseVendorized: true, expectedPath: vendorizedDir, out failureMessage, out failureContext))
                return false;

            if (!VerifyLiveFullControllerReferencesUnderPrefix(component, vendorizedDir, out failureMessage, out failureContext))
                return false;

            if (!AssetDatabase.IsValidFolder(vendorizedDir))
            {
                failureMessage = "[ASM-Lite] Attached vendorize verification failed because the vendorized generated-assets folder was missing.";
                failureContext = vendorizedDir;
                return false;
            }

            if (HasAvatarGeneratedReferencesUnderPrefix(avatar, ASMLiteAssetPaths.GeneratedDir))
            {
                failureMessage = "[ASM-Lite] Attached vendorize verification failed because descriptor-level generated assets still referenced package-managed generated assets after vendorization.";
                failureContext = ASMLiteAssetPaths.GeneratedDir;
                return false;
            }

            if (ResolveToolState(avatar, component) != ASMLiteWindow.AsmLiteToolState.Vendorized)
            {
                failureMessage = "[ASM-Lite] Attached vendorize verification failed because tool-state classification did not resolve to Vendorized.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static bool VerifyAttachedReturnState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            string vendorizedDir,
            out string failureMessage,
            out string failureContext)
        {
            if (!VerifyComponentVendorizedState(component, expectedUseVendorized: false, expectedPath: string.Empty, out failureMessage, out failureContext))
                return false;

            if (!VerifyLiveFullControllerReferencesUnderPrefix(component, ASMLiteAssetPaths.GeneratedDir, out failureMessage, out failureContext))
                return false;

            if (AssetDatabase.IsValidFolder(vendorizedDir))
            {
                failureMessage = "[ASM-Lite] Attached return verification failed because the vendorized generated-assets folder still existed after delete staging.";
                failureContext = vendorizedDir;
                return false;
            }

            if (HasAvatarGeneratedReferencesUnderPrefix(avatar, vendorizedDir))
            {
                failureMessage = "[ASM-Lite] Attached return verification failed because descriptor-level generated assets still referenced the vendorized folder.";
                failureContext = vendorizedDir;
                return false;
            }

            if (ResolveToolState(avatar, component) != ASMLiteWindow.AsmLiteToolState.PackageManaged)
            {
                failureMessage = "[ASM-Lite] Attached return verification failed because tool-state classification did not resolve to PackageManaged.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static bool VerifyPackageManagedRollbackState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            out string failureMessage,
            out string failureContext)
        {
            return VerifyAttachedReturnState(component, avatar, NormalizeOptionalPath(component != null ? component.vendorizedGeneratedAssetsPath : string.Empty), out failureMessage, out failureContext)
                && ResolveToolState(avatar, component) == ASMLiteWindow.AsmLiteToolState.PackageManaged;
        }

        private static bool VerifyVendorizedRollbackState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            string vendorizedDir,
            out string failureMessage,
            out string failureContext)
        {
            return VerifyAttachedVendorizeState(component, avatar, vendorizedDir, out failureMessage, out failureContext)
                && ResolveToolState(avatar, component) == ASMLiteWindow.AsmLiteToolState.Vendorized;
        }

        private static bool VerifyComponentVendorizedState(
            ASMLiteComponent component,
            bool expectedUseVendorized,
            string expectedPath,
            out string failureMessage,
            out string failureContext)
        {
            if (component == null)
            {
                failureMessage = "[ASM-Lite] Lifecycle transaction verification failed because the ASM-Lite component was missing.";
                failureContext = "component";
                return false;
            }

            string normalizedExpectedPath = NormalizeOptionalPath(expectedPath);
            string normalizedActualPath = NormalizeOptionalPath(component.vendorizedGeneratedAssetsPath);
            if (component.useVendorizedGeneratedAssets != expectedUseVendorized)
            {
                failureMessage = "[ASM-Lite] Lifecycle transaction verification failed because the component vendorized-mode flag did not match the expected state.";
                failureContext = component.gameObject.name;
                return false;
            }

            if (!string.Equals(normalizedActualPath, normalizedExpectedPath, StringComparison.Ordinal))
            {
                failureMessage = "[ASM-Lite] Lifecycle transaction verification failed because the component vendorized generated-assets path did not match the expected state.";
                failureContext = normalizedActualPath;
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static bool VerifyLiveFullControllerReferencesUnderPrefix(
            ASMLiteComponent component,
            string expectedPrefix,
            out string failureMessage,
            out string failureContext)
        {
            var snapshotResult = ASMLitePrefabCreator.TryCaptureLiveFullControllerReferenceSnapshot(component, "Lifecycle Transaction Verify", out var snapshot);
            if (!snapshotResult.Success)
            {
                failureMessage = snapshotResult.Message;
                failureContext = snapshotResult.ContextPath;
                return false;
            }

            string normalizedExpectedPrefix = NormalizeOptionalPath(expectedPrefix);
            if (!PathStartsWith(snapshot.ControllerAssetPath, normalizedExpectedPrefix)
                || !PathStartsWith(snapshot.MenuAssetPath, normalizedExpectedPrefix)
                || !PathStartsWith(snapshot.ParametersAssetPath, normalizedExpectedPrefix))
            {
                failureMessage = "[ASM-Lite] Lifecycle transaction verification failed because live FullController references were not retargeted to the expected generated-assets prefix.";
                failureContext = normalizedExpectedPrefix;
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static bool HasAvatarGeneratedReferencesUnderPrefix(VRCAvatarDescriptor avatar, string prefix)
        {
            if (avatar == null)
                return false;

            string normalizedPrefix = NormalizeOptionalPath(prefix);
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
                return false;

            string exprPath = NormalizeOptionalPath(avatar.expressionParameters ? AssetDatabase.GetAssetPath(avatar.expressionParameters) : string.Empty);
            if (PathStartsWith(exprPath, normalizedPrefix))
                return true;

            string menuPath = NormalizeOptionalPath(avatar.expressionsMenu ? AssetDatabase.GetAssetPath(avatar.expressionsMenu) : string.Empty);
            if (PathStartsWith(menuPath, normalizedPrefix) || MenuReferencesPrefix(avatar.expressionsMenu, normalizedPrefix))
                return true;

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                string controllerPath = NormalizeOptionalPath(avatar.baseAnimationLayers[i].animatorController
                    ? AssetDatabase.GetAssetPath(avatar.baseAnimationLayers[i].animatorController)
                    : string.Empty);
                if (PathStartsWith(controllerPath, normalizedPrefix))
                    return true;
            }

            return false;
        }

        private static ASMLiteBuildDiagnosticResult BuildRollbackDiagnostic(
            ASMLiteBuildDiagnosticResult originalDiagnostic,
            ASMLiteBuildDiagnosticResult rollbackDiagnostic,
            bool rollbackSucceeded)
        {
            if (originalDiagnostic != null && !originalDiagnostic.Success)
            {
                if (!rollbackSucceeded && rollbackDiagnostic != null && !rollbackDiagnostic.Success)
                {
                    return ASMLiteBuildDiagnosticResult.Fail(
                        code: originalDiagnostic.Code,
                        contextPath: originalDiagnostic.ContextPath,
                        remediation: originalDiagnostic.Remediation,
                        message: originalDiagnostic.Message,
                        innerDiagnostic: rollbackDiagnostic);
                }

                return originalDiagnostic;
            }

            if (rollbackDiagnostic != null && !rollbackDiagnostic.Success)
                return rollbackDiagnostic;

            return ASMLiteBuildDiagnosticResult.Pass();
        }

        private static ComponentVendorizedStateSnapshot CaptureComponentVendorizedState(ASMLiteComponent component)
        {
            return new ComponentVendorizedStateSnapshot(
                component != null && component.useVendorizedGeneratedAssets,
                NormalizeOptionalPath(component != null ? component.vendorizedGeneratedAssetsPath : string.Empty));
        }

        private static void ApplyComponentVendorizedState(ASMLiteComponent component, bool useVendorizedGeneratedAssets, string vendorizedGeneratedAssetsPath)
        {
            if (component == null)
                return;

            component.useVendorizedGeneratedAssets = useVendorizedGeneratedAssets;
            component.vendorizedGeneratedAssetsPath = NormalizeOptionalPath(vendorizedGeneratedAssetsPath);
            EditorUtility.SetDirty(component);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static ASMLiteWindow.AsmLiteToolState ResolveToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            return ASMLiteWindow.GetAsmLiteToolState(avatar, component);
        }

        private static ASMLiteLifecycleTransactionStage MapMirrorStage(ASMLiteGeneratedAssetMirrorStage mirrorStage)
        {
            switch (mirrorStage)
            {
                case ASMLiteGeneratedAssetMirrorStage.Preflight:
                    return ASMLiteLifecycleTransactionStage.Preflight;
                case ASMLiteGeneratedAssetMirrorStage.Verify:
                    return ASMLiteLifecycleTransactionStage.Verify;
                case ASMLiteGeneratedAssetMirrorStage.Rollback:
                    return ASMLiteLifecycleTransactionStage.Rollback;
                default:
                    return ASMLiteLifecycleTransactionStage.Execute;
            }
        }

        private static bool TryRefreshLiveInstallPathRouting(ASMLiteComponent component, string contextLabel, out string failureMessage)
        {
            failureMessage = string.Empty;
            if (component == null)
            {
                failureMessage = $"[ASM-Lite] {contextLabel}: Cannot refresh install-path routing because the ASM-Lite component was null.";
                return false;
            }

            var refreshResult = ASMLitePrefabCreator.TryRefreshLiveFullControllerWiringWithDiagnostics(
                component.gameObject,
                component,
                contextLabel + " Auto-Heal");
            if (!refreshResult.Success)
            {
                failureMessage = refreshResult.Message;
                return false;
            }

            if (!ASMLiteBuilder.TrySyncInstallPathRouting(component))
            {
                failureMessage = $"[ASM-Lite] {contextLabel}: Failed to refresh install-path routing on '{component.gameObject.name}'.";
                return false;
            }

            return true;
        }

        private static bool PathStartsWith(string assetPath, string prefix)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(prefix))
                return false;

            return assetPath.StartsWith(prefix.TrimEnd('/'), StringComparison.Ordinal);
        }

        private static bool MenuReferencesPrefix(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu menu, string prefix)
        {
            return MenuReferencesPrefix(menu, prefix, new System.Collections.Generic.HashSet<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>());
        }

        private static bool MenuReferencesPrefix(
            VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu menu,
            string prefix,
            System.Collections.Generic.HashSet<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu) || menu.controls == null)
                return false;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control?.subMenu == null)
                    continue;

                string subMenuPath = NormalizeOptionalPath(AssetDatabase.GetAssetPath(control.subMenu));
                if (PathStartsWith(subMenuPath, prefix) || MenuReferencesPrefix(control.subMenu, prefix, visited))
                    return true;
            }

            return false;
        }

        private static string NormalizeOptionalPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimEnd('/');
        }

        private static bool ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint failurePoint)
        {
            return s_testFailurePoint == failurePoint;
        }

        private readonly struct ComponentVendorizedStateSnapshot
        {
            internal ComponentVendorizedStateSnapshot(bool useVendorizedGeneratedAssets, string vendorizedGeneratedAssetsPath)
            {
                UseVendorizedGeneratedAssets = useVendorizedGeneratedAssets;
                VendorizedGeneratedAssetsPath = vendorizedGeneratedAssetsPath ?? string.Empty;
            }

            internal bool UseVendorizedGeneratedAssets { get; }
            internal string VendorizedGeneratedAssetsPath { get; }
        }

        private sealed class ScopedFailurePoint : IDisposable
        {
            private readonly Action _restore;
            private bool _disposed;

            internal ScopedFailurePoint(Action restore)
            {
                _restore = restore;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _restore?.Invoke();
            }
        }
    }
}
