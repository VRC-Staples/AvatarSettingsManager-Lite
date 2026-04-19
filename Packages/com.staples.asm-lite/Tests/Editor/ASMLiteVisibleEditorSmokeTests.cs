using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteVisibleEditorSmokeTests
    {
        private const string OverlayTitle = "ASM-Lite visible smoke test";
        private const int VisibleSmokeTotalSteps = 5;
        private static readonly string[] VisibleSmokeChecklist =
        {
            "Open the ASM-Lite editor window",
            "Select the live avatar from the hierarchy",
            "Execute Add ASM-Lite Prefab from the visible primary action",
            "Verify the rendered primary action updates to Rebuild",
            "Confirm the visible smoke run completed successfully",
        };
        private const float DefaultStepDelaySeconds = 1.0f;
        private const string StepDelayEnvVarName = "ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS";

        private AsmLiteTestContext _ctx;
        private ASMLite.Editor.ASMLiteWindow _window;
        private string _externalOverlayTempDir;
        private string _externalOverlayStatePath;
        private string _externalOverlayAckPath;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            Selection.activeGameObject = null;
            ConfigureExternalOverlayPaths();

            if (_ctx.Comp != null)
            {
                Object.DestroyImmediate(_ctx.Comp.gameObject);
                _ctx.Comp = null;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (_window != null)
            {
                _window.ClearVisibleAutomationOverlay();
                _window.Close();
            }

            Selection.activeGameObject = null;
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            CleanupExternalOverlayPaths();
            _window = null;
            _ctx = null;
        }

        [Test]
        public void CommandLineConfiguration_ParsesExternalOverlayPaths()
        {
            string resultsPath = Path.Combine(_externalOverlayTempDir, "visible-results.xml");
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity.exe",
                "-asmliteVisibleAutomationResultsPath", resultsPath,
                "-asmliteVisibleAutomationSelector", "ASMLiteVisibleEditorSmokeTests",
                "-asmliteVisibleAutomationMode", "editor",
                "-asmliteVisibleAutomationExternalOverlayStatePath", _externalOverlayStatePath,
                "-asmliteVisibleAutomationExternalOverlayAckPath", _externalOverlayAckPath,
            });

            Assert.AreEqual(Path.GetFullPath(resultsPath), Path.GetFullPath(configuration.resultsPath),
                "Visible automation command-line parsing should preserve the explicit results path.");
            Assert.AreEqual(Path.GetFullPath(_externalOverlayStatePath), configuration.externalOverlayStatePath,
                "Visible automation command-line parsing should forward the external overlay state path to the persisted run configuration.");
            Assert.AreEqual(Path.GetFullPath(_externalOverlayAckPath), configuration.externalOverlayAckPath,
                "Visible automation command-line parsing should forward the external overlay acknowledgement path to the persisted run configuration.");
            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.Editor, configuration.mode,
                "Visible automation command-line parsing should preserve the requested editor-mode selector.");
        }

        [Test]
        public void ExternalOverlayState_PublishesAndConsumesAcknowledgement()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.ConfigureExternalVisibleAutomationOverlay(_externalOverlayStatePath, _externalOverlayAckPath);
                window.SetVisibleAutomationOverlayStatus(
                    OverlayTitle,
                    "Executing Add ASM-Lite Prefab through the rendered primary action",
                    stepIndex: 3,
                    totalSteps: VisibleSmokeTotalSteps,
                    state: ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Running,
                    presentationMode: true,
                    checklistItems: VisibleSmokeChecklist);

                var snapshot = window.GetVisibleAutomationOverlayHostSnapshotForTesting();
                Assert.AreEqual(
                    ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayHostKind.ExternalPythonProcess,
                    snapshot.HostKind,
                    "Configured visible automation overlays should publish state through the external Python host.");
                Assert.AreEqual(Path.GetFullPath(_externalOverlayStatePath), snapshot.ExternalOverlayStatePath,
                    "Configured visible automation overlays should report the external state path used for publishing overlay updates.");
                Assert.AreEqual(Path.GetFullPath(_externalOverlayAckPath), snapshot.ExternalOverlayAckPath,
                    "Configured visible automation overlays should report the external acknowledgement path used for completion review acceptance.");
                Assert.IsTrue(snapshot.ExternalOverlayStateFileExists,
                    "Configured visible automation overlays should materialize the external state file as soon as the first step is published.");
                Assert.IsFalse(snapshot.HasStatusWindow,
                    "Configured visible automation overlays should not keep the old detached status popup alive.");
                Assert.IsFalse(snapshot.HasChecklistWindow,
                    "Configured visible automation overlays should not keep the old detached checklist popup alive.");

                var stateDocument = window.GetVisibleAutomationExternalOverlayStateForTesting();
                Assert.IsNotNull(stateDocument,
                    "Configured visible automation overlays should serialize a readable external state document for the Python overlay process.");
                Assert.IsTrue(stateDocument.sessionActive,
                    "Configured visible automation overlays should mark the external session active while a visible smoke step is running.");
                Assert.AreEqual(OverlayTitle, stateDocument.title,
                    "Configured visible automation overlays should publish the current overlay title into the external state document.");
                Assert.AreEqual(3, stateDocument.stepIndex,
                    "Configured visible automation overlays should publish the current visible smoke step index into the external state document.");
                Assert.AreEqual(VisibleSmokeChecklist.Length, stateDocument.checklist.Length,
                    "Configured visible automation overlays should publish the full checklist so the external Python overlay can render it.");

                window.ShowVisibleAutomationCompletionReview();
                stateDocument = window.GetVisibleAutomationExternalOverlayStateForTesting();
                Assert.IsTrue(stateDocument.completionReviewVisible,
                    "Configured visible automation overlays should publish completion review visibility into the external state document.");
                Assert.Greater(stateDocument.completionReviewRequestId, 0,
                    "Configured visible automation overlays should increment the completion review request id so the external acknowledgement can target the active review.");

                WriteCompletionReviewAcknowledgement(window);

                Assert.IsTrue(window.TryConsumeVisibleAutomationExternalOverlayAcknowledgementForTesting(),
                    "Configured visible automation overlays should consume acknowledgement payloads written by the external Python overlay process.");
                Assert.IsFalse(window.IsVisibleAutomationCompletionReviewVisibleForAutomation(),
                    "Configured visible automation overlays should close the completion review after a valid external acknowledgement is received.");
                Assert.IsTrue(window.WasVisibleAutomationCompletionReviewAcknowledgedForAutomation(),
                    "Configured visible automation overlays should mark the completion review as acknowledged after a valid external acknowledgement is received.");
            }
            finally
            {
                window.ClearVisibleAutomationOverlay();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PresentationModeOverlay_RunningStep_DoesNotAnimate()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SetVisibleAutomationOverlayStatus(
                    OverlayTitle,
                    "Executing Add ASM-Lite Prefab through the rendered primary action",
                    stepIndex: 3,
                    totalSteps: VisibleSmokeTotalSteps,
                    state: ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Running,
                    presentationMode: true,
                    checklistItems: VisibleSmokeChecklist);

                Assert.IsFalse(InvokeShouldAnimateVisibleAutomationOverlay(window),
                    "Visible smoke presentation overlays should stay visually stable instead of pulsing every editor update.");
            }
            finally
            {
                window.ClearVisibleAutomationOverlay();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void NonPresentationOverlay_RunningStep_StillAnimates()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SetVisibleAutomationOverlayStatus(
                    OverlayTitle,
                    "Executing Add ASM-Lite Prefab through the rendered primary action",
                    stepIndex: 3,
                    totalSteps: VisibleSmokeTotalSteps,
                    state: ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Running,
                    presentationMode: false,
                    checklistItems: VisibleSmokeChecklist);

                Assert.IsTrue(InvokeShouldAnimateVisibleAutomationOverlay(window),
                    "Non-presentation overlays should keep their existing live animation behavior.");
            }
            finally
            {
                window.ClearVisibleAutomationOverlay();
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PresentationModeChecklist_ActiveItem_DoesNotAnimate()
        {
            var earlyPalette = InvokeChecklistItemPalette(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Active,
                stateAgeSeconds: 0.1d,
                now: 1.0d,
                presentationMode: true);
            var latePalette = InvokeChecklistItemPalette(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Active,
                stateAgeSeconds: 0.8d,
                now: 4.0d,
                presentationMode: true);

            AssertPaletteEqual(earlyPalette, latePalette,
                "Visible smoke presentation checklist items should stay visually stable instead of pulsing while active.");
        }

        [Test]
        public void PresentationModeChecklist_CompletedItem_DoesNotFlash()
        {
            var earlyPalette = InvokeChecklistItemPalette(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Completed,
                stateAgeSeconds: 0.05d,
                now: 1.0d,
                presentationMode: true);
            var latePalette = InvokeChecklistItemPalette(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Completed,
                stateAgeSeconds: 1.2d,
                now: 4.0d,
                presentationMode: true);

            AssertPaletteEqual(earlyPalette, latePalette,
                "Visible smoke presentation checklist items should not flash after a step transitions to completed.");
        }

        [Test]
        public void NonPresentationChecklist_ActiveItem_StillAnimates()
        {
            var earlyPalette = InvokeChecklistItemPalette(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Active,
                stateAgeSeconds: 0.1d,
                now: 1.0d,
                presentationMode: false);
            var latePalette = InvokeChecklistItemPalette(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Active,
                stateAgeSeconds: 0.8d,
                now: 4.0d,
                presentationMode: false);

            AssertPaletteNotEqual(earlyPalette, latePalette,
                "Non-presentation checklist items should keep their existing animated active state.");
        }

        [UnityTest]
        [Category("VisibleEditorAutomation")]
        public IEnumerator VisibleWindow_AddPrefab_PrimaryActionExecutesThroughRenderedWindow()
        {
            IEnumerator WaitForCompletionReviewToBeVisibleLocal()
            {
                double deadline = EditorApplication.timeSinceStartup + 2.5d;
                while (EditorApplication.timeSinceStartup < deadline)
                {
                    if (_window == null)
                        Assert.Fail("Visible smoke automation should keep the ASM-Lite editor window alive while the completion review is displayed.");

                    _window.Repaint();
                    if (GetCompletionReviewVisible(_window))
                    {
                        AssertOverlayHostSnapshot(_window, expectCompletionReviewWindow: true);
                        yield break;
                    }

                    yield return null;
                }

                Assert.IsTrue(GetCompletionReviewVisible(_window),
                    "Visible smoke automation should display the completion review overlay before exiting.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(GetCompletionReviewTitle(_window)),
                    "Visible smoke automation should populate the completion review title.");
                StringAssert.Contains("Review the overlays", GetCompletionReviewMessage(_window),
                    "Visible smoke automation should populate the completion review message with review guidance.");
            }

            IEnumerator WaitForCompletionReviewToBeAcceptedLocal()
            {
                while (true)
                {
                    if (_window == null)
                        Assert.Fail("Visible smoke automation should keep the ASM-Lite editor window alive until the user accepts the completion review.");

                    _window.Repaint();
                    if (!GetCompletionReviewVisible(_window))
                        break;

                    yield return null;
                }

                Assert.IsTrue(GetCompletionReviewAcknowledged(_window),
                    "Visible smoke automation should require explicit user acceptance before the completion review can close.");
            }
            if (!CanRenderVisibleEditorWindow())
            {
                Assert.Ignore("Visible editor smoke automation requires a graphics-backed local Unity editor window.");
                yield break;
            }

            _window = ASMLite.Editor.ASMLiteWindow.OpenForAutomation();
            _window.ConfigureExternalVisibleAutomationOverlay(_externalOverlayStatePath, _externalOverlayAckPath);
            _window.position = new Rect(120f, 120f, 920f, 900f);
            _window.Focus();
            SetOverlayStep(1, "Opening ASM-Lite editor window");
            _window.Repaint();

            yield return WaitForVisibleStep(_window, 1, "Opening ASM-Lite editor window");

            Assert.IsTrue(EditorWindow.HasOpenInstances<ASMLite.Editor.ASMLiteWindow>(),
                "Visible smoke automation should open the ASM-Lite editor window on screen.");

            Selection.activeGameObject = _ctx.AvatarGo;
            SetOverlayStep(2, "Selecting avatar from the live editor hierarchy");
            _window.Repaint();

            yield return WaitForVisibleStep(_window, 2, "Selecting avatar from the live editor hierarchy");
            yield return WaitForSelectedAvatar(_window, _ctx.AvDesc, 30);

            var beforeHierarchy = _window.GetActionHierarchyContract();
            Assert.IsTrue(beforeHierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab),
                "Visible smoke automation should expose Add ASM-Lite Prefab as the primary action before installation.");

            SetOverlayStep(3, "Executing Add ASM-Lite Prefab through the rendered primary action");
            _window.QueueVisibleAutomationAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.AddPrefab);
            _window.Repaint();

            yield return WaitForVisibleStep(_window, 3, "Executing Add ASM-Lite Prefab through the rendered primary action");
            yield return WaitForComponent(_window, _ctx.AvDesc, 120);

            var component = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            Assert.IsNotNull(component,
                "Visible smoke automation should add the ASM-Lite prefab through the rendered primary action path.");

            SetOverlayStep(4, "Verifying the visible UI refreshed to Rebuild after installation");
            _window.Repaint();

            yield return WaitForVisibleStep(_window, 4, "Verifying the visible UI refreshed to Rebuild after installation");

            var afterHierarchy = _window.GetActionHierarchyContract();
            Assert.IsTrue(afterHierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild),
                "Visible smoke automation should refresh the rendered primary action to Rebuild after installation.");

            SetOverlayStep(
                5,
                "Visible smoke test completed successfully",
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Success);
            _window.Repaint();

            yield return WaitForVisibleStep(
                _window,
                5,
                "Visible smoke test completed successfully",
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Success);

            _window.ShowVisibleAutomationCompletionReview();
            _window.Repaint();

            yield return WaitForCompletionReviewToBeVisibleLocal();
            WriteCompletionReviewAcknowledgement(_window);
            yield return WaitForCompletionReviewToBeAcceptedLocal();
            _window.ClearVisibleAutomationOverlay();
        }

        private void SetOverlayStep(
            int stepIndex,
            string step,
            ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState state = ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Running)
        {
            _window.SetVisibleAutomationOverlayStatus(
                OverlayTitle,
                step,
                stepIndex,
                VisibleSmokeTotalSteps,
                state,
                presentationMode: true,
                checklistItems: VisibleSmokeChecklist);
        }

        private static bool CanRenderVisibleEditorWindow()
        {
            return !Application.isBatchMode && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        }

        private static IEnumerator WaitForVisibleStep(
            ASMLite.Editor.ASMLiteWindow window,
            int expectedStepIndex,
            string expectedStep,
            ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState expectedState = ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Running)
        {
            float stepDelaySeconds = GetConfiguredStepDelaySeconds();
            string expectedSuffix = NormalizeOverlayStep(expectedStep);
            double deadline = EditorApplication.timeSinceStartup + stepDelaySeconds + 1.5d;

            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (window == null)
                    Assert.Fail("Visible smoke automation should keep the ASM-Lite editor window alive while stepping through the overlay flow.");

                window.Repaint();

                string overlayStep = GetOverlayStep(window);
                if (string.Equals(NormalizeOverlayStep(overlayStep), expectedSuffix, System.StringComparison.Ordinal)
                    && GetOverlayStepIndex(window) == expectedStepIndex
                    && GetOverlayTotalSteps(window) == VisibleSmokeTotalSteps
                    && GetOverlayState(window) == expectedState
                    && GetOverlayPresentationMode(window))
                {
                    break;
                }

                yield return null;
            }

            Assert.AreEqual(expectedSuffix, NormalizeOverlayStep(GetOverlayStep(window)),
                $"Visible smoke automation overlay should show step '{expectedStep}'.");
            Assert.AreEqual(expectedStepIndex, GetOverlayStepIndex(window),
                $"Visible smoke automation overlay should report step index {expectedStepIndex}.");
            Assert.AreEqual(VisibleSmokeTotalSteps, GetOverlayTotalSteps(window),
                $"Visible smoke automation overlay should report {VisibleSmokeTotalSteps} total steps.");
            Assert.AreEqual(expectedState, GetOverlayState(window),
                $"Visible smoke automation overlay should report state '{expectedState}'.");
            Assert.IsTrue(GetOverlayPresentationMode(window),
                "Visible smoke automation overlay should remain in presentation mode.");

            var checklistItems = GetChecklistItems(window);
            var checklistStates = GetChecklistStates(window);
            Assert.AreEqual(VisibleSmokeChecklist.Length, checklistItems.Length,
                "Visible smoke automation checklist should include the full visible smoke step list.");
            CollectionAssert.AreEqual(VisibleSmokeChecklist, checklistItems,
                "Visible smoke automation checklist should preserve the expected step labels.");
            Assert.AreEqual(VisibleSmokeChecklist.Length, checklistStates.Length,
                "Visible smoke automation checklist should track one state per visible smoke step.");

            for (int i = 0; i < checklistStates.Length; i++)
            {
                var expectedChecklistState = i < expectedStepIndex - 1
                    ? ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Completed
                    : i == expectedStepIndex - 1
                        ? expectedState switch
                        {
                            ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Success => ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Completed,
                            ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Failure => ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Failed,
                            ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState.Warning => ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Warning,
                            _ => ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Active,
                        }
                        : ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState.Pending;

                Assert.AreEqual(expectedChecklistState, checklistStates[i],
                    $"Visible smoke automation checklist item {i + 1} should show '{expectedChecklistState}' at step {expectedStepIndex}.");
            }

            AssertOverlayHostSnapshot(window, expectCompletionReviewWindow: false);

            double waitUntil = EditorApplication.timeSinceStartup + stepDelaySeconds;
            while (EditorApplication.timeSinceStartup < waitUntil)
            {
                if (window != null)
                    window.Repaint();

                yield return null;
            }
        }

        private static void AssertOverlayHostSnapshot(
            ASMLite.Editor.ASMLiteWindow window,
            bool expectCompletionReviewWindow)
        {
            var snapshot = window.GetVisibleAutomationOverlayHostSnapshotForTesting();
            if (snapshot.HostKind == ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayHostKind.ExternalPythonProcess)
            {
                Assert.IsFalse(snapshot.HasStatusWindow,
                    "Visible smoke automation should disable the legacy detached status overlay window when the external Python overlay host is configured.");
                Assert.IsFalse(snapshot.HasChecklistWindow,
                    "Visible smoke automation should disable the legacy detached checklist overlay window when the external Python overlay host is configured.");
                Assert.IsFalse(snapshot.HasCompletionReviewWindow,
                    "Visible smoke automation should disable the legacy detached completion review overlay window when the external Python overlay host is configured.");
                Assert.IsTrue(snapshot.ExternalOverlayStateFileExists,
                    "Visible smoke automation should publish the external overlay state file while the Python overlay host is active.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.ExternalOverlayStatePath),
                    "Visible smoke automation should surface the external overlay state path when the Python overlay host is active.");
                Assert.IsFalse(string.IsNullOrWhiteSpace(snapshot.ExternalOverlayAckPath),
                    "Visible smoke automation should surface the external overlay acknowledgement path when the Python overlay host is active.");

                var stateDocument = window.GetVisibleAutomationExternalOverlayStateForTesting();
                Assert.IsNotNull(stateDocument,
                    "Visible smoke automation should serialize a readable external overlay state document while the Python overlay host is active.");
                Assert.AreEqual(expectCompletionReviewWindow, stateDocument.completionReviewVisible,
                    expectCompletionReviewWindow
                        ? "Visible smoke automation should publish the completion review state once the review phase starts."
                        : "Visible smoke automation should defer the completion review state until the review phase starts.");
                return;
            }

            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayHostKind.DetachedAuxiliaryWindows,
                snapshot.HostKind,
                "Visible smoke automation should use detached auxiliary overlay windows when no external Python host is configured.");
            Assert.IsTrue(snapshot.HasStatusWindow,
                "Visible smoke automation should keep the detached status overlay window alive while steps are running.");
            Assert.IsTrue(snapshot.HasChecklistWindow,
                "Visible smoke automation should keep the detached checklist overlay window alive while steps are running.");
            Assert.AreEqual(expectCompletionReviewWindow, snapshot.HasCompletionReviewWindow,
                expectCompletionReviewWindow
                    ? "Visible smoke automation should surface the completion review through a detached auxiliary overlay window."
                    : "Visible smoke automation should defer the completion review overlay window until the review phase starts.");

            AssertRectHasArea(snapshot.ScreenBounds,
                "Visible smoke automation should resolve non-empty anchor bounds for the detached overlay windows.");
            AssertRectWithin(snapshot.StatusWindowRect, snapshot.ScreenBounds,
                "Visible smoke automation should anchor the detached status overlay within the resolved bounds.");
            AssertRectWithin(snapshot.ChecklistWindowRect, snapshot.ScreenBounds,
                "Visible smoke automation should anchor the detached checklist overlay within the resolved bounds.");

            if (expectCompletionReviewWindow)
            {
                AssertRectWithin(snapshot.CompletionReviewWindowRect, snapshot.ScreenBounds,
                    "Visible smoke automation should anchor the detached completion review overlay within the resolved bounds.");
            }
        }

        private void ConfigureExternalOverlayPaths()
        {
            _externalOverlayTempDir = Path.Combine(Path.GetTempPath(), "asmlite-visible-overlay-tests", Guid.NewGuid().ToString("N"));
            _externalOverlayStatePath = Path.Combine(_externalOverlayTempDir, "overlay-state.json");
            _externalOverlayAckPath = Path.Combine(_externalOverlayTempDir, "overlay-ack.json");
        }

        private void CleanupExternalOverlayPaths()
        {
            if (string.IsNullOrWhiteSpace(_externalOverlayTempDir) || !Directory.Exists(_externalOverlayTempDir))
                return;

            Directory.Delete(_externalOverlayTempDir, recursive: true);
        }

        private void WriteCompletionReviewAcknowledgement(ASMLite.Editor.ASMLiteWindow window)
        {
            var stateDocument = window.GetVisibleAutomationExternalOverlayStateForTesting();
            Assert.IsNotNull(stateDocument,
                "Visible smoke automation should publish an external state document before a completion review acknowledgement is written.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(stateDocument.sessionId),
                "Visible smoke automation should publish an external overlay session id before a completion review acknowledgement is written.");
            Assert.Greater(stateDocument.completionReviewRequestId, 0,
                "Visible smoke automation should publish a positive completion review request id before a completion review acknowledgement is written.");

            Directory.CreateDirectory(Path.GetDirectoryName(_externalOverlayAckPath));
            var acknowledgement = new ASMLite.Editor.ASMLiteWindow.VisibleAutomationExternalOverlayAckDocument
            {
                sessionId = stateDocument.sessionId,
                completionReviewRequestId = stateDocument.completionReviewRequestId,
                acknowledged = true,
                acknowledgedUtcTicks = DateTime.UtcNow.Ticks,
            };

            File.WriteAllText(_externalOverlayAckPath, JsonUtility.ToJson(acknowledgement, true));
        }

        private static void AssertRectHasArea(Rect rect, string message)
        {
            Assert.Greater(rect.width, 0f, message + " Expected a positive width.");
            Assert.Greater(rect.height, 0f, message + " Expected a positive height.");
        }

        private static void AssertRectWithin(Rect rect, Rect bounds, string message)
        {
            AssertRectHasArea(rect, message);
            AssertRectHasArea(bounds, message + " Bounds were empty.");
            Assert.GreaterOrEqual(rect.xMin, bounds.xMin - 0.5f, message + " Expected xMin to stay within bounds.");
            Assert.GreaterOrEqual(rect.yMin, bounds.yMin - 0.5f, message + " Expected yMin to stay within bounds.");
            Assert.LessOrEqual(rect.xMax, bounds.xMax + 0.5f, message + " Expected xMax to stay within bounds.");
            Assert.LessOrEqual(rect.yMax, bounds.yMax + 0.5f, message + " Expected yMax to stay within bounds.");
        }

        private static int GetOverlayStepIndex(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<int>(window, "_visibleAutomationOverlayStepIndex");
        }

        private static string[] GetChecklistItems(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<string[]>(window, "_visibleAutomationChecklistItems") ?? System.Array.Empty<string>();
        }

        private static ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState[] GetChecklistStates(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState[]>(window, "_visibleAutomationChecklistStates")
                ?? System.Array.Empty<ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState>();
        }

        private static int GetOverlayTotalSteps(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<int>(window, "_visibleAutomationOverlayTotalSteps");
        }

        private static ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState GetOverlayState(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<ASMLite.Editor.ASMLiteWindow.VisibleAutomationOverlayState>(window, "_visibleAutomationOverlayState");
        }

        private static bool GetOverlayPresentationMode(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<bool>(window, "_visibleAutomationOverlayPresentationMode");
        }

        private static bool GetCompletionReviewVisible(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<bool>(window, "_visibleAutomationCompletionReviewVisible");
        }

        private static bool GetCompletionReviewAcknowledged(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<bool>(window, "_visibleAutomationCompletionReviewAcknowledged");
        }

        private static string GetCompletionReviewTitle(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<string>(window, "_visibleAutomationCompletionReviewTitle") ?? string.Empty;
        }

        private static string GetCompletionReviewMessage(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<string>(window, "_visibleAutomationCompletionReviewMessage") ?? string.Empty;
        }

        private static string GetOverlayStep(ASMLite.Editor.ASMLiteWindow window)
        {
            return GetPrivateField<string>(window, "_visibleAutomationOverlayStep") ?? string.Empty;
        }

        private static string NormalizeOverlayStep(string value)
        {
            return Regex.Replace(value ?? string.Empty, "\\s+", " ").Trim();
        }

        private static float GetConfiguredStepDelaySeconds()
        {
            string rawValue = System.Environment.GetEnvironmentVariable(StepDelayEnvVarName);
            if (string.IsNullOrWhiteSpace(rawValue))
                return DefaultStepDelaySeconds;

            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue))
            {
                Debug.LogWarning($"[ASM-Lite] Visible smoke step delay env '{StepDelayEnvVarName}' had invalid value '{rawValue}'. Falling back to {DefaultStepDelaySeconds.ToString("0.0", CultureInfo.InvariantCulture)}s.");
                return DefaultStepDelaySeconds;
            }

            if (parsedValue < 0f)
            {
                Debug.LogWarning($"[ASM-Lite] Visible smoke step delay env '{StepDelayEnvVarName}' cannot be negative. Falling back to {DefaultStepDelaySeconds.ToString("0.0", CultureInfo.InvariantCulture)}s.");
                return DefaultStepDelaySeconds;
            }

            return parsedValue;
        }

        private static IEnumerator WaitForSelectedAvatar(ASMLite.Editor.ASMLiteWindow window, VRCAvatarDescriptor expected, int maxFrames)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (ReferenceEquals(GetPrivateField<VRCAvatarDescriptor>(window, "_selectedAvatar"), expected))
                    yield break;

                window.Repaint();
                yield return null;
            }

            Assert.AreSame(expected, GetPrivateField<VRCAvatarDescriptor>(window, "_selectedAvatar"),
                "Visible smoke automation should synchronize the selected avatar through the live editor selection flow.");
        }

        private static IEnumerator WaitForComponent(ASMLite.Editor.ASMLiteWindow window, VRCAvatarDescriptor avatar, int maxFrames)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (avatar != null && avatar.GetComponentInChildren<ASMLiteComponent>(true) != null)
                    yield break;

                if (window != null)
                    window.Repaint();

                yield return null;
            }

            Assert.IsNotNull(avatar != null ? avatar.GetComponentInChildren<ASMLiteComponent>(true) : null,
                "Visible smoke automation should complete the rendered Add ASM-Lite Prefab action within the allotted editor frames.");
        }

        private static bool InvokeShouldAnimateVisibleAutomationOverlay(ASMLite.Editor.ASMLiteWindow window)
        {
            var method = typeof(ASMLite.Editor.ASMLiteWindow).GetMethod(
                "ShouldAnimateVisibleAutomationOverlay",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method,
                "Expected private method 'ShouldAnimateVisibleAutomationOverlay' on ASMLiteWindow.");
            return (bool)method.Invoke(window, null);
        }

        private static ChecklistPaletteSnapshot InvokeChecklistItemPalette(
            ASMLite.Editor.ASMLiteWindow.VisibleAutomationChecklistItemState state,
            double stateAgeSeconds,
            double now,
            bool presentationMode)
        {
            var method = typeof(ASMLite.Editor.ASMLiteWindow).GetMethod(
                "GetVisibleAutomationChecklistItemPalette",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method,
                "Expected private method 'GetVisibleAutomationChecklistItemPalette' on ASMLiteWindow.");

            object[] args =
            {
                state,
                stateAgeSeconds,
                now,
                presentationMode,
                default(Color),
                default(Color),
                default(Color),
                default(Color),
                default(Color),
                default(Color),
                default(Color),
            };

            method.Invoke(null, args);
            return new ChecklistPaletteSnapshot(
                (Color)args[4],
                (Color)args[5],
                (Color)args[6],
                (Color)args[7],
                (Color)args[8],
                (Color)args[9],
                (Color)args[10]);
        }

        private static void AssertPaletteEqual(ChecklistPaletteSnapshot expected, ChecklistPaletteSnapshot actual, string message)
        {
            Assert.IsTrue(expected.Equals(actual), message + $" Expected {expected} but got {actual}.");
        }

        private static void AssertPaletteNotEqual(ChecklistPaletteSnapshot expected, ChecklistPaletteSnapshot actual, string message)
        {
            Assert.IsFalse(expected.Equals(actual), message + $" Both snapshots were {actual}.");
        }

        private readonly struct ChecklistPaletteSnapshot : IEquatable<ChecklistPaletteSnapshot>
        {
            private const float Tolerance = 0.0001f;

            internal ChecklistPaletteSnapshot(
                Color backgroundColor,
                Color borderColor,
                Color accentColor,
                Color textColor,
                Color badgeColor,
                Color badgeTextColor,
                Color glyphColor)
            {
                BackgroundColor = backgroundColor;
                BorderColor = borderColor;
                AccentColor = accentColor;
                TextColor = textColor;
                BadgeColor = badgeColor;
                BadgeTextColor = badgeTextColor;
                GlyphColor = glyphColor;
            }

            internal Color BackgroundColor { get; }
            internal Color BorderColor { get; }
            internal Color AccentColor { get; }
            internal Color TextColor { get; }
            internal Color BadgeColor { get; }
            internal Color BadgeTextColor { get; }
            internal Color GlyphColor { get; }

            public bool Equals(ChecklistPaletteSnapshot other)
            {
                return ColorsApproximatelyEqual(BackgroundColor, other.BackgroundColor)
                    && ColorsApproximatelyEqual(BorderColor, other.BorderColor)
                    && ColorsApproximatelyEqual(AccentColor, other.AccentColor)
                    && ColorsApproximatelyEqual(TextColor, other.TextColor)
                    && ColorsApproximatelyEqual(BadgeColor, other.BadgeColor)
                    && ColorsApproximatelyEqual(BadgeTextColor, other.BadgeTextColor)
                    && ColorsApproximatelyEqual(GlyphColor, other.GlyphColor);
            }

            public override bool Equals(object obj)
            {
                return obj is ChecklistPaletteSnapshot other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(
                    BackgroundColor,
                    BorderColor,
                    AccentColor,
                    TextColor,
                    BadgeColor,
                    BadgeTextColor,
                    GlyphColor);
            }

            public override string ToString()
            {
                return $"bg={BackgroundColor}, border={BorderColor}, accent={AccentColor}, text={TextColor}, badge={BadgeColor}, badgeText={BadgeTextColor}, glyph={GlyphColor}";
            }

            private static bool ColorsApproximatelyEqual(Color left, Color right)
            {
                return Mathf.Abs(left.r - right.r) <= Tolerance
                    && Mathf.Abs(left.g - right.g) <= Tolerance
                    && Mathf.Abs(left.b - right.b) <= Tolerance
                    && Mathf.Abs(left.a - right.a) <= Tolerance;
            }
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected private field '{fieldName}' on {instance.GetType().FullName}.");
            return (T)field.GetValue(instance);
        }
    }
}
