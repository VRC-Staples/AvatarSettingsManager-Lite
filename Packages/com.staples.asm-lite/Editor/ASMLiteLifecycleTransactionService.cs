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
        AfterDetachExecute = 5,
        DuringDetachVerify = 6,
        DuringVendorizeDetachVerify = 7,
        DuringDetachedRecoveryVerify = 8,
        BeforeDetachedRecoveryRoutingFinalize = 9,
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

            if (beforeState != ASMLiteInstallationState.PackageManaged)
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

            var buildResult = ASMLiteGeneratedAssetBuildTransaction.Execute(component);
            discoveredParamCount = buildResult.DiscoveredParamCount;
            if (!buildResult.Success)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.AttachedVendorize,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: ResolveToolState(avatar, component),
                    rollbackState: ResolveToolState(avatar, component),
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: string.IsNullOrWhiteSpace(buildResult.ContextPath) ? ASMLiteAssetPaths.GeneratedDir : buildResult.ContextPath,
                    remediation: string.IsNullOrWhiteSpace(buildResult.Remediation) ? "Fix the build diagnostic before retrying attached vendorize." : buildResult.Remediation,
                    message: string.IsNullOrWhiteSpace(buildResult.Message) ? "[ASM-Lite] Attached vendorize transaction failed because Build() did not succeed." : buildResult.Message,
                    diagnostic: buildResult.Diagnostic,
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
                || !ASMLiteLifecycleVerification.VerifyAttachedVendorizeState(component, avatar, mirrorResult.TargetPath, out verifyFailureMessage, out verifyFailureContext))
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

            if (beforeState != ASMLiteInstallationState.Vendorized)
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
                || !ASMLiteLifecycleVerification.VerifyAttachedReturnState(component, avatar, vendorizedDir, out verifyFailureMessage, out verifyFailureContext))
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

        internal static ASMLiteLifecycleTransactionResult ExecuteDetachToDirectDelivery(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            var beforeState = ResolveToolState(avatar, component);
            if (component == null || avatar == null)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.DetachToDirectDelivery,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: component == null ? "component" : "avatar",
                    remediation: "Pass a valid ASM-Lite component and avatar descriptor before detaching to direct delivery.",
                    message: "[ASM-Lite] Detach transaction failed because component or avatar context was missing.");
            }

            if (beforeState != ASMLiteInstallationState.PackageManaged
                && beforeState != ASMLiteInstallationState.Vendorized)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.DetachToDirectDelivery,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "toolState",
                    remediation: "Run detach only when ASM-Lite is attached in package-managed or vendorized mode.",
                    message: $"[ASM-Lite] Detach transaction expected PackageManaged or Vendorized state but found {beforeState}.");
            }

            var snapshot = ASMLiteDirectDeliveryRollbackSnapshot.Capture(avatar);
            var componentSnapshot = ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(component);
            try
            {
                ASMLiteInstallationState expectedDetachedState = ResolveDetachSuccessState(beforeState, vendorizeToAssets: false);
                string vendorizedDir = NormalizeOptionalPath(component.vendorizedGeneratedAssetsPath);
                var directDeliveryResult = ASMLiteDirectDeliveryTransaction.Execute(
                    component,
                    avatar,
                    new ASMLiteDirectDeliveryTransactionPlan(
                        expectedDetachedState,
                        vendorizedDir,
                        retargetDescriptorToVendorized: expectedDetachedState == ASMLiteInstallationState.Vendorized,
                        verifyFailurePoint: ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify,
                        executeFailureRemediation: "Restore the attached ASM-Lite state before retrying detach.",
                        afterApplyFailureMessage: "[ASM-Lite] Injected detach failure after direct-delivery content was applied but before destroy-time verification completed.",
                        afterApplyFailureRemediation: "Disable the detach failure injection after validating rollback behavior.",
                        verifyFailureMessage: "[ASM-Lite] Injected detach verification failure after direct-delivery content was applied.",
                        verifyFailureRemediation: "Disable the detach verification failure injection after validating rollback behavior.",
                        verificationFailureRemediation: "Restore the attached ASM-Lite state before retrying detach.",
                        verifyFailureContextUsesVendorizedDir: false),
                    ShouldFailForTesting);

                if (!directDeliveryResult.Success)
                {
                    return FailDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        directDeliveryResult.Message,
                        directDeliveryResult.ContextPath,
                        directDeliveryResult.Remediation,
                        directDeliveryResult.FailedStage,
                        diagnostic: null);
                }

                return ASMLiteLifecycleTransactionResult.Pass(
                    operation: ASMLiteLifecycleOperation.DetachToDirectDelivery,
                    beforeState: beforeState,
                    afterState: directDeliveryResult.ExpectedDetachedState,
                    discoveredParamCount: -1,
                    message: $"[ASM-Lite] {directDeliveryResult.Detail}");
            }
            finally
            {
                snapshot.Dispose();
            }
        }

        internal static ASMLiteLifecycleTransactionResult ExecuteVendorizeAndDetach(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            var beforeState = ResolveToolState(avatar, component);
            if (component == null || avatar == null)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.VendorizeAndDetach,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: component == null ? "component" : "avatar",
                    remediation: "Pass a valid ASM-Lite component and avatar descriptor before vendorizing and detaching.",
                    message: "[ASM-Lite] Vendorize + detach transaction failed because component or avatar context was missing.");
            }

            if (beforeState != ASMLiteInstallationState.PackageManaged
                && beforeState != ASMLiteInstallationState.Vendorized)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.VendorizeAndDetach,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "toolState",
                    remediation: "Run vendorize + detach only when ASM-Lite is attached in package-managed or vendorized mode.",
                    message: $"[ASM-Lite] Vendorize + detach expected PackageManaged or Vendorized state but found {beforeState}.");
            }

            var componentSnapshot = ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(component);
            if (beforeState == ASMLiteInstallationState.PackageManaged)
            {
                var vendorizeResult = ExecuteAttachedVendorize(component, avatar).WithOperation(
                    ASMLiteLifecycleOperation.VendorizeAndDetach,
                    "[ASM-Lite] Vendorize + detach could not complete because the attached vendorize stage failed.");
                if (!vendorizeResult.Success)
                    return vendorizeResult;
            }

            var snapshot = beforeState == ASMLiteInstallationState.Vendorized
                ? ASMLiteDirectDeliveryRollbackSnapshot.Capture(avatar)
                : null;
            try
            {
                string vendorizedDir = NormalizeOptionalPath(component.vendorizedGeneratedAssetsPath);
                ASMLiteInstallationState expectedDetachedState = ResolveDetachSuccessState(beforeState, vendorizeToAssets: true);
                var directDeliveryResult = ASMLiteDirectDeliveryTransaction.Execute(
                    component,
                    avatar,
                    new ASMLiteDirectDeliveryTransactionPlan(
                        expectedDetachedState,
                        vendorizedDir,
                        retargetDescriptorToVendorized: true,
                        verifyFailurePoint: ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeDetachVerify,
                        executeFailureRemediation: "Restore the attached ASM-Lite state before retrying vendorize + detach.",
                        afterApplyFailureMessage: "[ASM-Lite] Injected vendorize + detach failure after direct-delivery content was applied but before destroy-time verification completed.",
                        afterApplyFailureRemediation: "Disable the vendorize + detach failure injection after validating rollback behavior.",
                        verifyFailureMessage: "[ASM-Lite] Injected vendorize + detach verification failure after direct-delivery content was applied to vendorized assets.",
                        verifyFailureRemediation: "Disable the vendorize + detach verification failure injection after validating rollback behavior.",
                        verificationFailureRemediation: "Restore the attached ASM-Lite state before retrying vendorize + detach.",
                        verifyFailureContextUsesVendorizedDir: true),
                    ShouldFailForTesting);

                if (!directDeliveryResult.Success)
                {
                    return FailVendorizeAndDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        directDeliveryResult.Message,
                        directDeliveryResult.ContextPath,
                        directDeliveryResult.Remediation,
                        directDeliveryResult.FailedStage,
                        diagnostic: null);
                }

                return ASMLiteLifecycleTransactionResult.Pass(
                    operation: ASMLiteLifecycleOperation.VendorizeAndDetach,
                    beforeState: beforeState,
                    afterState: directDeliveryResult.ExpectedDetachedState,
                    discoveredParamCount: -1,
                    message: $"[ASM-Lite] {directDeliveryResult.Detail}");
            }
            finally
            {
                snapshot?.Dispose();
            }
        }

        internal static ASMLiteLifecycleTransactionResult ExecuteDetachedReturnToPackageManagedRecovery(
            VRCAvatarDescriptor avatar,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot pendingSnapshot)
        {
            var beforeState = ASMLiteWindow.GetAsmLiteToolState(avatar, null);
            if (avatar == null)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "avatar",
                    remediation: "Select a valid avatar descriptor before restoring package-managed mode.",
                    message: "[ASM-Lite] Detached return-to-package-managed recovery failed because the avatar descriptor was null.");
            }

            if (avatar.GetComponentInChildren<ASMLiteComponent>(true) != null)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: avatar.gameObject.name,
                    remediation: "Use the attached return-to-package-managed flow when an ASM-Lite component is already present.",
                    message: "[ASM-Lite] Detached return-to-package-managed recovery expected no attached ASM-Lite component, but one was found.");
            }

            if (beforeState != ASMLiteInstallationState.Detached
                && beforeState != ASMLiteInstallationState.Vendorized)
            {
                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                    failedStage: ASMLiteLifecycleTransactionStage.Preflight,
                    beforeState: beforeState,
                    afterState: beforeState,
                    rollbackState: beforeState,
                    rollbackAttempted: false,
                    rollbackSucceeded: false,
                    contextPath: "toolState",
                    remediation: "Run detached return only when the avatar is classified as Detached or Vendorized with no attached component.",
                    message: $"[ASM-Lite] Detached return-to-package-managed recovery expected Detached or Vendorized state but found {beforeState}.");
            }

            var cleanup = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(avatar);
            var migrationOutcome = ASMLiteMigrationContinuityService.CreateOutcomeReport(
                default,
                new ASMLiteBuilder.RebuildMigrationReport(0, cleanup, componentMissing: false, avatarDescriptorFound: true),
                default);
            var packageManagedSnapshot = CreatePackageManagedCustomizationSnapshot(pendingSnapshot);

            GameObject instance = null;
            bool reattachAttempted = false;
            bool installPathAdoptionAttempted = false;
            bool installPathAdoptionSucceeded = false;
            ASMLiteMigrationContinuityService.InstallPathAdoptionResult adoption = default;
            int pendingLegacyMoveMenuHelpers = 0;

            try
            {
                ASMLitePrefabCreator.CreatePrefab();
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);
                if (prefabAsset == null)
                {
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Execute,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: false,
                        rollbackSucceeded: false,
                        contextPath: ASMLiteAssetPaths.Prefab,
                        remediation: "Recreate the ASM-Lite prefab asset before retrying detached recovery.",
                        message: "[ASM-Lite] Detached return-to-package-managed recovery could not load the ASM-Lite prefab asset.",
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: false,
                        reattachSucceeded: false,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                reattachAttempted = true;
                instance = PrefabUtility.InstantiatePrefab(prefabAsset, avatar.transform) as GameObject;
                var component = instance != null ? instance.GetComponent<ASMLiteComponent>() : null;
                if (component == null)
                {
                    if (instance != null)
                        UnityEngine.Object.DestroyImmediate(instance);

                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Execute,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: true,
                        rollbackSucceeded: true,
                        contextPath: avatar.gameObject.name,
                        remediation: "Ensure the ASM-Lite prefab still contains ASMLiteComponent before retrying detached recovery.",
                        message: "[ASM-Lite] Detached return-to-package-managed recovery instantiated the prefab, but the ASM-Lite component was missing.",
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                bool resolvedDetachedInstallPath = ASMLiteMigrationContinuityService.TryResolveInstallPathPrefixFromMoveMenu(
                    avatar,
                    pendingSnapshot.UseCustomRootName && !string.IsNullOrWhiteSpace(pendingSnapshot.CustomRootName)
                        ? pendingSnapshot.CustomRootName
                        : ASMLiteBuilder.DefaultRootControlName,
                    out string detachedInstallPrefix);

                ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, packageManagedSnapshot);
                if (resolvedDetachedInstallPath
                    && (!packageManagedSnapshot.UseCustomInstallPath || string.IsNullOrWhiteSpace(packageManagedSnapshot.CustomInstallPath)))
                {
                    component.useCustomInstallPath = true;
                    component.customInstallPath = detachedInstallPrefix;
                    EditorUtility.SetDirty(component);
                    adoption = new ASMLiteMigrationContinuityService.InstallPathAdoptionResult(true, detachedInstallPrefix, 0);
                    installPathAdoptionAttempted = true;
                    installPathAdoptionSucceeded = true;
                    migrationOutcome = ASMLiteMigrationContinuityService.CreateOutcomeReport(
                        default,
                        new ASMLiteBuilder.RebuildMigrationReport(0, cleanup, componentMissing: false, avatarDescriptorFound: true),
                        adoption);
                }

                var adoptedInstallPath = ASMLiteMigrationContinuityService.TryAdoptInstallPathFromMoveMenu(
                    component,
                    avatar,
                    consumeLegacyMoveMenuHelpers: false);
                pendingLegacyMoveMenuHelpers = ASMLiteMigrationContinuityService.CountMatchingMoveMenuHelpers(component, avatar);
                if (adoptedInstallPath.Adopted || pendingLegacyMoveMenuHelpers > 0)
                {
                    adoption = MergeInstallPathAdoptionResult(adoption, adoptedInstallPath, removedMoveComponents: 0);
                    installPathAdoptionAttempted = true;
                    installPathAdoptionSucceeded = adoption.Adopted;
                    migrationOutcome = ASMLiteMigrationContinuityService.CreateOutcomeReport(
                        default,
                        new ASMLiteBuilder.RebuildMigrationReport(0, cleanup, componentMissing: false, avatarDescriptorFound: true),
                        adoption);
                }

                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.BeforeDetachedRecoveryRoutingFinalize))
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = null;
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Execute,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: true,
                        rollbackSucceeded: true,
                        contextPath: avatar.gameObject.name,
                        remediation: "Disable the detached-recovery pre-finalize failure injection after validating continuity-marker retention.",
                        message: "[ASM-Lite] Injected detached recovery failure before install-path routing finalization completed.",
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        installPathAdoptionAttempted: installPathAdoptionAttempted,
                        installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                AssetDatabase.SaveAssets();

                var refreshResult = ASMLitePrefabCreator.TryRefreshLiveFullControllerWiringWithDiagnostics(instance, component, "Detached Return Recovery");
                if (!refreshResult.Success)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = null;
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Execute,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: true,
                        rollbackSucceeded: true,
                        contextPath: refreshResult.ContextPath,
                        remediation: refreshResult.Remediation,
                        message: refreshResult.Message,
                        diagnostic: refreshResult,
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        installPathAdoptionAttempted: installPathAdoptionAttempted,
                        installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                if (!TryRefreshLiveInstallPathRouting(component, "Detached Return Recovery Routing", out string routingFailure))
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = null;
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Execute,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: true,
                        rollbackSucceeded: true,
                        contextPath: avatar.gameObject.name,
                        remediation: "Repair install-path routing before retrying detached recovery.",
                        message: routingFailure,
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        installPathAdoptionAttempted: installPathAdoptionAttempted,
                        installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                var recoveryBuildResult = ASMLiteGeneratedAssetBuildTransaction.Execute(component);
                int buildResult = recoveryBuildResult.DiscoveredParamCount;
                if (!recoveryBuildResult.Success)
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = null;
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Execute,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: true,
                        rollbackSucceeded: true,
                        contextPath: string.IsNullOrWhiteSpace(recoveryBuildResult.ContextPath) ? ASMLiteAssetPaths.GeneratedDir : recoveryBuildResult.ContextPath,
                        remediation: string.IsNullOrWhiteSpace(recoveryBuildResult.Remediation) ? "Fix the build diagnostic before retrying detached recovery." : recoveryBuildResult.Remediation,
                        message: string.IsNullOrWhiteSpace(recoveryBuildResult.Message) ? "[ASM-Lite] Detached return-to-package-managed recovery failed because Build() did not succeed." : recoveryBuildResult.Message,
                        diagnostic: recoveryBuildResult.Diagnostic,
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        installPathAdoptionAttempted: installPathAdoptionAttempted,
                        installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                if (!adoption.Adopted && packageManagedSnapshot.UseCustomInstallPath)
                {
                    ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, packageManagedSnapshot);
                    if (!TryRefreshLiveInstallPathRouting(component, "Detached Return Recovery Finalize", out string finalizeRoutingFailure))
                    {
                        UnityEngine.Object.DestroyImmediate(instance);
                        instance = null;
                        return ASMLiteLifecycleTransactionResult.Fail(
                            operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                            failedStage: ASMLiteLifecycleTransactionStage.Verify,
                            beforeState: beforeState,
                            afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                            rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                            rollbackAttempted: true,
                            rollbackSucceeded: true,
                            contextPath: avatar.gameObject.name,
                            remediation: "Repair install-path routing before finalizing detached recovery.",
                            message: finalizeRoutingFailure,
                            migrationOutcomeReport: migrationOutcome,
                            cleanupAttempted: true,
                            cleanupSucceeded: true,
                            reattachAttempted: true,
                            reattachSucceeded: false,
                            installPathAdoptionAttempted: installPathAdoptionAttempted,
                            installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                            recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                    }
                }

                string verifyFailureMessage = string.Empty;
                string verifyFailureContext = string.Empty;
                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify)
                    || !ASMLiteLifecycleVerification.VerifyDetachedRecoveryState(component, avatar, out verifyFailureMessage, out verifyFailureContext))
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = null;
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Verify,
                        beforeState: beforeState,
                        afterState: ASMLiteInstallationState.PackageManaged,
                        rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                        rollbackAttempted: true,
                        rollbackSucceeded: true,
                        contextPath: ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify)
                            ? avatar.gameObject.name
                            : verifyFailureContext,
                        remediation: ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify)
                            ? "Disable the detached recovery verification failure injection after validating cleanup behavior."
                            : "Retry detached recovery after restoring package-managed attach/build invariants.",
                        message: ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify)
                            ? "[ASM-Lite] Injected detached recovery verification failure after package-managed reattachment completed."
                            : verifyFailureMessage,
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        installPathAdoptionAttempted: installPathAdoptionAttempted,
                        installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                if (pendingLegacyMoveMenuHelpers > 0)
                {
                    int removedMoveMenuHelpers = ASMLiteMigrationContinuityService.RemoveMatchingMoveMenuHelpers(component, avatar);
                    if (removedMoveMenuHelpers > 0)
                    {
                        adoption = MergeInstallPathAdoptionResult(adoption, default, removedMoveComponents: removedMoveMenuHelpers);
                        migrationOutcome = ASMLiteMigrationContinuityService.CreateOutcomeReport(
                            default,
                            new ASMLiteBuilder.RebuildMigrationReport(0, cleanup, componentMissing: false, avatarDescriptorFound: true),
                            adoption);
                    }
                }

                return ASMLiteLifecycleTransactionResult.Pass(
                    operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                    beforeState: beforeState,
                    afterState: ASMLiteInstallationState.PackageManaged,
                    discoveredParamCount: buildResult,
                    message: $"[ASM-Lite] Detached return-to-package-managed recovery reattached package-managed ASM-Lite for '{avatar.gameObject.name}'.",
                    migrationOutcomeReport: migrationOutcome,
                    cleanupAttempted: true,
                    cleanupSucceeded: true,
                    reattachAttempted: true,
                    reattachSucceeded: true,
                    installPathAdoptionAttempted: installPathAdoptionAttempted,
                    installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                    recoveredState: ASMLiteInstallationState.PackageManaged);
            }
            catch (Exception ex)
            {
                if (instance != null)
                    UnityEngine.Object.DestroyImmediate(instance);

                return ASMLiteLifecycleTransactionResult.Fail(
                    operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                    failedStage: ASMLiteLifecycleTransactionStage.Execute,
                    beforeState: beforeState,
                    afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                    rollbackState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                    rollbackAttempted: reattachAttempted,
                    rollbackSucceeded: true,
                    contextPath: ex.GetType().Name,
                    remediation: ex.Message,
                    message: "[ASM-Lite] Detached return-to-package-managed recovery threw an exception.",
                    migrationOutcomeReport: migrationOutcome,
                    cleanupAttempted: true,
                    cleanupSucceeded: true,
                    reattachAttempted: reattachAttempted,
                    reattachSucceeded: false,
                    installPathAdoptionAttempted: installPathAdoptionAttempted,
                    installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                    recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
            }
        }

        private static ASMLiteMigrationContinuityService.InstallPathAdoptionResult MergeInstallPathAdoptionResult(
            ASMLiteMigrationContinuityService.InstallPathAdoptionResult existing,
            ASMLiteMigrationContinuityService.InstallPathAdoptionResult incoming,
            int removedMoveComponents)
        {
            bool adopted = existing.Adopted || incoming.Adopted;
            string adoptedInstallPrefix = !string.IsNullOrWhiteSpace(incoming.AdoptedInstallPrefix)
                ? incoming.AdoptedInstallPrefix
                : existing.AdoptedInstallPrefix;
            int totalRemovedMoveComponents = existing.RemovedMoveComponents + incoming.RemovedMoveComponents + Math.Max(0, removedMoveComponents);
            return new ASMLiteMigrationContinuityService.InstallPathAdoptionResult(adopted, adoptedInstallPrefix, totalRemovedMoveComponents);
        }

        private static ASMLiteLifecycleTransactionResult FailAttachedVendorizeAndRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteInstallationState beforeState,
            ComponentVendorizedStateSnapshot originalComponentState,
            ASMLiteGeneratedAssetMirrorResult mirrorResult,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            int discoveredParamCount,
            ASMLiteBuildDiagnosticResult diagnostic,
            ASMLiteGeneratedAssetMirrorResult mirrorDetail = null,
            ASMLiteInstallationState? expectedAfterFailureState = null)
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
                && ASMLiteLifecycleVerification.VerifyPackageManagedRollbackState(component, avatar, out _, out _);
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
            ASMLiteInstallationState beforeState,
            ComponentVendorizedStateSnapshot originalComponentState,
            string vendorizedDir,
            ASMLiteGeneratedAssetMirrorResult deleteBackupResult,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteBuildDiagnosticResult diagnostic,
            ASMLiteGeneratedAssetMirrorResult mirrorDetail = null,
            ASMLiteInstallationState? expectedAfterFailureState = null)
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
                && ASMLiteLifecycleVerification.VerifyVendorizedRollbackState(component, avatar, vendorizedDir, out _, out _);
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

        private static ASMLiteLifecycleTransactionResult FailDetachAndRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteInstallationState beforeState,
            ASMLiteDirectDeliveryRollbackSnapshot snapshot,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot componentSnapshot,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteBuildDiagnosticResult diagnostic)
        {
            bool rollbackAttempted = true;
            string rollbackFailureContext = string.Empty;
            string rollbackFailureRemediation = string.Empty;
            bool snapshotRestored = snapshot != null && snapshot.TryRestore(avatar, out rollbackFailureContext, out rollbackFailureRemediation);
            if (component != null)
            {
                ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, componentSnapshot);
                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                AssetDatabase.SaveAssets();
            }
            bool rollbackSucceeded = snapshotRestored
                && ASMLiteLifecycleVerification.VerifyAttachedStateAfterDetachRollback(component, avatar, beforeState, out _, out _);
            var rollbackState = ResolveToolState(avatar, component);
            string effectiveContext = string.IsNullOrWhiteSpace(rollbackFailureContext) ? contextPath : rollbackFailureContext;
            string effectiveRemediation = string.IsNullOrWhiteSpace(rollbackFailureRemediation) ? remediation : rollbackFailureRemediation;

            return ASMLiteLifecycleTransactionResult.Fail(
                operation: ASMLiteLifecycleOperation.DetachToDirectDelivery,
                failedStage: failedStage,
                beforeState: beforeState,
                afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                rollbackState: rollbackState,
                rollbackAttempted: rollbackAttempted,
                rollbackSucceeded: rollbackSucceeded,
                contextPath: effectiveContext,
                remediation: effectiveRemediation,
                message: message,
                diagnostic: BuildRollbackDiagnostic(diagnostic, rollbackSucceeded ? ASMLiteBuildDiagnosticResult.Pass() : ASMLiteBuildDiagnosticResult.Fail(string.Empty, effectiveContext, effectiveRemediation, message), rollbackSucceeded),
                recoveredState: ResolveDetachedBaselineState(beforeState));
        }

        private static ASMLiteLifecycleTransactionResult FailVendorizeAndDetachAndRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteInstallationState beforeState,
            ASMLiteDirectDeliveryRollbackSnapshot snapshot,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot componentSnapshot,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteBuildDiagnosticResult diagnostic)
        {
            bool rollbackAttempted = true;
            bool rollbackSucceeded;
            ASMLiteInstallationState rollbackState;
            ASMLiteBuildDiagnosticResult rollbackDiagnostic;
            ASMLiteGeneratedAssetMirrorResult rollbackMirrorResult = null;

            if (beforeState == ASMLiteInstallationState.PackageManaged)
            {
                var rollbackResult = ExecuteAttachedReturnToPackageManaged(component, avatar).WithOperation(ASMLiteLifecycleOperation.VendorizeAndDetach);
                if (component != null)
                {
                    ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, componentSnapshot);
                    EditorUtility.SetDirty(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    AssetDatabase.SaveAssets();
                }
                rollbackSucceeded = rollbackResult.Success;
                rollbackState = rollbackSucceeded ? rollbackResult.AfterState : rollbackResult.RollbackState;
                rollbackDiagnostic = rollbackResult.Diagnostic;
                rollbackMirrorResult = rollbackResult.MirrorResult;
            }
            else
            {
                string rollbackFailureContext = string.Empty;
                string rollbackFailureRemediation = string.Empty;
                bool snapshotRestored = snapshot != null && snapshot.TryRestore(avatar, out rollbackFailureContext, out rollbackFailureRemediation);
                if (component != null)
                {
                    ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, componentSnapshot);
                    EditorUtility.SetDirty(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    AssetDatabase.SaveAssets();
                }
                rollbackSucceeded = snapshotRestored
                    && ASMLiteLifecycleVerification.VerifyAttachedStateAfterDetachRollback(component, avatar, beforeState, out _, out _);
                rollbackState = ResolveToolState(avatar, component);
                rollbackDiagnostic = rollbackSucceeded
                    ? ASMLiteBuildDiagnosticResult.Pass()
                    : ASMLiteBuildDiagnosticResult.Fail(string.Empty, rollbackFailureContext, rollbackFailureRemediation, message);
            }

            return ASMLiteLifecycleTransactionResult.Fail(
                operation: ASMLiteLifecycleOperation.VendorizeAndDetach,
                failedStage: failedStage,
                beforeState: beforeState,
                afterState: ASMLiteWindow.GetAsmLiteToolState(avatar, null),
                rollbackState: rollbackState,
                rollbackAttempted: rollbackAttempted,
                rollbackSucceeded: rollbackSucceeded,
                contextPath: contextPath,
                remediation: remediation,
                message: message,
                diagnostic: BuildRollbackDiagnostic(diagnostic, rollbackDiagnostic, rollbackSucceeded),
                mirrorResult: rollbackMirrorResult,
                recoveredState: ResolveDetachedBaselineState(beforeState));
        }

        private static ASMLiteInstallationState ResolveDetachSuccessState(ASMLiteInstallationState beforeState, bool vendorizeToAssets)
        {
            if (vendorizeToAssets || beforeState == ASMLiteInstallationState.Vendorized)
                return ASMLiteInstallationState.Vendorized;

            return ASMLiteInstallationState.Detached;
        }

        private static ASMLiteInstallationState ResolveDetachedBaselineState(ASMLiteInstallationState beforeState)
        {
            return beforeState == ASMLiteInstallationState.Vendorized
                ? ASMLiteInstallationState.Vendorized
                : ASMLiteInstallationState.NotInstalled;
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

        private static ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot CreatePackageManagedCustomizationSnapshot(
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot snapshot)
        {
            return new ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot(
                snapshot.SlotCount,
                snapshot.IconMode,
                snapshot.SelectedGearIndex,
                snapshot.ActionIconMode,
                snapshot.CustomSaveIcon,
                snapshot.CustomLoadIcon,
                snapshot.CustomClearIcon,
                snapshot.UseCustomSlotIcons,
                snapshot.CustomIcons,
                snapshot.UseCustomRootIcon,
                snapshot.CustomRootIcon,
                snapshot.UseCustomRootName,
                snapshot.CustomRootName,
                snapshot.CustomPresetNames,
                snapshot.CustomPresetNameFormat,
                snapshot.CustomSaveLabel,
                snapshot.CustomLoadLabel,
                snapshot.CustomClearPresetLabel,
                snapshot.CustomConfirmLabel,
                snapshot.UseCustomInstallPath,
                snapshot.CustomInstallPath,
                snapshot.UseParameterExclusions,
                snapshot.ExcludedParameterNames,
                useVendorizedGeneratedAssets: false,
                vendorizedGeneratedAssetsPath: string.Empty);
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
            PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static ASMLiteInstallationState ResolveToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
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
