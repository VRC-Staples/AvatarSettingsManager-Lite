using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace ASMLite
{
    /// <summary>
    /// Controls which icon set is displayed in the ASM-Lite radial menu.
    /// SameColor  — all gear icons tinted to match the avatar's active colour.
    /// MultiColor — each gear slot uses its own distinct colour icon.
    /// Custom     — user-supplied Texture2D icons from the customIcons array.
    /// </summary>
    public enum IconMode
    {
        SameColor  = 0,
        MultiColor = 1,
        Custom     = 2,
    }

    /// <summary>
    /// Controls how ASM-Lite encodes slot control parameters.
    /// SafeBool   — 3 synced Bool parameters per slot (simplest, costs 3×slotCount bits).
    /// CompactInt — 1 shared synced Int for all slots (costs 8 bits regardless of slot count).
    /// </summary>
    public enum ControlScheme
    {
        SafeBool   = 0,
        CompactInt = 1,
    }

    /// <summary>
    /// ASM-Lite component. Add this to an avatar root (or any child GameObject) to
    /// enable the ASM-Lite slot system. Implements IEditorOnly so the VRChat SDK
    /// strips the component from the build, and IPreprocessCallbackBehaviour so the
    /// builder runs before VRCFury merges its generated assets.
    /// </summary>
    [AddComponentMenu("ASM-Lite/ASM-Lite Component")]
    [DisallowMultipleComponent]
    public class ASMLiteComponent : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        // ─── Public fields ────────────────────────────────────────────────────

        /// <summary>
        /// Number of avatar expression parameter slots that ASM-Lite manages.
        /// Informational only in S01; used by the builder in S02+.
        /// </summary>
        [SerializeField]
        public int slotCount = 3;

        /// <summary>
        /// Which icon set mode to use for the radial menu gear icons.
        /// </summary>
        [SerializeField]
        public IconMode iconMode = IconMode.SameColor;

        /// <summary>
        /// Index of the gear icon (0-7) used when iconMode is SameColor or MultiColor.
        /// Corresponds to the colour order in ASMLiteAssetPaths.GearIconPaths.
        /// </summary>
        [SerializeField]
        public int selectedGearIndex = 0;

        /// <summary>
        /// User-supplied icon textures used when iconMode is Custom.
        /// Should have one entry per expression parameter slot.
        /// </summary>
        [SerializeField]
        public Texture2D[] customIcons = new Texture2D[0];

        /// <summary>
        /// Which control parameter encoding scheme to use for synced slot operations.
        /// </summary>
        [SerializeField]
        public ControlScheme controlScheme = ControlScheme.SafeBool;

#if UNITY_EDITOR
        // ─── Reflection cache ─────────────────────────────────────────────────

        /// <summary>
        /// Cached reference to ASMLiteBuilder.Build to avoid repeated reflection
        /// lookups on every preprocess call.
        /// </summary>
        private static MethodInfo s_buildMethod;

        /// <summary>
        /// Resolves and caches a reference to the ASMLiteBuilder.Build static method.
        /// Returns null if the editor assembly is not loaded or the method is not found.
        /// </summary>
        private static MethodInfo GetBuildMethod()
        {
            if (s_buildMethod != null)
                return s_buildMethod;

            var type = System.Type.GetType("ASMLite.Editor.ASMLiteBuilder, ASMLite.Editor");
            if (type == null)
                return null;

            s_buildMethod = type.GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static);

            return s_buildMethod;
        }
#endif

        // ─── IPreprocessCallbackBehaviour ─────────────────────────────────────

        /// <summary>
        /// PreprocessOrder 0 ensures this runs before VRCFury (which uses order 0 as
        /// well but is registered later). Lower values run first.
        /// </summary>
        public int PreprocessOrder => -10;

        /// <summary>
        /// Called by the VRChat SDK before avatar upload. Delegates to ASMLiteBuilder
        /// to generate FX layers and populate expression parameters at build time.
        /// </summary>
        public bool OnPreprocess()
        {
#if UNITY_EDITOR
            // Call ASMLiteBuilder.Build via cached reflection to avoid a hard
            // compile-time dependency from the runtime assembly onto the editor assembly.
            var buildMethod = GetBuildMethod();
            if (buildMethod == null)
            {
                Debug.LogError("[ASM-Lite] ASMLiteBuilder.Build method not found. Is the Editor assembly loaded?");
                return false;
            }

            try
            {
                buildMethod.Invoke(null, new object[] { this });
            }
            catch (TargetInvocationException ex)
            {
                Debug.LogError("[ASM-Lite] Build failed.");
                Debug.LogException(ex.InnerException ?? ex);
                return false;
            }
#endif
            return true;
        }
    }
}
