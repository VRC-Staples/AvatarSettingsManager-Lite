using System;

namespace ASMLite.Editor
{
    internal readonly struct ASMLiteMigrationOutcomeReport
    {
        internal ASMLiteMigrationOutcomeReport(
            int mappedLegacyCount,
            int mirroredLegacyCount,
            int unmatchedLegacyCount,
            int malformedLegacyCount,
            int staleVrcFuryRemoved,
            int cleanedFxLayers,
            int cleanedFxParams,
            int cleanedExprParams,
            int cleanedMenuControls,
            bool installPathAdopted,
            string adoptedInstallPrefix,
            int removedMoveMenuHelpers)
        {
            MappedLegacyCount = mappedLegacyCount;
            MirroredLegacyCount = mirroredLegacyCount;
            UnmatchedLegacyCount = unmatchedLegacyCount;
            MalformedLegacyCount = malformedLegacyCount;
            StaleVrcFuryRemoved = staleVrcFuryRemoved;
            CleanedFxLayers = cleanedFxLayers;
            CleanedFxParams = cleanedFxParams;
            CleanedExprParams = cleanedExprParams;
            CleanedMenuControls = cleanedMenuControls;
            InstallPathAdopted = installPathAdopted;
            AdoptedInstallPrefix = adoptedInstallPrefix ?? string.Empty;
            RemovedMoveMenuHelpers = removedMoveMenuHelpers;
        }

        internal int MappedLegacyCount { get; }
        internal int MirroredLegacyCount { get; }
        internal int UnmatchedLegacyCount { get; }
        internal int MalformedLegacyCount { get; }
        internal int StaleVrcFuryRemoved { get; }
        internal int CleanedFxLayers { get; }
        internal int CleanedFxParams { get; }
        internal int CleanedExprParams { get; }
        internal int CleanedMenuControls { get; }
        internal bool InstallPathAdopted { get; }
        internal string AdoptedInstallPrefix { get; }
        internal int RemovedMoveMenuHelpers { get; }

        internal int TotalCleanedArtifacts => CleanedFxLayers + CleanedFxParams + CleanedExprParams + CleanedMenuControls;

        internal bool HasNonCriticalSignals =>
            UnmatchedLegacyCount > 0
            || MalformedLegacyCount > 0;

        internal string ToCompactSummary()
        {
            string adoptedPrefix = string.IsNullOrEmpty(AdoptedInstallPrefix) ? "<root>" : AdoptedInstallPrefix;
            return string.Format(
                "preserved legacy state: mapped={0}, mirrored={1}; unmatched legacy preserved={2}; malformed excluded={3}; cleaned ASM-Lite-owned state: staleVrcFury={4}, fxLayers={5}, fxParams={6}, exprParams={7}, menuControls={8}; install-path adoption: adopted={9}, prefix={10}, removedMoveHelpers={11}",
                MappedLegacyCount,
                MirroredLegacyCount,
                UnmatchedLegacyCount,
                MalformedLegacyCount,
                StaleVrcFuryRemoved,
                CleanedFxLayers,
                CleanedFxParams,
                CleanedExprParams,
                CleanedMenuControls,
                InstallPathAdopted,
                adoptedPrefix,
                RemovedMoveMenuHelpers);
        }
    }
}
