using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    internal static class ASMLiteLifecycleVerification
    {
        internal static bool VerifyAttachedVendorizeState(
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

            if (ResolveToolState(avatar, component) != ASMLiteInstallationState.Vendorized)
            {
                failureMessage = "[ASM-Lite] Attached vendorize verification failed because tool-state classification did not resolve to Vendorized.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        internal static bool VerifyAttachedReturnState(
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

            if (ResolveToolState(avatar, component) != ASMLiteInstallationState.PackageManaged)
            {
                failureMessage = "[ASM-Lite] Attached return verification failed because tool-state classification did not resolve to PackageManaged.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        internal static bool VerifyPackageManagedRollbackState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            out string failureMessage,
            out string failureContext)
        {
            return VerifyAttachedReturnState(component, avatar, NormalizeOptionalPath(component != null ? component.vendorizedGeneratedAssetsPath : string.Empty), out failureMessage, out failureContext)
                && ResolveToolState(avatar, component) == ASMLiteInstallationState.PackageManaged;
        }

        internal static bool VerifyVendorizedRollbackState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            string vendorizedDir,
            out string failureMessage,
            out string failureContext)
        {
            return VerifyAttachedVendorizeState(component, avatar, vendorizedDir, out failureMessage, out failureContext)
                && ResolveToolState(avatar, component) == ASMLiteInstallationState.Vendorized;
        }

        internal static bool VerifyDirectDeliveryState(
            VRCAvatarDescriptor avatar,
            ASMLiteInstallationState expectedDetachedState,
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

            if (expectedDetachedState == ASMLiteInstallationState.Vendorized)
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

        internal static bool VerifyAttachedStateAfterDetachRollback(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            ASMLiteInstallationState beforeState,
            out string failureMessage,
            out string failureContext)
        {
            if (beforeState == ASMLiteInstallationState.Vendorized)
            {
                string expectedVendorizedPath = NormalizeOptionalPath(component != null ? component.vendorizedGeneratedAssetsPath : string.Empty);
                if (!VerifyComponentVendorizedState(component, expectedUseVendorized: true, expectedPath: expectedVendorizedPath, out failureMessage, out failureContext))
                    return false;

                if (ASMLiteWindow.GetAsmLiteToolState(avatar, null) != ASMLiteInstallationState.Vendorized)
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

            if (ASMLiteWindow.GetAsmLiteToolState(avatar, null) != ASMLiteInstallationState.NotInstalled)
            {
                failureMessage = "[ASM-Lite] Detach rollback failed because detached runtime markers still remained on the avatar after restoring the attached package-managed baseline.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
        }

        internal static bool VerifyDetachedRecoveryState(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            out string failureMessage,
            out string failureContext)
        {
            if (!VerifyComponentVendorizedState(component, expectedUseVendorized: false, expectedPath: string.Empty, out failureMessage, out failureContext))
                return false;

            if (!VerifyLiveFullControllerReferencesUnderPrefix(component, ASMLiteAssetPaths.GeneratedDir, out failureMessage, out failureContext))
                return false;

            if (ResolveToolState(avatar, component) != ASMLiteInstallationState.PackageManaged)
            {
                failureMessage = "[ASM-Lite] Detached recovery verification failed because the reattached avatar did not resolve to PackageManaged tool state.";
                failureContext = "toolState";
                return false;
            }

            failureMessage = string.Empty;
            failureContext = string.Empty;
            return true;
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

        private static ASMLiteInstallationState ResolveToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            return ASMLiteWindow.GetAsmLiteToolState(avatar, component);
        }

        private static bool PathStartsWith(string assetPath, string prefix)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || string.IsNullOrWhiteSpace(prefix))
                return false;

            return assetPath.StartsWith(prefix.TrimEnd('/'), StringComparison.Ordinal);
        }

        private static bool MenuReferencesPrefix(VRCExpressionsMenu menu, string prefix)
        {
            return MenuReferencesPrefix(menu, prefix, new HashSet<VRCExpressionsMenu>());
        }

        private static bool MenuReferencesPrefix(
            VRCExpressionsMenu menu,
            string prefix,
            HashSet<VRCExpressionsMenu> visited)
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
    }
}
