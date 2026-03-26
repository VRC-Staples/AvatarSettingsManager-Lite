using UnityEngine;
using VRC.SDKBase;

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

        // ─── IPreprocessCallbackBehaviour ─────────────────────────────────────

        /// <summary>
        /// PreprocessOrder 0 ensures this runs before VRCFury (which uses order 0 as
        /// well but is registered later). Lower values run first.
        /// </summary>
        public int PreprocessOrder => 0;

        /// <summary>
        /// Called by the VRChat SDK before avatar upload. S01 stub — S02 fills in
        /// the real asset-generation logic via ASMLiteBuilder.
        /// </summary>
        public void Preprocess(VRC.SDKBase.VRC_AvatarDescriptor avatarDescriptor)
        {
            // S01 stub: no-op. S02 replaces this with real logic.
            Debug.Log($"[ASM-Lite] Preprocess called on {gameObject.name} (slot count: {slotCount}) — stub, no action taken.");
        }
    }
}
