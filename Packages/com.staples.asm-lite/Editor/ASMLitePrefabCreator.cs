using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASMLitePrefabCreator — editor utility that builds the ASM-Lite prefab.
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
    /// script uses reflection to access them — this is intentional and is the
    /// standard pattern for third-party tooling that integrates with VRCFury.
    /// </summary>
    public static class ASMLitePrefabCreator
    {
        // Asset paths — see ASMLiteAssetPaths for centralized constants.

        // Stable GUIDs from the .meta files written in T03.
        private const string ControllerGuid = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c301";
        private const string MenuGuid       = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c302";
        private const string ParamsGuid     = "a1b2c3d4e5f6a1b2c3d4e5f6a1b2c303";

        // Unity fileID for the primary object in each asset type.
        private const long AnimatorControllerFileID = 9100000L;
        private const long MonoBehaviourFileID      = 11400000L;

        /// <summary>
        /// Builds (or rebuilds) the ASM-Lite prefab asset.
        /// Called by <see cref="ASMLiteWindow"/> when the user clicks "Add ASM-Lite Prefab".
        /// </summary>
        public static void CreatePrefab()
        {
            // ── Ensure Prefabs directory exists ──────────────────────────────
            if (!System.IO.Directory.Exists("Packages/com.staples.asm-lite/Prefabs"))
                System.IO.Directory.CreateDirectory("Packages/com.staples.asm-lite/Prefabs");

            // ── Load stub assets ─────────────────────────────────────────────
            var fxController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ASMLiteAssetPaths.FXController);
            var menu         = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.Menu);
            var prms         = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.ExprParams);

            if (fxController == null)
                Debug.LogWarning($"[ASM-Lite] Stub FX controller not found at {ASMLiteAssetPaths.FXController} — FullController will have no controller reference.");
            if (menu == null)
                Debug.LogWarning($"[ASM-Lite] Stub menu not found at {ASMLiteAssetPaths.Menu} — FullController will have no menu reference.");
            if (prms == null)
                Debug.LogWarning($"[ASM-Lite] Stub params not found at {ASMLiteAssetPaths.ExprParams} — FullController will have no params reference.");

            // ── Locate VRCFury types via reflection ──────────────────────────
            Type vrcfuryType        = FindType("VF.Model.VRCFury");
            Type fullControllerType = FindType("VF.Model.Feature.FullController");
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

            if (fullControllerType == null)
            {
                Debug.LogError("[ASM-Lite] VF.Model.Feature.FullController not found — VRCFury may be an unexpected version.");
                return;
            }

            // ── Build the GameObject ─────────────────────────────────────────
            var go = new GameObject("ASM-Lite");

            // Add ASMLiteComponent
            go.AddComponent<ASMLiteComponent>();

            // Add VRCFury component and set its 'content' to a FullController
            var vrcfuryComp = go.AddComponent(vrcfuryType);

            if (fullControllerType != null)
            {
                object fullController = Activator.CreateInstance(fullControllerType);

                // Configure controllers list
                Type controllerEntryType = fullControllerType.GetNestedType("ControllerEntry");
                if (controllerEntryType != null && fxController != null)
                {
                    object entry = Activator.CreateInstance(controllerEntryType);
                    FieldInfo controllerField = controllerEntryType.GetField("controller",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (controllerField != null && guidControllerType != null)
                    {
                        object guidCtrl = CreateGuidWrapper(guidControllerType, fxController,
                            ControllerGuid, AnimatorControllerFileID);
                        if (guidCtrl != null)
                            controllerField.SetValue(entry, guidCtrl);
                    }

                    AppendToList(fullController, fullControllerType, "controllers", entry);
                }

                // Configure menus list
                Type menuEntryType = fullControllerType.GetNestedType("MenuEntry");
                if (menuEntryType != null && menu != null)
                {
                    object entry = Activator.CreateInstance(menuEntryType);
                    FieldInfo menuField = menuEntryType.GetField("menu",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (menuField != null && guidMenuType != null)
                    {
                        object guidMenu = CreateGuidWrapper(guidMenuType, menu,
                            MenuGuid, MonoBehaviourFileID);
                        if (guidMenu != null)
                            menuField.SetValue(entry, guidMenu);
                    }

                    AppendToList(fullController, fullControllerType, "menus", entry);
                }

                // Configure prms list
                Type paramsEntryType = fullControllerType.GetNestedType("ParamsEntry");
                if (paramsEntryType != null && prms != null)
                {
                    object entry = Activator.CreateInstance(paramsEntryType);
                    FieldInfo paramsField = paramsEntryType.GetField("parameters",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (paramsField != null && guidParamsType != null)
                    {
                        object guidPrms = CreateGuidWrapper(guidParamsType, prms,
                            ParamsGuid, MonoBehaviourFileID);
                        if (guidPrms != null)
                            paramsField.SetValue(entry, guidPrms);
                    }

                    AppendToList(fullController, fullControllerType, "prms", entry);
                }

                // Configure globalParams = ["*"]
                // This is List<string> — set '*' so all parameters pass through without VF## prefix.
                SetGlobalParams(fullController, fullControllerType, "*");

                // Assign content
                FieldInfo contentField = vrcfuryType.GetField("content",
                    BindingFlags.Public | BindingFlags.Instance);
                contentField?.SetValue(vrcfuryComp, fullController);

                Debug.Log("[ASM-Lite] FullController configured: controller=" +
                    (fxController != null ? ASMLiteAssetPaths.FXController : "null") +
                    ", menu=" + (menu != null ? ASMLiteAssetPaths.Menu : "null") +
                    ", params=" + (prms != null ? ASMLiteAssetPaths.ExprParams : "null") +
                    ", globalParams=[\"*\"]");
            }

            // ── Save as prefab ───────────────────────────────────────────────
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, ASMLiteAssetPaths.Prefab);
            UnityEngine.Object.DestroyImmediate(go);

            if (prefab != null)
            {
                AssetDatabase.Refresh();
                Debug.Log($"[ASM-Lite] Prefab created at {ASMLiteAssetPaths.Prefab}");
            }
            else
            {
                Debug.LogError($"[ASM-Lite] Failed to save prefab at {ASMLiteAssetPaths.Prefab}");
            }
        }

        // ── Reflection helpers ────────────────────────────────────────────────

        /// <summary>
        /// Searches all loaded assemblies for a type by its full name.
        /// </summary>
        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName);
                    if (t != null) return t;
                }
                catch { /* skip assemblies that throw on introspection */ }
            }
            return null;
        }

        /// <summary>
        /// Creates a GuidWrapper instance (GuidController / GuidMenu / GuidParams)
        /// with both <c>objRef</c> and <c>id</c> set from the given asset.
        /// </summary>
        private static object CreateGuidWrapper(Type wrapperType, UnityEngine.Object asset,
            string guid, long fileID)
        {
            try
            {
                object instance = Activator.CreateInstance(wrapperType);

                FieldInfo objRefField = GetBaseField(wrapperType, "objRef");
                FieldInfo idField     = GetBaseField(wrapperType, "id");

                objRefField?.SetValue(instance, asset);

                // 'id' format used by VRCFury: "<guid>:<fileID>"
                idField?.SetValue(instance, $"{guid}:{fileID}");

                return instance;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Could not create {wrapperType.Name}: {ex.Message}");
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
