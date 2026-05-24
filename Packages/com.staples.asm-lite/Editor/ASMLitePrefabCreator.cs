using System;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLitePrefabCreator: editor utility that builds the ASM-Lite prefab.
    ///
    /// Run via: ASM-Lite editor window > Add ASM-Lite Prefab
    ///
    /// Creates a GameObject with ASMLiteComponent plus deterministic VRCFury
    /// FullController wiring to generated ASM-Lite assets.
    /// </summary>
    public static class ASMLitePrefabCreator
    {
        /// <summary>
        /// Detects stale pre-1.0.5 prefab state where a legacy "prms" child/object path
        /// can cause double parameter registration during bake.
        /// </summary>
        public static bool HasStalePrmsEntry(GameObject root)
        {
            if (root == null)
                return false;

            var all = root.GetComponentsInChildren<Transform>(true);
            foreach (var t in all)
            {
                if (t == null)
                    continue;

                var n = t.name;
                if (string.Equals(n, "prms", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(n, "ASMLite_prms", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds (or rebuilds) the ASM-Lite prefab asset.
        /// Called by <see cref="ASMLiteWindow"/> when the user clicks "Add ASM-Lite Prefab".
        /// </summary>
        public static void CreatePrefab()
        {
            // Ensure Prefabs directory exists
            if (!AssetDatabase.IsValidFolder("Packages/com.staples.asm-lite/Prefabs"))
                AssetDatabase.CreateFolder("Packages/com.staples.asm-lite", "Prefabs");

            var go = new GameObject("ASM-Lite");
            var component = go.AddComponent<ASMLiteComponent>();

            ASMLiteFullControllerWiring.ConfigurePrefabRoot(go, component);

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ASMLiteAssetPaths.Prefab);
            UnityEngine.Object.DestroyImmediate(go);

            if (prefab != null)
            {
                AssetDatabase.Refresh();
#if ASM_LITE_VERBOSE
                Debug.Log($"[ASM-Lite] Prefab created at {ASMLiteAssetPaths.Prefab}");
#endif
            }
            else
            {
                Debug.LogError($"[ASM-Lite] Failed to save prefab at {ASMLiteAssetPaths.Prefab}");
            }
        }

    }
}
