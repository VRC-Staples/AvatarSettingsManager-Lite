using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Editor
{
    internal sealed class ASMLiteCustomizationDraft
    {
        internal interface IApplyAdapter
        {
            void RecordObject(UnityEngine.Object target, string undoLabel);
            void SetDirty(UnityEngine.Object target);
        }

        private sealed class UnityApplyAdapter : IApplyAdapter
        {
            internal static readonly UnityApplyAdapter Instance = new UnityApplyAdapter();

            public void RecordObject(UnityEngine.Object target, string undoLabel)
            {
                Undo.RecordObject(target, undoLabel);
            }

            public void SetDirty(UnityEngine.Object target)
            {
                EditorUtility.SetDirty(target);
            }
        }

        private Texture2D[] _customIcons = new Texture2D[3];
        private string[] _customPresetNames = Array.Empty<string>();
        private string[] _excludedParameterNames = Array.Empty<string>();

        private ASMLiteCustomizationDraft()
        {
            SlotCount = 3;
            IconMode = IconMode.MultiColor;
            SelectedGearIndex = 0;
            ActionIconMode = ActionIconMode.Default;
        }

        internal int SlotCount { get; set; }
        internal IconMode IconMode { get; set; }
        internal int SelectedGearIndex { get; set; }
        internal ActionIconMode ActionIconMode { get; set; }
        internal Texture2D CustomSaveIcon { get; set; }
        internal Texture2D CustomLoadIcon { get; set; }
        internal Texture2D CustomClearIcon { get; set; }
        internal bool UseCustomSlotIcons { get; set; }

        internal Texture2D[] CustomIcons
        {
            get => _customIcons;
            set => _customIcons = CloneTextures(value);
        }

        internal bool UseCustomRootIcon { get; set; }
        internal Texture2D CustomRootIcon { get; set; }
        internal bool UseCustomRootName { get; set; }

        internal string CustomRootName { get; set; } = string.Empty;

        internal string[] CustomPresetNames
        {
            get => _customPresetNames;
            set => _customPresetNames = CloneStrings(value);
        }

        internal string CustomPresetNameFormat { get; set; } = string.Empty;
        internal string CustomSaveLabel { get; set; } = string.Empty;
        internal string CustomLoadLabel { get; set; } = string.Empty;
        internal string CustomClearPresetLabel { get; set; } = string.Empty;
        internal string CustomConfirmLabel { get; set; } = string.Empty;
        internal bool UseCustomInstallPath { get; set; }
        internal string CustomInstallPath { get; set; } = string.Empty;
        internal bool UseParameterExclusions { get; set; }

        internal string[] ExcludedParameterNames
        {
            get => _excludedParameterNames;
            set => _excludedParameterNames = SanitizeExcludedParameterNames(value);
        }

        internal bool UseVendorizedGeneratedAssets { get; set; }
        internal string VendorizedGeneratedAssetsPath { get; set; } = string.Empty;

        internal static ASMLiteCustomizationDraft CreateDefault()
        {
            return new ASMLiteCustomizationDraft();
        }

        internal static ASMLiteCustomizationDraft FromSnapshot(ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot snapshot)
        {
            var draft = CreateDefault();
            draft.ApplySnapshot(snapshot);
            return draft;
        }

        internal static ASMLiteCustomizationDraft CaptureFromComponent(ASMLiteComponent component)
        {
            return FromSnapshot(ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(component));
        }

        internal void RefreshFromComponent(ASMLiteComponent component)
        {
            ApplySnapshot(ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(component));
        }

        internal void ApplySnapshot(ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot snapshot)
        {
            SlotCount = snapshot.SlotCount;
            IconMode = snapshot.IconMode;
            SelectedGearIndex = snapshot.SelectedGearIndex;
            ActionIconMode = snapshot.ActionIconMode;
            CustomSaveIcon = snapshot.CustomSaveIcon;
            CustomLoadIcon = snapshot.CustomLoadIcon;
            CustomClearIcon = snapshot.CustomClearIcon;
            UseCustomSlotIcons = snapshot.UseCustomSlotIcons;
            CustomIcons = snapshot.CustomIcons;
            UseCustomRootIcon = snapshot.UseCustomRootIcon;
            CustomRootIcon = snapshot.CustomRootIcon;
            UseCustomRootName = snapshot.UseCustomRootName;
            CustomRootName = snapshot.CustomRootName;
            CustomPresetNames = snapshot.CustomPresetNames;
            CustomPresetNameFormat = snapshot.CustomPresetNameFormat;
            CustomSaveLabel = snapshot.CustomSaveLabel;
            CustomLoadLabel = snapshot.CustomLoadLabel;
            CustomClearPresetLabel = snapshot.CustomClearPresetLabel;
            CustomConfirmLabel = snapshot.CustomConfirmLabel;
            UseCustomInstallPath = snapshot.UseCustomInstallPath;
            CustomInstallPath = snapshot.CustomInstallPath;
            UseParameterExclusions = snapshot.UseParameterExclusions;
            ExcludedParameterNames = snapshot.ExcludedParameterNames;
            UseVendorizedGeneratedAssets = snapshot.UseVendorizedGeneratedAssets;
            VendorizedGeneratedAssetsPath = snapshot.VendorizedGeneratedAssetsPath;
        }

        internal ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot ToComponentSnapshot()
        {
            return new ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot(
                SlotCount,
                IconMode,
                SelectedGearIndex,
                ActionIconMode,
                CustomSaveIcon,
                CustomLoadIcon,
                CustomClearIcon,
                UseCustomSlotIcons,
                CustomIcons,
                UseCustomRootIcon,
                CustomRootIcon,
                UseCustomRootName,
                CustomRootName,
                CustomPresetNames,
                CustomPresetNameFormat,
                CustomSaveLabel,
                CustomLoadLabel,
                CustomClearPresetLabel,
                CustomConfirmLabel,
                UseCustomInstallPath,
                CustomInstallPath,
                UseParameterExclusions,
                ExcludedParameterNames,
                UseVendorizedGeneratedAssets,
                VendorizedGeneratedAssetsPath);
        }

        internal ASMLiteWindow.PendingCustomizationSnapshot ToPendingSnapshot(VRCAvatarDescriptor selectedAvatar)
        {
            var snapshot = ToComponentSnapshot();
            return new ASMLiteWindow.PendingCustomizationSnapshot(
                selectedAvatar,
                snapshot.UseCustomRootIcon,
                snapshot.UseCustomRootName,
                snapshot.CustomRootName,
                NormalizePresetNamesBySlot(snapshot.CustomPresetNames, snapshot.SlotCount),
                snapshot.CustomSaveLabel,
                snapshot.CustomLoadLabel,
                snapshot.CustomClearPresetLabel,
                snapshot.CustomConfirmLabel,
                snapshot.UseCustomInstallPath,
                snapshot.CustomInstallPath,
                snapshot.UseParameterExclusions,
                snapshot.ExcludedParameterNames,
                snapshot.CustomIcons,
                snapshot.UseVendorizedGeneratedAssets,
                snapshot.VendorizedGeneratedAssetsPath);
        }

        internal static ASMLiteWindow.CustomizationAutomationSnapshot CreateAutomationSnapshot(
            VRCAvatarDescriptor selectedAvatar,
            ASMLiteComponent component,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot customization,
            ASMLiteInstallationState toolState,
            ASMLiteWindow.AsmLiteActionHierarchy actionHierarchy)
        {
            return new ASMLiteWindow.CustomizationAutomationSnapshot(
                selectedAvatar,
                component,
                customization,
                toolState,
                actionHierarchy);
        }

        internal void SetSlotCount(int slotCount)
        {
            SlotCount = slotCount;
            CustomIcons = EnsureSizedTextureArray(CustomIcons, slotCount);
            CustomPresetNames = EnsureSizedStringArray(CustomPresetNames, slotCount);
        }

        internal void SetIconMode(IconMode iconMode)
        {
            IconMode = iconMode;
        }

        internal void SetGearIndex(int selectedGearIndex)
        {
            SelectedGearIndex = selectedGearIndex;
        }

        internal void SetRootNameState(bool enabled, string value)
        {
            UseCustomRootName = enabled;
            CustomRootName = enabled ? NormalizeOptionalString(value) : string.Empty;
        }

        internal void SetPresetNames(string[] presetNamesBySlot, bool clearLegacyFormat)
        {
            CustomPresetNames = NormalizePresetNamesBySlot(presetNamesBySlot, SlotCount);
            if (clearLegacyFormat)
                CustomPresetNameFormat = string.Empty;
        }

        internal void SetActionLabels(string saveLabel, string loadLabel, string clearLabel, string confirmLabel)
        {
            CustomSaveLabel = NormalizeOptionalString(saveLabel);
            CustomLoadLabel = NormalizeOptionalString(loadLabel);
            CustomClearPresetLabel = NormalizeOptionalString(clearLabel);
            CustomConfirmLabel = NormalizeOptionalString(confirmLabel);
        }

        internal void SetInstallPathState(bool useCustomInstallPath, string customInstallPath)
        {
            UseCustomInstallPath = useCustomInstallPath;
            CustomInstallPath = useCustomInstallPath ? NormalizeInstallPath(customInstallPath) : string.Empty;
        }

        internal void SetParameterExclusions(bool useParameterExclusions, IEnumerable<string> excludedParameterNames)
        {
            UseParameterExclusions = useParameterExclusions;
            ExcludedParameterNames = useParameterExclusions ? excludedParameterNames?.ToArray() : Array.Empty<string>();
        }

        internal void SetCustomIconsEnabled(bool enabled)
        {
            UseCustomSlotIcons = enabled;
            CustomIcons = enabled ? EnsureSizedTextureArray(CustomIcons, SlotCount) : new Texture2D[Mathf.Max(0, SlotCount)];
            if (!enabled)
                ClearCustomIconOverrides();
        }

        internal void SetRootIcon(Texture2D rootIcon)
        {
            UseCustomRootIcon = rootIcon != null;
            CustomRootIcon = rootIcon;
        }

        internal void SetSlotIcons(Texture2D[] slotIcons)
        {
            UseCustomSlotIcons = true;
            CustomIcons = EnsureSizedTextureArray(slotIcons, SlotCount);
        }

        internal void SetActionIcons(Texture2D saveIcon, Texture2D loadIcon, Texture2D clearIcon)
        {
            ActionIconMode = saveIcon != null || loadIcon != null || clearIcon != null
                ? ActionIconMode.Custom
                : ActionIconMode.Default;
            CustomSaveIcon = saveIcon;
            CustomLoadIcon = loadIcon;
            CustomClearIcon = clearIcon;
        }

        internal void SetIconFixtureTextures(
            Texture2D rootIcon,
            Texture2D[] slotIcons,
            Texture2D saveIcon,
            Texture2D loadIcon,
            Texture2D clearIcon)
        {
            UseCustomSlotIcons = true;
            SetSlotIcons(slotIcons);
            SetRootIcon(rootIcon);
            SetActionIcons(saveIcon, loadIcon, clearIcon);
        }

        private void ClearCustomIconOverrides()
        {
            UseCustomRootIcon = false;
            CustomRootIcon = null;
            ActionIconMode = ActionIconMode.Default;
            CustomSaveIcon = null;
            CustomLoadIcon = null;
            CustomClearIcon = null;
        }

        internal bool HasDiffAgainst(ASMLiteComponent component)
        {
            if (component == null)
                return true;

            return !SnapshotsEqual(ToComponentSnapshot(), ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(component));
        }

        internal bool ApplyToComponent(ASMLiteComponent component, string undoLabel)
        {
            return ApplyToComponent(component, UnityApplyAdapter.Instance, undoLabel);
        }

        internal bool ApplyToComponent(ASMLiteComponent component, IApplyAdapter adapter, string undoLabel)
        {
            if (component == null)
                return false;

            if (!HasDiffAgainst(component))
                return false;

            adapter ??= UnityApplyAdapter.Instance;
            adapter.RecordObject(component, string.IsNullOrWhiteSpace(undoLabel) ? "Apply ASM-Lite Customization" : undoLabel);
            ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot(component, ToComponentSnapshot());
            adapter.SetDirty(component);
            return true;
        }

        private static bool SnapshotsEqual(
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot left,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot right)
        {
            return left.SlotCount == right.SlotCount
                && left.IconMode == right.IconMode
                && left.SelectedGearIndex == right.SelectedGearIndex
                && left.ActionIconMode == right.ActionIconMode
                && left.CustomSaveIcon == right.CustomSaveIcon
                && left.CustomLoadIcon == right.CustomLoadIcon
                && left.CustomClearIcon == right.CustomClearIcon
                && left.UseCustomSlotIcons == right.UseCustomSlotIcons
                && TextureArraysEqual(left.CustomIcons, right.CustomIcons)
                && left.UseCustomRootIcon == right.UseCustomRootIcon
                && left.CustomRootIcon == right.CustomRootIcon
                && left.UseCustomRootName == right.UseCustomRootName
                && string.Equals(left.CustomRootName, right.CustomRootName, StringComparison.Ordinal)
                && StringArraysEqual(left.CustomPresetNames, right.CustomPresetNames)
                && string.Equals(left.CustomPresetNameFormat, right.CustomPresetNameFormat, StringComparison.Ordinal)
                && string.Equals(left.CustomSaveLabel, right.CustomSaveLabel, StringComparison.Ordinal)
                && string.Equals(left.CustomLoadLabel, right.CustomLoadLabel, StringComparison.Ordinal)
                && string.Equals(left.CustomClearPresetLabel, right.CustomClearPresetLabel, StringComparison.Ordinal)
                && string.Equals(left.CustomConfirmLabel, right.CustomConfirmLabel, StringComparison.Ordinal)
                && left.UseCustomInstallPath == right.UseCustomInstallPath
                && string.Equals(left.CustomInstallPath, right.CustomInstallPath, StringComparison.Ordinal)
                && left.UseParameterExclusions == right.UseParameterExclusions
                && StringArraysEqual(left.ExcludedParameterNames, right.ExcludedParameterNames)
                && left.UseVendorizedGeneratedAssets == right.UseVendorizedGeneratedAssets
                && string.Equals(left.VendorizedGeneratedAssetsPath, right.VendorizedGeneratedAssetsPath, StringComparison.Ordinal);
        }

        private static Texture2D[] EnsureSizedTextureArray(Texture2D[] source, int size)
        {
            if (size <= 0)
                return Array.Empty<Texture2D>();

            var resized = new Texture2D[size];
            if (source != null)
                Array.Copy(source, resized, Mathf.Min(source.Length, size));
            return resized;
        }

        private static string[] EnsureSizedStringArray(string[] source, int size)
        {
            if (size <= 0)
                return Array.Empty<string>();

            var resized = new string[size];
            if (source != null)
                Array.Copy(source, resized, Mathf.Min(source.Length, size));

            for (int i = 0; i < resized.Length; i++)
                resized[i] ??= string.Empty;

            return resized;
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

        private static string NormalizeInstallPath(string value)
        {
            return NormalizeSlashPath(value);
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

        private static string[] NormalizePresetNamesBySlot(string[] source, int slotCount)
        {
            if (slotCount <= 0)
                return Array.Empty<string>();

            var normalized = new string[slotCount];
            for (int i = 0; i < normalized.Length; i++)
            {
                string candidate = source != null && i < source.Length ? source[i] : string.Empty;
                normalized[i] = NormalizeOptionalString(candidate);
            }

            return normalized;
        }

        private static string[] SanitizeExcludedParameterNames(string[] names)
        {
            if (names == null || names.Length == 0)
                return Array.Empty<string>();

            return names
                .Select(ASMLiteParameterBackupPresetResolver.NormalizeVisibleName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        private static bool TextureArraysEqual(Texture2D[] left, Texture2D[] right)
        {
            left = left ?? Array.Empty<Texture2D>();
            right = right ?? Array.Empty<Texture2D>();
            if (left.Length != right.Length)
                return false;

            for (int index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }

            return true;
        }

        private static bool StringArraysEqual(string[] left, string[] right)
        {
            left = left ?? Array.Empty<string>();
            right = right ?? Array.Empty<string>();
            if (left.Length != right.Length)
                return false;

            for (int index = 0; index < left.Length; index++)
            {
                if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
                    return false;
            }

            return true;
        }
    }
}
