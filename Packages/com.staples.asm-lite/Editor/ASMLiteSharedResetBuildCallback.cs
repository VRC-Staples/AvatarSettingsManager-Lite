using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ASMLite.Editor
{
    // Run after controller processing/compression, before final budget validation.
    public sealed class ASMLiteSharedResetBuildCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MaxValue - 3;

        public bool OnPreprocessAvatar(GameObject avatar)
        {
            var descriptor = avatar != null ? avatar.GetComponent<VRCAvatarDescriptor>() : null;
            if (!ASMLiteParameterBudgetBuildGuard.HasClearBakedEvidence(descriptor)) return true;
            var fx = descriptor.baseAnimationLayers.FirstOrDefault(layer =>
                layer.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController as AnimatorController;
            string path = AssetDatabase.GetAssetPath(fx);
            // Never optimize an authored or detached baked controller in place.
            if (fx == null || !(path.StartsWith("Packages/com.vrcfury.temp/", StringComparison.Ordinal)
                || path.StartsWith("Packages/nadena.dev.ndmf/__Generated/", StringComparison.Ordinal))) return true;
            if (TryShareLiveResetDrivers(fx)) return true;
            Debug.LogError("[ASM-Lite] Shared reset refused: processed slot reset payloads are incompatible.");
            return false;
        }

        internal static bool TryShareLiveResetDrivers(AnimatorController fx)
        {
            var resets = new List<AnimatorState>();
            var physical = new List<List<VRC_AvatarParameterDriver.Parameter>>();
            List<VRC_AvatarParameterDriver.Parameter> liveDefaults = null;
            string control = null;
            for (int slot = 1; slot <= 8; slot++)
            {
                string name = "ASMLite_Slot" + slot;
                var layers = fx.layers.Where(layer => layer.name == name
                    || layer.name.EndsWith("] " + name, StringComparison.Ordinal)).ToArray();
                if (layers.Length == 0) continue;
                if (layers.Length != 1 || slot != resets.Count + 1 || layers[0].stateMachine == null) return false;
                var states = layers[0].stateMachine.states.Where(child =>
                    child.state != null && child.state.name == "ResetSlot" + slot).ToArray();
                if (states.Length != 1) return false;
                var state = states[0].state;
                var driver = state.behaviours.Length == 1 ? state.behaviours[0] as VRCAvatarParameterDriver : null;
                if (driver == null || !driver.localOnly || driver.parameters == null || driver.parameters.Count == 0) return false;
                var acknowledgement = driver.parameters.Last();
                if (acknowledgement == null || acknowledgement.type != VRC_AvatarParameterDriver.ChangeType.Set
                    || acknowledgement.value != 0f || acknowledgement.name == null
                    || !acknowledgement.name.EndsWith(ASMLiteBuilder.CtrlParam, StringComparison.Ordinal)) return false;
                if (control == null) control = acknowledgement.name;
                if (control != acknowledgement.name) return false;
                string prefix = control.Substring(0, control.Length - ASMLiteBuilder.CtrlParam.Length);
                var backup = new List<VRC_AvatarParameterDriver.Parameter>();
                var live = new List<VRC_AvatarParameterDriver.Parameter>();
                foreach (var parameter in driver.parameters.Take(driver.parameters.Count - 1))
                {
                    if (parameter == null || parameter.type != VRC_AvatarParameterDriver.ChangeType.Set
                        || string.IsNullOrWhiteSpace(parameter.name) || parameter.name == control) return false;
                    // Parse legacy slot spellings without changing the saved parameter name.
                    bool isBackup = parameter.name.StartsWith(prefix, StringComparison.Ordinal)
                        && ASMLiteMigrationContinuityService.IsBackupNameForSlot(parameter.name.Substring(prefix.Length), slot);
                    (isBackup ? backup : live).Add(parameter);
                }
                if (live.Count != 0 && backup.Count == 0) return false;
                if (liveDefaults == null) liveDefaults = live;
                if (!liveDefaults.Select(JsonUtility.ToJson).SequenceEqual(live.Select(JsonUtility.ToJson))) return false;
                backup.Add(acknowledgement);
                physical.Add(backup);
                resets.Add(state);
            }
            if (resets.Count < 2 || liveDefaults.Count == 0) return true;

            // Validate every slot before changing any driver. Preserve routing and Save/Load.
            var shared = ScriptableObject.CreateInstance<VRCAvatarParameterDriver>();
            shared.name = "ASMLite_SharedLiveDefaults";
            shared.localOnly = true;
            shared.parameters = liveDefaults;
            if (AssetDatabase.Contains(fx)) AssetDatabase.AddObjectToAsset(shared, fx);
            for (int i = 0; i < resets.Count; i++)
            {
                var driver = (VRCAvatarParameterDriver)resets[i].behaviours[0];
                driver.parameters = physical[i];
                // Same Clear entry; only the physical driver acknowledges the command.
                resets[i].behaviours = new StateMachineBehaviour[] { driver, shared };
                EditorUtility.SetDirty(driver);
                EditorUtility.SetDirty(resets[i]);
            }
            EditorUtility.SetDirty(shared);
            EditorUtility.SetDirty(fx);
            return true;
        }
    }
}
