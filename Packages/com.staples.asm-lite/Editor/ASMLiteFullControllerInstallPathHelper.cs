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
        internal const string DefaultInstallPrefix = "";
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
