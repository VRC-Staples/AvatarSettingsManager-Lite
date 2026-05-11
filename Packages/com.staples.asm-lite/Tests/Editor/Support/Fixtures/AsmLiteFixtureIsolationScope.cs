using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly int[] _baselineSelectionIds;
        private readonly UnityEngine.Object[] _baselineSelectionObjects;
        private readonly SceneSetup[] _baselineSceneSetup;
        private readonly string _baselineSceneSetupSignature;
        private bool _disposed;

        private AsmLiteFixtureIsolationScope(string owner)
        {
            _owner = string.IsNullOrWhiteSpace(owner) ? "ASM-Lite fixture" : owner;
            AssetDatabase.Refresh();
            _baselineAssetPathsByGuid = CaptureGeneratedAssetPathsByGuid();
            _baselineSceneRootPathsById = CaptureSceneRootPathsById();
            _baselineSelectionObjects = Selection.objects ?? Array.Empty<UnityEngine.Object>();
            _baselineSelectionIds = _baselineSelectionObjects
                .Where(selection => selection != null)
                .Select(selection => selection.GetInstanceID())
                .ToArray();
            _baselineSceneSetup = EditorSceneManager.GetSceneManagerSetup() ?? Array.Empty<SceneSetup>();
            _baselineSceneSetupSignature = SceneSetupSignature(_baselineSceneSetup);
        }

        internal static AsmLiteFixtureIsolationScope Capture(string owner)
        {
            return new AsmLiteFixtureIsolationScope(owner);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            var contamination = CaptureContaminationMessages();
            RestoreEditorState();
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

            if (_baselineSceneSetup.Length > 0)
                EditorSceneManager.RestoreSceneManagerSetup(_baselineSceneSetup);
        }

        private void AddGeneratedAssetLeakMessages(List<string> messages)
        {
            var currentAssetPathsByGuid = CaptureGeneratedAssetPathsByGuid();
            var leakedAssetPaths = currentAssetPathsByGuid
                .Where(pair => !_baselineAssetPathsByGuid.ContainsKey(pair.Key))
                .Select(pair => pair.Value)
                .Where(path => !string.IsNullOrEmpty(path))
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
            var currentSelectionIds = (Selection.objects ?? Array.Empty<UnityEngine.Object>())
                .Where(selection => selection != null)
                .Select(selection => selection.GetInstanceID())
                .ToArray();

            if (_baselineSelectionIds.SequenceEqual(currentSelectionIds))
                return;

            messages.Add("selection leak: expected [" + string.Join(", ", _baselineSelectionIds) + "] but found [" + string.Join(", ", currentSelectionIds) + "]");
        }

        private void AddOpenSceneLeakMessage(List<string> messages)
        {
            var currentSceneSetup = EditorSceneManager.GetSceneManagerSetup() ?? Array.Empty<SceneSetup>();
            var currentSceneSetupSignature = SceneSetupSignature(currentSceneSetup);
            if (string.Equals(_baselineSceneSetupSignature, currentSceneSetupSignature, StringComparison.Ordinal))
                return;

            messages.Add("open scene leak: expected [" + _baselineSceneSetupSignature + "] but found [" + currentSceneSetupSignature + "]");
        }

        private static Dictionary<string, string> CaptureGeneratedAssetPathsByGuid()
        {
            var pathsByGuid = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var root in GeneratedAssetRoots)
            {
                AddAssetGuid(pathsByGuid, AssetDatabase.AssetPathToGUID(root));
                if (!AssetDatabase.IsValidFolder(root))
                    continue;

                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new[] { root }))
                    AddAssetGuid(pathsByGuid, guid);
            }

            return pathsByGuid;
        }

        private static void AddAssetGuid(Dictionary<string, string> pathsByGuid, string guid)
        {
            if (string.IsNullOrEmpty(guid) || pathsByGuid.ContainsKey(guid))
                return;

            pathsByGuid.Add(guid, AssetDatabase.GUIDToAssetPath(guid));
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

        private static string SceneSetupSignature(SceneSetup[] setup)
        {
            if (setup == null || setup.Length == 0)
                return string.Empty;

            return string.Join(";", setup.Select(scene =>
                (scene.path ?? string.Empty) + "|loaded=" + scene.isLoaded + "|active=" + scene.isActive));
        }
    }
}
