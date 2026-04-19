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

            ConfigureVRCFuryFullController(go, component);

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

        internal static bool TryRefreshLiveFullControllerWiring(GameObject root, ASMLiteComponent component, string contextLabel)
        {
            if (root == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Cannot refresh live FullController wiring because the root object was null.");
                return false;
            }

            if (!ASMLiteBuilder.TryRepairPackageGeneratedFxControllerIfCorrupt(contextLabel + " Generated FX Repair"))
                return false;

            ConfigureVRCFuryFullController(root, component);

            var vfType = FindTypeByFullName("VF.Model.VRCFury");
            if (vfType == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: VF.Model.VRCFury type was not found while refreshing live FullController wiring.");
                return false;
            }

            var vfComponent = root.GetComponent(vfType) as MonoBehaviour;
            if (vfComponent == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: VF.Model.VRCFury component is missing after live FullController wiring refresh.");
                return false;
            }

            return true;
        }

        private static void ConfigureVRCFuryFullController(GameObject root, ASMLiteComponent component)
        {
            if (root == null)
                return;

            var vfType = FindTypeByFullName("VF.Model.VRCFury");
            var fullControllerType = FindTypeByFullName("VF.Model.Feature.FullController");

            if (vfType == null || fullControllerType == null)
            {
                Debug.LogWarning("[ASM-Lite] VRCFury FullController types were not found. Prefab created without deterministic FullController wiring.");
                return;
            }

            var vfComponent = root.GetComponent(vfType) as MonoBehaviour;
            if (vfComponent == null)
                vfComponent = root.AddComponent(vfType) as MonoBehaviour;

            if (vfComponent == null)
            {
                Debug.LogError("[ASM-Lite] Failed to create VRCFury component for ASM-Lite prefab wiring.");
                return;
            }

            if (!ASMLiteBuilder.TryRepairPackageGeneratedFxControllerIfCorrupt("Configure FullController Wiring"))
            {
                Debug.LogWarning("[ASM-Lite] Generated FX controller could not be repaired while configuring FullController wiring. Prefab wiring was skipped.");
                return;
            }

            var fxController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.FXController);
            var menu = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.Menu);
            var parameters = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.ExprParams);

            if (fxController == null || menu == null || parameters == null)
            {
                Debug.LogWarning("[ASM-Lite] Generated ASM-Lite assets were missing while wiring FullController. Rebuild ASM-Lite assets, then recreate the prefab.");
            }

            var so = new SerializedObject(vfComponent);
            so.Update();

            var content = so.FindProperty("content");
            if (content == null)
            {
                Debug.LogError("[ASM-Lite] VRCFury component did not expose expected 'content' field. FullController wiring was not applied.");
                return;
            }

            if (content.managedReferenceValue == null || content.managedReferenceValue.GetType() != fullControllerType)
                content.managedReferenceValue = Activator.CreateInstance(fullControllerType, true);

            bool ok = TryApplyFullControllerAssetReferences(so, component, fxController, menu, parameters);

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vfComponent);

#if ASM_LITE_VERBOSE
            if (ok)
                Debug.Log("[ASM-Lite] Applied deterministic VRCFury FullController wiring to ASM-Lite prefab root.");
#endif
            if (!ok)
                Debug.LogWarning("[ASM-Lite] FullController wiring completed with missing fields. Check VRCFury schema compatibility.");
        }

        internal static bool TryApplyFullControllerAssetReferences(
            SerializedObject serializedVfComponent,
            ASMLiteComponent component,
            UnityEngine.Object fxController,
            UnityEngine.Object menu,
            UnityEngine.Object parameters)
        {
            if (serializedVfComponent == null)
                return false;

            bool ok = true;

            ok &= EnsureArraySize(serializedVfComponent, "content.controllers", 1, required: true);
            ok &= SetObjectReference(serializedVfComponent, "content.controllers.Array.data[0].controller.objRef", fxController, required: true);
            ok &= SetInt(serializedVfComponent, "content.controllers.Array.data[0].type", 5, required: true);

            ok &= EnsureArraySize(serializedVfComponent, "content.menus", 1, required: true);
            ok &= SetObjectReference(serializedVfComponent, "content.menus.Array.data[0].menu.objRef", menu, required: true);
            ok &= ASMLiteFullControllerInstallPathHelper.TryApplyMenuPrefix(serializedVfComponent, component);

            // VRCFury consumes FullController parameter registrations from prms.
            // Keep this populated so merged menu controls (ASMLite_Ctrl triggers)
            // always resolve against merged parameters at build time.
            ok &= EnsureArraySize(serializedVfComponent, "content.prms", 1, required: true);
            ok &= SetAnyObjectReference(
                serializedVfComponent,
                new[]
                {
                    "content.prms.Array.data[0].parameters.objRef", // expected schema
                    "content.prms.Array.data[0].parameter.objRef",  // compatibility fallback
                    "content.prms.Array.data[0].objRef",            // compatibility fallback
                },
                parameters,
                required: true,
                fieldLabel: "content.prms[0].parameters.objRef");

            ok &= EnsureArraySize(serializedVfComponent, "content.smoothedPrms", 0, required: true);
            ok &= EnsureArraySize(serializedVfComponent, "content.globalParams", 1, required: true);
            ok &= SetString(serializedVfComponent, "content.globalParams.Array.data[0]", "*", required: true);
            // Newer VRCFury schemas expose this flag and need it so unsaved/non-synced
            // Clear Preset defaults remain globally addressable. Older reflected test
            // schemas may not have the field yet, so treat it as optional.
            ok &= SetBool(serializedVfComponent, "content.allNonsyncedAreGlobal", true, required: false);
            ok &= SetBool(serializedVfComponent, "content.ignoreSaved", false, required: false);

            ok &= SetObjectReference(serializedVfComponent, "content.controller.objRef", fxController, required: true);
            ok &= SetObjectReference(serializedVfComponent, "content.menu.objRef", menu, required: true);
            // Keep the top-level field in sync as a compatibility mirror.
            ok &= SetObjectReference(serializedVfComponent, "content.parameters.objRef", parameters, required: true);

            return ok;
        }

        private static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            Type firstMatch = null;
            Type nonTestMatch = null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var asm = assemblies[i];
                if (asm == null)
                    continue;

                var t = asm.GetType(fullName, throwOnError: false);
                if (t == null)
                    continue;

                firstMatch ??= t;

                string asmName = asm.GetName()?.Name ?? string.Empty;
                bool isTestAssembly = asmName.IndexOf("Test", StringComparison.OrdinalIgnoreCase) >= 0;

                if (!isTestAssembly)
                    nonTestMatch ??= t;

                if (string.Equals(asmName, "VRCFury", StringComparison.Ordinal)
                    || asmName.StartsWith("VRCFury.", StringComparison.Ordinal))
                {
                    return t;
                }
            }

            return nonTestMatch ?? firstMatch;
        }

        private static bool EnsureArraySize(SerializedObject so, string path, int size, bool required)
        {
            var prop = so.FindProperty(path);
            if (prop == null || !prop.isArray)
                return MissingField(path, required);

            prop.arraySize = size;
            return true;
        }

        private static bool SetObjectReference(SerializedObject so, string path, UnityEngine.Object value, bool required)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
                return MissingField(path, required);

            prop.objectReferenceValue = value;
            return true;
        }

        private static bool SetAnyObjectReference(SerializedObject so, string[] candidatePaths, UnityEngine.Object value, bool required, string fieldLabel)
        {
            if (candidatePaths == null || candidatePaths.Length == 0)
                return MissingField(fieldLabel, required);

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                var path = candidatePaths[i];
                if (string.IsNullOrEmpty(path))
                    continue;

                var prop = so.FindProperty(path);
                if (prop == null)
                    continue;

                prop.objectReferenceValue = value;
                return true;
            }

            return MissingField(fieldLabel, required);
        }

        private static bool SetString(SerializedObject so, string path, string value, bool required)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
                return MissingField(path, required);

            prop.stringValue = value ?? string.Empty;
            return true;
        }

        private static bool SetBool(SerializedObject so, string path, bool value, bool required)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
                return MissingField(path, required);

            prop.boolValue = value;
            return true;
        }

        private static bool SetInt(SerializedObject so, string path, int value, bool required)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
                return MissingField(path, required);

            prop.intValue = value;
            return true;
        }

        private static bool MissingField(string path, bool required)
        {
            if (required)
                Debug.LogError($"[ASM-Lite] Expected VRCFury FullController field was not found: '{path}'.");
            return !required;
        }
    }
}
