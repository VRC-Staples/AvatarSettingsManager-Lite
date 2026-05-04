using System;
using System.Collections.Generic;
using System.Linq;

namespace ASMLite.Editor
{
    internal static class ASMLiteParameterBackupPresetResolver
    {
        internal const string NoneExcludedPresetId = "none-excluded";
        internal const string SingleVisiblePresetId = "single-visible";
        internal const string FirstTwoVisiblePresetId = "first-two-visible";

        private static readonly string[] s_stablePresetIds =
        {
            FirstTwoVisiblePresetId,
            NoneExcludedPresetId,
            SingleVisiblePresetId,
        };

        internal static string[] StablePresetIds => s_stablePresetIds
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        internal static bool TryResolvePresetExcludedNames(
            string presetId,
            IEnumerable<string> visibleParameterOptions,
            out string[] excludedParameterNames,
            out string errorMessage)
        {
            excludedParameterNames = Array.Empty<string>();
            errorMessage = string.Empty;

            string normalizedPresetId = string.IsNullOrWhiteSpace(presetId) ? string.Empty : presetId.Trim();
            if (string.IsNullOrEmpty(normalizedPresetId))
            {
                errorMessage = "Parameter backup preset ID is required.";
                return false;
            }

            switch (normalizedPresetId)
            {
                case NoneExcludedPresetId:
                    excludedParameterNames = Array.Empty<string>();
                    return true;
                case SingleVisiblePresetId:
                    return TryResolveFirstVisibleOptions(
                        visibleParameterOptions,
                        1,
                        out excludedParameterNames,
                        out errorMessage,
                        $"parameter backup preset ID '{normalizedPresetId}'");
                case FirstTwoVisiblePresetId:
                    return TryResolveFirstVisibleOptions(
                        visibleParameterOptions,
                        2,
                        out excludedParameterNames,
                        out errorMessage,
                        $"parameter backup preset ID '{normalizedPresetId}'");
                default:
                    errorMessage = $"Unknown parameter backup preset ID '{normalizedPresetId}'. Known preset IDs: {string.Join(", ", StablePresetIds)}.";
                    return false;
            }
        }

        internal static string[] ResolvePresetExcludedNames(
            string presetId,
            IEnumerable<string> visibleParameterOptions)
        {
            if (TryResolvePresetExcludedNames(presetId, visibleParameterOptions, out var excludedParameterNames, out var errorMessage))
                return excludedParameterNames;

            throw new InvalidOperationException(errorMessage);
        }

        internal static bool TryResolveExactExcludedNames(
            IEnumerable<string> exactVisibleNames,
            IEnumerable<string> visibleParameterOptions,
            out string[] excludedParameterNames,
            out string errorMessage,
            string sourceLabel = "exact parameter backup exclusion names")
        {
            excludedParameterNames = Array.Empty<string>();
            errorMessage = string.Empty;

            var visibleByNormalizedName = BuildVisibleNameLookup(visibleParameterOptions, out errorMessage);
            if (!string.IsNullOrEmpty(errorMessage))
                return false;

            var requestedNames = NormalizeVisibleNames(exactVisibleNames);
            if (requestedNames.Length == 0)
                return true;

            var missing = requestedNames
                .Where(name => !visibleByNormalizedName.ContainsKey(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (missing.Length > 0)
            {
                errorMessage = $"Unable to resolve {sourceLabel}; missing visible parameter backup option(s): {string.Join(", ", missing)}.";
                return false;
            }

            excludedParameterNames = requestedNames
                .Select(name => visibleByNormalizedName[name])
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        internal static string[] ResolveExactExcludedNames(
            IEnumerable<string> exactVisibleNames,
            IEnumerable<string> visibleParameterOptions)
        {
            if (TryResolveExactExcludedNames(exactVisibleNames, visibleParameterOptions, out var excludedParameterNames, out var errorMessage))
                return excludedParameterNames;

            throw new InvalidOperationException(errorMessage);
        }

        private static bool TryResolveFirstVisibleOptions(
            IEnumerable<string> visibleParameterOptions,
            int requiredCount,
            out string[] excludedParameterNames,
            out string errorMessage,
            string sourceLabel)
        {
            excludedParameterNames = Array.Empty<string>();
            errorMessage = string.Empty;

            string[] visibleNames = NormalizeVisibleNames(visibleParameterOptions);
            if (visibleNames.Length < requiredCount)
            {
                errorMessage = $"Unable to resolve {sourceLabel}; requires at least {requiredCount} visible parameter backup option(s), but found {visibleNames.Length}.";
                return false;
            }

            excludedParameterNames = visibleNames
                .Take(requiredCount)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            return true;
        }

        internal static string[] NormalizeVisibleNames(IEnumerable<string> names)
        {
            if (names == null)
                return Array.Empty<string>();

            return names
                .Select(NormalizeVisibleName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
        }

        internal static string NormalizeVisibleName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            string normalized = name.Trim().Replace('\\', '/');
            while (normalized.Contains("//"))
                normalized = normalized.Replace("//", "/");

            var segments = normalized
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim())
                .Where(segment => segment.Length > 0)
                .ToArray();

            return segments.Length == 0
                ? string.Empty
                : string.Join("/", segments);
        }

        private static Dictionary<string, string> BuildVisibleNameLookup(
            IEnumerable<string> visibleParameterOptions,
            out string errorMessage)
        {
            errorMessage = string.Empty;
            var lookup = new Dictionary<string, string>(StringComparer.Ordinal);
            if (visibleParameterOptions == null)
                return lookup;

            foreach (var visibleOption in visibleParameterOptions)
            {
                string normalized = NormalizeVisibleName(visibleOption);
                if (string.IsNullOrEmpty(normalized))
                    continue;

                if (lookup.TryGetValue(normalized, out var existing))
                {
                    string normalizedExisting = NormalizeVisibleName(existing);
                    if (!string.Equals(normalizedExisting, normalized, StringComparison.Ordinal))
                    {
                        errorMessage = $"Visible parameter backup options contain ambiguous normalized name '{normalized}'.";
                        return lookup;
                    }

                    continue;
                }

                lookup.Add(normalized, normalized);
            }

            return lookup;
        }
    }
}
