using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
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

            if (!VerifyLiveFullControllerReferencesUnderPrefix(component, ASMLiteAssetPaths.ResolveGeneratedDirectory(component), out failureMessage, out failureContext))
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
            ASMLiteComponent component,
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

            string generatedDir = expectedDetachedState == ASMLiteInstallationState.Vendorized
                ? NormalizeOptionalPath(vendorizedDir) : ASMLiteAssetPaths.ResolveGeneratedDirectory(component);
            var presets = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(generatedDir + "/" + ASMLiteGeneratedOwnershipPolicy.PresetsMenuFileName);
            var fx = avatar.baseAnimationLayers?
                .FirstOrDefault(layer => layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            var parameters = avatar.expressionParameters?.parameters;
            bool menuDelivered = component != null && presets?.controls != null && presets.controls.Count == component.slotCount
                && presets.controls.All(control => control?.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                && avatar.expressionsMenu?.controls != null
                && avatar.expressionsMenu.controls.Any(control => control?.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu == presets);
            bool parametersDelivered = parameters != null && parameters.Any(parameter => parameter != null
                && parameter.name == ASMLiteBuilder.CtrlParam && parameter.valueType == VRCExpressionParameters.ValueType.Int)
                && fx != null && fx.parameters.Where(parameter => parameter.name.StartsWith("ASMLite_Bak_", StringComparison.Ordinal))
                    .All(backup => parameters.Any(parameter => parameter != null && parameter.name == backup.name));
            bool fxDelivered = component != null && fx != null
                && fx.parameters.Any(parameter => parameter.name == ASMLiteBuilder.CtrlParam && parameter.type == AnimatorControllerParameterType.Int)
                && Enumerable.Range(1, component.slotCount).All(slot => fx.layers.Any(layer =>
                    layer.name == $"ASMLite_Slot{slot}" && layer.stateMachine != null && layer.stateMachine.states.Length >= 4));
            if (!menuDelivered || !parametersDelivered || !fxDelivered)
            {
                failureMessage = $"[ASM-Lite] Detach verification failed because required direct-delivery content was missing (menu: {menuDelivered}, parameters: {parametersDelivered}, FX: {fxDelivered}).";
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

            var detachedState = ASMLiteInstallationStateService.Resolve(avatar, null);
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
            ASMLiteInstallationState detachedBaselineState,
            out string failureMessage,
            out string failureContext)
        {
            bool vendorized = beforeState == ASMLiteInstallationState.Vendorized;
            string expectedPath = vendorized ? NormalizeOptionalPath(component != null ? component.vendorizedGeneratedAssetsPath : string.Empty) : string.Empty;
            if (!VerifyComponentVendorizedState(component, vendorized, expectedPath, out failureMessage, out failureContext))
                return false;

            if (ASMLiteInstallationStateService.Resolve(avatar, null) != detachedBaselineState)
            {
                failureMessage = "[ASM-Lite] Detach rollback failed because the descriptor did not recover its original runtime-marker state.";
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

            if (!VerifyLiveFullControllerReferencesUnderPrefix(component, ASMLiteAssetPaths.ResolveGeneratedDirectory(component), out failureMessage, out failureContext))
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
            var snapshotResult = ASMLiteFullControllerWiring.TryCaptureLiveFullControllerReferenceSnapshot(component, "Lifecycle Transaction Verify", out var snapshot);
            if (!snapshotResult.Success)
            {
                failureMessage = snapshotResult.Message;
                failureContext = snapshotResult.ContextPath;
                return false;
            }

            string normalizedExpectedPrefix = NormalizeOptionalPath(expectedPrefix);
            if (snapshot.ControllersArraySize == 0 || snapshot.MenusArraySize == 0
                || !PathStartsWith(snapshot.ControllerAssetPath, normalizedExpectedPrefix)
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

        private static ASMLiteInstallationState ResolveToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            return ASMLiteInstallationStateService.Resolve(avatar, component);
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
