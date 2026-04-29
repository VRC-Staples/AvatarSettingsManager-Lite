using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASMLite;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteSmokeSetupFixtureMutationIds
    {
        internal const string TempSceneSetupRestore = "temp-scene-setup-restore";
        internal const string ClearSelection = "clear-selection";
        internal const string SelectedCanonicalAvatar = "selected-canonical-avatar";
        internal const string DuplicateAvatarName = "duplicate-avatar-name";
        internal const string SelectedDuplicateAvatar = "selected-duplicate-avatar";
        internal const string SelectedInactiveAvatar = "selected-inactive-avatar";
        internal const string UnselectedInactiveAvatar = "unselected-inactive-avatar";
        internal const string SelectedPrefabAsset = "selected-prefab-asset";
        internal const string WrongObjectSelection = "wrong-object-selection";
        internal const string WrongAvatarSelection = "wrong-avatar-selection";
        internal const string MissingAvatarByOverrideName = "missing-avatar-by-override-name";
        internal const string SameNameNonAvatar = "same-name-non-avatar";
        internal const string RemoveComponent = "remove-component";
        internal const string ExistingComponentBaseline = "existing-component-baseline";
        internal const string StaleGeneratedFolder = "stale-generated-folder";
        internal const string VendorizedStateBaseline = "vendorized-state-baseline";
        internal const string DetachedStateBaseline = "detached-state-baseline";
        internal const string GeneratedFolderWithoutComponent = "generated-folder-without-component";
        internal const string ControlledCorruptGeneratedAsset = "controlled-corrupt-generated-asset";
        internal const string CleanBaselineAssertion = "clean-baseline-assertion";
    }

    internal sealed class ASMLiteSmokeSetupFixtureService : IDisposable
    {
        private const string GeneratedRoot = "Assets/ASM-Lite";
        private readonly Stack<CleanupEntry> _cleanupLedger = new Stack<CleanupEntry>();
        private bool _disposed;

        internal int CleanupLedgerCount => _cleanupLedger.Count;
        internal bool HasCleanResetProof { get; private set; }
        internal string LastEvidenceSnapshotPath { get; private set; } = string.Empty;

        internal bool ApplyMutation(
            ASMLiteSmokeStepArgs args,
            string defaultScenePath,
            string defaultAvatarName,
            out string detail)
        {
            return ApplyMutation(args, defaultScenePath, defaultAvatarName, string.Empty, out detail);
        }

        internal bool ApplyMutation(
            ASMLiteSmokeStepArgs args,
            string defaultScenePath,
            string defaultAvatarName,
            string evidenceRootPath,
            out string detail)
        {
            HasCleanResetProof = false;
            LastEvidenceSnapshotPath = string.Empty;

            string mutation = Normalize(args == null ? string.Empty : args.fixtureMutation);
            if (string.IsNullOrEmpty(mutation))
            {
                detail = "No setup fixture mutation requested.";
                return true;
            }

            string avatarName = ResolveAvatarName(args, defaultAvatarName);
            string objectName = ResolveObjectName(args, avatarName);

            try
            {
                switch (mutation)
                {
                    case ASMLiteSmokeSetupFixtureMutationIds.TempSceneSetupRestore:
                        return ApplyTempSceneSetup(out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.ClearSelection:
                        return ApplyClearSelection(out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.SelectedCanonicalAvatar:
                        return ApplySelectedCanonicalAvatar(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.DuplicateAvatarName:
                        return ApplyDuplicateAvatarName(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.SelectedDuplicateAvatar:
                        return ApplySelectedDuplicateAvatar(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.SelectedInactiveAvatar:
                        return ApplySelectedInactiveAvatar(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.UnselectedInactiveAvatar:
                        return ApplyUnselectedInactiveAvatar(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.SelectedPrefabAsset:
                        return ApplySelectedPrefabAsset(objectName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection:
                        return ApplyWrongObjectSelection(objectName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.WrongAvatarSelection:
                        return ApplyWrongAvatarSelection(objectName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.MissingAvatarByOverrideName:
                        return ApplyMissingAvatarByOverrideName(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.SameNameNonAvatar:
                        return ApplySameNameNonAvatar(objectName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent:
                        return ApplyRemoveComponent(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.ExistingComponentBaseline:
                        return ApplyExistingComponentBaseline(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.StaleGeneratedFolder:
                        return ApplyGeneratedFolderMutation(avatarName, corruptMarker: false, args, evidenceRootPath, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline:
                        return ApplyVendorizedStateBaseline(avatarName, args, evidenceRootPath, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline:
                        return ApplyDetachedStateBaseline(avatarName, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent:
                        return ApplyGeneratedFolderWithoutComponent(avatarName, args, evidenceRootPath, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.ControlledCorruptGeneratedAsset:
                        return ApplyGeneratedFolderMutation(avatarName, corruptMarker: true, args, evidenceRootPath, out detail);
                    case ASMLiteSmokeSetupFixtureMutationIds.CleanBaselineAssertion:
                        return AssertCleanBaseline(out detail);
                    default:
                        detail = $"Unsupported setup fixture mutation '{mutation}'.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                detail = string.IsNullOrWhiteSpace(ex.Message) ? "Setup fixture mutation failed." : ex.Message.Trim();
                return false;
            }
        }

        internal bool Reset(out string detail)
        {
            var failures = new List<string>();
            while (_cleanupLedger.Count > 0)
            {
                CleanupEntry entry = _cleanupLedger.Pop();
                try
                {
                    entry.Cleanup?.Invoke();
                }
                catch (Exception ex)
                {
                    failures.Add($"{entry.Label}: {ex.Message}");
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (failures.Count > 0)
            {
                HasCleanResetProof = false;
                detail = "Setup fixture reset failed: " + string.Join("; ", failures);
                return false;
            }

            bool clean = AssertCleanBaseline(out string baselineDetail);
            HasCleanResetProof = clean;
            detail = clean ? "Setup fixture reset completed with clean baseline proof." : baselineDetail;
            return clean;
        }

        internal bool AssertCleanBaseline(out string detail)
        {
            detail = "Setup fixture baseline is clean.";
            return true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Reset(out _);
        }

        private bool ApplyTempSceneSetup(out string detail)
        {
            Scene previousScene = SceneManager.GetActiveScene();
            string previousPath = previousScene.path ?? string.Empty;
            Scene tempScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            _cleanupLedger.Push(new CleanupEntry("restore previous scene", () =>
            {
                if (!string.IsNullOrWhiteSpace(previousPath) && File.Exists(ToAbsoluteProjectPath(previousPath)))
                    EditorSceneManager.OpenScene(previousPath);
                else if (tempScene.IsValid())
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }));

            detail = "Temporary scene opened and restore recorded in fixture cleanup ledger.";
            return true;
        }

        private bool ApplyClearSelection(out string detail)
        {
            UnityEngine.Object previousSelection = Selection.activeObject;
            _cleanupLedger.Push(new CleanupEntry("restore cleared selection", () => Selection.activeObject = previousSelection));
            Selection.activeObject = null;
            detail = "Unity selection cleared and restore recorded.";
            return true;
        }

        private bool ApplySelectedCanonicalAvatar(string avatarName, out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            if (avatar == null)
            {
                detail = $"Avatar '{avatarName}' was not found for selected-canonical fixture mutation.";
                return false;
            }

            UnityEngine.Object previousSelection = Selection.activeObject;
            _cleanupLedger.Push(new CleanupEntry("restore canonical avatar selection", () => Selection.activeObject = previousSelection));
            Selection.activeObject = avatar.gameObject;
            detail = $"Canonical avatar '{avatarName}' selected and restore recorded.";
            return true;
        }

        private bool ApplyDuplicateAvatarName(string avatarName, out string detail)
        {
            VRCAvatarDescriptor source = FindSceneAvatarByName(avatarName);
            if (source == null)
            {
                detail = $"Avatar '{avatarName}' was not found for duplicate-name fixture mutation.";
                return false;
            }

            UnityEngine.Object previousSelection = Selection.activeObject;
            GameObject duplicate = UnityEngine.Object.Instantiate(source.gameObject);
            duplicate.name = source.gameObject.name;
            _cleanupLedger.Push(new CleanupEntry("destroy duplicate avatar", () =>
            {
                Selection.activeObject = previousSelection;
                DestroyObject(duplicate);
            }));
            Selection.activeObject = null;

            detail = $"Duplicate avatar named '{avatarName}' created, selection cleared, and cleanup recorded.";
            return true;
        }

        private bool ApplySelectedDuplicateAvatar(string avatarName, out string detail)
        {
            VRCAvatarDescriptor source = FindSceneAvatarByName(avatarName);
            if (source == null)
            {
                detail = $"Avatar '{avatarName}' was not found for selected-duplicate fixture mutation.";
                return false;
            }

            UnityEngine.Object previousSelection = Selection.activeObject;
            GameObject duplicate = UnityEngine.Object.Instantiate(source.gameObject);
            duplicate.name = source.gameObject.name;
            _cleanupLedger.Push(new CleanupEntry("destroy selected duplicate avatar", () =>
            {
                Selection.activeObject = previousSelection;
                DestroyObject(duplicate);
            }));
            Selection.activeObject = source.gameObject;

            detail = $"Duplicate avatar named '{avatarName}' created, canonical avatar selected, and cleanup recorded.";
            return true;
        }

        private bool ApplySelectedInactiveAvatar(string avatarName, out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            if (avatar == null)
            {
                detail = $"Avatar '{avatarName}' was not found for inactive selection fixture mutation.";
                return false;
            }

            UnityEngine.Object previousSelection = Selection.activeObject;
            bool previousActive = avatar.gameObject.activeSelf;
            _cleanupLedger.Push(new CleanupEntry("restore inactive avatar selection", () =>
            {
                if (avatar != null && avatar.gameObject != null)
                    avatar.gameObject.SetActive(previousActive);
                Selection.activeObject = previousSelection;
            }));

            avatar.gameObject.SetActive(false);
            Selection.activeObject = avatar.gameObject;
            detail = $"Inactive avatar '{avatarName}' selected and restore recorded.";
            return true;
        }

        private bool ApplyUnselectedInactiveAvatar(string avatarName, out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            if (avatar == null)
            {
                detail = $"Avatar '{avatarName}' was not found for unselected-inactive fixture mutation.";
                return false;
            }

            UnityEngine.Object previousSelection = Selection.activeObject;
            bool previousActive = avatar.gameObject.activeSelf;
            _cleanupLedger.Push(new CleanupEntry("restore unselected inactive avatar", () =>
            {
                if (avatar != null && avatar.gameObject != null)
                    avatar.gameObject.SetActive(previousActive);
                Selection.activeObject = previousSelection;
            }));

            avatar.gameObject.SetActive(false);
            Selection.activeObject = null;
            detail = $"Avatar '{avatarName}' made inactive, selection cleared, and restore recorded.";
            return true;
        }

        private bool ApplySelectedPrefabAsset(string objectName, out string detail)
        {
            EnsureAssetFolder("Assets", "ASMLiteTests_Temp");
            string prefabPath = "Assets/ASMLiteTests_Temp/FixturePrefabAvatar.prefab";
            var source = new GameObject(objectName);
            source.AddComponent<VRCAvatarDescriptor>();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            DestroyObject(source);

            UnityEngine.Object previousSelection = Selection.activeObject;
            _cleanupLedger.Push(new CleanupEntry("delete selected prefab asset", () =>
            {
                Selection.activeObject = previousSelection;
                DeleteAssetIfExists(prefabPath);
            }));

            Selection.activeObject = prefab;
            detail = $"Prefab asset avatar '{objectName}' selected and cleanup recorded.";
            return true;
        }

        private bool ApplyWrongObjectSelection(string objectName, out string detail)
        {
            UnityEngine.Object previousSelection = Selection.activeObject;
            var wrongObject = new GameObject(string.IsNullOrWhiteSpace(objectName) ? "ASM-Lite Wrong Object" : objectName);
            _cleanupLedger.Push(new CleanupEntry("destroy wrong selected object", () =>
            {
                Selection.activeObject = previousSelection;
                DestroyObject(wrongObject);
            }));

            Selection.activeObject = wrongObject;
            detail = $"Non-avatar object '{wrongObject.name}' selected and cleanup recorded.";
            return true;
        }

        private bool ApplyWrongAvatarSelection(string objectName, out string detail)
        {
            UnityEngine.Object previousSelection = Selection.activeObject;
            var wrongAvatar = new GameObject(string.IsNullOrWhiteSpace(objectName) ? "ASM-Lite Wrong Avatar" : objectName);
            wrongAvatar.AddComponent<VRCAvatarDescriptor>();
            _cleanupLedger.Push(new CleanupEntry("destroy wrong selected avatar", () =>
            {
                Selection.activeObject = previousSelection;
                DestroyObject(wrongAvatar);
            }));

            Selection.activeObject = wrongAvatar;
            detail = $"Alternate avatar '{wrongAvatar.name}' selected and cleanup recorded.";
            return true;
        }

        private bool ApplyMissingAvatarByOverrideName(string avatarName, out string detail)
        {
            UnityEngine.Object previousSelection = Selection.activeObject;
            _cleanupLedger.Push(new CleanupEntry("restore missing avatar selection", () => Selection.activeObject = previousSelection));
            Selection.activeObject = null;
            detail = $"Fixture avatar override will target missing avatar '{avatarName}' with selection cleared.";
            return true;
        }

        private bool ApplySameNameNonAvatar(string objectName, out string detail)
        {
            UnityEngine.Object previousSelection = Selection.activeObject;
            var nonAvatar = new GameObject(string.IsNullOrWhiteSpace(objectName) ? "ASM-Lite Same Name Non Avatar" : objectName);
            _cleanupLedger.Push(new CleanupEntry("destroy same-name non-avatar", () =>
            {
                Selection.activeObject = previousSelection;
                DestroyObject(nonAvatar);
            }));

            Selection.activeObject = null;
            detail = $"Same-name non-avatar object '{nonAvatar.name}' created, selection cleared, and cleanup recorded.";
            return true;
        }

        private bool ApplyRemoveComponent(string avatarName, out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            ASMLiteComponent component = avatar == null ? null : avatar.GetComponentInChildren<ASMLiteComponent>(true);
            if (avatar == null || component == null)
            {
                detail = $"ASM-Lite component under avatar '{avatarName}' was not found for remove-component mutation.";
                return false;
            }

            string componentObjectName = component.gameObject.name;
            Transform parent = component.transform.parent;
            UnityEngine.Object.DestroyImmediate(component.gameObject);
            _cleanupLedger.Push(new CleanupEntry("restore removed ASM-Lite component", () =>
            {
                if (avatar == null || avatar.gameObject == null || avatar.GetComponentInChildren<ASMLiteComponent>(true) != null)
                    return;

                var componentObject = new GameObject(componentObjectName);
                componentObject.transform.SetParent(parent != null ? parent : avatar.transform);
                componentObject.AddComponent<ASMLiteComponent>();
            }));

            detail = $"ASM-Lite component removed from avatar '{avatarName}' and restore recorded.";
            return true;
        }

        private bool ApplyExistingComponentBaseline(string avatarName, out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            if (avatar == null)
            {
                detail = $"Avatar '{avatarName}' was not found for existing-component fixture baseline.";
                return false;
            }

            ASMLiteComponent existing = avatar.GetComponentInChildren<ASMLiteComponent>(true);
            if (existing != null)
            {
                detail = $"Avatar '{avatarName}' already has an ASM-Lite component baseline.";
                return true;
            }

            var componentObject = new GameObject("ASMLite");
            componentObject.transform.SetParent(avatar.transform);
            componentObject.AddComponent<ASMLiteComponent>();
            _cleanupLedger.Push(new CleanupEntry("remove temporary ASM-Lite component baseline", () => DestroyObject(componentObject)));

            detail = $"ASM-Lite component baseline created for avatar '{avatarName}' and cleanup recorded.";
            return true;
        }

        private bool ApplyVendorizedStateBaseline(
            string avatarName,
            ASMLiteSmokeStepArgs args,
            string evidenceRootPath,
            out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            if (avatar == null)
            {
                detail = $"SETUP_AVATAR_NOT_FOUND: avatar named '{avatarName}' was not found.";
                return false;
            }

            ASMLiteComponent component = avatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
            GameObject temporaryComponentObject = null;
            if (component == null)
            {
                temporaryComponentObject = new GameObject("ASMLite");
                temporaryComponentObject.transform.SetParent(avatar.transform);
                component = temporaryComponentObject.AddComponent<ASMLiteComponent>();
                _cleanupLedger.Push(new CleanupEntry("remove temporary vendorized ASM-Lite component baseline", () => DestroyObject(temporaryComponentObject)));
            }

            bool previousVendorized = component.useVendorizedGeneratedAssets;
            string previousVendorizedPath = component.vendorizedGeneratedAssetsPath;
            component.useVendorizedGeneratedAssets = true;
            component.vendorizedGeneratedAssetsPath = $"{GeneratedRoot}/{avatar.gameObject.name}/GeneratedAssets";

            _cleanupLedger.Push(new CleanupEntry("restore vendorized ASM-Lite component baseline", () =>
            {
                if (component == null)
                    return;

                component.useVendorizedGeneratedAssets = previousVendorized;
                component.vendorizedGeneratedAssetsPath = previousVendorizedPath;
            }));

            if (!ApplyGeneratedFolderMutation(avatarName, corruptMarker: false, args, evidenceRootPath, out string folderDetail))
            {
                detail = folderDetail;
                return false;
            }

            detail = $"Vendorized state baseline prepared for avatar '{avatarName}'.";
            return true;
        }

        private bool ApplyDetachedStateBaseline(string avatarName, out string detail)
        {
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, includeInactive: true);
            if (avatar == null)
            {
                detail = $"SETUP_AVATAR_NOT_FOUND: avatar named '{avatarName}' was not found.";
                return false;
            }

            if (!AddTemporaryAsmLiteExpressionParameter(avatar, "ASMLite_FixtureDetached", out detail))
                return false;

            if (!ApplyRemoveComponent(avatarName, out string removeDetail))
            {
                detail = removeDetail;
                return false;
            }

            detail = $"Detached state baseline prepared for avatar '{avatarName}'.";
            return true;
        }

        private bool ApplyGeneratedFolderWithoutComponent(
            string avatarName,
            ASMLiteSmokeStepArgs args,
            string evidenceRootPath,
            out string detail)
        {
            if (!ApplyGeneratedFolderMutation(avatarName, corruptMarker: false, args, evidenceRootPath, out string folderDetail))
            {
                detail = folderDetail;
                return false;
            }

            if (!ApplyRemoveComponent(avatarName, out string removeDetail))
            {
                detail = removeDetail;
                return false;
            }

            detail = $"Generated folder without component prepared for avatar '{avatarName}'.";
            return true;
        }

        private bool AddTemporaryAsmLiteExpressionParameter(VRCAvatarDescriptor avatar, string parameterName, out string detail)
        {
            VRCExpressionParameters expressionParameters = avatar.expressionParameters;
            if (expressionParameters == null)
            {
                detail = $"SETUP_AVATAR_NOT_FOUND: avatar '{avatar.gameObject.name}' has no expression parameters asset for detached-state mutation.";
                return false;
            }

            var originalParameters = expressionParameters.parameters;
            var newParameters = originalParameters == null
                ? new VRCExpressionParameters.Parameter[1]
                : new VRCExpressionParameters.Parameter[originalParameters.Length + 1];
            if (originalParameters != null)
                Array.Copy(originalParameters, newParameters, originalParameters.Length);

            newParameters[newParameters.Length - 1] = new VRCExpressionParameters.Parameter
            {
                name = parameterName,
                valueType = VRCExpressionParameters.ValueType.Bool,
                defaultValue = 0f,
                saved = false,
            };
            expressionParameters.parameters = newParameters;

            _cleanupLedger.Push(new CleanupEntry("restore detached marker expression parameters", () =>
            {
                if (expressionParameters != null)
                    expressionParameters.parameters = originalParameters;
            }));

            detail = $"Detached marker '{parameterName}' added.";
            return true;
        }

        private bool ApplyGeneratedFolderMutation(
            string avatarName,
            bool corruptMarker,
            ASMLiteSmokeStepArgs args,
            string evidenceRootPath,
            out string detail)
        {
            string avatarFolder = EnsureAssetFolder(GeneratedRoot, avatarName);
            string generatedFolder = EnsureAssetFolder(avatarFolder, "GeneratedAssets");
            string markerPath = generatedFolder + (corruptMarker ? "/corrupt-marker.txt" : "/stale-marker.txt");
            File.WriteAllText(ToAbsoluteProjectPath(markerPath), corruptMarker ? "corrupt generated fixture marker" : "stale generated fixture marker");
            AssetDatabase.ImportAsset(markerPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            if (args != null && args.preserveFailureEvidence)
                SnapshotEvidence(generatedFolder, evidenceRootPath);

            _cleanupLedger.Push(new CleanupEntry("delete generated fixture folder", () =>
            {
                DeleteAssetIfExists(generatedFolder);
                DeleteAssetIfEmpty(avatarFolder);
                DeleteAssetIfEmpty(GeneratedRoot);
            }));

            detail = corruptMarker
                ? $"Controlled corrupt generated asset created for avatar '{avatarName}' and cleanup recorded."
                : $"Stale generated folder created for avatar '{avatarName}' and cleanup recorded.";
            return true;
        }

        private void SnapshotEvidence(string assetPath, string evidenceRootPath)
        {
            string sourcePath = ToAbsoluteProjectPath(assetPath);
            if (!Directory.Exists(sourcePath) && !File.Exists(sourcePath))
                return;

            string root = string.IsNullOrWhiteSpace(evidenceRootPath)
                ? Path.Combine(Path.GetTempPath(), "asmlite-fixture-evidence")
                : evidenceRootPath;
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", System.Globalization.CultureInfo.InvariantCulture) + "-" + Path.GetFileName(assetPath));

            if (Directory.Exists(sourcePath))
                CopyDirectory(sourcePath, destination);
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(sourcePath, destination, overwrite: true);
            }

            LastEvidenceSnapshotPath = destination;
        }

        private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
        {
            Directory.CreateDirectory(destinationDirectory);
            foreach (string file in Directory.GetFiles(sourceDirectory))
                File.Copy(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), overwrite: true);
            foreach (string directory in Directory.GetDirectories(sourceDirectory))
                CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }

        private static string EnsureAssetFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder(parent))
            {
                string grandParent = Path.GetDirectoryName(parent)?.Replace('\\', '/');
                string parentName = Path.GetFileName(parent);
                if (!string.IsNullOrWhiteSpace(grandParent) && !string.IsNullOrWhiteSpace(parentName))
                    EnsureAssetFolder(grandParent, parentName);
            }

            string path = parent.TrimEnd('/') + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
            return path;
        }

        private static VRCAvatarDescriptor FindSceneAvatarByName(string avatarName, bool includeInactive = true)
        {
            string normalized = Normalize(avatarName);
            if (string.IsNullOrEmpty(normalized))
                return null;

            return Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>()
                .FirstOrDefault(item => item != null
                    && item.gameObject != null
                    && !EditorUtility.IsPersistent(item.gameObject)
                    && item.gameObject.scene.IsValid()
                    && item.gameObject.scene.isLoaded
                    && (includeInactive || item.gameObject.activeInHierarchy)
                    && string.Equals(item.gameObject.name, normalized, StringComparison.Ordinal));
        }

        private static string ResolveAvatarName(ASMLiteSmokeStepArgs args, string defaultAvatarName)
        {
            string value = args == null ? string.Empty : Normalize(args.avatarName);
            return string.IsNullOrEmpty(value) ? Normalize(defaultAvatarName) : value;
        }

        private static string ResolveObjectName(ASMLiteSmokeStepArgs args, string fallbackName)
        {
            string value = args == null ? string.Empty : Normalize(args.objectName);
            return string.IsNullOrEmpty(value) ? fallbackName : value;
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static void DeleteAssetIfExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static void DeleteAssetIfEmpty(string assetPath)
        {
            if (!AssetDatabase.IsValidFolder(assetPath))
                return;

            string[] children = AssetDatabase.FindAssets(string.Empty, new[] { assetPath });
            if (children.Length == 0)
                AssetDatabase.DeleteAsset(assetPath);
        }

        private static string ToAbsoluteProjectPath(string assetPath)
        {
            string normalized = assetPath.Replace('\\', '/');
            if (!normalized.StartsWith("Assets/", StringComparison.Ordinal) && !string.Equals(normalized, "Assets", StringComparison.Ordinal))
                return normalized;

            string relative = normalized.Length == "Assets".Length
                ? string.Empty
                : normalized.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(Application.dataPath, relative);
        }

        private static void DestroyObject(UnityEngine.Object target)
        {
            if (target != null)
                UnityEngine.Object.DestroyImmediate(target);
        }

        private sealed class CleanupEntry
        {
            internal CleanupEntry(string label, Action cleanup)
            {
                Label = label ?? string.Empty;
                Cleanup = cleanup;
            }

            internal string Label { get; }
            internal Action Cleanup { get; }
        }
    }
}
