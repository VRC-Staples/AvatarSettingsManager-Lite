using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using ASMLite;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    internal static class ASMLiteMigrationContinuityService
    {
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

        internal static ASMLiteBuilder.BackupNamePlan BuildBackupNamePlan(
            int slotCount,
            List<VRCExpressionParameters.Parameter> avatarParams,
            string[] existingParamNames,
            ASMLiteToggleNameBroker.GlobalParamMapping[] brokerMappings,
            HashSet<string> excludedCanonicalNames)
        {
            avatarParams ??= new List<VRCExpressionParameters.Parameter>();

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
            var bindings = new List<ASMLiteBuilder.LegacyAliasBinding>();
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

                    if (excludedCanonicalNames != null
                        && excludedCanonicalNames.Count > 0
                        && excludedCanonicalNames.Contains(parsed.SourceParamName))
                    {
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
                        bindings.Add(new ASMLiteBuilder.LegacyAliasBinding(parsed.Slot, assignedSourceName, existingName));
                }
            }

            int mirroredCount = bindings.Count;
            var report = new ASMLiteBuilder.LegacyAliasContinuityReport(mappedCount, mirroredCount, unmatchedCount, malformedCount);
            return new ASMLiteBuilder.BackupNamePlan(names, bindings, report);
        }

        internal static string[] GetExistingGeneratedBackupNames()
        {
            var paramsAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (paramsAsset?.parameters == null)
                return Array.Empty<string>();

            var existing = new string[paramsAsset.parameters.Length];
            for (int i = 0; i < paramsAsset.parameters.Length; i++)
                existing[i] = paramsAsset.parameters[i]?.name;

            return existing;
        }

        internal static int MigrateStaleVRCFuryComponentsWithReport(ASMLiteComponent component)
        {
            if (component == null)
                return 0;

            var go = component.gameObject;
            var allComponents = go.GetComponents<Component>();
            var vfComponents = new List<Component>();
            foreach (var candidate in allComponents)
            {
                if (candidate == null)
                    continue;

                string typeName = candidate.GetType().FullName;
                if (typeName == "VF.Model.VRCFury")
                    vfComponents.Add(candidate);
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

        internal static ASMLiteBuilder.RebuildMigrationReport PrepareRevertedDeliveryRebuild(ASMLiteComponent component)
        {
            if (component == null)
            {
                var emptyCleanup = new ASMLiteBuilder.CleanupReport(0, 0, 0, 0, descriptorMissing: true);
                return new ASMLiteBuilder.RebuildMigrationReport(0, emptyCleanup, componentMissing: true, avatarDescriptorFound: false);
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

            return new ASMLiteBuilder.RebuildMigrationReport(staleVfRemoved, cleanup, componentMissing: false, avatarDescriptorFound: avatarDescriptorFound);
        }

        internal static ASMLiteBuilder.CleanupReport CleanUpAvatarAssetsWithReport(VRCAvatarDescriptor avDesc)
        {
            if (avDesc == null)
                return new ASMLiteBuilder.CleanupReport(0, 0, 0, 0, descriptorMissing: true);

            int removedFxLayers = 0;
            int removedFxParams = 0;
            int removedExprParams = 0;
            int removedMenuControls = 0;

            for (int i = 0; i < avDesc.baseAnimationLayers.Length; i++)
            {
                if (avDesc.baseAnimationLayers[i].type != VRCAvatarDescriptor.AnimLayerType.FX)
                    continue;

                var ctrl = avDesc.baseAnimationLayers[i].animatorController as AnimatorController;
                if (ctrl == null)
                    break;

                for (int j = ctrl.layers.Length - 1; j >= 0; j--)
                {
                    if (!ctrl.layers[j].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    ctrl.RemoveLayer(j);
                    removedFxLayers++;
                }

                bool removed;
                do
                {
                    removed = false;
                    foreach (var parameter in ctrl.parameters)
                    {
                        if (string.IsNullOrEmpty(parameter.name))
                            continue;
                        if (!parameter.name.StartsWith("ASMLite_", StringComparison.Ordinal) && parameter.name != ASMLiteBuilder.CtrlParam)
                            continue;

                        ctrl.RemoveParameter(parameter);
                        removedFxParams++;
                        removed = true;
                        break;
                    }
                } while (removed);

                EditorUtility.SetDirty(ctrl);
                break;
            }

            var exprParams = avDesc.expressionParameters;
            if (exprParams != null && exprParams.parameters != null)
            {
                var filtered = new List<VRCExpressionParameters.Parameter>(exprParams.parameters.Length);
                foreach (var parameter in exprParams.parameters)
                {
                    if (parameter == null || string.IsNullOrEmpty(parameter.name))
                        continue;

                    if (parameter.name.StartsWith("ASMLite_", StringComparison.Ordinal) || parameter.name == ASMLiteBuilder.CtrlParam)
                    {
                        removedExprParams++;
                        continue;
                    }

                    filtered.Add(parameter);
                }

                exprParams.parameters = filtered.ToArray();
                EditorUtility.SetDirty(exprParams);
            }

            var rootMenu = avDesc.expressionsMenu;
            if (rootMenu != null && rootMenu.controls != null)
            {
                string generatedPresetsMenuPath = ($"{ASMLiteAssetPaths.GeneratedDir}/ASMLite_Presets_Menu.asset").Replace('\\', '/');
                const string presetsMenuFileName = "ASMLite_Presets_Menu.asset";

                for (int i = rootMenu.controls.Count - 1; i >= 0; i--)
                {
                    var control = rootMenu.controls[i];
                    if (control == null)
                        continue;
                    if (control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    string submenuPath = control.subMenu != null
                        ? (AssetDatabase.GetAssetPath(control.subMenu) ?? string.Empty).Replace('\\', '/')
                        : string.Empty;

                    bool matchesAsmLiteRootName = string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal);
                    bool matchesGeneratedPresetsPath = string.Equals(submenuPath, generatedPresetsMenuPath, StringComparison.Ordinal);
                    bool matchesPresetsMenuFileName = !string.IsNullOrWhiteSpace(submenuPath)
                        && string.Equals(Path.GetFileName(submenuPath), presetsMenuFileName, StringComparison.Ordinal);

                    if (!matchesAsmLiteRootName && !matchesGeneratedPresetsPath && !matchesPresetsMenuFileName)
                        continue;

                    rootMenu.controls.RemoveAt(i);
                    removedMenuControls++;
                }

                EditorUtility.SetDirty(rootMenu);
            }

            AssetDatabase.SaveAssets();
            return new ASMLiteBuilder.CleanupReport(removedFxLayers, removedFxParams, removedExprParams, removedMenuControls, descriptorMissing: false);
        }

        private static bool TryParseBackupName(string backupName, out ParsedBackupName parsed)
        {
            parsed = default;
            if (string.IsNullOrWhiteSpace(backupName) || !backupName.StartsWith("ASMLite_Bak_S", StringComparison.Ordinal))
                return false;

            int separator = backupName.IndexOf('_', "ASMLite_Bak_S".Length);
            if (separator < 0)
                return false;

            string slotText = backupName.Substring("ASMLite_Bak_S".Length, separator - "ASMLite_Bak_S".Length);
            if (!int.TryParse(slotText, out int slot) || slot <= 0)
                return false;

            string source = backupName.Substring(separator + 1);
            if (string.IsNullOrWhiteSpace(source))
                return false;

            parsed = new ParsedBackupName(slot, source);
            return true;
        }
    }
}
