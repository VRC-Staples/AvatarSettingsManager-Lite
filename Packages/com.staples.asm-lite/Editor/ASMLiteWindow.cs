using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASMLite;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

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

        // Parameter count returned by the last successful build (post-VRCFury clone).
        // -1 means no build has run yet this session.
        private int _discoveredParamCount = -1;

        // Pending icon mode: shown before the prefab is added, applied on add.
        private IconMode _pendingIconMode = IconMode.MultiColor;

        // Pending gear index: shown before the prefab is added, applied on add.
        private int _pendingSelectedGearIndex = 0;

        // Pending custom slot-icon toggle/icons: shown before prefab is added, applied on add.
        private bool _pendingUseCustomSlotIcons = false;
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
        private string[] _pendingCustomPresetNames = Array.Empty<string>();
        private string _pendingCustomPresetNameFormat = string.Empty;
        private string _pendingCustomSaveLabel = string.Empty;
        private string _pendingCustomLoadLabel = string.Empty;
        private string _pendingCustomClearPresetLabel = string.Empty;
        private string _pendingCustomConfirmLabel = string.Empty;
        private bool _pendingUseCustomInstallPath = false;
        private string _pendingCustomInstallPath = string.Empty;
        private bool _pendingUseParameterExclusions = false;
        private string[] _pendingExcludedParameterNames = Array.Empty<string>();

        // Icon settings foldouts (hierarchy-style UI groups).
        private bool _iconsRootFoldout = true;
        private bool _iconsActionFoldout = true;
        private bool _iconsSlotFoldout = true;

        // Action hierarchy disclosure state. Advanced maintenance actions stay hidden
        // until explicitly expanded by the user.
        [SerializeField] private bool _showAdvancedActions;

        // ── Install Path Tree ─────────────────────────────────────────────────

        // Which nodes in the install-path tree are expanded (keyed by full path).
        private readonly HashSet<string> _expandedInstallPaths = new HashSet<string>(StringComparer.Ordinal);

        // Scroll position for the install-path tree view.
        private Vector2 _installPathTreeScrollPos;

        // User-draggable height of the install-path tree scroll area.
        private float _installPathTreeHeight = 240f;

        // True while the user is dragging the tree resize handle.
        private bool _isDraggingTreeResize;

        // ── Parameter Checklist ───────────────────────────────────────────────

        private Vector2 _paramChecklistScrollPos;
        private float _paramChecklistHeight = 160f;
        private bool _isDraggingParamResize;
        private string[] _cachedParamList;
        private VRCAvatarDescriptor _lastParamListAvatar;
        private ParamTreeNode _cachedParamTree;
        private readonly HashSet<string> _expandedParamMenuPaths = new HashSet<string>(StringComparer.Ordinal);

        // Cached tree; rebuilt when the selected avatar changes.
        private MenuTreeNode _cachedInstallPathTree;
        private VRCAvatarDescriptor _lastInstallPathTreeAvatar;

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

        // Sub-wheel arrays are invariant. Allocate once as static readonly.
        private static readonly string s_subWheelBackLabel = "Back";


        // Fallback grey square drawn when a custom icon slot is unassigned.
        private Texture2D _previewFallback;

        // Bundled action icons: loaded once and held until domain reload.
        // These paths never change at runtime so there is no reason to reload
        // them on every preview cache invalidation.
        private Texture2D _cachedIconSave;
        private Texture2D _cachedIconLoad;
        private Texture2D _cachedIconClear;
        private Texture2D _cachedFlowArrow;

        // ── Banner ────────────────────────────────────────────────────────────

        private const string BannerPath = "Packages/com.staples.asm-lite/Icons/banner.png";
        private const float BannerMaxDrawWidth = 1200f;
        private const float SectionGap = 12f;

        // ── Radial wheel style cache ──────────────────────────────────────────

        // Colors declared once. Color is a struct, and static readonly keeps intent explicit.
        // makes the intent explicit and avoids accidental per-call reconstruction.
        private static readonly Color s_wheelColorMain   = new Color(0.14f, 0.18f, 0.20f);
        private static readonly Color s_wheelColorBorder = new Color(0.10f, 0.35f, 0.38f);
        private static readonly Color s_wheelColorInner  = new Color(0.21f, 0.24f, 0.27f);
        private static readonly Color s_separatorColor   = new Color(0.10f, 0.35f, 0.38f, 0.20f);
        private static readonly Color s_sectionBorderColor = new Color(0.10f, 0.35f, 0.38f, 0.32f);
        private static readonly Color s_sectionTintColor = new Color(0.12f, 0.16f, 0.18f, 0.12f);

        // GUIStyle cached across repaints. Rebuilt lazily when null (domain reload).
        // Only fontSize is updated per call; cloning on every repaint is expensive.
        private GUIStyle _radialLabelStyle;
        private GUIStyle _sectionCardStyle;
        private GUIStyle _sectionContentStyle;
        // Loaded once on first draw, never reloaded mid-session.
        private Texture2D _bannerTexture;

        // ── Static GUIContent ─────────────────────────────────────────────────

        private const string SlotCountTooltipActive =
            "How many presets your avatar has. Each preset can hold a full snapshot of your settings.";

        private const string SlotCountTooltipPending =
            "How many presets to add. Each preset lets you save and load a full set of avatar settings.";

        private const string SlotColorLegendHelpText =
            "Each preset uses a different gear color for quick visual scanning.\nPresets 1 to 4: Blue, Red, Green, Purple\nPresets 5 to 8: Cyan, Orange, Pink, Yellow";

        private const string PreviewFlowSubtitle = "Flow: Root Menu → Presets Menu → Action Submenu";
        private const string PreviewMiddleDialTitle = "Presets Menu";

        private const string StatusPackageManagedText =
            "Status: Ready to edit. ASM-Lite is attached to this avatar and can be updated here.";

        private const string StatusVendorizedAttachedText =
            "Status: Vendorized. ASM-Lite is still editable, and generated files are also copied to Assets/ASM-Lite.";

        private const string StatusVendorizedDetachedText =
            "Status: Vendorized. This avatar is using ASM-Lite files copied under Assets/ASM-Lite, but the editable ASM-Lite object is not attached.";

        private const string StatusDetachedText =
            "Status: Baked only. This avatar has ASM-Lite data, but the editable ASM-Lite object is not attached.";

        private const string StatusNotInstalledText =
            "Status: Not installed. ASM-Lite has not been added to this avatar yet.";

        private const string AttachedComponentInfoText = "✓ ASM-Lite is attached to this avatar.";

        private const string AttachedCountSummaryFormat =
            "✓ {0} custom parameter(s) are being saved across {1} preset(s).";

        private const string DescriptorCountSourceText =
            "This count is based on the avatar settings currently loaded in the descriptor.";

        private const string MissingExpressionParametersWarningText =
            "⚠ This avatar has no Expression Parameters asset assigned yet.";

        private const string ParameterImportPendingWarningText =
            "⚠ Avatar parameter data is still importing in Unity. Please wait a moment.";

        private const string ToggleBrokerCollisionWarningFormat =
            "[Toggle Broker] Last setup reserved {0} name(s) and auto-adjusted conflicting names: preflight={1}, intra-candidate={2}.";

        private const string ToggleBrokerNoCollisionInfoFormat =
            "[Toggle Broker] Last setup reserved {0} name(s). No naming conflicts needed adjustment.";

        private const string DetachedOrVendorizedNoComponentText =
            "ASM-Lite is in baked-only mode on this avatar. Use the option below to return to editable package mode.";

        private const string NotInstalledNoComponentText =
            "ASM-Lite is not on this avatar yet.\nSet your options above, then click \"Add ASM-Lite Prefab\".";

        private const string DetachDescriptionText =
            "Keep your current in-game preset data working, but remove the ASM-Lite tool object from this avatar. Great for sharing a finished avatar. You won’t be able to tweak ASM-Lite settings unless you add it again.";

        private const string ChangedPresetCountHelpText =
            "Changed preset count? Click \"Rebuild ASM-Lite\" to apply it.";

        private const string PresetIconsFoldoutTitle = "Preset Icons";
        private const string PresetNameLabelFormat = "Preset {0}";
        private const string ClearPresetLabel = "Clear Preset";
        private const string PresetIconFieldLabelFormat = "Preset {0} Icon";
        private const string ClearPresetIconFieldLabel = "Clear Preset Icon";
        private const string RootMenuFieldLabel = "Root Menu";
        private const string SaveFieldLabel = "Save";
        private const string LoadFieldLabel = "Load";
        private const string ConfirmFieldLabel = "Confirm";
        private const string NameFallbackGuidanceText = "Leave any name field blank to use ASM-Lite's default menu name for that item.";
        private const string NamingSectionRootHeader = "Root Menu Name";
        private const string NamingSectionPresetHeader = "Preset Names";
        private const string NamingSectionActionHeader = "Action Labels";

        private const string PresetIconOverridesHelpText =
            "A preset icon set here overrides the selected Icon Mode for that preset only.\nEmpty presets keep the normal Icon Mode icon.";

        private static readonly GUIContent s_slotCountLabelActive =
            new GUIContent("Preset Count", SlotCountTooltipActive);

        private static readonly GUIContent s_slotCountLabelPending =
            new GUIContent("Preset Count", SlotCountTooltipPending);

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
            // Draw the banner outside the scroll view so it sits flush with the window top.
            DrawHeader();

            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos,
                alwaysShowHorizontal: false,
                alwaysShowVertical: false,
                horizontalScrollbar: GUIStyle.none,
                verticalScrollbar: GUI.skin.verticalScrollbar,
                background: GUIStyle.none);

            try
            {
                BeginSectionCard();
                DrawAvatarPicker();
                EndSectionCard();

                if (_selectedAvatar != null)
                {
                    EditorGUILayout.Space(SectionGap);

                    BeginSectionCard();
                    DrawSettings();
                    EndSectionCard();

                    SectionSeparator();

                    BeginSectionCard();
                    DrawIconSettingsSection();
                    EndSectionCard();

                    SectionSeparator();

                    BeginSectionCard();
                    DrawCustomizeSection();
                    EndSectionCard();

                    SectionSeparator();

                    BeginSectionCard();
                    DrawStatus();
                    EndSectionCard();

                    EditorGUILayout.Space(SectionGap);

                    BeginSectionCard();
                    DrawActionButton();
                    EndSectionCard();
                }

                EditorGUILayout.Space(SectionGap);
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
            GUILayoutUtility.GetRect(1f, SectionGap * 0.5f, GUILayout.ExpandWidth(true));
            Rect lineRect = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            GUILayoutUtility.GetRect(1f, SectionGap * 0.5f, GUILayout.ExpandWidth(true));

            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(lineRect, s_separatorColor);
        }

        private void EnsureSectionStyles()
        {
            if (_sectionCardStyle == null)
            {
                _sectionCardStyle = new GUIStyle(EditorStyles.helpBox)
                {
                    padding = new RectOffset(12, 12, 10, 12),
                    margin = new RectOffset(0, 0, 0, 0),
                    stretchWidth = true,
                };
            }

            if (_sectionContentStyle == null)
            {
                _sectionContentStyle = new GUIStyle()
                {
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    stretchWidth = true,
                };
            }
        }

        private void BeginSectionCard()
        {
            EnsureSectionStyles();

            EditorGUILayout.BeginVertical(_sectionCardStyle);

            Rect accentRect = GUILayoutUtility.GetRect(0f, 4f, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(accentRect, s_sectionTintColor);
                EditorGUI.DrawRect(new Rect(accentRect.x, accentRect.y, accentRect.width, 1f), s_sectionBorderColor);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginVertical(_sectionContentStyle);
        }

        private static void EndSectionCard()
        {
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndVertical();
        }

        private void DrawHeader()
        {
            // Load banner texture once: null after domain reload until first draw.
            if (_bannerTexture == null)
                _bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BannerPath);

            if (_bannerTexture != null)
            {
                float aspect = (float)_bannerTexture.width / _bannerTexture.height;
                float availableWidth = EditorGUIUtility.currentViewWidth;
                float drawWidth = Mathf.Min(availableWidth, BannerMaxDrawWidth);
                float drawX = Mathf.Max(0f, (availableWidth - drawWidth) * 0.5f);
                float bannerHeight = Mathf.Round(drawWidth / aspect);

                // Draw at the top with a capped width so very wide editor windows don't let
                // the banner dominate the whole viewport.
                GUI.DrawTexture(
                    new Rect(drawX, 0f, drawWidth, bannerHeight),
                    _bannerTexture,
                    ScaleMode.StretchToFill,
                    alphaBlend: true);

                // Consume the height in the layout system so content below doesn't overlap.
                GUILayout.Space(bannerHeight + 4f);
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
            EditorGUILayout.LabelField(
                "Choose the avatar root you want to inspect and configure.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            var newAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                label:             "Avatar Root",
                obj:               _selectedAvatar,
                objType:           typeof(VRCAvatarDescriptor),
                allowSceneObjects: true);

            if (newAvatar != _selectedAvatar)
            {
                _selectedAvatar = newAvatar;
                _cachedComponent = null;
                _lastRefreshFrame = -1;
                _discoveredParamCount = -1;

                if (_selectedAvatar != null)
                    SyncPendingSlotCountFromAvatar();

                Repaint();
            }

            if (_selectedAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Select your avatar in the Hierarchy (the object with a VRC Avatar Descriptor) to begin.",
                    MessageType.Info);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Set the preset count first. Everything else adapts around that choice.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

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
                    ChangedPresetCountHelpText,
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
            EditorGUILayout.LabelField(
                "Tune slot icon behavior here. The preview below stays as the source of truth for how the menu will look.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6);

            var component = GetOrRefreshComponent();

            // Icon mode stays directly under section title.
            DrawIconMode(component);

            EditorGUILayout.Space(8);
            DrawWheelPreview();
        }

        private static string[] ParseExcludedParameterNames(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return Array.Empty<string>();

            return SanitizeExcludedParameterNames(
                rawValue
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .ToArray());
        }

        private void SetComponentBool(ASMLiteComponent component, string undoLabel, ref bool target, bool value)
        {
            if (target == value)
                return;

            Undo.RecordObject(component, undoLabel);
            target = value;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentTexture(ASMLiteComponent component, string undoLabel, ref Texture2D target, Texture2D value)
        {
            if (target == value)
                return;

            Undo.RecordObject(component, undoLabel);
            target = value;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentString(ASMLiteComponent component, string undoLabel, ref string target, string value)
        {
            string normalized = NormalizeOptionalString(value);
            if (string.Equals(target, normalized, StringComparison.Ordinal))
                return;

            Undo.RecordObject(component, undoLabel);
            target = normalized;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentRawString(ASMLiteComponent component, string undoLabel, ref string target, string value)
        {
            if (string.Equals(target, value, StringComparison.Ordinal))
                return;

            Undo.RecordObject(component, undoLabel);
            target = value ?? string.Empty;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentStringArray(ASMLiteComponent component, string undoLabel, ref string[] target, string[] value)
        {
            string[] next = value ?? Array.Empty<string>();
            string[] current = target ?? Array.Empty<string>();

            if (current.Length == next.Length)
            {
                bool equal = true;
                for (int i = 0; i < current.Length; i++)
                {
                    if (!string.Equals(current[i], next[i], StringComparison.Ordinal))
                    {
                        equal = false;
                        break;
                    }
                }

                if (equal)
                    return;
            }

            Undo.RecordObject(component, undoLabel);
            target = CloneStrings(next);
            EditorUtility.SetDirty(component);
        }

        private static string DrawTextFieldWithFocusCue(string label, string value, string controlName)
        {
            Rect rect = EditorGUILayout.GetControlRect();
            int id = GUIUtility.GetControlID(FocusType.Keyboard, rect);
            Rect fieldRect = EditorGUI.PrefixLabel(rect, id, new GUIContent(label));

            GUI.SetNextControlName(controlName);
            return EditorGUI.TextField(fieldRect, value ?? string.Empty);
        }

        private static bool IsNamedTextFieldFocused(string controlName)
        {
            return string.Equals(GUI.GetNameOfFocusedControl(), controlName, StringComparison.Ordinal);
        }

        private void CommitDraftRawStringIfBlurred(ASMLiteComponent component, string controlName, string undoLabel, ref string target, string draftValue)
        {
            if (component == null || IsNamedTextFieldFocused(controlName))
                return;

            SetComponentRawString(component, undoLabel, ref target, draftValue ?? string.Empty);
        }

        private void CommitDraftStringIfBlurred(ASMLiteComponent component, string controlName, string undoLabel, ref string target, string draftValue)
        {
            if (component == null || IsNamedTextFieldFocused(controlName))
                return;

            SetComponentString(component, undoLabel, ref target, draftValue);
        }

        private void CommitDraftStringArrayIfBlurred(ASMLiteComponent component, string[] controlNames, string undoLabel, ref string[] target, string[] draftValue)
        {
            if (component == null)
                return;

            if (controlNames != null)
            {
                for (int i = 0; i < controlNames.Length; i++)
                {
                    if (IsNamedTextFieldFocused(controlNames[i]))
                        return;
                }
            }

            SetComponentStringArray(component, undoLabel, ref target, draftValue);
        }

        private void SetComponentExcludedNames(ASMLiteComponent component, string undoLabel, string[] value)
        {
            string[] sanitized = SanitizeExcludedParameterNames(value);

            if (component.excludedParameterNames != null && component.excludedParameterNames.SequenceEqual(sanitized, StringComparer.Ordinal))
                return;

            Undo.RecordObject(component, undoLabel);
            component.excludedParameterNames = sanitized;
            EditorUtility.SetDirty(component);
        }

        private string ResolveEffectiveRootNameForPreview(ASMLiteComponent component)
        {
            return component
                ? ASMLiteBuilder.ResolveEffectiveRootControlName(component)
                : (_pendingUseCustomRootName && !string.IsNullOrWhiteSpace(_pendingCustomRootName)
                    ? NormalizeOptionalString(_pendingCustomRootName)
                    : ASMLiteBuilder.DefaultRootControlName);
        }

        private string[] ResolveEffectiveActionLabelsForPreview(ASMLiteComponent component)
        {
            string save = component
                ? ASMLiteBuilder.ResolveEffectiveSaveLabel(component)
                : (_pendingUseCustomRootName && !string.IsNullOrWhiteSpace(_pendingCustomSaveLabel)
                    ? NormalizeOptionalString(_pendingCustomSaveLabel)
                    : ASMLiteBuilder.DefaultSaveLabel);

            string load = component
                ? ASMLiteBuilder.ResolveEffectiveLoadLabel(component)
                : (_pendingUseCustomRootName && !string.IsNullOrWhiteSpace(_pendingCustomLoadLabel)
                    ? NormalizeOptionalString(_pendingCustomLoadLabel)
                    : ASMLiteBuilder.DefaultLoadLabel);

            string clear = component
                ? ASMLiteBuilder.ResolveEffectiveClearPresetLabel(component)
                : (_pendingUseCustomRootName && !string.IsNullOrWhiteSpace(_pendingCustomClearPresetLabel)
                    ? NormalizeOptionalString(_pendingCustomClearPresetLabel)
                    : ASMLiteBuilder.DefaultClearPresetLabel);

            return new[] { s_subWheelBackLabel, save, load, clear };
        }

        private string ResolveEffectivePendingPresetLabelForPreview(int presetIndex)
        {
            int slot = presetIndex + 1;
            if (!_pendingUseCustomRootName)
                return ASMLiteBuilder.DefaultPresetNameFormat.Replace("{slot}", slot.ToString(), StringComparison.OrdinalIgnoreCase).Trim();

            string[] presetNames = EnsureSizedStringArray(_pendingCustomPresetNames, Mathf.Max(_pendingSlotCount, slot));
            string candidate = presetIndex < presetNames.Length ? NormalizeOptionalString(presetNames[presetIndex]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;

            string legacyFormat = NormalizeOptionalString(_pendingCustomPresetNameFormat);
            if (!string.IsNullOrWhiteSpace(legacyFormat) && legacyFormat.IndexOf("{slot}", StringComparison.OrdinalIgnoreCase) >= 0)
                return legacyFormat.Replace("{slot}", slot.ToString(), StringComparison.OrdinalIgnoreCase).Trim();

            return ASMLiteBuilder.DefaultPresetNameFormat.Replace("{slot}", slot.ToString(), StringComparison.OrdinalIgnoreCase).Trim();
        }

        private void DrawCustomizeSection()
        {
            EditorGUILayout.LabelField("Customize", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Everything here is optional. If you leave these toggles off, ASM-Lite keeps its default look and behavior.",
                MessageType.None);
            EditorGUILayout.Space(6);

            var component = GetOrRefreshComponent();

            EditorGUILayout.BeginVertical("box");

            bool useCustomSlotIcons = component ? component.useCustomSlotIcons : _pendingUseCustomSlotIcons;
            bool newUseCustomSlotIcons = EditorGUILayout.ToggleLeft("Use custom icons", useCustomSlotIcons);
            if (component)
            {
                SetComponentBool(component, "Toggle ASM-Lite Custom Slot Icons", ref component.useCustomSlotIcons, newUseCustomSlotIcons);

                if (!newUseCustomSlotIcons && component.actionIconMode != ActionIconMode.Default)
                {
                    Undo.RecordObject(component, "Disable ASM-Lite Custom Action Icons");
                    component.actionIconMode = ActionIconMode.Default;
                    EditorUtility.SetDirty(component);
                }
            }
            else
            {
                _pendingUseCustomSlotIcons = newUseCustomSlotIcons;
                if (!newUseCustomSlotIcons)
                    _pendingActionIconMode = ActionIconMode.Default;
            }

            if (newUseCustomSlotIcons)
            {
                EditorGUILayout.BeginVertical("box");

                _iconsRootFoldout = EditorGUILayout.Foldout(_iconsRootFoldout, "Root Icon", true);
                if (_iconsRootFoldout)
                {
                    EditorGUI.indentLevel++;
                    DrawRootIconSettings(component);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(4);
                _iconsActionFoldout = EditorGUILayout.Foldout(_iconsActionFoldout, "Action Icons", true);
                if (_iconsActionFoldout)
                {
                    EditorGUI.indentLevel++;
                    DrawActionIcons(component);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(4);
                _iconsSlotFoldout = EditorGUILayout.Foldout(_iconsSlotFoldout, PresetIconsFoldoutTitle, true);
                if (_iconsSlotFoldout)
                {
                    EditorGUI.indentLevel++;
                    DrawSlotIconSelectors(component);
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6);
            bool useCustomRootName = component ? component.useCustomRootName : _pendingUseCustomRootName;
            bool newUseCustomRootName = EditorGUILayout.ToggleLeft("Use custom name", useCustomRootName);
            if (component)
                SetComponentBool(component, "Toggle ASM-Lite Custom Menu Names", ref component.useCustomRootName, newUseCustomRootName);
            else
                _pendingUseCustomRootName = newUseCustomRootName;

            if (newUseCustomRootName)
            {
                int nameSlotCount = component ? component.slotCount : _pendingSlotCount;

                // ── Root Menu Name ───────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(NamingSectionRootHeader, EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;

                if (component)
                {
                    _pendingCustomRootName = DrawTextFieldWithFocusCue(RootMenuFieldLabel, _pendingCustomRootName, "asm_name_root") ?? string.Empty;
                    CommitDraftRawStringIfBlurred(component, "asm_name_root", "Change ASM-Lite Root Menu Name", ref component.customRootName, _pendingCustomRootName);
                }
                else
                {
                    _pendingCustomRootName = DrawTextFieldWithFocusCue(RootMenuFieldLabel, _pendingCustomRootName, "asm_name_root_pending") ?? string.Empty;
                }

                EditorGUI.indentLevel--;

                // ── Preset Names ────────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(NamingSectionPresetHeader, EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;

                if (component)
                {
                    _pendingCustomPresetNames = EnsureSizedStringArray(_pendingCustomPresetNames, nameSlotCount);
                    string[] controlNames = new string[nameSlotCount];
                    for (int i = 0; i < nameSlotCount; i++)
                    {
                        string controlName = $"asm_name_preset_{i + 1}";
                        controlNames[i] = controlName;
                        _pendingCustomPresetNames[i] = DrawTextFieldWithFocusCue(string.Format(PresetNameLabelFormat, i + 1), _pendingCustomPresetNames[i], controlName) ?? string.Empty;
                    }

                    CommitDraftStringArrayIfBlurred(component, controlNames, "Change ASM-Lite Preset Name", ref component.customPresetNames, _pendingCustomPresetNames);
                }
                else
                {
                    _pendingCustomPresetNames = EnsureSizedStringArray(_pendingCustomPresetNames, nameSlotCount);
                    for (int i = 0; i < nameSlotCount; i++)
                    {
                        _pendingCustomPresetNames[i] = DrawTextFieldWithFocusCue(string.Format(PresetNameLabelFormat, i + 1), _pendingCustomPresetNames[i], $"asm_name_preset_pending_{i + 1}") ?? string.Empty;
                    }
                }

                EditorGUI.indentLevel--;

                // ── Action Labels ───────────────────────────────────────────────
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField(NamingSectionActionHeader, EditorStyles.miniBoldLabel);
                EditorGUI.indentLevel++;

                if (component)
                {
                    _pendingCustomSaveLabel = DrawTextFieldWithFocusCue(SaveFieldLabel, _pendingCustomSaveLabel, "asm_name_save") ?? string.Empty;
                    CommitDraftRawStringIfBlurred(component, "asm_name_save", "Change ASM-Lite Save Label", ref component.customSaveLabel, _pendingCustomSaveLabel);

                    _pendingCustomLoadLabel = DrawTextFieldWithFocusCue(LoadFieldLabel, _pendingCustomLoadLabel, "asm_name_load") ?? string.Empty;
                    CommitDraftRawStringIfBlurred(component, "asm_name_load", "Change ASM-Lite Load Label", ref component.customLoadLabel, _pendingCustomLoadLabel);

                    _pendingCustomClearPresetLabel = DrawTextFieldWithFocusCue(ClearPresetLabel, _pendingCustomClearPresetLabel, "asm_name_clear") ?? string.Empty;
                    CommitDraftRawStringIfBlurred(component, "asm_name_clear", "Change ASM-Lite Clear Preset Label", ref component.customClearPresetLabel, _pendingCustomClearPresetLabel);

                    _pendingCustomConfirmLabel = DrawTextFieldWithFocusCue(ConfirmFieldLabel, _pendingCustomConfirmLabel, "asm_name_confirm") ?? string.Empty;
                    CommitDraftRawStringIfBlurred(component, "asm_name_confirm", "Change ASM-Lite Confirm Label", ref component.customConfirmLabel, _pendingCustomConfirmLabel);
                }
                else
                {
                    _pendingCustomSaveLabel = DrawTextFieldWithFocusCue(SaveFieldLabel, _pendingCustomSaveLabel, "asm_name_save_pending") ?? string.Empty;
                    _pendingCustomLoadLabel = DrawTextFieldWithFocusCue(LoadFieldLabel, _pendingCustomLoadLabel, "asm_name_load_pending") ?? string.Empty;
                    _pendingCustomClearPresetLabel = DrawTextFieldWithFocusCue(ClearPresetLabel, _pendingCustomClearPresetLabel, "asm_name_clear_pending") ?? string.Empty;
                    _pendingCustomConfirmLabel = DrawTextFieldWithFocusCue(ConfirmFieldLabel, _pendingCustomConfirmLabel, "asm_name_confirm_pending") ?? string.Empty;
                }

                EditorGUI.indentLevel--;

                EditorGUILayout.Space(4);
                EditorGUILayout.HelpBox(
                    NameFallbackGuidanceText,
                    MessageType.None);
            }

            EditorGUILayout.Space(6);

            bool useCustomInstallPath = component ? component.useCustomInstallPath : _pendingUseCustomInstallPath;
            bool newUseCustomInstallPath = EditorGUILayout.ToggleLeft("Use custom install path", useCustomInstallPath);
            if (component)
            {
                bool installToggleChanged = component.useCustomInstallPath != newUseCustomInstallPath;
                SetComponentBool(component, "Toggle ASM-Lite Custom Install Path", ref component.useCustomInstallPath, newUseCustomInstallPath);
                if (installToggleChanged)
                    TryRefreshInstallPathPrefix(component, "Customize Toggle");
            }
            else
            {
                _pendingUseCustomInstallPath = newUseCustomInstallPath;
            }

            if (newUseCustomInstallPath)
            {
                string currentInstallPath = NormalizeOptionalString(_pendingCustomInstallPath);

                EditorGUI.BeginChangeCheck();
                string newInstallPath = DrawTextFieldWithFocusCue("Install Path", currentInstallPath, "asm_install_path");
                if (EditorGUI.EndChangeCheck())
                    _pendingCustomInstallPath = NormalizeOptionalString(newInstallPath);

                bool installPathFocused = IsNamedTextFieldFocused("asm_install_path");
                CommitDraftStringIfBlurred(component, "asm_install_path", "Change ASM-Lite Install Path", ref component.customInstallPath, _pendingCustomInstallPath);
                if (component && !installPathFocused)
                    TryRefreshInstallPathPrefix(component, "Customize Text");

                DrawInstallPathTree(component);
            }

            EditorGUILayout.Space(6);

            bool useParameterExclusions = component ? component.useParameterExclusions : _pendingUseParameterExclusions;
            bool newUseParameterExclusions = EditorGUILayout.ToggleLeft("Customize parameter backup", useParameterExclusions);
            if (component)
                SetComponentBool(component, "Toggle ASM-Lite Parameter Backup Customization", ref component.useParameterExclusions, newUseParameterExclusions);
            else
                _pendingUseParameterExclusions = newUseParameterExclusions;

            if (newUseParameterExclusions)
                DrawParameterChecklist(component);

            EditorGUILayout.EndVertical();
        }

        private void ApplyInstallPathSelection(ASMLiteComponent component, string selectedPath)
        {
            string normalized = NormalizeOptionalString(selectedPath);
            if (component)
            {
                bool enableToggle = !string.IsNullOrEmpty(normalized) && !component.useCustomInstallPath;

                if (enableToggle)
                    SetComponentBool(component, "Toggle ASM-Lite Custom Install Path", ref component.useCustomInstallPath, true);

                SetComponentString(component, "Change ASM-Lite Install Path", ref component.customInstallPath, normalized);
                // Always attempt live prefix refresh for explicit tree selection.
                // This repairs stale VF prefix state even when the selected path text
                // matches the component's current customInstallPath value.
                TryRefreshInstallPathPrefix(component, "Customize Tree");
            }
            else
            {
                _pendingCustomInstallPath = normalized;
                if (!string.IsNullOrEmpty(normalized))
                    _pendingUseCustomInstallPath = true;
            }
        }

        private static void TryRefreshInstallPathPrefix(ASMLiteComponent component, string contextLabel)
        {
            if (component == null)
                return;

            if (!TryRefreshLiveInstallPathPrefix(component, contextLabel))
            {
                Debug.LogWarning($"[ASM-Lite] {contextLabel}: Install-path update did not refresh live FullController menu prefix immediately. Rebuild/upload will retry.");
            }
        }

        // ── Install Path Tree UI ──────────────────────────────────────────────

        private void DrawInstallPathTree(ASMLiteComponent component)
        {
            // Invalidate cached tree when avatar changes.
            if (_lastInstallPathTreeAvatar != _selectedAvatar)
            {
                _cachedInstallPathTree = null;
                _lastInstallPathTreeAvatar = _selectedAvatar;
                _expandedInstallPaths.Clear();
            }

            if (_cachedInstallPathTree == null)
                _cachedInstallPathTree = BuildInstallPathTree(_selectedAvatar);

            string currentPath = component
                ? NormalizeOptionalString(component.customInstallPath)
                : NormalizeOptionalString(_pendingCustomInstallPath);

            EditorGUILayout.Space(2f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Install Location", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("↻", GUILayout.Width(22f), GUILayout.Height(14f)))
            {
                _cachedInstallPathTree = BuildInstallPathTree(_selectedAvatar);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            if (_cachedInstallPathTree == null || _cachedInstallPathTree.Children.Count == 0)
            {
                EditorGUILayout.HelpBox("No expression menu paths were found on this avatar yet.", MessageType.None);
                return;
            }

            // Root row. Selects empty install path (menu root).
            bool rootSelected = string.IsNullOrEmpty(currentPath);
            var rootRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            if (rootSelected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rootRect, new Color(0.24f, 0.49f, 0.91f, 0.30f));
            if (GUI.Button(rootRect, rootSelected ? "(root)" : "(root)", rootSelected ? EditorStyles.boldLabel : EditorStyles.label))
            {
                ApplyInstallPathSelection(component, string.Empty);
                Repaint();
            }

            _installPathTreeScrollPos = EditorGUILayout.BeginScrollView(
                _installPathTreeScrollPos, GUILayout.Height(_installPathTreeHeight));

            foreach (var child in _cachedInstallPathTree.Children)
                DrawInstallPathTreeNode(child, component, currentPath, 0);

            EditorGUILayout.EndScrollView();

            // ── Resize handle ─────────────────────────────────────────────────
            // A narrow strip at the bottom-right that the user can drag up/down.
            var handleRect = EditorGUILayout.GetControlRect(false, 8f);
            handleRect = new Rect(handleRect.xMax - 24f, handleRect.y, 24f, 8f);

            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.Repaint)
            {
                // Draw three small dots as a visual grip indicator.
                var dotColor = new Color(0.55f, 0.55f, 0.55f, 0.80f);
                float cx = handleRect.x + handleRect.width / 2f;
                float cy = handleRect.y + handleRect.height / 2f;
                for (int d = -1; d <= 1; d++)
                    EditorGUI.DrawRect(new Rect(cx + d * 5f - 1f, cy - 1f, 2f, 2f), dotColor);
            }

            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
            {
                _isDraggingTreeResize = true;
                Event.current.Use();
            }

            if (_isDraggingTreeResize)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _installPathTreeHeight = Mathf.Clamp(
                        _installPathTreeHeight + Event.current.delta.y, 80f, 600f);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    _isDraggingTreeResize = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawInstallPathTreeNode(
            MenuTreeNode node,
            ASMLiteComponent component,
            string currentPath,
            int depth)
        {
            bool isSelected = string.Equals(currentPath, node.FullPath, StringComparison.Ordinal);
            bool hasChildren = node.Children.Count > 0;
            bool isExpanded = _expandedInstallPaths.Contains(node.FullPath);

            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float indentPx = depth * 14f + 2f;
            var activeRect = new Rect(rowRect.x + indentPx, rowRect.y, rowRect.width - indentPx, rowRect.height);

            // Selection highlight.
            if (isSelected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(activeRect, new Color(0.24f, 0.49f, 0.91f, 0.30f));

            // Foldout arrow. Toggles expand/collapse without selecting.
            if (hasChildren)
            {
                var arrowRect = new Rect(activeRect.x, activeRect.y, 14f, activeRect.height);
                bool toggled = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);
                if (toggled != isExpanded)
                {
                    if (toggled) _expandedInstallPaths.Add(node.FullPath);
                    else _expandedInstallPaths.Remove(node.FullPath);
                    isExpanded = toggled;
                    Repaint();
                }
            }

            // Label. Clicking selects this path as the install location.
            var labelRect = new Rect(
                activeRect.x + 14f, activeRect.y,
                activeRect.width - 14f, activeRect.height);

            if (GUI.Button(labelRect, node.Name, isSelected ? EditorStyles.boldLabel : EditorStyles.label))
            {
                ApplyInstallPathSelection(component, node.FullPath);
                Repaint();
            }

            if (hasChildren && isExpanded)
            {
                foreach (var child in node.Children)
                    DrawInstallPathTreeNode(child, component, currentPath, depth + 1);
            }
        }

        private static MenuTreeNode BuildInstallPathTree(VRCAvatarDescriptor avatar)
        {
            var root = new MenuTreeNode { Name = string.Empty, FullPath = string.Empty };
            if (avatar == null)
                return root;

            var allPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in GetAvatarSubmenuPaths(avatar)) allPaths.Add(p);
            foreach (var p in GetVrcFuryMenuPrefixes(avatar)) allPaths.Add(p);

            // Apply VRCFury MoveMenuItem remaps so install-path choices reflect
            // the effective post-move menu layout (destination paths) instead of
            // exposing stale pre-move source locations.
            var moveRemaps = GetVrcFuryMoveMenuPathRemaps(avatar);
            ApplyInstallPathMoveRemaps(allPaths, moveRemaps);

            var nodeMap = new Dictionary<string, MenuTreeNode>(StringComparer.Ordinal);
            nodeMap[string.Empty] = root;

            foreach (var path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
                EnsureTreeNodeExists(nodeMap, path);

            SortTreeChildren(root);
            return root;
        }

        private static MenuTreeNode EnsureTreeNodeExists(
            Dictionary<string, MenuTreeNode> nodeMap, string path)
        {
            if (nodeMap.TryGetValue(path, out var existing))
                return existing;

            int slash = path.LastIndexOf('/');
            string parentPath = slash < 0 ? string.Empty : path.Substring(0, slash);
            string name = slash < 0 ? path : path.Substring(slash + 1);

            var parent = EnsureTreeNodeExists(nodeMap, parentPath);
            var node = new MenuTreeNode { Name = name, FullPath = path };
            parent.Children.Add(node);
            nodeMap[path] = node;
            return node;
        }

        private static void SortTreeChildren(MenuTreeNode node)
        {
            node.Children.Sort((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var child in node.Children)
                SortTreeChildren(child);
        }

        private static Dictionary<string, string> GetVrcFuryMoveMenuPathRemaps(VRCAvatarDescriptor avatar)
        {
            var remaps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (avatar == null)
                return remaps;

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                var iterator = so.GetIterator();
                if (!iterator.NextVisible(true))
                    continue;

                var seenPaths = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                        continue;

                    string managedRefType = iterator.managedReferenceFullTypename;
                    if (string.IsNullOrWhiteSpace(managedRefType)
                        || managedRefType.IndexOf("MoveMenuItem", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string managedPath = iterator.propertyPath;
                    if (!seenPaths.Add(managedPath))
                        continue;

                    var fromProp = so.FindProperty(managedPath + ".fromPath");
                    var toProp = so.FindProperty(managedPath + ".toPath");
                    if (fromProp == null || toProp == null)
                        continue;
                    if (fromProp.propertyType != SerializedPropertyType.String
                        || toProp.propertyType != SerializedPropertyType.String)
                        continue;

                    string fromPath = NormalizeSlashPath(fromProp.stringValue);
                    string toPath = NormalizeSlashPath(toProp.stringValue);
                    if (string.IsNullOrWhiteSpace(toPath))
                        continue;

                    if (!remaps.ContainsKey(fromPath ?? string.Empty))
                        remaps[fromPath ?? string.Empty] = toPath;
                } while (iterator.NextVisible(true));
            }

            return remaps;
        }

        private static void ApplyInstallPathMoveRemaps(HashSet<string> allPaths, Dictionary<string, string> remaps)
        {
            if (allPaths == null || remaps == null || remaps.Count == 0)
                return;

            foreach (var kv in remaps)
            {
                string fromPath = kv.Key ?? string.Empty;
                string toPath = kv.Value ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(fromPath))
                {
                    allPaths.RemoveWhere(path =>
                        string.Equals(path, fromPath, StringComparison.Ordinal)
                        || path.StartsWith(fromPath + "/", StringComparison.Ordinal));
                }

                AddPathAndParents(allPaths, toPath);
            }
        }

        private static void AddPathAndParents(HashSet<string> allPaths, string fullPath)
        {
            if (allPaths == null || string.IsNullOrWhiteSpace(fullPath))
                return;

            string normalized = NormalizeSlashPath(fullPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < segments.Length; i++)
            {
                if (sb.Length > 0)
                    sb.Append('/');
                sb.Append(segments[i]);
                allPaths.Add(sb.ToString());
            }
        }

        // ── Parameter Backup Tree UI ──────────────────────────────────────────

        private static Dictionary<string, string> BuildHiddenAssignedByVisibleOriginalMap(IReadOnlyCollection<string> visibleParamNames)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (visibleParamNames == null || visibleParamNames.Count == 0)
                return map;

            var visibleSet = new HashSet<string>(visibleParamNames, StringComparer.Ordinal);
            var mappings = ASMLiteToggleNameBroker.GetLatestGlobalParamMappings();
            if (mappings == null || mappings.Length == 0)
                return map;

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (string.IsNullOrWhiteSpace(mapping.OriginalGlobalParam)
                    || string.IsNullOrWhiteSpace(mapping.AssignedGlobalParam))
                    continue;

                if (!visibleSet.Contains(mapping.OriginalGlobalParam))
                    continue;
                if (visibleSet.Contains(mapping.AssignedGlobalParam))
                    continue;

                if (!map.ContainsKey(mapping.OriginalGlobalParam))
                    map.Add(mapping.OriginalGlobalParam, mapping.AssignedGlobalParam);
            }

            return map;
        }

        private static HashSet<string> NormalizeExcludedSetForVisibleRows(
            IEnumerable<string> excludedRaw,
            Dictionary<string, string> hiddenAssignedByVisibleOriginal)
        {
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            if (excludedRaw != null)
            {
                foreach (var name in excludedRaw)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        normalized.Add(name);
                }
            }

            if (hiddenAssignedByVisibleOriginal != null)
            {
                foreach (var kv in hiddenAssignedByVisibleOriginal)
                {
                    string original = kv.Key;
                    string assigned = kv.Value;
                    if (normalized.Contains(assigned))
                        normalized.Add(original);
                }
            }

            return normalized;
        }

        private static string[] ExpandExcludedForStorage(
            HashSet<string> visibleExcluded,
            Dictionary<string, string> hiddenAssignedByVisibleOriginal)
        {
            var expanded = new HashSet<string>(visibleExcluded ?? new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

            if (hiddenAssignedByVisibleOriginal != null)
            {
                foreach (var kv in hiddenAssignedByVisibleOriginal)
                {
                    if (expanded.Contains(kv.Key))
                        expanded.Add(kv.Value);
                }
            }

            return expanded.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        private void DrawParameterChecklist(ASMLiteComponent component)
        {
            if (_lastParamListAvatar != _selectedAvatar)
            {
                _cachedParamList = null;
                _cachedParamTree = null;
                _expandedParamMenuPaths.Clear();
                _lastParamListAvatar = _selectedAvatar;
            }
            if (_cachedParamList == null)
                _cachedParamList = GetBackableParameterNames(_selectedAvatar);
            if (_cachedParamTree == null)
                _cachedParamTree = BuildParamTree(_selectedAvatar);

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Parameter Backup", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("↻", GUILayout.Width(22f), GUILayout.Height(14f)))
            {
                _cachedParamList = GetBackableParameterNames(_selectedAvatar);
                _cachedParamTree = BuildParamTree(_selectedAvatar);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Uncheck parameters to exclude them from backup.", EditorStyles.wordWrappedMiniLabel);

            if (_cachedParamList == null || _cachedParamList.Length == 0)
            {
                EditorGUILayout.HelpBox("No expression parameters were found on this avatar yet.", MessageType.None);
                return;
            }

            // Build hidden mapping: visible original param -> hidden assigned ASM_VF_* alias.
            var hiddenAssignedByVisibleOriginal = BuildHiddenAssignedByVisibleOriginalMap(_cachedParamList);

            // Build mutable exclusion set from current component/pending state.
            string[] currentExcluded = component
                ? SanitizeExcludedParameterNames(component.excludedParameterNames)
                : SanitizeExcludedParameterNames(_pendingExcludedParameterNames);
            var excludedSet = NormalizeExcludedSetForVisibleRows(currentExcluded, hiddenAssignedByVisibleOriginal);
            var originalExcluded = new HashSet<string>(excludedSet, StringComparer.Ordinal);

            _paramChecklistScrollPos = EditorGUILayout.BeginScrollView(
                _paramChecklistScrollPos, GUILayout.Height(_paramChecklistHeight));

            foreach (var child in _cachedParamTree.Children)
                DrawParamTreeNode(child, excludedSet, 0);

            EditorGUILayout.EndScrollView();

            // Write back if anything changed.
            if (!excludedSet.SetEquals(originalExcluded))
            {
                string[] newExcluded = ExpandExcludedForStorage(excludedSet, hiddenAssignedByVisibleOriginal);
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Parameter Backup");
                    component.excludedParameterNames = newExcluded;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingExcludedParameterNames = newExcluded;
                }
                Repaint();
            }

            // ── Resize handle ─────────────────────────────────────────────────
            var handleRect = EditorGUILayout.GetControlRect(false, 8f);
            handleRect = new Rect(handleRect.xMax - 24f, handleRect.y, 24f, 8f);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.Repaint)
            {
                var dotColor = new Color(0.55f, 0.55f, 0.55f, 0.80f);
                float cx = handleRect.x + handleRect.width / 2f;
                float cy = handleRect.y + handleRect.height / 2f;
                for (int d = -1; d <= 1; d++)
                    EditorGUI.DrawRect(new Rect(cx + d * 5f - 1f, cy - 1f, 2f, 2f), dotColor);
            }

            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
            {
                _isDraggingParamResize = true;
                Event.current.Use();
            }

            if (_isDraggingParamResize)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _paramChecklistHeight = Mathf.Clamp(
                        _paramChecklistHeight + Event.current.delta.y, 60f, 400f);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    _isDraggingParamResize = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawParamTreeNode(ParamTreeNode node, HashSet<string> excludedSet, int depth)
        {
            float indentPx = depth * 14f + 2f;

            if (node.IsParam)
            {
                // Checkbox row for a parameter leaf.
                // Add extra offset so child-item checkboxes sit to the right of
                // category checkboxes/foldouts and read as subordinate rows.
                const float childItemOffset = 12f;
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var labelRect = new Rect(rowRect.x + indentPx + childItemOffset, rowRect.y, rowRect.width - indentPx - childItemOffset, rowRect.height);
                bool isIncluded = !excludedSet.Contains(node.ParamName);
                bool newIncluded = EditorGUI.ToggleLeft(labelRect, node.Name, isIncluded);
                if (newIncluded != isIncluded)
                {
                    if (newIncluded) excludedSet.Remove(node.ParamName);
                    else             excludedSet.Add(node.ParamName);
                }
            }
            else
            {
                // Folder row. Foldout arrow + category checkbox + label.
                bool isExpanded = _expandedParamMenuPaths.Contains(node.MenuPath);
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var activeRect = new Rect(rowRect.x + indentPx, rowRect.y, rowRect.width - indentPx, rowRect.height);

                var arrowRect = new Rect(activeRect.x, activeRect.y, 14f, activeRect.height);
                bool toggled = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);
                if (toggled != isExpanded)
                {
                    if (toggled) _expandedParamMenuPaths.Add(node.MenuPath);
                    else         _expandedParamMenuPaths.Remove(node.MenuPath);
                    Repaint();
                }

                int totalLeafCount = CountParamLeafNodes(node);
                int includedLeafCount = CountIncludedParamLeafNodes(node, excludedSet);
                bool allIncluded = totalLeafCount > 0 && includedLeafCount == totalLeafCount;
                bool mixed = includedLeafCount > 0 && includedLeafCount < totalLeafCount;

                var toggleRect = new Rect(activeRect.x + 14f, activeRect.y, 16f, activeRect.height);
                EditorGUI.showMixedValue = mixed;
                bool newAllIncluded = EditorGUI.Toggle(toggleRect, allIncluded);
                EditorGUI.showMixedValue = false;

                if (newAllIncluded != allIncluded || (mixed && Event.current.type == EventType.MouseUp && toggleRect.Contains(Event.current.mousePosition)))
                {
                    SetFolderIncludedState(node, excludedSet, newAllIncluded);
                }

                var labelRect = new Rect(activeRect.x + 32f, activeRect.y, activeRect.width - 32f, activeRect.height);
                EditorGUI.LabelField(labelRect, node.Name, EditorStyles.boldLabel);

                if (toggled)
                    foreach (var child in node.Children)
                        DrawParamTreeNode(child, excludedSet, depth + 1);
            }
        }

        private static int CountParamLeafNodes(ParamTreeNode node)
        {
            if (node == null)
                return 0;
            if (node.IsParam)
                return 1;

            int count = 0;
            for (int i = 0; i < node.Children.Count; i++)
                count += CountParamLeafNodes(node.Children[i]);
            return count;
        }

        private static int CountIncludedParamLeafNodes(ParamTreeNode node, HashSet<string> excludedSet)
        {
            if (node == null)
                return 0;

            if (node.IsParam)
                return excludedSet.Contains(node.ParamName) ? 0 : 1;

            int count = 0;
            for (int i = 0; i < node.Children.Count; i++)
                count += CountIncludedParamLeafNodes(node.Children[i], excludedSet);
            return count;
        }

        private static void SetFolderIncludedState(ParamTreeNode node, HashSet<string> excludedSet, bool include)
        {
            if (node == null)
                return;

            if (node.IsParam)
            {
                if (include) excludedSet.Remove(node.ParamName);
                else         excludedSet.Add(node.ParamName);
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
                SetFolderIncludedState(node.Children[i], excludedSet, include);
        }

        private static ParamTreeNode BuildParamTree(VRCAvatarDescriptor avatar)
        {
            var root = new ParamTreeNode { Name = string.Empty, MenuPath = string.Empty };
            if (avatar == null) return root;

            string[] backableParams = GetBackableParameterNames(avatar);
            if (backableParams.Length == 0) return root;

            // Build VRCFury Toggle metadata so ASM_VF_* global parameters can be
            // grouped under their real menu paths with friendly labels instead of
            // falling into the "(No menu)" bucket with raw global names.
            var vrcFuryMeta = BuildVrcFuryGlobalParamMetadata(avatar);

            // Reuse the same VRCFury menu-prefix discovery strategy as the custom
            // install-path tree, then map each normalized prefix token so ASM_VF_*
            // deterministic names can be mapped back to real menu folders.
            var sanitizedPrefixToMenuPath = new Dictionary<string, string>(StringComparer.Ordinal);
            var vrcFuryMenuPrefixes = GetVrcFuryMenuPrefixes(avatar);
            for (int i = 0; i < vrcFuryMenuPrefixes.Length; i++)
            {
                string menuPrefix = vrcFuryMenuPrefixes[i];
                if (string.IsNullOrWhiteSpace(menuPrefix))
                    continue;

                string token = ASMLiteToggleNameBroker.SanitizePathToken(menuPrefix);
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                if (!sanitizedPrefixToMenuPath.ContainsKey(token))
                    sanitizedPrefixToMenuPath[token] = menuPrefix;
            }

            // Map each param name → the menu folder path where it appears.
            var paramToMenuPath = new Dictionary<string, string>(StringComparer.Ordinal);
            if (avatar.expressionsMenu != null)
                ScanMenuForParamLocations(avatar.expressionsMenu, string.Empty, paramToMenuPath,
                    new HashSet<VRCExpressionsMenu>());

            // Augment with VRCFury-assigned global parameters (ASM_VF_*) when a menu
            // path hint is available. This keeps Toggle-generated parameters out of
            // the "(No menu)" catch-all when they are actually driven from a menu.
            foreach (var kvp in vrcFuryMeta)
            {
                string paramName = kvp.Key;
                var meta = kvp.Value;
                if (string.IsNullOrEmpty(meta.MenuPath))
                    continue;
                if (!paramToMenuPath.ContainsKey(paramName))
                    paramToMenuPath[paramName] = meta.MenuPath;
            }

            // Build folder nodes mirroring the menu hierarchy.
            var menuNodes = new Dictionary<string, ParamTreeNode>(StringComparer.Ordinal);
            menuNodes[string.Empty] = root;

            var unassigned = new List<string>();
            var assigned = new List<(string MenuPath, string DisplayName, string ParamName)>();
            foreach (var paramName in backableParams)
            {
                string menuPath = null;
                if (paramToMenuPath.TryGetValue(paramName, out string mappedPath) && mappedPath != null)
                    menuPath = mappedPath;

                string displayName = paramName;
                if (vrcFuryMeta.TryGetValue(paramName, out var meta) && !string.IsNullOrEmpty(meta.DisplayName))
                    displayName = meta.DisplayName;

                // For deterministic ASM_VF_* names, always infer display + parent
                // folder from the encoded menu token so rows appear as user-facing
                // toggle labels at the correct menu level.
                if (paramName.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, StringComparison.Ordinal)
                    && TryInferMenuPathAndDisplayNameFromAsmVfGlobalName(
                        paramName,
                        sanitizedPrefixToMenuPath,
                        out string inferredAsmMenuPath,
                        out string inferredAsmDisplayName))
                {
                    menuPath = inferredAsmMenuPath;
                    displayName = inferredAsmDisplayName;
                }

                // Fallback: when a parameter name itself encodes a menu-like path
                // (common in some VRCFury Toggle outputs), infer folder + label
                // directly from that path so it does not land in "(No menu)".
                if (string.IsNullOrEmpty(menuPath)
                    && TryInferMenuPathAndDisplayNameFromParamName(paramName, out string inferredMenuPath, out string inferredDisplayName))
                {
                    menuPath = inferredMenuPath;
                    if (string.IsNullOrEmpty(displayName) || string.Equals(displayName, paramName, StringComparison.Ordinal))
                        displayName = inferredDisplayName;
                }

                if (string.IsNullOrEmpty(menuPath))
                {
                    unassigned.Add(paramName);
                    continue;
                }

                assigned.Add((menuPath, string.IsNullOrWhiteSpace(displayName) ? paramName : displayName, paramName));
            }

            // Suffix duplicate display names within the same menu folder so they are
            // distinguishable (Rezz1, Rezz2, ...).
            var totalByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assigned.Count; i++)
            {
                var entry = assigned[i];
                string key = entry.MenuPath + "\u001F" + entry.DisplayName;
                totalByKey[key] = totalByKey.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            var nextIndexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assigned.Count; i++)
            {
                var entry = assigned[i];
                string key = entry.MenuPath + "\u001F" + entry.DisplayName;

                string finalDisplayName = entry.DisplayName;
                if (totalByKey.TryGetValue(key, out int total) && total > 1)
                {
                    int idx = nextIndexByKey.TryGetValue(key, out int next) ? next : 1;
                    nextIndexByKey[key] = idx + 1;
                    finalDisplayName = entry.DisplayName + idx;
                }

                var folderNode = EnsureParamMenuNode(menuNodes, entry.MenuPath);
                folderNode.Children.Add(new ParamTreeNode { Name = finalDisplayName, ParamName = entry.ParamName });
            }

            // Unassigned params shown in a catch-all group.
            if (unassigned.Count > 0)
            {
                const string unassignedPath = "\x01unassigned";
                var group = new ParamTreeNode { Name = "(No menu)", MenuPath = unassignedPath };
                foreach (var p in unassigned)
                {
                    string displayName = p;
                    if (vrcFuryMeta.TryGetValue(p, out var meta) && !string.IsNullOrEmpty(meta.DisplayName))
                        displayName = meta.DisplayName;

                    group.Children.Add(new ParamTreeNode { Name = displayName, ParamName = p });
                }
                root.Children.Add(group);
            }

            SortParamTreeChildren(root);
            return root;
        }

        /// <summary>
        /// Builds a map from VRCFury toggle-like managed-reference payloads to
        /// parameter metadata (menu path + friendly display name).
        ///
        /// This intentionally does not hard-require a specific managed-reference type
        /// name so it remains compatible across VRCFury schema variants.
        /// </summary>
        private static Dictionary<string, (string MenuPath, string DisplayName)> BuildVrcFuryGlobalParamMetadata(VRCAvatarDescriptor avatar)
        {
            var result = new Dictionary<string, (string MenuPath, string DisplayName)>(StringComparer.Ordinal);
            if (avatar == null)
                return result;

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var so = new SerializedObject(behaviour);
                var iterator = so.GetIterator();
                if (!iterator.NextVisible(true))
                    continue;

                var seenPaths = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                        continue;

                    string togglePropertyPath = iterator.propertyPath;
                    if (!seenPaths.Add(togglePropertyPath))
                        continue;

                    var useGlobalProp = so.FindProperty(togglePropertyPath + ".useGlobalParam");
                    var globalParamProp = so.FindProperty(togglePropertyPath + ".globalParam");
                    var menuPathProp = so.FindProperty(togglePropertyPath + ".menuPath");
                    var nameProp = so.FindProperty(togglePropertyPath + ".name");
                    var labelProp = so.FindProperty(togglePropertyPath + ".label");
                    var paramNameProp = so.FindProperty(togglePropertyPath + ".paramName");

                    bool hasAnyToggleFields = useGlobalProp != null
                        || globalParamProp != null
                        || menuPathProp != null
                        || nameProp != null
                        || labelProp != null
                        || paramNameProp != null;

                    if (!hasAnyToggleFields)
                        continue;

                    bool useGlobal = useGlobalProp != null
                        && useGlobalProp.propertyType == SerializedPropertyType.Boolean
                        && useGlobalProp.boolValue;

                    string globalName = globalParamProp != null && globalParamProp.propertyType == SerializedPropertyType.String
                        ? (globalParamProp.stringValue ?? string.Empty).Trim()
                        : string.Empty;

                    string rawMenuPath = menuPathProp != null && menuPathProp.propertyType == SerializedPropertyType.String
                        ? menuPathProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawName = nameProp != null && nameProp.propertyType == SerializedPropertyType.String
                        ? nameProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawLabel = labelProp != null && labelProp.propertyType == SerializedPropertyType.String
                        ? labelProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawParamName = paramNameProp != null && paramNameProp.propertyType == SerializedPropertyType.String
                        ? paramNameProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawNamePath = !string.IsNullOrWhiteSpace(rawName) && rawName.IndexOf('/') >= 0
                        ? rawName
                        : string.Empty;

                    string resolvedPathSource = !string.IsNullOrWhiteSpace(rawMenuPath)
                        ? rawMenuPath
                        : rawNamePath;

                    string normalizedFullPath = NormalizeSlashPath(resolvedPathSource);
                    string normalizedMenuPath = normalizedFullPath;
                    string displayName = string.Empty;

                    if (!string.IsNullOrWhiteSpace(rawLabel))
                    {
                        displayName = rawLabel.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(rawNamePath))
                    {
                        string[] nameParts = NormalizeSlashPath(rawNamePath).Split('/');
                        if (nameParts.Length > 0)
                        {
                            displayName = nameParts[nameParts.Length - 1];
                            if (nameParts.Length > 1)
                                normalizedMenuPath = string.Join("/", nameParts.Take(nameParts.Length - 1));
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(rawName))
                    {
                        displayName = rawName.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(rawParamName))
                    {
                        displayName = rawParamName.Trim();
                    }

                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        if (!string.IsNullOrWhiteSpace(normalizedFullPath))
                        {
                            int lastSlash = normalizedFullPath.LastIndexOf('/');
                            if (lastSlash >= 0 && lastSlash < normalizedFullPath.Length - 1)
                            {
                                displayName = normalizedFullPath.Substring(lastSlash + 1);
                                normalizedMenuPath = normalizedFullPath.Substring(0, lastSlash);
                            }
                            else
                            {
                                displayName = normalizedFullPath;
                            }
                        }
                    }

                    // Candidate parameter names this toggle payload may emit.
                    var candidateParamNames = new List<string>(3);
                    if (useGlobal && !string.IsNullOrWhiteSpace(globalName))
                        candidateParamNames.Add(globalName);
                    if (!string.IsNullOrWhiteSpace(rawParamName))
                        candidateParamNames.Add(rawParamName.Trim());
                    if (!string.IsNullOrWhiteSpace(rawName) && rawName.IndexOf('/') < 0)
                        candidateParamNames.Add(rawName.Trim());

                    for (int c = 0; c < candidateParamNames.Count; c++)
                    {
                        string candidate = candidateParamNames[c];
                        if (string.IsNullOrWhiteSpace(candidate))
                            continue;
                        if (result.ContainsKey(candidate))
                            continue;

                        string resolvedDisplay = string.IsNullOrWhiteSpace(displayName) ? candidate : displayName;
                        if (candidate.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, StringComparison.Ordinal))
                            resolvedDisplay = candidate;

                        result[candidate] = (normalizedMenuPath, resolvedDisplay);
                    }
                } while (iterator.NextVisible(true));
            }

            // Also include deterministic names for eligible toggle candidates that
            // are not currently materialized into avatar expression parameters.
            if (avatar.gameObject != null)
            {
                var reserved = new HashSet<string>(result.Keys, StringComparer.Ordinal);
                var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(avatar.gameObject);
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    string deterministic = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                        candidate.MenuPathHint,
                        candidate.ObjectPath,
                        reserved);

                    if (string.IsNullOrWhiteSpace(deterministic) || result.ContainsKey(deterministic))
                        continue;

                    string menuPath = NormalizeSlashPath(candidate.MenuPathHint);
                    string displayName = deterministic;

                    result[deterministic] = (menuPath, displayName);
                }
            }

            return result;
        }

        private static string NormalizeSlashPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Replace('\\', '/');
            var rawSegments = normalized.Split('/');
            var cleanSegments = new List<string>(rawSegments.Length);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = NormalizeMenuPathSegment(rawSegments[i]);
                if (!string.IsNullOrEmpty(segment))
                    cleanSegments.Add(segment);
            }

            return cleanSegments.Count == 0 ? string.Empty : string.Join("/", cleanSegments);
        }

        private static bool TryInferMenuPathAndDisplayNameFromAsmVfGlobalName(
            string paramName,
            Dictionary<string, string> sanitizedPrefixToMenuPath,
            out string menuPath,
            out string displayName)
        {
            menuPath = string.Empty;
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
                return false;
            if (!paramName.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, StringComparison.Ordinal))
                return false;

            string withoutPrefix = paramName.Substring(ASMLiteToggleNameBroker.GlobalPrefix.Length);
            int split = withoutPrefix.IndexOf("__", StringComparison.Ordinal);
            if (split <= 0)
                return false;

            string menuToken = withoutPrefix.Substring(0, split);

            // First try exact token->path match from discovered VRCFury menu prefixes.
            if (sanitizedPrefixToMenuPath != null
                && sanitizedPrefixToMenuPath.TryGetValue(menuToken, out string exactPath)
                && !string.IsNullOrWhiteSpace(exactPath))
            {
                string normalized = NormalizeSlashPath(exactPath);
                if (TrySplitMenuPathForLabel(normalized, out menuPath, out displayName))
                    return true;
            }

            // Then try longest discovered prefix token match. This handles deterministic
            // names where menuToken includes the leaf label (e.g. prefix + _Bass).
            if (sanitizedPrefixToMenuPath != null && sanitizedPrefixToMenuPath.Count > 0)
            {
                string bestKey = null;
                string bestPath = null;
                foreach (var kvp in sanitizedPrefixToMenuPath)
                {
                    string key = kvp.Key;
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    if (string.Equals(menuToken, key, StringComparison.Ordinal)
                        || menuToken.StartsWith(key + "_", StringComparison.Ordinal))
                    {
                        if (bestKey == null || key.Length > bestKey.Length)
                        {
                            bestKey = key;
                            bestPath = kvp.Value;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(bestKey) && !string.IsNullOrWhiteSpace(bestPath))
                {
                    string remainder = menuToken.Length > bestKey.Length
                        ? menuToken.Substring(bestKey.Length).TrimStart('_')
                        : string.Empty;

                    string normalizedParent = NormalizeSlashPath(bestPath);
                    if (!string.IsNullOrWhiteSpace(remainder))
                    {
                        string remainderLabel = DecodeAsmVfTokenToWords(remainder);
                        if (!string.IsNullOrWhiteSpace(normalizedParent)
                            && !string.IsNullOrWhiteSpace(remainderLabel))
                        {
                            menuPath = normalizedParent;
                            displayName = remainderLabel;
                            return true;
                        }
                    }

                    if (TrySplitMenuPathForLabel(normalizedParent, out menuPath, out displayName))
                        return true;
                }
            }

            // Final fallback when no discoverable prefix exists: decode token into
            // a reasonable flat folder+label shape instead of deep underscore nesting.
            string[] words = SplitAsmVfTokenWords(menuToken);
            if (words.Length < 2)
                return false;

            menuPath = string.Join(" ", words.Take(words.Length - 1));
            displayName = words[words.Length - 1];
            return !string.IsNullOrWhiteSpace(menuPath) && !string.IsNullOrWhiteSpace(displayName);
        }

        private static bool TrySplitMenuPathForLabel(string fullPath, out string menuPath, out string displayName)
        {
            menuPath = string.Empty;
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            var segments = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return false;

            displayName = segments[segments.Length - 1];
            menuPath = segments.Length > 1
                ? string.Join("/", segments.Take(segments.Length - 1))
                : segments[0];

            return !string.IsNullOrWhiteSpace(menuPath) && !string.IsNullOrWhiteSpace(displayName);
        }

        private static string[] SplitAsmVfTokenWords(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Array.Empty<string>();

            return token
                .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }

        private static string DecodeAsmVfTokenToWords(string token)
        {
            var words = SplitAsmVfTokenWords(token);
            if (words.Length == 0)
                return string.Empty;

            return string.Join(" ", words);
        }

        private static bool TryInferMenuPathAndDisplayNameFromParamName(
            string paramName,
            out string menuPath,
            out string displayName)
        {
            menuPath = string.Empty;
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
                return false;

            string normalized = paramName.Replace('\\', '/').Trim();
            if (normalized.IndexOf('/') < 0)
                return false;

            var rawSegments = normalized.Split('/');
            var cleanSegments = new List<string>(rawSegments.Length);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = NormalizeMenuPathSegment(rawSegments[i]);
                if (!string.IsNullOrEmpty(segment))
                    cleanSegments.Add(segment);
            }

            if (cleanSegments.Count < 2)
                return false;

            displayName = cleanSegments[cleanSegments.Count - 1];
            menuPath = string.Join("/", cleanSegments.Take(cleanSegments.Count - 1));

            return !string.IsNullOrEmpty(menuPath) && !string.IsNullOrEmpty(displayName);
        }

        private static void ScanMenuForParamLocations(
            VRCExpressionsMenu menu, string parentPath,
            Dictionary<string, string> paramToPath,
            HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu) || menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                if (control == null) continue;

                // Main control parameter.
                if (!string.IsNullOrEmpty(control.parameter?.name)
                    && !paramToPath.ContainsKey(control.parameter.name))
                    paramToPath[control.parameter.name] = parentPath;

                // Sub-parameters (radial / 2-axis / 4-axis puppets).
                if (control.subParameters != null)
                    foreach (var sub in control.subParameters)
                        if (sub != null && !string.IsNullOrEmpty(sub.name)
                            && !paramToPath.ContainsKey(sub.name))
                            paramToPath[sub.name] = parentPath;

                // Recurse into submenus.
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null)
                {
                    string seg = NormalizeMenuPathSegment(control.name);
                    string childPath = string.IsNullOrEmpty(parentPath) ? seg
                        : string.IsNullOrEmpty(seg) ? parentPath
                        : parentPath + "/" + seg;
                    ScanMenuForParamLocations(control.subMenu, childPath, paramToPath, visited);
                }
            }
        }

        private static ParamTreeNode EnsureParamMenuNode(
            Dictionary<string, ParamTreeNode> nodeMap, string path)
        {
            if (nodeMap.TryGetValue(path, out var existing)) return existing;

            int slash = path.LastIndexOf('/');
            string parentPath = slash < 0 ? string.Empty : path.Substring(0, slash);
            string name       = slash < 0 ? path          : path.Substring(slash + 1);

            var parent = EnsureParamMenuNode(nodeMap, parentPath);
            var node   = new ParamTreeNode { Name = name, MenuPath = path };
            parent.Children.Add(node);
            nodeMap[path] = node;
            return node;
        }

        private static void SortParamTreeChildren(ParamTreeNode node)
        {
            // Folder nodes first, then param leaves, each group alphabetical.
            node.Children.Sort((a, b) =>
            {
                if (a.IsParam != b.IsParam) return a.IsParam ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var child in node.Children)
                if (!child.IsParam) SortParamTreeChildren(child);
        }

        private static string[] GetBackableParameterNames(VRCAvatarDescriptor avatar)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            // 1) Existing avatar expression parameters (current runtime truth).
            if (avatar?.expressionParameters?.parameters != null)
            {
                foreach (var p in avatar.expressionParameters.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name))
                        continue;
                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;
                    if (p.valueType != VRCExpressionParameters.ValueType.Bool
                        && p.valueType != VRCExpressionParameters.ValueType.Int
                        && p.valueType != VRCExpressionParameters.ValueType.Float)
                        continue;

                    names.Add(p.name);
                }
            }

            // 2) VRCFury FullController referenced parameter assets (content.prms).
            //    Include these pre-bake so package/prefab-provided parameter files
            //    (for example media-control prefabs) are available in backup UI.
            if (avatar?.gameObject != null)
            {
                var referencedVfParams = GetVrcFuryReferencedParameterNames(avatar);
                for (int i = 0; i < referencedVfParams.Length; i++)
                {
                    string paramName = referencedVfParams[i];
                    if (string.IsNullOrWhiteSpace(paramName))
                        continue;
                    if (paramName.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    names.Add(paramName);
                }
            }

            // 3) VRCFury Toggle globals already assigned on serialized toggle payloads.
            //    Include them pre-bake so Parameter Backup customization can target
            //    prefab-driven toggles (e.g., nested utility prefabs) even before
            //    expressionParameters has been rebuilt.
            if (avatar?.gameObject != null)
            {
                var assignedGlobals = ASMLiteToggleNameBroker.DiscoverAssignedToggleGlobalParams(avatar.gameObject);
                for (int i = 0; i < assignedGlobals.Count; i++)
                {
                    string assigned = assignedGlobals[i];
                    if (string.IsNullOrWhiteSpace(assigned))
                        continue;
                    if (assigned.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    names.Add(assigned);
                }
            }

            // 4) VRCFury Toggle candidates that will be deterministically promoted to
            //    globals during build-request enrollment. Include them pre-bake so
            //    Parameter Backup customization can target not-yet-assigned toggles.
            //    When a candidate already carries a legacy/non-deterministic source
            //    global name, suppress that stale visible name in favor of the
            //    deterministic ASM_VF_* name ASM-Lite will actually back up after
            //    build-request enrollment.
            if (avatar?.gameObject != null)
            {
                var reserved = new HashSet<string>(names, StringComparer.Ordinal);
                var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(avatar.gameObject);
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    string deterministic = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                        candidate.MenuPathHint,
                        candidate.ObjectPath,
                        reserved);

                    if (string.IsNullOrWhiteSpace(deterministic))
                        continue;
                    if (deterministic.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    if (!string.IsNullOrWhiteSpace(candidate.GlobalParam)
                        && !candidate.GlobalParam.StartsWith("ASMLite_", StringComparison.Ordinal))
                    {
                        names.Remove(candidate.GlobalParam.Trim());
                    }

                    names.Add(deterministic);
                }
            }

            // If both sides of a broker mapping are present, show only the
            // original discovered parameter in the checklist and keep the
            // deterministic ASM_VF_* side hidden.
            var mappings = ASMLiteToggleNameBroker.GetLatestGlobalParamMappings();
            if (mappings != null && mappings.Length > 0)
            {
                for (int i = 0; i < mappings.Length; i++)
                {
                    var mapping = mappings[i];
                    if (string.IsNullOrWhiteSpace(mapping.OriginalGlobalParam)
                        || string.IsNullOrWhiteSpace(mapping.AssignedGlobalParam))
                        continue;

                    if (names.Contains(mapping.OriginalGlobalParam)
                        && names.Contains(mapping.AssignedGlobalParam))
                    {
                        names.Remove(mapping.AssignedGlobalParam);
                    }
                }
            }

            return names
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetAvatarSubmenuPaths(VRCAvatarDescriptor avatar)
        {
            if (avatar == null || avatar.expressionsMenu == null)
                return Array.Empty<string>();

            var allPaths = new HashSet<string>(StringComparer.Ordinal);
            var parentPaths = new HashSet<string>(StringComparer.Ordinal);
            var visitedMenus = new HashSet<VRCExpressionsMenu>();
            CollectSubmenuPathsRecursive(avatar.expressionsMenu, string.Empty, allPaths, parentPaths, visitedMenus);

            // Return every submenu path. Parent folders are valid install locations too.
            return allPaths
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetVrcFuryMenuPrefixes(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return Array.Empty<string>();

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                // FullController: content.menus array with prefix + menu asset pairs.
                var menusProperty = so.FindProperty("content.menus");
                if (menusProperty != null && menusProperty.isArray)
                {
                    for (int menuIndex = 0; menuIndex < menusProperty.arraySize; menuIndex++)
                    {
                        var menuEntry = menusProperty.GetArrayElementAtIndex(menuIndex);
                        if (menuEntry == null)
                            continue;

                        var prefixProperty = menuEntry.FindPropertyRelative("prefix");
                        string normalizedPrefix = prefixProperty == null
                            ? string.Empty
                            : NormalizeMenuPathSegment(prefixProperty.stringValue);

                        if (!string.IsNullOrEmpty(normalizedPrefix))
                            paths.Add(normalizedPrefix);

                        var menuObjRef = FindVrcFuryMenuObjectReference(menuEntry);
                        var menuAsset = menuObjRef != null ? menuObjRef.objectReferenceValue as VRCExpressionsMenu : null;
                        if (menuAsset != null)
                        {
                            var visitedMenus = new HashSet<VRCExpressionsMenu>();
                            CollectVrcFuryMenuPathsRecursive(menuAsset, normalizedPrefix, paths, visitedMenus);
                        }
                    }
                }

                // Toggle / SPS / other features: iterate ALL string properties under
                // "content" whose name starts with "menu". This is robust across VRCFury
                // versions without hardcoding per-type field names like "menuPath".
                ScanVrcFuryContentForMenuPaths(so, paths);
            }

            return paths
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetVrcFuryReferencedParameterNames(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return Array.Empty<string>();

            var names = new HashSet<string>(StringComparer.Ordinal);
            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                var prmsProperty = so.FindProperty("content.prms");
                if (prmsProperty == null || !prmsProperty.isArray)
                    continue;

                for (int entryIndex = 0; entryIndex < prmsProperty.arraySize; entryIndex++)
                {
                    var entry = prmsProperty.GetArrayElementAtIndex(entryIndex);
                    if (entry == null)
                        continue;

                    var parametersRefProp = FindVrcFuryParametersObjectReference(entry);
                    var parametersIdProp = entry.FindPropertyRelative("parameters.id");

                    VRCExpressionParameters referencedParams = null;
                    if (parametersRefProp != null)
                        referencedParams = parametersRefProp.objectReferenceValue as VRCExpressionParameters;

                    if (referencedParams == null && parametersIdProp != null)
                    {
                        string referencedPath = ParseVrcFuryReferencePath(parametersIdProp.stringValue);
                        if (!string.IsNullOrWhiteSpace(referencedPath))
                            referencedParams = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(referencedPath);
                    }

                    if (referencedParams?.parameters == null)
                        continue;

                    for (int paramIndex = 0; paramIndex < referencedParams.parameters.Length; paramIndex++)
                    {
                        var param = referencedParams.parameters[paramIndex];
                        if (param == null || string.IsNullOrWhiteSpace(param.name))
                            continue;

                        if (param.valueType != VRCExpressionParameters.ValueType.Bool
                            && param.valueType != VRCExpressionParameters.ValueType.Int
                            && param.valueType != VRCExpressionParameters.ValueType.Float)
                            continue;

                        names.Add(param.name);
                    }
                }
            }

            return names
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static SerializedProperty FindVrcFuryParametersObjectReference(SerializedProperty prmsEntry)
        {
            if (prmsEntry == null)
                return null;

            var direct = prmsEntry.FindPropertyRelative("parameters.objRef");
            if (direct != null)
                return direct;

            return prmsEntry.FindPropertyRelative("parameters");
        }

        private static string ParseVrcFuryReferencePath(string serializedId)
        {
            if (string.IsNullOrWhiteSpace(serializedId))
                return string.Empty;

            string trimmed = serializedId.Trim();
            int split = trimmed.IndexOf('|');
            if (split >= 0 && split < trimmed.Length - 1)
                return trimmed.Substring(split + 1).Trim();

            return trimmed;
        }

        /// <summary>
        /// Iterates all serialized string properties under a VRCFury component's "content"
        /// managed reference and collects parent path segments from any property whose name
        /// starts with "menu". Works across VRCFury versions (Toggle, SPS, etc.) without
        /// hardcoding per-type field names.
        /// </summary>
        private static void ScanVrcFuryContentForMenuPaths(SerializedObject so, HashSet<string> paths)
        {
            var contentProp = so.FindProperty("content");
            if (contentProp == null)
                return;

            var it = contentProp.Copy();

            // Enter the managed reference's first child property.
            if (!it.Next(true))
                return;

            int baseDepth = contentProp.depth;

            while (it.depth > baseDepth)
            {
                if (it.propertyType == SerializedPropertyType.String)
                {
                    string val = it.stringValue;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        string lowerName = it.name.ToLowerInvariant();

                        // Match fields whose name starts with "menu" (e.g. FullController
                        // menuPath variants), OR known path/name carriers used by VRCFury
                        // features:
                        //   - name with slash (Toggle-style menu/item path)
                        //   - *Path fields (e.g. MoveMenuItem.toPath, legacy content.path)
                        bool isMenuField = lowerName.StartsWith("menu", StringComparison.Ordinal);
                        bool isNamePath = lowerName == "name" && val.IndexOf('/') >= 0;
                        bool isPathField = lowerName.EndsWith("path", StringComparison.Ordinal) && val.IndexOf('/') >= 0;

                        if (isMenuField || isNamePath || isPathField)
                        {
                            string[] segs = val.Split('/');
                            // For Toggle "name" fields the last segment is the item name, not a
                            // folder. Stop one short so only real parent menus are offered.
                            // For menu/path destination fields (menuPath, toPath, path), include
                            // all segments because the full value is itself a folder path.
                            int segLimit = isNamePath ? segs.Length - 1 : segs.Length;
                            var sb = new System.Text.StringBuilder();
                            for (int si = 0; si < segLimit; si++)
                            {
                                string seg = NormalizeMenuPathSegment(segs[si]);
                                if (string.IsNullOrEmpty(seg)) continue;
                                if (sb.Length > 0) sb.Append('/');
                                sb.Append(seg);
                                paths.Add(sb.ToString());
                            }
                        }
                    }
                }

                // Enter children up to 4 levels deep inside content; skip deeper subtrees.
                if (!it.Next(it.depth < baseDepth + 4))
                    break;
            }
        }

        private static SerializedProperty FindVrcFuryMenuObjectReference(SerializedProperty menuEntry)
        {
            if (menuEntry == null)
                return null;

            // Most common FullController schema path.
            var direct = menuEntry.FindPropertyRelative("menu.objRef");
            if (direct != null)
                return direct;

            // Fallback for nested serialized layouts.
            var menuProperty = menuEntry.FindPropertyRelative("menu");
            if (menuProperty == null)
                return null;

            return menuProperty.FindPropertyRelative("objRef");
        }

        private static void CollectVrcFuryMenuPathsRecursive(
            VRCExpressionsMenu menu,
            string parentPath,
            HashSet<string> paths,
            HashSet<VRCExpressionsMenu> visitedMenus)
        {
            if (menu == null || !visitedMenus.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                    continue;

                string segment = NormalizeMenuPathSegment(control.name);
                string fullPath = string.IsNullOrEmpty(parentPath)
                    ? segment
                    : (string.IsNullOrEmpty(segment) ? parentPath : $"{parentPath}/{segment}");

                if (!string.IsNullOrEmpty(fullPath))
                    paths.Add(fullPath);

                CollectVrcFuryMenuPathsRecursive(control.subMenu, fullPath, paths, visitedMenus);
            }
        }

        private static void CollectSubmenuPathsRecursive(
            VRCExpressionsMenu menu,
            string parentPath,
            HashSet<string> allPaths,
            HashSet<string> parentPaths,
            HashSet<VRCExpressionsMenu> visitedMenus)
        {
            if (menu == null || !visitedMenus.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                    continue;

                string segment = NormalizeMenuPathSegment(control.name);
                string fullPath = string.IsNullOrEmpty(parentPath)
                    ? segment
                    : (string.IsNullOrEmpty(segment) ? parentPath : $"{parentPath}/{segment}");

                if (string.IsNullOrEmpty(fullPath))
                    continue;

                allPaths.Add(fullPath);
                if (HasSubmenuChildren(control.subMenu))
                    parentPaths.Add(fullPath);

                CollectSubmenuPathsRecursive(control.subMenu, fullPath, allPaths, parentPaths, visitedMenus);
            }
        }

        private static bool HasSubmenuChildren(VRCExpressionsMenu menu)
        {
            if (menu == null || menu.controls == null)
                return false;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control != null && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                    return true;
            }

            return false;
        }

        private static string NormalizeMenuPathSegment(string value)
        {
            string normalized = NormalizeOptionalString(value)
                .Replace('\\', '/')
                .Trim('/');

            return normalized;
        }

        private void DrawRootIconSettings(ASMLiteComponent component)
        {
            bool useCustomRootIcon = component ? component.useCustomRootIcon : _pendingUseCustomRootIcon;
            bool newUseCustomRootIcon = EditorGUILayout.ToggleLeft("Use custom root icon", useCustomRootIcon);

            if (component)
                SetComponentBool(component, "Toggle ASM-Lite Custom Root Icon", ref component.useCustomRootIcon, newUseCustomRootIcon);
            else
                _pendingUseCustomRootIcon = newUseCustomRootIcon;

            if (!newUseCustomRootIcon)
                return;

            Texture2D currentRootIcon = component ? component.customRootIcon : _pendingCustomRootIcon;
            Texture2D newRootIcon = (Texture2D)EditorGUILayout.ObjectField("Root Icon", currentRootIcon, typeof(Texture2D), false);

            if (component)
            {
                SetComponentTexture(component, "Change ASM-Lite Root Icon", ref component.customRootIcon, newRootIcon);
            }
            else
            {
                _pendingCustomRootIcon = newRootIcon;
            }

            EditorGUILayout.HelpBox(
                "If this is empty, ASM-Lite uses its built-in root icon.",
                MessageType.None);
        }

        private void DrawIconMode(ASMLiteComponent component)
        {
            IconMode currentMode = component ? component.iconMode : _pendingIconMode;
            int currentGearIndex = component ? component.selectedGearIndex : _pendingSelectedGearIndex;

            // Slot icon mode selector intentionally excludes Custom.
            // Existing Custom values are migrated to MultiColor the first time this UI is drawn.
            if (currentMode == IconMode.Custom)
            {
                currentMode = IconMode.MultiColor;
                if (component)
                {
                    Undo.RecordObject(component, "Migrate ASM-Lite Slot Icon Mode");
                    component.iconMode = currentMode;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingIconMode = currentMode;
                }
            }

            var modeOptions = new[] { "MultiColor", "SameColor" };
            int currentModeIndex = currentMode == IconMode.SameColor ? 1 : 0;
            int newModeIndex = EditorGUILayout.Popup("Icon Mode", currentModeIndex, modeOptions);
            IconMode newMode = newModeIndex == 1 ? IconMode.SameColor : IconMode.MultiColor;

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

            if (newMode == IconMode.SameColor)
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
                return;
            }

            EditorGUILayout.HelpBox(
                SlotColorLegendHelpText,
                MessageType.None);
        }

        private void DrawSlotIconSelectors(ASMLiteComponent component)
        {
            int slotCount = component ? component.slotCount : _pendingSlotCount;
            Texture2D[] currentCustomIcons = component ? component.customIcons : _pendingCustomIcons;

            if (currentCustomIcons == null || currentCustomIcons.Length != slotCount)
            {
                var resized = new Texture2D[slotCount];
                if (currentCustomIcons != null)
                {
                    int copy = Mathf.Min(currentCustomIcons.Length, slotCount);
                    Array.Copy(currentCustomIcons, resized, copy);
                }

                currentCustomIcons = resized;
                if (component)
                {
                    Undo.RecordObject(component, "Resize ASM-Lite Slot Icons");
                    component.customIcons = resized;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingCustomIcons = resized;
                }
            }

            for (int i = 0; i < slotCount; i++)
            {
                Texture2D newTex = (Texture2D)EditorGUILayout.ObjectField(
                    string.Format(PresetIconFieldLabelFormat, i + 1),
                    currentCustomIcons[i],
                    typeof(Texture2D),
                    allowSceneObjects: false);

                if (newTex == currentCustomIcons[i])
                    continue;

                currentCustomIcons[i] = newTex;
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Slot Icon");
                    component.customIcons[i] = newTex;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingCustomIcons[i] = newTex;
                }
            }

            EditorGUILayout.HelpBox(
                PresetIconOverridesHelpText,
                MessageType.None);
        }

        /// <summary>
        /// Draws Action Icon controls (Save/Load/Clear). Mode auto-derived:
        /// any assigned icon => Custom, all empty => Default.
        /// </summary>
        private void DrawActionIcons(ASMLiteComponent component)
        {
            Texture2D currentSave = component ? component.customSaveIcon : _pendingCustomSaveIcon;
            Texture2D currentLoad = component ? component.customLoadIcon : _pendingCustomLoadIcon;
            Texture2D currentClear = component ? component.customClearIcon : _pendingCustomClearIcon;

            var newSave = (Texture2D)EditorGUILayout.ObjectField(
                "Save Icon", currentSave, typeof(Texture2D), allowSceneObjects: false);
            var newLoad = (Texture2D)EditorGUILayout.ObjectField(
                "Load Icon", currentLoad, typeof(Texture2D), allowSceneObjects: false);
            var newClear = (Texture2D)EditorGUILayout.ObjectField(
                ClearPresetIconFieldLabel, currentClear, typeof(Texture2D), allowSceneObjects: false);

            ActionIconMode desiredMode = (newSave != null || newLoad != null || newClear != null)
                ? ActionIconMode.Custom
                : ActionIconMode.Default;

            if (component)
            {
                bool changed = newSave != currentSave || newLoad != currentLoad || newClear != currentClear;
                bool modeChanged = component.actionIconMode != desiredMode;

                if (changed || modeChanged)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Action Icons");
                    component.customSaveIcon = newSave;
                    component.customLoadIcon = newLoad;
                    component.customClearIcon = newClear;
                    component.actionIconMode = desiredMode;
                    EditorUtility.SetDirty(component);
                }
            }
            else
            {
                _pendingCustomSaveIcon = newSave;
                _pendingCustomLoadIcon = newLoad;
                _pendingCustomClearIcon = newClear;
                _pendingActionIconMode = desiredMode;
            }

            EditorGUILayout.HelpBox(
                "Leave any icon field empty to use ASM-Lite's built-in icon for that action.",
                MessageType.None);
        }

        // ── Wheel Preview ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves and caches the icon textures used by the preview wheel.
        /// Exits immediately if the settings signature is unchanged.
        /// </summary>
        private void RefreshPreviewCache(
            int slotCount, IconMode iconMode, int gearIndex,
            bool useCustomSlotIcons,
            ActionIconMode actionIconMode,
            Texture2D[] customIcons, Texture2D customSave, Texture2D customLoad, Texture2D customClear)
        {
            int modeInt       = (int)iconMode;
            int actionModeInt = (int)actionIconMode;

            if (_previewFallback == null)
            {
                _previewFallback = new Texture2D(1, 1);
                _previewFallback.SetPixel(0, 0, new Color(0.35f, 0.35f, 0.35f));
                _previewFallback.Apply();
            }

            // Load bundled defaults once; used for preview fallback even in custom mode.
            _cachedIconSave   ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconSave);
            _cachedIconLoad   ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconLoad);
            _cachedIconClear  ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconReset);
            _cachedFlowArrow  ??= AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.staples.asm-lite/Icons/FlowArrow.png");

            Texture2D bundledSave   = _cachedIconSave ?? _previewFallback;
            Texture2D bundledLoad   = _cachedIconLoad ?? _previewFallback;
            Texture2D bundledClear  = _cachedIconClear ?? _previewFallback;

            bool dirty = _previewSlotCount      != slotCount
                      || _previewIconMode       != modeInt
                      || _previewGearIndex      != gearIndex
                      || _previewActionIconMode != actionModeInt;

            if (!dirty && _previewGearTextures != null
                && _previewGearTextures.Length == slotCount)
            {
                for (int i = 0; i < slotCount; i++)
                {
                    Texture2D expected;
                    if (useCustomSlotIcons
                        && customIcons != null
                        && i < customIcons.Length
                        && customIcons[i] != null)
                    {
                        expected = customIcons[i];
                    }
                    else
                    {
                        if (iconMode == IconMode.SameColor)
                        {
                            expected = AssetDatabase.LoadAssetAtPath<Texture2D>(
                                ASMLiteAssetPaths.GearIconPaths[gearIndex]) ?? _previewFallback;
                        }
                        else
                        {
                            expected = AssetDatabase.LoadAssetAtPath<Texture2D>(
                                ASMLiteAssetPaths.GearIconPaths[i % ASMLiteAssetPaths.GearIconPaths.Length]) ?? _previewFallback;
                        }
                    }

                    if (_previewGearTextures[i] != expected) { dirty = true; break; }
                }

                Texture2D expectedSave = actionIconMode == ActionIconMode.Custom
                    ? (customSave ?? bundledSave)
                    : bundledSave;
                Texture2D expectedLoad = actionIconMode == ActionIconMode.Custom
                    ? (customLoad ?? bundledLoad)
                    : bundledLoad;
                Texture2D expectedClear = actionIconMode == ActionIconMode.Custom
                    ? (customClear ?? bundledClear)
                    : bundledClear;

                if (_previewSaveIcon != expectedSave
                    || _previewLoadIcon != expectedLoad
                    || _previewClearIcon != expectedClear)
                    dirty = true;
            }
            else dirty = true;

            if (!dirty) return;

            _previewGearTextures = new Texture2D[slotCount];
            for (int slot = 1; slot <= slotCount; slot++)
            {
                Texture2D tex = null;
                int idx = slot - 1;

                if (useCustomSlotIcons
                    && customIcons != null
                    && idx < customIcons.Length
                    && customIcons[idx] != null)
                {
                    tex = customIcons[idx];
                }
                else if (iconMode == IconMode.SameColor)
                {
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        ASMLiteAssetPaths.GearIconPaths[gearIndex]);
                }
                else
                {
                    tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                        ASMLiteAssetPaths.GearIconPaths[idx % ASMLiteAssetPaths.GearIconPaths.Length]);
                }

                _previewGearTextures[idx] = tex != null ? tex : _previewFallback;
            }

            if (actionIconMode == ActionIconMode.Custom)
            {
                _previewSaveIcon  = customSave  != null ? customSave  : bundledSave;
                _previewLoadIcon  = customLoad  != null ? customLoad  : bundledLoad;
                _previewClearIcon = customClear != null ? customClear : bundledClear;
            }
            else
            {
                _previewSaveIcon  = bundledSave;
                _previewLoadIcon  = bundledLoad;
                _previewClearIcon = bundledClear;
            }

            _previewSlotCount      = slotCount;
            _previewIconMode       = modeInt;
            _previewGearIndex      = gearIndex;
            _previewActionIconMode = actionModeInt;
        }

        /// <summary>
        /// Draws expression menu preview flow: Root Menu → Slots Menu → Action Submenu.
        /// All three dials use same size and sit in one horizontal flow with arrows.
        /// </summary>
        private void DrawWheelPreview()
        {
            var component = GetOrRefreshComponent();

            int            slotCount   = component ? component.slotCount          : _pendingSlotCount;
            IconMode       iconMode    = component ? component.iconMode            : _pendingIconMode;
            int            gearIndex   = component ? component.selectedGearIndex   : _pendingSelectedGearIndex;
            bool           useCustomSlotIcons = component ? component.useCustomSlotIcons : _pendingUseCustomSlotIcons;
            ActionIconMode actionMode  = (component != null && component.useCustomSlotIcons)
                ? component.actionIconMode
                : (_pendingUseCustomSlotIcons ? _pendingActionIconMode : ActionIconMode.Default);
            Texture2D[]    customIcons = component ? component.customIcons         : _pendingCustomIcons;
            Texture2D      customSave  = component ? component.customSaveIcon      : _pendingCustomSaveIcon;
            Texture2D      customLoad  = component ? component.customLoadIcon      : _pendingCustomLoadIcon;
            Texture2D      customClear = component ? component.customClearIcon     : _pendingCustomClearIcon;

            RefreshPreviewCache(slotCount, iconMode, gearIndex, useCustomSlotIcons, actionMode,
                customIcons, customSave, customLoad, customClear);

            if (_previewBackIcon == null)
                _previewBackIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    ASMLiteAssetPaths.IconBackArrow) ?? _previewFallback;

            Texture2D rootFallbackIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconPresets) ?? _previewFallback;
            Texture2D rootPreviewIcon = component
                ? ASMLiteBuilder.ResolveEffectiveRootControlIcon(component, rootFallbackIcon)
                : ((_pendingUseCustomRootIcon && _pendingCustomRootIcon != null) ? _pendingCustomRootIcon : rootFallbackIcon);
            string rootPreviewName = ResolveEffectiveRootNameForPreview(component);
            string[] actionLabels = ResolveEffectiveActionLabelsForPreview(component);

            EditorGUILayout.LabelField("Expression Menu Preview", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(PreviewFlowSubtitle, EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(6f);

            float availWidth = EditorGUIUtility.currentViewWidth - 40f;
            const float connectorWidth = 60f;
            const float gap = 8f;
            const float titleHeight = 16f;

            float dialSize = Mathf.Clamp((availWidth - (connectorWidth * 2f) - (gap * 4f)) / 3f, 120f, 220f);
            float rootIconSize = Mathf.Clamp(dialSize * 0.22f, 28f, 54f);

            float totalWidth = dialSize * 3f + connectorWidth * 2f + gap * 4f;
            float rowHeight = titleHeight + dialSize + 2f;
            Rect rowRect = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));

            if (Event.current.type != EventType.Repaint)
                return;

            float startX = rowRect.x + Mathf.Max(0f, (rowRect.width - totalWidth) * 0.5f);
            float y = rowRect.y;

            Rect rootDialRect = new Rect(startX, y + titleHeight, dialSize, dialSize);
            Rect arrow1Rect = new Rect(rootDialRect.xMax + gap, rootDialRect.y, connectorWidth, dialSize);
            Rect presetsDialRect = new Rect(arrow1Rect.xMax + gap, rootDialRect.y, dialSize, dialSize);
            Rect arrow2Rect = new Rect(presetsDialRect.xMax + gap, rootDialRect.y, connectorWidth, dialSize);
            Rect actionDialRect = new Rect(arrow2Rect.xMax + gap, rootDialRect.y, dialSize, dialSize);

            var titleStyle = new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.UpperCenter };
            GUI.Label(new Rect(rootDialRect.x, y, rootDialRect.width, titleHeight), "Root Menu", titleStyle);
            GUI.Label(new Rect(presetsDialRect.x, y, presetsDialRect.width, titleHeight), PreviewMiddleDialTitle, titleStyle);
            GUI.Label(new Rect(actionDialRect.x, y, actionDialRect.width, titleHeight), "Action Submenu", titleStyle);

            if (_mainWheelIcons == null || _mainWheelIcons.Length != slotCount + 1
                || _mainWheelLabels == null || _mainWheelLabels.Length != slotCount + 1)
            {
                _mainWheelIcons  = new Texture2D[slotCount + 1];
                _mainWheelLabels = new string[slotCount + 1];
            }

            _mainWheelLabels[0] = "Back";
            for (int i = 0; i < slotCount; i++)
            {
                _mainWheelLabels[i + 1] = component
                    ? ASMLiteBuilder.ResolveEffectivePresetControlName(component, i + 1)
                    : ResolveEffectivePendingPresetLabelForPreview(i);
            }

            _mainWheelIcons[0] = _previewBackIcon;
            for (int i = 0; i < slotCount; i++)
                _mainWheelIcons[i + 1] = _previewGearTextures[i];

            DrawRootDialPreview(rootDialRect, rootPreviewIcon, rootPreviewName, rootIconSize);
            DrawFlowArrow(arrow1Rect);
            DrawRadialWheel(presetsDialRect, _mainWheelIcons, _mainWheelLabels);
            DrawFlowArrow(arrow2Rect);
            DrawRadialWheel(actionDialRect, new[] { _previewBackIcon, _previewSaveIcon, _previewLoadIcon, _previewClearIcon }, actionLabels);
        }

        private void DrawRootDialPreview(Rect cropRect, Texture2D icon, string rootName, float iconSize)
        {
            // Clip all drawing to crop rect so overhang is naturally cut off.
            GUI.BeginGroup(cropRect);

            // Zoomed crop of left wheel segment (Slot-3-like wedge).
            float cx = cropRect.width * 0.90f;
            float cy = cropRect.height * 0.50f;

            // Keep original circle scale; clipping removes overhang.
            float outerR = Mathf.Min(cropRect.width, cropRect.height) * 0.90f;
            float innerR = outerR / 3f;

            var oldHandles = Handles.color;

            // Fill dial body only (outside curve remains transparent).
            Handles.color = s_wheelColorMain;
            Handles.DrawSolidDisc(new Vector3(cx, cy, 0f), Vector3.forward, outerR - 1f);

            Handles.color = new Color(s_wheelColorBorder.r, s_wheelColorBorder.g, s_wheelColorBorder.b, 0.55f);

            float a1 = Mathf.Deg2Rad * 135f;
            float a2 = Mathf.Deg2Rad * 225f;
            var p1Inner = new Vector3(cx + Mathf.Cos(a1) * innerR, cy + Mathf.Sin(a1) * innerR, 0f);
            var p1Outer = new Vector3(cx + Mathf.Cos(a1) * (outerR - 1f), cy + Mathf.Sin(a1) * (outerR - 1f), 0f);
            var p2Inner = new Vector3(cx + Mathf.Cos(a2) * innerR, cy + Mathf.Sin(a2) * innerR, 0f);
            var p2Outer = new Vector3(cx + Mathf.Cos(a2) * (outerR - 1f), cy + Mathf.Sin(a2) * (outerR - 1f), 0f);
            Handles.DrawLine(p1Inner, p1Outer);
            Handles.DrawLine(p2Inner, p2Outer);

            Handles.color = s_wheelColorBorder;
            Handles.DrawWireArc(new Vector3(cx, cy, 0f), Vector3.forward, new Vector3(Mathf.Cos(a1), Mathf.Sin(a1), 0f), 90f, outerR - 1f);

            Handles.color = s_wheelColorInner;
            Handles.DrawSolidDisc(new Vector3(cx, cy, 0f), Vector3.forward, innerR);
            Handles.color = s_wheelColorBorder;
            Handles.DrawWireDisc(new Vector3(cx, cy, 0f), Vector3.forward, innerR);
            Handles.color = oldHandles;

            // Icon centered in zoomed wedge around 180°.
            float iconRadius = outerR * 0.66f;
            float ix = cx - iconRadius - iconSize * 0.5f;
            float iy = cy - iconSize * 0.5f;
            Rect iconRect = new Rect(ix, iy, iconSize, iconSize);
            GUI.DrawTexture(iconRect, icon ?? _previewFallback, ScaleMode.ScaleToFit, true);

            string displayName = string.IsNullOrWhiteSpace(rootName)
                ? ASMLiteBuilder.DefaultRootControlName
                : rootName;
            var centeredMini = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.UpperCenter
            };
            Rect labelRect = new Rect(iconRect.x - 24f, iconRect.yMax + 2f, iconRect.width + 48f, 16f);
            GUI.Label(labelRect, displayName, centeredMini);

            GUI.EndGroup();
        }

             private void DrawFlowArrow(Rect rect)
        {
            // Draw cached arrow icon centered in rect with small margin.
            if (_cachedFlowArrow != null)
            {
                float margin = 8f;
                float iconWidth = rect.width - margin * 2f;
                float iconHeight = rect.height - margin * 2f;
                Rect iconRect = new Rect(
                    rect.center.x - iconWidth * 0.5f,
                    rect.center.y - iconHeight * 0.5f,
                    iconWidth,
                    iconHeight);
                GUI.DrawTexture(iconRect, _cachedFlowArrow, ScaleMode.ScaleToFit);
            }
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

            // Fill dial body only (outside circle stays transparent).
            Handles.color = s_wheelColorMain;
            Handles.DrawSolidDisc(new Vector3(cx, cy), Vector3.forward, outerR - 1f);

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
            // Reuse cached GUIStyle. Only update fontSize (depends on scale).
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

        private int GetEffectiveBackedUpParameterCount(ASMLiteComponent component)
        {
            if (_selectedAvatar == null)
                return -1;

            var exprParams = _selectedAvatar.expressionParameters;
            if (exprParams == null || exprParams.parameters == null)
                return -1;

            bool useExclusions = component ? component.useParameterExclusions : _pendingUseParameterExclusions;
            if (!useExclusions)
            {
                var discovered = ASMLiteBuilder.GetFinalAvatarParams(_selectedAvatar, null, out _);
                return discovered?.Count ?? 0;
            }

            string[] rawExcluded = component
                ? SanitizeExcludedParameterNames(component.excludedParameterNames)
                : SanitizeExcludedParameterNames(_pendingExcludedParameterNames);

            var canonicalExcluded = new HashSet<string>(StringComparer.Ordinal);
            if (rawExcluded != null)
            {
                for (int i = 0; i < rawExcluded.Length; i++)
                {
                    var candidate = rawExcluded[i];
                    if (!string.IsNullOrWhiteSpace(candidate))
                        canonicalExcluded.Add(candidate);
                }
            }

            var expandedExcluded = ExpandExcludedNamesWithToggleMappingsForUi(canonicalExcluded);
            var filtered = ASMLiteBuilder.GetFinalAvatarParams(_selectedAvatar, expandedExcluded, out _);
            return filtered?.Count ?? 0;
        }

        private static HashSet<string> ExpandExcludedNamesWithToggleMappingsForUi(HashSet<string> excludedCanonicalNames)
        {
            var expanded = excludedCanonicalNames != null
                ? new HashSet<string>(excludedCanonicalNames, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            if (expanded.Count == 0)
                return expanded;

            var mappings = ASMLiteToggleNameBroker.GetLatestGlobalParamMappings();
            if (mappings == null || mappings.Length == 0)
                return expanded;

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (string.IsNullOrWhiteSpace(mapping.OriginalGlobalParam)
                    || string.IsNullOrWhiteSpace(mapping.AssignedGlobalParam))
                    continue;

                bool excludeOriginal = expanded.Contains(mapping.OriginalGlobalParam);
                bool excludeAssigned = expanded.Contains(mapping.AssignedGlobalParam);
                if (!excludeOriginal && !excludeAssigned)
                    continue;

                expanded.Add(mapping.OriginalGlobalParam);
                expanded.Add(mapping.AssignedGlobalParam);
            }

            return expanded;
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Use this section to confirm whether the avatar is attached, baked-only, or vendorized before taking action.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            var component = GetOrRefreshComponent();
            bool hasComponent = component;
            var toolState = GetAsmLiteToolState(_selectedAvatar, component);

            int? backedUpCount = null;
            bool parameterImportPending = false;

            if (hasComponent)
            {
                // Guard against mid-reimport state: expressionParameters or its
                // parameters array can be transiently null while Unity is importing.
                try
                {
                    int computedCount = GetEffectiveBackedUpParameterCount(component);
                    if (computedCount >= 0)
                        backedUpCount = computedCount;
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

                    parameterImportPending = true;
                }
            }

            bool hasToggleBrokerReport = ASMLiteToggleNameBroker.TryGetLatestEnrollmentReport(out var toggleBrokerReport);
            var snapshot = BuildStatusPanelSnapshot(new StatusPanelSnapshotInput(
                toolState,
                hasComponent,
                component != null ? component.slotCount : 0,
                _discoveredParamCount,
                backedUpCount,
                parameterImportPending,
                hasToggleBrokerReport,
                toggleBrokerReport.PreReservedNameCount,
                toggleBrokerReport.PreflightCollisionAdjustments,
                toggleBrokerReport.CandidateCollisionAdjustments));

            EditorGUILayout.HelpBox(BuildCombinedStatusMessage(snapshot), ToMessageType(GetCombinedStatusSeverity(snapshot)));
        }

        private static string ResolveStatusCopy(AsmLiteToolState toolState, bool hasComponent)
        {
            switch (toolState)
            {
                case AsmLiteToolState.PackageManaged:
                    return StatusPackageManagedText;
                case AsmLiteToolState.Vendorized:
                    return hasComponent ? StatusVendorizedAttachedText : StatusVendorizedDetachedText;
                case AsmLiteToolState.Detached:
                    return StatusDetachedText;
                default:
                    return StatusNotInstalledText;
            }
        }

        private static MessageType ResolveStatusMessageType(AsmLiteToolState toolState)
        {
            return toolState == AsmLiteToolState.NotInstalled
                ? MessageType.None
                : MessageType.Info;
        }

        internal enum StatusDetailSeverity
        {
            Neutral,
            Info,
            Warning,
            Error,
        }

        internal readonly struct StatusDetailEntry
        {
            public StatusDetailEntry(string text, StatusDetailSeverity severity)
            {
                Text = text ?? string.Empty;
                Severity = severity;
            }

            public string Text { get; }
            public StatusDetailSeverity Severity { get; }
        }

        internal readonly struct StatusPanelSnapshotInput
        {
            public StatusPanelSnapshotInput(
                AsmLiteToolState toolState,
                bool hasComponent,
                int slotCount,
                int discoveredParamCount,
                int? backedUpCount,
                bool parameterImportPending,
                bool hasToggleBrokerReport,
                int toggleBrokerPreReservedNameCount,
                int toggleBrokerPreflightCollisionAdjustments,
                int toggleBrokerCandidateCollisionAdjustments)
            {
                ToolState = toolState;
                HasComponent = hasComponent;
                SlotCount = slotCount;
                DiscoveredParamCount = discoveredParamCount;
                BackedUpCount = backedUpCount;
                ParameterImportPending = parameterImportPending;
                HasToggleBrokerReport = hasToggleBrokerReport;
                ToggleBrokerPreReservedNameCount = toggleBrokerPreReservedNameCount;
                ToggleBrokerPreflightCollisionAdjustments = toggleBrokerPreflightCollisionAdjustments;
                ToggleBrokerCandidateCollisionAdjustments = toggleBrokerCandidateCollisionAdjustments;
            }

            public AsmLiteToolState ToolState { get; }
            public bool HasComponent { get; }
            public int SlotCount { get; }
            public int DiscoveredParamCount { get; }
            public int? BackedUpCount { get; }
            public bool ParameterImportPending { get; }
            public bool HasToggleBrokerReport { get; }
            public int ToggleBrokerPreReservedNameCount { get; }
            public int ToggleBrokerPreflightCollisionAdjustments { get; }
            public int ToggleBrokerCandidateCollisionAdjustments { get; }
        }

        internal readonly struct StatusPanelSnapshot
        {
            public StatusPanelSnapshot(string summaryText, StatusDetailSeverity summarySeverity, StatusDetailEntry[] detailEntries)
            {
                SummaryText = summaryText ?? string.Empty;
                SummarySeverity = summarySeverity;
                DetailEntries = detailEntries ?? Array.Empty<StatusDetailEntry>();
            }

            public string SummaryText { get; }
            public StatusDetailSeverity SummarySeverity { get; }
            public StatusDetailEntry[] DetailEntries { get; }
        }

        internal static StatusPanelSnapshot BuildStatusPanelSnapshot(StatusPanelSnapshotInput input)
        {
            var details = new List<StatusDetailEntry>();

            if (input.HasComponent)
            {
                details.Add(new StatusDetailEntry(AttachedComponentInfoText, StatusDetailSeverity.Info));

                if (input.ParameterImportPending)
                {
                    details.Add(new StatusDetailEntry(ParameterImportPendingWarningText, StatusDetailSeverity.Warning));
                }
                else if (input.BackedUpCount.HasValue)
                {
                    details.Add(new StatusDetailEntry(
                        string.Format(AttachedCountSummaryFormat, input.BackedUpCount.Value, input.SlotCount),
                        StatusDetailSeverity.Info));

                    if (input.DiscoveredParamCount < 0)
                    {
                        details.Add(new StatusDetailEntry(
                            DescriptorCountSourceText,
                            StatusDetailSeverity.Neutral));
                    }
                }
                else
                {
                    details.Add(new StatusDetailEntry(
                        MissingExpressionParametersWarningText,
                        StatusDetailSeverity.Warning));
                }

                if (input.HasToggleBrokerReport)
                {
                    int totalAdjustments = input.ToggleBrokerPreflightCollisionAdjustments + input.ToggleBrokerCandidateCollisionAdjustments;
                    if (totalAdjustments > 0)
                    {
                        details.Add(new StatusDetailEntry(
                            string.Format(
                                ToggleBrokerCollisionWarningFormat,
                                input.ToggleBrokerPreReservedNameCount,
                                input.ToggleBrokerPreflightCollisionAdjustments,
                                input.ToggleBrokerCandidateCollisionAdjustments),
                            StatusDetailSeverity.Warning));
                    }
                    else
                    {
                        details.Add(new StatusDetailEntry(
                            string.Format(ToggleBrokerNoCollisionInfoFormat, input.ToggleBrokerPreReservedNameCount),
                            StatusDetailSeverity.Neutral));
                    }
                }
            }
            else if (input.ToolState == AsmLiteToolState.Detached || input.ToolState == AsmLiteToolState.Vendorized)
            {
                details.Add(new StatusDetailEntry(DetachedOrVendorizedNoComponentText, StatusDetailSeverity.Info));
            }
            else if (input.ToolState == AsmLiteToolState.NotInstalled)
            {
                details.Add(new StatusDetailEntry(NotInstalledNoComponentText, StatusDetailSeverity.Warning));
            }

            var summarySeverity = ResolveStatusMessageType(input.ToolState) == MessageType.None
                ? StatusDetailSeverity.Neutral
                : StatusDetailSeverity.Info;

            return new StatusPanelSnapshot(
                ResolveStatusCopy(input.ToolState, input.HasComponent),
                summarySeverity,
                details.ToArray());
        }

        internal static string BuildCombinedStatusMessage(StatusPanelSnapshot snapshot)
        {
            var lines = new List<string>();

            if (!string.IsNullOrWhiteSpace(snapshot.SummaryText))
                lines.Add(snapshot.SummaryText.Trim());

            for (int i = 0; i < snapshot.DetailEntries.Length; i++)
            {
                string detailText = snapshot.DetailEntries[i].Text;
                if (!string.IsNullOrWhiteSpace(detailText))
                    lines.Add($"• {detailText.Trim()}");
            }

            return string.Join("\n", lines);
        }

        internal static StatusDetailSeverity GetCombinedStatusSeverity(StatusPanelSnapshot snapshot)
        {
            StatusDetailSeverity highest = snapshot.SummarySeverity;

            for (int i = 0; i < snapshot.DetailEntries.Length; i++)
            {
                if ((int)snapshot.DetailEntries[i].Severity > (int)highest)
                    highest = snapshot.DetailEntries[i].Severity;
            }

            return highest;
        }

        private static MessageType ToMessageType(StatusDetailSeverity severity)
        {
            switch (severity)
            {
                case StatusDetailSeverity.Info:
                    return MessageType.Info;
                case StatusDetailSeverity.Warning:
                    return MessageType.Warning;
                case StatusDetailSeverity.Error:
                    return MessageType.Error;
                default:
                    return MessageType.None;
            }
        }

        internal enum NamingGroupFlowState
        {
            Attached,
            PendingInstall,
        }

        internal readonly struct NamingGroupSectionSnapshot
        {
            public NamingGroupSectionSnapshot(string header, string[] orderedFieldLabels)
            {
                Header = header ?? string.Empty;
                OrderedFieldLabels = orderedFieldLabels ?? Array.Empty<string>();
            }

            public string Header { get; }
            public string[] OrderedFieldLabels { get; }
        }

        internal readonly struct NamingGroupSnapshot
        {
            public NamingGroupSnapshot(NamingGroupFlowState flowState, NamingGroupSectionSnapshot[] sections, string fallbackGuidance)
            {
                FlowState = flowState;
                Sections = sections ?? Array.Empty<NamingGroupSectionSnapshot>();
                FallbackGuidance = fallbackGuidance ?? string.Empty;

                var flattened = new List<string>();
                for (int i = 0; i < Sections.Length; i++)
                {
                    var section = Sections[i];
                    for (int j = 0; j < section.OrderedFieldLabels.Length; j++)
                        flattened.Add(section.OrderedFieldLabels[j]);
                }

                OrderedFieldLabels = flattened.ToArray();
            }

            public NamingGroupFlowState FlowState { get; }
            public NamingGroupSectionSnapshot[] Sections { get; }
            public string[] OrderedFieldLabels { get; }
            public string FallbackGuidance { get; }
        }

        internal static NamingGroupSnapshot GetNamingGroupSnapshot(NamingGroupFlowState flowState, int presetCount)
        {
            int normalizedPresetCount = Mathf.Max(1, presetCount);

            var sections = new[]
            {
                new NamingGroupSectionSnapshot(NamingSectionRootHeader, new[] { RootMenuFieldLabel }),
                new NamingGroupSectionSnapshot(NamingSectionPresetHeader, BuildPresetNameFieldLabels(normalizedPresetCount)),
                new NamingGroupSectionSnapshot(
                    NamingSectionActionHeader,
                    new[] { SaveFieldLabel, LoadFieldLabel, ClearPresetLabel, ConfirmFieldLabel }),
            };

            return new NamingGroupSnapshot(flowState, sections, NameFallbackGuidanceText);
        }

        private static string[] BuildPresetNameFieldLabels(int presetCount)
        {
            int normalizedPresetCount = Mathf.Max(1, presetCount);
            var fieldLabels = new string[normalizedPresetCount];
            for (int i = 0; i < normalizedPresetCount; i++)
                fieldLabels[i] = string.Format(PresetNameLabelFormat, i + 1);

            return fieldLabels;
        }

        internal readonly struct TerminologySnapshot
        {
            public TerminologySnapshot(string[] alwaysVisibleCopy, string[] stateSpecificCopy)
            {
                AlwaysVisibleCopy = alwaysVisibleCopy ?? Array.Empty<string>();
                StateSpecificCopy = stateSpecificCopy ?? Array.Empty<string>();
            }

            public string[] AlwaysVisibleCopy { get; }
            public string[] StateSpecificCopy { get; }

            public IEnumerable<string> EnumerateAllCopy()
            {
                for (int i = 0; i < AlwaysVisibleCopy.Length; i++)
                    yield return AlwaysVisibleCopy[i];

                for (int i = 0; i < StateSpecificCopy.Length; i++)
                    yield return StateSpecificCopy[i];
            }
        }

        internal static TerminologySnapshot GetTerminologySnapshot(AsmLiteToolState toolState, bool hasComponent)
        {
            var alwaysVisible = new[]
            {
                s_slotCountLabelActive.text,
                s_slotCountLabelPending.text,
                s_slotCountLabelActive.tooltip,
                s_slotCountLabelPending.tooltip,
                ChangedPresetCountHelpText,
                SlotColorLegendHelpText,
                PreviewFlowSubtitle,
                PreviewMiddleDialTitle,
                PresetIconsFoldoutTitle,
                string.Format(PresetNameLabelFormat, 1),
                ClearPresetLabel,
                string.Format(PresetIconFieldLabelFormat, 1),
                ClearPresetIconFieldLabel,
                PresetIconOverridesHelpText,
                AttachedCountSummaryFormat,
                DetachDescriptionText,
            };

            var stateSpecific = new List<string>
            {
                ResolveStatusCopy(toolState, hasComponent),
            };

            if (toolState == AsmLiteToolState.Detached || toolState == AsmLiteToolState.Vendorized)
                stateSpecific.Add(DetachedOrVendorizedNoComponentText);

            if (toolState == AsmLiteToolState.NotInstalled)
                stateSpecific.Add(NotInstalledNoComponentText);

            return new TerminologySnapshot(alwaysVisible, stateSpecific.ToArray());
        }

        private void DrawActionButton()
        {
            var component = GetOrRefreshComponent();
            var toolState = GetAsmLiteToolState(_selectedAvatar, component);
            var hierarchy = BuildActionHierarchyContract(toolState, component != null, _showAdvancedActions);

            EditorGUILayout.LabelField("Primary Actions", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Normal workflow first. Maintenance and destructive actions stay below so the main path stays obvious.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Recommended", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginVertical("box");
            for (int i = 0; i < hierarchy.PrimaryActions.Length; i++)
                DrawActionControl(hierarchy.PrimaryActions[i], component, toolState);
            EditorGUILayout.EndVertical();

            if (!hierarchy.HasAdvancedActions)
                return;

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Maintenance / Advanced", EditorStyles.miniBoldLabel);
            EditorGUILayout.Space(2f);

            EditorGUILayout.BeginVertical("box");
            _showAdvancedActions = EditorGUILayout.Foldout(_showAdvancedActions, "Advanced Actions", true);
            if (_showAdvancedActions)
            {
                EditorGUILayout.Space(6f);
                for (int i = 0; i < hierarchy.AdvancedActions.Length; i++)
                    DrawActionControl(hierarchy.AdvancedActions[i], component, toolState);
            }
            else
            {
                EditorGUILayout.LabelField(
                    "Maintenance and destructive actions stay hidden until you expand Advanced.",
                    EditorStyles.wordWrappedMiniLabel);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActionControl(AsmLiteWindowAction action, ASMLiteComponent component, AsmLiteToolState toolState)
        {
            switch (action)
            {
                case AsmLiteWindowAction.AddPrefab:
                    if (GUILayout.Button("Add ASM-Lite Prefab", GUILayout.Height(40), GUILayout.ExpandWidth(true)))
                        EditorApplication.delayCall += AddPrefabToAvatar;
                    break;
                case AsmLiteWindowAction.Rebuild:
                    if (GUILayout.Button("Rebuild ASM-Lite", GUILayout.Height(40), GUILayout.MinWidth(220), GUILayout.ExpandWidth(true)))
                    {
                        var captured = component;
                        EditorApplication.delayCall += () => BakeAssets(captured);
                    }
                    break;
                case AsmLiteWindowAction.ReturnToPackageManaged:
                    DrawBakedOnlyReturnToPackageManagedAction();
                    break;
                case AsmLiteWindowAction.RemovePrefab:
                    DrawRemovePrefabAction(component);
                    break;
                case AsmLiteWindowAction.Detach:
                    DrawDetachAction(component);
                    break;
                case AsmLiteWindowAction.Vendorize:
                    DrawVendorizeAction(component, toolState);
                    break;
                case AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged:
                    DrawReturnAttachedVendorizedToPackageManagedAction();
                    break;
            }
        }

        private void DrawBakedOnlyReturnToPackageManagedAction()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Return to Package Managed Mode", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Re-attach the editable ASM-Lite prefab and return this avatar to package-managed workflow. " +
                "Keeps your current avatar content and restores normal ASM-Lite editing.",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Return to Package Managed", GUILayout.Height(32), GUILayout.ExpandWidth(true)))
                EditorApplication.delayCall += ReturnToPackageManaged;
            EditorGUILayout.EndVertical();
        }

        private void DrawRemovePrefabAction(ASMLiteComponent component)
        {
            var prevColor = GUI.color;
            GUI.color = new Color(1f, 0.45f, 0.45f);
            bool removeClicked = GUILayout.Button("Remove Prefab", GUILayout.Height(32), GUILayout.MinWidth(110));
            GUI.color = prevColor;
            if (!removeClicked)
                return;

            bool confirm = EditorUtility.DisplayDialog(
                "Remove ASM-Lite Prefab",
                "Are you sure you want to remove the ASM-Lite prefab from this avatar?\n\n" +
                "Any unsaved changes will be lost, but your avatar and expression parameters will not be affected.",
                "Remove", "Cancel");

            if (confirm)
                EditorApplication.delayCall += () => RemovePrefab(component);
        }

        private void DrawDetachAction(ASMLiteComponent component)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Detach ASM-Lite (Runtime-safe)", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(DetachDescriptionText, EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Detach ASM-Lite", GUILayout.Height(24)))
            {
                var captured = component;
                EditorApplication.delayCall += () => DetachAsmLite(captured, vendorizeToAssets: false);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawVendorizeAction(ASMLiteComponent component, AsmLiteToolState toolState)
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Vendorize ASM-Lite Payload", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Keep ASM-Lite attached and editable, but mirror generated payload files into Assets/ASM-Lite/<AvatarName> " +
                "and use those mirrored files instead of package generated assets.",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Vendorize (Keep Attached)", GUILayout.Height(24)))
            {
                var captured = component;
                EditorApplication.delayCall += () => VendorizeAsmLite(captured);
            }

            if (toolState == AsmLiteToolState.Vendorized)
            {
                string currentVendorizedPath = NormalizeOptionalString(component.vendorizedGeneratedAssetsPath);
                if (string.IsNullOrWhiteSpace(currentVendorizedPath))
                    currentVendorizedPath = "(path pending sync)";

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Current vendorized folder:", EditorStyles.miniBoldLabel);
                EditorGUILayout.SelectableLabel(currentVendorizedPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawReturnAttachedVendorizedToPackageManagedAction()
        {
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Return This Avatar to Package Managed", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Stop using the vendorized payload folder for this attached ASM-Lite component and return to package-managed generated assets.",
                EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("Return This Avatar to Package Managed", GUILayout.Height(22)))
                EditorApplication.delayCall += ReturnToPackageManaged;
            EditorGUILayout.EndVertical();
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        // Per-frame component cache: refreshed once per OnGUI call, not once per draw section.
        private int _lastRefreshFrame = -1;

        internal enum AsmLiteToolState
        {
            NotInstalled,
            PackageManaged,
            Detached,
            Vendorized,
        }

        internal enum AsmLiteWindowAction
        {
            AddPrefab,
            Rebuild,
            ReturnToPackageManaged,
            RemovePrefab,
            Detach,
            Vendorize,
            ReturnAttachedVendorizedToPackageManaged,
        }

        internal readonly struct AsmLiteActionHierarchy
        {
            public AsmLiteActionHierarchy(AsmLiteWindowAction[] primaryActions, AsmLiteWindowAction[] advancedActions, bool advancedDisclosureExpanded)
            {
                PrimaryActions = primaryActions ?? Array.Empty<AsmLiteWindowAction>();
                AdvancedActions = advancedActions ?? Array.Empty<AsmLiteWindowAction>();
                AdvancedDisclosureExpanded = advancedDisclosureExpanded;
            }

            public AsmLiteWindowAction[] PrimaryActions { get; }
            public AsmLiteWindowAction[] AdvancedActions { get; }
            public bool AdvancedDisclosureExpanded { get; }
            public bool HasAdvancedActions => AdvancedActions.Length > 0;

            public bool HasPrimaryAction(AsmLiteWindowAction action)
            {
                for (int i = 0; i < PrimaryActions.Length; i++)
                {
                    if (PrimaryActions[i] == action)
                        return true;
                }

                return false;
            }

            public bool HasAdvancedAction(AsmLiteWindowAction action)
            {
                for (int i = 0; i < AdvancedActions.Length; i++)
                {
                    if (AdvancedActions[i] == action)
                        return true;
                }

                return false;
            }
        }

        internal AsmLiteActionHierarchy GetActionHierarchyContract()
        {
            var component = GetOrRefreshComponent();
            var toolState = GetAsmLiteToolState(_selectedAvatar, component);
            return BuildActionHierarchyContract(toolState, component != null, _showAdvancedActions);
        }

        internal static AsmLiteActionHierarchy BuildActionHierarchyContract(AsmLiteToolState toolState, bool hasComponent, bool advancedDisclosureExpanded)
        {
            if (hasComponent)
            {
                var primaryActions = new[] { AsmLiteWindowAction.Rebuild };

                if (toolState == AsmLiteToolState.Vendorized)
                {
                    return new AsmLiteActionHierarchy(
                        primaryActions,
                        new[]
                        {
                            AsmLiteWindowAction.RemovePrefab,
                            AsmLiteWindowAction.Detach,
                            AsmLiteWindowAction.Vendorize,
                            AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged,
                        },
                        advancedDisclosureExpanded);
                }

                return new AsmLiteActionHierarchy(
                    primaryActions,
                    new[]
                    {
                        AsmLiteWindowAction.RemovePrefab,
                        AsmLiteWindowAction.Detach,
                        AsmLiteWindowAction.Vendorize,
                    },
                    advancedDisclosureExpanded);
            }

            if (toolState == AsmLiteToolState.Detached || toolState == AsmLiteToolState.Vendorized)
            {
                return new AsmLiteActionHierarchy(
                    new[] { AsmLiteWindowAction.ReturnToPackageManaged },
                    Array.Empty<AsmLiteWindowAction>(),
                    advancedDisclosureExpanded);
            }

            return new AsmLiteActionHierarchy(
                new[] { AsmLiteWindowAction.AddPrefab },
                Array.Empty<AsmLiteWindowAction>(),
                advancedDisclosureExpanded);
        }

        internal static bool IsMaintenanceAction(AsmLiteWindowAction action)
        {
            return action == AsmLiteWindowAction.RemovePrefab
                || action == AsmLiteWindowAction.Detach
                || action == AsmLiteWindowAction.Vendorize
                || action == AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged;
        }

        internal static AsmLiteToolState GetAsmLiteToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            if (component != null)
                return component.useVendorizedGeneratedAssets ? AsmLiteToolState.Vendorized : AsmLiteToolState.PackageManaged;
            if (avatar == null)
                return AsmLiteToolState.NotInstalled;
            if (HasVendorizedAsmLiteReferences(avatar))
                return AsmLiteToolState.Vendorized;
            if (HasAsmLiteRuntimeMarkers(avatar))
                return AsmLiteToolState.Detached;
            return AsmLiteToolState.NotInstalled;
        }

        private static bool HasVendorizedAsmLiteReferences(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            const string vendorPrefix = "Assets/ASM-Lite/";

            string exprPath = avatar.expressionParameters ? AssetDatabase.GetAssetPath(avatar.expressionParameters)?.Replace('\\', '/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(exprPath) && exprPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                return true;

            string menuPath = avatar.expressionsMenu ? AssetDatabase.GetAssetPath(avatar.expressionsMenu)?.Replace('\\', '/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(menuPath) && menuPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                return true;

            if (avatar.expressionsMenu != null && avatar.expressionsMenu.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control?.subMenu == null)
                        continue;

                    string subPath = AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(subPath) && subPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                        return true;
                }
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var ctrl = avatar.baseAnimationLayers[i].animatorController;
                if (!ctrl)
                    continue;

                string ctrlPath = AssetDatabase.GetAssetPath(ctrl)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(ctrlPath) && ctrlPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasAsmLiteRuntimeMarkers(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            var expr = avatar.expressionParameters;
            if (expr?.parameters != null)
            {
                for (int i = 0; i < expr.parameters.Length; i++)
                {
                    var p = expr.parameters[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.name))
                        continue;
                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(p.name, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var ctrl = avatar.baseAnimationLayers[i].animatorController as UnityEditor.Animations.AnimatorController;
                if (ctrl == null)
                    continue;

                for (int j = 0; j < ctrl.layers.Length; j++)
                {
                    if (ctrl.layers[j].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        return true;
                }

                for (int j = 0; j < ctrl.parameters.Length; j++)
                {
                    string paramName = ctrl.parameters[j].name;
                    if (string.IsNullOrWhiteSpace(paramName))
                        continue;
                    if (paramName.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(paramName, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            if (avatar.expressionsMenu?.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    if (string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal))
                        return true;

                    string subPath = control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/') : string.Empty;
                    if (!string.IsNullOrWhiteSpace(subPath)
                        && (subPath.IndexOf("ASMLite_", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/ASM-Lite/", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/com.staples.asm-lite/", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }

            return false;
        }

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

        private static string[] CloneStrings(string[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<string>();

            var clone = new string[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static string[] EnsureSizedStringArray(string[] source, int size)
        {
            if (size <= 0)
                return Array.Empty<string>();

            if (source != null && source.Length == size)
                return source;

            var resized = new string[size];
            if (source != null)
                Array.Copy(source, resized, Mathf.Min(source.Length, size));

            for (int i = 0; i < resized.Length; i++)
                resized[i] ??= string.Empty;

            return resized;
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

        private static MonoBehaviour FindLiveVrcFuryComponent(ASMLiteComponent component)
        {
            if (component == null || component.gameObject == null)
                return null;

            var behaviors = component.gameObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviors.Length; i++)
            {
                var behavior = behaviors[i];
                if (behavior == null)
                    continue;

                var type = behavior.GetType();
                if (type == null)
                    continue;

                if (string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    return behavior;
            }

            return null;
        }

        private static bool TryRefreshLiveInstallPathPrefix(ASMLiteComponent component, string contextLabel)
        {
            if (component == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Cannot refresh install-path routing because the ASM-Lite component was null.");
                return false;
            }

            var vf = FindLiveVrcFuryComponent(component);
            if (vf == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Expected VF.Model.VRCFury component was not found on '{component.gameObject.name}'.");
                return false;
            }

            if (!ASMLiteBuilder.TrySyncInstallPathRouting(component))
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Failed to refresh install-path routing on '{component.gameObject.name}'.");
                return false;
            }

            var effectivePrefix = ASMLiteFullControllerInstallPathHelper.ResolveEffectivePrefix(component);
            if (string.IsNullOrEmpty(effectivePrefix))
                Debug.Log($"[ASM-Lite] {contextLabel}: refreshed install-path routing to root on '{component.gameObject.name}'.");
            else
                Debug.Log($"[ASM-Lite] {contextLabel}: refreshed install-path routing to '{effectivePrefix}' on '{component.gameObject.name}'.");

            return true;
        }

        private static string SanitizePathFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Avatar";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (invalid.Contains(c))
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            string cleaned = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Avatar" : cleaned;
        }

        private static string EnsureAssetFolder(string parent, string child)
        {
            string normalizedParent = parent.Replace('\\', '/').TrimEnd('/');
            string candidate = normalizedParent + "/" + child;
            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(normalizedParent, child);
            return candidate;
        }

        private static string EnsureVendorizeRootFolder(VRCAvatarDescriptor avatar)
        {
            string root = EnsureAssetFolder("Assets", "ASM-Lite");
            string avatarFolder = EnsureAssetFolder(root, SanitizePathFragment(avatar != null ? avatar.gameObject.name : "Avatar"));
            return EnsureAssetFolder(avatarFolder, "GeneratedAssets");
        }

        private static void CopyAssetIfPresent(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
                return;

            if (!AssetDatabase.LoadMainAssetAtPath(sourcePath))
                return;

            AssetDatabase.DeleteAsset(destinationPath);
            AssetDatabase.CopyAsset(sourcePath, destinationPath);
        }

        private static void RetargetMenuGeneratedSubmenus(VRCExpressionsMenu menu, string sourcePrefix, string destinationPrefix, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.subMenu == null)
                    continue;

                string subPath = AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(subPath)
                    && subPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    string fileName = Path.GetFileName(subPath);
                    string newPath = destinationPrefix + "/" + fileName;
                    var replaced = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(newPath);
                    if (replaced != null)
                    {
                        control.subMenu = replaced;
                        menu.controls[i] = control;
                        EditorUtility.SetDirty(menu);
                    }
                }

                RetargetMenuGeneratedSubmenus(control.subMenu, sourcePrefix, destinationPrefix, visited);
            }
        }

        private static bool TryVendorizeGeneratedAssetsToAvatarFolder(VRCAvatarDescriptor avatar, out string vendorizedDir)
        {
            vendorizedDir = string.Empty;
            if (avatar == null)
                return false;

            string sourcePrefix = ASMLiteAssetPaths.GeneratedDir.Replace('\\', '/').TrimEnd('/');
            string targetDir = EnsureVendorizeRootFolder(avatar);

            var generatedGuids = AssetDatabase.FindAssets(string.Empty, new[] { sourcePrefix });
            for (int i = 0; i < generatedGuids.Length; i++)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(generatedGuids[i])?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(sourcePath) || Directory.Exists(sourcePath))
                    continue;

                string fileName = Path.GetFileName(sourcePath);
                string destinationPath = targetDir + "/" + fileName;
                CopyAssetIfPresent(sourcePath, destinationPath);
            }

            // Retarget descriptor-level generated assets.
            if (avatar.expressionParameters != null)
            {
                string exprPath = AssetDatabase.GetAssetPath(avatar.expressionParameters)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(exprPath) && exprPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(targetDir + "/" + Path.GetFileName(exprPath));
                    if (replacement != null)
                    {
                        avatar.expressionParameters = replacement;
                        EditorUtility.SetDirty(avatar);
                    }
                }
            }

            if (avatar.expressionsMenu != null)
            {
                string menuPath = AssetDatabase.GetAssetPath(avatar.expressionsMenu)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(menuPath) && menuPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(targetDir + "/" + Path.GetFileName(menuPath));
                    if (replacement != null)
                    {
                        avatar.expressionsMenu = replacement;
                        EditorUtility.SetDirty(avatar);
                    }
                }

                RetargetMenuGeneratedSubmenus(avatar.expressionsMenu, sourcePrefix, targetDir, new HashSet<VRCExpressionsMenu>());
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var layer = avatar.baseAnimationLayers[i];
                var controller = layer.animatorController;
                string ctrlPath = controller ? AssetDatabase.GetAssetPath(controller)?.Replace('\\', '/') : string.Empty;
                if (string.IsNullOrWhiteSpace(ctrlPath) || !ctrlPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                    continue;

                var replacement = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(targetDir + "/" + Path.GetFileName(ctrlPath));
                if (replacement == null)
                    continue;

                layer.animatorController = replacement;
                avatar.baseAnimationLayers[i] = layer;
                EditorUtility.SetDirty(avatar);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            vendorizedDir = targetDir;
            return true;
        }

        private static bool TryRetargetLiveFullControllerGeneratedAssets(ASMLiteComponent component, string generatedDir)
        {
            if (component == null || string.IsNullOrWhiteSpace(generatedDir))
                return false;

            var vfComponent = FindLiveVrcFuryComponent(component);
            if (vfComponent == null)
                return false;

            var fxController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(generatedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            var menu = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(generatedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            var parameters = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(generatedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            if (fxController == null || menu == null || parameters == null)
                return false;

            var so = new SerializedObject(vfComponent);
            so.Update();

            bool applied = false;
            applied |= SetObjectReferenceIfPresent(so, "content.controllers.Array.data[0].controller.objRef", fxController);
            applied |= SetObjectReferenceIfPresent(so, "content.menus.Array.data[0].menu.objRef", menu);
            applied |= SetObjectReferenceIfPresent(so, "content.prms.Array.data[0].parameters.objRef", parameters);
            applied |= SetObjectReferenceIfPresent(so, "content.prms.Array.data[0].parameter.objRef", parameters);
            applied |= SetObjectReferenceIfPresent(so, "content.prms.Array.data[0].objRef", parameters);
            applied |= SetObjectReferenceIfPresent(so, "content.controller.objRef", fxController);
            applied |= SetObjectReferenceIfPresent(so, "content.menu.objRef", menu);
            applied |= SetObjectReferenceIfPresent(so, "content.parameters.objRef", parameters);

            if (!applied)
                return false;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vfComponent);
            return true;
        }

        private static bool SetObjectReferenceIfPresent(SerializedObject so, string path, UnityEngine.Object value)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
                return false;

            prop.objectReferenceValue = value;
            return true;
        }

        private void VendorizeAsmLite(ASMLiteComponent component)
        {
            if (component == null)
                return;

            const string modeLabel = "Vendorize ASM-Lite (Keep Attached)";
            bool confirm = EditorUtility.DisplayDialog(
                modeLabel,
                "This will keep ASM-Lite attached and editable, mirror generated payload files into Assets/ASM-Lite/<AvatarName>/GeneratedAssets, and switch this avatar to those mirrored files. Continue?",
                "Continue",
                "Cancel");
            if (!confirm)
                return;

            var avatar = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null)
            {
                EditorUtility.DisplayDialog(modeLabel, "No VRCAvatarDescriptor found for this ASM-Lite component.", "OK");
                return;
            }

            if (!TryRefreshLiveInstallPathPrefix(component, "Vendorize"))
            {
                EditorUtility.DisplayDialog(modeLabel, "Failed to refresh FullController install prefix before vendorizing.", "OK");
                return;
            }

            int count = ASMLiteBuilder.Build(component);
            if (count >= 0)
                _discoveredParamCount = count;

            if (!TryVendorizeGeneratedAssetsToAvatarFolder(avatar, out string vendorizedDir))
            {
                EditorUtility.DisplayDialog(modeLabel, "Failed to mirror generated assets to Assets/ASM-Lite.", "OK");
                return;
            }

            if (!TryRetargetLiveFullControllerGeneratedAssets(component, vendorizedDir))
            {
                EditorUtility.DisplayDialog(modeLabel, "Mirrored assets were created, but live FullController references could not be retargeted.", "OK");
                return;
            }

            Undo.RecordObject(component, "Enable ASM-Lite Vendorized Assets");
            component.useVendorizedGeneratedAssets = true;
            component.vendorizedGeneratedAssetsPath = vendorizedDir;
            EditorUtility.SetDirty(component);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ASM-Lite] Vendorized generated payload for '{avatar.gameObject.name}' to '{vendorizedDir}' and kept ASM-Lite attached.");
            EditorUtility.DisplayDialog(modeLabel + " Complete", $"Vendorized assets folder:\n{vendorizedDir}\n\nASM-Lite remains attached and editable on this avatar.", "OK");
            Repaint();
        }

        private void DetachAsmLite(ASMLiteComponent component, bool vendorizeToAssets)
        {
            if (component == null)
                return;

            string modeLabel = vendorizeToAssets ? "Vendorize + Detach" : "Detach ASM-Lite";
            bool confirm = EditorUtility.DisplayDialog(
                modeLabel,
                vendorizeToAssets
                    ? "This will bake ASM-Lite directly into avatar assets, copy generated assets into Assets/ASM-Lite/<AvatarName>/GeneratedAssets, then remove the ASM-Lite prefab object. Continue?"
                    : "This will bake ASM-Lite directly into avatar assets, then remove the ASM-Lite prefab object. Continue?",
                "Continue",
                "Cancel");

            if (!confirm)
                return;

            if (!ASMLiteBuilder.TryDetachToDirectDelivery(component, out string detail))
            {
                Debug.LogError(detail);
                EditorUtility.DisplayDialog(modeLabel, detail, "OK");
                return;
            }

            var avatar = component.GetComponentInParent<VRCAvatarDescriptor>();
            string vendorizedDir = string.Empty;
            if (vendorizeToAssets && avatar != null)
            {
                if (!TryVendorizeGeneratedAssetsToAvatarFolder(avatar, out vendorizedDir))
                {
                    Debug.LogWarning("[ASM-Lite] Vendorize requested, but generated assets could not be copied/rebound. Detached payload still applied.");
                }
            }

            Undo.SetCurrentGroupName(modeLabel);
            int group = Undo.GetCurrentGroup();
            Undo.DestroyObjectImmediate(component.gameObject);
            Undo.CollapseUndoOperations(group);

            _cachedComponent = null;
            _lastRefreshFrame = -1;
            _discoveredParamCount = -1;

            string completion;
            if (vendorizeToAssets && !string.IsNullOrWhiteSpace(vendorizedDir))
            {
                completion = $"{detail}\n\nVendorized assets folder:\n{vendorizedDir}";
                Debug.Log($"[ASM-Lite] {detail} Vendorized generated assets to '{vendorizedDir}'.");
            }
            else
            {
                completion = detail;
                Debug.Log($"[ASM-Lite] {detail}");
            }

            EditorUtility.DisplayDialog(modeLabel + " Complete", completion, "OK");
            Repaint();
        }

        private void ReturnToPackageManaged()
        {
            if (_selectedAvatar == null)
                return;

            var existing = GetOrRefreshComponent();
            if (existing != null)
            {
                if (!existing.useVendorizedGeneratedAssets)
                {
                    EditorUtility.DisplayDialog(
                        "Already Package Managed",
                        "This avatar already has an ASM-Lite component attached and editable.",
                        "OK");
                    return;
                }

                Undo.RecordObject(existing, "Disable ASM-Lite Vendorized Assets");
                existing.useVendorizedGeneratedAssets = false;
                existing.vendorizedGeneratedAssetsPath = string.Empty;
                EditorUtility.SetDirty(existing);

                if (!ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(existing.gameObject, existing, "Return To Package Managed"))
                {
                    EditorUtility.DisplayDialog(
                        "Return to Package Managed",
                        "Failed to restore package-managed FullController wiring on the attached ASM-Lite component.",
                        "OK");
                    return;
                }

                _discoveredParamCount = -1;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog(
                    "Package Managed Restored",
                    "ASM-Lite remains attached and editable. This avatar now uses package-managed generated payload references again.",
                    "OK");
                Repaint();
                return;
            }

            ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_selectedAvatar);

            _cachedComponent = null;
            _lastRefreshFrame = -1;
            _discoveredParamCount = -1;

            AddPrefabToAvatar();

            EditorUtility.DisplayDialog(
                "Package Managed Restored",
                "ASM-Lite has been re-attached in package-managed mode for this avatar.\n\nYou can now edit settings and rebuild normally.",
                "OK");
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
            component.useCustomSlotIcons = _pendingUseCustomSlotIcons;
            component.customIcons = CloneTextures(_pendingCustomIcons);

            component.useCustomRootIcon = _pendingUseCustomRootIcon;
            component.customRootIcon = _pendingCustomRootIcon;
            component.useCustomRootName = _pendingUseCustomRootName;
            component.customRootName = NormalizeOptionalString(_pendingCustomRootName);
            component.customPresetNames = CloneStrings(_pendingCustomPresetNames);
            component.customPresetNameFormat = NormalizeOptionalString(_pendingCustomPresetNameFormat);
            component.customSaveLabel = _pendingCustomSaveLabel ?? string.Empty;
            component.customLoadLabel = _pendingCustomLoadLabel ?? string.Empty;
            component.customClearPresetLabel = _pendingCustomClearPresetLabel ?? string.Empty;
            component.customConfirmLabel = _pendingCustomConfirmLabel ?? string.Empty;
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
            _pendingUseCustomSlotIcons = component.useCustomSlotIcons;
            _pendingCustomIcons = CloneTextures(component.customIcons);

            _pendingUseCustomRootIcon = component.useCustomRootIcon;
            _pendingCustomRootIcon = component.customRootIcon;
            _pendingUseCustomRootName = component.useCustomRootName;
            _pendingCustomRootName = NormalizeOptionalString(component.customRootName);
            _pendingCustomPresetNames = CloneStrings(component.customPresetNames);
            _pendingCustomPresetNameFormat = NormalizeOptionalString(component.customPresetNameFormat);
            _pendingCustomSaveLabel = component.customSaveLabel ?? string.Empty;
            _pendingCustomLoadLabel = component.customLoadLabel ?? string.Empty;
            _pendingCustomClearPresetLabel = component.customClearPresetLabel ?? string.Empty;
            _pendingCustomConfirmLabel = component.customConfirmLabel ?? string.Empty;
            _pendingUseCustomInstallPath = component.useCustomInstallPath;
            _pendingCustomInstallPath = NormalizeOptionalString(component.customInstallPath);
            _pendingUseParameterExclusions = component.useParameterExclusions;
            _pendingExcludedParameterNames = SanitizeExcludedParameterNames(component.excludedParameterNames);
        }

        private static bool TryResolveInstallPrefixFromMovedRootPath(string rootControlName, string movedDestinationPath, out string installPrefix)
        {
            installPrefix = string.Empty;

            string root = NormalizeOptionalString(rootControlName);
            string destination = NormalizeSlashPath(movedDestinationPath);
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(destination))
                return false;

            if (string.Equals(destination, root, StringComparison.Ordinal))
            {
                installPrefix = string.Empty;
                return true;
            }

            string suffix = "/" + root;
            if (destination.EndsWith(suffix, StringComparison.Ordinal))
            {
                installPrefix = destination.Substring(0, destination.Length - suffix.Length);
                return true;
            }

            // Fallback: treat destination as direct prefix if it does not include
            // the root segment explicitly.
            installPrefix = destination;
            return true;
        }

        private static bool TryAdoptInstallPathFromMoveMenu(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            out string adoptedInstallPrefix,
            out int removedMoveComponents)
        {
            adoptedInstallPrefix = string.Empty;
            removedMoveComponents = 0;

            if (component == null || avatar == null)
                return false;

            string effectiveRootName = ASMLiteBuilder.ResolveEffectiveRootControlName(component);
            if (string.IsNullOrWhiteSpace(effectiveRootName))
                return false;

            var remaps = GetVrcFuryMoveMenuPathRemaps(avatar);
            if (remaps.Count == 0)
                return false;

            string normalizedRoot = NormalizeSlashPath(effectiveRootName);
            string matchedDestination = null;
            foreach (var kv in remaps)
            {
                string fromPath = NormalizeSlashPath(kv.Key);
                if (!string.Equals(fromPath, normalizedRoot, StringComparison.Ordinal))
                    continue;

                matchedDestination = kv.Value;
                break;
            }

            if (string.IsNullOrWhiteSpace(matchedDestination))
                return false;

            if (!TryResolveInstallPrefixFromMovedRootPath(effectiveRootName, matchedDestination, out string resolvedPrefix))
                return false;

            resolvedPrefix = NormalizeOptionalString(resolvedPrefix);

            bool changedComponent = !component.useCustomInstallPath
                || !string.Equals(NormalizeOptionalString(component.customInstallPath), resolvedPrefix, StringComparison.Ordinal);

            if (changedComponent)
            {
                Undo.RecordObject(component, "Adopt ASM-Lite Install Path From Move Menu");
                component.useCustomInstallPath = true;
                component.customInstallPath = resolvedPrefix;
                EditorUtility.SetDirty(component);
            }

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                // Preserve ASM-Lite managed install-path routing helper.
                if (string.Equals(behaviour.gameObject.name, "ASM-Lite Install Path Routing", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                var content = so.FindProperty("content");
                if (content == null || content.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                string managedRefType = content.managedReferenceFullTypename;
                if (string.IsNullOrWhiteSpace(managedRefType)
                    || managedRefType.IndexOf("MoveMenuItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var fromProp = so.FindProperty("content.fromPath");
                if (fromProp == null || fromProp.propertyType != SerializedPropertyType.String)
                    continue;

                string fromPath = NormalizeSlashPath(fromProp.stringValue);
                if (!string.Equals(fromPath, normalizedRoot, StringComparison.Ordinal))
                    continue;

                Undo.DestroyObjectImmediate(behaviour);
                removedMoveComponents++;
            }

            adoptedInstallPrefix = resolvedPrefix;
            return changedComponent || removedMoveComponents > 0;
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
                CopyPendingCustomizationToComponent(component);
                if (!ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(instance, component, "Add Prefab"))
                {
                    Debug.LogError("[ASM-Lite] Failed to refresh live FullController wiring on newly added prefab instance.");
                    Undo.DestroyObjectImmediate(instance);
                    return;
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
                bool savedUseCustomSlotIcons = component.useCustomSlotIcons;
                Texture2D[] savedCustomIcons = CloneTextures(component.customIcons);
                Texture2D savedCustomRootIcon = component.customRootIcon;
                bool savedUseCustomRootIcon = component.useCustomRootIcon;
                bool savedUseCustomRootName = component.useCustomRootName;
                string savedCustomRootName = component.customRootName ?? string.Empty;
                string[] savedCustomPresetNames = CloneStrings(component.customPresetNames);
                string savedCustomPresetNameFormat = NormalizeOptionalString(component.customPresetNameFormat);
                string savedCustomSaveLabel = component.customSaveLabel ?? string.Empty;
                string savedCustomLoadLabel = component.customLoadLabel ?? string.Empty;
                string savedCustomClearPresetLabel = component.customClearPresetLabel ?? string.Empty;
                string savedCustomConfirmLabel = component.customConfirmLabel ?? string.Empty;
                bool savedUseCustomInstallPath = component.useCustomInstallPath;
                string savedCustomInstallPath = NormalizeOptionalString(component.customInstallPath);
                bool savedUseParameterExclusions = component.useParameterExclusions;
                string[] savedExcludedParameterNames = SanitizeExcludedParameterNames(component.excludedParameterNames);
                bool savedUseVendorizedGeneratedAssets = component.useVendorizedGeneratedAssets;
                string savedVendorizedGeneratedAssetsPath = NormalizeOptionalString(component.vendorizedGeneratedAssetsPath);
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
                    newComponent.useCustomSlotIcons = savedUseCustomSlotIcons;
                    newComponent.customIcons = savedCustomIcons;
                    newComponent.useCustomRootIcon = savedUseCustomRootIcon;
                    newComponent.customRootIcon = savedCustomRootIcon;
                    newComponent.useCustomRootName = savedUseCustomRootName;
                    newComponent.customRootName = savedCustomRootName;
                    newComponent.customPresetNames = savedCustomPresetNames;
                    newComponent.customPresetNameFormat = savedCustomPresetNameFormat;
                    newComponent.customSaveLabel = savedCustomSaveLabel;
                    newComponent.customLoadLabel = savedCustomLoadLabel;
                    newComponent.customClearPresetLabel = savedCustomClearPresetLabel;
                    newComponent.customConfirmLabel = savedCustomConfirmLabel;
                    newComponent.useCustomInstallPath = savedUseCustomInstallPath;
                    newComponent.customInstallPath = savedCustomInstallPath;
                    newComponent.useParameterExclusions = savedUseParameterExclusions;
                    newComponent.excludedParameterNames = savedExcludedParameterNames;
                    newComponent.useVendorizedGeneratedAssets = savedUseVendorizedGeneratedAssets;
                    newComponent.vendorizedGeneratedAssetsPath = savedVendorizedGeneratedAssetsPath;

                    if (!ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(instance, newComponent, "Bake Migration"))
                    {
                        Debug.LogError("[ASM-Lite] Migration rebuild failed to refresh live FullController wiring. Aborting rebuild.");
                        return;
                    }
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

                if (!TryRefreshLiveInstallPathPrefix(component, "Bake"))
                {
                    Debug.LogError("[ASM-Lite] Bake aborted before asset rebuild because live FullController menu prefix refresh failed.");
                    return;
                }

                int count = ASMLiteBuilder.Build(component);
                if (count >= 0)
                    _discoveredParamCount = count;

                if (component.useVendorizedGeneratedAssets)
                {
                    string preferredDir = NormalizeOptionalString(component.vendorizedGeneratedAssetsPath);
                    if (!TryVendorizeGeneratedAssetsToAvatarFolder(_selectedAvatar, out string syncedDir))
                    {
                        Debug.LogWarning("[ASM-Lite] Vendorized mode enabled but generated asset mirror sync failed. Keeping existing references.");
                    }
                    else
                    {
                        string effectiveDir = string.IsNullOrWhiteSpace(preferredDir) ? syncedDir : preferredDir;
                        if (!string.Equals(effectiveDir, syncedDir, StringComparison.Ordinal))
                            effectiveDir = syncedDir;

                        if (TryRetargetLiveFullControllerGeneratedAssets(component, effectiveDir))
                        {
                            component.vendorizedGeneratedAssetsPath = effectiveDir;
                            EditorUtility.SetDirty(component);
                            Debug.Log($"[ASM-Lite] Vendorized payload sync complete at '{effectiveDir}'.");
                        }
                        else
                        {
                            Debug.LogWarning("[ASM-Lite] Vendorized payload sync copied assets, but live FullController references were not retargeted.");
                        }
                    }
                }

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
            {
                if (TryAdoptInstallPathFromMoveMenu(existing, _selectedAvatar, out string adoptedPrefix, out int removedMoveComponents))
                {
                    string readablePrefix = string.IsNullOrEmpty(adoptedPrefix) ? "<root>" : adoptedPrefix;
                    Debug.Log($"[ASM-Lite] Adopted install path from VRCFury Move Menu for '{existing.gameObject.name}': prefix='{readablePrefix}', removedMoveComponents={removedMoveComponents}.");
                }

                CopyComponentCustomizationToPending(existing);
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
                _lastRefreshFrame = -1;
                _discoveredParamCount = -1;
                SyncPendingSlotCountFromAvatar();

                Repaint();
            }
        }

        // ── Nested types ──────────────────────────────────────────────────────

        /// <summary>Node in the install-path menu tree.</summary>
        private class MenuTreeNode
        {
            public string Name;
            public string FullPath;
            public readonly List<MenuTreeNode> Children = new List<MenuTreeNode>();
        }

        /// <summary>
        /// Node in the parameter-backup tree. Leaf nodes (IsParam == true) represent
        /// individual parameters with a checkbox. Interior nodes are menu folders.
        /// </summary>
        private class ParamTreeNode
        {
            public string Name;
            public string MenuPath;   // non-null / set for folder nodes
            public string ParamName;  // non-null for leaf param nodes
            public bool IsParam => ParamName != null;
            public readonly List<ParamTreeNode> Children = new List<ParamTreeNode>();
        }
    }
}
