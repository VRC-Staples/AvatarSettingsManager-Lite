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
        private const string FullControllerMenuPrefixPath = "content.menus.Array.data[0].prefix";

        /// <summary>
        /// Resolves the effective FullController install prefix from component state.
        /// Fail-closed behavior: any disabled/blank/null state resolves to empty prefix.
        /// </summary>
        internal static string ResolveEffectivePrefix(ASMLiteComponent component)
        {
            if (component == null)
                return string.Empty;

            if (!component.useCustomInstallPath)
                return string.Empty;

            var trimmed = string.IsNullOrWhiteSpace(component.customInstallPath)
                ? string.Empty
                : component.customInstallPath.Trim();

            return string.IsNullOrEmpty(trimmed) ? string.Empty : trimmed;
        }

        /// <summary>
        /// Applies the resolved prefix to FullController menu wiring on the serialized VRCFury component.
        /// Returns false when the expected schema is missing (fail-closed, no partial write).
        /// </summary>
        internal static bool TryApplyMenuPrefix(SerializedObject serializedVfComponent, ASMLiteComponent component)
        {
            if (serializedVfComponent == null)
            {
                Debug.LogError("[ASM-Lite] Cannot apply FullController menu prefix: serialized VRCFury component was null.");
                return false;
            }

            var prefixProperty = serializedVfComponent.FindProperty(FullControllerMenuPrefixPath);
            if (prefixProperty == null)
            {
                Debug.LogError($"[ASM-Lite] Expected VRCFury FullController menu prefix field was not found: '{FullControllerMenuPrefixPath}'.");
                return false;
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

            return true;
        }
    }
}
