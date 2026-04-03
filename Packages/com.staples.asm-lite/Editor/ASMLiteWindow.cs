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

        // Pending control scheme — shown before the prefab is added, applied on add.
        private ControlScheme _pendingControlScheme = ControlScheme.SafeBool;

        // Cached component reference — rebuilt when avatar or scene changes.
        private ASMLiteComponent _cachedComponent;

        // Cached LINQ Count() result — avoids per-repaint enumeration.
        // -1 means invalid; recomputed lazily in DrawStatus.
        private int _cachedCustomParamCount = -1;

        // Pending icon mode — shown before the prefab is added, applied on add.
        private IconMode _pendingIconMode = IconMode.SameColor;

        // Pending gear index — shown before the prefab is added, applied on add.
        private int _pendingSelectedGearIndex = 0;

        // Pending custom icons — shown before the prefab is added, applied on add.
        private Texture2D[] _pendingCustomIcons = new Texture2D[3];

        // Pending action icon mode — shown before the prefab is added, applied on add.
        private ActionIconMode _pendingActionIconMode = ActionIconMode.Default;

        // Pending custom action icons — used when _pendingActionIconMode is Custom.
        private Texture2D _pendingCustomSaveIcon;
        private Texture2D _pendingCustomLoadIcon;
        private Texture2D _pendingCustomClearIcon;

        // ── Banner ────────────────────────────────────────────────────────────

        private const string BannerPath = "Packages/com.staples.asm-lite/Icons/banner.png";
        private const float  BannerAspect = 1200f / 300f; // 4 : 1

        // Loaded once on first draw, never reloaded mid-session.
        private Texture2D _bannerTexture;

        // ── Static GUIContent ─────────────────────────────────────────────────

        private static readonly GUIContent s_slotCountLabelActive =
            new GUIContent("Slot Count",
                "How many preset slots your avatar has. Each slot can hold a full snapshot of your settings.");

        private static readonly GUIContent s_slotCountLabelPending =
            new GUIContent("Slot Count",
                "How many preset slots to add. Each slot lets you save and load a full set of avatar settings.");

        private static readonly GUIContent s_schemeLabelActive =
            new GUIContent("Control Scheme",
                "How slot buttons are wired up. SafeBool is simpler; CompactInt saves sync budget on avatars with lots of slots.");

        private static readonly GUIContent s_schemeLabelPending =
            new GUIContent("Control Scheme",
                "How slot buttons will be wired up. SafeBool is simpler; CompactInt saves sync budget on avatars with lots of slots.");

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/.Staples./ASM-Lite")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteWindow>(title: ".Staples. ASM-Lite");
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

                DrawAvatarPicker();

                if (_selectedAvatar != null)
                {
                    EditorGUILayout.Space(8);
                    DrawSettings();
                    EditorGUILayout.Space(8);
                    DrawIconMode();
                    EditorGUILayout.Space(8);
                    DrawActionIcons();
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
            // Load banner texture once — null after domain reload until first draw.
            if (_bannerTexture == null)
                _bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BannerPath);

            if (_bannerTexture != null)
            {
                // Scale to full available width, clamp height to preserve 4:1 aspect.
                float availableWidth = EditorGUIUtility.currentViewWidth - 4f; // 2px padding each side
                float bannerHeight   = Mathf.Round(availableWidth / BannerAspect);

                // Reserve layout space so the scroll view accounts for the banner height.
                Rect bannerRect = GUILayoutUtility.GetRect(availableWidth, bannerHeight,
                    GUILayout.ExpandWidth(true));

                // Draw flush to the left edge of the window.
                bannerRect.x      = 0f;
                bannerRect.width  = availableWidth + 4f;

                GUI.DrawTexture(bannerRect, _bannerTexture, ScaleMode.ScaleToFit, alphaBlend: false);
                EditorGUILayout.Space(4);
            }
            else
            {
                // Fallback when banner hasn't been imported yet.
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField(".Staples. ASM-Lite", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Avatar Settings Manager — Lite Edition", EditorStyles.miniLabel);
            }
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

            // ── Control scheme ──
            if (component)
            {
                var newScheme = (ControlScheme)EditorGUILayout.EnumPopup(
                    s_schemeLabelActive, component.controlScheme);
                if (newScheme != component.controlScheme)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Control Scheme");
                    component.controlScheme = newScheme;
                    EditorUtility.SetDirty(component);
                }
            }
            else
            {
                _pendingControlScheme = (ControlScheme)EditorGUILayout.EnumPopup(
                    s_schemeLabelPending, _pendingControlScheme);
            }

            // Scheme description HelpBox — resolve from whichever source is active
            var activeScheme = component ? component.controlScheme : _pendingControlScheme;
            string schemeDesc = activeScheme == ControlScheme.CompactInt
                ? "Compact (1 shared Int): Uses a single synced Int parameter for all slots.\nMaximum parameter budget savings — recommended for avatars with many other synced parameters."
                : "Safe (3 bools/slot): Uses 3 synced Bool parameters per slot.\nSimplest setup — recommended for avatars with a small parameter budget.";
            EditorGUILayout.HelpBox(schemeDesc, MessageType.None);
        }

        private void DrawIconMode()
        {
            var component = GetOrRefreshComponent();

            EditorGUILayout.LabelField("Icon Mode", EditorStyles.boldLabel);

            // Determine current mode and slot count based on whether component exists
            int currentSlotCount = component ? component.slotCount : _pendingSlotCount;
            IconMode currentMode = component ? component.iconMode : _pendingIconMode;
            int currentGearIndex = component ? component.selectedGearIndex : _pendingSelectedGearIndex;
            Texture2D[] currentCustomIcons = component ? component.customIcons : _pendingCustomIcons;

            // Always resize customIcons to match slotCount before any indexing.
            if (currentCustomIcons == null || currentCustomIcons.Length != currentSlotCount)
            {
                var resized = new Texture2D[currentSlotCount];
                if (currentCustomIcons != null)
                {
                    int copy = Mathf.Min(currentCustomIcons.Length, currentSlotCount);
                    System.Array.Copy(currentCustomIcons, resized, copy);
                }
                currentCustomIcons = resized;

                if (component)
                {
                    component.customIcons = resized;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingCustomIcons = resized;
                }
            }

            // Mode selector.
            var newMode = (IconMode)EditorGUILayout.EnumPopup("Icon Mode", currentMode);
            if (newMode != currentMode)
            {
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Icon Mode");
                    component.iconMode = newMode;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingIconMode = newMode;
                }
            }

            // Per-mode controls.
            switch (newMode)
            {
                case IconMode.SameColor:
                {
                    var colorNames = new[] { "Blue", "Red", "Green", "Purple", "Cyan", "Orange", "Pink", "Yellow" };
                    int newIndex = EditorGUILayout.Popup("Gear Color", currentGearIndex, colorNames);
                    if (newIndex != currentGearIndex)
                    {
                        if (component)
                        {
                            Undo.RecordObject(component, "Change ASM-Lite Gear Color");
                            component.selectedGearIndex = newIndex;
                            EditorUtility.SetDirty(component);
                        }
                        else
                        {
                            _pendingSelectedGearIndex = newIndex;
                        }
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
                    for (int i = 0; i < currentSlotCount; i++)
                    {
                        var newTex = (Texture2D)EditorGUILayout.ObjectField(
                            $"Slot {i + 1} Icon",
                            currentCustomIcons[i],
                            typeof(Texture2D),
                            allowSceneObjects: false);
                        if (newTex != currentCustomIcons[i])
                        {
                            currentCustomIcons[i] = newTex;
                            if (component)
                            {
                                Undo.RecordObject(component, "Change ASM-Lite Custom Icon");
                                component.customIcons[i] = newTex;
                                EditorUtility.SetDirty(component);
                            }
                            else
                            {
                                _pendingCustomIcons[i] = newTex;
                            }
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Draws the Action Icons section. Allows the user to choose between the
        /// bundled Save/Load/Clear Preset icons (Default) or custom Texture2D icons
        /// (Custom). Custom icons apply globally — the same three textures are used
        /// across all slot submenus.
        /// </summary>
        private void DrawActionIcons()
        {
            var component = GetOrRefreshComponent();

            EditorGUILayout.LabelField("Action Icons", EditorStyles.boldLabel);

            ActionIconMode currentMode = component ? component.actionIconMode : _pendingActionIconMode;

            var newMode = (ActionIconMode)EditorGUILayout.EnumPopup("Action Icon Mode", currentMode);
            if (newMode != currentMode)
            {
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Action Icon Mode");
                    component.actionIconMode = newMode;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingActionIconMode = newMode;
                }
            }

            if (newMode == ActionIconMode.Custom)
            {
                Texture2D currentSave  = component ? component.customSaveIcon  : _pendingCustomSaveIcon;
                Texture2D currentLoad  = component ? component.customLoadIcon  : _pendingCustomLoadIcon;
                Texture2D currentClear = component ? component.customClearIcon : _pendingCustomClearIcon;

                // Save icon
                var newSave = (Texture2D)EditorGUILayout.ObjectField(
                    "Save Icon", currentSave, typeof(Texture2D), allowSceneObjects: false);
                if (newSave != currentSave)
                {
                    if (component)
                    {
                        Undo.RecordObject(component, "Change ASM-Lite Save Icon");
                        component.customSaveIcon = newSave;
                        EditorUtility.SetDirty(component);
                    }
                    else { _pendingCustomSaveIcon = newSave; }
                }

                // Load icon
                var newLoad = (Texture2D)EditorGUILayout.ObjectField(
                    "Load Icon", currentLoad, typeof(Texture2D), allowSceneObjects: false);
                if (newLoad != currentLoad)
                {
                    if (component)
                    {
                        Undo.RecordObject(component, "Change ASM-Lite Load Icon");
                        component.customLoadIcon = newLoad;
                        EditorUtility.SetDirty(component);
                    }
                    else { _pendingCustomLoadIcon = newLoad; }
                }

                // Clear Preset icon
                var newClear = (Texture2D)EditorGUILayout.ObjectField(
                    "Clear Preset Icon", currentClear, typeof(Texture2D), allowSceneObjects: false);
                if (newClear != currentClear)
                {
                    if (component)
                    {
                        Undo.RecordObject(component, "Change ASM-Lite Clear Preset Icon");
                        component.customClearIcon = newClear;
                        EditorUtility.SetDirty(component);
                    }
                    else { _pendingCustomClearIcon = newClear; }
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

                int syncedBits = component.controlScheme == ControlScheme.CompactInt
                    ? 8
                    : 3 * component.slotCount;
                EditorGUILayout.HelpBox(
                    $"ASM-Lite uses {syncedBits} / 256 synced bits",
                    MessageType.Info);
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
                // Two-button layout: Rebuild and Remove
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Rebuild ASM-Lite", GUILayout.Height(36)))
                {
                    // Defer past the current OnGUI pass so AssetDatabase operations
                    // don't corrupt the layout group stack mid-frame.
                    var captured = component;
                    EditorApplication.delayCall += () => BakeAssets(captured);
                }

                if (GUILayout.Button("Remove Prefab", GUILayout.Height(36)))
                {
                    bool confirm = EditorUtility.DisplayDialog(
                        "Remove ASM-Lite Prefab",
                        "Are you sure you want to remove the ASM-Lite prefab from this avatar?\n\n" +
                        "Any unsaved changes will be lost, but your avatar and expression parameters will not be affected.",
                        "Remove", "Cancel");

                    if (confirm)
                    {
                        EditorApplication.delayCall += () => RemovePrefab(component);
                    }
                }

                EditorGUILayout.EndHorizontal();
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
            {
                component.slotCount = _pendingSlotCount;
                component.controlScheme = _pendingControlScheme;
                component.iconMode = _pendingIconMode;
                component.selectedGearIndex = _pendingSelectedGearIndex;
                component.actionIconMode = _pendingActionIconMode;
                component.customSaveIcon  = _pendingCustomSaveIcon;
                component.customLoadIcon  = _pendingCustomLoadIcon;
                component.customClearIcon = _pendingCustomClearIcon;

                // Resize and copy custom icons
                if (_pendingCustomIcons != null)
                {
                    component.customIcons = new Texture2D[_pendingCustomIcons.Length];
                    System.Array.Copy(_pendingCustomIcons, component.customIcons, _pendingCustomIcons.Length);
                }
            }

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

        private void RemovePrefab(ASMLiteComponent component)
        {
            if (component == null || component.gameObject == null)
                return;

            Undo.SetCurrentGroupName("Remove ASM-Lite Prefab");
            int group = Undo.GetCurrentGroup();

            var prefabRoot = component.gameObject;
            Undo.DestroyObjectImmediate(prefabRoot);

            Undo.CollapseUndoOperations(group);

            _cachedComponent  = null;
            _lastRefreshFrame = -1;

            Debug.Log("[ASM-Lite] Prefab removed from avatar.");
            Repaint();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the slot count and icon settings from the existing ASMLiteComponent on
        /// <see cref="_selectedAvatar"/> and stores them in the pending fields.
        /// Routes through the per-frame cache rather than calling GetComponentInChildren directly.
        /// </summary>
        private void SyncPendingSlotCountFromAvatar()
        {
            var existing = GetOrRefreshComponent();
            if (existing != null)
            {
                _pendingSlotCount = existing.slotCount;
                _pendingControlScheme = existing.controlScheme;
                _pendingIconMode = existing.iconMode;
                _pendingSelectedGearIndex = existing.selectedGearIndex;
                _pendingActionIconMode = existing.actionIconMode;
                _pendingCustomSaveIcon  = existing.customSaveIcon;
                _pendingCustomLoadIcon  = existing.customLoadIcon;
                _pendingCustomClearIcon = existing.customClearIcon;

                // Sync custom icons array
                if (existing.customIcons != null)
                {
                    _pendingCustomIcons = new Texture2D[existing.customIcons.Length];
                    System.Array.Copy(existing.customIcons, _pendingCustomIcons, existing.customIcons.Length);
                }
            }
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
