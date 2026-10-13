using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    internal sealed class AsmLiteFixtureIsolationScope : IDisposable
    {
        private static readonly string[] GeneratedAssetRoots =
        {
            ASMLiteTestFixtures.TempDir,
            "Assets/ASM-Lite",
            ASMLiteAssetPaths.GeneratedDir,
        };

        private readonly string _owner;
        private readonly Dictionary<string, string> _baselineAssetPathsByGuid;
        private readonly Dictionary<int, string> _baselineSceneRootPathsById;
        private readonly HashSet<string> _baselineSceneRootPaths;
        private readonly UnityEngine.Object[] _baselineSelectionObjects;
        private readonly SceneSetup[] _baselineSceneSetup;
        private readonly string _baselineOpenSceneSignature;
        private bool _disposed;
        private static int s_activeScopes;
        private static ASMLiteGeneratedAssetTestIsolation.SourceAssetsSnapshot s_expressionParametersBaseline;
        private static readonly HashSet<AsmLiteFixtureIsolationScope> FailedCleanupScopes = new HashSet<AsmLiteFixtureIsolationScope>();

        private AsmLiteFixtureIsolationScope(string owner)
        {
            if (FailedCleanupScopes.Count != 0)
                throw new InvalidOperationException("Fixture capture is blocked after incomplete cleanup. Repair the cause and call RetryFailedCleanup before creating another fixture. Failed owners: "
                    + string.Join(", ", FailedCleanupScopes.Select(scope => scope._owner)));
            _owner = string.IsNullOrWhiteSpace(owner) ? "ASM-Lite fixture" : owner;
            if (s_activeScopes == 0)
            {
                var parameters = AssetDatabase.LoadMainAssetAtPath(ASMLiteAssetPaths.ExprParams);
                if (parameters != null && EditorUtility.IsDirty(parameters))
                    throw new InvalidOperationException("Fixture capture cannot preserve unsaved expression parameter edits.");
            }
            AssetDatabase.Refresh();
            InitializeNdmfSceneHelpers();
            _baselineAssetPathsByGuid = CaptureGeneratedAssetPathsByGuid();
            _baselineSceneRootPathsById = CaptureSceneRootPathsById();
            _baselineSceneRootPaths = new HashSet<string>(_baselineSceneRootPathsById.Values, StringComparer.Ordinal);
            _baselineSelectionObjects = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            _baselineSceneSetup = EditorSceneManager.GetSceneManagerSetup() ?? Array.Empty<SceneSetup>();
            _baselineOpenSceneSignature = OpenSceneSignature();
            // Concurrent Editor fixtures share this template and must retain its first baseline.
            if (s_activeScopes == 0 && File.Exists(ASMLiteAssetPaths.ExprParams))
            {
                s_expressionParametersBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(_owner,
                    new[] { ASMLiteAssetPaths.ExprParams });
            }
            s_activeScopes++;
        }

        internal static AsmLiteFixtureIsolationScope Capture(string owner)
        {
            return new AsmLiteFixtureIsolationScope(owner);
        }

        internal static void RetryFailedCleanup()
        {
            // Keep failed owners reachable even after their avatars have been unregistered and destroyed.
            foreach (var scope in FailedCleanupScopes.ToArray())
                scope.Dispose();
        }

        private static void InitializeNdmfSceneHelpers()
        {
            // NDMF defers scene activation until delayCall, after synchronous SetUp.
            // Initialize the framework environment before capturing fixture-owned changes.
            var activatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name == "nadena.dev.ndmf.runtime")
                .Select(assembly => assembly.GetType("nadena.dev.ndmf.runtime.ApplyOnPlayGlobalActivator"))
                .SingleOrDefault(type => type != null);
            if (activatorType == null || EditorApplication.isPlaying)
                return;

            var initialize = activatorType.GetMethod("CreateIfNotPresent", BindingFlags.Static | BindingFlags.NonPublic,
                null, new[] { typeof(Scene) }, null);
            if (initialize == null)
                throw new InvalidOperationException("Installed NDMF scene initialization API is unavailable; fixture baseline cannot be captured safely.");

            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene))
                    continue;
                // Preserve existing helpers, including their flags and any duplicate state.
                if (scene.GetRootGameObjects().Any(root => root.GetComponent(activatorType) != null))
                    continue;
                initialize.Invoke(null, new object[] { scene });
            }
        }

        internal void DeleteFixtureGeneratedAssetLeaks(IEnumerable<string> ownedAssetPaths)
        {
            AssetDatabase.Refresh();
            var ownedPaths = ownedAssetPaths.Select(NormalizeAssetPath).Where(path => !string.IsNullOrEmpty(path)).ToArray();
            var leakedAssetPaths = CaptureGeneratedAssetPathsByGuid()
                .Where(pair => !_baselineAssetPathsByGuid.ContainsKey(pair.Key))
                .Select(pair => pair.Value)
                .Where(path => IsFixtureGeneratedAssetLeakPath(path, ownedPaths))
                .Where(path => !ASMLiteTestFixtures.IsRegisteredFixtureGeneratedAssetPath(path))
                .OrderByDescending(path => path.Length)
                .ToArray();

            foreach (var leakedAssetPath in leakedAssetPaths)
            {
                if (AssetDatabase.IsValidFolder(leakedAssetPath))
                    DeleteFixtureFolderIfEmpty(leakedAssetPath);
                else
                    DeleteFixtureAssetPath(leakedAssetPath);
            }

            AssetDatabase.Refresh();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            List<string> contamination;
            try
            {
                contamination = CaptureContaminationMessages();
                if (s_activeScopes == 1)
                {
                    s_expressionParametersBaseline?.Restore();
                    s_expressionParametersBaseline?.AssertUnchanged(_owner);
                }
                RestoreEditorState();
            }
            catch
            {
                FailedCleanupScopes.Add(this);
                throw;
            }
            FailedCleanupScopes.Remove(this);
            s_activeScopes--;
            if (s_activeScopes == 0)
                s_expressionParametersBaseline = null;
            _disposed = true;

            if (contamination.Count == 0)
                return;

            var message = new StringBuilder();
            message.Append(_owner);
            message.AppendLine(" left ASM-Lite fixture contamination after teardown:");
            foreach (var item in contamination)
            {
                message.Append("- ");
                message.AppendLine(item);
            }

            Assert.Fail(message.ToString().TrimEnd());
        }

        private List<string> CaptureContaminationMessages()
        {
            AssetDatabase.Refresh();
            var messages = new List<string>();
            AddGeneratedAssetLeakMessages(messages);
            AddSceneObjectLeakMessages(messages);
            AddSelectionLeakMessage(messages);
            AddOpenSceneLeakMessage(messages);
            return messages;
        }

        private void RestoreEditorState()
        {
            Selection.objects = _baselineSelectionObjects
                .Where(selection => selection != null)
                .ToArray();

            if (_baselineSceneSetup.Length > 0
                && !string.Equals(_baselineOpenSceneSignature, OpenSceneSignature(), StringComparison.Ordinal))
            {
                EditorSceneManager.RestoreSceneManagerSetup(_baselineSceneSetup);
            }
        }

        private void AddGeneratedAssetLeakMessages(List<string> messages)
        {
            var currentAssetPathsByGuid = CaptureGeneratedAssetPathsByGuid();
            var leakedAssetPaths = currentAssetPathsByGuid
                .Where(pair => !_baselineAssetPathsByGuid.ContainsKey(pair.Key))
                .Select(pair => pair.Value)
                .Where(path => !string.IsNullOrEmpty(path))
                .Where(path => !ASMLiteTestFixtures.IsRegisteredFixtureGeneratedAssetPath(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (leakedAssetPaths.Length == 0)
                return;

            messages.Add("generated asset leak: " + string.Join(", ", leakedAssetPaths));
        }

        private void AddSceneObjectLeakMessages(List<string> messages)
        {
            var currentSceneRootPathsById = CaptureSceneRootPathsById();
            var leakedSceneRoots = currentSceneRootPathsById
                .Where(pair => !_baselineSceneRootPathsById.ContainsKey(pair.Key))
                .Where(pair => !_baselineSceneRootPaths.Contains(pair.Value))
                .Where(pair => !ASMLiteTestFixtures.IsRegisteredFixtureAvatarRootId(pair.Key))
                .Select(pair => pair.Value)
                .Where(path => !string.IsNullOrEmpty(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            if (leakedSceneRoots.Length == 0)
                return;

            messages.Add("scene object leak: " + string.Join(", ", leakedSceneRoots));
        }

        private void AddSelectionLeakMessage(List<string> messages)
        {
            var expectedSelectionIds = _baselineSelectionObjects
                .Where(selection => selection != null)
                .Select(selection => selection.GetInstanceID())
                .ToArray();
            var currentSelectionIds = (Selection.objects ?? Array.Empty<UnityEngine.Object>())
                .Where(selection => selection != null)
                .Select(selection => selection.GetInstanceID())
                .ToArray();

            if (expectedSelectionIds.SequenceEqual(currentSelectionIds))
                return;

            messages.Add("selection leak: expected [" + string.Join(", ", expectedSelectionIds) + "] but found [" + string.Join(", ", currentSelectionIds) + "]");
        }

        private void AddOpenSceneLeakMessage(List<string> messages)
        {
            var currentOpenSceneSignature = OpenSceneSignature();
            if (string.Equals(_baselineOpenSceneSignature, currentOpenSceneSignature, StringComparison.Ordinal))
                return;

            messages.Add("open scene leak: expected [" + _baselineOpenSceneSignature + "] but found [" + currentOpenSceneSignature + "]");
        }

        private static Dictionary<string, string> CaptureGeneratedAssetPathsByGuid()
        {
            var pathsByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var root in GeneratedAssetRoots)
            {
                AddAssetPathGuid(pathsByGuid, root);
                if (!AssetDatabase.IsValidFolder(root))
                    continue;

                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { root }))
                    AddAssetGuid(pathsByGuid, guid);
            }

            return pathsByGuid;
        }

        private static bool IsFixtureGeneratedAssetLeakPath(string assetPath, IEnumerable<string> ownedPaths)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return false;

            if (string.Equals(normalizedPath, "Assets/ASM-Lite", StringComparison.Ordinal))
                return true;

            return ownedPaths.Any(ownedPath => string.Equals(normalizedPath, ownedPath, StringComparison.Ordinal)
                || normalizedPath.StartsWith(ownedPath + "/", StringComparison.Ordinal));
        }

        private static void DeleteFixtureAssetPath(string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return;

            AssetDatabase.DeleteAsset(normalizedPath);
            AssetDatabase.Refresh();

            if (!AssetDatabase.IsValidFolder(normalizedPath)
                && AssetDatabase.LoadMainAssetAtPath(normalizedPath) == null
                && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(normalizedPath))
                && !Directory.Exists(normalizedPath)
                && !File.Exists(normalizedPath))
                return;

            FileUtil.DeleteFileOrDirectory(normalizedPath);
            FileUtil.DeleteFileOrDirectory(normalizedPath + ".meta");
            AssetDatabase.Refresh();
        }

        private static void DeleteFixtureFolderIfEmpty(string assetFolderPath)
        {
            string normalizedPath = NormalizeAssetPath(assetFolderPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return;

            bool isValidAssetFolder = AssetDatabase.IsValidFolder(normalizedPath);
            bool existsOnDisk = Directory.Exists(normalizedPath);
            if (!isValidAssetFolder && !existsOnDisk)
                return;

            if (isValidAssetFolder)
            {
                var childAssetPaths = AssetDatabase.FindAssets(string.Empty, new[] { normalizedPath })
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .Select(NormalizeAssetPath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Where(path => !string.Equals(path, normalizedPath, StringComparison.Ordinal))
                    .ToArray();
                if (childAssetPaths.Length != 0)
                    return;
            }

            if (existsOnDisk && Directory.EnumerateFileSystemEntries(normalizedPath).Any())
                return;

            DeleteFixtureAssetPath(normalizedPath);
        }


        private static string NormalizeAssetPath(string assetPath)
        {
            return (assetPath ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
        }

        private static void AddAssetPathGuid(Dictionary<string, string> pathsByGuid, string assetPath)
        {
            string normalizedPath = NormalizeAssetPath(assetPath);
            if (string.IsNullOrEmpty(normalizedPath))
                return;
            if (!AssetDatabase.IsValidFolder(normalizedPath)
                && AssetDatabase.LoadMainAssetAtPath(normalizedPath) == null
                && !Directory.Exists(normalizedPath)
                && !File.Exists(normalizedPath))
                return;

            AddAssetGuid(pathsByGuid, AssetDatabase.AssetPathToGUID(normalizedPath));
        }

        private static void AddAssetGuid(Dictionary<string, string> pathsByGuid, string guid)
        {
            if (string.IsNullOrEmpty(guid) || pathsByGuid.ContainsKey(guid))
                return;

            string assetPath = NormalizeAssetPath(AssetDatabase.GUIDToAssetPath(guid));
            if (string.IsNullOrEmpty(assetPath))
                return;

            pathsByGuid.Add(guid, assetPath);
        }

        private static Dictionary<int, string> CaptureSceneRootPathsById()
        {
            var pathsById = new Dictionary<int, string>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded)
                    continue;

                foreach (var root in scene.GetRootGameObjects())
                {
                    if (root == null)
                        continue;

                    pathsById[root.GetInstanceID()] = SceneObjectPath(root);
                }
            }

            return pathsById;
        }

        private static string SceneObjectPath(GameObject root)
        {
            var sceneName = root.scene.IsValid() ? root.scene.name : string.Empty;
            if (string.IsNullOrEmpty(sceneName))
                sceneName = string.IsNullOrEmpty(root.scene.path) ? "<unsaved>" : root.scene.path;
            return sceneName + "/" + root.name;
        }

        private static string OpenSceneSignature()
        {
            var activeScene = SceneManager.GetActiveScene();
            var scenes = new List<string>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid())
                    continue;

                var sceneIdentity = string.IsNullOrEmpty(scene.path)
                    ? "<unsaved>:" + scene.name
                    : scene.path;
                scenes.Add(sceneIndex + ":" + sceneIdentity
                    + "|loaded=" + scene.isLoaded
                    + "|active=" + (scene.handle == activeScene.handle));
            }

            return string.Join(";", scenes);
        }
    }
}
