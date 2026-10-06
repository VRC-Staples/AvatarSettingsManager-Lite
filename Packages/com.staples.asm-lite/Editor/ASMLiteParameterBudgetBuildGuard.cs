using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ASMLite.Editor
{
    // Capture eligibility before processors strip authoring components or rewrite the payload.
    public sealed class ASMLiteParameterBudgetCaptureCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;
        public bool OnPreprocessAvatar(GameObject avatar)
        {
            ASMLiteParameterBudgetBuildGuard.Capture(avatar);
            return true;
        }
    }

    public sealed class ASMLiteParameterBudgetFinalCallback : IVRCSDKPreprocessAvatarCallback
    {
        // Reviewed VRCFury versions compress at MaxValue - 100. Only exact-version
        // non-schema cleanup/runtime callbacks may run after this measurement.
        public int callbackOrder => int.MaxValue - 1;
        public bool OnPreprocessAvatar(GameObject avatar)
            => ASMLiteParameterBudgetBuildGuard.Validate(avatar);
    }

    internal static class ASMLiteParameterBudgetBuildGuard
    {
        // https://creators.vrchat.com/avatars/animator-parameters/ (custom entries, not bits)
        internal const int ParameterLimit = ASMLiteParameterBudget.Limit;
        private static readonly HashSet<GameObject> s_guarded = new HashSet<GameObject>();

        internal static void Capture(GameObject avatar)
        {
            if (avatar == null) return;
            s_guarded.Remove(avatar);
            var descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;
            bool guarded = descriptor != null && (avatar.GetComponentInChildren<ASMLiteComponent>(true) != null
                || HasClearBakedEvidence(descriptor));
            if (guarded) s_guarded.Add(avatar);
            if (!guarded && ASMLiteGeneratedOwnershipPolicy.HasRuntimeMarkers(descriptor))
                Debug.LogWarning("[ASM-Lite] Ambiguous baked installation: final parameter-count guard coverage cannot be established from a menu label or prefix alone.");
            // Clear failed builds on the next editor tick. Keep synchronous nested builds
            // scoped independently so validating an inner avatar cannot drop the outer guard.
            EditorApplication.delayCall -= Clear;
            EditorApplication.delayCall += Clear;
        }

        private static void Clear()
        {
            s_guarded.Clear();
            EditorApplication.delayCall -= Clear;
        }

        internal static bool Validate(GameObject avatar)
        {
            // Unity's destroyed-object null must not erase captured eligibility.
            bool guarded = !ReferenceEquals(avatar, null) && s_guarded.Contains(avatar);
            try
            {
                if (!guarded) return true;
                string incompatibility = FindLaterUnsupportedCallback();
                var descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;
                if (incompatibility != null || descriptor?.expressionParameters?.parameters == null)
                {
                    var unavailable = new ASMLiteParameterBudget(0, 0, false, "Final parameter count could not be verified. "
                        + (incompatibility ?? "The processed expression schema is unreadable.")
                        + " Restore readable expression parameters or use a supported processor ordering before building.");
                    Debug.LogError(unavailable.ToDiagnostic().ToLogString());
                    return false;
                }
                var diagnostic = ASMLiteBuilder.MeasureParameterBudget(descriptor.expressionParameters.parameters).ToDiagnostic();
                if (diagnostic.Success) return true;
                Debug.LogError(diagnostic.ToLogString());
                return false;
            }
            finally
            {
                if (!ReferenceEquals(avatar, null)) s_guarded.Remove(avatar);
                if (s_guarded.Count == 0) EditorApplication.delayCall -= Clear;
            }
        }

        private static string FindLaterUnsupportedCallback()
        {
            foreach (var type in TypeCache.GetTypesDerivedFrom<IVRCSDKPreprocessAvatarCallback>())
            {
                if (type.IsAbstract || type.IsInterface || type == typeof(ASMLiteParameterBudgetFinalCallback)) continue;
                try
                {
                    var callback = (IVRCSDKPreprocessAvatarCallback)Activator.CreateInstance(type);
                    if (callback.callbackOrder < int.MaxValue - 1) continue;
                    if (IsKnownNonSchemaCallback(type)) continue;
                    return $"Processor {type.FullName} runs at or after the final count guard.";
                }
                catch (Exception exception)
                {
                    return $"Processor {type.FullName} ordering is unreadable ({exception.GetType().Name}).";
                }
            }
            return null;
        }

        private static bool IsKnownNonSchemaCallback(Type type)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(type.Assembly);
            return IsReviewedNonSchemaCallback(package?.name, package?.version, type.FullName);
        }

        internal static bool IsReviewedNonSchemaCallback(string packageName, string version, string typeName)
        {
            if (packageName == "nadena.dev.ndmf" && version == "1.14.8")
                return typeName == "nadena.dev.ndmf.VRChat.ForceReinitVRCConstraintsHook"
                    || typeName == "nadena.dev.ndmf.VRChat.ForceReinitPhysBonesHook";
            if (packageName == "nadena.dev.modular-avatar" && version == "1.18.7")
                return typeName == "nadena.dev.modular_avatar.core.editor.ReplacementRemoveIEditorOnly";
            if (packageName != "com.vrcfury.vrcfury" || (version != "1.1334.0" && version != "1.1430.0")) return false;
            switch (typeName)
            {
                case "VF.Hooks.VrcsdkFixes.KeepEditorOnlyComponentsLongerHook+VrcfRemoveEditorOnlyComponents":
                case "VF.Hooks.PreProcessingFailureCheckHook+FailureCheckEnd":
                case "VF.Hooks.Av3EmuFixes.Av3EmuAnimatorFixHook":
                case "VF.Hooks.AudioLinkFixes.AudioLinkPlayModeRefreshHook":
                    return true;
                case "VF.Hooks.VrcsdkFixes.PlayModeContactFixHook+PlayerBuilt":
                    return version == "1.1334.0";
                default: return false;
            }
        }

        internal static bool HasClearBakedEvidence(VRCAvatarDescriptor descriptor)
        {
            if (descriptor == null) return false;
            var parameters = descriptor.expressionParameters?.parameters;
            if (parameters == null) return false;
            var byName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);
            foreach (var parameter in parameters)
                if (parameter != null && !string.IsNullOrEmpty(parameter.name)) byName[parameter.name] = parameter;
            if (!byName.TryGetValue("ASMLite_Ctrl", out var control)
                || control.valueType != VRCExpressionParameters.ValueType.Int || control.networkSynced) return false;
            bool pairedBackup = false;
            foreach (var parameter in parameters)
            {
                const string prefix = "ASMLite_Bak_S";
                if (parameter == null || parameter.name == null || !parameter.name.StartsWith(prefix, StringComparison.Ordinal)) continue;
                int separator = parameter.name.IndexOf('_', prefix.Length);
                if (separator < 0 || !int.TryParse(parameter.name.Substring(prefix.Length, separator - prefix.Length), out int slot) || slot < 1) continue;
                if (byName.TryGetValue("ASMLite_Def_" + parameter.name.Substring(separator + 1), out var defaults)
                    && defaults.valueType == parameter.valueType && !defaults.networkSynced && !parameter.networkSynced)
                    pairedBackup = true;
            }
            bool ownedAssets = ASMLiteGeneratedOwnershipPolicy.HasDescriptorGeneratedReferencesUnderPrefix(descriptor, ASMLiteAssetPaths.GeneratedDir)
                || ASMLiteGeneratedOwnershipPolicy.HasVendorizedReferences(descriptor);
            // Zero selected backups still produce a control, menu, and slot FX wiring.
            // A label/prefix alone remains ambiguous; require those independent surfaces.
            bool slotWiring = (descriptor.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>()).Any(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX && layer.animatorController is AnimatorController controller
                && controller.parameters.Any(p => p.name == ASMLiteBuilder.CtrlParam && p.type == AnimatorControllerParameterType.Int)
                && controller.layers.Any(l => l.name.StartsWith("ASMLite_Slot", StringComparison.Ordinal)
                    && l.stateMachine != null
                    && l.stateMachine.states.Any(s => s.state != null && s.state.name.StartsWith("SaveSlot", StringComparison.Ordinal))
                    && l.stateMachine.states.Any(s => s.state != null && s.state.name.StartsWith("LoadSlot", StringComparison.Ordinal))
                    && l.stateMachine.states.Any(s => s.state != null && s.state.name.StartsWith("ResetSlot", StringComparison.Ordinal))));
            return (pairedBackup || ownedAssets || slotWiring)
                && HasControlButton(descriptor.expressionsMenu, new HashSet<VRCExpressionsMenu>());
        }

        private static bool HasControlButton(VRCExpressionsMenu menu, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu) || menu.controls == null) return false;
            foreach (var control in menu.controls)
            {
                if (control == null) continue;
                if (control.type == VRCExpressionsMenu.Control.ControlType.Button
                    && control.parameter?.name == "ASMLite_Ctrl" && control.value >= 1 && control.value <= 24) return true;
                if (HasControlButton(control.subMenu, visited)) return true;
            }
            return false;
        }
    }
}
