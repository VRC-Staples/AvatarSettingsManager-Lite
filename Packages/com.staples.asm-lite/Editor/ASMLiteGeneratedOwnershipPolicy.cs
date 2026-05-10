using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    internal enum ASMLiteGeneratedReferenceKind
    {
        None = 0,
        PackageManaged = 1,
        Vendorized = 2,
        DirectDeliveryMarker = 3,
    }

    internal static class ASMLiteGeneratedOwnershipPolicy
    {
        internal const string VendorizedGeneratedAssetsRoot = "Assets/ASM-Lite";
        internal const string PresetsMenuFileName = "ASMLite_Presets_Menu.asset";

        internal static bool IsGeneratedRuntimeName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && (name.StartsWith("ASMLite_", StringComparison.Ordinal)
                    || string.Equals(name, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal));
        }

        internal static bool IsGeneratedFxLayer(AnimatorControllerLayer layer)
        {
            return IsGeneratedRuntimeName(layer.name);
        }

        internal static bool IsGeneratedFxParameter(AnimatorControllerParameter parameter)
        {
            return parameter != null && IsGeneratedRuntimeName(parameter.name);
        }

        internal static bool IsGeneratedExpressionParameter(VRCExpressionParameters.Parameter parameter)
        {
            return parameter != null && IsGeneratedRuntimeName(parameter.name);
        }

        internal static bool IsGeneratedRootMenuControl(VRCExpressionsMenu.Control control)
        {
            if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                return false;

            if (string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal))
                return true;

            string submenuPath = NormalizeAssetPath(control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu) : string.Empty);
            if (IsGeneratedPresetsMenuPath(submenuPath))
                return true;

            return IsGeneratedPresetsMenuFileName(submenuPath);
        }

        internal static bool IsInjectedRootMenuControl(
            VRCExpressionsMenu.Control control,
            string effectiveRootControlName)
        {
            if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                return false;

            return string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal)
                || string.Equals(control.name, effectiveRootControlName, StringComparison.Ordinal)
                || IsGeneratedPresetsMenuPath(control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu) : string.Empty);
        }

        internal static bool HasRuntimeMarkers(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            var expressionParameters = avatar.expressionParameters;
            if (expressionParameters?.parameters != null)
            {
                for (int i = 0; i < expressionParameters.parameters.Length; i++)
                {
                    if (IsGeneratedExpressionParameter(expressionParameters.parameters[i]))
                        return true;
                }
            }

            if (avatar.baseAnimationLayers != null)
            {
                for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
                {
                    var controller = avatar.baseAnimationLayers[i].animatorController as AnimatorController;
                    if (controller == null)
                        continue;

                    for (int layerIndex = 0; layerIndex < controller.layers.Length; layerIndex++)
                    {
                        if (IsGeneratedFxLayer(controller.layers[layerIndex]))
                            return true;
                    }

                    for (int parameterIndex = 0; parameterIndex < controller.parameters.Length; parameterIndex++)
                    {
                        if (IsGeneratedFxParameter(controller.parameters[parameterIndex]))
                            return true;
                    }
                }
            }

            if (avatar.expressionsMenu?.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    if (IsGeneratedRootMenuControl(control))
                        return true;

                    string subPath = NormalizeAssetPath(control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu) : string.Empty);
                    if (PathReferencesDirectDeliveryMarker(subPath))
                        return true;
                }
            }

            return false;
        }

        internal static bool HasVendorizedReferences(VRCAvatarDescriptor avatar)
        {
            return HasDescriptorGeneratedReferencesUnderPrefix(avatar, VendorizedGeneratedAssetsRoot);
        }

        internal static bool HasDescriptorGeneratedReferencesUnderPrefix(VRCAvatarDescriptor avatar, string prefix)
        {
            if (avatar == null)
                return false;

            string normalizedPrefix = NormalizeAssetPath(prefix);
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
                return false;

            string expressionParametersPath = NormalizeAssetPath(
                avatar.expressionParameters ? AssetDatabase.GetAssetPath(avatar.expressionParameters) : string.Empty);
            if (PathStartsWith(expressionParametersPath, normalizedPrefix))
                return true;

            string menuPath = NormalizeAssetPath(
                avatar.expressionsMenu ? AssetDatabase.GetAssetPath(avatar.expressionsMenu) : string.Empty);
            if (PathStartsWith(menuPath, normalizedPrefix) || MenuReferencesPrefix(avatar.expressionsMenu, normalizedPrefix))
                return true;

            if (avatar.baseAnimationLayers != null)
            {
                for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
                {
                    string controllerPath = NormalizeAssetPath(avatar.baseAnimationLayers[i].animatorController
                        ? AssetDatabase.GetAssetPath(avatar.baseAnimationLayers[i].animatorController)
                        : string.Empty);
                    if (PathStartsWith(controllerPath, normalizedPrefix))
                        return true;
                }
            }

            return false;
        }

        internal static bool MenuReferencesPrefix(VRCExpressionsMenu menu, string prefix)
        {
            return MenuReferencesPrefix(menu, prefix, new HashSet<VRCExpressionsMenu>());
        }

        internal static bool MenuReferencesPrefix(
            VRCExpressionsMenu menu,
            string prefix,
            HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu) || menu.controls == null)
                return false;

            string normalizedPrefix = NormalizeAssetPath(prefix);
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
                return false;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control?.subMenu == null)
                    continue;

                string subMenuPath = NormalizeAssetPath(AssetDatabase.GetAssetPath(control.subMenu));
                if (PathStartsWith(subMenuPath, normalizedPrefix)
                    || MenuReferencesPrefix(control.subMenu, normalizedPrefix, visited))
                {
                    return true;
                }
            }

            return false;
        }

        internal static ASMLiteGeneratedReferenceKind ClassifyAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return ASMLiteGeneratedReferenceKind.None;

            if (PathStartsWith(normalizedPath, ASMLiteAssetPaths.GeneratedDir))
                return ASMLiteGeneratedReferenceKind.PackageManaged;

            if (PathStartsWith(normalizedPath, VendorizedGeneratedAssetsRoot))
                return ASMLiteGeneratedReferenceKind.Vendorized;

            if (PathReferencesDirectDeliveryMarker(normalizedPath))
                return ASMLiteGeneratedReferenceKind.DirectDeliveryMarker;

            return ASMLiteGeneratedReferenceKind.None;
        }

        internal static bool PathReferencesDirectDeliveryMarker(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            return !string.IsNullOrWhiteSpace(normalizedPath)
                && (normalizedPath.IndexOf("ASMLite_", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalizedPath.IndexOf("/ASM-Lite/", StringComparison.OrdinalIgnoreCase) >= 0
                    || normalizedPath.IndexOf("/com.staples.asm-lite/", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool PathStartsWith(string assetPath, string prefix)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            string normalizedPrefix = NormalizeAssetPath(prefix);
            if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedPrefix))
                return false;

            normalizedPrefix = normalizedPrefix.TrimEnd('/');
            return string.Equals(normalizedPath, normalizedPrefix, StringComparison.Ordinal)
                || normalizedPath.StartsWith(normalizedPrefix + "/", StringComparison.Ordinal);
        }

        internal static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : path.Replace('\\', '/').TrimEnd('/');
        }

        internal static bool IsGeneratedPresetsMenuPath(string assetPath)
        {
            return string.Equals(NormalizeAssetPath(assetPath), GeneratedPresetsMenuPath, StringComparison.Ordinal);
        }

        internal static bool IsGeneratedPresetsMenuFileName(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            return !string.IsNullOrWhiteSpace(normalizedPath)
                && string.Equals(Path.GetFileName(normalizedPath), PresetsMenuFileName, StringComparison.Ordinal);
        }

        internal static bool IsGeneratedMenuAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return false;

            string fileName = Path.GetFileName(normalizedPath);
            return fileName.StartsWith("ASMLite_", StringComparison.Ordinal)
                && fileName.EndsWith("Menu.asset", StringComparison.Ordinal);
        }

        internal static string GeneratedPresetsMenuPath
            => NormalizeAssetPath($"{ASMLiteAssetPaths.GeneratedDir}/{PresetsMenuFileName}");
    }
}
