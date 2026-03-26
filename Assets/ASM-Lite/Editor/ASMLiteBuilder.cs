using UnityEditor;
using UnityEngine;
using ASMLite;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLiteBuilder — static editor utility for build-time asset generation.
    ///
    /// S01 STUB: All methods are no-ops. S02 will fill in the real logic:
    ///   - GenerateExpressionAssets: writes slot-specific FX layers, menu entries,
    ///     and parameter entries into the managed GeneratedAssets/ files.
    ///   - CleanupGeneratedAssets: removes assets created by a previous build pass.
    ///
    /// This class is invoked by <see cref="ASMLiteComponent.Preprocess"/> during the
    /// VRChat SDK avatar build pipeline. It must NOT implement any VRC SDK interfaces
    /// directly — it is a pure utility class called from the runtime component.
    /// </summary>
    public static class ASMLiteBuilder
    {
        // ─── Public API (called from ASMLiteComponent.Preprocess) ─────────────

        /// <summary>
        /// Entry point called during avatar build preprocessing.
        /// S01 stub — no assets are generated yet.
        /// </summary>
        /// <param name="component">The ASMLiteComponent initiating the build.</param>
        public static void Build(ASMLiteComponent component)
        {
            // S01 stub: log only.
            Debug.Log($"[ASM-Lite] ASMLiteBuilder.Build() called for '{component.gameObject.name}' — stub, no assets generated.");
        }

        /// <summary>
        /// Removes any previously generated assets from the managed output directory.
        /// S01 stub — no-op.
        /// </summary>
        /// <param name="component">The ASMLiteComponent that owns the generated assets.</param>
        public static void CleanupGeneratedAssets(ASMLiteComponent component)
        {
            // S01 stub: no-op.
            Debug.Log($"[ASM-Lite] ASMLiteBuilder.CleanupGeneratedAssets() called — stub, nothing to clean.");
        }

        /// <summary>
        /// Validates that the component's configuration is compatible with the
        /// current avatar and returns a human-readable error message, or null if valid.
        /// S01 stub — always returns null (valid).
        /// </summary>
        /// <param name="component">The component to validate.</param>
        /// <returns>Error message string, or null if configuration is valid.</returns>
        public static string Validate(ASMLiteComponent component)
        {
            // S01 stub: no validation logic yet.
            return null;
        }
    }
}
