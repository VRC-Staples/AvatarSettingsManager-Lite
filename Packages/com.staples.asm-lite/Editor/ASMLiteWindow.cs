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
    ///   • Slot count configuration (editable before add; locked after)
    ///   • Status / diagnostics panel
    ///   • "Add ASM-Lite Prefab" button — adds prefab and immediately bakes assets
    ///   • "Rebuild ASM-Lite" button — re-bakes when prefab already present
    /// </summary>
    public class ASMLiteWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private VRCAvatarDescriptor _selectedAvatar;
        private Vector2             _scrollPos;

        // Pending slot count — shown before the prefab is added, applied on add.
        private int _pendingSlotCount = 3;

        // Cached component reference — rebuilt when avatar or scene changes.
        private ASMLiteComponent _cachedComponent;

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
                DrawActionButton();
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
                _cachedComponent = null;

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
                // Prefab is present — slot count still editable, but a rebuild
                // is needed to apply changes to the generated assets.
                int newSlot = EditorGUILayout.IntSlider(
                    new GUIContent(
                        "Slot Count",
                        "Number of expression parameter slots ASM-Lite manages on this avatar."),
                    component.slotCount, 1, 10);

                if (newSlot != component.slotCount)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Slot Count");
                    component.slotCount = newSlot;
                    EditorUtility.SetDirty(component);
                }

                EditorGUILayout.HelpBox(
                    "Click \"Rebuild ASM-Lite\" to apply slot count changes.",
                    MessageType.None);
            }
            else
            {
                // No prefab yet — user can configure before adding.
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

        private void DrawActionButton()
        {
            var component = GetOrRefreshComponent();

            if (component != null)
            {
                if (GUILayout.Button("Rebuild ASM-Lite", GUILayout.Height(36)))
                    BakeAssets(component);
            }
            else
            {
                if (GUILayout.Button("Add ASM-Lite Prefab", GUILayout.Height(36)))
                    AddPrefabToAvatar();
            }
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        private ASMLiteComponent GetOrRefreshComponent()
        {
            if (_selectedAvatar == null)
            {
                _cachedComponent = null;
                return null;
            }

            // Re-check every frame in case the prefab was added/removed externally.
            _cachedComponent = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
            return _cachedComponent;
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

            var component = instance.GetComponent<ASMLiteComponent>();
            if (component != null)
                component.slotCount = _pendingSlotCount;

            Undo.RegisterCreatedObjectUndo(instance, "Add ASM-Lite Prefab");
            Undo.CollapseUndoOperations(group);

            _cachedComponent = null;
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            Debug.Log($"[ASM-Lite] Prefab added to '{_selectedAvatar.gameObject.name}' with {_pendingSlotCount} slot(s). Baking assets...");

            // Immediately bake so assets are populated before the user hits Play.
            component = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
            if (component != null)
                BakeAssets(component);

            Repaint();
        }

        private void BakeAssets(ASMLiteComponent component)
        {
            if (component == null)
                return;

            try
            {
                ASMLiteBuilder.Build(component);
                AssetDatabase.Refresh();
                Debug.Log($"[ASM-Lite] Assets baked for '{component.gameObject.name}'.");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "ASM-Lite: Build Error",
                    $"An error occurred while baking assets:\n\n{ex.Message}\n\nCheck the Console for details.",
                    "OK");
                Debug.LogException(ex);
            }

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
                _selectedAvatar  = descriptor;
                _cachedComponent = null;

                var existing = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
                if (existing != null)
                    _pendingSlotCount = existing.slotCount;

                Repaint();
            }
        }
    }
}
