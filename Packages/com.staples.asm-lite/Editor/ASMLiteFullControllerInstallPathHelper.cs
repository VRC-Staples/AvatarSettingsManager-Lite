using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// Shared resolver/writer for ASM-Lite FullController menu install prefix wiring.
    /// Keeps prefab-time and rebuild-time paths aligned on one normalization rule.
    /// </summary>
    internal static class ASMLiteFullControllerInstallPathHelper
    {
        private const string FullControllerMenuPrefixPath = ASMLiteDriftProbe.MenuPrefixPath;

        /// <summary>
        /// Resolves the effective FullController install prefix from component state.
        /// Fail-closed behavior: any disabled/blank/null state resolves to empty prefix.
        /// </summary>
        internal static string ResolveEffectivePrefix(ASMLiteComponent component)
        {
            if (component == null)
                return string.Empty;

            return ResolveEffectivePrefix(component.useCustomInstallPath, component.customInstallPath);
        }

        internal static string ResolveEffectivePrefix(bool useCustomInstallPath, string customInstallPath)
        {
            if (!useCustomInstallPath)
                return string.Empty;

            var trimmed = string.IsNullOrWhiteSpace(customInstallPath)
                ? string.Empty
                : customInstallPath.Trim();

            return string.IsNullOrEmpty(trimmed) ? string.Empty : trimmed;
        }

        /// <summary>
        /// Applies the resolved prefix to FullController menu wiring on the serialized VRCFury component.
        /// Returns false when the expected schema is missing (fail-closed, no partial write).
        /// </summary>
        internal static bool TryApplyMenuPrefix(SerializedObject serializedVfComponent, ASMLiteComponent component)
        {
            var result = TryApplyMenuPrefixWithDiagnostics(serializedVfComponent, component);
            if (!result.Success)
                Debug.LogError(result.ToLogString());

            return result.Success;
        }

        internal static ASMLiteBuildDiagnosticResult TryApplyMenuPrefixWithDiagnostics(SerializedObject serializedVfComponent, ASMLiteComponent component)
        {
            var probeResult = ASMLiteDriftProbe.ValidateInstallPrefixWritePath(serializedVfComponent);
            if (!probeResult.Success)
                return probeResult.ToDiagnosticResult();

            var prefixProperty = serializedVfComponent.FindProperty(FullControllerMenuPrefixPath);
            if (prefixProperty == null)
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath,
                    contextPath: FullControllerMenuPrefixPath,
                    remediation: "Update VRCFury FullController schema mapping so menu prefix write path remains available.");
            }

            var effectivePrefix = ResolveEffectivePrefix(component);
            prefixProperty.stringValue = effectivePrefix;

            if (string.IsNullOrEmpty(effectivePrefix))
            {
                Debug.Log("[ASM-Lite] FullController menu prefix resolved to empty (custom install path disabled or blank).");
            }
            else
            {
                Debug.Log($"[ASM-Lite] FullController menu prefix resolved to '{effectivePrefix}'.");
            }

            return ASMLiteBuildDiagnosticResult.Pass();
        }
    }
}
