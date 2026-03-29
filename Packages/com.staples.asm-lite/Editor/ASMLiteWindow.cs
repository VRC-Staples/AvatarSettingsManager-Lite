using System.Linq;
using ASMLite;
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

        // Cached LINQ Count() result — avoids per-repaint enumeration.
        // -1 means invalid; recomputed lazily in DrawStatus.
        private int _cachedCustomParamCount = -1;

        // ── Static GUIContent ─────────────────────────────────────────────────

        private static readonly GUIContent s_slotCountLabelActive =
            new GUIContent("Slot Count",
                "Number of expression parameter slots ASM-Lite manages on this avatar.");

        private static readonly GUIContent s_slotCountLabelPending =
            new GUIContent("Slot Count",
                "Number of expression parameter slots ASM-Lite will manage on this avatar.");

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/.Staples./ASM-Lite")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteWindow>(title: "ASM-Lite");
            win.minSize = new Vector2(380, 480);
            win.Show();
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            try
            {
                DrawHeader();
                EditorGUILayout.Space(8);

                DrawAvatarPicker();

                if (_selectedAvatar != null)
                {
                    EditorGUILayout.Space(8);
                    DrawSettings();
                    EditorGUILayout.Space(8);
                    DrawIconMode();
                    EditorGUILayout.Space(8);
                    DrawStatus();
                    EditorGUILayout.Space(12);
                    DrawActionButton();
                }

                EditorGUILayout.Space(8);
            }
            catch (ExitGUIException) { throw; }
            catch (System.Exception ex)
            {
                // Swallow mid-draw exceptions so EndScrollView always runs.
                // The exception is logged so it's not silently lost.
                // Log only on Layout events to avoid flooding during Repaint passes.
                if (Event.current.type == EventType.Layout)
                    Debug.LogException(ex);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
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
                _cachedCustomParamCount = -1;

                if (_selectedAvatar != null)
                    SyncPendingSlotCountFromAvatar();

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

            // Use the Unity-aware null check (operator bool) — a C# != null check
            // passes for destroyed UnityEngine.Objects, which would throw on field access.
            if (component)
            {
                // Prefab is present — slot count still editable, but a rebuild
                // is needed to apply changes to the generated assets.
                int newSlot = EditorGUILayout.IntSlider(
                    s_slotCountLabelActive,
                    component.slotCount, 1, 8);

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
                    s_slotCountLabelPending,
                    _pendingSlotCount, 1, 8);
            }
        }

        private void DrawIconMode()
        {
            var component = GetOrRefreshComponent();

            if (!component)
                return;

            Undo.RecordObject(component, "Change ASM-Lite Icon Mode");

            EditorGUILayout.LabelField("Icon Mode", EditorStyles.boldLabel);

            // Always resize customIcons to match slotCount before any indexing.
            if (component.customIcons == null || component.customIcons.Length != component.slotCount)
            {
                var resized = new Texture2D[component.slotCount];
                if (component.customIcons != null)
                {
                    int copy = Mathf.Min(component.customIcons.Length, component.slotCount);
                    System.Array.Copy(component.customIcons, resized, copy);
                }
                component.customIcons = resized;
                EditorUtility.SetDirty(component);
            }

            // Mode selector.
            var newMode = (IconMode)EditorGUILayout.EnumPopup("Icon Mode", component.iconMode);
            if (newMode != component.iconMode)
            {
                component.iconMode = newMode;
                EditorUtility.SetDirty(component);
            }

            // Per-mode controls.
            switch (component.iconMode)
            {
                case IconMode.SameColor:
                {
                    var colorNames = new[] { "Blue", "Red", "Green", "Purple", "Cyan", "Orange", "Pink", "Yellow" };
                    int newIndex = EditorGUILayout.Popup("Gear Color", component.selectedGearIndex, colorNames);
                    if (newIndex != component.selectedGearIndex)
                    {
                        component.selectedGearIndex = newIndex;
                        EditorUtility.SetDirty(component);
                    }
                    break;
                }

                case IconMode.MultiColor:
                {
                    EditorGUILayout.HelpBox(
                        "Each slot gets a unique gear color.\nSlots 1\u20134: Blue, Red, Green, Purple\nSlots 5\u20138: Cyan, Orange, Pink, Yellow",
                        MessageType.None);
                    break;
                }

                case IconMode.Custom:
                {
                    for (int i = 0; i < component.slotCount; i++)
                    {
                        var newTex = (Texture2D)EditorGUILayout.ObjectField(
                            $"Slot {i + 1} Icon",
                            component.customIcons[i],
                            typeof(Texture2D),
                            allowSceneObjects: false);
                        if (newTex != component.customIcons[i])
                        {
                            component.customIcons[i] = newTex;
                            EditorUtility.SetDirty(component);
                        }
                    }
                    break;
                }
            }
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            var component = GetOrRefreshComponent();

            if (component)
            {
                EditorGUILayout.HelpBox(
                    "✓ ASM-Lite prefab is present on this avatar.",
                    MessageType.Info);

                // Guard against mid-reimport state: expressionParameters or its
                // parameters array can be transiently null while Unity is importing.
                try
                {
                    var exprParams = _selectedAvatar.expressionParameters;
                    if (exprParams != null && exprParams.parameters != null)
                    {
                        if (_cachedCustomParamCount < 0)
                        {
                            _cachedCustomParamCount = exprParams.parameters
                                .Count(p => !string.IsNullOrEmpty(p.name) && !p.name.StartsWith("ASMLite_"));
                        }

                        EditorGUILayout.HelpBox(
                            $"✓ {_cachedCustomParamCount} custom parameter(s) will be backed up across " +
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
                catch (System.Exception)
                {
                    // Asset is mid-reimport — show a neutral message and wait for
                    // the next repaint when it will be stable again.
                    EditorGUILayout.HelpBox(
                        "⚠ Expression parameters are currently being imported. Please wait.",
                        MessageType.Warning);
                    // Gate Repaint on Layout to avoid a busy-spin loop when the
                    // exception persists across multiple Repaint passes.
                    if (Event.current.type == EventType.Layout)
                        Repaint();
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

            if (component)
            {
                if (GUILayout.Button("Rebuild ASM-Lite", GUILayout.Height(36)))
                {
                    // Defer past the current OnGUI pass so AssetDatabase operations
                    // don't corrupt the layout group stack mid-frame.
                    var captured = component;
                    EditorApplication.delayCall += () => BakeAssets(captured);
                }
            }
            else
            {
                if (GUILayout.Button("Add ASM-Lite Prefab", GUILayout.Height(36)))
                {
                    // Defer past the current OnGUI pass — CreatePrefab calls
                    // AssetDatabase.Refresh() which can trigger re-entrant layout
                    // events and leave BeginScrollView unmatched.
                    EditorApplication.delayCall += AddPrefabToAvatar;
                }
            }
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        // Per-frame component cache — refreshed once per OnGUI call, not once per draw section.
        private int _lastRefreshFrame = -1;

        private ASMLiteComponent GetOrRefreshComponent()
        {
            if (!_selectedAvatar)
            {
                _cachedComponent = null;
                return null;
            }

            // Refresh once per editor frame. Multiple Draw* calls in the same OnGUI
            // invocation reuse the cached result — avoids 3× GetComponentInChildren
            // per repaint and ensures consistent state within a single frame.
            int frame = Time.frameCount;
            if (frame != _lastRefreshFrame)
            {
                _cachedComponent   = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
                _lastRefreshFrame  = frame;
            }

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

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);

            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "ASM-Lite: Error",
                    $"Could not load prefab at {ASMLiteAssetPaths.Prefab}.\nCheck the Console for details.",
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

            _cachedComponent  = null;
            _lastRefreshFrame = -1;   // force cache refresh on next draw
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            Debug.Log($"[ASM-Lite] Prefab added to '{_selectedAvatar.gameObject.name}' with {_pendingSlotCount} slot(s). Baking assets...");

            // Immediately bake so assets are populated before the user hits Play.
            // Invalidate and re-fetch through the cache so the component reference
            // is consistent with subsequent GetOrRefreshComponent() calls.
            _cachedComponent  = null;
            _lastRefreshFrame = -1;
            component = GetOrRefreshComponent();
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
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the slot count from the existing ASMLiteComponent on
        /// <see cref="_selectedAvatar"/> and stores it in <see cref="_pendingSlotCount"/>.
        /// Routes through the per-frame cache rather than calling GetComponentInChildren directly.
        /// </summary>
        private void SyncPendingSlotCountFromAvatar()
        {
            var existing = GetOrRefreshComponent();
            if (existing != null)
                _pendingSlotCount = existing.slotCount;
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
                _selectedAvatar  = descriptor;
                _cachedComponent = null;
                _cachedCustomParamCount = -1;
                SyncPendingSlotCountFromAvatar();

                Repaint();
            }
        }
    }
}
