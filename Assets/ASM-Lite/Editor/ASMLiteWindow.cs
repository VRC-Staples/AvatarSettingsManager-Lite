using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASM-Lite main editor window. Opens via Tools → .Staples. → ASM-Lite.
    ///
    /// Provides:
    ///   • Avatar hierarchy picker
    ///   • Status / diagnostics panel
    ///   • "Add ASM-Lite Prefab" button
    /// </summary>
    public class ASMLiteWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private VRCAvatarDescriptor _selectedAvatar;
        private Vector2             _scrollPos;

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/.Staples./ASM-Lite")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteWindow>(title: "ASM-Lite");
            win.minSize = new Vector2(380, 320);
            win.Show();
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(8);

            DrawAvatarPicker();
            EditorGUILayout.Space(8);

            DrawStatus();
            EditorGUILayout.Space(12);

            DrawAddButton();
            EditorGUILayout.Space(8);

            EditorGUILayout.EndScrollView();
        }

        // ── Sections ──────────────────────────────────────────────────────────

        private void DrawHeader()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("ASM-Lite", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Avatar Settings Manager — Lite Edition",
                EditorStyles.miniLabel);
        }

        private void DrawAvatarPicker()
        {
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);

            var newAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                label:       "Avatar Root",
                obj:         _selectedAvatar,
                objType:     typeof(VRCAvatarDescriptor),
                allowSceneObjects: true);

            if (newAvatar != _selectedAvatar)
            {
                _selectedAvatar = newAvatar;
                Repaint();
            }

            if (_selectedAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Select the VRC Avatar Descriptor in your scene hierarchy to get started.",
                    MessageType.Info);
            }
        }

        private void DrawStatus()
        {
            if (_selectedAvatar == null)
                return;

            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            // ── ASM-Lite component presence ───────────────────────────────────
            var component = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);

            if (component != null)
            {
                EditorGUILayout.HelpBox(
                    "✓ ASM-Lite prefab is present on this avatar.",
                    MessageType.Info);

                // Slot count
                EditorGUILayout.LabelField(
                    $"Slot count: {component.slotCount}",
                    EditorStyles.miniLabel);

                // Expression parameters
                var exprParams = _selectedAvatar.expressionParameters;
                if (exprParams != null && exprParams.parameters != null)
                {
                    int customCount = exprParams.parameters
                        .Count(p => !string.IsNullOrEmpty(p.name) && !p.name.StartsWith("ASMLite_"));

                    EditorGUILayout.HelpBox(
                        $"✓ {customCount} custom parameter(s) will be backed up across " +
                        $"{component.slotCount} slot(s).",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "⚠ No VRCExpressionParameters asset assigned on avatar descriptor.",
                        MessageType.Warning);
                }
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "ASM-Lite prefab has not been added to this avatar yet.\n" +
                    "Click \"Add ASM-Lite Prefab\" below.",
                    MessageType.Warning);
            }
        }

        private void DrawAddButton()
        {
            using (new EditorGUI.DisabledScope(_selectedAvatar == null))
            {
                if (GUILayout.Button("Add ASM-Lite Prefab", GUILayout.Height(36)))
                    AddPrefabToAvatar();
            }

            if (_selectedAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Select an avatar above to enable this button.",
                    MessageType.None);
            }
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private void AddPrefabToAvatar()
        {
            if (_selectedAvatar == null)
                return;

            // Check for existing ASM-Lite component to avoid duplicates.
            var existing = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "ASM-Lite Already Present",
                    "An ASM-Lite component is already on this avatar.\n\n" +
                    "Do you want to add another instance?",
                    "Add Anyway", "Cancel");
                if (!replace)
                    return;
            }

            // Ensure the prefab exists first.
            ASMLitePrefabCreator.CreatePrefab();

            // Load the saved prefab and instantiate it under the avatar root.
            const string PrefabPath = "Assets/ASM-Lite/Prefabs/ASM-Lite.prefab";
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "ASM-Lite: Error",
                    $"Could not load prefab at {PrefabPath}.\nCheck the Console for details.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Add ASM-Lite Prefab");
            int group = Undo.GetCurrentGroup();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefabAsset, _selectedAvatar.transform);

            Undo.RegisterCreatedObjectUndo(instance, "Add ASM-Lite Prefab");
            Undo.CollapseUndoOperations(group);

            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            Debug.Log($"[ASM-Lite] Prefab added to '{_selectedAvatar.gameObject.name}'.");
            Repaint();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        private void OnSelectionChange()
        {
            // Auto-populate avatar picker when user selects something in the hierarchy.
            if (Selection.activeGameObject == null)
                return;

            var descriptor = Selection.activeGameObject
                .GetComponentInParent<VRCAvatarDescriptor>(includeInactive: true)
                ?? Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>();

            if (descriptor != null && descriptor != _selectedAvatar)
            {
                _selectedAvatar = descriptor;
                Repaint();
            }
        }
    }
}
