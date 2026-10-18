using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using ASMLite.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static ASMLite.Editor.ASMLiteWindow;

namespace ASMLite.Tests.Editor
{
    // Created by the optional smoke host, never by ordinary window rendering.
    internal sealed class ASMLiteSmokePresentation
    {
        private string _visibleAutomationOverlayTitle = string.Empty;
        private string _visibleAutomationOverlayStep = string.Empty;
        private int _visibleAutomationOverlayStepIndex;
        private int _visibleAutomationOverlayTotalSteps;
        private VisibleAutomationOverlayState _visibleAutomationOverlayState = VisibleAutomationOverlayState.Running;
        private bool _visibleAutomationOverlayPresentationMode;
        private string[] _visibleAutomationChecklistItems = Array.Empty<string>();
        private VisibleAutomationChecklistItemState[] _visibleAutomationChecklistStates = Array.Empty<VisibleAutomationChecklistItemState>();
        private double[] _visibleAutomationChecklistStateChangedAt = Array.Empty<double>();
        private bool _visibleAutomationUsingHostWindowFallbackBounds;
        private Rect _visibleAutomationScreenBounds;
        private bool _visibleAutomationCompletionReviewVisible;
        private bool _visibleAutomationCompletionReviewAcknowledged;
        private string _visibleAutomationCompletionReviewTitle = string.Empty;
        private string _visibleAutomationCompletionReviewMessage = string.Empty;
        private VisibleAutomationStatusOverlayWindow _visibleAutomationStatusOverlayWindow;
        private VisibleAutomationChecklistOverlayWindow _visibleAutomationChecklistOverlayWindow;
        private VisibleAutomationCompletionReviewOverlayWindow _visibleAutomationCompletionReviewOverlayWindow;
        private string _visibleAutomationExternalOverlayStatePath = string.Empty;
        private string _visibleAutomationExternalOverlayAckPath = string.Empty;
        private string _visibleAutomationExternalOverlaySessionId = string.Empty;
        private int _visibleAutomationCompletionReviewRequestId;
        private ASMLiteSmokePresentationPublisher _externalPublisher = new ASMLiteSmokePresentationPublisher();
        private string _visibleAutomationExternalOverlayLastAckPayload = string.Empty;

        private const float VisibleAutomationScreenOverlayTopMargin = 18f;
        private const float VisibleAutomationScreenOverlaySideMargin = 18f;
        private const float VisibleAutomationScreenOverlayBottomMargin = 18f;
        private const float VisibleAutomationScreenChecklistWidthFactor = 0.25f;
        private const float VisibleAutomationScreenStatusMaxWidthFactor = 0.46f;
        private const float VisibleAutomationScreenStatusMinWidth = 420f;
        private const float VisibleAutomationScreenChecklistMinWidth = 260f;
        private const float VisibleAutomationScreenStatusHeight = 136f;
        private const float VisibleAutomationReviewPopupWidth = 440f;
        private const float VisibleAutomationReviewPopupHeight = 210f;
        private const float VisibleAutomationChecklistCompactItemHeight = 40f;

        private readonly EditorWindow _owner;
        private bool _disposed;

        internal ASMLiteSmokePresentation(ASMLiteWindow owner)
        {
            _owner = owner;
            RegisterVisibleAutomationOverlayGeometryCallback();
            EditorApplication.update += HandleVisibleAutomationOverlayAnimationTick;
        }

        internal ASMLiteWindowSmokeCallbacks CreateCallbacks(Action dispose)
        {
            return new ASMLiteWindowSmokeCallbacks
            {
                ConfigureExternalOverlay = ConfigureExternalVisibleAutomationOverlay,
                SetOverlayStatus = SetVisibleAutomationOverlayStatus,
                ClearOverlay = ClearVisibleAutomationOverlay,
                ShowCompletionReview = ShowVisibleAutomationCompletionReview,
                AcknowledgeCompletionReview = AcknowledgeVisibleAutomationCompletionReview,
                IsCompletionReviewVisible = IsVisibleAutomationCompletionReviewVisibleForAutomation,
                WasCompletionReviewAcknowledged = WasVisibleAutomationCompletionReviewAcknowledgedForAutomation,
                GetHostSnapshot = GetVisibleAutomationOverlayHostSnapshotForTesting,
                ReadExternalState = GetVisibleAutomationExternalOverlayStateForTesting,
                ConsumeExternalAcknowledgement = TryConsumeVisibleAutomationExternalOverlayAcknowledgementForTesting,
                Refresh = () => RefreshVisibleAutomationOverlayVisuals(),
                Dispose = dispose,
            };
        }

        internal void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            EditorApplication.update -= HandleVisibleAutomationOverlayAnimationTick;
            UnregisterVisibleAutomationOverlayGeometryCallback();
            ClearVisibleAutomationOverlay();
            DeleteVisibleAutomationExternalOverlayAckFile();
        }

        internal VisibleAutomationExternalOverlayStateDocument GetPresentationState()
        {
            bool hasAnyOverlay = HasAnyVisibleAutomationOverlay();
            return new VisibleAutomationExternalOverlayStateDocument
            {
                sessionId = _visibleAutomationExternalOverlaySessionId,
                sessionActive = hasAnyOverlay,
                presentationMode = _visibleAutomationOverlayPresentationMode,
                title = _visibleAutomationOverlayTitle ?? string.Empty,
                step = _visibleAutomationOverlayStep ?? string.Empty,
                stepIndex = _visibleAutomationOverlayStepIndex,
                totalSteps = _visibleAutomationOverlayTotalSteps,
                state = _visibleAutomationOverlayState.ToString(),
                checklist = BuildVisibleAutomationExternalOverlayChecklist(),
                completionReviewVisible = _visibleAutomationCompletionReviewVisible,
                completionReviewRequestId = _visibleAutomationCompletionReviewRequestId,
                completionReviewTitle = _visibleAutomationCompletionReviewTitle ?? string.Empty,
                completionReviewMessage = _visibleAutomationCompletionReviewMessage ?? string.Empty,
                completionReviewAcknowledged = _visibleAutomationCompletionReviewAcknowledged,
            };

        }

        private sealed class VisibleAutomationChecklistVisualRefs
        {
            public VisualElement Root;
            public VisualElement Accent;
            public Label Glyph;
            public Label Text;
            public VisualElement Badge;
            public Label BadgeLabel;
            public Label StepLabel;
        }


        private void RegisterVisibleAutomationOverlayGeometryCallback()
        {
            if (_owner.rootVisualElement == null)
                return;

            _owner.rootVisualElement.UnregisterCallback<GeometryChangedEvent>(HandleVisibleAutomationOverlayGeometryChanged);
            _owner.rootVisualElement.RegisterCallback<GeometryChangedEvent>(HandleVisibleAutomationOverlayGeometryChanged);
        }

        private void UnregisterVisibleAutomationOverlayGeometryCallback()
        {
            _owner.rootVisualElement?.UnregisterCallback<GeometryChangedEvent>(HandleVisibleAutomationOverlayGeometryChanged);
        }

        private void HandleVisibleAutomationOverlayGeometryChanged(GeometryChangedEvent _)
        {
            RefreshVisibleAutomationOverlayVisuals();
        }

        private void HandleVisibleAutomationOverlayAnimationTick()
        {
            UpdateVisibleAutomationCompletionReviewState();

            if (!HasAnyVisibleAutomationOverlay())
                return;

            if (ShouldAnimateVisibleAutomationOverlay() || NeedsVisibleAutomationOverlayHostResync())
                RefreshVisibleAutomationOverlayVisuals();
        }

        private bool HasAnyVisibleAutomationOverlay()
        {
            return !string.IsNullOrWhiteSpace(_visibleAutomationOverlayStep)
                || HasVisibleAutomationChecklist()
                || _visibleAutomationCompletionReviewVisible;
        }

        private bool NeedsVisibleAutomationOverlayHostResync()
        {
            if (!HasAnyVisibleAutomationOverlay())
                return false;

            if (!string.IsNullOrWhiteSpace(_visibleAutomationOverlayStep)
                && _visibleAutomationStatusOverlayWindow == null)
                return true;

            if (HasVisibleAutomationChecklist()
                && _visibleAutomationChecklistOverlayWindow == null)
                return true;

            if (_visibleAutomationCompletionReviewVisible
                && _visibleAutomationCompletionReviewOverlayWindow == null)
                return true;

            return false;
        }

        private bool RefreshVisibleAutomationOverlayVisuals()
        {
            bool hasStatusOverlay = !string.IsNullOrWhiteSpace(_visibleAutomationOverlayStep);
            bool hasChecklistOverlay = HasVisibleAutomationChecklist();
            bool hasCompletionReview = _visibleAutomationCompletionReviewVisible;
            bool hasAnyOverlay = hasStatusOverlay || hasChecklistOverlay || hasCompletionReview;

            SyncVisibleAutomationExternalOverlayState();
            if (UsesExternalVisibleAutomationOverlay())
            {
                CloseVisibleAutomationOverlayPopupWindows();
                return false;
            }

            if (!hasAnyOverlay)
            {
                CloseVisibleAutomationOverlayPopupWindows();
                return false;
            }

            // Visible smoke uses detached auxiliary windows as its single hosting model.
            // If monitor detection cannot resolve native display bounds, those windows still
            // stay detached and fall back to the owner window's screen rect for anchoring.
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CloseVisibleAutomationOverlayPopupWindows();
                return false;
            }

            if (hasStatusOverlay || hasChecklistOverlay)
                TrySyncVisibleAutomationOverlayPopupWindows(hasStatusOverlay, hasChecklistOverlay);
            else
                CloseVisibleAutomationStatusOverlayWindow();

            SyncVisibleAutomationCompletionReviewOverlayWindow();
            return true;
        }

        private bool UsesExternalVisibleAutomationOverlay()
        {
            return !string.IsNullOrWhiteSpace(_visibleAutomationExternalOverlayStatePath);
        }

        private void SyncVisibleAutomationExternalOverlayState()
        {
            if (!UsesExternalVisibleAutomationOverlay())
                return;

            var document = GetPresentationState();

            string semanticJson = JsonUtility.ToJson(document, true);
            try
            {
                _externalPublisher.Publish(_visibleAutomationExternalOverlayStatePath, semanticJson, ticks =>
                {
                    document.updatedUtcTicks = ticks;
                    return JsonUtility.ToJson(document, true);
                });
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Failed to write visible automation external overlay state: {ex.Message}");
            }
        }

        private VisibleAutomationExternalOverlayChecklistItem[] BuildVisibleAutomationExternalOverlayChecklist()
        {
            if (!HasVisibleAutomationChecklist())
                return Array.Empty<VisibleAutomationExternalOverlayChecklistItem>();

            var checklist = new VisibleAutomationExternalOverlayChecklistItem[_visibleAutomationChecklistItems.Length];
            for (int i = 0; i < _visibleAutomationChecklistItems.Length; i++)
            {
                checklist[i] = new VisibleAutomationExternalOverlayChecklistItem
                {
                    text = _visibleAutomationChecklistItems[i] ?? string.Empty,
                    state = i < _visibleAutomationChecklistStates.Length
                        ? _visibleAutomationChecklistStates[i].ToString()
                        : VisibleAutomationChecklistItemState.Pending.ToString(),
                };
            }

            return checklist;
        }

        private void DeleteVisibleAutomationExternalOverlayAckFile()
        {
            if (string.IsNullOrWhiteSpace(_visibleAutomationExternalOverlayAckPath))
                return;

            try
            {
                if (File.Exists(_visibleAutomationExternalOverlayAckPath))
                    File.Delete(_visibleAutomationExternalOverlayAckPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Failed to clear visible automation external overlay acknowledgement file: {ex.Message}");
            }
        }

        private bool TryConsumeVisibleAutomationExternalOverlayAcknowledgement()
        {
            if (!_visibleAutomationCompletionReviewVisible
                || !UsesExternalVisibleAutomationOverlay()
                || string.IsNullOrWhiteSpace(_visibleAutomationExternalOverlayAckPath)
                || !File.Exists(_visibleAutomationExternalOverlayAckPath))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(_visibleAutomationExternalOverlayAckPath);
                if (string.IsNullOrWhiteSpace(json)
                    || string.Equals(json, _visibleAutomationExternalOverlayLastAckPayload, StringComparison.Ordinal))
                {
                    return false;
                }

                _visibleAutomationExternalOverlayLastAckPayload = json;
                var acknowledgement = JsonUtility.FromJson<VisibleAutomationExternalOverlayAckDocument>(json);
                if (acknowledgement == null
                    || !acknowledgement.acknowledged
                    || !string.Equals(acknowledgement.sessionId, _visibleAutomationExternalOverlaySessionId, StringComparison.Ordinal)
                    || acknowledgement.completionReviewRequestId != _visibleAutomationCompletionReviewRequestId)
                {
                    return false;
                }

                DeleteVisibleAutomationExternalOverlayAckFile();
                AcknowledgeVisibleAutomationCompletionReview();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Failed to read visible automation external overlay acknowledgement: {ex.Message}");
                return false;
            }
        }


        private bool TrySyncVisibleAutomationOverlayPopupWindows(bool hasStatusOverlay, bool hasChecklistOverlay)
        {
            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CloseVisibleAutomationOverlayPopupWindows();
                return false;
            }

            if (!TryGetVisibleAutomationScreenOverlayRects(out Rect? statusRect, out Rect? checklistRect)
                || !statusRect.HasValue
                || !checklistRect.HasValue)
            {
                CloseVisibleAutomationOverlayPopupWindows();
                return false;
            }

            if (hasStatusOverlay)
            {
                if (_visibleAutomationStatusOverlayWindow == null)
                    _visibleAutomationStatusOverlayWindow = VisibleAutomationStatusOverlayWindow.Create(this);

                _visibleAutomationStatusOverlayWindow.SyncFromOwner(this, statusRect.Value);
            }
            else
            {
                CloseVisibleAutomationStatusOverlayWindow();
            }

            if (hasChecklistOverlay)
            {
                if (_visibleAutomationChecklistOverlayWindow == null)
                    _visibleAutomationChecklistOverlayWindow = VisibleAutomationChecklistOverlayWindow.Create(this);

                _visibleAutomationChecklistOverlayWindow.SyncFromOwner(this, checklistRect.Value);
            }
            else
            {
                CloseVisibleAutomationChecklistOverlayWindow();
            }

            return true;
        }

        private void SyncVisibleAutomationCompletionReviewOverlayWindow()
        {
            if (!_visibleAutomationCompletionReviewVisible)
            {
                CloseVisibleAutomationCompletionReviewOverlayWindow();
                return;
            }

            if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                CloseVisibleAutomationCompletionReviewOverlayWindow();
                return;
            }

            if (!TryGetVisibleAutomationCompletionReviewOverlayRect(out Rect reviewRect))
            {
                CloseVisibleAutomationCompletionReviewOverlayWindow();
                return;
            }

            if (_visibleAutomationCompletionReviewOverlayWindow == null)
                _visibleAutomationCompletionReviewOverlayWindow = VisibleAutomationCompletionReviewOverlayWindow.Create(this);

            _visibleAutomationCompletionReviewOverlayWindow.SyncFromOwner(this, reviewRect);
        }

        private void CloseVisibleAutomationOverlayPopupWindows()
        {
            CloseVisibleAutomationStatusOverlayWindow();
            CloseVisibleAutomationChecklistOverlayWindow();
            CloseVisibleAutomationCompletionReviewOverlayWindow();
        }

        private void CloseVisibleAutomationStatusOverlayWindow()
        {
            if (_visibleAutomationStatusOverlayWindow == null)
                return;

            _visibleAutomationStatusOverlayWindow.Close();
            _visibleAutomationStatusOverlayWindow = null;
        }

        private void CloseVisibleAutomationChecklistOverlayWindow()
        {
            if (_visibleAutomationChecklistOverlayWindow == null)
                return;

            _visibleAutomationChecklistOverlayWindow.Close();
            _visibleAutomationChecklistOverlayWindow = null;
        }

        private void CloseVisibleAutomationCompletionReviewOverlayWindow()
        {
            if (_visibleAutomationCompletionReviewOverlayWindow == null)
                return;

            _visibleAutomationCompletionReviewOverlayWindow.Close();
            _visibleAutomationCompletionReviewOverlayWindow = null;
        }


        private bool TryGetVisibleAutomationScreenOverlayRects(out Rect? statusRect, out Rect? checklistRect)
        {
            statusRect = null;
            checklistRect = null;

            if (!TryGetVisibleAutomationScreenBounds(out Rect screenBounds))
                return false;

            _visibleAutomationScreenBounds = screenBounds;

            float topMargin = VisibleAutomationScreenOverlayTopMargin;
            float sideMargin = VisibleAutomationScreenOverlaySideMargin;
            float bottomMargin = VisibleAutomationScreenOverlayBottomMargin;
            float availableWidth = Mathf.Max(680f, screenBounds.width - sideMargin * 2f);
            float availableHeight = Mathf.Max(360f, screenBounds.height - topMargin - bottomMargin);
            float checklistWidth = Mathf.Max(VisibleAutomationScreenChecklistMinWidth, availableWidth * VisibleAutomationScreenChecklistWidthFactor);
            checklistWidth = Mathf.Min(checklistWidth, availableWidth * 0.42f);
            float checklistX = screenBounds.xMax - sideMargin - checklistWidth;
            float checklistY = screenBounds.yMin + topMargin;
            float checklistHeight = availableHeight;

            float leftAvailableWidth = Mathf.Max(VisibleAutomationScreenStatusMinWidth, checklistX - sideMargin - screenBounds.xMin - sideMargin);
            float statusWidth = Mathf.Max(VisibleAutomationScreenStatusMinWidth, availableWidth * VisibleAutomationScreenStatusMaxWidthFactor);
            statusWidth = Mathf.Min(statusWidth, leftAvailableWidth);
            float statusHeight = _visibleAutomationOverlayPresentationMode
                ? VisibleAutomationScreenStatusHeight + 12f
                : VisibleAutomationScreenStatusHeight;
            float statusX = screenBounds.xMin + sideMargin + Mathf.Max(0f, (leftAvailableWidth - statusWidth) * 0.5f);
            float statusY = screenBounds.yMin + topMargin;

            statusRect = new Rect(statusX, statusY, statusWidth, statusHeight);
            checklistRect = new Rect(checklistX, checklistY, checklistWidth, checklistHeight);
            return true;
        }

        private bool TryGetVisibleAutomationCompletionReviewOverlayRect(out Rect reviewRect)
        {
            reviewRect = default;

            if (!TryGetVisibleAutomationScreenBounds(out Rect screenBounds))
                return false;

            _visibleAutomationScreenBounds = screenBounds;

            float width = Mathf.Min(VisibleAutomationReviewPopupWidth, Mathf.Max(320f, screenBounds.width - VisibleAutomationScreenOverlaySideMargin * 2f));
            float height = Mathf.Min(VisibleAutomationReviewPopupHeight, Mathf.Max(180f, screenBounds.height - VisibleAutomationScreenOverlayTopMargin * 2f));
            float x = screenBounds.xMin + Mathf.Max(VisibleAutomationScreenOverlaySideMargin, (screenBounds.width - width) * 0.5f);
            float y = screenBounds.yMin + Mathf.Max(VisibleAutomationScreenOverlayTopMargin * 2f, (screenBounds.height - height) * 0.34f);
            x = Mathf.Min(x, screenBounds.xMax - VisibleAutomationScreenOverlaySideMargin - width);
            y = Mathf.Min(y, screenBounds.yMax - VisibleAutomationScreenOverlayBottomMargin - height);
            reviewRect = new Rect(x, y, width, height);
            return reviewRect.width > 0f && reviewRect.height > 0f;
        }

        private bool TryGetVisibleAutomationScreenBounds(out Rect screenBounds)
        {
            screenBounds = default;

            if (ShouldFreezeVisibleAutomationScreenBoundsToLastKnownRect())
            {
                screenBounds = _visibleAutomationScreenBounds;
                return true;
            }

            _visibleAutomationUsingHostWindowFallbackBounds = false;

            Rect referenceRect = _owner.position;
            if (TryGetHostContainerScreenRect(out Rect hostContainerRect))
                referenceRect = hostContainerRect;

            if (referenceRect.width <= 0f || referenceRect.height <= 0f)
            {
                if (HasCachedVisibleAutomationScreenBounds())
                {
                    screenBounds = _visibleAutomationScreenBounds;
                    return true;
                }

                return false;
            }

            if (TryGetDisplayBoundsFromNativeWindow(referenceRect.center, out screenBounds))
            {
                _visibleAutomationScreenBounds = screenBounds;
                return true;
            }

            // Keep detached overlay windows as the only host model even when native
            // monitor lookup is unavailable by anchoring them to the owner window rect.
            _visibleAutomationUsingHostWindowFallbackBounds = true;
            screenBounds = referenceRect;
            _visibleAutomationScreenBounds = screenBounds;
            return true;
        }

        private bool HasCachedVisibleAutomationScreenBounds()
        {
            return _visibleAutomationScreenBounds.width > 0f && _visibleAutomationScreenBounds.height > 0f;
        }

        private bool ShouldFreezeVisibleAutomationScreenBoundsToLastKnownRect()
        {
            return HasCachedVisibleAutomationScreenBounds()
                && EditorWindow.focusedWindow != _owner
                && !_owner.hasFocus;
        }

        private bool TryGetHostContainerScreenRect(out Rect screenRect)
        {
            screenRect = default;

            object hostView = typeof(EditorWindow)
                .GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(_owner);
            if (hostView == null)
                return false;

            if (TryReadRectMember(hostView, "screenPosition", out screenRect)
                || TryReadRectMember(hostView, "windowPosition", out screenRect)
                || TryReadRectMember(hostView, "position", out screenRect))
            {
                return screenRect.width > 0f && screenRect.height > 0f;
            }

            object containerWindow = hostView.GetType()
                .GetProperty("window", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(hostView, null);
            if (containerWindow == null)
                return false;

            if (TryReadRectMember(containerWindow, "position", out screenRect)
                || TryReadRectMember(containerWindow, "screenPosition", out screenRect)
                || TryReadRectMember(containerWindow, "windowPosition", out screenRect))
            {
                return screenRect.width > 0f && screenRect.height > 0f;
            }

            return false;
        }

        private static bool TryReadRectMember(object target, string memberName, out Rect rect)
        {
            rect = default;
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type targetType = target.GetType();

            PropertyInfo property = targetType.GetProperty(memberName, flags);
            if (property != null && property.PropertyType == typeof(Rect))
            {
                rect = (Rect)property.GetValue(target, null);
                return true;
            }

            FieldInfo field = targetType.GetField(memberName, flags);
            if (field != null && field.FieldType == typeof(Rect))
            {
                rect = (Rect)field.GetValue(target);
                return true;
            }

            return false;
        }

        private static bool TryGetDisplayBoundsFromNativeWindow(Vector2 referencePoint, out Rect screenBounds)
        {
            screenBounds = default;

#if UNITY_EDITOR_WIN
            if (!TryGetWin32MonitorBounds(referencePoint, out screenBounds))
                return false;

            return screenBounds.width > 0f && screenBounds.height > 0f;
#else
            return false;
#endif
        }

#if UNITY_EDITOR_WIN
        private static bool TryGetWin32MonitorBounds(Vector2 referencePoint, out Rect screenBounds)
        {
            screenBounds = default;

            POINT point;
            point.x = (int)Mathf.Round(referencePoint.x);
            point.y = (int)Mathf.Round(referencePoint.y);
            IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero)
                return false;

            var monitorInfo = new MONITORINFO();
            monitorInfo.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return false;

            RECT workRect = monitorInfo.rcWork;
            screenBounds = new Rect(
                workRect.left,
                workRect.top,
                Mathf.Max(0, workRect.right - workRect.left),
                Mathf.Max(0, workRect.bottom - workRect.top));
            return screenBounds.width > 0f && screenBounds.height > 0f;
        }

        private const int MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int left;
            public int top;
            public int right;
            public int bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);
#endif

        private bool HasVisibleAutomationChecklist()
        {
            return _visibleAutomationChecklistItems != null && _visibleAutomationChecklistItems.Length > 0;
        }

        internal bool ShouldAnimateVisibleAutomationOverlay()
        {
            if (_visibleAutomationOverlayPresentationMode)
                return false;

            if (!string.IsNullOrWhiteSpace(_visibleAutomationOverlayStep)
                && _visibleAutomationOverlayState == VisibleAutomationOverlayState.Running)
                return true;

            if (!HasVisibleAutomationChecklist())
                return false;

            double now = EditorApplication.timeSinceStartup;
            for (int i = 0; i < _visibleAutomationChecklistStates.Length; i++)
            {
                if (_visibleAutomationChecklistStates[i] == VisibleAutomationChecklistItemState.Active)
                    return true;

                if (i < _visibleAutomationChecklistStateChangedAt.Length
                    && now - _visibleAutomationChecklistStateChangedAt[i] < 1.1d)
                    return true;
            }

            return false;
        }

        private int CountChecklistItemsWithState(VisibleAutomationChecklistItemState state)
        {
            if (_visibleAutomationChecklistStates == null)
                return 0;

            int count = 0;
            for (int i = 0; i < _visibleAutomationChecklistStates.Length; i++)
            {
                if (_visibleAutomationChecklistStates[i] == state)
                    count++;
            }

            return count;
        }

        private static string GetVisibleAutomationChecklistItemGlyph(VisibleAutomationChecklistItemState state)
        {
            switch (state)
            {
                case VisibleAutomationChecklistItemState.Completed:
                    return "✓";
                case VisibleAutomationChecklistItemState.Failed:
                    return "!";
                case VisibleAutomationChecklistItemState.Warning:
                    return "!";
                case VisibleAutomationChecklistItemState.Active:
                    return "•";
                default:
                    return "○";
            }
        }

        private static string GetVisibleAutomationChecklistItemStatusLabel(VisibleAutomationChecklistItemState state)
        {
            switch (state)
            {
                case VisibleAutomationChecklistItemState.Completed:
                    return "DONE";
                case VisibleAutomationChecklistItemState.Failed:
                    return "FAIL";
                case VisibleAutomationChecklistItemState.Warning:
                    return "WARN";
                case VisibleAutomationChecklistItemState.Active:
                    return "LIVE";
                default:
                    return "WAIT";
            }
        }

        internal static void GetVisibleAutomationChecklistItemPalette(
            VisibleAutomationChecklistItemState state,
            double stateAgeSeconds,
            double now,
            bool presentationMode,
            out Color backgroundColor,
            out Color borderColor,
            out Color accentColor,
            out Color textColor,
            out Color badgeColor,
            out Color badgeTextColor,
            out Color glyphColor)
        {
            switch (state)
            {
                case VisibleAutomationChecklistItemState.Completed:
                    {
                        if (presentationMode)
                        {
                            backgroundColor = new Color(0.12f, 0.26f, 0.18f, 0.97f);
                            borderColor = new Color(0.28f, 0.64f, 0.44f, 0.98f);
                            accentColor = new Color(0.34f, 0.82f, 0.56f, 1f);
                            textColor = Color.white;
                            badgeColor = new Color(0.24f, 0.58f, 0.40f, 1f);
                            badgeTextColor = Color.white;
                            glyphColor = new Color(0.84f, 1f, 0.90f, 1f);
                            break;
                        }

                        float flash = Mathf.Clamp01(1f - (float)(stateAgeSeconds / 0.9d));
                        backgroundColor = Color.Lerp(
                            new Color(0.08f, 0.15f, 0.11f, 0.94f),
                            new Color(0.18f, 0.34f, 0.24f, 0.98f),
                            flash);
                        borderColor = Color.Lerp(
                            new Color(0.20f, 0.48f, 0.32f, 0.95f),
                            new Color(0.34f, 0.78f, 0.54f, 1f),
                            flash);
                        accentColor = Color.Lerp(
                            new Color(0.22f, 0.62f, 0.40f, 1f),
                            new Color(0.40f, 0.95f, 0.63f, 1f),
                            flash);
                        textColor = Color.white;
                        badgeColor = Color.Lerp(
                            new Color(0.18f, 0.44f, 0.30f, 1f),
                            new Color(0.28f, 0.70f, 0.46f, 1f),
                            flash);
                        badgeTextColor = Color.white;
                        glyphColor = new Color(0.84f, 1f, 0.90f, 1f);
                        break;
                    }
                case VisibleAutomationChecklistItemState.Failed:
                    backgroundColor = new Color(0.15f, 0.08f, 0.09f, 0.98f);
                    borderColor = new Color(0.66f, 0.24f, 0.24f, 0.96f);
                    accentColor = new Color(0.88f, 0.32f, 0.32f, 1f);
                    textColor = Color.white;
                    badgeColor = new Color(0.58f, 0.20f, 0.20f, 1f);
                    badgeTextColor = Color.white;
                    glyphColor = new Color(1f, 0.88f, 0.88f, 1f);
                    break;
                case VisibleAutomationChecklistItemState.Warning:
                    backgroundColor = new Color(0.15f, 0.12f, 0.07f, 0.98f);
                    borderColor = new Color(0.72f, 0.54f, 0.16f, 0.96f);
                    accentColor = new Color(0.94f, 0.72f, 0.20f, 1f);
                    textColor = Color.white;
                    badgeColor = new Color(0.66f, 0.50f, 0.14f, 1f);
                    badgeTextColor = new Color(0.16f, 0.11f, 0.03f, 1f);
                    glyphColor = new Color(1f, 0.95f, 0.78f, 1f);
                    break;
                case VisibleAutomationChecklistItemState.Active:
                    {
                        if (presentationMode)
                        {
                            backgroundColor = new Color(0.11f, 0.17f, 0.24f, 0.98f);
                            borderColor = new Color(0.28f, 0.56f, 0.82f, 0.99f);
                            accentColor = new Color(0.30f, 0.70f, 0.96f, 1f);
                            textColor = Color.white;
                            badgeColor = new Color(0.22f, 0.50f, 0.76f, 1f);
                            badgeTextColor = Color.white;
                            glyphColor = new Color(0.86f, 0.95f, 1f, 1f);
                            break;
                        }

                        float pulse = 0.5f + 0.5f * Mathf.Sin((float)(now * 4.5d));
                        backgroundColor = Color.Lerp(
                            new Color(0.08f, 0.11f, 0.16f, 0.95f),
                            new Color(0.12f, 0.20f, 0.28f, 0.99f),
                            pulse);
                        borderColor = Color.Lerp(
                            new Color(0.20f, 0.40f, 0.58f, 0.96f),
                            new Color(0.32f, 0.68f, 0.94f, 1f),
                            pulse);
                        accentColor = Color.Lerp(
                            new Color(0.20f, 0.48f, 0.76f, 1f),
                            new Color(0.34f, 0.78f, 1f, 1f),
                            pulse);
                        textColor = Color.white;
                        badgeColor = Color.Lerp(
                            new Color(0.16f, 0.40f, 0.62f, 1f),
                            new Color(0.26f, 0.62f, 0.90f, 1f),
                            pulse);
                        badgeTextColor = Color.white;
                        glyphColor = new Color(0.86f, 0.95f, 1f, 1f);
                        break;
                    }
                default:
                    backgroundColor = new Color(0.08f, 0.10f, 0.13f, 0.92f);
                    borderColor = new Color(0.19f, 0.25f, 0.31f, 0.90f);
                    accentColor = new Color(0.25f, 0.31f, 0.37f, 0.92f);
                    textColor = new Color(0.84f, 0.88f, 0.93f, 0.95f);
                    badgeColor = new Color(0.16f, 0.19f, 0.23f, 1f);
                    badgeTextColor = new Color(0.77f, 0.82f, 0.88f, 0.95f);
                    glyphColor = new Color(0.74f, 0.79f, 0.85f, 0.95f);
                    break;
            }
        }

        private string BuildVisibleAutomationOverlayMetaText()
        {
            string progressText = BuildVisibleAutomationOverlayProgressText(_visibleAutomationOverlayStepIndex, _visibleAutomationOverlayTotalSteps);
            if (_visibleAutomationOverlayPresentationMode)
            {
                return string.IsNullOrEmpty(progressText)
                    ? "Presentation Mode"
                    : progressText + " • Presentation Mode";
            }

            return progressText;
        }

        internal VisibleAutomationOverlayHostSnapshot GetVisibleAutomationOverlayHostSnapshotForTesting()
        {
            if (UsesExternalVisibleAutomationOverlay())
                RefreshVisibleAutomationOverlayVisuals();
            else if (NeedsVisibleAutomationOverlayHostResync())
                RefreshVisibleAutomationOverlayVisuals();

            Rect screenBounds = _visibleAutomationScreenBounds;
            if (screenBounds.width <= 0f || screenBounds.height <= 0f)
                TryGetVisibleAutomationScreenBounds(out screenBounds);

            if (UsesExternalVisibleAutomationOverlay())
            {
                return new VisibleAutomationOverlayHostSnapshot(
                    VisibleAutomationOverlayHostKind.ExternalPythonProcess,
                    screenBounds,
                    false,
                    default,
                    false,
                    default,
                    false,
                    default,
                    _visibleAutomationUsingHostWindowFallbackBounds,
                    _visibleAutomationExternalOverlayStatePath,
                    _visibleAutomationExternalOverlayAckPath,
                    File.Exists(_visibleAutomationExternalOverlayStatePath));
            }

            return new VisibleAutomationOverlayHostSnapshot(
                VisibleAutomationOverlayHostKind.DetachedAuxiliaryWindows,
                screenBounds,
                _visibleAutomationStatusOverlayWindow != null,
                _visibleAutomationStatusOverlayWindow != null ? _visibleAutomationStatusOverlayWindow.position : default,
                _visibleAutomationChecklistOverlayWindow != null,
                _visibleAutomationChecklistOverlayWindow != null ? _visibleAutomationChecklistOverlayWindow.position : default,
                _visibleAutomationCompletionReviewOverlayWindow != null,
                _visibleAutomationCompletionReviewOverlayWindow != null ? _visibleAutomationCompletionReviewOverlayWindow.position : default,
                _visibleAutomationUsingHostWindowFallbackBounds,
                _visibleAutomationExternalOverlayStatePath,
                _visibleAutomationExternalOverlayAckPath,
                false);
        }

        internal VisibleAutomationExternalOverlayStateDocument GetVisibleAutomationExternalOverlayStateForTesting()
        {
            if (!UsesExternalVisibleAutomationOverlay() || string.IsNullOrWhiteSpace(_visibleAutomationExternalOverlayStatePath))
                return null;

            if (!File.Exists(_visibleAutomationExternalOverlayStatePath))
                return null;

            try
            {
                string json = File.ReadAllText(_visibleAutomationExternalOverlayStatePath);
                if (string.IsNullOrWhiteSpace(json))
                    return null;

                return JsonUtility.FromJson<VisibleAutomationExternalOverlayStateDocument>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Failed to read visible automation external overlay state for testing: {ex.Message}");
                return null;
            }
        }

        internal bool TryConsumeVisibleAutomationExternalOverlayAcknowledgementForTesting()
        {
            return TryConsumeVisibleAutomationExternalOverlayAcknowledgement();
        }

        internal void ShowVisibleAutomationCompletionReview(string title = null, string message = null)
        {
            _visibleAutomationCompletionReviewTitle = string.IsNullOrWhiteSpace(title)
                ? "Visible smoke results ready"
                : title.Trim();
            _visibleAutomationCompletionReviewMessage = string.IsNullOrWhiteSpace(message)
                ? BuildVisibleAutomationCompletionReviewMessage()
                : message.Trim();
            _visibleAutomationCompletionReviewAcknowledged = false;
            _visibleAutomationCompletionReviewVisible = true;
            _visibleAutomationCompletionReviewRequestId++;
            _visibleAutomationExternalOverlayLastAckPayload = string.Empty;
            DeleteVisibleAutomationExternalOverlayAckFile();
            RefreshVisibleAutomationOverlayVisuals();
            _owner.Repaint();
        }

        internal void AcknowledgeVisibleAutomationCompletionReview()
        {
            _visibleAutomationCompletionReviewAcknowledged = true;
            _visibleAutomationCompletionReviewVisible = false;
            CloseVisibleAutomationCompletionReviewOverlayWindow();
            RefreshVisibleAutomationOverlayVisuals();
            _owner.Repaint();
        }

        internal bool IsVisibleAutomationCompletionReviewVisibleForAutomation()
        {
            return _visibleAutomationCompletionReviewVisible;
        }

        internal bool WasVisibleAutomationCompletionReviewAcknowledgedForAutomation()
        {
            return _visibleAutomationCompletionReviewAcknowledged;
        }

        private void UpdateVisibleAutomationCompletionReviewState()
        {
            if (TryConsumeVisibleAutomationExternalOverlayAcknowledgement())
                return;

            if (!_visibleAutomationCompletionReviewVisible)
                return;

            if (_visibleAutomationCompletionReviewOverlayWindow != null)
                _visibleAutomationCompletionReviewOverlayWindow.Repaint();
        }

        private string BuildVisibleAutomationCompletionReviewMessage()
        {
            int totalItems = _visibleAutomationChecklistItems?.Length ?? 0;
            int completedItems = CountChecklistItemsWithState(VisibleAutomationChecklistItemState.Completed);
            string progressText = BuildVisibleAutomationOverlayProgressText(_visibleAutomationOverlayStepIndex, _visibleAutomationOverlayTotalSteps);
            string statusLabel = GetVisibleAutomationOverlayStatusLabel(_visibleAutomationOverlayState);
            string summaryText = totalItems > 0
                ? $"Checklist complete: {completedItems}/{totalItems} steps finished."
                : "Checklist complete.";

            if (!string.IsNullOrWhiteSpace(progressText))
                return $"{statusLabel} • {progressText}\n{summaryText}\nReview the overlays, then click Accept and close when you are ready to approve this run.";

            return $"{statusLabel}\n{summaryText}\nReview the overlays, then click Accept and close when you are ready to approve this run.";
        }

        private void SetVisibleAutomationChecklistItems(string[] checklistItems)
        {
            if (checklistItems == null || checklistItems.Length == 0)
            {
                _visibleAutomationChecklistItems = Array.Empty<string>();
                _visibleAutomationChecklistStates = Array.Empty<VisibleAutomationChecklistItemState>();
                _visibleAutomationChecklistStateChangedAt = Array.Empty<double>();
                return;
            }

            var sanitized = checklistItems
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .ToArray();

            bool sameItems = _visibleAutomationChecklistItems != null
                && sanitized.Length == _visibleAutomationChecklistItems.Length;
            if (sameItems)
            {
                for (int i = 0; i < sanitized.Length; i++)
                {
                    if (!string.Equals(sanitized[i], _visibleAutomationChecklistItems[i], StringComparison.Ordinal))
                    {
                        sameItems = false;
                        break;
                    }
                }
            }

            if (sameItems)
                return;

            _visibleAutomationChecklistItems = sanitized;
            _visibleAutomationChecklistStates = new VisibleAutomationChecklistItemState[sanitized.Length];
            _visibleAutomationChecklistStateChangedAt = new double[sanitized.Length];
            double now = EditorApplication.timeSinceStartup;
            for (int i = 0; i < sanitized.Length; i++)
                _visibleAutomationChecklistStateChangedAt[i] = now;
        }

        private void UpdateVisibleAutomationChecklistProgress(int stepIndex, VisibleAutomationOverlayState overlayState)
        {
            if (!HasVisibleAutomationChecklist())
                return;

            int clampedActiveIndex = Mathf.Clamp(stepIndex - 1, 0, _visibleAutomationChecklistItems.Length - 1);
            bool hasActiveIndex = stepIndex > 0 && _visibleAutomationChecklistItems.Length > 0;

            for (int i = 0; i < _visibleAutomationChecklistStates.Length; i++)
            {
                VisibleAutomationChecklistItemState desiredState;
                if (!hasActiveIndex)
                {
                    desiredState = overlayState == VisibleAutomationOverlayState.Success
                        ? VisibleAutomationChecklistItemState.Completed
                        : VisibleAutomationChecklistItemState.Pending;
                }
                else if (i < clampedActiveIndex)
                {
                    desiredState = VisibleAutomationChecklistItemState.Completed;
                }
                else if (i > clampedActiveIndex)
                {
                    desiredState = VisibleAutomationChecklistItemState.Pending;
                }
                else
                {
                    desiredState = overlayState switch
                    {
                        VisibleAutomationOverlayState.Success => VisibleAutomationChecklistItemState.Completed,
                        VisibleAutomationOverlayState.Failure => VisibleAutomationChecklistItemState.Failed,
                        VisibleAutomationOverlayState.Warning => VisibleAutomationChecklistItemState.Warning,
                        _ => VisibleAutomationChecklistItemState.Active,
                    };
                }

                if (_visibleAutomationChecklistStates[i] != desiredState)
                {
                    _visibleAutomationChecklistStates[i] = desiredState;
                    if (i < _visibleAutomationChecklistStateChangedAt.Length)
                        _visibleAutomationChecklistStateChangedAt[i] = EditorApplication.timeSinceStartup;
                }
            }
        }

        private static string BuildVisibleAutomationOverlayProgressText(int stepIndex, int totalSteps)
        {
            if (stepIndex > 0 && totalSteps > 0)
                return $"Step {stepIndex}/{totalSteps}";

            if (stepIndex > 0)
                return $"Step {stepIndex}";

            return string.Empty;
        }

        private static string GetVisibleAutomationOverlayStatusLabel(VisibleAutomationOverlayState state)
        {
            switch (state)
            {
                case VisibleAutomationOverlayState.Success:
                    return "PASSED";
                case VisibleAutomationOverlayState.Failure:
                    return "FAILED";
                case VisibleAutomationOverlayState.Warning:
                    return "NOTICE";
                default:
                    return "RUNNING";
            }
        }

        private static void GetVisibleAutomationOverlayPalette(
            VisibleAutomationOverlayState state,
            out Color accentColor,
            out Color backgroundColor,
            out Color borderColor,
            out Color badgeColor,
            out Color badgeTextColor)
        {
            switch (state)
            {
                case VisibleAutomationOverlayState.Success:
                    accentColor = new Color(0.22f, 0.75f, 0.45f, 1f);
                    backgroundColor = new Color(0.06f, 0.11f, 0.08f, 0.96f);
                    borderColor = new Color(0.24f, 0.60f, 0.39f, 0.95f);
                    badgeColor = new Color(0.18f, 0.46f, 0.30f, 1f);
                    badgeTextColor = Color.white;
                    break;
                case VisibleAutomationOverlayState.Failure:
                    accentColor = new Color(0.86f, 0.30f, 0.30f, 1f);
                    backgroundColor = new Color(0.13f, 0.07f, 0.08f, 0.97f);
                    borderColor = new Color(0.58f, 0.22f, 0.22f, 0.96f);
                    badgeColor = new Color(0.52f, 0.18f, 0.18f, 1f);
                    badgeTextColor = Color.white;
                    break;
                case VisibleAutomationOverlayState.Warning:
                    accentColor = new Color(0.90f, 0.67f, 0.22f, 1f);
                    backgroundColor = new Color(0.13f, 0.10f, 0.06f, 0.97f);
                    borderColor = new Color(0.62f, 0.47f, 0.17f, 0.95f);
                    badgeColor = new Color(0.60f, 0.45f, 0.14f, 1f);
                    badgeTextColor = new Color(0.12f, 0.08f, 0.02f, 1f);
                    break;
                default:
                    accentColor = new Color(0.18f, 0.65f, 0.70f, 1f);
                    backgroundColor = new Color(0.07f, 0.08f, 0.10f, 0.94f);
                    borderColor = new Color(0.14f, 0.39f, 0.43f, 0.96f);
                    badgeColor = new Color(0.15f, 0.39f, 0.43f, 1f);
                    badgeTextColor = Color.white;
                    break;
            }
        }

        internal void ConfigureExternalVisibleAutomationOverlay(string statePath, string ackPath)
        {
            string normalizedStatePath = string.IsNullOrWhiteSpace(statePath)
                ? string.Empty
                : Path.GetFullPath(statePath.Trim());
            string normalizedAckPath = string.IsNullOrWhiteSpace(ackPath)
                ? string.Empty
                : Path.GetFullPath(ackPath.Trim());
            bool changed = !string.Equals(_visibleAutomationExternalOverlayStatePath, normalizedStatePath, StringComparison.Ordinal)
                || !string.Equals(_visibleAutomationExternalOverlayAckPath, normalizedAckPath, StringComparison.Ordinal);

            if (!changed
                && string.Equals(_visibleAutomationExternalOverlayStatePath, normalizedStatePath, StringComparison.Ordinal)
                && string.Equals(_visibleAutomationExternalOverlayAckPath, normalizedAckPath, StringComparison.Ordinal)
                && (!string.IsNullOrWhiteSpace(_visibleAutomationExternalOverlaySessionId) || string.IsNullOrWhiteSpace(normalizedStatePath)))
            {
                return;
            }

            _visibleAutomationExternalOverlayStatePath = normalizedStatePath;
            _visibleAutomationExternalOverlayAckPath = normalizedAckPath;
            _visibleAutomationExternalOverlaySessionId = string.IsNullOrWhiteSpace(normalizedStatePath)
                ? string.Empty
                : Guid.NewGuid().ToString("N");
            _externalPublisher = new ASMLiteSmokePresentationPublisher();
            _visibleAutomationExternalOverlayLastAckPayload = string.Empty;
            _visibleAutomationCompletionReviewRequestId = 0;
            DeleteVisibleAutomationExternalOverlayAckFile();
            RefreshVisibleAutomationOverlayVisuals();
        }

        internal void SetVisibleAutomationOverlayStatus(
            string title,
            string step,
            int stepIndex = 0,
            int totalSteps = 0,
            VisibleAutomationOverlayState state = VisibleAutomationOverlayState.Running,
            bool presentationMode = false,
            string[] checklistItems = null)
        {
            _visibleAutomationOverlayTitle = string.IsNullOrWhiteSpace(title)
                ? "ASM-Lite visible smoke test"
                : title.Trim();
            _visibleAutomationOverlayStep = string.IsNullOrWhiteSpace(step)
                ? string.Empty
                : step.Trim();
            _visibleAutomationOverlayStepIndex = Mathf.Max(0, stepIndex);
            _visibleAutomationOverlayTotalSteps = Mathf.Max(0, totalSteps);
            _visibleAutomationOverlayState = state;
            _visibleAutomationOverlayPresentationMode = presentationMode;

            if (checklistItems != null)
                SetVisibleAutomationChecklistItems(checklistItems);

            UpdateVisibleAutomationChecklistProgress(_visibleAutomationOverlayStepIndex, _visibleAutomationOverlayState);
            RefreshVisibleAutomationOverlayVisuals();
            _owner.Repaint();
        }

        internal void ClearVisibleAutomationOverlay()
        {
            _visibleAutomationOverlayTitle = string.Empty;
            _visibleAutomationOverlayStep = string.Empty;
            _visibleAutomationOverlayStepIndex = 0;
            _visibleAutomationOverlayTotalSteps = 0;
            _visibleAutomationOverlayState = VisibleAutomationOverlayState.Running;
            _visibleAutomationOverlayPresentationMode = false;
            _visibleAutomationChecklistItems = Array.Empty<string>();
            _visibleAutomationChecklistStates = Array.Empty<VisibleAutomationChecklistItemState>();
            _visibleAutomationChecklistStateChangedAt = Array.Empty<double>();
            _visibleAutomationCompletionReviewVisible = false;
            _visibleAutomationCompletionReviewAcknowledged = false;
            _visibleAutomationCompletionReviewTitle = string.Empty;
            _visibleAutomationCompletionReviewMessage = string.Empty;
            _visibleAutomationUsingHostWindowFallbackBounds = false;
            CloseVisibleAutomationOverlayPopupWindows();
            RefreshVisibleAutomationOverlayVisuals();
            _owner.Repaint();
        }

        private abstract class VisibleAutomationOverlayPopupWindowBase : EditorWindow
        {
            private const float PositionSyncEpsilon = 0.5f;

            protected ASMLiteSmokePresentation Owner;
            private bool _hasBeenShown;

            protected static void ApplyBorder(VisualElement element, Color color, float width = 1f)
            {
                if (element == null)
                    return;

                element.style.borderLeftWidth = width;
                element.style.borderRightWidth = width;
                element.style.borderTopWidth = width;
                element.style.borderBottomWidth = width;
                element.style.borderLeftColor = color;
                element.style.borderRightColor = color;
                element.style.borderTopColor = color;
                element.style.borderBottomColor = color;
            }

            protected void PrepareWindow(ASMLiteSmokePresentation owner)
            {
                Owner = owner;
                titleContent = GUIContent.none;
                minSize = new Vector2(120f, 60f);
                maxSize = new Vector2(10000f, 10000f);
            }

            protected void ShowOverlayWindow(Rect rect)
            {
                if (!_hasBeenShown)
                {
                    position = rect;
                    ShowAuxWindow();
                    position = rect;
                    _hasBeenShown = true;
                    return;
                }

                if (!HasMeaningfulPositionChange(position, rect))
                    return;

                position = rect;
                Repaint();
            }

            private static bool HasMeaningfulPositionChange(Rect current, Rect next)
            {
                return Mathf.Abs(current.x - next.x) > PositionSyncEpsilon
                    || Mathf.Abs(current.y - next.y) > PositionSyncEpsilon
                    || Mathf.Abs(current.width - next.width) > PositionSyncEpsilon
                    || Mathf.Abs(current.height - next.height) > PositionSyncEpsilon;
            }

            protected virtual void OnDisable()
            {
                _hasBeenShown = false;
            }
        }

        private sealed class VisibleAutomationStatusOverlayWindow : VisibleAutomationOverlayPopupWindowBase
        {
            private VisualElement _panel;
            private VisualElement _accent;
            private Label _titleLabel;
            private Label _metaLabel;
            private Label _stepLabel;
            private VisualElement _badge;
            private Label _badgeLabel;

            internal static VisibleAutomationStatusOverlayWindow Create(ASMLiteSmokePresentation owner)
            {
                var window = CreateInstance<VisibleAutomationStatusOverlayWindow>();
                window.PrepareWindow(owner);
                return window;
            }

            public void CreateGUI()
            {
                rootVisualElement.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                rootVisualElement.style.paddingLeft = 0f;
                rootVisualElement.style.paddingRight = 0f;
                rootVisualElement.style.paddingTop = 0f;
                rootVisualElement.style.paddingBottom = 0f;
                rootVisualElement.pickingMode = PickingMode.Ignore;

                _panel = new VisualElement { pickingMode = PickingMode.Ignore };
                _panel.style.flexDirection = FlexDirection.Column;
                _panel.style.flexGrow = 1f;
                _panel.style.overflow = Overflow.Hidden;
                rootVisualElement.Add(_panel);

                _accent = new VisualElement { pickingMode = PickingMode.Ignore };
                _accent.style.height = 4f;
                _panel.Add(_accent);

                var body = new VisualElement { pickingMode = PickingMode.Ignore };
                body.style.flexDirection = FlexDirection.Column;
                body.style.flexGrow = 1f;
                body.style.paddingLeft = 16f;
                body.style.paddingRight = 16f;
                body.style.paddingTop = 14f;
                body.style.paddingBottom = 14f;
                _panel.Add(body);

                var headerRow = new VisualElement { pickingMode = PickingMode.Ignore };
                headerRow.style.flexDirection = FlexDirection.Row;
                headerRow.style.alignItems = Align.FlexStart;
                body.Add(headerRow);

                var titleColumn = new VisualElement { pickingMode = PickingMode.Ignore };
                titleColumn.style.flexDirection = FlexDirection.Column;
                titleColumn.style.flexGrow = 1f;
                titleColumn.style.marginRight = 12f;
                headerRow.Add(titleColumn);

                _titleLabel = new Label { pickingMode = PickingMode.Ignore };
                _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleColumn.Add(_titleLabel);

                _metaLabel = new Label { pickingMode = PickingMode.Ignore };
                titleColumn.Add(_metaLabel);

                _badge = new VisualElement { pickingMode = PickingMode.Ignore };
                _badge.style.minWidth = 96f;
                _badge.style.height = 22f;
                _badge.style.justifyContent = Justify.Center;
                _badge.style.alignItems = Align.Center;
                _badge.style.paddingLeft = 10f;
                _badge.style.paddingRight = 10f;
                headerRow.Add(_badge);

                _badgeLabel = new Label { pickingMode = PickingMode.Ignore };
                _badgeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _badge.Add(_badgeLabel);

                _stepLabel = new Label { pickingMode = PickingMode.Ignore };
                _stepLabel.style.whiteSpace = WhiteSpace.Normal;
                _stepLabel.style.marginTop = 12f;
                _stepLabel.style.flexGrow = 1f;
                body.Add(_stepLabel);
            }

            internal void SyncFromOwner(ASMLiteSmokePresentation owner, Rect rect)
            {
                if (owner == null)
                    return;

                PrepareWindow(owner);
                ShowOverlayWindow(rect);
                if (_panel == null)
                    return;

                GetVisibleAutomationOverlayPalette(
                    owner._visibleAutomationOverlayState,
                    out Color accentColor,
                    out Color backgroundColor,
                    out Color borderColor,
                    out Color badgeColor,
                    out Color badgeTextColor);

                bool presentationMode = owner._visibleAutomationOverlayPresentationMode;
                _panel.style.backgroundColor = backgroundColor;
                ApplyBorder(_panel, borderColor);
                _accent.style.backgroundColor = accentColor;
                _titleLabel.text = string.IsNullOrWhiteSpace(owner._visibleAutomationOverlayTitle)
                    ? "ASM-Lite visible smoke test"
                    : owner._visibleAutomationOverlayTitle;
                _titleLabel.style.fontSize = presentationMode ? 15 : 12;
                _titleLabel.style.color = Color.white;

                string metaText = owner.BuildVisibleAutomationOverlayMetaText();
                _metaLabel.text = metaText;
                _metaLabel.style.display = string.IsNullOrEmpty(metaText) ? DisplayStyle.None : DisplayStyle.Flex;
                _metaLabel.style.fontSize = presentationMode ? 11 : 10;
                _metaLabel.style.color = new Color(0.74f, 0.82f, 0.91f, 0.96f);

                _stepLabel.text = owner._visibleAutomationOverlayStep;
                _stepLabel.style.fontSize = presentationMode ? 16 : 12;
                _stepLabel.style.color = new Color(0.95f, 0.97f, 0.99f, 1f);

                _badge.style.backgroundColor = badgeColor;
                _badgeLabel.text = GetVisibleAutomationOverlayStatusLabel(owner._visibleAutomationOverlayState);
                _badgeLabel.style.fontSize = presentationMode ? 11 : 10;
                _badgeLabel.style.color = badgeTextColor;
            }
        }

        private sealed class VisibleAutomationChecklistOverlayWindow : VisibleAutomationOverlayPopupWindowBase
        {
            private VisualElement _panel;
            private VisualElement _accent;
            private Label _titleLabel;
            private Label _metaLabel;
            private ScrollView _scrollView;
            private VisualElement _itemsContainer;
            private readonly List<VisibleAutomationChecklistVisualRefs> _itemVisuals = new List<VisibleAutomationChecklistVisualRefs>();

            internal static VisibleAutomationChecklistOverlayWindow Create(ASMLiteSmokePresentation owner)
            {
                var window = CreateInstance<VisibleAutomationChecklistOverlayWindow>();
                window.PrepareWindow(owner);
                return window;
            }

            public void CreateGUI()
            {
                rootVisualElement.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                rootVisualElement.pickingMode = PickingMode.Ignore;

                _panel = new VisualElement { pickingMode = PickingMode.Ignore };
                _panel.style.flexDirection = FlexDirection.Column;
                _panel.style.flexGrow = 1f;
                _panel.style.overflow = Overflow.Hidden;
                rootVisualElement.Add(_panel);

                _accent = new VisualElement { pickingMode = PickingMode.Ignore };
                _accent.style.height = 5f;
                _panel.Add(_accent);

                var body = new VisualElement { pickingMode = PickingMode.Ignore };
                body.style.flexDirection = FlexDirection.Column;
                body.style.flexGrow = 1f;
                body.style.paddingLeft = 12f;
                body.style.paddingRight = 10f;
                body.style.paddingTop = 12f;
                body.style.paddingBottom = 12f;
                _panel.Add(body);

                _titleLabel = new Label("Visible Smoke Checklist") { pickingMode = PickingMode.Ignore };
                _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                body.Add(_titleLabel);

                _metaLabel = new Label { pickingMode = PickingMode.Ignore };
                _metaLabel.style.marginTop = 2f;
                body.Add(_metaLabel);

                _scrollView = new ScrollView(ScrollViewMode.Vertical)
                {
                    pickingMode = PickingMode.Ignore,
                };
                _scrollView.style.flexGrow = 1f;
                _scrollView.style.marginTop = 10f;
                _scrollView.style.paddingRight = 2f;
                _scrollView.verticalScrollerVisibility = ScrollerVisibility.AlwaysVisible;
                _scrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                body.Add(_scrollView);

                _itemsContainer = new VisualElement { pickingMode = PickingMode.Ignore };
                _itemsContainer.style.flexDirection = FlexDirection.Column;
                _itemsContainer.style.flexGrow = 1f;
                _scrollView.Add(_itemsContainer);
            }

            internal void SyncFromOwner(ASMLiteSmokePresentation owner, Rect rect)
            {
                if (owner == null)
                    return;

                PrepareWindow(owner);
                ShowOverlayWindow(rect);
                if (_panel == null)
                    return;

                bool presentationMode = owner._visibleAutomationOverlayPresentationMode;
                int totalItems = owner._visibleAutomationChecklistItems.Length;
                int completedItems = owner.CountChecklistItemsWithState(VisibleAutomationChecklistItemState.Completed);

                _panel.style.backgroundColor = new Color(0.05f, 0.07f, 0.09f, 0.96f);
                ApplyBorder(_panel, new Color(0.14f, 0.39f, 0.43f, 0.96f));
                _accent.style.backgroundColor = new Color(0.18f, 0.65f, 0.70f, 1f);

                _titleLabel.text = "Visible Smoke Checklist";
                _titleLabel.style.fontSize = presentationMode ? 13 : 11;
                _titleLabel.style.color = Color.white;

                _metaLabel.text = $"Completed {completedItems}/{totalItems} • Scroll for full test list";
                _metaLabel.style.fontSize = presentationMode ? 10 : 9;
                _metaLabel.style.color = new Color(0.76f, 0.84f, 0.92f, 0.95f);

                if (_scrollView != null)
                    _scrollView.style.marginTop = presentationMode ? 12f : 10f;

                EnsureVisualCount(totalItems);
                double now = EditorApplication.timeSinceStartup;
                for (int i = 0; i < totalItems; i++)
                    UpdateItemVisual(owner, _itemVisuals[i], i, now, totalItems);
            }

            private void EnsureVisualCount(int totalItems)
            {
                if (_itemsContainer == null)
                    return;

                while (_itemVisuals.Count > totalItems)
                {
                    int last = _itemVisuals.Count - 1;
                    _itemVisuals[last].Root.RemoveFromHierarchy();
                    _itemVisuals.RemoveAt(last);
                }

                while (_itemVisuals.Count < totalItems)
                {
                    var refs = CreateChecklistVisual();
                    _itemsContainer.Add(refs.Root);
                    _itemVisuals.Add(refs);
                }
            }

            private static VisibleAutomationChecklistVisualRefs CreateChecklistVisual()
            {
                var refs = new VisibleAutomationChecklistVisualRefs();
                refs.Root = new VisualElement { pickingMode = PickingMode.Ignore };
                refs.Root.style.flexDirection = FlexDirection.Row;
                refs.Root.style.alignItems = Align.Stretch;
                refs.Root.style.flexGrow = 1f;
                refs.Root.style.minHeight = VisibleAutomationChecklistCompactItemHeight;
                refs.Root.style.marginBottom = 6f;
                refs.Root.style.overflow = Overflow.Hidden;

                refs.Accent = new VisualElement { pickingMode = PickingMode.Ignore };
                refs.Accent.style.width = 4f;
                refs.Accent.style.flexShrink = 0f;
                refs.Root.Add(refs.Accent);

                var content = new VisualElement { pickingMode = PickingMode.Ignore };
                content.style.flexDirection = FlexDirection.Column;
                content.style.flexGrow = 1f;
                content.style.paddingLeft = 9f;
                content.style.paddingRight = 8f;
                content.style.paddingTop = 7f;
                content.style.paddingBottom = 5f;
                refs.Root.Add(content);

                var topRow = new VisualElement { pickingMode = PickingMode.Ignore };
                topRow.style.flexDirection = FlexDirection.Row;
                topRow.style.alignItems = Align.FlexStart;
                topRow.style.flexGrow = 1f;
                content.Add(topRow);

                refs.Glyph = new Label { pickingMode = PickingMode.Ignore };
                refs.Glyph.style.width = 20f;
                refs.Glyph.style.minWidth = 20f;
                refs.Glyph.style.marginRight = 6f;
                refs.Glyph.style.unityFontStyleAndWeight = FontStyle.Bold;
                refs.Glyph.style.unityTextAlign = TextAnchor.UpperCenter;
                refs.Glyph.style.whiteSpace = WhiteSpace.NoWrap;
                topRow.Add(refs.Glyph);

                refs.Text = new Label { pickingMode = PickingMode.Ignore };
                refs.Text.style.flexGrow = 1f;
                refs.Text.style.whiteSpace = WhiteSpace.Normal;
                refs.Text.style.unityTextAlign = TextAnchor.UpperLeft;
                refs.Text.style.marginRight = 6f;
                topRow.Add(refs.Text);

                refs.Badge = new VisualElement { pickingMode = PickingMode.Ignore };
                refs.Badge.style.minWidth = 52f;
                refs.Badge.style.height = 18f;
                refs.Badge.style.justifyContent = Justify.Center;
                refs.Badge.style.alignItems = Align.Center;
                refs.Badge.style.paddingLeft = 6f;
                refs.Badge.style.paddingRight = 6f;
                refs.Badge.style.flexShrink = 0f;
                topRow.Add(refs.Badge);

                refs.BadgeLabel = new Label { pickingMode = PickingMode.Ignore };
                refs.BadgeLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                refs.BadgeLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                refs.BadgeLabel.style.whiteSpace = WhiteSpace.NoWrap;
                refs.Badge.Add(refs.BadgeLabel);

                refs.StepLabel = new Label { pickingMode = PickingMode.Ignore };
                refs.StepLabel.style.marginTop = 3f;
                refs.StepLabel.style.whiteSpace = WhiteSpace.NoWrap;
                refs.StepLabel.style.unityTextAlign = TextAnchor.LowerLeft;
                content.Add(refs.StepLabel);
                return refs;
            }

            private static void UpdateItemVisual(ASMLiteSmokePresentation owner, VisibleAutomationChecklistVisualRefs refs, int itemIndex, double now, int totalItems)
            {
                string itemLabel = owner._visibleAutomationChecklistItems[itemIndex];
                var itemState = owner._visibleAutomationChecklistStates[itemIndex];
                double changedAt = itemIndex < owner._visibleAutomationChecklistStateChangedAt.Length
                    ? owner._visibleAutomationChecklistStateChangedAt[itemIndex]
                    : now;
                double stateAgeSeconds = Mathf.Max(0f, (float)(now - changedAt));

                GetVisibleAutomationChecklistItemPalette(
                    itemState,
                    stateAgeSeconds,
                    now,
                    owner._visibleAutomationOverlayPresentationMode,
                    out Color backgroundColor,
                    out Color borderColor,
                    out Color accentColor,
                    out Color textColor,
                    out Color badgeColor,
                    out Color badgeTextColor,
                    out Color glyphColor);

                refs.Root.style.backgroundColor = backgroundColor;
                ApplyBorder(refs.Root, borderColor);
                refs.Root.style.minHeight = owner._visibleAutomationOverlayPresentationMode ? 48f : VisibleAutomationChecklistCompactItemHeight;
                refs.Root.style.marginBottom = itemIndex < totalItems - 1 ? 6f : 0f;
                refs.Accent.style.backgroundColor = accentColor;
                refs.Glyph.text = GetVisibleAutomationChecklistItemGlyph(itemState);
                refs.Glyph.style.fontSize = owner._visibleAutomationOverlayPresentationMode ? 16 : 14;
                refs.Glyph.style.color = glyphColor;

                refs.Text.text = itemLabel;
                refs.Text.style.fontSize = owner._visibleAutomationOverlayPresentationMode ? 12 : 11;
                refs.Text.style.color = textColor;

                refs.Badge.style.backgroundColor = badgeColor;
                refs.BadgeLabel.text = GetVisibleAutomationChecklistItemStatusLabel(itemState);
                refs.BadgeLabel.style.fontSize = owner._visibleAutomationOverlayPresentationMode ? 9 : 8;
                refs.BadgeLabel.style.color = badgeTextColor;

                refs.StepLabel.text = $"Step {itemIndex + 1}";
                refs.StepLabel.style.fontSize = owner._visibleAutomationOverlayPresentationMode ? 9 : 8;
                refs.StepLabel.style.color = new Color(0.72f, 0.78f, 0.86f, 0.92f);
            }
        }

        private sealed class VisibleAutomationCompletionReviewOverlayWindow : VisibleAutomationOverlayPopupWindowBase
        {
            private VisualElement _panel;
            private VisualElement _accent;
            private Label _titleLabel;
            private Label _messageLabel;
            private Label _hintLabel;
            private Button _finishButton;

            internal static VisibleAutomationCompletionReviewOverlayWindow Create(ASMLiteSmokePresentation owner)
            {
                var window = CreateInstance<VisibleAutomationCompletionReviewOverlayWindow>();
                window.PrepareWindow(owner);
                return window;
            }

            public void CreateGUI()
            {
                rootVisualElement.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
                rootVisualElement.style.paddingLeft = 0f;
                rootVisualElement.style.paddingRight = 0f;
                rootVisualElement.style.paddingTop = 0f;
                rootVisualElement.style.paddingBottom = 0f;

                _panel = new VisualElement();
                _panel.style.flexDirection = FlexDirection.Column;
                _panel.style.flexGrow = 1f;
                _panel.style.overflow = Overflow.Hidden;
                rootVisualElement.Add(_panel);

                _accent = new VisualElement();
                _accent.style.height = 5f;
                _panel.Add(_accent);

                var body = new VisualElement();
                body.style.flexDirection = FlexDirection.Column;
                body.style.flexGrow = 1f;
                body.style.paddingLeft = 18f;
                body.style.paddingRight = 18f;
                body.style.paddingTop = 16f;
                body.style.paddingBottom = 16f;
                _panel.Add(body);

                _titleLabel = new Label();
                _titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                _titleLabel.style.fontSize = 15;
                _titleLabel.style.color = Color.white;
                _titleLabel.style.whiteSpace = WhiteSpace.Normal;
                body.Add(_titleLabel);

                _messageLabel = new Label();
                _messageLabel.style.marginTop = 10f;
                _messageLabel.style.whiteSpace = WhiteSpace.Normal;
                _messageLabel.style.flexGrow = 1f;
                _messageLabel.style.color = new Color(0.92f, 0.96f, 0.99f, 1f);
                _messageLabel.style.fontSize = 12;
                body.Add(_messageLabel);

                _hintLabel = new Label();
                _hintLabel.style.marginTop = 10f;
                _hintLabel.style.color = new Color(0.77f, 0.84f, 0.91f, 0.96f);
                _hintLabel.style.fontSize = 10;
                _hintLabel.style.whiteSpace = WhiteSpace.Normal;
                body.Add(_hintLabel);

                _finishButton = new Button(() => Owner?.AcknowledgeVisibleAutomationCompletionReview())
                {
                    text = "Accept and close"
                };
                _finishButton.style.marginTop = 14f;
                _finishButton.style.height = 28f;
                _finishButton.style.unityFontStyleAndWeight = FontStyle.Bold;
                body.Add(_finishButton);
            }

            internal void SyncFromOwner(ASMLiteSmokePresentation owner, Rect rect)
            {
                if (owner == null)
                    return;

                PrepareWindow(owner);
                ShowOverlayWindow(rect);
                if (_panel == null)
                    return;

                _panel.style.backgroundColor = new Color(0.06f, 0.09f, 0.12f, 0.98f);
                ApplyBorder(_panel, new Color(0.24f, 0.53f, 0.62f, 0.98f));
                _accent.style.backgroundColor = new Color(0.22f, 0.74f, 0.83f, 1f);

                _titleLabel.text = string.IsNullOrWhiteSpace(owner._visibleAutomationCompletionReviewTitle)
                    ? "Visible smoke results ready"
                    : owner._visibleAutomationCompletionReviewTitle;
                _messageLabel.text = owner._visibleAutomationCompletionReviewMessage ?? string.Empty;

                _hintLabel.text = "Review the overlays before exiting, then click Accept and close to approve this visible smoke run.";
            }
        }
    }
}
