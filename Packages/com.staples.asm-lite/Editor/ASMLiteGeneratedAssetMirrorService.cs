using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    internal enum ASMLiteGeneratedAssetMirrorOperation
    {
        None = 0,
        StageVendorizedMirror = 1,
        RetargetAvatarToVendorized = 2,
        RestoreAvatarToPackageManaged = 3,
        RestoreAvatarToVendorized = 4,
        BackupVendorizedFolderForDelete = 5,
        FinalizeVendorizedMirror = 6,
        FinalizeVendorizedFolderDelete = 7,
        RollbackVendorizedMirror = 8,
        RollbackVendorizedFolderDelete = 9,
    }

    internal enum ASMLiteGeneratedAssetMirrorStage
    {
        None = 0,
        Preflight = 1,
        Execute = 2,
        Verify = 3,
        Rollback = 4,
    }

    internal enum ASMLiteGeneratedAssetMirrorTestFailurePoint
    {
        None = 0,
        AfterStagedCopy = 1,
        DuringVendorizedFolderDelete = 2,
    }

    internal sealed class ASMLiteGeneratedAssetMirrorResult
    {
        internal ASMLiteGeneratedAssetMirrorResult(
            bool success,
            ASMLiteGeneratedAssetMirrorOperation operation,
            ASMLiteGeneratedAssetMirrorStage failedStage,
            bool rollbackAttempted,
            bool rollbackSucceeded,
            string sourcePath,
            string targetPath,
            string stagingPath,
            string backupPath,
            int assetCount,
            string message,
            string contextPath,
            string remediation)
        {
            Success = success;
            Operation = operation;
            FailedStage = failedStage;
            RollbackAttempted = rollbackAttempted;
            RollbackSucceeded = rollbackSucceeded;
            SourcePath = sourcePath ?? string.Empty;
            TargetPath = targetPath ?? string.Empty;
            StagingPath = stagingPath ?? string.Empty;
            BackupPath = backupPath ?? string.Empty;
            AssetCount = assetCount;
            Message = message ?? string.Empty;
            ContextPath = contextPath ?? string.Empty;
            Remediation = remediation ?? string.Empty;
        }

        internal bool Success { get; }
        internal ASMLiteGeneratedAssetMirrorOperation Operation { get; }
        internal ASMLiteGeneratedAssetMirrorStage FailedStage { get; }
        internal bool RollbackAttempted { get; }
        internal bool RollbackSucceeded { get; }
        internal string SourcePath { get; }
        internal string TargetPath { get; }
        internal string StagingPath { get; }
        internal string BackupPath { get; }
        internal int AssetCount { get; }
        internal string Message { get; }
        internal string ContextPath { get; }
        internal string Remediation { get; }

        internal static ASMLiteGeneratedAssetMirrorResult Pass(
            ASMLiteGeneratedAssetMirrorOperation operation,
            string sourcePath,
            string targetPath,
            string stagingPath,
            string backupPath,
            int assetCount,
            string message)
        {
            return new ASMLiteGeneratedAssetMirrorResult(
                success: true,
                operation: operation,
                failedStage: ASMLiteGeneratedAssetMirrorStage.None,
                rollbackAttempted: false,
                rollbackSucceeded: false,
                sourcePath: sourcePath,
                targetPath: targetPath,
                stagingPath: stagingPath,
                backupPath: backupPath,
                assetCount: assetCount,
                message: message,
                contextPath: string.Empty,
                remediation: string.Empty);
        }

        internal static ASMLiteGeneratedAssetMirrorResult Fail(
            ASMLiteGeneratedAssetMirrorOperation operation,
            ASMLiteGeneratedAssetMirrorStage failedStage,
            string sourcePath,
            string targetPath,
            string stagingPath,
            string backupPath,
            int assetCount,
            string message,
            string contextPath,
            string remediation,
            bool rollbackAttempted = false,
            bool rollbackSucceeded = false)
        {
            return new ASMLiteGeneratedAssetMirrorResult(
                success: false,
                operation: operation,
                failedStage: failedStage,
                rollbackAttempted: rollbackAttempted,
                rollbackSucceeded: rollbackSucceeded,
                sourcePath: sourcePath,
                targetPath: targetPath,
                stagingPath: stagingPath,
                backupPath: backupPath,
                assetCount: assetCount,
                message: message,
                contextPath: contextPath,
                remediation: remediation);
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
            return log;
        }
    }

    internal static class ASMLiteGeneratedAssetMirrorService
    {
        private const string VendorizedRoot = "Assets/ASM-Lite";
        private static ASMLiteGeneratedAssetMirrorTestFailurePoint s_testFailurePoint;

        internal static IDisposable PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint failurePoint)
        {
            var previous = s_testFailurePoint;
            s_testFailurePoint = failurePoint;
            return new ScopedFailurePoint(() => s_testFailurePoint = previous);
        }

        internal static ASMLiteGeneratedAssetMirrorResult StageVendorizedMirror(VRCAvatarDescriptor avatar)
        {
            string sourcePrefix = NormalizeAssetPath(ASMLiteAssetPaths.GeneratedDir);
            if (avatar == null)
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Preflight,
                    sourcePath: sourcePrefix,
                    targetPath: string.Empty,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: "[ASM-Lite] Vendorized mirror staging failed because the avatar descriptor was null.",
                    contextPath: "avatar",
                    remediation: "Pass a valid avatar descriptor before mirroring generated assets.");
            }

            string avatarFolder = EnsureVendorizeAvatarFolder(avatar);
            string targetDir = NormalizeAssetPath(avatarFolder + "/GeneratedAssets");
            string stagingDir = CreateUniqueFolder(avatarFolder, "GeneratedAssets.__stage__");
            string backupDir = string.Empty;
            int copiedAssetCount = 0;

            try
            {
                copiedAssetCount = CopyGeneratedAssetsToFolder(sourcePrefix, stagingDir);
                if (copiedAssetCount <= 0)
                {
                    DeleteAssetIfExists(stagingDir);
                    return ASMLiteGeneratedAssetMirrorResult.Fail(
                        operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                        failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                        sourcePath: sourcePrefix,
                        targetPath: targetDir,
                        stagingPath: stagingDir,
                        backupPath: string.Empty,
                        assetCount: copiedAssetCount,
                        message: "[ASM-Lite] Vendorized mirror staging failed because no generated assets were copied into the staging folder.",
                        contextPath: sourcePrefix,
                        remediation: "Rebuild ASM-Lite generated assets before vendorizing.");
                }

                if (!VerifyGeneratedAssetFolder(stagingDir))
                {
                    DeleteAssetIfExists(stagingDir);
                    return ASMLiteGeneratedAssetMirrorResult.Fail(
                        operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                        failedStage: ASMLiteGeneratedAssetMirrorStage.Verify,
                        sourcePath: sourcePrefix,
                        targetPath: targetDir,
                        stagingPath: stagingDir,
                        backupPath: string.Empty,
                        assetCount: copiedAssetCount,
                        message: "[ASM-Lite] Vendorized mirror staging verification failed before the staged folder was promoted.",
                        contextPath: stagingDir,
                        remediation: "Ensure the staged generated assets load before vendorized promotion.");
                }

                if (ShouldFailForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.AfterStagedCopy))
                {
                    DeleteAssetIfExists(stagingDir);
                    return ASMLiteGeneratedAssetMirrorResult.Fail(
                        operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                        failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                        sourcePath: sourcePrefix,
                        targetPath: targetDir,
                        stagingPath: stagingDir,
                        backupPath: string.Empty,
                        assetCount: copiedAssetCount,
                        message: "[ASM-Lite] Injected staged-copy failure before vendorized mirror promotion.",
                        contextPath: stagingDir,
                        remediation: "Disable the staged-copy failure injection after validating rollback behavior.");
                }

                if (AssetDatabase.IsValidFolder(targetDir))
                {
                    backupDir = CreateUniqueFolderName(avatarFolder, "GeneratedAssets.__backup__");
                    string moveTargetError = AssetDatabase.MoveAsset(targetDir, backupDir);
                    AssetDatabase.Refresh();
                    if (!string.IsNullOrEmpty(moveTargetError))
                    {
                        DeleteAssetIfExists(stagingDir);
                        return ASMLiteGeneratedAssetMirrorResult.Fail(
                            operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                            failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                            sourcePath: sourcePrefix,
                            targetPath: targetDir,
                            stagingPath: stagingDir,
                            backupPath: backupDir,
                            assetCount: copiedAssetCount,
                            message: "[ASM-Lite] Vendorized mirror staging failed while moving the existing generated-assets folder into rollback backup.",
                            contextPath: targetDir,
                            remediation: moveTargetError);
                    }
                }

                string promoteError = AssetDatabase.MoveAsset(stagingDir, targetDir);
                AssetDatabase.Refresh();
                if (!string.IsNullOrEmpty(promoteError) || !VerifyGeneratedAssetFolder(targetDir))
                {
                    bool rollbackSucceeded = RestoreFolderFromBackup(targetDir, backupDir);
                    DeleteAssetIfExists(stagingDir);
                    return ASMLiteGeneratedAssetMirrorResult.Fail(
                        operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                        failedStage: string.IsNullOrEmpty(promoteError)
                            ? ASMLiteGeneratedAssetMirrorStage.Verify
                            : ASMLiteGeneratedAssetMirrorStage.Execute,
                        sourcePath: sourcePrefix,
                        targetPath: targetDir,
                        stagingPath: stagingDir,
                        backupPath: backupDir,
                        assetCount: copiedAssetCount,
                        message: string.IsNullOrEmpty(promoteError)
                            ? "[ASM-Lite] Vendorized mirror verification failed after promoting the staged folder."
                            : "[ASM-Lite] Vendorized mirror promotion failed while replacing the active generated-assets folder.",
                        contextPath: string.IsNullOrEmpty(promoteError) ? targetDir : stagingDir,
                        remediation: string.IsNullOrEmpty(promoteError)
                            ? "Ensure the promoted generated assets load before continuing vendorization."
                            : promoteError,
                        rollbackAttempted: true,
                        rollbackSucceeded: rollbackSucceeded);
                }

                return ASMLiteGeneratedAssetMirrorResult.Pass(
                    operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                    sourcePath: sourcePrefix,
                    targetPath: targetDir,
                    stagingPath: string.Empty,
                    backupPath: backupDir,
                    assetCount: copiedAssetCount,
                    message: $"[ASM-Lite] Staged {copiedAssetCount} generated assets into '{targetDir}' for attached vendorization.");
            }
            catch (Exception ex)
            {
                bool rollbackSucceeded = RestoreFolderFromBackup(targetDir, backupDir);
                DeleteAssetIfExists(stagingDir);
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.StageVendorizedMirror,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                    sourcePath: sourcePrefix,
                    targetPath: targetDir,
                    stagingPath: stagingDir,
                    backupPath: backupDir,
                    assetCount: copiedAssetCount,
                    message: "[ASM-Lite] Vendorized mirror staging threw an exception.",
                    contextPath: ex.GetType().Name,
                    remediation: ex.Message,
                    rollbackAttempted: !string.IsNullOrEmpty(backupDir),
                    rollbackSucceeded: rollbackSucceeded);
            }
        }

        internal static ASMLiteGeneratedAssetMirrorResult RollbackVendorizedMirror(ASMLiteGeneratedAssetMirrorResult mirrorResult)
        {
            if (mirrorResult == null)
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.RollbackVendorizedMirror,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Preflight,
                    sourcePath: string.Empty,
                    targetPath: string.Empty,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: "[ASM-Lite] Vendorized mirror rollback failed because the prior mirror result was null.",
                    contextPath: "mirrorResult",
                    remediation: "Pass the successful mirror-stage result into rollback.");
            }

            bool rollbackSucceeded = RestoreFolderFromBackup(mirrorResult.TargetPath, mirrorResult.BackupPath);
            return rollbackSucceeded
                ? ASMLiteGeneratedAssetMirrorResult.Pass(
                    operation: ASMLiteGeneratedAssetMirrorOperation.RollbackVendorizedMirror,
                    sourcePath: mirrorResult.SourcePath,
                    targetPath: mirrorResult.TargetPath,
                    stagingPath: mirrorResult.StagingPath,
                    backupPath: mirrorResult.BackupPath,
                    assetCount: mirrorResult.AssetCount,
                    message: $"[ASM-Lite] Restored vendorized mirror folder state for '{mirrorResult.TargetPath}'.")
                : ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.RollbackVendorizedMirror,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Rollback,
                    sourcePath: mirrorResult.SourcePath,
                    targetPath: mirrorResult.TargetPath,
                    stagingPath: mirrorResult.StagingPath,
                    backupPath: mirrorResult.BackupPath,
                    assetCount: mirrorResult.AssetCount,
                    message: "[ASM-Lite] Vendorized mirror rollback failed while restoring the prior folder state.",
                    contextPath: mirrorResult.TargetPath,
                    remediation: "Manually restore the generated-assets folder from the rollback backup.",
                    rollbackAttempted: true,
                    rollbackSucceeded: false);
        }

        internal static ASMLiteGeneratedAssetMirrorResult FinalizeVendorizedMirror(ASMLiteGeneratedAssetMirrorResult mirrorResult)
        {
            if (mirrorResult == null || string.IsNullOrWhiteSpace(mirrorResult.BackupPath))
            {
                return ASMLiteGeneratedAssetMirrorResult.Pass(
                    operation: ASMLiteGeneratedAssetMirrorOperation.FinalizeVendorizedMirror,
                    sourcePath: mirrorResult?.SourcePath,
                    targetPath: mirrorResult?.TargetPath,
                    stagingPath: mirrorResult?.StagingPath,
                    backupPath: mirrorResult?.BackupPath,
                    assetCount: mirrorResult?.AssetCount ?? 0,
                    message: "[ASM-Lite] Vendorized mirror finalization had no backup folder to clean up.");
            }

            bool deleted = AssetDatabase.DeleteAsset(mirrorResult.BackupPath);
            AssetDatabase.Refresh();
            if (!deleted)
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.FinalizeVendorizedMirror,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                    sourcePath: mirrorResult.SourcePath,
                    targetPath: mirrorResult.TargetPath,
                    stagingPath: mirrorResult.StagingPath,
                    backupPath: mirrorResult.BackupPath,
                    assetCount: mirrorResult.AssetCount,
                    message: "[ASM-Lite] Vendorized mirror finalization failed while deleting the rollback backup folder.",
                    contextPath: mirrorResult.BackupPath,
                    remediation: "Delete the stale rollback backup folder after verifying the promoted generated assets are correct.");
            }

            DeleteAssetFolderIfEmpty(Path.GetDirectoryName(mirrorResult.BackupPath)?.Replace('\\', '/'));
            DeleteAssetFolderIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(mirrorResult.BackupPath) ?? string.Empty)?.Replace('\\', '/'));
            return ASMLiteGeneratedAssetMirrorResult.Pass(
                operation: ASMLiteGeneratedAssetMirrorOperation.FinalizeVendorizedMirror,
                sourcePath: mirrorResult.SourcePath,
                targetPath: mirrorResult.TargetPath,
                stagingPath: mirrorResult.StagingPath,
                backupPath: mirrorResult.BackupPath,
                assetCount: mirrorResult.AssetCount,
                message: $"[ASM-Lite] Removed rollback backup folder '{mirrorResult.BackupPath}' after successful vendorization.");
        }

        internal static ASMLiteGeneratedAssetMirrorResult RetargetAvatarGeneratedAssetsToVendorized(VRCAvatarDescriptor avatar, string vendorizedDir)
        {
            return RetargetAvatarGeneratedAssets(
                avatar,
                NormalizeAssetPath(ASMLiteAssetPaths.GeneratedDir),
                NormalizeAssetPath(vendorizedDir),
                ASMLiteGeneratedAssetMirrorOperation.RetargetAvatarToVendorized,
                "[ASM-Lite] Failed to retarget avatar generated-asset references to the vendorized mirror folder.");
        }

        internal static ASMLiteGeneratedAssetMirrorResult RestoreAvatarGeneratedAssetsToPackageManaged(VRCAvatarDescriptor avatar, string vendorizedDir)
        {
            return RetargetAvatarGeneratedAssets(
                avatar,
                NormalizeAssetPath(vendorizedDir),
                NormalizeAssetPath(ASMLiteAssetPaths.GeneratedDir),
                ASMLiteGeneratedAssetMirrorOperation.RestoreAvatarToPackageManaged,
                "[ASM-Lite] Failed to restore avatar generated-asset references back to package-managed assets.");
        }

        internal static ASMLiteGeneratedAssetMirrorResult RestoreAvatarGeneratedAssetsToVendorized(VRCAvatarDescriptor avatar, string vendorizedDir)
        {
            return RetargetAvatarGeneratedAssets(
                avatar,
                NormalizeAssetPath(ASMLiteAssetPaths.GeneratedDir),
                NormalizeAssetPath(vendorizedDir),
                ASMLiteGeneratedAssetMirrorOperation.RestoreAvatarToVendorized,
                "[ASM-Lite] Failed to restore avatar generated-asset references back to vendorized assets during rollback.");
        }

        internal static ASMLiteGeneratedAssetMirrorResult BackupVendorizedFolderForDelete(string vendorizedDir)
        {
            string normalizedDir = NormalizeAssetPath(vendorizedDir);
            if (string.IsNullOrWhiteSpace(normalizedDir) || !AssetDatabase.IsValidFolder(normalizedDir))
            {
                return ASMLiteGeneratedAssetMirrorResult.Pass(
                    operation: ASMLiteGeneratedAssetMirrorOperation.BackupVendorizedFolderForDelete,
                    sourcePath: normalizedDir,
                    targetPath: normalizedDir,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: "[ASM-Lite] No vendorized generated-assets folder existed to back up for deletion.");
            }

            if (ShouldFailForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringVendorizedFolderDelete))
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.BackupVendorizedFolderForDelete,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                    sourcePath: normalizedDir,
                    targetPath: normalizedDir,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: "[ASM-Lite] Injected vendorized-folder delete failure before the folder was backed up for deletion.",
                    contextPath: normalizedDir,
                    remediation: "Disable the delete failure injection after validating rollback behavior.");
            }

            string parentFolder = Path.GetDirectoryName(normalizedDir)?.Replace('\\', '/');
            string backupDir = CreateUniqueFolderName(parentFolder, Path.GetFileName(normalizedDir) + ".__delete__");
            string moveError = AssetDatabase.MoveAsset(normalizedDir, backupDir);
            AssetDatabase.Refresh();
            if (!string.IsNullOrEmpty(moveError) || AssetDatabase.IsValidFolder(normalizedDir))
            {
                bool rollbackSucceeded = RestoreMovedFolder(normalizedDir, backupDir);
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.BackupVendorizedFolderForDelete,
                    failedStage: string.IsNullOrEmpty(moveError)
                        ? ASMLiteGeneratedAssetMirrorStage.Verify
                        : ASMLiteGeneratedAssetMirrorStage.Execute,
                    sourcePath: normalizedDir,
                    targetPath: normalizedDir,
                    stagingPath: string.Empty,
                    backupPath: backupDir,
                    assetCount: 0,
                    message: string.IsNullOrEmpty(moveError)
                        ? "[ASM-Lite] Vendorized-folder delete staging failed because the source folder still existed after backup move."
                        : "[ASM-Lite] Vendorized-folder delete staging failed while moving the active folder into rollback backup.",
                    contextPath: normalizedDir,
                    remediation: string.IsNullOrEmpty(moveError)
                        ? "Ensure the vendorized folder can be removed before attached return commits."
                        : moveError,
                    rollbackAttempted: true,
                    rollbackSucceeded: rollbackSucceeded);
            }

            return ASMLiteGeneratedAssetMirrorResult.Pass(
                operation: ASMLiteGeneratedAssetMirrorOperation.BackupVendorizedFolderForDelete,
                sourcePath: normalizedDir,
                targetPath: normalizedDir,
                stagingPath: string.Empty,
                backupPath: backupDir,
                assetCount: 0,
                message: $"[ASM-Lite] Backed up vendorized generated-assets folder '{normalizedDir}' into '{backupDir}' before deletion commit.");
        }

        internal static ASMLiteGeneratedAssetMirrorResult RollbackVendorizedFolderDelete(ASMLiteGeneratedAssetMirrorResult deleteBackupResult)
        {
            if (deleteBackupResult == null)
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.RollbackVendorizedFolderDelete,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Preflight,
                    sourcePath: string.Empty,
                    targetPath: string.Empty,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: "[ASM-Lite] Vendorized-folder delete rollback failed because the prior delete-backup result was null.",
                    contextPath: "deleteBackupResult",
                    remediation: "Pass the successful delete-backup result into rollback.");
            }

            bool rollbackSucceeded = RestoreMovedFolder(deleteBackupResult.TargetPath, deleteBackupResult.BackupPath);
            return rollbackSucceeded
                ? ASMLiteGeneratedAssetMirrorResult.Pass(
                    operation: ASMLiteGeneratedAssetMirrorOperation.RollbackVendorizedFolderDelete,
                    sourcePath: deleteBackupResult.SourcePath,
                    targetPath: deleteBackupResult.TargetPath,
                    stagingPath: deleteBackupResult.StagingPath,
                    backupPath: deleteBackupResult.BackupPath,
                    assetCount: deleteBackupResult.AssetCount,
                    message: $"[ASM-Lite] Restored vendorized generated-assets folder '{deleteBackupResult.TargetPath}' from rollback backup.")
                : ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.RollbackVendorizedFolderDelete,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Rollback,
                    sourcePath: deleteBackupResult.SourcePath,
                    targetPath: deleteBackupResult.TargetPath,
                    stagingPath: deleteBackupResult.StagingPath,
                    backupPath: deleteBackupResult.BackupPath,
                    assetCount: deleteBackupResult.AssetCount,
                    message: "[ASM-Lite] Vendorized-folder delete rollback failed while restoring the backed-up folder.",
                    contextPath: deleteBackupResult.TargetPath,
                    remediation: "Manually restore the vendorized folder from the rollback backup.",
                    rollbackAttempted: true,
                    rollbackSucceeded: false);
        }

        internal static ASMLiteGeneratedAssetMirrorResult FinalizeVendorizedFolderDelete(ASMLiteGeneratedAssetMirrorResult deleteBackupResult)
        {
            if (deleteBackupResult == null || string.IsNullOrWhiteSpace(deleteBackupResult.BackupPath))
            {
                return ASMLiteGeneratedAssetMirrorResult.Pass(
                    operation: ASMLiteGeneratedAssetMirrorOperation.FinalizeVendorizedFolderDelete,
                    sourcePath: deleteBackupResult?.SourcePath,
                    targetPath: deleteBackupResult?.TargetPath,
                    stagingPath: deleteBackupResult?.StagingPath,
                    backupPath: deleteBackupResult?.BackupPath,
                    assetCount: deleteBackupResult?.AssetCount ?? 0,
                    message: "[ASM-Lite] Vendorized-folder delete finalization had no rollback backup to remove.");
            }

            bool deleted = AssetDatabase.DeleteAsset(deleteBackupResult.BackupPath);
            AssetDatabase.Refresh();
            if (!deleted)
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: ASMLiteGeneratedAssetMirrorOperation.FinalizeVendorizedFolderDelete,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                    sourcePath: deleteBackupResult.SourcePath,
                    targetPath: deleteBackupResult.TargetPath,
                    stagingPath: deleteBackupResult.StagingPath,
                    backupPath: deleteBackupResult.BackupPath,
                    assetCount: deleteBackupResult.AssetCount,
                    message: "[ASM-Lite] Vendorized-folder delete finalization failed while deleting the rollback backup folder.",
                    contextPath: deleteBackupResult.BackupPath,
                    remediation: "Delete the stale rollback backup folder after verifying the attached return completed cleanly.");
            }

            DeleteAssetFolderIfEmpty(Path.GetDirectoryName(deleteBackupResult.TargetPath)?.Replace('\\', '/'));
            DeleteAssetFolderIfEmpty(Path.GetDirectoryName(Path.GetDirectoryName(deleteBackupResult.TargetPath) ?? string.Empty)?.Replace('\\', '/'));
            return ASMLiteGeneratedAssetMirrorResult.Pass(
                operation: ASMLiteGeneratedAssetMirrorOperation.FinalizeVendorizedFolderDelete,
                sourcePath: deleteBackupResult.SourcePath,
                targetPath: deleteBackupResult.TargetPath,
                stagingPath: deleteBackupResult.StagingPath,
                backupPath: deleteBackupResult.BackupPath,
                assetCount: deleteBackupResult.AssetCount,
                message: $"[ASM-Lite] Deleted rollback backup folder '{deleteBackupResult.BackupPath}' after attached return completed.");
        }

        private static ASMLiteGeneratedAssetMirrorResult RetargetAvatarGeneratedAssets(
            VRCAvatarDescriptor avatar,
            string sourcePrefix,
            string destinationPrefix,
            ASMLiteGeneratedAssetMirrorOperation operation,
            string failureMessage)
        {
            if (avatar == null)
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: operation,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Preflight,
                    sourcePath: sourcePrefix,
                    targetPath: destinationPrefix,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: failureMessage,
                    contextPath: "avatar",
                    remediation: "Pass a valid avatar descriptor before retargeting generated assets.");
            }

            if (string.IsNullOrWhiteSpace(destinationPrefix))
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: operation,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Preflight,
                    sourcePath: sourcePrefix,
                    targetPath: destinationPrefix,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: failureMessage,
                    contextPath: "destinationPrefix",
                    remediation: "Pass a valid generated-assets destination path before retargeting avatar references.");
            }

            if (avatar.expressionParameters != null)
            {
                string exprPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(avatar.expressionParameters));
                if (PathStartsWith(exprPath, sourcePrefix))
                {
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(destinationPrefix + "/" + Path.GetFileName(exprPath));
                    if (replacement == null)
                    {
                        return ASMLiteGeneratedAssetMirrorResult.Fail(
                            operation: operation,
                            failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                            sourcePath: sourcePrefix,
                            targetPath: destinationPrefix,
                            stagingPath: string.Empty,
                            backupPath: string.Empty,
                            assetCount: 0,
                            message: failureMessage,
                            contextPath: destinationPrefix + "/" + Path.GetFileName(exprPath),
                            remediation: "Ensure the target expression-parameters asset exists before retargeting avatar references.");
                    }

                    avatar.expressionParameters = replacement;
                    EditorUtility.SetDirty(avatar);
                }
            }

            if (avatar.expressionsMenu != null)
            {
                string menuPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(avatar.expressionsMenu));
                if (PathStartsWith(menuPath, sourcePrefix))
                {
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(destinationPrefix + "/" + Path.GetFileName(menuPath));
                    if (replacement == null)
                    {
                        return ASMLiteGeneratedAssetMirrorResult.Fail(
                            operation: operation,
                            failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                            sourcePath: sourcePrefix,
                            targetPath: destinationPrefix,
                            stagingPath: string.Empty,
                            backupPath: string.Empty,
                            assetCount: 0,
                            message: failureMessage,
                            contextPath: destinationPrefix + "/" + Path.GetFileName(menuPath),
                            remediation: "Ensure the target expressions-menu asset exists before retargeting avatar references.");
                    }

                    avatar.expressionsMenu = replacement;
                    EditorUtility.SetDirty(avatar);
                }

                RetargetMenuGeneratedSubmenus(avatar.expressionsMenu, sourcePrefix, destinationPrefix, new HashSet<VRCExpressionsMenu>());
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var layer = avatar.baseAnimationLayers[i];
                var controller = layer.animatorController;
                string controllerPath = NormalizeAssetPath(controller ? AssetDatabase.GetAssetPath(controller) : string.Empty);
                if (!PathStartsWith(controllerPath, sourcePrefix))
                    continue;

                var replacement = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(destinationPrefix + "/" + Path.GetFileName(controllerPath));
                if (replacement == null)
                {
                    return ASMLiteGeneratedAssetMirrorResult.Fail(
                        operation: operation,
                        failedStage: ASMLiteGeneratedAssetMirrorStage.Execute,
                        sourcePath: sourcePrefix,
                        targetPath: destinationPrefix,
                        stagingPath: string.Empty,
                        backupPath: string.Empty,
                        assetCount: 0,
                        message: failureMessage,
                        contextPath: destinationPrefix + "/" + Path.GetFileName(controllerPath),
                        remediation: "Ensure the target FX controller exists before retargeting avatar references.");
                }

                layer.animatorController = replacement;
                avatar.baseAnimationLayers[i] = layer;
                EditorUtility.SetDirty(avatar);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (HasAvatarGeneratedReferencesUnderPrefix(avatar, sourcePrefix))
            {
                return ASMLiteGeneratedAssetMirrorResult.Fail(
                    operation: operation,
                    failedStage: ASMLiteGeneratedAssetMirrorStage.Verify,
                    sourcePath: sourcePrefix,
                    targetPath: destinationPrefix,
                    stagingPath: string.Empty,
                    backupPath: string.Empty,
                    assetCount: 0,
                    message: failureMessage,
                    contextPath: sourcePrefix,
                    remediation: "Verify all descriptor-level generated-asset references moved to the requested target prefix.");
            }

            return ASMLiteGeneratedAssetMirrorResult.Pass(
                operation: operation,
                sourcePath: sourcePrefix,
                targetPath: destinationPrefix,
                stagingPath: string.Empty,
                backupPath: string.Empty,
                assetCount: 0,
                message: $"[ASM-Lite] Retargeted avatar generated-asset references from '{sourcePrefix}' to '{destinationPrefix}'.");
        }

        private static int CopyGeneratedAssetsToFolder(string sourcePrefix, string stagingDir)
        {
            var generatedGuids = AssetDatabase.FindAssets(string.Empty, new[] { sourcePrefix });
            int copiedAssetCount = 0;
            for (int i = 0; i < generatedGuids.Length; i++)
            {
                string sourcePath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(generatedGuids[i]));
                if (string.IsNullOrWhiteSpace(sourcePath) || AssetDatabase.IsValidFolder(sourcePath))
                    continue;

                string destinationPath = stagingDir + "/" + Path.GetFileName(sourcePath);
                if (!AssetDatabase.CopyAsset(sourcePath, destinationPath))
                    throw new InvalidOperationException($"Failed to stage generated asset '{sourcePath}' to '{destinationPath}'.");

                copiedAssetCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return copiedAssetCount;
        }

        private static bool VerifyGeneratedAssetFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                return false;

            return AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController)) != null
                && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu)) != null
                && AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams)) != null;
        }

        private static bool RestoreFolderFromBackup(string targetDir, string backupDir)
        {
            if (!string.IsNullOrWhiteSpace(targetDir) && AssetDatabase.IsValidFolder(targetDir))
                AssetDatabase.DeleteAsset(targetDir);

            bool restored = RestoreMovedFolder(targetDir, backupDir);
            AssetDatabase.Refresh();
            return restored;
        }

        private static bool RestoreMovedFolder(string targetDir, string backupDir)
        {
            if (string.IsNullOrWhiteSpace(backupDir) || !AssetDatabase.IsValidFolder(backupDir))
                return !AssetDatabase.IsValidFolder(targetDir);

            if (AssetDatabase.IsValidFolder(targetDir))
                AssetDatabase.DeleteAsset(targetDir);

            string moveError = AssetDatabase.MoveAsset(backupDir, targetDir);
            AssetDatabase.Refresh();
            return string.IsNullOrEmpty(moveError) && AssetDatabase.IsValidFolder(targetDir);
        }

        private static void RetargetMenuGeneratedSubmenus(VRCExpressionsMenu menu, string sourcePrefix, string destinationPrefix, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control?.subMenu == null)
                    continue;

                string subPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(control.subMenu));
                if (PathStartsWith(subPath, sourcePrefix))
                {
                    string newPath = destinationPrefix + "/" + Path.GetFileName(subPath);
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(newPath);
                    if (replacement != null)
                    {
                        control.subMenu = replacement;
                        menu.controls[i] = control;
                        EditorUtility.SetDirty(menu);
                    }
                }

                RetargetMenuGeneratedSubmenus(control.subMenu, sourcePrefix, destinationPrefix, visited);
            }
        }

        private static bool MenuReferencesAssetPrefix(VRCExpressionsMenu menu, string assetPrefix, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu) || menu.controls == null)
                return false;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control?.subMenu == null)
                    continue;

                string subPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(control.subMenu));
                if (PathStartsWith(subPath, assetPrefix))
                    return true;

                if (MenuReferencesAssetPrefix(control.subMenu, assetPrefix, visited))
                    return true;
            }

            return false;
        }

        private static bool HasAvatarGeneratedReferencesUnderPrefix(VRCAvatarDescriptor avatar, string assetPrefix)
        {
            if (avatar == null || string.IsNullOrWhiteSpace(assetPrefix))
                return false;

            string normalizedPrefix = NormalizeAssetPath(assetPrefix);
            string exprPath = NormalizeAssetPath(avatar.expressionParameters ? AssetDatabase.GetAssetPath(avatar.expressionParameters) : string.Empty);
            if (PathStartsWith(exprPath, normalizedPrefix))
                return true;

            string menuPath = NormalizeAssetPath(avatar.expressionsMenu ? AssetDatabase.GetAssetPath(avatar.expressionsMenu) : string.Empty);
            if (PathStartsWith(menuPath, normalizedPrefix))
                return true;

            if (MenuReferencesAssetPrefix(avatar.expressionsMenu, normalizedPrefix, new HashSet<VRCExpressionsMenu>()))
                return true;

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var controller = avatar.baseAnimationLayers[i].animatorController;
                string controllerPath = NormalizeAssetPath(controller ? AssetDatabase.GetAssetPath(controller) : string.Empty);
                if (PathStartsWith(controllerPath, normalizedPrefix))
                    return true;
            }

            return false;
        }

        private static string EnsureVendorizeAvatarFolder(VRCAvatarDescriptor avatar)
        {
            string root = EnsureAssetFolder("Assets", "ASM-Lite");
            return EnsureAssetFolder(root, SanitizePathFragment(avatar != null ? avatar.gameObject.name : "Avatar"));
        }

        private static string EnsureAssetFolder(string parent, string child)
        {
            string normalizedParent = NormalizeAssetPath(parent).TrimEnd('/');
            string candidate = normalizedParent + "/" + child;
            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(normalizedParent, child);
            return candidate;
        }

        private static string CreateUniqueFolder(string parentFolder, string prefix)
        {
            string uniqueName = CreateUniqueFolderName(parentFolder, prefix);
            AssetDatabase.CreateFolder(parentFolder, Path.GetFileName(uniqueName));
            AssetDatabase.Refresh();
            return uniqueName;
        }

        private static string CreateUniqueFolderName(string parentFolder, string prefix)
        {
            string normalizedParent = NormalizeAssetPath(parentFolder).TrimEnd('/');
            string candidateName = prefix + Guid.NewGuid().ToString("N").Substring(0, 8);
            string candidate = normalizedParent + "/" + candidateName;
            while (AssetDatabase.IsValidFolder(candidate))
            {
                candidateName = prefix + Guid.NewGuid().ToString("N").Substring(0, 8);
                candidate = normalizedParent + "/" + candidateName;
            }

            return candidate;
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            string normalized = NormalizeAssetPath(assetPath);
            if (AssetDatabase.IsValidFolder(normalized) || AssetDatabase.LoadMainAssetAtPath(normalized) != null)
            {
                AssetDatabase.DeleteAsset(normalized);
                AssetDatabase.Refresh();
            }
        }

        private static void DeleteAssetFolderIfEmpty(string assetFolderPath)
        {
            if (string.IsNullOrWhiteSpace(assetFolderPath))
                return;

            string normalizedPath = NormalizeAssetPath(assetFolderPath).TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(normalizedPath))
                return;

            if (AssetDatabase.FindAssets(string.Empty, new[] { normalizedPath }).Length == 0)
                AssetDatabase.DeleteAsset(normalizedPath);
        }

        private static bool PathStartsWith(string assetPath, string prefix)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(prefix))
                return false;

            return assetPath.StartsWith(prefix.TrimEnd('/'), StringComparison.Ordinal);
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimEnd('/');
        }

        private static string SanitizePathFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Avatar";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            string cleaned = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Avatar" : cleaned;
        }

        private static bool ShouldFailForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint failurePoint)
        {
            return s_testFailurePoint == failurePoint;
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
