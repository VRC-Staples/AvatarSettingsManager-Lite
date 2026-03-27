using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using ASMLite;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLiteBuilder — static editor utility for build-time asset generation.
    ///
    /// Called from ASMLiteComponent.Preprocess() during the VRChat SDK avatar build
    /// pipeline. Discovers all custom avatar parameters, generates 3 FX animator
    /// slot layers with Save/Load/Reset states using VRCAvatarParameterDriver Copy
    /// operations, and writes 3 synced Int control parameters to ASMLite_Params.asset.
    ///
    /// All generated content is written into the existing stub assets in-place,
    /// preserving their stable GUIDs.
    /// </summary>
    public static class ASMLiteBuilder
    {
        // ─── Asset paths ──────────────────────────────────────────────────────

        private const string FXControllerPath = "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_FX.controller";
        private const string ParamsAssetPath  = "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_Params.asset";
        private const string MenuAssetPath    = "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_Menu.asset";

        // ─── Icon paths ───────────────────────────────────────────────────────

        private const string IconSavePath    = "Packages/com.staples.asm-lite/Icons/Save.png";
        private const string IconLoadPath    = "Packages/com.staples.asm-lite/Icons/Load.png";
        private const string IconResetPath   = "Packages/com.staples.asm-lite/Icons/Reset.png";
        private const string IconPresetsPath = "Packages/com.staples.asm-lite/Icons/Presets.png";

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Entry point called during avatar build preprocessing.
        /// Discovers custom avatar parameters and generates all slot assets.
        /// </summary>
        public static void Build(ASMLiteComponent component)
        {
            // 1. Find avatar descriptor
            var avDesc = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (avDesc == null)
            {
                Debug.LogError($"[ASM-Lite] Build failed: no VRCAvatarDescriptor found in parent hierarchy of '{component.gameObject.name}'.");
                return;
            }

            // 2. Get expression parameters
            var exprParams = avDesc.expressionParameters;
            if (exprParams == null)
            {
                Debug.LogWarning($"[ASM-Lite] No expressionParameters asset assigned on VRCAvatarDescriptor '{avDesc.gameObject.name}'. Generating empty layers.");
            }

            // 3. Discover custom parameters
            var discoveredParams = new List<VRCExpressionParameters.Parameter>();
            if (exprParams != null && exprParams.parameters != null)
            {
                foreach (var p in exprParams.parameters)
                {
                    if (string.IsNullOrEmpty(p.name))
                        continue;

                    if (p.name.StartsWith("ASMLite_"))
                    {
                        Debug.LogWarning($"[ASM-Lite] Skipping expression parameter '{p.name}' — already prefixed with 'ASMLite_'. Remove it from the avatar's expression parameters to avoid conflicts.");
                        continue;
                    }

                    discoveredParams.Add(p);
                }
            }

            // 4. Log discovery
            Debug.Log($"[ASM-Lite] Discovered {discoveredParams.Count} custom parameters for '{component.gameObject.name}'.");

            // 5. Warn if zero params (layers will be generated with empty Copy lists)
            if (discoveredParams.Count == 0)
            {
                Debug.LogWarning($"[ASM-Lite] No custom parameters discovered. FX layers will be generated with empty driver lists.");
            }

            // 6–8. Generate assets
            PopulateFXController(discoveredParams, component.slotCount);
            PopulateExpressionParams(component.slotCount);
            PopulateExpressionMenu(component.slotCount);

            // 9. Log completion
            Debug.Log($"[ASM-Lite] Build complete for '{component.gameObject.name}': {component.slotCount} slots, {discoveredParams.Count} parameters backed up.");
        }

        /// <summary>
        /// Removes any previously generated assets. Currently a no-op; reserved for
        /// future cleanup passes if slot count changes between builds.
        /// </summary>
        public static void CleanupGeneratedAssets(ASMLiteComponent component)
        {
            Debug.Log($"[ASM-Lite] CleanupGeneratedAssets called for '{component.gameObject.name}' — no cleanup required (in-place mutation model).");
        }

        /// <summary>
        /// Validates the component configuration. Returns null if valid, or an error
        /// message string if invalid.
        /// </summary>
        public static string Validate(ASMLiteComponent component)
        {
            if (component.slotCount < 1 || component.slotCount > 3)
                return $"[ASM-Lite] slotCount must be between 1 and 3 (got {component.slotCount}).";
            return null;
        }

        // ─── Private implementation ────────────────────────────────────────────

        /// <summary>
        /// Clears and regenerates all parameters and layers in the managed FX
        /// AnimatorController, then saves the asset.
        /// </summary>
        private static void PopulateFXController(List<VRCExpressionParameters.Parameter> avatarParams, int slotCount)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(FXControllerPath);
            if (ctrl == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load AnimatorController at '{FXControllerPath}'.");
                return;
            }

            // Clear existing layers (iterate backwards to avoid index shifting)
            while (ctrl.layers.Length > 0)
                ctrl.RemoveLayer(0);

            // Clear existing parameters
            var existingParams = ctrl.parameters.ToArray();
            foreach (var p in existingParams)
                ctrl.RemoveParameter(p);

            // Add slot control parameters: ASMLite_S1, ASMLite_S2, ..., ASMLite_SN
            for (int slot = 1; slot <= slotCount; slot++)
                ctrl.AddParameter($"ASMLite_S{slot}", AnimatorControllerParameterType.Int);

            // Add per-slot backup parameters: ASMLite_Bak_S{slot}_{paramName}
            for (int slot = 1; slot <= slotCount; slot++)
            {
                foreach (var p in avatarParams)
                    ctrl.AddParameter($"ASMLite_Bak_S{slot}_{p.name}", MapValueType(p.valueType));
            }

            // Add default parameters (one set, not per-slot): ASMLite_Def_{paramName}
            foreach (var p in avatarParams)
            {
                var acp = new AnimatorControllerParameter
                {
                    name = $"ASMLite_Def_{p.name}",
                    type = MapValueType(p.valueType)
                };

                // Seed default value from the avatar's expression parameter
                switch (p.valueType)
                {
                    case VRCExpressionParameters.ValueType.Int:
                        acp.defaultInt = (int)p.defaultValue;
                        break;
                    case VRCExpressionParameters.ValueType.Float:
                        acp.defaultFloat = p.defaultValue;
                        break;
                    case VRCExpressionParameters.ValueType.Bool:
                        acp.defaultBool = p.defaultValue != 0f;
                        break;
                }

                ctrl.AddParameter(acp);
            }

            // Generate one layer per slot
            for (int slot = 1; slot <= slotCount; slot++)
                AddSlotLayer(ctrl, slot, avatarParams);

            EditorUtility.SetDirty(ctrl);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Builds one FX animator layer for the given slot with Idle, SaveSlot,
        /// LoadSlot, and ResetSlot states, each backed by a VRCAvatarParameterDriver.
        /// </summary>
        private static void AddSlotLayer(AnimatorController ctrl, int slot, List<VRCExpressionParameters.Parameter> avatarParams)
        {
            string slotName     = $"ASMLite_Slot{slot}";
            string controlParam = $"ASMLite_S{slot}";

            // Create and register the state machine as a sub-asset of the controller so
            // it survives serialization (required when HideInHierarchy is set).
            var sm = new AnimatorStateMachine
            {
                name      = slotName,
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(sm, ctrl);

            var layer = new AnimatorControllerLayer
            {
                name          = slotName,
                defaultWeight = 1f,
                stateMachine  = sm
            };
            ctrl.AddLayer(layer);

            // IMPORTANT: AddLayer copies the struct, so we must re-fetch the live
            // stateMachine reference from the controller after adding.
            int layerIndex = ctrl.layers.Length - 1;
            sm = ctrl.layers[layerIndex].stateMachine;

            // ── Create states ────────────────────────────────────────────────

            var idleState = sm.AddState("Idle", new Vector3(250, 0, 0));
            idleState.writeDefaultValues = false;

            var saveState = sm.AddState($"SaveSlot{slot}", new Vector3(500, -75, 0));
            saveState.writeDefaultValues = false;

            var loadState = sm.AddState($"LoadSlot{slot}", new Vector3(500, 0, 0));
            loadState.writeDefaultValues = false;

            var resetState = sm.AddState($"ResetSlot{slot}", new Vector3(500, 75, 0));
            resetState.writeDefaultValues = false;

            sm.defaultState = idleState;

            // ── Save state: avatar param → backup param, then reset control ──

            var saveDriver = saveState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            saveDriver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            foreach (var p in avatarParams)
            {
                saveDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = p.name,
                    name   = $"ASMLite_Bak_S{slot}_{p.name}",
                });
            }
            saveDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type  = VRC_AvatarParameterDriver.ChangeType.Set,
                name  = controlParam,
                value = 0f,
            });

            // ── Load state: backup param → avatar param, then reset control ──

            var loadDriver = loadState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            loadDriver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            foreach (var p in avatarParams)
            {
                loadDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = $"ASMLite_Bak_S{slot}_{p.name}",
                    name   = p.name,
                });
            }
            loadDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type  = VRC_AvatarParameterDriver.ChangeType.Set,
                name  = controlParam,
                value = 0f,
            });

            // ── Reset state: default param → avatar param, then reset control ─

            var resetDriver = resetState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            resetDriver.parameters = new List<VRC_AvatarParameterDriver.Parameter>();
            foreach (var p in avatarParams)
            {
                resetDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = $"ASMLite_Def_{p.name}",
                    name   = p.name,
                });
            }
            resetDriver.parameters.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type  = VRC_AvatarParameterDriver.ChangeType.Set,
                name  = controlParam,
                value = 0f,
            });

            // ── Transitions ──────────────────────────────────────────────────

            // Idle → action states (condition-based, no exit time)
            AddConditionTransition(idleState, saveState,  controlParam, AnimatorConditionMode.Equals, 1);
            AddConditionTransition(idleState, loadState,  controlParam, AnimatorConditionMode.Equals, 2);
            AddConditionTransition(idleState, resetState, controlParam, AnimatorConditionMode.Equals, 3);

            // Action states → Idle (exit-time at 0, immediate)
            AddExitTimeTransition(saveState,  idleState);
            AddExitTimeTransition(loadState,  idleState);
            AddExitTimeTransition(resetState, idleState);
        }

        /// <summary>
        /// Writes exactly <paramref name="slotCount"/> synced Int control parameters
        /// (ASMLite_S1 … ASMLite_SN) into the managed VRCExpressionParameters asset.
        /// Backup and default parameters are NOT written here — they are local-only
        /// AnimatorController parameters with zero sync cost (R008).
        /// </summary>
        private static void PopulateExpressionParams(int slotCount)
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ParamsAssetPath);
            if (paramsAsset == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load VRCExpressionParameters at '{ParamsAssetPath}'.");
                return;
            }

            var paramList = new List<VRCExpressionParameters.Parameter>();
            for (int i = 1; i <= slotCount; i++)
            {
                paramList.Add(new VRCExpressionParameters.Parameter
                {
                    name         = $"ASMLite_S{i}",
                    valueType    = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 0f,
                    saved        = false,
                    networkSynced = true,
                });
            }
            paramsAsset.parameters = paramList.ToArray();

            EditorUtility.SetDirty(paramsAsset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Generates the nested VRCExpressionsMenu tree at build time.
        /// Creates 7 menu assets total:
        ///   1 root (mutated in-place to preserve stable GUID) +
        ///   slotCount slot sub-menus +
        ///   slotCount confirm sub-menus (Save confirmation).
        ///
        /// Menu hierarchy:
        ///   Presets (root)
        ///     └─ Preset N  (SubMenu → slotMenu)
        ///          ├─ Save  (SubMenu → confirmMenu)
        ///          │    └─ Confirm  (Button, param ASMLite_SN = 1)
        ///          ├─ Load  (Button, param ASMLite_SN = 2)
        ///          └─ Reset (Button, param ASMLite_SN = 3)
        /// </summary>
        private static void PopulateExpressionMenu(int slotCount)
        {
            // ── Load icons (null-safe — icons are optional) ───────────────────
            var iconSave    = AssetDatabase.LoadAssetAtPath<Texture2D>(IconSavePath);
            var iconLoad    = AssetDatabase.LoadAssetAtPath<Texture2D>(IconLoadPath);
            var iconReset   = AssetDatabase.LoadAssetAtPath<Texture2D>(IconResetPath);
            var iconPresets = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPresetsPath);

            if (iconSave == null)
                Debug.LogWarning("[ASM-Lite] Save icon not found at " + IconSavePath + " — controls will have no icon.");

            // ── Load root menu in-place to preserve its stable GUID ───────────
            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(MenuAssetPath);
            if (rootMenu == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load VRCExpressionsMenu at '{MenuAssetPath}'.");
                return;
            }

            string generatedDir = System.IO.Path.GetDirectoryName(MenuAssetPath);

            // ── Delete and recreate sub-menu assets ───────────────────────────
            for (int slot = 1; slot <= slotCount; slot++)
            {
                string slotPath    = $"{generatedDir}/ASMLite_Slot{slot}_Menu.asset";
                string confirmPath = $"{generatedDir}/ASMLite_Slot{slot}_ConfirmMenu.asset";

                if (AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(slotPath) != null)
                    AssetDatabase.DeleteAsset(slotPath);
                if (AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(confirmPath) != null)
                    AssetDatabase.DeleteAsset(confirmPath);

                // ── Confirm sub-menu ──────────────────────────────────────────
                var confirmMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                confirmMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>
                {
                    new VRCExpressionsMenu.Control
                    {
                        name      = "Confirm",
                        type      = VRCExpressionsMenu.Control.ControlType.Button,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = $"ASMLite_S{slot}" },
                        value     = 1f,
                        icon      = iconSave,
                    }
                };
                AssetDatabase.CreateAsset(confirmMenu, confirmPath);

                // ── Slot sub-menu (Save / Load / Reset) ───────────────────────
                var slotMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                slotMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>
                {
                    new VRCExpressionsMenu.Control
                    {
                        name    = "Save",
                        type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                        subMenu = confirmMenu,
                        icon    = iconSave,
                    },
                    new VRCExpressionsMenu.Control
                    {
                        name      = "Load",
                        type      = VRCExpressionsMenu.Control.ControlType.Button,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = $"ASMLite_S{slot}" },
                        value     = 2f,
                        icon      = iconLoad,
                    },
                    new VRCExpressionsMenu.Control
                    {
                        name      = "Reset",
                        type      = VRCExpressionsMenu.Control.ControlType.Button,
                        parameter = new VRCExpressionsMenu.Control.Parameter { name = $"ASMLite_S{slot}" },
                        value     = 3f,
                        icon      = iconReset,
                    },
                };
                AssetDatabase.CreateAsset(slotMenu, slotPath);
            }

            // Force Unity to import the newly created sub-menu assets before
            // LoadAssetAtPath is called. Without this, the files exist on disk but
            // are not yet registered in the asset database, causing LoadAssetAtPath
            // to return null and leaving the root menu with null subMenu references
            // on the first build.
            AssetDatabase.Refresh();

            // ── Rebuild root menu entries in-place ────────────────────────────
            rootMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            for (int slot = 1; slot <= slotCount; slot++)
            {
                string slotPath = $"{generatedDir}/ASMLite_Slot{slot}_Menu.asset";
                var slotMenu    = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(slotPath);

                rootMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name    = $"Preset {slot}",
                    type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = slotMenu,
                    icon    = iconPresets,
                });
            }

            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ASM-Lite] PopulateExpressionMenu: generated root + {slotCount} slot menus + {slotCount} confirm menus.");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Maps a VRCExpressionParameters ValueType to the corresponding
        /// AnimatorControllerParameterType. Uses ValueType (not ParameterType) —
        /// the enum is on VRCExpressionParameters, not on the parameter struct.
        /// </summary>
        private static AnimatorControllerParameterType MapValueType(VRCExpressionParameters.ValueType vt)
        {
            switch (vt)
            {
                case VRCExpressionParameters.ValueType.Int:   return AnimatorControllerParameterType.Int;
                case VRCExpressionParameters.ValueType.Float: return AnimatorControllerParameterType.Float;
                case VRCExpressionParameters.ValueType.Bool:  return AnimatorControllerParameterType.Bool;
                default:                                      return AnimatorControllerParameterType.Float;
            }
        }

        /// <summary>
        /// Adds an immediate condition-based transition between two animator states.
        /// hasExitTime = false, duration = 0.
        /// </summary>
        private static void AddConditionTransition(
            AnimatorState from, AnimatorState to,
            string param, AnimatorConditionMode mode, int threshold)
        {
            var t = from.AddTransition(to);
            t.hasExitTime    = false;
            t.exitTime       = 0f;
            t.duration       = 0f;
            t.hasFixedDuration = true;
            t.AddCondition(mode, threshold, param);
        }

        /// <summary>
        /// Adds an exit-time transition with exitTime = 0 (immediate after one frame).
        /// Used for action → Idle returns.
        /// </summary>
        private static void AddExitTimeTransition(AnimatorState from, AnimatorState to)
        {
            var t = from.AddTransition(to);
            t.hasExitTime    = true;
            t.exitTime       = 0f;
            t.duration       = 0f;
            t.hasFixedDuration = true;
        }
    }
}
