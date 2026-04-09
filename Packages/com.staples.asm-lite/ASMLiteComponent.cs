using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace ASMLite
{
    /// <summary>
    /// Controls which icon set is displayed in the ASM-Lite radial menu.
    /// SameColor : all gear icons tinted to match the avatar's active colour.
    /// MultiColor: each gear slot uses its own distinct colour icon.
    /// Custom    : user-supplied Texture2D icons from the customIcons array.
    /// </summary>
    public enum IconMode
    {
        MultiColor = 0,
        SameColor  = 1,
        Custom     = 2,
    }

    /// <summary>
    /// Controls which icons are used for the Save, Load, and Clear Preset action buttons
    /// inside each slot submenu.
    /// Default: use the bundled Save.png, Load.png, Reset.png icons.
    /// Custom : user-supplied Texture2D icons for each action type.
    /// </summary>
    public enum ActionIconMode
    {
        Default = 0,
        Custom  = 1,
    }

    /// <summary>
    /// ASM-Lite component. Add this to an avatar root (or any child GameObject) to
    /// enable the ASM-Lite slot system. Implements IEditorOnly so the VRChat SDK
    /// strips the component from the build, and IPreprocessCallbackBehaviour so the
    /// VRChat SDK invokes the builder during the avatar build pipeline.
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
        public IconMode iconMode = IconMode.MultiColor;

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
        /// Which icon set to use for the Save, Load, and Clear Preset action buttons.
        /// Default uses the bundled icons; Custom allows user-supplied textures.
        /// </summary>
        [SerializeField]
        public ActionIconMode actionIconMode = ActionIconMode.Default;

        /// <summary>
        /// User-supplied icon for the Save action button. Used when actionIconMode is Custom.
        /// </summary>
        [SerializeField]
        public Texture2D customSaveIcon;

        /// <summary>
        /// User-supplied icon for the Load action button. Used when actionIconMode is Custom.
        /// </summary>
        [SerializeField]
        public Texture2D customLoadIcon;

        /// <summary>
        /// User-supplied icon for the Clear Preset action button. Used when actionIconMode is Custom.
        /// </summary>
        [SerializeField]
        public Texture2D customClearIcon;

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
        /// PreprocessOrder controls ordering relative to other IPreprocessCallbackBehaviour
        /// implementors on the avatar. The entire IPreprocessCallbackBehaviour phase runs
        /// via PreprocessCallbackBehaviours (callbackOrder=-2048), which is after VRCFury's
        /// main build (VrcfAvatarPreprocessor, callbackOrder=int.MinValue). Lower values
        /// run earlier within the IPreprocessCallbackBehaviour phase.
        ///
        /// ASM-Lite uses this callback to regenerate its stub assets so the prefab's
        /// VRCFury FullController payload can consume those assets as the delivery path.
        /// </summary>
        public int PreprocessOrder => -10;

        /// <summary>
        /// Called by the VRChat SDK before avatar upload. Delegates to ASMLiteBuilder
        /// to regenerate generated assets used by ASM-Lite's VRCFury FullController
        /// delivery wiring.
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
                // Emit a single linked console entry so stack trace and message
                // appear together and double-click-to-source works correctly.
                Debug.LogException(ex.InnerException ?? ex);
                return false;
            }
#endif
            return true;
        }
    }
}
