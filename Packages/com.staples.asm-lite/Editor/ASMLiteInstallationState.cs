using System;
using ASMLite;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Editor
{
    internal enum ASMLiteInstallationState
    {
        NotInstalled,
        PackageManaged,
        Detached,
        Vendorized,
    }

    internal static class ASMLiteInstallationStateService
    {
        internal static ASMLiteInstallationState Resolve(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            if (component != null)
                return component.useVendorizedGeneratedAssets ? ASMLiteInstallationState.Vendorized : ASMLiteInstallationState.PackageManaged;
            if (avatar == null)
                return ASMLiteInstallationState.NotInstalled;
            if (HasVendorizedAsmLiteReferences(avatar))
                return ASMLiteInstallationState.Vendorized;
            if (HasAsmLiteRuntimeMarkers(avatar))
                return ASMLiteInstallationState.Detached;
            return ASMLiteInstallationState.NotInstalled;
        }

        private static bool HasVendorizedAsmLiteReferences(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            const string vendorPrefix = "Assets/ASM-Lite/";

            string exprPath = avatar.expressionParameters ? AssetDatabase.GetAssetPath(avatar.expressionParameters)?.Replace('\\', '/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(exprPath) && exprPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                return true;

            string menuPath = avatar.expressionsMenu ? AssetDatabase.GetAssetPath(avatar.expressionsMenu)?.Replace('\\', '/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(menuPath) && menuPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                return true;

            if (avatar.expressionsMenu != null && avatar.expressionsMenu.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control?.subMenu == null)
                        continue;

                    string subPath = AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(subPath) && subPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                        return true;
                }
            }

            if (avatar.baseAnimationLayers != null)
            {
                for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
                {
                    var ctrl = avatar.baseAnimationLayers[i].animatorController;
                    if (!ctrl)
                        continue;

                    string ctrlPath = AssetDatabase.GetAssetPath(ctrl)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(ctrlPath) && ctrlPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                        return true;
                }
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
                    var p = expr.parameters[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.name))
                        continue;
                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(p.name, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            if (avatar.baseAnimationLayers != null)
            {
                for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
                {
                    var ctrl = avatar.baseAnimationLayers[i].animatorController as AnimatorController;
                    if (ctrl == null)
                        continue;

                    for (int j = 0; j < ctrl.layers.Length; j++)
                    {
                        if (ctrl.layers[j].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                            return true;
                    }

                    for (int j = 0; j < ctrl.parameters.Length; j++)
                    {
                        string paramName = ctrl.parameters[j].name;
                        if (string.IsNullOrWhiteSpace(paramName))
                            continue;
                        if (paramName.StartsWith("ASMLite_", StringComparison.Ordinal)
                            || string.Equals(paramName, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                            return true;
                    }
                }
            }

            if (avatar.expressionsMenu?.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control == null || control.type != VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    if (string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal))
                        return true;

                    string subPath = control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/') : string.Empty;
                    if (!string.IsNullOrWhiteSpace(subPath)
                        && (subPath.IndexOf("ASMLite_", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/ASM-Lite/", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/com.staples.asm-lite/", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }

            return false;
        }
    }
}
