using System;
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
    ///   • "Add ASM-Lite Prefab" button: adds prefab and immediately bakes assets
    ///   • "Rebuild ASM-Lite" button: re-bakes when prefab already present
    /// </summary>
    public class ASMLiteWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private VRCAvatarDescriptor _selectedAvatar;
        private Vector2             _scrollPos;

        // Pending slot count: shown before the prefab is added, applied on add.
        private int _pendingSlotCount = 3;

        // Cached component reference: rebuilt when avatar or scene changes.
        private ASMLiteComponent _cachedComponent;

        // Cached LINQ Count() result: avoids per-repaint enumeration.
        // -1 means invalid; recomputed lazily in DrawStatus.
        private int _cachedCustomParamCount = -1;

        // Parameter count returned by the last successful build (post-VRCFury clone).
        // -1 means no build has run yet this session.
        private int _discoveredParamCount = -1;

        // Pending icon mode: shown before the prefab is added, applied on add.
        private IconMode _pendingIconMode = IconMode.MultiColor;

        // Pending gear index: shown before the prefab is added, applied on add.
        private int _pendingSelectedGearIndex = 0;

        // Pending custom icons: shown before the prefab is added, applied on add.
        private Texture2D[] _pendingCustomIcons = new Texture2D[3];

        // Pending action icon mode: shown before the prefab is added, applied on add.
        private ActionIconMode _pendingActionIconMode = ActionIconMode.Default;

        // Pending custom action icons: used when _pendingActionIconMode is Custom.
        private Texture2D _pendingCustomSaveIcon;
        private Texture2D _pendingCustomLoadIcon;
        private Texture2D _pendingCustomClearIcon;

        // Pending customization scaffold state: copied into new prefab instances
        // and refreshed from the selected avatar component when present.
        private bool _pendingUseCustomRootIcon = false;
        private Texture2D _pendingCustomRootIcon;
        private bool _pendingUseCustomRootName = false;
        private string _pendingCustomRootName = string.Empty;
        private bool _pendingUseCustomInstallPath = false;
        private string _pendingCustomInstallPath = string.Empty;
        private bool _pendingUseParameterExclusions = false;
        private string[] _pendingExcludedParameterNames = Array.Empty<string>();


        // ── Wheel Preview Cache ───────────────────────────────────────────────

        // Resolved gear textures for the current settings. Rebuilt whenever
        // mode, color index, slot count, or custom icons change.
        private Texture2D[] _previewGearTextures;
        private Texture2D   _previewSaveIcon;
        private Texture2D   _previewLoadIcon;
        private Texture2D   _previewClearIcon;
        private Texture2D   _previewBackIcon;

        // Signature of the last preview build: used to detect staleness.
        private int    _previewSlotCount      = -1;
        private int    _previewIconMode       = -1;
        private int    _previewGearIndex      = -1;
        private int    _previewActionIconMode = -1;

        // Cached arrays for the main wheel. Rebuilt only when slot count or icons change.
        private Texture2D[] _mainWheelIcons;
        private string[]    _mainWheelLabels;

        // Sub-wheel arrays are invariant -- allocate once as static readonly.
        private static readonly string[] s_subWheelLabels = { "Back", "Save", "Load", "Clear" };

        // Fallback grey square drawn when a custom icon slot is unassigned.
        private Texture2D _previewFallback;

        // Bundled action icons: loaded once and held until domain reload.
        // These paths never change at runtime so there is no reason to reload
        // them on every preview cache invalidation.
        private Texture2D _cachedIconSave;
        private Texture2D _cachedIconLoad;
        private Texture2D _cachedIconClear;

        // ── Banner ────────────────────────────────────────────────────────────

        private const string BannerPath = "Packages/com.staples.asm-lite/Icons/banner.png";

        // ── Radial wheel style cache ──────────────────────────────────────────

        // Colors declared once -- Color is a struct but declaring as static readonly
        // makes the intent explicit and avoids accidental per-call reconstruction.
        private static readonly Color s_wheelColorMain   = new Color(0.14f, 0.18f, 0.20f);
        private static readonly Color s_wheelColorBorder = new Color(0.10f, 0.35f, 0.38f);
        private static readonly Color s_wheelColorInner  = new Color(0.21f, 0.24f, 0.27f);
        private static readonly Color s_separatorColor   = new Color(0.10f, 0.35f, 0.38f, 0.20f);

        // GUIStyle cached across repaints. Rebuilt lazily when null (domain reload).
        // Only fontSize is updated per call; cloning on every repaint is expensive.
        private GUIStyle _radialLabelStyle;
        private const float  BannerAspect = 1200f / 520f; // slightly shorter so the UI, not the banner, remains dominant

        // Loaded once on first draw, never reloaded mid-session.
        private Texture2D _bannerTexture;

        // ── Static GUIContent ─────────────────────────────────────────────────

        private static readonly GUIContent s_slotCountLabelActive =
            new GUIContent("Slot Count",
                "How many preset slots your avatar has. Each slot can hold a full snapshot of your settings.");

        private static readonly GUIContent s_slotCountLabelPending =
            new GUIContent("Slot Count",
                "How many preset slots to add. Each slot lets you save and load a full set of avatar settings.");

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/.Staples./ASM-Lite")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteWindow>(title: ".Staples. ASM-Lite");
            win.minSize = new Vector2(600, 680);
            win.Show();
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos,
                alwaysShowHorizontal: false,
                alwaysShowVertical: false,
                horizontalScrollbar: GUIStyle.none,
                verticalScrollbar: GUI.skin.verticalScrollbar,
                background: GUIStyle.none);

            try
            {
                DrawHeader();

                DrawAvatarPicker();

                if (_selectedAvatar != null)
                {
                    EditorGUILayout.Space(8);
                    DrawSettings();
                    SectionSeparator();
                    DrawIconSettingsSection();
                    SectionSeparator();
                    DrawStatus();
                    EditorGUILayout.Space(16);
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

        /// <summary>
        /// Draws a subtle 1px horizontal rule between sections.
        /// Provides visual boundary (ux-common-region-boundaries) without heavy chrome.
        /// </summary>
        private static void SectionSeparator()
        {
            EditorGUILayout.Space(6);
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, s_separatorColor);
            EditorGUILayout.Space(6);
        }

        private void DrawHeader()
        {
            // Load banner texture once: null after domain reload until first draw.
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
                EditorGUILayout.LabelField("Avatar Settings Manager: Lite Edition", EditorStyles.miniLabel);
            }
        }

        private void DrawAvatarPicker()
        {
            EditorGUILayout.Space(8);
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
                _discoveredParamCount = -1;

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

            // Use the Unity-aware null check (operator bool): a C# != null check
            // passes for destroyed UnityEngine.Objects, which would throw on field access.
            if (component)
            {
                // Prefab is present: slot count still editable, but a rebuild
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
                // No prefab yet: user can configure before adding.
                _pendingSlotCount = EditorGUILayout.IntSlider(
                    s_slotCountLabelPending,
                    _pendingSlotCount, 1, 8);
            }


        }

        private void DrawIconSettingsSection()
        {
            EditorGUILayout.LabelField("Icon Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical("box");
            DrawIconMode();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical("box");
            DrawActionIcons();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            DrawWheelPreview();
        }

        private void DrawIconMode()
        {
            var component = GetOrRefreshComponent();

            EditorGUILayout.LabelField("Slot Icons", EditorStyles.miniBoldLabel);

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
                        "Each slot gets a unique gear color.\nSlots 1-4: Blue, Red, Green, Purple\nSlots 5-8: Cyan, Orange, Pink, Yellow",
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
        /// (Custom). Custom icons apply globally: the same three textures are used
        /// across all slot submenus.
        /// </summary>
        private void DrawActionIcons()
        {
            var component = GetOrRefreshComponent();

            EditorGUILayout.LabelField("Action Icons", EditorStyles.miniBoldLabel);

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

        // ── Wheel Preview ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves and caches the icon textures used by the preview wheel.
        /// Exits immediately if the settings signature is unchanged.
        /// </summary>
        private void RefreshPreviewCache(
            int slotCount, IconMode iconMode, int gearIndex,
            ActionIconMode actionIconMode,
            Texture2D[] customIcons, Texture2D customSave, Texture2D customLoad, Texture2D customClear)
        {
            int modeInt       = (int)iconMode;
            int actionModeInt = (int)actionIconMode;

            bool dirty = _previewSlotCount      != slotCount
                      || _previewIconMode       != modeInt
                      || _previewGearIndex      != gearIndex
                      || _previewActionIconMode != actionModeInt;

            if (!dirty && _previewGearTextures != null
                && _previewGearTextures.Length == slotCount)
            {
                if (iconMode == IconMode.Custom && customIcons != null)
                {
                    for (int i = 0; i < slotCount; i++)
                    {
                        var expected = (i < customIcons.Length) ? customIcons[i] : null;
                        if (_previewGearTextures[i] != expected) { dirty = true; break; }
                    }
                }
                if (actionIconMode == ActionIconMode.Custom
                    && (_previewSaveIcon  != customSave
                     || _previewLoadIcon  != customLoad
                     || _previewClearIcon != customClear))
                    dirty = true;
            }
            else dirty = true;

            if (!dirty) return;

            if (_previewFallback == null)
            {
                _previewFallback = new Texture2D(1, 1);
                _previewFallback.SetPixel(0, 0, new Color(0.35f, 0.35f, 0.35f));
                _previewFallback.Apply();
            }

            _previewGearTextures = new Texture2D[slotCount];
            for (int slot = 1; slot <= slotCount; slot++)
            {
                Texture2D tex = null;
                switch (iconMode)
                {
                    case IconMode.SameColor:
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                            ASMLiteAssetPaths.GearIconPaths[gearIndex]);
                        break;
                    case IconMode.MultiColor:
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                            ASMLiteAssetPaths.GearIconPaths[(slot - 1) % ASMLiteAssetPaths.GearIconPaths.Length]);
                        break;
                    case IconMode.Custom:
                        int idx = slot - 1;
                        if (customIcons != null && idx < customIcons.Length)
                            tex = customIcons[idx];
                        break;
                }
                _previewGearTextures[slot - 1] = tex != null ? tex : _previewFallback;
            }

            if (actionIconMode == ActionIconMode.Custom)
            {
                _previewSaveIcon  = customSave  != null ? customSave  : _previewFallback;
                _previewLoadIcon  = customLoad  != null ? customLoad  : _previewFallback;
                _previewClearIcon = customClear != null ? customClear : _previewFallback;
            }
            else
            {
                // Load once and hold -- these paths never change. The ??= null check
                // also handles post-domain-reload resets (Unity clears instance fields).
                _cachedIconSave  ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconSave);
                _cachedIconLoad  ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconLoad);
                _cachedIconClear ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconReset);
                _previewSaveIcon  = _cachedIconSave  ?? _previewFallback;
                _previewLoadIcon  = _cachedIconLoad  ?? _previewFallback;
                _previewClearIcon = _cachedIconClear ?? _previewFallback;
            }

            _previewSlotCount      = slotCount;
            _previewIconMode       = modeInt;
            _previewGearIndex      = gearIndex;
            _previewActionIconMode = actionModeInt;
        }

        /// <summary>
        /// Draws a VRC-style radial menu preview: main slot wheel and inset
        /// Save/Load/Clear sub-wheel for slot 1.
        /// </summary>
        private void DrawWheelPreview()
        {
            var component = GetOrRefreshComponent();

            int            slotCount   = component ? component.slotCount          : _pendingSlotCount;
            IconMode       iconMode    = component ? component.iconMode            : _pendingIconMode;
            int            gearIndex   = component ? component.selectedGearIndex   : _pendingSelectedGearIndex;
            ActionIconMode actionMode  = component ? component.actionIconMode      : _pendingActionIconMode;
            Texture2D[]    customIcons = component ? component.customIcons         : _pendingCustomIcons;
            Texture2D      customSave  = component ? component.customSaveIcon      : _pendingCustomSaveIcon;
            Texture2D      customLoad  = component ? component.customLoadIcon      : _pendingCustomLoadIcon;
            Texture2D      customClear = component ? component.customClearIcon     : _pendingCustomClearIcon;

            RefreshPreviewCache(slotCount, iconMode, gearIndex, actionMode,
                customIcons, customSave, customLoad, customClear);

            // Load back arrow once.
            if (_previewBackIcon == null)
                _previewBackIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    ASMLiteAssetPaths.IconBackArrow) ?? _previewFallback;

            EditorGUILayout.LabelField("Expression Menu Preview", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "Preview of generated menu icon placement.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            float availWidth = EditorGUIUtility.currentViewWidth - 32f;

            // GestureManager canonical size is 300px. Keep the preview one step
            // smaller than the settings controls so it reads as confirmation,
            // not the primary focal point.
            float mainSize = Mathf.Clamp(availWidth * 0.46f, 150f, 260f);
            float subSize  = Mathf.Round(mainSize * 0.46f);

            Rect rowRect = GUILayoutUtility.GetRect(availWidth, mainSize + subSize * 0.35f + 4f);

            // Main wheel: left-center.
            Rect mainRect = new Rect(
                rowRect.x + availWidth * 0.24f - mainSize * 0.5f,
                rowRect.y,
                mainSize, mainSize);

            // Sub-wheel: offset down and right so it reads as a drill-down from the main wheel.
            Rect subRect = new Rect(
                rowRect.x + availWidth * 0.63f,
                rowRect.y + mainSize * 0.36f,
                subSize, subSize);

            if (Event.current.type != EventType.Repaint)
                return;

            // Main wheel: Back at top, then user slot icons.
            // Rebuild cached arrays only when size or content changed (handled by
            // RefreshPreviewCache above which sets _previewSlotCount when dirty).
            if (_mainWheelIcons == null || _mainWheelIcons.Length != slotCount + 1
                || _mainWheelLabels == null || _mainWheelLabels.Length != slotCount + 1)
            {
                _mainWheelIcons  = new Texture2D[slotCount + 1];
                _mainWheelLabels = new string[slotCount + 1];
                _mainWheelLabels[0] = "Back";
                for (int i = 0; i < slotCount; i++)
                    _mainWheelLabels[i + 1] = $"Slot {i + 1}";
            }
            _mainWheelIcons[0] = _previewBackIcon;
            for (int i = 0; i < slotCount; i++)
                _mainWheelIcons[i + 1] = _previewGearTextures[i];

            DrawRadialWheel(mainRect, _mainWheelIcons, _mainWheelLabels);

            // Sub-wheel: Back at top, then Save/Load/Clear.
            // Icon array must be rebuilt each repaint (action icons can change), but
            // the label array is static readonly -- no per-frame allocation.
            var subIcons = new[] { _previewBackIcon, _previewSaveIcon, _previewLoadIcon, _previewClearIcon };
            DrawRadialWheel(subRect, subIcons, s_subWheelLabels);

            // Connector line.
            var origHandles = Handles.color;
            Handles.color = new Color(s_wheelColorBorder.r, s_wheelColorBorder.g, s_wheelColorBorder.b, 0.6f);
            Handles.DrawLine(
                new Vector3(mainRect.xMax, mainRect.center.y),
                new Vector3(subRect.xMin,  subRect.center.y));
            Handles.color = origHandles;
        }

        /// <summary>
        /// Draws a VRC-style radial menu wheel using GestureManager exact dimensions and colors.
        /// Slot 0 is always at the top (12 o'clock) and goes clockwise.
        /// </summary>
        private void DrawRadialWheel(Rect rect, Texture2D[] icons, string[] labels)
        {
            int count = icons.Length;
            if (count == 0) return;

            float cx = rect.center.x;
            float cy = rect.center.y;

            // GestureManager: Size=300, radius=150. Scale proportionally.
            float scale      = rect.width / 300f;
            float outerR     = rect.width * 0.5f;         // 150px at 300
            float innerR     = rect.width / 6f;            // 50px at 300 (InnerSize/2 = 100/2)
            float iconRadius = rect.width / 3f;            // 100px at 300 (Size/3)
            float iconSize   = rect.width * 0.22f;         // ~66px at 300
            float halfIcon   = iconSize * 0.5f;

            var origColor   = GUI.color;
            var origHandles = Handles.color;

            // Background fill (full circle approximated by square: Handles clips it).
            GUI.color = s_wheelColorMain;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = origColor;

            // Outer ring.
            Handles.color = s_wheelColorBorder;
            Handles.DrawWireDisc(new Vector3(cx, cy), Vector3.forward, outerR - 1f);

            // Segment dividers: 2px lines between icons, offset by half a step.
            float angleStep = 360f / count;
            Handles.color = new Color(s_wheelColorBorder.r, s_wheelColorBorder.g, s_wheelColorBorder.b, 0.55f);
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Deg2Rad * (i * angleStep - 90f + angleStep * 0.5f);
                var p0 = new Vector3(cx + Mathf.Cos(a) * innerR,       cy + Mathf.Sin(a) * innerR,       0f);
                var p1 = new Vector3(cx + Mathf.Cos(a) * (outerR - 1f), cy + Mathf.Sin(a) * (outerR - 1f), 0f);
                Handles.DrawLine(p0, p1);
            }

            // Inner hub circle (RadialInner color, with teal border).
            Handles.color = s_wheelColorInner;
            Handles.DrawSolidDisc(new Vector3(cx, cy), Vector3.forward, innerR);
            Handles.color = s_wheelColorBorder;
            Handles.DrawWireDisc(new Vector3(cx, cy), Vector3.forward, innerR);

            // Icons and labels.
            // Reuse cached GUIStyle -- only update fontSize (depends on scale).
            if (_radialLabelStyle == null)
            {
                _radialLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    normal    = { textColor = new Color(1f, 1f, 1f, 0.82f) }
                };
            }
            _radialLabelStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(8f * scale));
            var labelStyle = _radialLabelStyle;

            for (int i = 0; i < count; i++)
            {
                float angleRad = Mathf.Deg2Rad * (i * angleStep - 90f);
                float ix = cx + Mathf.Cos(angleRad) * iconRadius;
                float iy = cy + Mathf.Sin(angleRad) * iconRadius;

                Rect iconRect = new Rect(ix - halfIcon, iy - halfIcon, iconSize, iconSize);
                GUI.color = Color.white;
                GUI.DrawTexture(iconRect, icons[i] ?? _previewFallback, ScaleMode.ScaleToFit, alphaBlend: true);

                if (labels != null && i < labels.Length)
                {
                    float lblWidth = iconSize * 2.2f;
                    Rect lblRect = new Rect(ix - lblWidth * 0.5f, iy + halfIcon + 1f, lblWidth, 14f);
                    GUI.Label(lblRect, labels[i], labelStyle);
                }
            }

            Handles.color = origHandles;
            GUI.color     = origColor;
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
                    if (_discoveredParamCount >= 0)
                    {
                        // Post-build count: includes VRCFury Toggle/FullController params.
                        EditorGUILayout.HelpBox(
                            $"✓ {_discoveredParamCount} custom parameter(s) backed up across " +
                            $"{component.slotCount} slot(s).",
                            MessageType.Info);
                    }
                    else
                    {
                        var exprParams = _selectedAvatar.expressionParameters;
                        if (exprParams != null && exprParams.parameters != null)
                        {
                            if (_cachedCustomParamCount < 0)
                            {
                                _cachedCustomParamCount = exprParams.parameters
                                    .Count(p => !string.IsNullOrEmpty(p.name) && !p.name.StartsWith("ASMLite_", StringComparison.Ordinal));
                            }

                            EditorGUILayout.HelpBox(
                                $"✓ {_cachedCustomParamCount} custom parameter(s) detected: rebuild to include VRCFury parameters.",
                                MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "⚠ No VRCExpressionParameters asset assigned on avatar descriptor.",
                                MessageType.Warning);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // Asset is mid-reimport: show a neutral message and wait for
                    // the next repaint when it will be stable again.
                    // Log unexpected exceptions (non-reimport bugs would otherwise
                    // be permanently hidden behind this UI message).
                    if (Event.current.type == EventType.Layout)
                    {
                        Debug.LogWarning($"[ASM-Lite] Expression parameters draw failed: {ex.GetType().Name}: {ex.Message}");
                        Repaint();
                    }
                    EditorGUILayout.HelpBox(
                        "⚠ Expression parameters are currently being imported. Please wait.",
                        MessageType.Warning);
                }

                DrawToggleBrokerStatus();
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "ASM-Lite prefab has not been added to this avatar yet.\n" +
                    "Configure settings above, then click \"Add ASM-Lite Prefab\".",
                    MessageType.Warning);
            }
        }

        private void DrawToggleBrokerStatus()
        {
            if (!ASMLiteToggleNameBroker.TryGetLatestEnrollmentReport(out var report))
                return;

            int totalAdjustments = report.PreflightCollisionAdjustments + report.CandidateCollisionAdjustments;
            if (totalAdjustments > 0)
            {
                EditorGUILayout.HelpBox(
                    $"[Toggle Broker] Last enrollment reserved {report.PreReservedNameCount} descriptor name(s) and adjusted deterministic assignments: preflight={report.PreflightCollisionAdjustments}, intra-candidate={report.CandidateCollisionAdjustments}.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"[Toggle Broker] Last enrollment reserved {report.PreReservedNameCount} descriptor name(s) with no deterministic suffix adjustments needed.",
                MessageType.None);
        }

        private void DrawActionButton()
        {
            var component = GetOrRefreshComponent();

            if (component)
            {
                // Two-button layout: Rebuild and Remove
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Rebuild ASM-Lite", GUILayout.Height(36), GUILayout.MinWidth(220)))
                {
                    // Defer past the current OnGUI pass so AssetDatabase operations
                    // don't corrupt the layout group stack mid-frame.
                    var captured = component;
                    EditorApplication.delayCall += () => BakeAssets(captured);
                }

                var prevColor = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                bool removeClicked = GUILayout.Button("Remove Prefab", GUILayout.Height(32), GUILayout.MinWidth(110));
                GUI.color = prevColor;
                if (removeClicked)
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
                    // Defer past the current OnGUI pass: CreatePrefab calls
                    // AssetDatabase.Refresh() which can trigger re-entrant layout
                    // events and leave BeginScrollView unmatched.
                    EditorApplication.delayCall += AddPrefabToAvatar;
                }
            }
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        // Per-frame component cache: refreshed once per OnGUI call, not once per draw section.
        private int _lastRefreshFrame = -1;

        private ASMLiteComponent GetOrRefreshComponent()
        {
            if (!_selectedAvatar)
            {
                _cachedComponent = null;
                return null;
            }

            // Refresh once per editor frame. Multiple Draw* calls in the same OnGUI
            // invocation reuse the cached result: avoids 3× GetComponentInChildren
            // per repaint and ensures consistent state within a single frame.
            int frame = Time.frameCount;
            if (frame != _lastRefreshFrame)
            {
                _cachedComponent   = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
                _lastRefreshFrame  = frame;
            }

            return _cachedComponent;
        }

        private static Texture2D[] CloneTextures(Texture2D[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Texture2D>();

            var clone = new Texture2D[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static string NormalizeOptionalString(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string[] SanitizeExcludedParameterNames(string[] names)
        {
            if (names == null || names.Length == 0)
                return Array.Empty<string>();

            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private void CopyPendingCustomizationToComponent(ASMLiteComponent component)
        {
            if (component == null)
                return;

            component.slotCount = _pendingSlotCount;
            component.iconMode = _pendingIconMode;
            component.selectedGearIndex = _pendingSelectedGearIndex;
            component.actionIconMode = _pendingActionIconMode;
            component.customSaveIcon = _pendingCustomSaveIcon;
            component.customLoadIcon = _pendingCustomLoadIcon;
            component.customClearIcon = _pendingCustomClearIcon;
            component.customIcons = CloneTextures(_pendingCustomIcons);

            component.useCustomRootIcon = _pendingUseCustomRootIcon;
            component.customRootIcon = _pendingCustomRootIcon;
            component.useCustomRootName = _pendingUseCustomRootName;
            component.customRootName = NormalizeOptionalString(_pendingCustomRootName);
            component.useCustomInstallPath = _pendingUseCustomInstallPath;
            component.customInstallPath = NormalizeOptionalString(_pendingCustomInstallPath);
            component.useParameterExclusions = _pendingUseParameterExclusions;
            component.excludedParameterNames = SanitizeExcludedParameterNames(_pendingExcludedParameterNames);
        }

        private void CopyComponentCustomizationToPending(ASMLiteComponent component)
        {
            _pendingSlotCount = component.slotCount;
            _pendingIconMode = component.iconMode;
            _pendingSelectedGearIndex = component.selectedGearIndex;
            _pendingActionIconMode = component.actionIconMode;
            _pendingCustomSaveIcon = component.customSaveIcon;
            _pendingCustomLoadIcon = component.customLoadIcon;
            _pendingCustomClearIcon = component.customClearIcon;
            _pendingCustomIcons = CloneTextures(component.customIcons);

            _pendingUseCustomRootIcon = component.useCustomRootIcon;
            _pendingCustomRootIcon = component.customRootIcon;
            _pendingUseCustomRootName = component.useCustomRootName;
            _pendingCustomRootName = NormalizeOptionalString(component.customRootName);
            _pendingUseCustomInstallPath = component.useCustomInstallPath;
            _pendingCustomInstallPath = NormalizeOptionalString(component.customInstallPath);
            _pendingUseParameterExclusions = component.useParameterExclusions;
            _pendingExcludedParameterNames = SanitizeExcludedParameterNames(component.excludedParameterNames);
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
                CopyPendingCustomizationToComponent(component);

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

            // Check for stale prms entry from pre-1.0.5 prefab instances. If present,
            // destroy the old instance and re-add a fresh prefab so the double-path
            // that produces 2 extra synced parameters is removed before baking.
            if (ASMLitePrefabCreator.HasStalePrmsEntry(component.gameObject))
            {
                Debug.Log("[ASM-Lite] Stale prms entry detected on prefab instance (pre-1.0.5). Replacing with current prefab to remove the double-registration path.");

                // Capture settings before destroying the instance.
                int savedSlotCount = component.slotCount;
                IconMode savedIconMode = component.iconMode;
                int savedGearIndex = component.selectedGearIndex;
                ActionIconMode savedActionIconMode = component.actionIconMode;
                Texture2D savedCustomSave = component.customSaveIcon;
                Texture2D savedCustomLoad = component.customLoadIcon;
                Texture2D savedCustomClear = component.customClearIcon;
                Texture2D[] savedCustomIcons = CloneTextures(component.customIcons);
                bool savedUseCustomRootIcon = component.useCustomRootIcon;
                Texture2D savedCustomRootIcon = component.customRootIcon;
                bool savedUseCustomRootName = component.useCustomRootName;
                string savedCustomRootName = NormalizeOptionalString(component.customRootName);
                bool savedUseCustomInstallPath = component.useCustomInstallPath;
                string savedCustomInstallPath = NormalizeOptionalString(component.customInstallPath);
                bool savedUseParameterExclusions = component.useParameterExclusions;
                string[] savedExcludedParameterNames = SanitizeExcludedParameterNames(component.excludedParameterNames);
                Transform savedParent = component.gameObject.transform.parent;

                Undo.SetCurrentGroupName("Rebuild ASM-Lite (migration)");
                int group = Undo.GetCurrentGroup();

                Undo.DestroyObjectImmediate(component.gameObject);

                ASMLitePrefabCreator.CreatePrefab();
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);
                if (prefabAsset == null)
                {
                    Debug.LogError("[ASM-Lite] Could not load refreshed prefab after migration. Aborting rebuild.");
                    return;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, savedParent);
                var newComponent = instance.GetComponent<ASMLiteComponent>();
                if (newComponent != null)
                {
                    newComponent.slotCount = savedSlotCount;
                    newComponent.iconMode = savedIconMode;
                    newComponent.selectedGearIndex = savedGearIndex;
                    newComponent.actionIconMode = savedActionIconMode;
                    newComponent.customSaveIcon = savedCustomSave;
                    newComponent.customLoadIcon = savedCustomLoad;
                    newComponent.customClearIcon = savedCustomClear;
                    newComponent.customIcons = savedCustomIcons;
                    newComponent.useCustomRootIcon = savedUseCustomRootIcon;
                    newComponent.customRootIcon = savedCustomRootIcon;
                    newComponent.useCustomRootName = savedUseCustomRootName;
                    newComponent.customRootName = savedCustomRootName;
                    newComponent.useCustomInstallPath = savedUseCustomInstallPath;
                    newComponent.customInstallPath = savedCustomInstallPath;
                    newComponent.useParameterExclusions = savedUseParameterExclusions;
                    newComponent.excludedParameterNames = savedExcludedParameterNames;
                }

                Undo.RegisterCreatedObjectUndo(instance, "Rebuild ASM-Lite (migration)");
                Undo.CollapseUndoOperations(group);

                _cachedComponent  = null;
                _lastRefreshFrame = -1;
                component = GetOrRefreshComponent();

                if (component == null)
                {
                    Debug.LogError("[ASM-Lite] Could not find component after migration. Aborting rebuild.");
                    return;
                }

                Debug.Log("[ASM-Lite] Migration complete. Continuing with bake.");
            }

            try
            {
                // Rebuild-prep contract for the reverted VF delivery path:
                // 1) collapse duplicate stale VF.Model.VRCFury components, preserving one;
                // 2) strip only direct-injection-era descriptor remnants (ASMLite_ namespace)
                //    so rebuild input reflects generated assets + VF wiring only.
                var migrationReport = ASMLiteBuilder.PrepareRevertedDeliveryRebuild(component);

                int count = ASMLiteBuilder.Build(component);
                if (count >= 0)
                    _discoveredParamCount = count;
                AssetDatabase.Refresh();
                Debug.Log($"[ASM-Lite] Assets baked for '{component.gameObject.name}' via generated assets + VRCFury FullController wiring. migrationRemoved={migrationReport.StaleVrcFuryRemoved}, cleanupFxLayers={migrationReport.Cleanup.FxLayersRemoved}, cleanupFxParams={migrationReport.Cleanup.FxParamsRemoved}, cleanupExprParams={migrationReport.Cleanup.ExprParamsRemoved}, cleanupMenuControls={migrationReport.Cleanup.MenuControlsRemoved}.");
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

            // Remove-path cleanup strips only direct-injection-era descriptor remnants.
            ASMLiteBuilder.CleanupReport removeCleanupReport = default;
            if (_selectedAvatar != null)
                removeCleanupReport = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_selectedAvatar);

            Undo.SetCurrentGroupName("Remove ASM-Lite Prefab");
            int group = Undo.GetCurrentGroup();

            var prefabRoot = component.gameObject;
            Undo.DestroyObjectImmediate(prefabRoot);

            Undo.CollapseUndoOperations(group);

            _cachedComponent  = null;
            _lastRefreshFrame = -1;

            Debug.Log($"[ASM-Lite] Prefab removed from avatar. cleanupFxLayers={removeCleanupReport.FxLayersRemoved}, cleanupFxParams={removeCleanupReport.FxParamsRemoved}, cleanupExprParams={removeCleanupReport.ExprParamsRemoved}, cleanupMenuControls={removeCleanupReport.MenuControlsRemoved}.");
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
                CopyComponentCustomizationToPending(existing);
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
                _discoveredParamCount = -1;
                SyncPendingSlotCountFromAvatar();

                Repaint();
            }
        }
    }
}
