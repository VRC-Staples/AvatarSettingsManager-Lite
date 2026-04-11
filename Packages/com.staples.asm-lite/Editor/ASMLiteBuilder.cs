// Add ASM_LITE_VERBOSE to Edit > Project Settings > Player > Scripting Define Symbols
// to enable verbose build logging throughout this file.

using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System;
using System.Collections.Generic;
using ASMLite;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLiteBuilder: static editor utility for build-time generated-asset output.
    ///
    /// Called from ASMLiteComponent.OnPreprocess() during the VRChat SDK avatar build
    /// pipeline. Discovers avatar parameters, generates FX animator slot layers with
    /// Save/Load/Clear Preset states using VRCAvatarParameterDriver Copy operations,
    /// and writes local control plus backup parameter assets under GeneratedAssets.
    ///
    /// Control trigger model: one shared local Int param (ASMLite_Ctrl) for all slots,
    /// with encoded values (slot-1)*3+1/2/3 for Save/Load/Clear.
    ///
    /// Normal Build() flow ends after generated assets are rebuilt and saved.
    /// Runtime application is handled by downstream VRCFury FullController wiring,
    /// not by direct descriptor mutation in this method.
    ///
    /// Parameter discovery reads avDesc.expressionParameters and consumes names as
    /// opaque canonical identifiers (no renaming), while filtering empty entries and
    /// ASMLite_-prefixed names to avoid self-referential backup loops.
    /// </summary>
    public static class ASMLiteBuilder
    {
        // ─── Constants ────────────────────────────────────────────────────────

        /// <summary>
        /// Name of the shared local Int parameter used to trigger slot actions.
        /// Encoding: Save=(slot-1)*3+1, Load=(slot-1)*3+2, Clear=(slot-1)*3+3.
        /// Value 0 = idle.
        /// </summary>
        internal const string CtrlParam = "ASMLite_Ctrl";

        internal const string DefaultRootControlName = "Settings Manager";

        internal readonly struct CleanupReport
        {
            internal CleanupReport(int fxLayersRemoved, int fxParamsRemoved, int exprParamsRemoved, int menuControlsRemoved, bool descriptorMissing)
            {
                FxLayersRemoved = fxLayersRemoved;
                FxParamsRemoved = fxParamsRemoved;
                ExprParamsRemoved = exprParamsRemoved;
                MenuControlsRemoved = menuControlsRemoved;
                DescriptorMissing = descriptorMissing;
            }

            internal int FxLayersRemoved { get; }
            internal int FxParamsRemoved { get; }
            internal int ExprParamsRemoved { get; }
            internal int MenuControlsRemoved { get; }
            internal bool DescriptorMissing { get; }
        }

        internal readonly struct RebuildMigrationReport
        {
            internal RebuildMigrationReport(int staleVrcFuryRemoved, CleanupReport cleanup, bool componentMissing, bool avatarDescriptorFound)
            {
                StaleVrcFuryRemoved = staleVrcFuryRemoved;
                Cleanup = cleanup;
                ComponentMissing = componentMissing;
                AvatarDescriptorFound = avatarDescriptorFound;
            }

            internal int StaleVrcFuryRemoved { get; }
            internal CleanupReport Cleanup { get; }
            internal bool ComponentMissing { get; }
            internal bool AvatarDescriptorFound { get; }
        }

        internal readonly struct LegacyAliasContinuityReport
        {
            internal LegacyAliasContinuityReport(int mappedCount, int mirroredCount, int unmatchedCount, int malformedCount)
            {
                MappedCount = mappedCount;
                MirroredCount = mirroredCount;
                UnmatchedCount = unmatchedCount;
                MalformedCount = malformedCount;
            }

            internal int MappedCount { get; }
            internal int MirroredCount { get; }
            internal int UnmatchedCount { get; }
            internal int MalformedCount { get; }
        }

        internal readonly struct LegacyAliasBinding
        {
            internal LegacyAliasBinding(int slot, string sourceParamName, string legacyBackupName)
            {
                Slot = slot;
                SourceParamName = sourceParamName;
                LegacyBackupName = legacyBackupName;
            }

            internal int Slot { get; }
            internal string SourceParamName { get; }
            internal string LegacyBackupName { get; }
        }

        internal readonly struct BackupNamePlan
        {
            internal BackupNamePlan(List<string> names, List<LegacyAliasBinding> legacyAliasBindings, LegacyAliasContinuityReport report)
            {
                Names = names;
                LegacyAliasBindings = legacyAliasBindings;
                Report = report;
            }

            internal List<string> Names { get; }
            internal List<LegacyAliasBinding> LegacyAliasBindings { get; }
            internal LegacyAliasContinuityReport Report { get; }
        }

        private readonly struct ParsedBackupName
        {
            internal ParsedBackupName(int slot, string sourceParamName)
            {
                Slot = slot;
                SourceParamName = sourceParamName;
            }

            internal int Slot { get; }
            internal string SourceParamName { get; }
        }

        private static LegacyAliasContinuityReport s_latestLegacyAliasReport;

        // ─── Public API ───────────────────────────────────────────────────────


        /// <summary>
        /// Reads the final avatar parameter schema from the descriptor's expression
        /// parameters snapshot.
        ///
        /// Names are consumed exactly as emitted (opaque canonical identifiers): no
        /// renaming or prefix rewriting is applied. Empty entries and ASMLite_-prefixed
        /// entries are filtered out to avoid self-referential backup loops.
        ///
        /// Returns an empty list (not null) if expressionParameters is unassigned.
        /// </summary>
        internal static List<VRCExpressionParameters.Parameter> GetFinalAvatarParams(VRCAvatarDescriptor avDesc)
        {
            var exprParams = avDesc?.expressionParameters;
            if (exprParams?.parameters == null)
                return new List<VRCExpressionParameters.Parameter>();

            var result = new List<VRCExpressionParameters.Parameter>(exprParams.parameters.Length);

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] Reading final avatar params from '{UnityEditor.AssetDatabase.GetAssetPath(exprParams)}' ({exprParams.parameters.Length} entries).");
#endif

            foreach (var p in exprParams.parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.name))
                    continue;
                if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal))
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
        /// Reads the final avatar parameter schema and generates all slot assets.
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

            // 2. Read the final avatar parameter schema from the descriptor snapshot.
            //    Names are treated as opaque canonical VF output and are not rewritten.
            var discoveredParams = GetFinalAvatarParams(avDesc);

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] Discovered {discoveredParams.Count} custom parameters for '{component.gameObject.name}'.");
#endif

            // 4. Warn if zero params (layers will be generated with empty Copy lists)
            if (discoveredParams.Count == 0)
            {
                Debug.LogWarning($"[ASM-Lite] No custom parameters discovered. FX layers will be generated with empty driver lists.");
            }

            var legacyAliasPlan = BuildBackupNamePlan(
                component.slotCount,
                discoveredParams,
                GetExistingGeneratedBackupNames(),
                ASMLiteToggleNameBroker.GetLatestGlobalParamMappings());
            s_latestLegacyAliasReport = legacyAliasPlan.Report;

            Debug.Log(
                $"[ASM-Lite] Legacy backup continuity: mapped={legacyAliasPlan.Report.MappedCount}, mirrored={legacyAliasPlan.Report.MirroredCount}, unmatched={legacyAliasPlan.Report.UnmatchedCount}, malformed={legacyAliasPlan.Report.MalformedCount}.");

            // 5-7. Generate stub assets (delivery source for VRCFury FullController).
            PopulateFXController(discoveredParams, component.slotCount, legacyAliasPlan.LegacyAliasBindings);
            PopulateExpressionParams(discoveredParams, legacyAliasPlan.Names);
            PopulateExpressionMenu(component);

            // 8. Flush generated assets to disk for downstream VF consumption.
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
            if (component == null)
                return "[ASM-Lite] component is null.";
            if (component.slotCount < 1 || component.slotCount > 8)
                return $"[ASM-Lite] slotCount must be between 1 and 8 (got {component.slotCount}).";
            return null;
        }

        internal static LegacyAliasContinuityReport GetLatestLegacyAliasContinuityReport()
        {
            return s_latestLegacyAliasReport;
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
        private static void PopulateFXController(
            List<VRCExpressionParameters.Parameter> avatarParams,
            int slotCount,
            List<LegacyAliasBinding> legacyAliasBindings)
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
            ctrl.AddParameter(CtrlParam, AnimatorControllerParameterType.Int);

            // Pre-compute mapped parameter types to avoid repeated MapValueType calls
            var mappedTypes = new AnimatorControllerParameterType[avatarParams.Count];
            for (int i = 0; i < avatarParams.Count; i++)
                mappedTypes[i] = MapValueType(avatarParams[i].valueType);

            // Declare the discovered avatar parameters directly in the FX controller.
            // This is required for two reasons:
            //   1. VRCAvatarParameterDriver Copy sources must be declared as parameters
            //      in the FX controller that contains the driving state, otherwise the
            //      runtime treats the source as missing and the Copy is a silent no-op.
            //   2. VRCFury's FullController merge uses globalParams=["*"] to connect
            //      FX controller parameters to the avatar's global parameter space.
            //      Parameters referenced only in driver entries (not declared) are not
            //      promoted by this binding, so the Save Copy would read from an
            //      unconnected local rather than the live avatar parameter value.
            // Track names added to guard against duplicate avatar param names.
            var addedParams = new HashSet<string>();
            for (int i = 0; i < avatarParams.Count; i++)
            {
                var p = avatarParams[i];
                if (addedParams.Add(p.name))
                    ctrl.AddParameter(p.name, mappedTypes[i]);
                else
                    Debug.LogWarning($"[ASM-Lite] Duplicate discovered parameter skipped: '{p.name}'");
            }

            // Add per-slot backup parameters: ASMLite_Bak_S{slot}_{paramName}
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

            if (legacyAliasBindings != null && legacyAliasBindings.Count > 0)
            {
                var typeBySource = new Dictionary<string, AnimatorControllerParameterType>(StringComparer.Ordinal);
                for (int i = 0; i < avatarParams.Count; i++)
                {
                    var param = avatarParams[i];
                    if (param == null || string.IsNullOrWhiteSpace(param.name))
                        continue;

                    if (!typeBySource.ContainsKey(param.name))
                        typeBySource.Add(param.name, mappedTypes[i]);
                }

                for (int i = 0; i < legacyAliasBindings.Count; i++)
                {
                    var binding = legacyAliasBindings[i];
                    if (string.IsNullOrWhiteSpace(binding.LegacyBackupName))
                        continue;

                    if (!typeBySource.TryGetValue(binding.SourceParamName, out var mappedType))
                        continue;

                    if (addedParams.Add(binding.LegacyBackupName))
                        ctrl.AddParameter(binding.LegacyBackupName, mappedType);
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
                AddSlotLayer(ctrl, slot, avatarParams, legacyAliasBindings);

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
        private static void AddSlotLayer(
            AnimatorController ctrl,
            int slot,
            List<VRCExpressionParameters.Parameter> avatarParams,
            List<LegacyAliasBinding> legacyAliasBindings)
        {
            string slotName = $"ASMLite_Slot{slot}";

            // Control encoding for this slot (shared Int trigger param)
            int saveValue  = (slot - 1) * 3 + 1;
            int loadValue  = (slot - 1) * 3 + 2;
            int clearValue = (slot - 1) * 3 + 3;

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

            // Pre-size all three lists and build in a single pass to avoid 3x iteration.
            int slotAliasCount = CountLegacyAliasBindingsForSlot(slot, legacyAliasBindings);
            var saveParams  = new List<VRC_AvatarParameterDriver.Parameter>(avatarParams.Count + slotAliasCount + 3);
            var loadParams  = new List<VRC_AvatarParameterDriver.Parameter>(avatarParams.Count + slotAliasCount + 3);
            var resetParams = new List<VRC_AvatarParameterDriver.Parameter>(avatarParams.Count + slotAliasCount + 3);

            var seenDriverParams = new HashSet<string>(StringComparer.Ordinal);
            var seenSavePairs = new HashSet<string>(StringComparer.Ordinal);
            var seenLoadPairs = new HashSet<string>(StringComparer.Ordinal);
            var seenResetPairs = new HashSet<string>(StringComparer.Ordinal);

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

                string deterministicBackupName = $"ASMLite_Bak_S{slot}_{p.name}";
                string defaultName = $"ASMLite_Def_{p.name}";

                AddDriverCopy(saveParams, seenSavePairs, p.name, deterministicBackupName);
                AddDriverCopy(loadParams, seenLoadPairs, deterministicBackupName, p.name);
                // Clear the slot's backup param to default so a subsequent
                // Load on this slot returns defaults instead of stale saved values.
                // Live avatar params are NOT touched: only the saved preset is cleared.
                AddDriverCopy(resetParams, seenResetPairs, defaultName, deterministicBackupName);

                AddLegacyAliasDriverCopiesForSlot(
                    slot,
                    p.name,
                    defaultName,
                    legacyAliasBindings,
                    saveParams,
                    seenSavePairs,
                    loadParams,
                    seenLoadPairs,
                    resetParams,
                    seenResetPairs);
            }

            // Zone B: trailing Set entries reset shared control Int back to idle (0)
            var resetCtrlEntry = new VRC_AvatarParameterDriver.Parameter
            {
                type  = VRC_AvatarParameterDriver.ChangeType.Set,
                name  = CtrlParam,
                value = 0f,
            };
            saveParams.Add(resetCtrlEntry);
            loadParams.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type  = VRC_AvatarParameterDriver.ChangeType.Set,
                name  = CtrlParam,
                value = 0f,
            });
            resetParams.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type  = VRC_AvatarParameterDriver.ChangeType.Set,
                name  = CtrlParam,
                value = 0f,
            });

            saveDriver.parameters  = saveParams;
            saveDriver.localOnly   = true;

            loadDriver.parameters  = loadParams;
            loadDriver.localOnly   = true;

            resetDriver.parameters = resetParams;
            resetDriver.localOnly  = true;

            // Transitions from Idle using shared Int control encoding.
            AddConditionTransition(idleState, saveState,  CtrlParam, AnimatorConditionMode.Equals, saveValue);
            AddConditionTransition(idleState, loadState,  CtrlParam, AnimatorConditionMode.Equals, loadValue);
            AddConditionTransition(idleState, resetState, CtrlParam, AnimatorConditionMode.Equals, clearValue);

            // Action states → Idle (exit-time at 0, immediate)
            AddExitTimeTransition(saveState,  idleState);
            AddExitTimeTransition(loadState,  idleState);
            AddExitTimeTransition(resetState, idleState);
        }

        private static int CountLegacyAliasBindingsForSlot(int slot, List<LegacyAliasBinding> bindings)
        {
            if (bindings == null || bindings.Count == 0)
                return 0;

            int count = 0;
            for (int i = 0; i < bindings.Count; i++)
            {
                if (bindings[i].Slot == slot)
                    count++;
            }

            return count;
        }

        private static void AddLegacyAliasDriverCopiesForSlot(
            int slot,
            string sourceParamName,
            string defaultName,
            List<LegacyAliasBinding> bindings,
            List<VRC_AvatarParameterDriver.Parameter> saveParams,
            HashSet<string> seenSavePairs,
            List<VRC_AvatarParameterDriver.Parameter> loadParams,
            HashSet<string> seenLoadPairs,
            List<VRC_AvatarParameterDriver.Parameter> resetParams,
            HashSet<string> seenResetPairs)
        {
            if (bindings == null || bindings.Count == 0)
                return;

            for (int i = 0; i < bindings.Count; i++)
            {
                var binding = bindings[i];
                if (binding.Slot != slot)
                    continue;
                if (!string.Equals(binding.SourceParamName, sourceParamName, StringComparison.Ordinal))
                    continue;
                if (string.IsNullOrWhiteSpace(binding.LegacyBackupName))
                    continue;

                AddDriverCopy(saveParams, seenSavePairs, sourceParamName, binding.LegacyBackupName);
                AddDriverCopy(loadParams, seenLoadPairs, binding.LegacyBackupName, sourceParamName);
                AddDriverCopy(resetParams, seenResetPairs, defaultName, binding.LegacyBackupName);
            }
        }

        private static void AddDriverCopy(
            List<VRC_AvatarParameterDriver.Parameter> target,
            HashSet<string> seenPairs,
            string source,
            string destination)
        {
            if (target == null || seenPairs == null)
                return;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
                return;

            string key = source + "\u001F" + destination;
            if (!seenPairs.Add(key))
                return;

            target.Add(new VRC_AvatarParameterDriver.Parameter
            {
                type = VRC_AvatarParameterDriver.ChangeType.Copy,
                source = source,
                name = destination,
            });
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
        private static bool TryParseBackupName(string backupName, out ParsedBackupName parsed)
        {
            parsed = default;

            if (string.IsNullOrWhiteSpace(backupName))
                return false;

            const string prefix = "ASMLite_Bak_S";
            if (!backupName.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            int slotStart = prefix.Length;
            int underscoreIndex = backupName.IndexOf('_', slotStart);
            if (underscoreIndex <= slotStart)
                return false;

            string slotSegment = backupName.Substring(slotStart, underscoreIndex - slotStart);
            if (!int.TryParse(slotSegment, out int slot) || slot <= 0)
                return false;

            string source = backupName.Substring(underscoreIndex + 1);
            if (string.IsNullOrWhiteSpace(source))
                return false;

            parsed = new ParsedBackupName(slot, source);
            return true;
        }

        private static BackupNamePlan BuildBackupNamePlan(
            int slotCount,
            List<VRCExpressionParameters.Parameter> avatarParams,
            string[] existingParamNames,
            ASMLiteToggleNameBroker.GlobalParamMapping[] brokerMappings)
        {
            var avatarParamNames = new List<string>(avatarParams.Count);
            var avatarParamSet = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < avatarParams.Count; i++)
            {
                var param = avatarParams[i];
                if (param == null || string.IsNullOrWhiteSpace(param.name))
                    continue;

                if (avatarParamSet.Add(param.name))
                    avatarParamNames.Add(param.name);
            }

            var mappingByOriginal = new Dictionary<string, string>(StringComparer.Ordinal);
            if (brokerMappings != null)
            {
                for (int i = 0; i < brokerMappings.Length; i++)
                {
                    var mapping = brokerMappings[i];
                    if (string.IsNullOrWhiteSpace(mapping.OriginalGlobalParam))
                        continue;
                    if (string.IsNullOrWhiteSpace(mapping.AssignedGlobalParam))
                        continue;
                    if (!mappingByOriginal.ContainsKey(mapping.OriginalGlobalParam))
                        mappingByOriginal.Add(mapping.OriginalGlobalParam, mapping.AssignedGlobalParam);
                }
            }

            var names = new List<string>(slotCount * avatarParamNames.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int slot = 1; slot <= slotCount; slot++)
            {
                for (int i = 0; i < avatarParamNames.Count; i++)
                {
                    string name = $"ASMLite_Bak_S{slot}_{avatarParamNames[i]}";
                    if (seen.Add(name))
                        names.Add(name);
                }
            }

            int mappedCount = 0;
            int unmatchedCount = 0;
            int malformedCount = 0;
            var bindings = new List<LegacyAliasBinding>();
            var seenBindings = new HashSet<string>(StringComparer.Ordinal);

            if (existingParamNames != null)
            {
                for (int i = 0; i < existingParamNames.Length; i++)
                {
                    string existingName = existingParamNames[i];
                    if (string.IsNullOrWhiteSpace(existingName))
                        continue;
                    if (!existingName.StartsWith("ASMLite_Bak_", StringComparison.Ordinal))
                        continue;

                    if (!TryParseBackupName(existingName, out var parsed))
                    {
                        malformedCount++;
                        continue;
                    }

                    if (seen.Add(existingName))
                        names.Add(existingName);

                    if (!mappingByOriginal.TryGetValue(parsed.SourceParamName, out string assignedSourceName) || string.IsNullOrWhiteSpace(assignedSourceName))
                    {
                        if (!avatarParamSet.Contains(parsed.SourceParamName))
                            unmatchedCount++;
                        continue;
                    }

                    if (!avatarParamSet.Contains(assignedSourceName))
                    {
                        unmatchedCount++;
                        continue;
                    }

                    mappedCount++;
                    string bindingKey = parsed.Slot + "\u001F" + assignedSourceName + "\u001F" + existingName;
                    if (seenBindings.Add(bindingKey))
                    {
                        bindings.Add(new LegacyAliasBinding(parsed.Slot, assignedSourceName, existingName));
                    }
                }
            }

            int mirroredCount = bindings.Count;
            var report = new LegacyAliasContinuityReport(mappedCount, mirroredCount, unmatchedCount, malformedCount);
            return new BackupNamePlan(names, bindings, report);
        }

        /// <summary>
        /// Builds backup parameter names for generated + preserved valid legacy entries.
        /// </summary>
        internal static List<string> BuildBackupParamNamesWithLegacyPreservation(
            int slotCount,
            List<string> avatarParamNames,
            string[] existingParamNames)
        {
            var avatarParams = new List<VRCExpressionParameters.Parameter>(avatarParamNames.Count);
            for (int i = 0; i < avatarParamNames.Count; i++)
            {
                string name = avatarParamNames[i];
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                avatarParams.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = false,
                });
            }

            return BuildBackupNamePlan(slotCount, avatarParams, existingParamNames, Array.Empty<ASMLiteToggleNameBroker.GlobalParamMapping>()).Names;
        }

        private static string[] GetExistingGeneratedBackupNames()
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (paramsAsset?.parameters == null)
                return Array.Empty<string>();

            var existing = new string[paramsAsset.parameters.Length];
            for (int i = 0; i < paramsAsset.parameters.Length; i++)
                existing[i] = paramsAsset.parameters[i]?.name;

            return existing;
        }

        /// <summary>
        /// Writes one local shared control trigger param (ASMLite_Ctrl) plus
        /// slot backup params into the managed VRCExpressionParameters asset.
        ///
        /// Legacy backup params from prior schemas are preserved when not colliding
        /// with the current schema to avoid dropping existing user presets.
        /// </summary>
        private static void PopulateExpressionParams(List<VRCExpressionParameters.Parameter> avatarParams, List<string> backupNames)
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (paramsAsset == null)
            {
                Debug.LogError($"[ASM-Lite] Cannot load VRCExpressionParameters at '{ASMLiteAssetPaths.ExprParams}'.");
                return;
            }

            int totalCount = 1 + (backupNames != null ? backupNames.Count : 0);
            var generated = new List<VRCExpressionParameters.Parameter>(totalCount);

            // Control Int used by ASM-Lite's FX layers and menu buttons.
            // Marked non-synced and non-saved so it does not consume network bits
            // and does not persist across sessions.
            generated.Add(new VRCExpressionParameters.Parameter
            {
                name          = CtrlParam,
                valueType     = VRCExpressionParameters.ValueType.Int,
                defaultValue  = 0f,
                saved         = false,
                networkSynced = false,
            });

            var resolvedBackupNames = backupNames ?? new List<string>();


            var byName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);
            foreach (var p in avatarParams)
                byName[p.name] = p;

            // Pre-build a lookup for existing asset params so legacy preservation
            // is O(1) per entry rather than O(n*m) with FirstOrDefault inside the loop.
            var existingByName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);
            if (paramsAsset.parameters != null)
            {
                foreach (var p in paramsAsset.parameters)
                {
                    if (p != null && !string.IsNullOrEmpty(p.name) && !existingByName.ContainsKey(p.name))
                        existingByName[p.name] = p;
                }
            }

            int preservedLegacyCount = 0;
            foreach (var name in resolvedBackupNames)
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
                else if (existingByName.TryGetValue(name, out var existing))
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

#if ASM_LITE_VERBOSE
            if (preservedLegacyCount > 0)
                Debug.Log($"[ASM-Lite] Preserved {preservedLegacyCount} legacy backup parameter(s) during schema rebuild.");
#endif

            // Deduplicate into a pre-allocated array to avoid the List intermediate.
            var seen   = new HashSet<string>(generated.Count, StringComparer.Ordinal);
            var merged = new VRCExpressionParameters.Parameter[generated.Count];
            int writeIdx = 0;
            foreach (var p in generated)
            {
                if (seen.Add(p.name))
                    merged[writeIdx++] = p;
                else
                    Debug.LogWarning($"[ASM-Lite] Duplicate parameter name dropped from generated output: '{p.name}'");
            }

            paramsAsset.parameters = writeIdx == merged.Length ? merged : merged[..writeIdx];

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
            // Pass an icon load cache so SameColor/MultiColor modes don't call
            // LoadAssetAtPath once per slot for the same path.
            var slotIcons    = new Texture2D[slotCount];
            var iconLoadCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
            for (int slot = 1; slot <= slotCount; slot++)
                slotIcons[slot - 1] = ResolveSlotIcon(component, slot, iconPresets, iconLoadCache);

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
                    float saveParamValue  = (float)((slot - 1) * 3 + 1);
                    float loadParamValue  = (float)((slot - 1) * 3 + 2);
                    float clearParamValue = (float)((slot - 1) * 3 + 3);

                    // ── Save confirm sub-menu ─────────────────────────────────────
                    var confirmMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    confirmMenu.controls = new List<VRCExpressionsMenu.Control>
                    {
                        new VRCExpressionsMenu.Control
                        {
                            name      = "Confirm",
                            type      = VRCExpressionsMenu.Control.ControlType.Button,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = CtrlParam },
                            value     = saveParamValue,
                            icon      = iconSave,
                        }
                    };
                    AssetDatabase.CreateAsset(confirmMenu, confirmPath);
                    confirmMenus[slot - 1] = confirmMenu;

                    // ── Reset confirm sub-menu ────────────────────────────────────
                    var resetConfirmMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    resetConfirmMenu.controls = new List<VRCExpressionsMenu.Control>
                    {
                        new VRCExpressionsMenu.Control
                        {
                            name      = "Confirm",
                            type      = VRCExpressionsMenu.Control.ControlType.Button,
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = CtrlParam },
                            value     = clearParamValue,
                            icon      = iconReset,
                        }
                    };
                    AssetDatabase.CreateAsset(resetConfirmMenu, resetConfirmPath);
                    resetConfirmMenus[slot - 1] = resetConfirmMenu;

                    // ── Slot sub-menu (Save / Load / Clear Preset) ───────────────────────
                    var slotMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                    slotMenu.controls = new List<VRCExpressionsMenu.Control>
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
                            parameter = new VRCExpressionsMenu.Control.Parameter { name = CtrlParam },
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
            presetsMenu.controls = new List<VRCExpressionsMenu.Control>();
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
            string effectiveRootControlName = ResolveEffectiveRootControlName(component);
            Texture2D effectiveRootControlIcon = ResolveEffectiveRootControlIcon(component, iconPresets);
            rootMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name    = effectiveRootControlName,
                    type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = presetsMenu,
                    icon    = effectiveRootControlIcon,
                }
            };

            EditorUtility.SetDirty(rootMenu);
            // SaveAssets is called once in Build() after all three Populate methods complete.

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] PopulateExpressionMenu: generated root + ASM-Lite wrapper + {slotCount} slot menus + {slotCount} confirm menus.");
#endif
        }

        // ─── Migration ────────────────────────────────────────────────────────

        /// <summary>
        /// Collapses duplicate stale VRCFury (VF.Model.VRCFury) components on an
        /// ASM-Lite prefab instance while preserving one component for the active
        /// FullController delivery path.
        ///
        /// This migration helper exists for upgrading older prefab instances whose
        /// serialized VF payload may contain duplicate VRCFury components. It is safe
        /// to call multiple times.
        /// </summary>
        public static void MigrateStaleVRCFuryComponents(ASMLiteComponent component)
        {
            _ = MigrateStaleVRCFuryComponentsWithReport(component);
        }

        internal static int MigrateStaleVRCFuryComponentsWithReport(ASMLiteComponent component)
        {
            if (component == null)
                return 0;

            var go = component.gameObject;
            // Find VRCFury components by type name since we cannot reference the
            // internal VF.Model.VRCFury type at compile time.
            var allComponents = go.GetComponents<Component>();
            var vfComponents = new List<Component>();
            foreach (var c in allComponents)
            {
                if (c == null) continue; // missing script
                string typeName = c.GetType().FullName;
                if (typeName == "VF.Model.VRCFury")
                    vfComponents.Add(c);
            }

            if (vfComponents.Count <= 1)
                return 0;

            int removedCount = 0;
            for (int i = 1; i < vfComponents.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(vfComponents[i]);
                removedCount++;
            }

            if (removedCount > 0)
                EditorUtility.SetDirty(go);

            return removedCount;
        }

        internal static RebuildMigrationReport PrepareRevertedDeliveryRebuild(ASMLiteComponent component)
        {
            if (component == null)
            {
                var emptyCleanup = new CleanupReport(0, 0, 0, 0, descriptorMissing: true);
                return new RebuildMigrationReport(0, emptyCleanup, componentMissing: true, avatarDescriptorFound: false);
            }

            int staleVfRemoved = MigrateStaleVRCFuryComponentsWithReport(component);

            var avDesc = component.GetComponentInParent<VRCAvatarDescriptor>();
            bool avatarDescriptorFound = avDesc != null;
            var cleanup = CleanUpAvatarAssetsWithReport(avDesc);

            if (staleVfRemoved > 0)
            {
                Debug.Log($"[ASM-Lite] Migration: removed {staleVfRemoved} duplicate stale VRCFury component(s) from '{component.gameObject.name}' while preserving one delivery component.");
            }

            if (avatarDescriptorFound)
            {
                Debug.Log($"[ASM-Lite] Rebuild cleanup: removed {cleanup.FxLayersRemoved} legacy FX layer(s), {cleanup.FxParamsRemoved} legacy FX parameter(s), {cleanup.ExprParamsRemoved} expression parameter(s), and {cleanup.MenuControlsRemoved} root menu control(s).");
            }

            return new RebuildMigrationReport(staleVfRemoved, cleanup, componentMissing: false, avatarDescriptorFound: avatarDescriptorFound);
        }

        // ─── Legacy descriptor-injection helpers (retired from normal flow) ───

        /// <summary>
        /// Injects ASM-Lite FX layers and parameters directly into the avatar's
        /// FX AnimatorController. Removes any previously injected ASMLite_ layers
        /// and parameters first (idempotent). If the avatar has no custom FX
        /// controller, the stub controller is assigned directly.
        /// </summary>
        private static void InjectFXLayers(VRCAvatarDescriptor avDesc, List<VRCExpressionParameters.Parameter> avatarParams, int slotCount)
        {
            // Find the FX layer in baseAnimationLayers (type == FX)
            int fxIndex = -1;
            for (int i = 0; i < avDesc.baseAnimationLayers.Length; i++)
            {
                if (avDesc.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                {
                    fxIndex = i;
                    break;
                }
            }

            if (fxIndex < 0)
            {
                Debug.LogWarning("[ASM-Lite] No FX layer found in avatar descriptor. Cannot inject FX layers.");
                return;
            }

            var fxLayer = avDesc.baseAnimationLayers[fxIndex];
            var ctrl = fxLayer.animatorController as AnimatorController;

            // If no custom FX controller exists, assign our stub directly
            if (ctrl == null || fxLayer.isDefault)
            {
                var stubCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
                if (stubCtrl != null)
                {
                    fxLayer.isDefault = false;
                    fxLayer.isEnabled = true;
                    fxLayer.animatorController = stubCtrl;
                    avDesc.baseAnimationLayers[fxIndex] = fxLayer;
#if ASM_LITE_VERBOSE
                    Debug.Log("[ASM-Lite] InjectFXLayers: assigned stub FX controller directly (no existing FX).");
#endif
                }
                else
                {
                    Debug.LogError("[ASM-Lite] InjectFXLayers: stub FX controller not found.");
                }
                return;
            }

            // Remove existing ASMLite_ layers (iterate backwards)
            for (int i = ctrl.layers.Length - 1; i >= 0; i--)
            {
                if (ctrl.layers[i].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                    ctrl.RemoveLayer(i);
            }

            // Drain ASMLite_ params (RemoveParameter modifies the array, so
            // iterate until none remain rather than using index arithmetic).
            bool removedAny;
            do
            {
                removedAny = false;
                foreach (var p in ctrl.parameters)
                {
                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal) || p.name == CtrlParam)
                    {
                        ctrl.RemoveParameter(p);
                        removedAny = true;
                        break;
                    }
                }
            } while (removedAny);

            // Add control parameter
            ctrl.AddParameter(CtrlParam, AnimatorControllerParameterType.Int);

            // Add discovered avatar parameters (needed for Copy driver sources)
            var mappedTypes = new AnimatorControllerParameterType[avatarParams.Count];
            for (int i = 0; i < avatarParams.Count; i++)
                mappedTypes[i] = MapValueType(avatarParams[i].valueType);

            var addedParams = new HashSet<string>(StringComparer.Ordinal);
            addedParams.Add(CtrlParam);
            for (int i = 0; i < avatarParams.Count; i++)
            {
                // Only add if not already present in the controller
                bool alreadyExists = false;
                foreach (var ep in ctrl.parameters)
                {
                    if (ep.name == avatarParams[i].name)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                if (!alreadyExists && addedParams.Add(avatarParams[i].name))
                    ctrl.AddParameter(avatarParams[i].name, mappedTypes[i]);
            }

            // Add per-slot backup and default parameters
            for (int slot = 1; slot <= slotCount; slot++)
            {
                for (int i = 0; i < avatarParams.Count; i++)
                {
                    string bakName = $"ASMLite_Bak_S{slot}_{avatarParams[i].name}";
                    if (addedParams.Add(bakName))
                        ctrl.AddParameter(bakName, mappedTypes[i]);
                }
            }
            for (int i = 0; i < avatarParams.Count; i++)
            {
                string defName = $"ASMLite_Def_{avatarParams[i].name}";
                if (!addedParams.Add(defName))
                    continue;

                var acp = new AnimatorControllerParameter
                {
                    name = defName,
                    type = mappedTypes[i]
                };
                switch (avatarParams[i].valueType)
                {
                    case VRCExpressionParameters.ValueType.Int:
                        acp.defaultInt = (int)avatarParams[i].defaultValue;
                        break;
                    case VRCExpressionParameters.ValueType.Float:
                        acp.defaultFloat = avatarParams[i].defaultValue;
                        break;
                    case VRCExpressionParameters.ValueType.Bool:
                        acp.defaultBool = avatarParams[i].defaultValue != 0f;
                        break;
                }
                ctrl.AddParameter(acp);
            }

            // Add slot layers
            for (int slot = 1; slot <= slotCount; slot++)
                AddSlotLayer(ctrl, slot, avatarParams, legacyAliasBindings: null);

            EditorUtility.SetDirty(ctrl);

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] InjectFXLayers: injected {slotCount} slot layers into avatar FX controller '{AssetDatabase.GetAssetPath(ctrl)}'.");
#endif
        }

        /// <summary>
        /// Injects ASM-Lite expression parameters directly into the avatar's
        /// VRCExpressionParameters asset. Removes any previously injected ASMLite_
        /// parameters first (idempotent).
        /// </summary>
        private static void InjectExpressionParams(VRCAvatarDescriptor avDesc, int slotCount, List<VRCExpressionParameters.Parameter> avatarParams)
        {
            var exprParams = avDesc.expressionParameters;
            if (exprParams == null)
            {
                Debug.LogWarning("[ASM-Lite] InjectExpressionParams: no expressionParameters on avatar descriptor.");
                return;
            }

            // Filter out any existing ASMLite_ entries
            var existing = exprParams.parameters ?? new VRCExpressionParameters.Parameter[0];
            var filtered = new List<VRCExpressionParameters.Parameter>(existing.Length);
            foreach (var p in existing)
            {
                if (p == null || string.IsNullOrEmpty(p.name))
                    continue;
                if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal) || p.name == CtrlParam)
                    continue;
                filtered.Add(p);
            }

            // Build ASMLite params to inject
            var asmParams = new List<VRCExpressionParameters.Parameter>();

            // Control Int
            asmParams.Add(new VRCExpressionParameters.Parameter
            {
                name          = CtrlParam,
                valueType     = VRCExpressionParameters.ValueType.Int,
                defaultValue  = 0f,
                saved         = false,
                networkSynced = false,
            });

            // Backup params per slot
            var byName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);
            foreach (var p in avatarParams)
                byName[p.name] = p;

            for (int slot = 1; slot <= slotCount; slot++)
            {
                foreach (var p in avatarParams)
                {
                    asmParams.Add(new VRCExpressionParameters.Parameter
                    {
                        name          = $"ASMLite_Bak_S{slot}_{p.name}",
                        valueType     = p.valueType,
                        defaultValue  = p.defaultValue,
                        saved         = true,
                        networkSynced = false,
                    });
                }
            }

            // Combine: existing non-ASMLite params + our generated params
            filtered.AddRange(asmParams);

            // Deduplicate
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var final = new List<VRCExpressionParameters.Parameter>(filtered.Count);
            foreach (var p in filtered)
            {
                if (seen.Add(p.name))
                    final.Add(p);
            }

            exprParams.parameters = final.ToArray();
            EditorUtility.SetDirty(exprParams);

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] InjectExpressionParams: {asmParams.Count} ASM-Lite params injected into avatar expression parameters.");
#endif
        }

        /// <summary>
        /// Injects the ASM-Lite root submenu entry directly into the avatar's
        /// VRCExpressionsMenu. Removes any previously injected entry first
        /// (idempotent). The submenu references the generated presets menu asset.
        /// </summary>
        private static void InjectExpressionMenu(VRCAvatarDescriptor avDesc, ASMLiteComponent component)
        {
            var rootMenu = avDesc.expressionsMenu;
            if (rootMenu == null)
            {
                Debug.LogWarning("[ASM-Lite] InjectExpressionMenu: no expressionsMenu on avatar descriptor.");
                return;
            }

            if (rootMenu.controls == null)
                rootMenu.controls = new List<VRCExpressionsMenu.Control>();

            // Load the generated presets menu that PopulateExpressionMenu created.
            string presetsMenuPath = $"{ASMLiteAssetPaths.GeneratedDir}/ASMLite_Presets_Menu.asset";
            var presetsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(presetsMenuPath);
            if (presetsMenu == null)
            {
                Debug.LogError($"[ASM-Lite] InjectExpressionMenu: presets menu not found at '{presetsMenuPath}'. Was PopulateExpressionMenu called first?");
                return;
            }

            string effectiveRootControlName = ResolveEffectiveRootControlName(component);

            // Remove existing ASM-Lite root entries (idempotent), including stale names
            // from toggle flips between custom and default root naming.
            rootMenu.controls.RemoveAll(c =>
                c != null
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                && (string.Equals(c.name, DefaultRootControlName, StringComparison.Ordinal)
                    || string.Equals(c.name, effectiveRootControlName, StringComparison.Ordinal)
                    || string.Equals(AssetDatabase.GetAssetPath(c.subMenu), presetsMenuPath, StringComparison.Ordinal)));

            // Check VRC menu control limit (8 max)
            if (rootMenu.controls.Count >= 8)
            {
                Debug.LogError($"[ASM-Lite] InjectExpressionMenu: avatar expression menu already has 8 controls. Cannot add {effectiveRootControlName} entry.");
                return;
            }

            var iconPresets = AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconPresets);
            Texture2D effectiveRootControlIcon = ResolveEffectiveRootControlIcon(component, iconPresets);

            rootMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name    = effectiveRootControlName,
                type    = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = presetsMenu,
                icon    = effectiveRootControlIcon,
            });

            EditorUtility.SetDirty(rootMenu);

#if ASM_LITE_VERBOSE
            Debug.Log($"[ASM-Lite] InjectExpressionMenu: '{effectiveRootControlName}' entry added to avatar expression menu.");
#endif
        }

        /// <summary>
        /// Removes all ASM-Lite injected content from the avatar's FX controller,
        /// expression parameters, and expression menu. Called when removing the
        /// ASM-Lite prefab from the avatar.
        /// </summary>
        public static void CleanUpAvatarAssets(VRCAvatarDescriptor avDesc)
        {
            _ = CleanUpAvatarAssetsWithReport(avDesc);
        }

        internal static CleanupReport CleanUpAvatarAssetsWithReport(VRCAvatarDescriptor avDesc)
        {
            if (avDesc == null)
                return new CleanupReport(0, 0, 0, 0, descriptorMissing: true);

            int removedFxLayers = 0;
            int removedFxParams = 0;
            int removedExprParams = 0;
            int removedMenuControls = 0;

            // Clean FX controller
            for (int i = 0; i < avDesc.baseAnimationLayers.Length; i++)
            {
                if (avDesc.baseAnimationLayers[i].type != VRCAvatarDescriptor.AnimLayerType.FX)
                    continue;

                var ctrl = avDesc.baseAnimationLayers[i].animatorController as AnimatorController;
                if (ctrl == null) break;

                // Remove ASMLite_ layers
                for (int j = ctrl.layers.Length - 1; j >= 0; j--)
                {
                    if (!ctrl.layers[j].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    ctrl.RemoveLayer(j);
                    removedFxLayers++;
                }

                // Remove ASMLite_ parameters (drain loop)
                bool removed;
                do
                {
                    removed = false;
                    foreach (var p in ctrl.parameters)
                    {
                        if (string.IsNullOrEmpty(p.name))
                            continue;
                        if (!p.name.StartsWith("ASMLite_", StringComparison.Ordinal) && p.name != CtrlParam)
                            continue;

                        ctrl.RemoveParameter(p);
                        removedFxParams++;
                        removed = true;
                        break;
                    }
                } while (removed);

                EditorUtility.SetDirty(ctrl);
                break;
            }

            // Clean expression parameters
            var exprParams = avDesc.expressionParameters;
            if (exprParams != null && exprParams.parameters != null)
            {
                var filtered = new List<VRCExpressionParameters.Parameter>(exprParams.parameters.Length);
                foreach (var p in exprParams.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name)) continue;

                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal) || p.name == CtrlParam)
                    {
                        removedExprParams++;
                        continue;
                    }

                    filtered.Add(p);
                }

                exprParams.parameters = filtered.ToArray();
                EditorUtility.SetDirty(exprParams);
            }

            // Clean expression menu
            var rootMenu = avDesc.expressionsMenu;
            if (rootMenu != null && rootMenu.controls != null)
            {
                for (int i = rootMenu.controls.Count - 1; i >= 0; i--)
                {
                    var control = rootMenu.controls[i];
                    if (control == null)
                        continue;
                    if (control.name != DefaultRootControlName)
                        continue;
                    if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    rootMenu.controls.RemoveAt(i);
                    removedMenuControls++;
                }

                EditorUtility.SetDirty(rootMenu);
            }

            AssetDatabase.SaveAssets();
            return new CleanupReport(removedFxLayers, removedFxParams, removedExprParams, removedMenuControls, descriptorMissing: false);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves the root wrapper menu control name for generated/injected paths.
        /// Uses the trimmed custom value only when explicitly enabled; otherwise falls
        /// back to the baseline Settings Manager contract.
        /// </summary>
        internal static string ResolveEffectiveRootControlName(ASMLiteComponent component)
        {
            if (component == null || !component.useCustomRootName)
                return DefaultRootControlName;

            string trimmed = component.customRootName?.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? DefaultRootControlName : trimmed;
        }

        /// <summary>
        /// Resolves the root wrapper menu control icon for generated/injected paths.
        /// Uses the custom icon only when explicitly enabled and non-null; otherwise
        /// falls back to the bundled presets icon contract.
        /// </summary>
        internal static Texture2D ResolveEffectiveRootControlIcon(ASMLiteComponent component, Texture2D fallbackIcon)
        {
            if (component == null || !component.useCustomRootIcon)
                return fallbackIcon;

            return component.customRootIcon != null ? component.customRootIcon : fallbackIcon;
        }

        /// <summary>
        /// Maps a VRCExpressionParameters ValueType to the corresponding
        /// AnimatorControllerParameterType. Uses ValueType (not ParameterType) --
        /// the enum is on VRCExpressionParameters, not on the parameter struct.
        /// </summary>
        internal static AnimatorControllerParameterType MapValueType(VRCExpressionParameters.ValueType vt)
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
        /// <paramref name="cache"/> deduplicates loads when the same path is resolved
        /// for multiple slots (e.g. SameColor mode).
        /// </summary>
        private static Texture2D ResolveSlotIcon(
            ASMLiteComponent component, int slot, Texture2D fallback,
            Dictionary<string, Texture2D> cache)
        {
            switch (component.iconMode)
            {
                case IconMode.SameColor:
                {
                    string path = ASMLiteAssetPaths.GearIconPaths[component.selectedGearIndex];
                    if (!cache.TryGetValue(path, out var tex))
                        cache[path] = tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                    return tex != null ? tex : fallback;
                }
                case IconMode.MultiColor:
                {
                    string path = ASMLiteAssetPaths.GearIconPaths[(slot - 1) % ASMLiteAssetPaths.GearIconPaths.Length];
                    if (!cache.TryGetValue(path, out var tex))
                        cache[path] = tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
