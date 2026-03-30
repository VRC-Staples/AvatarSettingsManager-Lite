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
    /// slot layers with Save/Load/Clear Preset states using VRCAvatarParameterDriver Copy
    /// operations, and writes synced control parameters to ASMLite_Params.asset.
    ///
    /// ControlScheme.SafeBool   — 3 Bool params per slot (ASMLite_S{slot}_Save/Load/Clear)
    ///                            transitions use AnimatorConditionMode.If
    /// ControlScheme.CompactInt — 1 shared Int param (ASMLite_Ctrl) for all slots
    ///                            transitions use AnimatorConditionMode.Equals with
    ///                            encoded values (slot-1)*3+1/2/3
    ///
    /// All generated content is written into the existing stub assets in-place,
    /// preserving their stable GUIDs.
    /// </summary>
    public static class ASMLiteBuilder
    {
        // ─── Asset paths — see ASMLiteAssetPaths for centralized constants ───

        // ─── Icon paths ───────────────────────────────────────────────────────

        private const string IconSavePath    = "Packages/com.staples.asm-lite/Icons/Save.png";
        private const string IconLoadPath    = "Packages/com.staples.asm-lite/Icons/Load.png";
        private const string IconResetPath   = "Packages/com.staples.asm-lite/Icons/Reset.png";
        private const string IconPresetsPath = "Packages/com.staples.asm-lite/Icons/Gears/BlueGear.png";

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
#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] Discovered {discoveredParams.Count} custom parameters for '{component.gameObject.name}'.");
#endif

            // 5. Warn if zero params (layers will be generated with empty Copy lists)
            if (discoveredParams.Count == 0)
            {
                Debug.LogWarning($"[ASM-Lite] No custom parameters discovered. FX layers will be generated with empty driver lists.");
            }

            // 6–8. Generate assets — pass controlScheme to each Populate method so
            //      the correct parameter encoding is applied consistently across
            //      FX controller, expression params, and expression menu.
            PopulateFXController(discoveredParams, component.slotCount, component.controlScheme);
            PopulateExpressionParams(component.slotCount, discoveredParams, component.controlScheme);
            PopulateExpressionMenu(component);

            // 9. Flush all dirty assets in one batch write
            AssetDatabase.SaveAssets();

            // 10. Log completion
#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] Build complete for '{component.gameObject.name}': {component.slotCount} slots, {discoveredParams.Count} parameters backed up.");
#endif
        }

        /// <summary>
        /// Removes any previously generated assets. Currently a no-op; reserved for
        /// future cleanup passes if slot count changes between builds.
        /// </summary>
        public static void CleanupGeneratedAssets(ASMLiteComponent component)
        {
#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] CleanupGeneratedAssets called for '{component.gameObject.name}' — no cleanup required (in-place mutation model).");
#endif
        }

        /// <summary>
        /// Validates the component configuration. Returns null if valid, or an error
        /// message string if invalid.
        /// </summary>
        public static string Validate(ASMLiteComponent component)
        {
            if (component.slotCount < 1 || component.slotCount > 8)
                return $"[ASM-Lite] slotCount must be between 1 and 8 (got {component.slotCount}).";
            return null;
        }

        // ─── Private implementation ────────────────────────────────────────────

        /// <summary>
        /// Clears and regenerates all parameters and layers in the managed FX
        /// AnimatorController, then marks the asset dirty for the consolidated
        /// SaveAssets call in Build().
        ///
        /// ControlScheme determines which control parameters are added:
        ///   SafeBool   — 3 Bool params per slot: ASMLite_S{slot}_Save/Load/Clear
        ///   CompactInt — 1 shared Int param: ASMLite_Ctrl (added once, before slot loop)
        /// </summary>
        private static void PopulateFXController(List<VRCExpressionParameters.Parameter> avatarParams, int slotCount, ControlScheme scheme)
        {
            var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            if (ctrl == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load AnimatorController at '{ASMLiteAssetPaths.FXController}'.");
                return;
            }

            // Clear existing layers (iterate backwards to avoid index shifting)
            for (int i = ctrl.layers.Length - 1; i >= 0; i--)
                ctrl.RemoveLayer(i);

            // Clear existing parameters
            var existingParams = ctrl.parameters;
            foreach (var p in existingParams)
                ctrl.RemoveParameter(p);

            // Add slot control parameters — branched on ControlScheme
            if (scheme == ControlScheme.SafeBool)
            {
                // SafeBool: 3 Bool params per slot — Save, Load, Clear
                for (int slot = 1; slot <= slotCount; slot++)
                {
                    ctrl.AddParameter($"ASMLite_S{slot}_Save",  AnimatorControllerParameterType.Bool);
                    ctrl.AddParameter($"ASMLite_S{slot}_Load",  AnimatorControllerParameterType.Bool);
                    ctrl.AddParameter($"ASMLite_S{slot}_Clear", AnimatorControllerParameterType.Bool);
                }
            }
            else
            {
                // CompactInt: 1 shared Int param for all slots
                ctrl.AddParameter("ASMLite_Ctrl", AnimatorControllerParameterType.Int);
            }

            // Pre-compute mapped parameter types to avoid repeated MapValueType calls
            var mappedTypes = new AnimatorControllerParameterType[avatarParams.Count];
            for (int i = 0; i < avatarParams.Count; i++)
                mappedTypes[i] = MapValueType(avatarParams[i].valueType);

            // Add per-slot backup parameters: ASMLite_Bak_S{slot}_{paramName}
            for (int slot = 1; slot <= slotCount; slot++)
            {
                for (int i = 0; i < avatarParams.Count; i++)
                    ctrl.AddParameter($"ASMLite_Bak_S{slot}_{avatarParams[i].name}", mappedTypes[i]);
            }

            // Add default parameters (one set, not per-slot): ASMLite_Def_{paramName}
            for (int i = 0; i < avatarParams.Count; i++)
            {
                var p = avatarParams[i];
                var acp = new AnimatorControllerParameter
                {
                    name = $"ASMLite_Def_{p.name}",
                    type = mappedTypes[i]
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

            // Generate one layer per slot — pass scheme so transitions are encoded correctly
            for (int slot = 1; slot <= slotCount; slot++)
                AddSlotLayer(ctrl, slot, avatarParams, scheme);

            EditorUtility.SetDirty(ctrl);
            // SaveAssets is called once in Build() after all three Populate methods complete.
        }

        /// <summary>
        /// Builds one FX animator layer for the given slot with Idle, SaveSlot,
        /// LoadSlot, and ResetSlot states, each backed by a VRCAvatarParameterDriver.
        /// ResetSlot clears the slot's backup parameters to defaults without
        /// touching the live avatar parameters.
        ///
        /// ControlScheme determines transition conditions and driver reset entries:
        ///   SafeBool   — transitions use AnimatorConditionMode.If on the per-slot Bool params;
        ///                driver resets add 3 Set entries (one per Bool param, value=0f)
        ///   CompactInt — transitions use AnimatorConditionMode.Equals on ASMLite_Ctrl with
        ///                encoded values (slot-1)*3+1/2/3; driver resets add 1 Set entry (value=0f)
        /// </summary>
        private static void AddSlotLayer(AnimatorController ctrl, int slot, List<VRCExpressionParameters.Parameter> avatarParams, ControlScheme scheme)
        {
            string slotName = $"ASMLite_Slot{slot}";

            // Zone A — control param names / encoded values, resolved per scheme
            string saveParam;
            string loadParam;
            string clearParam;
            string controlParam;
            int    saveValue;
            int    loadValue;
            int    clearValue;

            if (scheme == ControlScheme.SafeBool)
            {
                saveParam   = $"ASMLite_S{slot}_Save";
                loadParam   = $"ASMLite_S{slot}_Load";
                clearParam  = $"ASMLite_S{slot}_Clear";
                // Not used in SafeBool, assigned to satisfy compiler
                controlParam = saveParam;
                saveValue    = 1;
                loadValue    = 1;
                clearValue   = 1;
            }
            else
            {
                // CompactInt: single shared param, encoded values
                controlParam = "ASMLite_Ctrl";
                saveValue    = (slot - 1) * 3 + 1;
                loadValue    = (slot - 1) * 3 + 2;
                clearValue   = (slot - 1) * 3 + 3;
                // Not used in CompactInt path, assigned to satisfy compiler
                saveParam  = controlParam;
                loadParam  = controlParam;
                clearParam = controlParam;
            }

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

            var saveDriver  = saveState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            var loadDriver  = loadState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();
            var resetDriver = resetState.AddStateMachineBehaviour<VRCAvatarParameterDriver>();

            // Pre-size all three lists and build in a single pass to avoid 3x iteration
            var saveParams  = new List<VRC_AvatarParameterDriver.Parameter>(avatarParams.Count + 3);
            var loadParams  = new List<VRC_AvatarParameterDriver.Parameter>(avatarParams.Count + 3);
            var resetParams = new List<VRC_AvatarParameterDriver.Parameter>(avatarParams.Count + 3);

            for (int i = 0; i < avatarParams.Count; i++)
            {
                var p = avatarParams[i];
                saveParams.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = p.name,
                    name   = $"ASMLite_Bak_S{slot}_{p.name}",
                });
                loadParams.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = $"ASMLite_Bak_S{slot}_{p.name}",
                    name   = p.name,
                });
                // Clear the slot's backup param to default so a subsequent
                // Load on this slot returns defaults instead of stale saved values.
                // Live avatar params are NOT touched — only the saved preset is cleared.
                resetParams.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = $"ASMLite_Def_{p.name}",
                    name   = $"ASMLite_Bak_S{slot}_{p.name}",
                });
            }

            // Zone B — trailing Set entries reset the control parameter(s) back to Idle
            // SafeBool:   3 Set entries per driver (one per Bool param, value = 0f)
            // CompactInt: 1 Set entry per driver for ASMLite_Ctrl, value = 0f
            if (scheme == ControlScheme.SafeBool)
            {
                var saveBoolReset = new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = $"ASMLite_S{slot}_Save",
                    value = 0f,
                };
                var loadBoolReset = new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = $"ASMLite_S{slot}_Load",
                    value = 0f,
                };
                var clearBoolReset = new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = $"ASMLite_S{slot}_Clear",
                    value = 0f,
                };
                // Each driver resets all three bools so any bool is back to false after
                // the action completes, regardless of which action was triggered.
                saveParams.Add(saveBoolReset);
                saveParams.Add(loadBoolReset);
                saveParams.Add(clearBoolReset);
                loadParams.Add(saveBoolReset);
                loadParams.Add(loadBoolReset);
                loadParams.Add(clearBoolReset);
                resetParams.Add(saveBoolReset);
                resetParams.Add(loadBoolReset);
                resetParams.Add(clearBoolReset);
            }
            else
            {
                // CompactInt: single reset entry for ASMLite_Ctrl
                var resetCtrlEntry = new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = "ASMLite_Ctrl",
                    value = 0f,
                };
                saveParams.Add(resetCtrlEntry);
                loadParams.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = "ASMLite_Ctrl",
                    value = 0f,
                });
                resetParams.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type  = VRC_AvatarParameterDriver.ChangeType.Set,
                    name  = "ASMLite_Ctrl",
                    value = 0f,
                });
            }

            saveDriver.parameters  = saveParams;
            loadDriver.parameters  = loadParams;
            resetDriver.parameters = resetParams;

            // ── Zone C — Transitions from Idle ───────────────────────────────
            // SafeBool:   AnimatorConditionMode.If  on per-slot Bool params
            // CompactInt: AnimatorConditionMode.Equals on ASMLite_Ctrl with encoded values
            if (scheme == ControlScheme.SafeBool)
            {
                AddConditionTransition(idleState, saveState,  saveParam,  AnimatorConditionMode.If, 1);
                AddConditionTransition(idleState, loadState,  loadParam,  AnimatorConditionMode.If, 1);
                AddConditionTransition(idleState, resetState, clearParam, AnimatorConditionMode.If, 1);
            }
            else
            {
                AddConditionTransition(idleState, saveState,  controlParam, AnimatorConditionMode.Equals, saveValue);
                AddConditionTransition(idleState, loadState,  controlParam, AnimatorConditionMode.Equals, loadValue);
                AddConditionTransition(idleState, resetState, controlParam, AnimatorConditionMode.Equals, clearValue);
            }

            // Action states → Idle (exit-time at 0, immediate)
            AddExitTimeTransition(saveState,  idleState);
            AddExitTimeTransition(loadState,  idleState);
            AddExitTimeTransition(resetState, idleState);
        }

        /// <summary>
        /// Writes the following into the managed VRCExpressionParameters asset:
        ///   SafeBool scheme:
        ///     • 3×slotCount synced Bool control params (ASMLite_S{slot}_Save/Load/Clear)
        ///       saved: false, networkSynced: true  — momentary triggers, no persistence needed
        ///   CompactInt scheme:
        ///     • 1 synced Int control param (ASMLite_Ctrl)
        ///       saved: false, networkSynced: true  — momentary trigger, shared across all slots
        ///   Both schemes:
        ///     • slotCount × avatarParams backup params (ASMLite_Bak_S{slot}_{name})
        ///       saved: true, networkSynced: false  — persisted across world changes via
        ///       VRChat local storage; VRCFury Unlimited Parameters handles sync compression
        ///       so the raw bit cost is irrelevant.
        /// </summary>
        private static void PopulateExpressionParams(int slotCount, List<VRCExpressionParameters.Parameter> avatarParams, ControlScheme scheme)
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (paramsAsset == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load VRCExpressionParameters at '{ASMLiteAssetPaths.ExprParams}'.");
                return;
            }

            // Calculate total count based on scheme
            int controlParamCount = (scheme == ControlScheme.SafeBool) ? (slotCount * 3) : 1;
            int totalCount = controlParamCount + (slotCount * avatarParams.Count);
            var paramList = new List<VRCExpressionParameters.Parameter>(totalCount);

            // Control params — synced triggers, not saved — branched on scheme
            if (scheme == ControlScheme.SafeBool)
            {
                // SafeBool: 3 Bool params per slot
                for (int slot = 1; slot <= slotCount; slot++)
                {
                    paramList.Add(new VRCExpressionParameters.Parameter
                    {
                        name          = $"ASMLite_S{slot}_Save",
                        valueType     = VRCExpressionParameters.ValueType.Bool,
                        defaultValue  = 0f,
                        saved         = false,
                        networkSynced = true,
                    });
                    paramList.Add(new VRCExpressionParameters.Parameter
                    {
                        name          = $"ASMLite_S{slot}_Load",
                        valueType     = VRCExpressionParameters.ValueType.Bool,
                        defaultValue  = 0f,
                        saved         = false,
                        networkSynced = true,
                    });
                    paramList.Add(new VRCExpressionParameters.Parameter
                    {
                        name          = $"ASMLite_S{slot}_Clear",
                        valueType     = VRCExpressionParameters.ValueType.Bool,
                        defaultValue  = 0f,
                        saved         = false,
                        networkSynced = true,
                    });
                }
            }
            else
            {
                // CompactInt: 1 shared Int param for all slots
                paramList.Add(new VRCExpressionParameters.Parameter
                {
                    name          = "ASMLite_Ctrl",
                    valueType     = VRCExpressionParameters.ValueType.Int,
                    defaultValue  = 0f,
                    saved         = false,
                    networkSynced = true,
                });
            }

            // Backup params — saved locally, not synced
            // saved: true persists values across world changes via VRChat local storage.
            // networkSynced: false keeps them off the sync budget entirely;
            // VRCFury Unlimited Parameters handles any compression needed.
            for (int slot = 1; slot <= slotCount; slot++)
            {
                foreach (var p in avatarParams)
                {
                    paramList.Add(new VRCExpressionParameters.Parameter
                    {
                        name          = $"ASMLite_Bak_S{slot}_{p.name}",
                        valueType     = p.valueType,
                        defaultValue  = p.defaultValue,
                        saved         = true,
                        networkSynced = false,
                    });
                }
            }

            paramsAsset.parameters = paramList.ToArray();

            EditorUtility.SetDirty(paramsAsset);
            // SaveAssets is called once in Build() after all three Populate methods complete.
        }

        /// <summary>
        /// Generates the nested VRCExpressionsMenu tree at build time.
        /// Creates 2 + (slotCount * 3) menu assets total:
        ///   1 root (mutated in-place to preserve stable GUID) +
        ///   1 ASM-Lite wrapper menu +
        ///   slotCount slot sub-menus +
        ///   slotCount confirm sub-menus (Save confirmation) +
        ///   slotCount reset-confirm sub-menus (Clear Preset confirmation).
        ///
        /// ControlScheme determines the parameter names and values in menu buttons:
        ///   SafeBool   — Save: ASMLite_S{slot}_Save=1, Load: ASMLite_S{slot}_Load=1,
        ///                      Clear: ASMLite_S{slot}_Clear=1
        ///   CompactInt — Save: ASMLite_Ctrl=(slot-1)*3+1, Load: ASMLite_Ctrl=(slot-1)*3+2,
        ///                      Clear: ASMLite_Ctrl=(slot-1)*3+3
        ///
        /// Menu hierarchy:
        ///   root
        ///     └─ ASM-Lite  (SubMenu → presetsMenu)
        ///          └─ Preset N  (SubMenu → slotMenu)
        ///               ├─ Save   (SubMenu → confirmMenu)
        ///               │    └─ Confirm  (Button, scheme-appropriate param)
        ///               ├─ Load   (Button, scheme-appropriate param)
        ///               └─ Clear Preset  (SubMenu → resetConfirmMenu)
        ///                    └─ Confirm  (Button, scheme-appropriate param)
        ///
        /// Asset operations are batched with StartAssetEditing/StopAssetEditing (in a
        /// try/finally) so Unity imports all created assets in one pass rather than
        /// triggering an import cycle per CreateAsset call. In-memory ScriptableObject
        /// references are used throughout — no LoadAssetAtPath reload after CreateAsset.
        /// </summary>
        private static void PopulateExpressionMenu(ASMLiteComponent component)
        {
            int slotCount = component.slotCount;
            var scheme    = component.controlScheme;

            // ── Load icons BEFORE StartAssetEditing (LoadAssetAtPath must run outside
            //    the edit batch or the asset database may not resolve paths correctly) ──
            var iconSave    = AssetDatabase.LoadAssetAtPath<Texture2D>(IconSavePath);
            var iconLoad    = AssetDatabase.LoadAssetAtPath<Texture2D>(IconLoadPath);
            var iconReset   = AssetDatabase.LoadAssetAtPath<Texture2D>(IconResetPath);
            var iconPresets = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPresetsPath);

            if (iconSave == null)
                Debug.LogWarning("[ASM-Lite] Save icon not found at " + IconSavePath + " — controls will have no icon.");

            // ── Build per-slot icon array BEFORE StartAssetEditing ───────────
            var slotIcons = new Texture2D[slotCount];
            for (int slot = 1; slot <= slotCount; slot++)
                slotIcons[slot - 1] = ResolveSlotIcon(component, slot, iconPresets);

            // ── Load root menu in-place BEFORE the batch (preserves stable GUID) ──
            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            if (rootMenu == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load VRCExpressionsMenu at '{ASMLiteAssetPaths.Menu}'.");
                return;
            }

            string generatedDir    = ASMLiteAssetPaths.GeneratedDir;
            string presetsMenuPath = $"{generatedDir}/ASMLite_Presets_Menu.asset";

            // In-memory arrays for references used outside the batch block.
            var confirmMenus      = new VRCExpressionsMenu[slotCount];
            var resetConfirmMenus = new VRCExpressionsMenu[slotCount];
            var slotMenus         = new VRCExpressionsMenu[slotCount];

            // ── Batch all delete/create operations to avoid per-asset import cycles ──
            try
            {
                AssetDatabase.StartAssetEditing();

                // Delete any existing presets wrapper (safe no-op if path doesn't exist)
                AssetDatabase.DeleteAsset(presetsMenuPath);

                for (int slot = 1; slot <= slotCount; slot++)
                {
                    string slotPath         = $"{generatedDir}/ASMLite_Slot{slot}_Menu.asset";
                    string confirmPath      = $"{generatedDir}/ASMLite_Slot{slot}_ConfirmMenu.asset";
                    string resetConfirmPath = $"{generatedDir}/ASMLite_Slot{slot}_ResetConfirmMenu.asset";

                    // Unconditional deletes — AssetDatabase.DeleteAsset is a safe no-op
                    // on paths that don't exist, so no existence check is needed.
                    AssetDatabase.DeleteAsset(slotPath);
                    AssetDatabase.DeleteAsset(confirmPath);
                    AssetDatabase.DeleteAsset(resetConfirmPath);

                    // Resolve scheme-specific param names and values for this slot
                    string saveParamName;
                    float  saveParamValue;
                    string loadParamName;
                    float  loadParamValue;
                    string clearParamName;
                    float  clearParamValue;

                    if (scheme == ControlScheme.SafeBool)
                    {
                        saveParamName  = $"ASMLite_S{slot}_Save";
                        saveParamValue = 1f;
                        loadParamName  = $"ASMLite_S{slot}_Load";
                        loadParamValue = 1f;
                        clearParamName = $"ASMLite_S{slot}_Clear";
                        clearParamValue = 1f;
                    }
                    else
                    {
                        saveParamName  = "ASMLite_Ctrl";
                        saveParamValue = (float)((slot - 1) * 3 + 1);
                        loadParamName  = "ASMLite_Ctrl";
                        loadParamValue = (float)((slot - 1) * 3 + 2);
                        clearParamName  = "ASMLite_Ctrl";
                        clearParamValue = (float)((slot - 1) * 3 + 3);
                    }

                    // ── Save confirm sub-menu ─────────────────────────────────────
                    var confirmMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    confirmMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>
                    {
                        new VRCExpressionsMenu.Control
                        {
                            name      = "Confirm",
                            type      = VRCExpressionsMenu.Control.ControlType.Button,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = saveParamName },
                            value     = saveParamValue,
                            icon      = iconSave,
                        }
                    };
                    AssetDatabase.CreateAsset(confirmMenu, confirmPath);
                    confirmMenus[slot - 1] = confirmMenu;

                    // ── Reset confirm sub-menu ────────────────────────────────────
                    var resetConfirmMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    resetConfirmMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>
                    {
                        new VRCExpressionsMenu.Control
                        {
                            name      = "Confirm",
                            type      = VRCExpressionsMenu.Control.ControlType.Button,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = clearParamName },
                            value     = clearParamValue,
                            icon      = iconReset,
                        }
                    };
                    AssetDatabase.CreateAsset(resetConfirmMenu, resetConfirmPath);
                    resetConfirmMenus[slot - 1] = resetConfirmMenu;

                    // ── Slot sub-menu (Save / Load / Clear Preset) ───────────────────────
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
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = loadParamName },
                            value     = loadParamValue,
                            icon      = iconLoad,
                        },
                        new VRCExpressionsMenu.Control
                        {
                            name    = "Clear Preset",
                            type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                            subMenu = resetConfirmMenu,
                            icon    = iconReset,
                        },
                    };
                    AssetDatabase.CreateAsset(slotMenu, slotPath);
                    slotMenus[slot - 1] = slotMenu;
                }
            }
            finally
            {
                // StopAssetEditing is in a finally block so Unity's edit-batch counter is
                // always decremented — even if an exception fires mid-loop. Forgetting this
                // would leave the Editor in a frozen "editing" state requiring a restart.
                AssetDatabase.StopAssetEditing();
            }

            // ── Build the ASM-Lite wrapper menu using in-memory slot references ──
            // StopAssetEditing() has processed the batch; in-memory ScriptableObject
            // references are valid — no LoadAssetAtPath reload needed.
            var presetsMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            presetsMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            for (int slot = 1; slot <= slotCount; slot++)
            {
                presetsMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name    = $"Preset {slot}",
                    type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = slotMenus[slot - 1], // in-memory reference, not reloaded from disk
                    icon    = slotIcons[slot - 1],
                });
            }
            AssetDatabase.CreateAsset(presetsMenu, presetsMenuPath);
            // presetsMenu in-memory reference remains valid — no reload needed.

            // ── Point root at the ASM-Lite wrapper (single entry) ────────────
            // Root is mutated in-place so its stable GUID (referenced by VRCFury)
            // is never broken.
            rootMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name    = "ASM-Lite",
                    type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = presetsMenu,
                    icon    = iconPresets,
                }
            };

            EditorUtility.SetDirty(rootMenu);
            // SaveAssets is called once in Build() after all three Populate methods complete.

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] PopulateExpressionMenu: generated root + ASM-Lite wrapper + {slotCount} slot menus + {slotCount} confirm menus.");
#endif
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
                default:
                    Debug.LogWarning($"[ASM-Lite] Unknown VRCExpressionParameters.ValueType '{vt}'. Defaulting to Float.");
                    return AnimatorControllerParameterType.Float;
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

        /// <summary>
        /// Resolves the icon for a given slot based on the component's iconMode.
        ///   SameColor  — all slots use the single gear icon at selectedGearIndex.
        ///   MultiColor — each slot cycles through GearIconPaths by index.
        ///   Custom     — uses the user-supplied texture from customIcons[slot-1],
        ///                falling back to <paramref name="fallback"/> if null/out-of-range.
        ///   default    — returns <paramref name="fallback"/>.
        /// All LoadAssetAtPath calls are expected to run before StartAssetEditing.
        /// </summary>
        private static Texture2D ResolveSlotIcon(ASMLiteComponent component, int slot, Texture2D fallback)
        {
            switch (component.iconMode)
            {
                case IconMode.SameColor:
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        ASMLiteAssetPaths.GearIconPaths[component.selectedGearIndex]);
                    return tex != null ? tex : fallback;
                }
                case IconMode.MultiColor:
                {
                    var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        ASMLiteAssetPaths.GearIconPaths[(slot - 1) % ASMLiteAssetPaths.GearIconPaths.Length]);
                    return tex != null ? tex : fallback;
                }
                case IconMode.Custom:
                {
                    int index = slot - 1;
                    if (component.customIcons != null
                        && index < component.customIcons.Length
                        && component.customIcons[index] != null)
                        return component.customIcons[index];
                    return fallback;
                }
                default:
                    return fallback;
            }
        }
    }
}
