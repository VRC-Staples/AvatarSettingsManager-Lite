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
    ///   • Slot count configuration (before and after prefab is added)
    ///   • Status / diagnostics panel
    ///   • "Add ASM-Lite Prefab" button
    /// </summary>
    public class ASMLiteWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private VRCAvatarDescriptor _selectedAvatar;
        private Vector2             _scrollPos;

        // Pending slot count — shown before the prefab is added, applied on add.
        private int _pendingSlotCount = 3;

        // Cached serialized object for the component — rebuilt when component changes.
        private ASMLiteComponent   _cachedComponent;
        private SerializedObject   _serializedComponent;
        private SerializedProperty _slotCountProp;

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/.Staples./ASM-Lite")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteWindow>(title: "ASM-Lite");
            win.minSize = new Vector2(380, 360);
            win.Show();
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(8);

            DrawAvatarPicker();

            if (_selectedAvatar != null)
            {
                EditorGUILayout.Space(8);
                DrawSettings();
                EditorGUILayout.Space(8);
                DrawStatus();
                EditorGUILayout.Space(12);
                DrawAddButton();
            }

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
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);

            var newAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                label:             "Avatar Root",
                obj:               _selectedAvatar,
                objType:           typeof(VRCAvatarDescriptor),
                allowSceneObjects: true);

            if (newAvatar != _selectedAvatar)
            {
                _selectedAvatar = newAvatar;
                InvalidateComponentCache();

                // Pre-populate pending slot count from existing component if present.
                if (_selectedAvatar != null)
                {
                    var existing = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
                    if (existing != null)
                        _pendingSlotCount = existing.slotCount;
                }

                Repaint();
            }

            if (_selectedAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Select the VRC Avatar Descriptor in your scene hierarchy to get started.",
                    MessageType.Info);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            var component = GetOrRefreshComponent();

            if (component != null)
            {
                // Prefab already on avatar — edit via SerializedObject for Undo support.
                _serializedComponent.Update();
                EditorGUILayout.PropertyField(
                    _slotCountProp,
                    new GUIContent(
                        "Slot Count",
                        "Number of expression parameter slots ASM-Lite manages on this avatar."));
                _serializedComponent.ApplyModifiedProperties();

                // Keep pending in sync so if the component is removed and re-added
                // the last used value is preserved as the default.
                _pendingSlotCount = component.slotCount;
            }
            else
            {
                // No prefab yet — show the pending value the user can configure
                // before clicking Add.
                _pendingSlotCount = EditorGUILayout.IntSlider(
                    new GUIContent(
                        "Slot Count",
                        "Number of expression parameter slots ASM-Lite will manage on this avatar."),
                    _pendingSlotCount, 1, 10);
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            var component = GetOrRefreshComponent();

            if (component != null)
            {
                EditorGUILayout.HelpBox(
                    "✓ ASM-Lite prefab is present on this avatar.",
                    MessageType.Info);

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
                    "Configure settings above, then click \"Add ASM-Lite Prefab\".",
                    MessageType.Warning);
            }
        }

        private void DrawAddButton()
        {
            if (GUILayout.Button("Add ASM-Lite Prefab", GUILayout.Height(36)))
                AddPrefabToAvatar();
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private ASMLiteComponent GetOrRefreshComponent()
        {
            if (_selectedAvatar == null)
            {
                InvalidateComponentCache();
                return null;
            }

            var component = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);

            if (component != _cachedComponent)
            {
                _cachedComponent     = component;
                _serializedComponent = component != null ? new SerializedObject(component) : null;
                _slotCountProp       = _serializedComponent?.FindProperty("slotCount");
            }

            return _cachedComponent;
        }

        private void InvalidateComponentCache()
        {
            _cachedComponent     = null;
            _serializedComponent = null;
            _slotCountProp       = null;
        }

        private void AddPrefabToAvatar()
        {
            if (_selectedAvatar == null)
                return;

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

            ASMLitePrefabCreator.CreatePrefab();

            const string PrefabPath = "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab";
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

            // Apply the pending slot count the user configured before adding.
            var component = instance.GetComponent<ASMLiteComponent>();
            if (component != null)
                component.slotCount = _pendingSlotCount;

            Undo.RegisterCreatedObjectUndo(instance, "Add ASM-Lite Prefab");
            Undo.CollapseUndoOperations(group);

            InvalidateComponentCache();
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            Debug.Log($"[ASM-Lite] Prefab added to '{_selectedAvatar.gameObject.name}' with {_pendingSlotCount} slot(s).");
            Repaint();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

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
                InvalidateComponentCache();

                // Pre-populate from existing component if present.
                var existing = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
                if (existing != null)
                    _pendingSlotCount = existing.slotCount;

                Repaint();
            }
        }
    }
}
