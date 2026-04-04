// Add ASM_LITE_VERBOSE to Edit > Project Settings > Player > Scripting Define Symbols
// to enable verbose build logging throughout this file.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ASMLite;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLiteBuilder: static editor utility for build-time asset generation.
    ///
    /// Called from ASMLiteComponent.Preprocess() during the VRChat SDK avatar build
    /// pipeline. Discovers all custom avatar parameters, generates 3 FX animator
    /// slot layers with Save/Load/Clear Preset states using VRCAvatarParameterDriver Copy
    /// operations, and writes local control parameters plus local backup parameters to ASMLite_Params.asset.
    ///
    /// Control trigger model: one shared local Int param (ASMLite_Ctrl) for all slots,
    /// with encoded values (slot-1)*3+1/2/3 for Save/Load/Clear.
    ///
    /// All generated content is written into the existing stub assets in-place,
    /// preserving their stable GUIDs.
    ///
    /// Parameter discovery uses a dry-clone build: a temporary copy of the avatar
    /// (minus ASM-Lite components) is instantiated, VRCFury's RunMain is invoked
    /// on it via reflection, and the resulting merged expression parameters are
    /// read back. This ensures VRCFury Toggle and FullController parameters (which
    /// are injected at preprocess order 0, after ASM-Lite's order -10) are captured
    /// correctly regardless of build order. The clone is destroyed immediately after.
    /// </summary>
    public static class ASMLiteBuilder
    {
        // ─── Reflection cache: VRCFuryBuilder.RunMain ───────────────────────

        private static MethodInfo s_vrcfuryRunMain;
        private static bool       s_vrcfuryRunMainSearched;

        /// <summary>
        /// Resolves VRCFuryBuilder.RunMain(VFGameObject) via reflection and caches it.
        /// Returns null if VRCFury is not installed or the method signature changed.
        /// </summary>
        private static MethodInfo GetVRCFuryRunMain()
        {
            if (s_vrcfuryRunMainSearched)
                return s_vrcfuryRunMain;

            s_vrcfuryRunMainSearched = true;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("VF.Builder.VRCFuryBuilder");
                    if (t == null) continue;

                    // RunMain(VFGameObject avatarObject): internal static
                    var m = t.GetMethod("RunMain",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null)
                    {
                        s_vrcfuryRunMain = m;
                        break;
                    }
                }
                catch (ReflectionTypeLoadException) { }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ASM-Lite] Error scanning assembly for VRCFuryBuilder: {ex.Message}");
                }
            }

            return s_vrcfuryRunMain;
        }

        // ─── Public API ───────────────────────────────────────────────────────

        /// <summary>
        /// Discovers the post-VRCFury parameter set for an avatar by running a
        /// temporary clone build. Instantiates a scene copy of the avatar, removes
        /// all ASMLiteComponent instances from it, calls VRCFury's RunMain on the
        /// clone to let VRCFury inject its own parameters, then reads the merged
        /// expressionParameters back. The clone is destroyed in a finally block.
        ///
        /// Returns the discovered parameters (excluding any ASMLite_ prefixed names),
        /// or falls back to the avatar's base expression parameters if VRCFury is not
        /// installed or the clone build throws.
        ///
        /// VFGameObject is VRCFury's internal scene-object wrapper. RunMain accepts
        /// a VFGameObject but VFGameObject has an implicit cast from GameObject.
        /// We pass the clone's root GameObject and rely on that cast via reflection.
        /// </summary>
        public static List<VRCExpressionParameters.Parameter> DiscoverParametersViaCloneBuild(
            VRCAvatarDescriptor avDesc)
        {
            var runMain = GetVRCFuryRunMain();

            if (runMain == null)
            {
                Debug.LogWarning("[ASM-Lite] VRCFuryBuilder.RunMain not found: falling back to base expression parameters. VRCFury Toggle/FullController parameters will not be discovered.");
                return ReadBaseParams(avDesc);
            }

            GameObject clone = null;
            try
            {
                // Instantiate a full copy of the avatar in the scene so RunMain has
                // a valid scene object with all its component references intact.
                clone = UnityEngine.Object.Instantiate(avDesc.gameObject);
                clone.name = avDesc.gameObject.name + "__ASMLiteDiscovery";
                clone.SetActive(false); // keep it invisible during the build

                // Remove every ASMLiteComponent from the clone so ASM-Lite's own
                // preprocess callback does not fire recursively on the clone.
                foreach (var c in clone.GetComponentsInChildren<ASMLiteComponent>(includeInactive: true))
                    UnityEngine.Object.DestroyImmediate(c);

#if ASM_LITE_VERBOSE
                Debug.Log($"[ASM-Lite] DiscoverParametersViaCloneBuild: running VRCFury on clone '{clone.name}'.");
#endif

                // RunMain(VFGameObject): VFGameObject has an implicit operator from
                // GameObject, but since we're calling via reflection with object[], we
                // pass the GameObject directly. VFGameObject's implicit cast is a static
                // operator and MethodInfo.Invoke performs no implicit conversions, so we
                // must wrap it manually via the VFGameObject type if it exists.
                // Simplest safe path: find VFGameObject and call its implicit cast, or
                // just pass the GameObject and let the runtime invoke the implicit op.
                // In practice, passing a GameObject to a VFGameObject parameter via
                // reflection works when VFGameObject defines `implicit operator VFGameObject(GameObject)`.
                // We verify this works by checking for that operator and using it explicitly.
                object cloneArg = WrapAsVFGameObject(clone);
                runMain.Invoke(null, new[] { cloneArg });

                // VRCFury has now merged all Toggle/FullController parameters into
                // the clone's VRCAvatarDescriptor.expressionParameters.
                var cloneDesc = clone.GetComponent<VRCAvatarDescriptor>();
                return ReadBaseParams(cloneDesc);
            }
            catch (TargetInvocationException tie)
            {
                Debug.LogWarning($"[ASM-Lite] VRCFury clone build failed: falling back to base expression parameters.\n{(tie.InnerException ?? tie).Message}");
                return ReadBaseParams(avDesc);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] DiscoverParametersViaCloneBuild failed: falling back to base expression parameters.\n{ex.Message}");
                return ReadBaseParams(avDesc);
            }
            finally
            {
                if (clone != null)
                    UnityEngine.Object.DestroyImmediate(clone);
            }
        }

        /// <summary>
        /// Attempts to wrap a GameObject as a VFGameObject by finding and invoking
        /// VFGameObject's implicit operator. If VFGameObject is not found, returns
        /// the GameObject directly (works when RunMain accepts a base type or when
        /// the runtime can resolve the implicit cast).
        /// </summary>
        private static object WrapAsVFGameObject(GameObject go)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType("VF.Utils.VFGameObject");
                    if (t == null) continue;

                    // Look for: public static implicit operator VFGameObject(GameObject go)
                    var op = t.GetMethod("op_Implicit",
                        BindingFlags.Static | BindingFlags.Public,
                        null,
                        new[] { typeof(GameObject) },
                        null);
                    if (op != null)
                        return op.Invoke(null, new object[] { go });

                    break;
                }
                catch (ReflectionTypeLoadException) { }
            }
            // Fallback: pass the GameObject directly. RunMain will throw if it can't
            // accept it, and the caller's catch will fall back to base params.
            return go;
        }

        /// <summary>
        /// Reads expression parameters from a VRCAvatarDescriptor, filtering out
        /// empty names and any ASMLite_-prefixed parameters.
        /// </summary>
        private static List<VRCExpressionParameters.Parameter> ReadBaseParams(VRCAvatarDescriptor avDesc)
        {
            var exprParams = avDesc?.expressionParameters;
            var result = new List<VRCExpressionParameters.Parameter>();
            if (exprParams?.parameters == null)
                return result;

            foreach (var p in exprParams.parameters)
            {
                if (string.IsNullOrEmpty(p.name))
                    continue;
                if (p.name.StartsWith("ASMLite_"))
                {
                    Debug.LogWarning($"[ASM-Lite] Skipping expression parameter '{p.name}': already prefixed with 'ASMLite_'. Remove it from the avatar's expression parameters to avoid conflicts.");
                    continue;
                }
                result.Add(p);
            }
            return result;
        }

        /// <summary>
        /// Entry point called during avatar build preprocessing.
        /// Discovers custom avatar parameters via a temporary VRCFury clone build,
        /// then generates all slot assets.
        /// </summary>
        public static int Build(ASMLiteComponent component)
        {
            // 0. Validate configuration -- catches bad state from non-window entry points
            //    (e.g. OnPreprocess during avatar upload where the window slider is bypassed).
            string validationError = Validate(component);
            if (validationError != null)
            {
                Debug.LogError(validationError);
                return -1;
            }

            // 1. Find avatar descriptor
            var avDesc = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (avDesc == null)
            {
                Debug.LogError($"[ASM-Lite] Build failed: no VRCAvatarDescriptor found in parent hierarchy of '{component.gameObject.name}'.");
                return -1;
            }

            if (avDesc.expressionParameters == null)
            {
                Debug.LogWarning($"[ASM-Lite] No expressionParameters asset assigned on VRCAvatarDescriptor '{avDesc.gameObject.name}'. Generating empty layers.");
            }

            // 2. Discover parameters via clone build so VRCFury Toggle and
            //    FullController parameters are included even though VRCFury runs
            //    after ASM-Lite in the preprocess order.
            var discoveredParams = DiscoverParametersViaCloneBuild(avDesc);

            // 3. Log discovery
#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] Discovered {discoveredParams.Count} custom parameters for '{component.gameObject.name}'.");
#endif

            // 4. Warn if zero params (layers will be generated with empty Copy lists)
            if (discoveredParams.Count == 0)
            {
                Debug.LogWarning($"[ASM-Lite] No custom parameters discovered. FX layers will be generated with empty driver lists.");
            }

            // 5-7. Generate assets.
            PopulateFXController(discoveredParams, component.slotCount);
            PopulateExpressionParams(component.slotCount, discoveredParams);
            PopulateExpressionMenu(component);

            // 8. Flush all dirty assets in one batch write, then force a synchronous
            //    re-import so VRCFury reads the freshly written params: not the
            //    stale in-memory state from a previous build session.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 9. Log completion
#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] Build complete for '{component.gameObject.name}': {component.slotCount} slots, {discoveredParams.Count} parameters backed up.");
#endif

            return discoveredParams.Count;
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
        /// Generates control parameters and backup/default parameters in the managed FX controller.
        /// Uses one shared local control Int (ASMLite_Ctrl) for all slot actions.
        /// </summary>
        private static void PopulateFXController(List<VRCExpressionParameters.Parameter> avatarParams, int slotCount)
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

            // Clear existing parameters.
            // Iterate until empty rather than foreach: RemoveParameter modifies the
            // underlying list, and a stale controller may contain duplicate-named entries
            // (from an older build) where a single-pass foreach leaves stragglers.
            while (ctrl.parameters.Length > 0)
                ctrl.RemoveParameter(ctrl.parameters[0]);

            // Add one shared control parameter for all slots.
            ctrl.AddParameter("ASMLite_Ctrl", AnimatorControllerParameterType.Int);

            // Pre-compute mapped parameter types to avoid repeated MapValueType calls
            var mappedTypes = new AnimatorControllerParameterType[avatarParams.Count];
            for (int i = 0; i < avatarParams.Count; i++)
                mappedTypes[i] = MapValueType(avatarParams[i].valueType);

            // Add per-slot backup parameters: ASMLite_Bak_S{slot}_{paramName}
            // Track names added to guard against duplicate avatar param names.
            var addedParams = new HashSet<string>();
            for (int slot = 1; slot <= slotCount; slot++)
            {
                for (int i = 0; i < avatarParams.Count; i++)
                {
                    string bakName = $"ASMLite_Bak_S{slot}_{avatarParams[i].name}";
                    if (addedParams.Add(bakName))
                        ctrl.AddParameter(bakName, mappedTypes[i]);
                    else
                        Debug.LogWarning($"[ASM-Lite] Duplicate FX parameter skipped: '{bakName}'");
                }
            }

            // Add default parameters (one set, not per-slot): ASMLite_Def_{paramName}
            for (int i = 0; i < avatarParams.Count; i++)
            {
                var p = avatarParams[i];
                string defName = $"ASMLite_Def_{p.name}";
                if (!addedParams.Add(defName))
                {
                    Debug.LogWarning($"[ASM-Lite] Duplicate FX parameter skipped: '{defName}'");
                    continue;
                }

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

            // Generate one layer per slot.
            for (int slot = 1; slot <= slotCount; slot++)
                AddSlotLayer(ctrl, slot, avatarParams);

            EditorUtility.SetDirty(ctrl);
            // SaveAssets is called once in Build() after all three Populate methods complete.
        }

        /// <summary>
        /// Builds one FX animator layer for the given slot with Idle, SaveSlot,
        /// LoadSlot, and ResetSlot states, each backed by a VRCAvatarParameterDriver.
        /// ResetSlot clears the slot's backup parameters to defaults without
        /// touching the live avatar parameters.
        ///
        /// Uses a single shared control Int (ASMLite_Ctrl) with encoded values:
        /// Save=(slot-1)*3+1, Load=(slot-1)*3+2, Clear=(slot-1)*3+3.
        /// </summary>
        private static void AddSlotLayer(AnimatorController ctrl, int slot, List<VRCExpressionParameters.Parameter> avatarParams)
        {
            string slotName = $"ASMLite_Slot{slot}";

            // Control encoding for this slot (shared Int trigger param)
            string controlParam = "ASMLite_Ctrl";
            int    saveValue    = (slot - 1) * 3 + 1;
            int    loadValue    = (slot - 1) * 3 + 2;
            int    clearValue   = (slot - 1) * 3 + 3;

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

            var seenDriverParams = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < avatarParams.Count; i++)
            {
                var p = avatarParams[i];
                if (p == null || string.IsNullOrWhiteSpace(p.name))
                    continue;

                // Guard against duplicate-discovered parameter names. Duplicate names
                // can cause VRCAvatarParameterDriver parameter assignment to throw.
                if (!seenDriverParams.Add(p.name))
                {
                    Debug.LogWarning($"[ASM-Lite] Duplicate discovered parameter skipped in slot driver: '{p.name}'");
                    continue;
                }

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
                // Live avatar params are NOT touched: only the saved preset is cleared.
                resetParams.Add(new VRC_AvatarParameterDriver.Parameter
                {
                    type   = VRC_AvatarParameterDriver.ChangeType.Copy,
                    source = $"ASMLite_Def_{p.name}",
                    name   = $"ASMLite_Bak_S{slot}_{p.name}",
                });
            }

            // Zone B: trailing Set entries reset shared control Int back to idle (0)
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

            saveDriver.parameters  = saveParams;
            loadDriver.parameters  = loadParams;
            resetDriver.parameters = resetParams;

            // Transitions from Idle using shared Int control encoding.
            AddConditionTransition(idleState, saveState,  controlParam, AnimatorConditionMode.Equals, saveValue);
            AddConditionTransition(idleState, loadState,  controlParam, AnimatorConditionMode.Equals, loadValue);
            AddConditionTransition(idleState, resetState, controlParam, AnimatorConditionMode.Equals, clearValue);

            // Action states → Idle (exit-time at 0, immediate)
            AddExitTimeTransition(saveState,  idleState);
            AddExitTimeTransition(loadState,  idleState);
            AddExitTimeTransition(resetState, idleState);
        }

        /// <summary>
        /// Builds the expression parameter payload for ASM-Lite.
        ///
        /// Generated params (controls + active backup schema) are always emitted first.
        /// Legacy backup params from an existing asset are appended if they are not part
        /// of the current schema. This preserves previously saved slot values when users
        /// increase slot count and also change avatar parameter schema in the same rebuild.
        ///
        /// Only legacy backup params are preserved. Legacy control params are not kept.
        /// If a generated backup name exists, generated wins and legacy is ignored.
        /// </summary>
        internal static List<string> BuildBackupParamNamesWithLegacyPreservation(
            int slotCount,
            List<string> avatarParamNames,
            string[] existingParamNames)
        {
            var names = new List<string>(slotCount * avatarParamNames.Count);
            var seen = new HashSet<string>();

            for (int slot = 1; slot <= slotCount; slot++)
            {
                foreach (var paramName in avatarParamNames)
                {
                    string name = $"ASMLite_Bak_S{slot}_{paramName}";
                    if (seen.Add(name))
                        names.Add(name);
                }
            }

            if (existingParamNames != null)
            {
                foreach (var existingName in existingParamNames)
                {
                    if (string.IsNullOrWhiteSpace(existingName))
                        continue;
                    if (!existingName.StartsWith("ASMLite_Bak_"))
                        continue;
                    if (seen.Add(existingName))
                        names.Add(existingName);
                }
            }

            return names;
        }

        /// <summary>
        /// Writes one local shared control trigger param (ASMLite_Ctrl) plus
        /// slot backup params into the managed VRCExpressionParameters asset.
        ///
        /// Legacy backup params from prior schemas are preserved when not colliding
        /// with the current schema to avoid dropping existing user presets.
        /// </summary>
        private static void PopulateExpressionParams(int slotCount, List<VRCExpressionParameters.Parameter> avatarParams)
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (paramsAsset == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load VRCExpressionParameters at '{ASMLiteAssetPaths.ExprParams}'.");
                return;
            }

            int totalCount = 1 + (slotCount * avatarParams.Count);
            var generated = new List<VRCExpressionParameters.Parameter>(totalCount);

            generated.Add(new VRCExpressionParameters.Parameter
            {
                name          = "ASMLite_Ctrl",
                valueType     = VRCExpressionParameters.ValueType.Int,
                defaultValue  = 0f,
                saved         = false,
                networkSynced = false,
            });

            var backupNames = BuildBackupParamNamesWithLegacyPreservation(
                slotCount,
                avatarParams.Select(p => p.name).ToList(),
                paramsAsset.parameters?.Select(p => p?.name).ToArray());

            var byName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);
            foreach (var p in avatarParams)
                byName[p.name] = p;

            int preservedLegacyCount = 0;
            foreach (var name in backupNames)
            {
                const string prefix = "ASMLite_Bak_S";
                int firstUnderscore = name.IndexOf('_', prefix.Length);
                if (firstUnderscore < 0)
                    continue;

                string sourceParamName = name.Substring(firstUnderscore + 1);
                if (byName.TryGetValue(sourceParamName, out var source))
                {
                    generated.Add(new VRCExpressionParameters.Parameter
                    {
                        name          = name,
                        valueType     = source.valueType,
                        defaultValue  = source.defaultValue,
                        saved         = true,
                        networkSynced = false,
                    });
                }
                else
                {
                    var existing = paramsAsset.parameters?.FirstOrDefault(p => p != null && p.name == name);
                    if (existing != null)
                    {
                        generated.Add(new VRCExpressionParameters.Parameter
                        {
                            name          = existing.name,
                            valueType     = existing.valueType,
                            defaultValue  = existing.defaultValue,
                            saved         = true,
                            networkSynced = false,
                        });
                        preservedLegacyCount++;
                    }
                }
            }

#if ASM_LITE_VERBOSE
            if (preservedLegacyCount > 0)
                Debug.Log($"[ASM-Lite] Preserved {preservedLegacyCount} legacy backup parameter(s) during schema rebuild.");
#endif

            var seen = new HashSet<string>();
            var merged = new List<VRCExpressionParameters.Parameter>(generated.Count);
            foreach (var p in generated)
            {
                if (seen.Add(p.name))
                    merged.Add(p);
                else
                    Debug.LogWarning($"[ASM-Lite] Duplicate parameter name dropped from generated output: '{p.name}'");
            }

            paramsAsset.parameters = merged.ToArray();

            EditorUtility.SetDirty(paramsAsset);
            // SaveAssets is called once in Build() after all three Populate methods complete.
        }

        /// <summary>
        /// Generates the nested VRCExpressionsMenu tree at build time.
        ///
        /// Uses one shared Int control parameter (ASMLite_Ctrl):
        /// Save=(slot-1)*3+1, Load=(slot-1)*3+2, Clear=(slot-1)*3+3.
        ///
        /// Menu hierarchy:
        ///   root
        ///     └─ ASM-Lite  (SubMenu → presetsMenu)
        ///          └─ Preset N  (SubMenu → slotMenu)
        ///               ├─ Save   (SubMenu → confirmMenu)
        ///               │    └─ Confirm  (Button trigger)
        ///               ├─ Load   (Button trigger)
        ///               └─ Clear Preset  (SubMenu → resetConfirmMenu)
        ///                    └─ Confirm  (Button trigger)
        ///
        /// Asset operations are batched with StartAssetEditing/StopAssetEditing (in a
        /// try/finally) so Unity imports all created assets in one pass rather than
        /// triggering an import cycle per CreateAsset call. In-memory ScriptableObject
        /// references are used throughout: no LoadAssetAtPath reload after CreateAsset.
        /// </summary>
        private static void PopulateExpressionMenu(ASMLiteComponent component)
        {
            int slotCount = component.slotCount;

            // ── Load icons BEFORE StartAssetEditing (LoadAssetAtPath must run outside
            //    the edit batch or the asset database may not resolve paths correctly) ──
            var iconPresets = AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconPresets);

            // Resolve action icons: use component's custom icons when actionIconMode is Custom,
            // falling back to the bundled defaults for any that are null.
            Texture2D bundledSave  = AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconSave);
            Texture2D bundledLoad  = AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconLoad);
            Texture2D bundledReset = AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconReset);

            Texture2D iconSave, iconLoad, iconReset;
            if (component.actionIconMode == ActionIconMode.Custom)
            {
                iconSave  = component.customSaveIcon  != null ? component.customSaveIcon  : bundledSave;
                iconLoad  = component.customLoadIcon  != null ? component.customLoadIcon  : bundledLoad;
                iconReset = component.customClearIcon != null ? component.customClearIcon : bundledReset;
            }
            else
            {
                iconSave  = bundledSave;
                iconLoad  = bundledLoad;
                iconReset = bundledReset;
            }

            if (iconSave == null)
                Debug.LogWarning("[ASM-Lite] Save icon not found at " + ASMLiteAssetPaths.IconSave + ": controls will have no icon.");

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

                    // Unconditional deletes: AssetDatabase.DeleteAsset is a safe no-op
                    // on paths that don't exist, so no existence check is needed.
                    AssetDatabase.DeleteAsset(slotPath);
                    AssetDatabase.DeleteAsset(confirmPath);
                    AssetDatabase.DeleteAsset(resetConfirmPath);

                    // Resolve control parameter values for this slot using shared Int encoding.
                    string saveParamName  = "ASMLite_Ctrl";
                    float  saveParamValue = (float)((slot - 1) * 3 + 1);
                    string loadParamName  = "ASMLite_Ctrl";
                    float  loadParamValue = (float)((slot - 1) * 3 + 2);
                    string clearParamName = "ASMLite_Ctrl";
                    float  clearParamValue = (float)((slot - 1) * 3 + 3);

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
                // always decremented: even if an exception fires mid-loop. Forgetting this
                // would leave the Editor in a frozen "editing" state requiring a restart.
                AssetDatabase.StopAssetEditing();
            }

            // ── Build the ASM-Lite wrapper menu using in-memory slot references ──
            // StopAssetEditing() has processed the batch; in-memory ScriptableObject
            // references are valid: no LoadAssetAtPath reload needed.
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
            // presetsMenu in-memory reference remains valid: no reload needed.

            // ── Point root at the ASM-Lite wrapper (single entry) ────────────
            // Root is mutated in-place so its stable GUID (referenced by VRCFury)
            // is never broken.
            rootMenu.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name    = "Settings Manager",
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
        /// AnimatorControllerParameterType. Uses ValueType (not ParameterType) --
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
        ///   SameColor : all slots use the single gear icon at selectedGearIndex.
        ///   MultiColor: each slot cycles through GearIconPaths by index.
        ///   Custom    : uses the user-supplied texture from customIcons[slot-1],
        ///                falling back to <paramref name="fallback"/> if null/out-of-range.
        ///   default   : returns <paramref name="fallback"/>.
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
