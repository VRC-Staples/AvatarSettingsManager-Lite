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
            var result = TryRefreshLiveFullControllerWiringWithDiagnostics(root, component, contextLabel);
            if (!result.Success)
                Debug.LogError(result.ToLogString());

            return result.Success;
        }

        internal static ASMLiteBuildDiagnosticResult TryRefreshLiveFullControllerWiringWithDiagnostics(GameObject root, ASMLiteComponent component, string contextLabel)
        {
            if (root == null)
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed,
                    contextPath: "root",
                    remediation: "Pass a valid ASM-Lite root GameObject before refreshing FullController wiring.",
                    message: $"[ASM-Lite] {contextLabel}: Cannot refresh live FullController wiring because the root object was null.");
            }

            if (!ASMLiteBuilder.TryRepairPackageGeneratedFxControllerIfCorrupt(contextLabel + " Generated FX Repair"))
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed,
                    contextPath: ASMLiteAssetPaths.FXController,
                    remediation: "Repair the generated FX controller before refreshing FullController wiring.",
                    message: $"[ASM-Lite] {contextLabel}: Generated FX controller repair failed before live FullController refresh.");
            }

            ConfigureVRCFuryFullController(root, component);

            var vfType = FindTypeByFullName("VF.Model.VRCFury");
            if (vfType == null)
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed,
                    contextPath: "VF.Model.VRCFury",
                    remediation: "Ensure VRCFury assemblies are available before refreshing FullController wiring.",
                    message: $"[ASM-Lite] {contextLabel}: VF.Model.VRCFury type was not found while refreshing live FullController wiring.");
            }

            var vfComponent = root.GetComponent(vfType) as MonoBehaviour;
            if (vfComponent == null)
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed,
                    contextPath: "VF.Model.VRCFury component",
                    remediation: "Ensure the live VRCFury component exists after FullController wiring refresh.",
                    message: $"[ASM-Lite] {contextLabel}: VF.Model.VRCFury component is missing after live FullController wiring refresh.");
            }

            return ASMLiteBuildDiagnosticResult.Pass();
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

            var wiringResult = TryApplyFullControllerAssetReferencesWithDiagnostics(so, component, fxController, menu, parameters);
            bool ok = wiringResult.Success;

            if (ok)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(vfComponent);
            }

#if ASM_LITE_VERBOSE
            if (ok)
                Debug.Log("[ASM-Lite] Applied deterministic VRCFury FullController wiring to ASM-Lite prefab root.");
#endif
            if (!ok)
                Debug.LogWarning(wiringResult.ToLogString());
        }

        internal static bool TryApplyFullControllerAssetReferences(
            SerializedObject serializedVfComponent,
            ASMLiteComponent component,
            UnityEngine.Object fxController,
            UnityEngine.Object menu,
            UnityEngine.Object parameters)
        {
            var result = TryApplyFullControllerAssetReferencesWithDiagnostics(
                serializedVfComponent,
                component,
                fxController,
                menu,
                parameters);

            if (!result.Success)
                Debug.LogError(result.ToLogString());

            return result.Success;
        }

        internal static ASMLiteBuildDiagnosticResult TryApplyFullControllerAssetReferencesWithDiagnostics(
            SerializedObject serializedVfComponent,
            ASMLiteComponent component,
            UnityEngine.Object fxController,
            UnityEngine.Object menu,
            UnityEngine.Object parameters)
        {
            var probeResult = ASMLiteDriftProbe.ValidateCriticalFullControllerWritePaths(serializedVfComponent);
            if (!probeResult.Success)
                return probeResult.ToDiagnosticResult();

            if (!EnsureArraySizeStrict(serializedVfComponent, ASMLiteDriftProbe.ControllersArrayPath, 1))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.ControllersArrayPath);

            if (!SetObjectReferenceStrict(serializedVfComponent, ASMLiteDriftProbe.ControllerObjectRefPath, fxController))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.ControllerObjectRefPath);

            if (!SetIntStrict(serializedVfComponent, ASMLiteDriftProbe.ControllerTypePath, 5))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.ControllerTypePath);

            if (!EnsureArraySizeStrict(serializedVfComponent, ASMLiteDriftProbe.MenuArrayPath, 1))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.MenuArrayPath);

            if (!SetObjectReferenceStrict(serializedVfComponent, ASMLiteDriftProbe.MenuObjectRefPath, menu))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.MenuObjectRefPath);

            var prefixResult = ASMLiteFullControllerInstallPathHelper.TryApplyMenuPrefixWithDiagnostics(serializedVfComponent, component);
            if (!prefixResult.Success)
                return prefixResult;

            // VRCFury consumes FullController parameter registrations from prms.
            // Keep this populated so merged menu controls (ASMLite_Ctrl triggers)
            // always resolve against merged parameters at build time.
            if (!EnsureArraySizeStrict(serializedVfComponent, ASMLiteDriftProbe.ParametersArrayPath, 1))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.ParametersArrayPath);

            if (!SetAnyObjectReferenceStrict(
                    serializedVfComponent,
                    new[]
                    {
                        ASMLiteDriftProbe.ParametersObjectRefPath,
                        ASMLiteDriftProbe.ParameterObjectRefPath,
                        ASMLiteDriftProbe.ParameterLegacyObjectRefPath,
                    },
                    parameters))
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Drift.MissingParameterFallbackGroup,
                    contextPath: ASMLiteDriftProbe.ParameterFallbackGroupKey,
                    remediation: "Expose at least one parameter reference path in FullController: parameters.objRef, parameter.objRef, or objRef.");
            }

            if (!SetObjectReferenceStrict(serializedVfComponent, ASMLiteDriftProbe.ControllerMirrorPath, fxController))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.ControllerMirrorPath);

            if (!SetObjectReferenceStrict(serializedVfComponent, ASMLiteDriftProbe.MenuMirrorPath, menu))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.MenuMirrorPath);

            if (!SetObjectReferenceStrict(serializedVfComponent, ASMLiteDriftProbe.ParametersMirrorPath, parameters))
                return CreateCriticalPathDiagnostic(ASMLiteDriftProbe.ParametersMirrorPath);

            bool ok = true;

            ok &= EnsureArraySize(serializedVfComponent, "content.smoothedPrms", 0, required: true);
            ok &= EnsureArraySize(serializedVfComponent, "content.globalParams", 1, required: true);
            ok &= SetString(serializedVfComponent, "content.globalParams.Array.data[0]", "*", required: true);
            // Newer VRCFury schemas expose this flag and need it so unsaved/non-synced
            // Clear Preset defaults remain globally addressable. Older reflected test
            // schemas may not have the field yet, so treat it as optional.
            ok &= SetBool(serializedVfComponent, "content.allNonsyncedAreGlobal", true, required: false);
            ok &= SetBool(serializedVfComponent, "content.ignoreSaved", false, required: false);

            if (!ok)
            {
                return ASMLiteBuildDiagnosticResult.Fail(
                    code: ASMLiteDiagnosticCodes.Build.FullControllerWiringFailed,
                    contextPath: "content.globalParams",
                    remediation: "Update ASM-Lite FullController non-critical mapping for this VRCFury schema.",
                    message: "[ASM-Lite] FullController wiring completed with missing non-critical fields. Check VRCFury schema compatibility.");
            }

            return ASMLiteBuildDiagnosticResult.Pass();
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

        private static ASMLiteBuildDiagnosticResult CreateCriticalPathDiagnostic(string path)
        {
            string code = string.Equals(path, ASMLiteDriftProbe.MenuPrefixPath, StringComparison.Ordinal)
                ? ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath
                : ASMLiteDiagnosticCodes.Drift.MissingRequiredPath;

            return ASMLiteBuildDiagnosticResult.Fail(
                code: code,
                contextPath: path,
                remediation: "Update ASM-Lite FullController path mapping for this VRCFury schema before applying writes.");
        }

        private static bool EnsureArraySizeStrict(SerializedObject so, string path, int size)
        {
            var prop = so?.FindProperty(path);
            if (prop == null || !prop.isArray)
                return false;

            prop.arraySize = size;
            return true;
        }

        private static bool SetObjectReferenceStrict(SerializedObject so, string path, UnityEngine.Object value)
        {
            var prop = so?.FindProperty(path);
            if (prop == null)
                return false;

            prop.objectReferenceValue = value;
            return true;
        }

        private static bool SetAnyObjectReferenceStrict(SerializedObject so, string[] candidatePaths, UnityEngine.Object value)
        {
            if (candidatePaths == null || candidatePaths.Length == 0)
                return false;

            for (int i = 0; i < candidatePaths.Length; i++)
            {
                var path = candidatePaths[i];
                if (string.IsNullOrEmpty(path))
                    continue;

                var prop = so?.FindProperty(path);
                if (prop == null)
                    continue;

                prop.objectReferenceValue = value;
                return true;
            }

            return false;
        }

        private static bool SetIntStrict(SerializedObject so, string path, int value)
        {
            var prop = so?.FindProperty(path);
            if (prop == null)
                return false;

            prop.intValue = value;
            return true;
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
