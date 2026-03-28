using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using System.Reflection;
#endif

namespace ASMLite
{
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
