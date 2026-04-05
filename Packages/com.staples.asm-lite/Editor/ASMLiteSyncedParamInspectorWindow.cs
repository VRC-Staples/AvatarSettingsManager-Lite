using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    /// <summary>
    /// Inspector window that shows the true synced expression parameter usage
    /// for any VRCAvatarDescriptor in the scene. This is intended as a source
    /// of truth when debugging tools that display aggregate synced counts.
    /// </summary>
    public class ASMLiteSyncedParamInspectorWindow : EditorWindow
    {
        private VRCAvatarDescriptor _selectedAvatar;
        private Vector2 _scroll;

        private int _totalParams;
        private int _syncedParams;
        private string _assetPath;
        private readonly List<string> _syncedNames = new List<string>(32);

        [MenuItem("Tools/.Staples./Synced Param Inspector (Debug Tool)")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteSyncedParamInspectorWindow>(title: ".Staples. Synced Params (Debug Tool)");
            win.minSize = new Vector2(420, 320);
            win.Show();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null)
                return;

            var descriptor = Selection.activeGameObject
                .GetComponentInParent<VRCAvatarDescriptor>(includeInactive: true)
                ?? Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>();

            if (descriptor != null && descriptor != _selectedAvatar)
            {
                _selectedAvatar = descriptor;
                Refresh();
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Synced Expression Parameters", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope("box"))
            {
                var newAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                    "Avatar Root",
                    _selectedAvatar,
                    typeof(VRCAvatarDescriptor),
                    allowSceneObjects: true);

                if (newAvatar != _selectedAvatar)
                {
                    _selectedAvatar = newAvatar;
                    Refresh();
                }

                if (_selectedAvatar == null)
                {
                    EditorGUILayout.HelpBox(
                        "Select a VRC Avatar Descriptor in the scene to inspect its expression parameters.",
                        MessageType.Info);
                    return;
                }

                EditorGUILayout.Space(4);

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField(
                        "Expression Parameters Asset",
                        _selectedAvatar.expressionParameters,
                        typeof(VRCExpressionParameters),
                        allowSceneObjects: false);
                }

                EditorGUILayout.LabelField("Asset Path", string.IsNullOrEmpty(_assetPath) ? "<none>" : _assetPath);
                EditorGUILayout.Space(2);

                EditorGUILayout.LabelField("Total Entries", _totalParams.ToString());
                EditorGUILayout.LabelField("Synced Entries", _syncedParams.ToString());

                EditorGUILayout.Space(6);
                EditorGUILayout.HelpBox(
                    "These counts come directly from the VRCExpressionParameters asset. " +
                    "Use them as the source of truth when other tools report different synced param totals.",
                    MessageType.None);
            }

            EditorGUILayout.Space(8);

            EditorGUILayout.LabelField("Synced Parameter Names", EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            if (_syncedNames.Count == 0)
            {
                EditorGUILayout.LabelField("<none>", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var name in _syncedNames)
                {
                    EditorGUILayout.LabelField(name, EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void Refresh()
        {
            _totalParams = 0;
            _syncedParams = 0;
            _assetPath = string.Empty;
            _syncedNames.Clear();

            if (_selectedAvatar == null)
                return;

            var expr = _selectedAvatar.expressionParameters;
            if (expr == null)
                return;

            _assetPath = AssetDatabase.GetAssetPath(expr);

            var parameters = expr.parameters;
            if (parameters == null)
                return;

            foreach (var p in parameters)
            {
                if (p == null || string.IsNullOrEmpty(p.name)) continue;
                _totalParams++;
                if (p.networkSynced)
                {
                    _syncedParams++;
                    _syncedNames.Add(p.name);
                }
            }
        }
    }
}
