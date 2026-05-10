using System;
using System.IO;
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

            string menuPath = AssetDatabase.GetAssetPath(menu)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(menuPath))
                return false;

            return string.Equals(Path.GetFileName(menuPath), "ASMLite_Presets_Menu.asset", StringComparison.Ordinal);
        }

        public static bool IsGeneratedMenuAsset(VRCExpressionsMenu menu)
        {
            if (menu == null)
                return false;

            string menuPath = AssetDatabase.GetAssetPath(menu)?.Replace('\\', '/');
            if (string.IsNullOrWhiteSpace(menuPath))
                return false;

            string fileName = Path.GetFileName(menuPath);
            return fileName.StartsWith("ASMLite_", StringComparison.Ordinal)
                && fileName.EndsWith("Menu.asset", StringComparison.Ordinal);
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
            if (component == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Cannot refresh install-path routing because the ASM-Lite component was null.");
                return false;
            }

            if (FindLiveVrcFuryComponent(component) == null)
            {
                bool repaired = TryRefreshLiveFullControllerWiring(
                    component.gameObject,
                    component,
                    contextLabel + " Auto-Heal");
                if (!repaired || FindLiveVrcFuryComponent(component) == null)
                {
                    Debug.LogError($"[ASM-Lite] {contextLabel}: Expected VF.Model.VRCFury component was not found on '{component.gameObject.name}'.");
                    return false;
                }

                Debug.LogWarning($"[ASM-Lite] {contextLabel}: VF.Model.VRCFury component was missing on '{component.gameObject.name}'. Live FullController wiring was repaired automatically.");
            }

            if (!ASMLiteBuilder.TrySyncInstallPathRouting(component))
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Failed to refresh install-path routing on '{component.gameObject.name}'.");
                return false;
            }

            var effectivePrefix = ASMLiteFullControllerInstallPathHelper.ResolveEffectivePrefix(component);
            if (string.IsNullOrEmpty(effectivePrefix))
                Debug.Log($"[ASM-Lite] {contextLabel}: refreshed install-path routing to root on '{component.gameObject.name}'.");
            else
                Debug.Log($"[ASM-Lite] {contextLabel}: refreshed install-path routing to '{effectivePrefix}' on '{component.gameObject.name}'.");

            return true;
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
            var result = ASMLiteFullControllerWiring.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(component, generatedDir, "Retarget Generated Assets");
            if (!result.Success)
                Debug.LogError(result.ToLogString());

            return result.Success;
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

        private static MonoBehaviour FindLiveVrcFuryComponent(ASMLiteComponent component)
        {
            if (component == null || component.gameObject == null)
                return null;

            var behaviors = component.gameObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviors.Length; i++)
            {
                var behavior = behaviors[i];
                if (behavior == null)
                    continue;

                var type = behavior.GetType();
                if (type == null)
                    continue;

                if (string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    return behavior;
            }

            return null;
        }
    }
}
