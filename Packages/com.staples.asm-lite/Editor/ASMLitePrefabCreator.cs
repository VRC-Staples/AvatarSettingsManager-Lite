// Add ASM_LITE_VERBOSE to Edit > Project Settings > Player > Scripting Define Symbols
// to enable verbose build logging throughout this file.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLitePrefabCreator: editor utility that builds the ASM-Lite prefab.
    ///
    /// Run via: ASM-Lite ▶ Create Prefab
    ///
    /// Creates a GameObject with:
    ///   • ASMLiteComponent  (our runtime component)
    ///   • VRCFury (VF.Model.VRCFury) component whose 'content' field is set to a
    ///     VF.Model.Feature.FullController that references the stub assets in
    ///     GeneratedAssets/ and has Global Parameters set to '*'.
    ///
    /// Both VRCFury and FullController are 'internal' in the VRCFury package, so this
    /// script uses reflection to access them: this is intentional and is the
    /// standard pattern for third-party tooling that integrates with VRCFury.
    /// </summary>
    public static class ASMLitePrefabCreator
    {
        // Asset paths: see ASMLiteAssetPaths for centralized constants.

        // Unity fileID for the primary object in each asset type.
        private const long AnimatorControllerFileID = 9100000L;
        private const long MonoBehaviourFileID      = 11400000L;

        // ── Reflection cache (R025) ───────────────────────────────────────────
        private static readonly Dictionary<string, Type> s_typeCache = new Dictionary<string, Type>();
        private static bool      s_reflInitialized;

        /// <summary>
        /// Clears the reflection and type caches after a domain reload so stale
        /// Type objects from the old AppDomain are never reused.
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ClearCaches()
        {
            s_typeCache.Clear();
            s_reflInitialized     = false;
            s_fullControllerType  = null;
            s_controllerEntryType = null;
            s_menuEntryType       = null;
            s_paramsEntryType     = null;
            s_controllerField     = null;
            s_menuField           = null;
            s_paramsField         = null;
            s_contentField        = null;
        }
        private static Type      s_fullControllerType;
        private static Type      s_controllerEntryType;
        private static Type      s_menuEntryType;
        private static Type      s_paramsEntryType;
        private static FieldInfo s_controllerField;
        private static FieldInfo s_menuField;
        private static FieldInfo s_paramsField;
        private static FieldInfo s_contentField;

        // ── EnsureReflectionCache (R025, R024) ────────────────────────────────
        private static void EnsureReflectionCache()
        {
            if (s_reflInitialized) return;

            // Resolve all fields before committing the initialized flag.
            // If any lookup fails (e.g. VRCFury not yet loaded), leave the flag
            // false so the next call retries rather than permanently poisoning the cache.
            var fullControllerType = FindType("VF.Model.Feature.FullController");
            if (fullControllerType == null) return;

            var controllerEntryType = fullControllerType.GetNestedType(
                "ControllerEntry", BindingFlags.Public | BindingFlags.NonPublic);
            var menuEntryType       = fullControllerType.GetNestedType(
                "MenuEntry",        BindingFlags.Public | BindingFlags.NonPublic);
            var paramsEntryType     = fullControllerType.GetNestedType(
                "ParamsEntry",      BindingFlags.Public | BindingFlags.NonPublic);

            var controllerField = controllerEntryType?.GetField(
                "controller", BindingFlags.Public | BindingFlags.Instance);
            var menuField       = menuEntryType?.GetField(
                "menu",        BindingFlags.Public | BindingFlags.Instance);
            var paramsField     = paramsEntryType?.GetField(
                "parameters",  BindingFlags.Public | BindingFlags.Instance);

            Type vrcfuryType = FindType("VF.Model.VRCFury");
            var contentField   = vrcfuryType?.GetField(
                "content", BindingFlags.Public | BindingFlags.Instance);

            // Commit to the cache only after all lookups succeed.
            s_fullControllerType  = fullControllerType;
            s_controllerEntryType = controllerEntryType;
            s_menuEntryType       = menuEntryType;
            s_paramsEntryType     = paramsEntryType;
            s_controllerField     = controllerField;
            s_menuField           = menuField;
            s_paramsField         = paramsField;
            s_contentField        = contentField;
            s_reflInitialized     = true;
        }

        /// <summary>
        /// Builds (or rebuilds) the ASM-Lite prefab asset.
        /// Called by <see cref="ASMLiteWindow"/> when the user clicks "Add ASM-Lite Prefab".
        /// </summary>
        public static void CreatePrefab()
        {
            // ── Ensure Prefabs directory exists (HIGH-3) ──────────────────────
            if (!AssetDatabase.IsValidFolder("Packages/com.staples.asm-lite/Prefabs"))
                AssetDatabase.CreateFolder("Packages/com.staples.asm-lite", "Prefabs");

            // ── Prime reflection cache ────────────────────────────────────────
            EnsureReflectionCache();

            // ── Load stub assets ─────────────────────────────────────────────
            var fxController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ASMLiteAssetPaths.FXController);
            var menu         = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.Menu);
            var prms         = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.ExprParams);

            if (fxController == null)
                Debug.LogWarning($"[ASM-Lite] Stub FX controller not found at {ASMLiteAssetPaths.FXController}: FullController will have no controller reference.");
            if (menu == null)
                Debug.LogWarning($"[ASM-Lite] Stub menu not found at {ASMLiteAssetPaths.Menu}: FullController will have no menu reference.");
            if (prms == null)
                Debug.LogWarning($"[ASM-Lite] Stub params not found at {ASMLiteAssetPaths.ExprParams}: FullController will have no params reference.");

            // ── Locate VRCFury types via reflection ──────────────────────────
            Type vrcfuryType        = FindType("VF.Model.VRCFury");
            Type guidControllerType = FindType("VF.Model.GuidController");
            Type guidMenuType       = FindType("VF.Model.GuidMenu");
            Type guidParamsType     = FindType("VF.Model.GuidParams");

            if (vrcfuryType == null)
            {
                EditorUtility.DisplayDialog(
                    "ASM-Lite: VRCFury Not Found",
                    "VRCFury (VF.Model.VRCFury) could not be found in the loaded assemblies.\n\n" +
                    "Please ensure VRCFury is installed via the scoped registry in Packages/manifest.json " +
                    "and that Unity has finished importing it, then run this menu item again.",
                    "OK");
                return;
            }

            if (s_fullControllerType == null)
            {
                Debug.LogError("[ASM-Lite] VF.Model.Feature.FullController not found: VRCFury may be an unexpected version.");
                return;
            }

            // ── Build the GameObject ─────────────────────────────────────────
            var go = new GameObject("ASM-Lite");

            // Add ASMLiteComponent
            go.AddComponent<ASMLiteComponent>();

            // Add VRCFury component and guard against failure (HIGH-4)
            var vrcfuryComp = go.AddComponent(vrcfuryType);
            if (vrcfuryComp == null)
            {
                Debug.LogError("[ASM-Lite] Failed to add VRCFury component. Is VRCFury installed correctly?");
                UnityEngine.Object.DestroyImmediate(go);
                return;
            }

            // Set 'content' to a FullController
            object fullController = Activator.CreateInstance(s_fullControllerType);

            // Configure controllers list
            if (s_controllerEntryType != null && fxController != null)
            {
                object entry = Activator.CreateInstance(s_controllerEntryType);

                if (s_controllerField != null && guidControllerType != null)
                {
                    object guidCtrl = CreateGuidWrapper(guidControllerType, fxController,
                        ASMLiteAssetPaths.FXController, AnimatorControllerFileID);
                    if (guidCtrl != null)
                        s_controllerField.SetValue(entry, guidCtrl);
                }

                AppendToList(fullController, s_fullControllerType, "controllers", entry);
            }

            // Configure menus list
            if (s_menuEntryType != null && menu != null)
            {
                object entry = Activator.CreateInstance(s_menuEntryType);

                if (s_menuField != null && guidMenuType != null)
                {
                    object guidMenu = CreateGuidWrapper(guidMenuType, menu,
                        ASMLiteAssetPaths.Menu, MonoBehaviourFileID);
                    if (guidMenu != null)
                        s_menuField.SetValue(entry, guidMenu);
                }

                AppendToList(fullController, s_fullControllerType, "menus", entry);
            }

            // Configure prms list
            if (s_paramsEntryType != null && prms != null)
            {
                object entry = Activator.CreateInstance(s_paramsEntryType);

                if (s_paramsField != null && guidParamsType != null)
                {
                    object guidPrms = CreateGuidWrapper(guidParamsType, prms,
                        ASMLiteAssetPaths.ExprParams, MonoBehaviourFileID);
                    if (guidPrms != null)
                        s_paramsField.SetValue(entry, guidPrms);
                }

                AppendToList(fullController, s_fullControllerType, "prms", entry);
            }

            // Configure globalParams = ["*"]
            SetGlobalParams(fullController, s_fullControllerType, "*");

            // Assign content via cached field -- guard required: a null field means
            // VF.Model.VRCFury.content was not found during reflection init, which
            // produces a broken prefab with an empty VRCFury component. Abort and
            // report rather than silently emit a prefab that looks valid but fails at upload.
            if (s_contentField == null)
            {
                Debug.LogError("[ASM-Lite] 'content' field not found on VF.Model.VRCFury. " +
                    "Prefab NOT saved -- VRCFury version may be incompatible.");
                UnityEngine.Object.DestroyImmediate(go);
                return;
            }
            s_contentField.SetValue(vrcfuryComp, fullController);

#if ASM_LITE_VERBOSE
            Debug.Log("[ASM-Lite] FullController configured: controller=" +
                (fxController != null ? ASMLiteAssetPaths.FXController : "null") +
                ", menu=" + (menu != null ? ASMLiteAssetPaths.Menu : "null") +
                ", params=" + (prms != null ? ASMLiteAssetPaths.ExprParams : "null") +
                ", globalParams=[\"*\"]");
#endif

            // ── Save as prefab ───────────────────────────────────────────────
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

        // ── Reflection helpers ────────────────────────────────────────────────

        /// <summary>
        /// Searches all loaded assemblies for a type by its full name. Results are
        /// cached in <see cref="s_typeCache"/> to avoid repeated assembly scans (R025).
        /// </summary>
        private static Type FindType(string fullName)
        {
            if (s_typeCache.TryGetValue(fullName, out var cached))
                return cached;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName);
                    if (t != null)
                    {
                        s_typeCache[fullName] = t;
                        return t;
                    }
                }
                catch (ReflectionTypeLoadException rtle)
                {
                    // Partially-loadable assemblies are expected; log under verbose flag.
#if ASM_LITE_VERBOSE
                    Debug.LogWarning($"[ASM-Lite] Partial load of '{asm.GetName().Name}': " +
                        string.Join(", ", System.Array.ConvertAll(
                            rtle.LoaderExceptions, e => e?.Message ?? "null")));
#else
                    _ = rtle;
#endif
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ASM-Lite] Error scanning assembly {asm.GetName().Name}: {ex.Message}");
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a GuidWrapper instance (GuidController / GuidMenu / GuidParams)
        /// with both <c>objRef</c> and <c>id</c> set from the given asset. The GUID is
        /// resolved at runtime via <see cref="AssetDatabase.AssetPathToGUID"/> (R026).
        /// </summary>
        private static object CreateGuidWrapper(Type wrapperType, UnityEngine.Object asset,
            string assetPath, long fileID)
        {
            try
            {
                string guid = AssetDatabase.AssetPathToGUID(assetPath);
                if (string.IsNullOrEmpty(guid))
                {
                    Debug.LogError($"[ASM-Lite] AssetPathToGUID returned empty for '{assetPath}'. Asset may not be imported yet.");
                    return null;
                }

                object instance = Activator.CreateInstance(wrapperType);

                FieldInfo objRefField = GetBaseField(wrapperType, "objRef");
                FieldInfo idField     = GetBaseField(wrapperType, "id");

                // Both fields are required -- a GuidWrapper with either field missing
                // produces a broken asset reference that fails silently on domain reload.
                if (objRefField == null)
                {
                    Debug.LogError($"[ASM-Lite] 'objRef' field not found on {wrapperType.Name} hierarchy. VRCFury version may be incompatible.");
                    return null;
                }
                if (idField == null)
                {
                    Debug.LogError($"[ASM-Lite] 'id' field not found on {wrapperType.Name} hierarchy. VRCFury version may be incompatible.");
                    return null;
                }

                objRefField.SetValue(instance, asset);

                // 'id' format used by VRCFury: "<guid>:<fileID>"
                idField.SetValue(instance, $"{guid}:{fileID}");

                return instance;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ASM-Lite] Could not create {wrapperType.Name}: {ex.Message}");
                Debug.LogException(ex);
                return null;
            }
        }

        /// <summary>
        /// Walks the inheritance chain to find a field by name (since GuidWrapper fields
        /// are declared on the non-generic base class).
        /// </summary>
        private static FieldInfo GetBaseField(Type type, string fieldName)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            while (type != null)
            {
                var fi = type.GetField(fieldName, flags);
                if (fi != null) return fi;
                type = type.BaseType;
            }
            return null;
        }

        /// <summary>
        /// Appends an item to a <c>List&lt;T&gt;</c> field on the target object.
        /// </summary>
        private static void AppendToList(object target, Type targetType, string fieldName, object item)
        {
            FieldInfo field = targetType.GetField(fieldName,
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return;

            IList list = field.GetValue(target) as IList;
            if (list == null)
            {
                // Instantiate a new List<T> of the correct concrete type
                Type listType = typeof(List<>).MakeGenericType(item.GetType());
                list = (IList)Activator.CreateInstance(listType);
                field.SetValue(target, list);
            }
            list.Add(item);
        }

        /// <summary>
        /// Sets globalParams on the FullController to a single-entry list containing
        /// the given pattern (typically "*" to make all parameters global).
        /// </summary>
        private static void SetGlobalParams(object fullController, Type fullControllerType, string pattern)
        {
            FieldInfo field = fullControllerType.GetField("globalParams",
                BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return;

            var list = field.GetValue(fullController) as IList ?? new List<string>();
            list.Add(pattern);
            field.SetValue(fullController, list);
        }
    }
}
