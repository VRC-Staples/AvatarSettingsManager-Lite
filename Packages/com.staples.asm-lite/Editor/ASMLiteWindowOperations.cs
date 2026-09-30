using ASMLite;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    /// <summary>
    /// Durable backend actions used by <see cref="ASMLiteWindow"/>.
    /// Keeps lifecycle, mirror, build, and FullController policy out of the EditorWindow adapter.
    /// </summary>
    internal static class ASMLiteWindowOperations
    {
        public static bool IsGeneratedPresetsMenu(VRCExpressionsMenu menu)
        {
            if (menu == null)
                return false;

            return ASMLiteGeneratedOwnershipPolicy.IsGeneratedPresetsMenuFileName(AssetDatabase.GetAssetPath(menu));
        }

        public static bool IsGeneratedMenuAsset(VRCExpressionsMenu menu)
        {
            if (menu == null)
                return false;

            return ASMLiteGeneratedOwnershipPolicy.IsGeneratedMenuAssetPath(AssetDatabase.GetAssetPath(menu));
        }

        public static ASMLiteInstallationState GetAsmLiteToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            return ASMLiteInstallationStateService.Resolve(avatar, component);
        }

        public static void CreatePrefab()
        {
            ASMLitePrefabCreator.CreatePrefab();
        }

        public static bool HasStalePrmsEntry(GameObject instance)
        {
            return ASMLitePrefabCreator.HasStalePrmsEntry(instance);
        }

        public static bool TryRefreshLiveFullControllerWiring(GameObject instance, ASMLiteComponent component, string contextLabel)
        {
            return ASMLiteFullControllerWiring.TryRefreshLiveFullControllerWiring(instance, component, contextLabel);
        }

        public static bool TryRefreshLiveInstallPathPrefix(ASMLiteComponent component, string contextLabel)
        {
            return ASMLiteLifecycleTransactionService.TryRefreshLiveInstallPathPrefix(component, contextLabel);
        }

        public static bool TryRestoreAvatarGeneratedAssetsToPackageManaged(VRCAvatarDescriptor avatar, string vendorizedDir)
        {
            var result = ASMLiteGeneratedAssetMirrorService.RestoreAvatarGeneratedAssetsToPackageManaged(avatar, vendorizedDir);
            if (!result.Success)
                Debug.LogError(result.ToLogString());

            return result.Success;
        }

        public static bool TryDeleteVendorizedGeneratedAssetsFolder(string vendorizedDir)
        {
            var backupResult = ASMLiteGeneratedAssetMirrorService.BackupVendorizedFolderForDelete(vendorizedDir);
            if (!backupResult.Success)
            {
                Debug.LogError(backupResult.ToLogString());
                return false;
            }

            var finalizeResult = ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedFolderDelete(backupResult);
            if (!finalizeResult.Success)
            {
                Debug.LogError(finalizeResult.ToLogString());
                return false;
            }

            return true;
        }

        public static bool TryVendorizeGeneratedAssetsToAvatarFolder(VRCAvatarDescriptor avatar, out string vendorizedDir)
        {
            var result = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(avatar);
            if (!result.Success)
            {
                Debug.LogError(result.ToLogString());
                vendorizedDir = string.Empty;
                return false;
            }

            var finalizeResult = ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(result);
            if (!finalizeResult.Success)
            {
                Debug.LogError(finalizeResult.ToLogString());
                vendorizedDir = string.Empty;
                return false;
            }

            vendorizedDir = result.TargetPath;
            return true;
        }

        public static bool TryRetargetLiveFullControllerGeneratedAssets(ASMLiteComponent component, string generatedDir)
        {
            return ASMLiteLifecycleTransactionService.TryRetargetLiveFullControllerGeneratedAssets(component, generatedDir);
        }

        public static ASMLiteRebuildResult ExecuteRebuild(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            bool stagePackageManagedGeneratedAssets = true)
        {
            return ASMLiteLifecycleTransactionService.ExecuteRebuild(component, avatar, stagePackageManagedGeneratedAssets);
        }

        public static bool TryReturnAttachedVendorizedToPackageManaged(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            var result = ExecuteAttachedReturnToPackageManaged(component, avatar);
            if (!result.Success)
                Debug.LogError(result.ToLogString());

            return result.Success;
        }

        public static ASMLiteLifecycleTransactionResult ExecuteAttachedReturnToPackageManaged(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            return ASMLiteLifecycleTransactionService.ExecuteAttachedReturnToPackageManaged(component, avatar);
        }

        public static ASMLiteLifecycleTransactionResult ExecuteAttachedVendorize(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            return ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(component, avatar);
        }

        public static ASMLiteLifecycleTransactionResult ExecuteVendorizeAndDetach(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            return ASMLiteLifecycleTransactionService.ExecuteVendorizeAndDetach(component, avatar);
        }

        public static ASMLiteLifecycleTransactionResult ExecuteDetachToDirectDelivery(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            return ASMLiteLifecycleTransactionService.ExecuteDetachToDirectDelivery(component, avatar);
        }

        public static ASMLiteLifecycleTransactionResult ExecuteDetachedReturnToPackageManagedRecovery(
            VRCAvatarDescriptor avatar,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot pendingSnapshot)
        {
            return ASMLiteLifecycleTransactionService.ExecuteDetachedReturnToPackageManagedRecovery(avatar, pendingSnapshot);
        }

    }
}
