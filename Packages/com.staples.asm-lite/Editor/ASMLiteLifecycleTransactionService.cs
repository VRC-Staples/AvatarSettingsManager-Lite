using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

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

            if (beforeState != ASMLiteWindow.AsmLiteToolState.PackageManaged
                && beforeState != ASMLiteWindow.AsmLiteToolState.Vendorized)
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

            var snapshot = CaptureDirectDeliveryRollbackSnapshot(avatar);
            var componentSnapshot = ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(component);
            try
            {
                if (!ASMLiteBuilder.TryDetachToDirectDelivery(component, out string detail))
                {
                    return FailDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        detail,
                        avatar.gameObject.name,
                        "Restore the attached ASM-Lite state before retrying detach.",
                        ASMLiteLifecycleTransactionStage.Execute,
                        diagnostic: null);
                }

                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterDetachExecute))
                {
                    return FailDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        "[ASM-Lite] Injected detach failure after direct-delivery content was applied but before destroy-time verification completed.",
                        avatar.gameObject.name,
                        "Disable the detach failure injection after validating rollback behavior.",
                        ASMLiteLifecycleTransactionStage.Execute,
                        diagnostic: null);
                }

                string verifyFailureMessage = string.Empty;
                string verifyFailureContext = string.Empty;
                ASMLiteWindow.AsmLiteToolState expectedDetachedState = ResolveDetachSuccessState(beforeState, vendorizeToAssets: false);
                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify)
                    || !VerifyDirectDeliveryState(avatar, expectedDetachedState, NormalizeOptionalPath(component.vendorizedGeneratedAssetsPath), out verifyFailureMessage, out verifyFailureContext))
                {
                    return FailDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify)
                            ? "[ASM-Lite] Injected detach verification failure after direct-delivery content was applied."
                            : verifyFailureMessage,
                        ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify)
                            ? avatar.gameObject.name
                            : verifyFailureContext,
                        ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify)
                            ? "Disable the detach verification failure injection after validating rollback behavior."
                            : "Restore the attached ASM-Lite state before retrying detach.",
                        ASMLiteLifecycleTransactionStage.Verify,
                        diagnostic: null);
                }

                return ASMLiteLifecycleTransactionResult.Pass(
                    operation: ASMLiteLifecycleOperation.DetachToDirectDelivery,
                    beforeState: beforeState,
                    afterState: expectedDetachedState,
                    discoveredParamCount: -1,
                    message: $"[ASM-Lite] {detail}");
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

            if (beforeState != ASMLiteWindow.AsmLiteToolState.PackageManaged
                && beforeState != ASMLiteWindow.AsmLiteToolState.Vendorized)
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
            if (beforeState == ASMLiteWindow.AsmLiteToolState.PackageManaged)
            {
                var vendorizeResult = ExecuteAttachedVendorize(component, avatar).WithOperation(
                    ASMLiteLifecycleOperation.VendorizeAndDetach,
                    "[ASM-Lite] Vendorize + detach could not complete because the attached vendorize stage failed.");
                if (!vendorizeResult.Success)
                    return vendorizeResult;
            }

            var snapshot = beforeState == ASMLiteWindow.AsmLiteToolState.Vendorized
                ? CaptureDirectDeliveryRollbackSnapshot(avatar)
                : null;
            try
            {
                if (!ASMLiteBuilder.TryDetachToDirectDelivery(component, out string detail))
                {
                    return FailVendorizeAndDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        detail,
                        avatar.gameObject.name,
                        "Restore the attached ASM-Lite state before retrying vendorize + detach.",
                        ASMLiteLifecycleTransactionStage.Execute,
                        diagnostic: null);
                }

                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterDetachExecute))
                {
                    return FailVendorizeAndDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        "[ASM-Lite] Injected vendorize + detach failure after direct-delivery content was applied but before destroy-time verification completed.",
                        avatar.gameObject.name,
                        "Disable the vendorize + detach failure injection after validating rollback behavior.",
                        ASMLiteLifecycleTransactionStage.Execute,
                        diagnostic: null);
                }

                string verifyFailureMessage = string.Empty;
                string verifyFailureContext = string.Empty;
                string vendorizedDir = NormalizeOptionalPath(component.vendorizedGeneratedAssetsPath);
                ASMLiteWindow.AsmLiteToolState expectedDetachedState = ResolveDetachSuccessState(beforeState, vendorizeToAssets: false);
                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeDetachVerify)
                    || !VerifyDirectDeliveryState(avatar, expectedDetachedState, vendorizedDir, out verifyFailureMessage, out verifyFailureContext))
                {
                    return FailVendorizeAndDetachAndRollback(
                        component,
                        avatar,
                        beforeState,
                        snapshot,
                        componentSnapshot,
                        ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeDetachVerify)
                            ? "[ASM-Lite] Injected vendorize + detach verification failure after direct-delivery content was applied to vendorized assets."
                            : verifyFailureMessage,
                        ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeDetachVerify)
                            ? vendorizedDir
                            : verifyFailureContext,
                        ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeDetachVerify)
                            ? "Disable the vendorize + detach verification failure injection after validating rollback behavior."
                            : "Restore the attached ASM-Lite state before retrying vendorize + detach.",
                        ASMLiteLifecycleTransactionStage.Verify,
                        diagnostic: null);
                }

                return ASMLiteLifecycleTransactionResult.Pass(
                    operation: ASMLiteLifecycleOperation.VendorizeAndDetach,
                    beforeState: beforeState,
                    afterState: expectedDetachedState,
                    discoveredParamCount: -1,
                    message: $"[ASM-Lite] {detail}");
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

            if (beforeState != ASMLiteWindow.AsmLiteToolState.Detached
                && beforeState != ASMLiteWindow.AsmLiteToolState.Vendorized)
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

            GameObject instance = null;
            bool reattachAttempted = false;
            bool reattachSucceeded = false;
            bool installPathAdoptionAttempted = false;
            bool installPathAdoptionSucceeded = false;
            ASMLiteMigrationContinuityService.InstallPathAdoptionResult adoption = default;

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

                ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, pendingSnapshot);
                if (resolvedDetachedInstallPath
                    && (!pendingSnapshot.UseCustomInstallPath || string.IsNullOrWhiteSpace(pendingSnapshot.CustomInstallPath)))
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

                var adoptedInstallPath = ASMLiteMigrationContinuityService.TryAdoptInstallPathFromMoveMenu(component, avatar);
                if (adoptedInstallPath.Adopted || adoptedInstallPath.RemovedMoveComponents > 0)
                {
                    adoption = adoptedInstallPath;
                    installPathAdoptionAttempted = true;
                    installPathAdoptionSucceeded = adoptedInstallPath.Adopted;
                    migrationOutcome = ASMLiteMigrationContinuityService.CreateOutcomeReport(
                        default,
                        new ASMLiteBuilder.RebuildMigrationReport(0, cleanup, componentMissing: false, avatarDescriptorFound: true),
                        adoption);
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

                int buildResult = ASMLiteBuilder.Build(component);
                if (buildResult < 0)
                {
                    var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
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
                        contextPath: diagnostic?.ContextPath ?? ASMLiteAssetPaths.GeneratedDir,
                        remediation: diagnostic?.Remediation ?? "Fix the build diagnostic before retrying detached recovery.",
                        message: diagnostic?.Message ?? "[ASM-Lite] Detached return-to-package-managed recovery failed because Build() did not succeed.",
                        diagnostic: diagnostic,
                        migrationOutcomeReport: migrationOutcome,
                        cleanupAttempted: true,
                        cleanupSucceeded: true,
                        reattachAttempted: true,
                        reattachSucceeded: false,
                        installPathAdoptionAttempted: installPathAdoptionAttempted,
                        installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                        recoveredState: ASMLiteWindow.GetAsmLiteToolState(avatar, null));
                }

                if (!adoption.Adopted && pendingSnapshot.UseCustomInstallPath)
                {
                    ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, pendingSnapshot);
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

                reattachSucceeded = true;
                string verifyFailureMessage = string.Empty;
                string verifyFailureContext = string.Empty;
                if (ShouldFailForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify)
                    || !VerifyDetachedRecoveryState(component, avatar, out verifyFailureMessage, out verifyFailureContext))
                {
                    UnityEngine.Object.DestroyImmediate(instance);
                    instance = null;
                    return ASMLiteLifecycleTransactionResult.Fail(
                        operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                        failedStage: ASMLiteLifecycleTransactionStage.Verify,
                        beforeState: beforeState,
                        afterState: ASMLiteWindow.AsmLiteToolState.PackageManaged,
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

                return ASMLiteLifecycleTransactionResult.Pass(
                    operation: ASMLiteLifecycleOperation.DetachedReturnToPackageManagedRecovery,
                    beforeState: beforeState,
                    afterState: ASMLiteWindow.AsmLiteToolState.PackageManaged,
                    discoveredParamCount: buildResult,
                    message: $"[ASM-Lite] Detached return-to-package-managed recovery reattached package-managed ASM-Lite for '{avatar.gameObject.name}'.",
                    migrationOutcomeReport: migrationOutcome,
                    cleanupAttempted: true,
                    cleanupSucceeded: true,
                    reattachAttempted: true,
                    reattachSucceeded: true,
                    installPathAdoptionAttempted: installPathAdoptionAttempted,
                    installPathAdoptionSucceeded: installPathAdoptionSucceeded,
                    recoveredState: ASMLiteWindow.AsmLiteToolState.PackageManaged);
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

        private static ASMLiteLifecycleTransactionResult FailDetachAndRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteWindow.AsmLiteToolState beforeState,
            DirectDeliveryRollbackSnapshot snapshot,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot componentSnapshot,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteBuildDiagnosticResult diagnostic)
        {
            bool rollbackAttempted = true;
            bool snapshotRestored = RestoreDirectDeliveryRollbackSnapshot(avatar, snapshot, out string rollbackFailureContext, out string rollbackFailureRemediation);
            if (component != null)
            {
                ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, componentSnapshot);
                EditorUtility.SetDirty(component);
                PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                AssetDatabase.SaveAssets();
            }
            bool rollbackSucceeded = snapshotRestored
                && VerifyAttachedStateAfterDetachRollback(component, avatar, beforeState, out _, out _);
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
            ASMLiteWindow.AsmLiteToolState beforeState,
            DirectDeliveryRollbackSnapshot snapshot,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot componentSnapshot,
            string message,
            string contextPath,
            string remediation,
            ASMLiteLifecycleTransactionStage failedStage,
            ASMLiteBuildDiagnosticResult diagnostic)
        {
            bool rollbackAttempted = true;
            bool rollbackSucceeded;
            ASMLiteWindow.AsmLiteToolState rollbackState;
            ASMLiteBuildDiagnosticResult rollbackDiagnostic;
            ASMLiteGeneratedAssetMirrorResult rollbackMirrorResult = null;

            if (beforeState == ASMLiteWindow.AsmLiteToolState.PackageManaged)
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
                bool snapshotRestored = RestoreDirectDeliveryRollbackSnapshot(avatar, snapshot, out string rollbackFailureContext, out string rollbackFailureRemediation);
                if (component != null)
                {
                    ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, componentSnapshot);
                    EditorUtility.SetDirty(component);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    AssetDatabase.SaveAssets();
                }
                rollbackSucceeded = snapshotRestored
                    && VerifyAttachedStateAfterDetachRollback(component, avatar, beforeState, out _, out _);
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

        private static bool VerifyDirectDeliveryState(
            VRCAvatarDescriptor avatar,
            ASMLiteWindow.AsmLiteToolState expectedDetachedState,
            string vendorizedDir,
            out string failureMessage,
            out string failureContext)
        {
            if (avatar == null)
            {
                failureMessage = "[ASM-Lite] Detach verification failed because the avatar descriptor was missing.";
                failureContext = "avatar";
                return false;
            }

            if (!HasAsmLiteRuntimeMarkers(avatar))
            {
                failureMessage = "[ASM-Lite] Detach verification failed because ASM-Lite runtime markers were not present after direct delivery.";
                failureContext = avatar.gameObject.name;
                return false;
            }

            if (expectedDetachedState == ASMLiteWindow.AsmLiteToolState.Vendorized)
            {
                string normalizedVendorizedDir = NormalizeOptionalPath(vendorizedDir);
                if (string.IsNullOrWhiteSpace(normalizedVendorizedDir) || !AssetDatabase.IsValidFolder(normalizedVendorizedDir))
                {
                    failureMessage = "[ASM-Lite] Vendorize + detach verification failed because the vendorized generated-assets folder was missing.";
                    failureContext = normalizedVendorizedDir;
                    return false;
                }

                if (!HasAvatarGeneratedReferencesUnderPrefix(avatar, normalizedVendorizedDir))
                {
                    failureMessage = "[ASM-Lite] Vendorize + detach verification failed because descriptor-level generated assets were not routed through the vendorized folder after direct delivery.";
                    failureContext = normalizedVendorizedDir;
                    return false;
                }
            }

            var detachedState = ASMLiteWindow.GetAsmLiteToolState(avatar, null);
            if (detachedState != expectedDetachedState)
            {
                failureMessage = $"[ASM-Lite] Detach verification failed because tool-state classification did not resolve to {expectedDetachedState} after direct delivery.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static bool VerifyAttachedStateAfterDetachRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteWindow.AsmLiteToolState beforeState,
            out string failureMessage,
            out string failureContext)
        {
            if (beforeState == ASMLiteWindow.AsmLiteToolState.Vendorized)
            {
                string expectedVendorizedPath = NormalizeOptionalPath(component != null ? component.vendorizedGeneratedAssetsPath : string.Empty);
                if (!VerifyComponentVendorizedState(component, expectedUseVendorized: true, expectedPath: expectedVendorizedPath, out failureMessage, out failureContext))
                    return false;

                if (ASMLiteWindow.GetAsmLiteToolState(avatar, null) != ASMLiteWindow.AsmLiteToolState.Vendorized)
                {
                    failureMessage = "[ASM-Lite] Detach rollback failed because the detached avatar state no longer resolved to Vendorized after restoring the attached vendorized baseline.";
                    failureContext = "toolState";
                    return false;
                }

                failureMessage = string.Empty;
                failureContext = string.Empty;
                return true;
            }

            if (!VerifyComponentVendorizedState(component, expectedUseVendorized: false, expectedPath: string.Empty, out failureMessage, out failureContext))
                return false;

            if (ASMLiteWindow.GetAsmLiteToolState(avatar, null) != ASMLiteWindow.AsmLiteToolState.NotInstalled)
            {
                failureMessage = "[ASM-Lite] Detach rollback failed because detached runtime markers still remained on the avatar after restoring the attached package-managed baseline.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static bool VerifyDetachedRecoveryState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            out string failureMessage,
            out string failureContext)
        {
            if (!VerifyComponentVendorizedState(component, expectedUseVendorized: false, expectedPath: string.Empty, out failureMessage, out failureContext))
                return false;

            if (!VerifyLiveFullControllerReferencesUnderPrefix(component, ASMLiteAssetPaths.GeneratedDir, out failureMessage, out failureContext))
                return false;

            if (ResolveToolState(avatar, component) != ASMLiteWindow.AsmLiteToolState.PackageManaged)
            {
                failureMessage = "[ASM-Lite] Detached recovery verification failed because the reattached avatar did not resolve to PackageManaged tool state.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        private static DirectDeliveryRollbackSnapshot CaptureDirectDeliveryRollbackSnapshot(VRCAvatarDescriptor avatar)
        {
            return new DirectDeliveryRollbackSnapshot(avatar);
        }

        private static bool RestoreDirectDeliveryRollbackSnapshot(
            VRCAvatarDescriptor avatar,
            DirectDeliveryRollbackSnapshot snapshot,
            out string failureContext,
            out string failureRemediation)
        {
            failureContext = string.Empty;
            failureRemediation = string.Empty;
            if (avatar == null || snapshot == null)
            {
                failureContext = avatar == null ? "avatar" : "snapshot";
                failureRemediation = "Capture a valid direct-delivery rollback snapshot before attempting detach rollback.";
                return false;
            }

            try
            {
                snapshot.Restore(avatar);
                return true;
            }
            catch (Exception ex)
            {
                failureContext = ex.GetType().Name;
                failureRemediation = ex.Message;
                return false;
            }
        }

        private static ASMLiteWindow.AsmLiteToolState ResolveDetachSuccessState(ASMLiteWindow.AsmLiteToolState beforeState, bool vendorizeToAssets)
        {
            if (vendorizeToAssets || beforeState == ASMLiteWindow.AsmLiteToolState.Vendorized)
                return ASMLiteWindow.AsmLiteToolState.Vendorized;

            return ASMLiteWindow.AsmLiteToolState.Detached;
        }

        private static ASMLiteWindow.AsmLiteToolState ResolveDetachedBaselineState(ASMLiteWindow.AsmLiteToolState beforeState)
        {
            return beforeState == ASMLiteWindow.AsmLiteToolState.Vendorized
                ? ASMLiteWindow.AsmLiteToolState.Vendorized
                : ASMLiteWindow.AsmLiteToolState.NotInstalled;
        }

        private static bool HasAsmLiteRuntimeMarkers(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            var expr = avatar.expressionParameters;
            if (expr?.parameters != null)
            {
                for (int i = 0; i < expr.parameters.Length; i++)
                {
                    var parameter = expr.parameters[i];
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.name))
                        continue;
                    if (parameter.name.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(parameter.name, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var controller = avatar.baseAnimationLayers[i].animatorController as AnimatorController;
                if (controller == null)
                    continue;

                for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
                {
                    if (controller.layers[layerIndex].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        return true;
                }

                for (int parameterIndex = 0; parameterIndex < controller.parameters.Length; parameterIndex++)
                {
                    string parameterName = controller.parameters[parameterIndex].name;
                    if (string.IsNullOrWhiteSpace(parameterName))
                        continue;
                    if (parameterName.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(parameterName, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            if (avatar.expressionsMenu?.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    if (string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal))
                        return true;

                    string subPath = control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/') : string.Empty;
                    if (!string.IsNullOrWhiteSpace(subPath)
                        && (subPath.IndexOf("ASMLite_", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/ASM-Lite/", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/com.staples.asm-lite/", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        return true;
                    }
                }
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

        private sealed class DirectDeliveryRollbackSnapshot : IDisposable
        {
            private readonly VRCExpressionParameters _expressionParametersAsset;
            private readonly VRCExpressionParameters _expressionParametersClone;
            private readonly VRCExpressionsMenu _expressionsMenuAsset;
            private readonly VRCExpressionsMenu _expressionsMenuClone;
            private readonly AnimatorController _fxControllerAsset;
            private readonly AnimatorController _fxControllerClone;
            private readonly int _fxLayerIndex;
            private readonly VRCAvatarDescriptor.CustomAnimLayer _originalFxLayer;
            private bool _disposed;

            internal DirectDeliveryRollbackSnapshot(VRCAvatarDescriptor avatar)
            {
                _expressionParametersAsset = avatar != null ? avatar.expressionParameters : null;
                _expressionParametersClone = CloneForRollback(_expressionParametersAsset);
                _expressionsMenuAsset = avatar != null ? avatar.expressionsMenu : null;
                _expressionsMenuClone = CloneForRollback(_expressionsMenuAsset);
                _fxLayerIndex = FindFxLayerIndex(avatar);
                if (_fxLayerIndex >= 0 && avatar != null)
                {
                    _originalFxLayer = avatar.baseAnimationLayers[_fxLayerIndex];
                    _fxControllerAsset = _originalFxLayer.animatorController as AnimatorController;
                    _fxControllerClone = CloneForRollback(_fxControllerAsset);
                }
                else
                {
                    _originalFxLayer = default;
                }
            }

            internal void Restore(VRCAvatarDescriptor avatar)
            {
                if (avatar == null)
                    return;

                if (_expressionParametersAsset != null && _expressionParametersClone != null)
                {
                    EditorUtility.CopySerialized(_expressionParametersClone, _expressionParametersAsset);
                    if (avatar.expressionParameters != _expressionParametersAsset)
                        avatar.expressionParameters = _expressionParametersAsset;
                    EditorUtility.SetDirty(_expressionParametersAsset);
                }

                if (_expressionsMenuAsset != null && _expressionsMenuClone != null)
                {
                    EditorUtility.CopySerialized(_expressionsMenuClone, _expressionsMenuAsset);
                    if (avatar.expressionsMenu != _expressionsMenuAsset)
                        avatar.expressionsMenu = _expressionsMenuAsset;
                    EditorUtility.SetDirty(_expressionsMenuAsset);
                }

                if (_fxLayerIndex >= 0 && _fxLayerIndex < avatar.baseAnimationLayers.Length)
                {
                    if (_fxControllerAsset != null && _fxControllerClone != null)
                    {
                        EditorUtility.CopySerialized(_fxControllerClone, _fxControllerAsset);
                        EditorUtility.SetDirty(_fxControllerAsset);
                    }

                    var restoredLayer = _originalFxLayer;
                    restoredLayer.animatorController = _originalFxLayer.animatorController;
                    avatar.baseAnimationLayers[_fxLayerIndex] = restoredLayer;
                }

                EditorUtility.SetDirty(avatar);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                if (_expressionParametersClone != null)
                    UnityEngine.Object.DestroyImmediate(_expressionParametersClone);
                if (_expressionsMenuClone != null)
                    UnityEngine.Object.DestroyImmediate(_expressionsMenuClone);
                if (_fxControllerClone != null)
                    UnityEngine.Object.DestroyImmediate(_fxControllerClone);
            }

            private static T CloneForRollback<T>(T source)
                where T : UnityEngine.Object
            {
                if (source == null)
                    return null;

                var clone = UnityEngine.Object.Instantiate(source);
                clone.hideFlags = HideFlags.HideAndDontSave;
                return clone;
            }

            private static int FindFxLayerIndex(VRCAvatarDescriptor avatar)
            {
                if (avatar == null || avatar.baseAnimationLayers == null)
                    return -1;

                for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
                {
                    if (avatar.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                        return i;
                }

                return -1;
            }
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
