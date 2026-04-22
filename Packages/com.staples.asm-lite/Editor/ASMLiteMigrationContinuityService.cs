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
        internal readonly struct ComponentCustomizationSnapshot
        {
            internal ComponentCustomizationSnapshot(
                int slotCount,
                IconMode iconMode,
                int selectedGearIndex,
                ActionIconMode actionIconMode,
                Texture2D customSaveIcon,
                Texture2D customLoadIcon,
                Texture2D customClearIcon,
                bool useCustomSlotIcons,
                Texture2D[] customIcons,
                bool useCustomRootIcon,
                Texture2D customRootIcon,
                bool useCustomRootName,
                string customRootName,
                string[] customPresetNames,
                string customPresetNameFormat,
                string customSaveLabel,
                string customLoadLabel,
                string customClearPresetLabel,
                string customConfirmLabel,
                bool useCustomInstallPath,
                string customInstallPath,
                bool useParameterExclusions,
                string[] excludedParameterNames,
                bool useVendorizedGeneratedAssets,
                string vendorizedGeneratedAssetsPath)
            {
                SlotCount = slotCount;
                IconMode = iconMode;
                SelectedGearIndex = selectedGearIndex;
                ActionIconMode = actionIconMode;
                CustomSaveIcon = customSaveIcon;
                CustomLoadIcon = customLoadIcon;
                CustomClearIcon = customClearIcon;
                UseCustomSlotIcons = useCustomSlotIcons;
                CustomIcons = CloneTextures(customIcons);
                UseCustomRootIcon = useCustomRootIcon;
                CustomRootIcon = customRootIcon;
                UseCustomRootName = useCustomRootName;
                CustomRootName = NormalizeOptionalString(customRootName);
                CustomPresetNames = CloneStrings(customPresetNames);
                CustomPresetNameFormat = NormalizeOptionalString(customPresetNameFormat);
                CustomSaveLabel = customSaveLabel ?? string.Empty;
                CustomLoadLabel = customLoadLabel ?? string.Empty;
                CustomClearPresetLabel = customClearPresetLabel ?? string.Empty;
                CustomConfirmLabel = customConfirmLabel ?? string.Empty;
                UseCustomInstallPath = useCustomInstallPath;
                CustomInstallPath = NormalizeOptionalString(customInstallPath);
                UseParameterExclusions = useParameterExclusions;
                ExcludedParameterNames = SanitizeExcludedParameterNames(excludedParameterNames);
                UseVendorizedGeneratedAssets = useVendorizedGeneratedAssets;
                VendorizedGeneratedAssetsPath = NormalizeOptionalString(vendorizedGeneratedAssetsPath);
            }

            internal int SlotCount { get; }
            internal IconMode IconMode { get; }
            internal int SelectedGearIndex { get; }
            internal ActionIconMode ActionIconMode { get; }
            internal Texture2D CustomSaveIcon { get; }
            internal Texture2D CustomLoadIcon { get; }
            internal Texture2D CustomClearIcon { get; }
            internal bool UseCustomSlotIcons { get; }
            internal Texture2D[] CustomIcons { get; }
            internal bool UseCustomRootIcon { get; }
            internal Texture2D CustomRootIcon { get; }
            internal bool UseCustomRootName { get; }
            internal string CustomRootName { get; }
            internal string[] CustomPresetNames { get; }
            internal string CustomPresetNameFormat { get; }
            internal string CustomSaveLabel { get; }
            internal string CustomLoadLabel { get; }
            internal string CustomClearPresetLabel { get; }
            internal string CustomConfirmLabel { get; }
            internal bool UseCustomInstallPath { get; }
            internal string CustomInstallPath { get; }
            internal bool UseParameterExclusions { get; }
            internal string[] ExcludedParameterNames { get; }
            internal bool UseVendorizedGeneratedAssets { get; }
            internal string VendorizedGeneratedAssetsPath { get; }
        }

        internal readonly struct InstallPathAdoptionResult
        {
            internal InstallPathAdoptionResult(bool adopted, string adoptedInstallPrefix, int removedMoveComponents)
            {
                Adopted = adopted;
                AdoptedInstallPrefix = adoptedInstallPrefix ?? string.Empty;
                RemovedMoveComponents = removedMoveComponents;
            }

            internal bool Adopted { get; }
            internal string AdoptedInstallPrefix { get; }
            internal int RemovedMoveComponents { get; }
            internal bool HasChanges => Adopted || RemovedMoveComponents > 0;
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

        internal static ComponentCustomizationSnapshot CaptureCustomizationSnapshot(ASMLiteComponent component)
        {
            if (component == null)
                return default;

            return new ComponentCustomizationSnapshot(
                component.slotCount,
                component.iconMode,
                component.selectedGearIndex,
                component.actionIconMode,
                component.customSaveIcon,
                component.customLoadIcon,
                component.customClearIcon,
                component.useCustomSlotIcons,
                component.customIcons,
                component.useCustomRootIcon,
                component.customRootIcon,
                component.useCustomRootName,
                component.customRootName,
                component.customPresetNames,
                component.customPresetNameFormat,
                component.customSaveLabel,
                component.customLoadLabel,
                component.customClearPresetLabel,
                component.customConfirmLabel,
                component.useCustomInstallPath,
                component.customInstallPath,
                component.useParameterExclusions,
                component.excludedParameterNames,
                component.useVendorizedGeneratedAssets,
                component.vendorizedGeneratedAssetsPath);
        }

        internal static void ApplyCustomizationSnapshot(ASMLiteComponent component, ComponentCustomizationSnapshot snapshot)
        {
            if (component == null)
                return;

            component.slotCount = snapshot.SlotCount;
            component.iconMode = snapshot.IconMode;
            component.selectedGearIndex = snapshot.SelectedGearIndex;
            component.actionIconMode = snapshot.ActionIconMode;
            component.customSaveIcon = snapshot.CustomSaveIcon;
            component.customLoadIcon = snapshot.CustomLoadIcon;
            component.customClearIcon = snapshot.CustomClearIcon;
            component.useCustomSlotIcons = snapshot.UseCustomSlotIcons;
            component.customIcons = CloneTextures(snapshot.CustomIcons);
            component.useCustomRootIcon = snapshot.UseCustomRootIcon;
            component.customRootIcon = snapshot.CustomRootIcon;
            component.useCustomRootName = snapshot.UseCustomRootName;
            component.customRootName = snapshot.CustomRootName;
            component.customPresetNames = CloneStrings(snapshot.CustomPresetNames);
            component.customPresetNameFormat = snapshot.CustomPresetNameFormat;
            component.customSaveLabel = snapshot.CustomSaveLabel;
            component.customLoadLabel = snapshot.CustomLoadLabel;
            component.customClearPresetLabel = snapshot.CustomClearPresetLabel;
            component.customConfirmLabel = snapshot.CustomConfirmLabel;
            component.useCustomInstallPath = snapshot.UseCustomInstallPath;
            component.customInstallPath = snapshot.CustomInstallPath;
            component.useParameterExclusions = snapshot.UseParameterExclusions;
            component.excludedParameterNames = SanitizeExcludedParameterNames(snapshot.ExcludedParameterNames);
            component.useVendorizedGeneratedAssets = snapshot.UseVendorizedGeneratedAssets;
            component.vendorizedGeneratedAssetsPath = snapshot.VendorizedGeneratedAssetsPath;
        }

        internal static InstallPathAdoptionResult TryAdoptInstallPathFromMoveMenu(ASMLiteComponent component, VRCAvatarDescriptor avatar)
        {
            if (component == null || avatar == null)
                return default;

            string effectiveRootName = ASMLiteBuilder.ResolveEffectiveRootControlName(component);
            if (string.IsNullOrWhiteSpace(effectiveRootName))
                return default;

            var remaps = GetVrcFuryMoveMenuPathRemaps(avatar);
            if (remaps.Count == 0)
                return default;

            string normalizedRoot = NormalizeSlashPath(effectiveRootName);
            string matchedDestination = null;
            foreach (var kv in remaps)
            {
                if (!string.Equals(NormalizeSlashPath(kv.Key), normalizedRoot, StringComparison.Ordinal))
                    continue;

                matchedDestination = kv.Value;
                break;
            }

            if (string.IsNullOrWhiteSpace(matchedDestination))
                return default;

            if (!TryResolveInstallPrefixFromMovedRootPath(effectiveRootName, matchedDestination, out string resolvedPrefix))
                return default;

            var currentSnapshot = CaptureCustomizationSnapshot(component);
            string normalizedPrefix = ASMLiteFullControllerInstallPathHelper.ResolveEffectivePrefix(true, resolvedPrefix);
            bool changedComponent = !currentSnapshot.UseCustomInstallPath
                || !string.Equals(currentSnapshot.CustomInstallPath, normalizedPrefix, StringComparison.Ordinal);

            if (changedComponent)
            {
                Undo.RecordObject(component, "Adopt ASM-Lite Install Path From Move Menu");
                var adoptedSnapshot = new ComponentCustomizationSnapshot(
                    currentSnapshot.SlotCount,
                    currentSnapshot.IconMode,
                    currentSnapshot.SelectedGearIndex,
                    currentSnapshot.ActionIconMode,
                    currentSnapshot.CustomSaveIcon,
                    currentSnapshot.CustomLoadIcon,
                    currentSnapshot.CustomClearIcon,
                    currentSnapshot.UseCustomSlotIcons,
                    currentSnapshot.CustomIcons,
                    currentSnapshot.UseCustomRootIcon,
                    currentSnapshot.CustomRootIcon,
                    currentSnapshot.UseCustomRootName,
                    currentSnapshot.CustomRootName,
                    currentSnapshot.CustomPresetNames,
                    currentSnapshot.CustomPresetNameFormat,
                    currentSnapshot.CustomSaveLabel,
                    currentSnapshot.CustomLoadLabel,
                    currentSnapshot.CustomClearPresetLabel,
                    currentSnapshot.CustomConfirmLabel,
                    true,
                    normalizedPrefix,
                    currentSnapshot.UseParameterExclusions,
                    currentSnapshot.ExcludedParameterNames,
                    currentSnapshot.UseVendorizedGeneratedAssets,
                    currentSnapshot.VendorizedGeneratedAssetsPath);
                ApplyCustomizationSnapshot(component, adoptedSnapshot);
                EditorUtility.SetDirty(component);
            }

            int removedMoveComponents = RemoveMatchingMoveMenuHelpers(avatar, normalizedRoot);
            return new InstallPathAdoptionResult(changedComponent, normalizedPrefix, removedMoveComponents);
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

        private static int RemoveMatchingMoveMenuHelpers(VRCAvatarDescriptor avatar, string normalizedRoot)
        {
            int removedMoveComponents = 0;
            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                if (string.Equals(behaviour.gameObject.name, "ASM-Lite Install Path Routing", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                var content = so.FindProperty("content");
                if (content == null || content.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                string managedRefType = content.managedReferenceFullTypename;
                if (string.IsNullOrWhiteSpace(managedRefType)
                    || managedRefType.IndexOf("MoveMenuItem", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                var fromProp = so.FindProperty("content.fromPath");
                if (fromProp == null || fromProp.propertyType != SerializedPropertyType.String)
                    continue;

                string fromPath = NormalizeSlashPath(fromProp.stringValue);
                if (!string.Equals(fromPath, normalizedRoot, StringComparison.Ordinal))
                    continue;

                Undo.DestroyObjectImmediate(behaviour);
                removedMoveComponents++;
            }

            return removedMoveComponents;
        }

        private static Dictionary<string, string> GetVrcFuryMoveMenuPathRemaps(VRCAvatarDescriptor avatar)
        {
            var remaps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (avatar == null)
                return remaps;

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                var iterator = so.GetIterator();
                if (!iterator.NextVisible(true))
                    continue;

                var seenPaths = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                        continue;

                    string managedRefType = iterator.managedReferenceFullTypename;
                    if (string.IsNullOrWhiteSpace(managedRefType)
                        || managedRefType.IndexOf("MoveMenuItem", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    string managedPath = iterator.propertyPath;
                    if (!seenPaths.Add(managedPath))
                        continue;

                    var fromProp = so.FindProperty(managedPath + ".fromPath");
                    var toProp = so.FindProperty(managedPath + ".toPath");
                    if (fromProp == null || toProp == null)
                        continue;
                    if (fromProp.propertyType != SerializedPropertyType.String
                        || toProp.propertyType != SerializedPropertyType.String)
                    {
                        continue;
                    }

                    string fromPath = NormalizeSlashPath(fromProp.stringValue);
                    string toPath = NormalizeSlashPath(toProp.stringValue);
                    if (string.IsNullOrWhiteSpace(toPath))
                        continue;

                    if (!remaps.ContainsKey(fromPath ?? string.Empty))
                        remaps[fromPath ?? string.Empty] = toPath;
                } while (iterator.NextVisible(true));
            }

            return remaps;
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

        private static bool TryResolveInstallPrefixFromMovedRootPath(string rootControlName, string movedDestinationPath, out string installPrefix)
        {
            installPrefix = string.Empty;

            string root = NormalizeOptionalString(rootControlName);
            string destination = NormalizeSlashPath(movedDestinationPath);
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(destination))
                return false;

            if (string.Equals(destination, root, StringComparison.Ordinal))
            {
                installPrefix = string.Empty;
                return true;
            }

            string suffix = "/" + root;
            if (destination.EndsWith(suffix, StringComparison.Ordinal))
            {
                installPrefix = destination.Substring(0, destination.Length - suffix.Length);
                return true;
            }

            installPrefix = destination;
            return true;
        }

        private static Texture2D[] CloneTextures(Texture2D[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Texture2D>();

            var clone = new Texture2D[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static string[] CloneStrings(string[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<string>();

            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static string NormalizeOptionalString(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string NormalizeMenuPathSegment(string value)
        {
            return NormalizeOptionalString(value)
                .Replace('\\', '/')
                .Trim('/');
        }

        private static string NormalizeSlashPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Replace('\\', '/');
            var rawSegments = normalized.Split('/');
            var cleanSegments = new List<string>(rawSegments.Length);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = NormalizeMenuPathSegment(rawSegments[i]);
                if (!string.IsNullOrEmpty(segment))
                    cleanSegments.Add(segment);
            }

            return cleanSegments.Count == 0 ? string.Empty : string.Join("/", cleanSegments);
        }

        private static string[] SanitizeExcludedParameterNames(string[] names)
        {
            if (names == null || names.Length == 0)
                return Array.Empty<string>();

            var sanitized = new List<string>(names.Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Length; i++)
            {
                string candidate = NormalizeOptionalString(names[i]);
                if (string.IsNullOrEmpty(candidate) || !seen.Add(candidate))
                    continue;

                sanitized.Add(candidate);
            }

            return sanitized.Count == 0 ? Array.Empty<string>() : sanitized.ToArray();
        }
    }
}
