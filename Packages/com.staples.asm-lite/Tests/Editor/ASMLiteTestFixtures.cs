using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.SceneManagement;
using NUnit.Framework;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    internal readonly struct SerializedObjectReferenceReadResult
    {
        internal SerializedObjectReferenceReadResult(string propertyPath, UnityEngine.Object reference, bool propertyExists)
        {
            PropertyPath = propertyPath ?? string.Empty;
            Reference = reference;
            PropertyExists = propertyExists;
            AssetPath = reference != null ? AssetDatabase.GetAssetPath(reference) ?? string.Empty : string.Empty;
        }

        internal string PropertyPath { get; }
        internal UnityEngine.Object Reference { get; }
        internal bool PropertyExists { get; }
        internal string AssetPath { get; }
        internal bool HasReference => Reference != null;

        internal static SerializedObjectReferenceReadResult Missing(string propertyPath)
        {
            return new SerializedObjectReferenceReadResult(propertyPath, null, propertyExists: false);
        }
    }

    /// <summary>
     /// Shared test fixtures for ASMLite integration tests.
     /// Call CreateTestAvatar() to get a fully-wired avatar, TearDownTestAvatar() to clean up.
     /// </summary>
    public static class ASMLiteTestFixtures
    {
        private const string TempDir = "Assets/ASMLiteTests_Temp";
        private static readonly List<ASMLiteGenerationWiringFailure> s_recordedGenerationWiringFailures = new List<ASMLiteGenerationWiringFailure>();
        private static readonly object s_recordedGenerationWiringFailuresLock = new object();

        public static AsmLiteTestContext CreateTestAvatar()
        {
            // Create temp directory (guard against already existing)
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");

            // Create AnimatorController
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(
                TempDir + "/TestFX.controller");

            // Create VRCExpressionParameters
            var paramsAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            paramsAsset.parameters = new VRCExpressionParameters.Parameter[0];
            AssetDatabase.CreateAsset(paramsAsset, TempDir + "/TestParams.asset");

            // Create VRCExpressionsMenu
            var menuAsset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menuAsset.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menuAsset, TempDir + "/TestMenu.asset");

            AssetDatabase.SaveAssets();

            // Create avatar GameObject
            var avatarGo = new GameObject("TestAvatar");
            var avDesc = avatarGo.AddComponent<VRCAvatarDescriptor>();

            // Wire FX layer -- resize to at least 5 slots rather than indexing blindly
            var layers = avDesc.baseAnimationLayers;
            if (layers == null || layers.Length < 5)
            {
                var newLayers = new VRCAvatarDescriptor.CustomAnimLayer[5];
                if (layers != null)
                    for (int i = 0; i < layers.Length; i++)
                        newLayers[i] = layers[i];
                // Slot 4 is FX
                newLayers[4] = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    isEnabled = true,
                    animatorController = ctrl
                };
                avDesc.baseAnimationLayers = newLayers;
            }
            else
            {
                layers[4].animatorController = ctrl;
                layers[4].isDefault = false;
                layers[4].isEnabled = true;
                avDesc.baseAnimationLayers = layers;
            }

            avDesc.expressionParameters = paramsAsset;
            avDesc.expressionsMenu = menuAsset;

            // Add ASMLiteComponent as child
            var compGo = new GameObject("ASMLite");
            compGo.transform.SetParent(avatarGo.transform);
            var comp = compGo.AddComponent<ASMLiteComponent>();

            return new AsmLiteTestContext
            {
                AvatarGo = avatarGo,
                AvDesc = avDesc,
                Comp = comp,
                Ctrl = ctrl,
                ParamsAsset = paramsAsset,
                MenuAsset = menuAsset
            };
        }

        public static void AddExpressionParam(
            AsmLiteTestContext ctx,
            string name,
            VRCExpressionParameters.ValueType valueType,
            float defaultValue = 0f,
            bool saved = true,
            bool networkSynced = true)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRCExpressionParameters.Parameter[0];
            var updated = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = valueType,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = networkSynced,
            };

            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        public static void SetExpressionParams(AsmLiteTestContext ctx, params VRCExpressionParameters.Parameter[] parameters)
        {
            Assert.IsNotNull(ctx, "SetExpressionParams requires a valid test context.");

            if (ctx.ParamsAsset == null && ctx.AvDesc != null && ctx.AvDesc.expressionParameters != null)
                ctx.ParamsAsset = ctx.AvDesc.expressionParameters;

            if (ctx.ParamsAsset == null)
            {
                var fallbackParamsAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                fallbackParamsAsset.parameters = new VRCExpressionParameters.Parameter[0];
                ctx.ParamsAsset = fallbackParamsAsset;

                if (ctx.AvDesc != null)
                {
                    ctx.AvDesc.expressionParameters = fallbackParamsAsset;
                    EditorUtility.SetDirty(ctx.AvDesc);
                }
            }

            Assert.IsTrue(ctx.ParamsAsset != null,
                "SetExpressionParams requires a live ParamsAsset reference. Ensure CreateTestAvatar() completed and fixture assets were not torn down before this helper call.");

            ctx.ParamsAsset.parameters = parameters ?? new VRCExpressionParameters.Parameter[0];
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        public static GameObject CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name ?? "Child");
            if (parent != null)
                child.transform.SetParent(parent.transform);
            return child;
        }

        internal static MonoBehaviour[] FindLiveVrcFuryComponents(GameObject root)
        {
            if (root == null)
                return Array.Empty<MonoBehaviour>();

            var behaviors = root.GetComponents<MonoBehaviour>();
            return behaviors
                .Where(behavior =>
                {
                    var type = behavior != null ? behavior.GetType() : null;
                    return type != null && string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal);
                })
                .ToArray();
        }

        internal static MonoBehaviour FindLiveVrcFuryComponent(GameObject root)
        {
            return FindLiveVrcFuryComponents(root).FirstOrDefault();
        }

        internal static SerializedObjectReferenceReadResult ReadSerializedObjectReference(MonoBehaviour vfComponent, string propertyPath)
        {
            if (vfComponent == null || string.IsNullOrWhiteSpace(propertyPath))
                return SerializedObjectReferenceReadResult.Missing(propertyPath);

            var serializedVf = new SerializedObject(vfComponent);
            serializedVf.Update();

            var property = serializedVf.FindProperty(propertyPath);
            if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                return SerializedObjectReferenceReadResult.Missing(propertyPath);

            return new SerializedObjectReferenceReadResult(propertyPath, property.objectReferenceValue, propertyExists: true);
        }

        internal static SerializedObjectReferenceReadResult ReadSerializedObjectReferenceFromAnyPath(MonoBehaviour vfComponent, params string[] propertyPaths)
        {
            if (propertyPaths == null || propertyPaths.Length == 0)
                return SerializedObjectReferenceReadResult.Missing(string.Empty);

            var firstExistingProperty = SerializedObjectReferenceReadResult.Missing(string.Empty);
            for (int i = 0; i < propertyPaths.Length; i++)
            {
                var referenceRead = ReadSerializedObjectReference(vfComponent, propertyPaths[i]);
                if (referenceRead.PropertyExists && !firstExistingProperty.PropertyExists)
                    firstExistingProperty = referenceRead;

                if (referenceRead.HasReference)
                    return referenceRead;
            }

            return firstExistingProperty;
        }

        internal static string[] ReadSerializedStringArray(MonoBehaviour vfComponent, string arrayPath)
        {
            if (vfComponent == null || string.IsNullOrWhiteSpace(arrayPath))
                return Array.Empty<string>();

            var serializedVf = new SerializedObject(vfComponent);
            serializedVf.Update();

            var property = serializedVf.FindProperty(arrayPath);
            if (property == null || !property.isArray)
                return Array.Empty<string>();

            var values = new string[property.arraySize];
            for (int i = 0; i < property.arraySize; i++)
            {
                values[i] = property.GetArrayElementAtIndex(i)?.stringValue ?? string.Empty;
            }

            return values;
        }

        internal static VF.Model.VRCFury EnsureLiveFullControllerPayload(ASMLiteComponent component)
        {
            if (component == null)
                return null;

            var vf = component.GetComponent<VF.Model.VRCFury>();
            if (vf == null)
                vf = component.gameObject.AddComponent<VF.Model.VRCFury>();

            vf.content = new VF.Model.Feature.FullController
            {
                menus = new[]
                {
                    new VF.Model.Feature.MenuEntry()
                }
            };

            return vf;
        }

        internal static string ReadSerializedMenuPrefix(Component vf)
        {
            Assert.IsNotNull(vf,
                "Expected a live VF.Model.VRCFury component before reading the serialized FullController menu prefix.");

            var serializedVf = new SerializedObject(vf);
            serializedVf.Update();

            var prefixProperty = serializedVf.FindProperty("content.menus.Array.data[0].prefix");
            Assert.IsNotNull(prefixProperty,
                "Expected serialized FullController menu prefix field at content.menus.Array.data[0].prefix.");

            return prefixProperty.stringValue;
        }

        internal static string ReadSerializedMenuPrefix(VF.Model.VRCFury vf)
        {
            return ReadSerializedMenuPrefix((Component)vf);
        }

        public static void TearDownTestAvatar(GameObject avatarGo)
        {
            AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.Refresh();
            if (avatarGo != null)
                UnityEngine.Object.DestroyImmediate(avatarGo);
        }

        public static void ResetGeneratedExprParams()
        {
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (generatedExpr == null)
                return;
            generatedExpr.parameters = new VRCExpressionParameters.Parameter[0];
            EditorUtility.SetDirty(generatedExpr);
            AssetDatabase.SaveAssets();
        }

        internal static void ClearRecordedGenerationWiringFailures()
        {
            lock (s_recordedGenerationWiringFailuresLock)
            {
                s_recordedGenerationWiringFailures.Clear();
            }
        }

        internal static ASMLiteGenerationWiringFailure[] GetRecordedGenerationWiringFailures()
        {
            lock (s_recordedGenerationWiringFailuresLock)
            {
                return s_recordedGenerationWiringFailures.ToArray();
            }
        }

        internal static void RecordBuildDiagnosticFailure(
            string suiteName,
            string testName,
            ASMLiteBuildDiagnosticResult diagnostic,
            string resultsFile = null)
        {
            if (diagnostic == null || diagnostic.Success)
                return;

            RecordGenerationWiringFailure(
                ASMLiteGenerationWiringSummaryWriter.CreateDiagnosticFailure(
                    suiteName,
                    testName,
                    diagnostic,
                    resultsFile));
        }

        internal static void RecordDeterminismFailure(
            string suiteName,
            string testName,
            string contextPath,
            string message,
            string resultsFile = null)
        {
            RecordGenerationWiringFailure(
                ASMLiteGenerationWiringSummaryWriter.CreateDeterminismFailure(
                    suiteName,
                    testName,
                    contextPath,
                    message,
                    resultsFile));
        }

        private static void RecordGenerationWiringFailure(ASMLiteGenerationWiringFailure failure)
        {
            if (failure == null)
                return;

            lock (s_recordedGenerationWiringFailuresLock)
            {
                s_recordedGenerationWiringFailures.Add(failure);
            }
        }
    }

    /// <summary>
    /// Holds all objects created by CreateTestAvatar().
    /// </summary>
    public class AsmLiteTestContext
    {
        public GameObject AvatarGo;
        public VRCAvatarDescriptor AvDesc;
        public ASMLiteComponent Comp;
        public AnimatorController Ctrl;
        public VRCExpressionParameters ParamsAsset;
        public VRCExpressionsMenu MenuAsset;
    }

    public enum AsmLiteVisibleAutomationMode
    {
        Editor = 0,
        PlayMode = 1,
        LaunchUnity = 2,
    }

    internal enum AsmLiteVisibleAutomationStage
    {
        OpeningWindow = 0,
        SelectingAvatar = 1,
        AddingPrefab = 2,
        AttachingDefaultPayload = 3,
        VerifyingRebuild = 4,
        WaitingForPlayMode = 5,
        InspectingPlayMode = 6,
        AwaitingAcceptance = 7,
        ExitingPlayModeAfterAcceptance = 8,
        VerifyingInteractivity = 9,
    }

    [Serializable]
    internal sealed class AsmLiteVisibleAutomationCommandLineConfiguration
    {
        public string resultsPath;
        public string selector;
        public int mode;
        public int stage;
        public long startedUtcTicks;
        public string externalOverlayStatePath;
        public string externalOverlayAckPath;
        public string scenePath;
        public string avatarName;
        public bool hasStepDelaySeconds;
        public float stepDelaySeconds;
    }

    [InitializeOnLoad]
    public static class ASMLiteVisibleAutomationCommandLine
    {
        private const string ResultsPathArg = "-asmliteVisibleAutomationResultsPath";
        private const string ModeArg = "-asmliteVisibleAutomationMode";
        private const string SelectorArg = "-asmliteVisibleAutomationSelector";
        private const string ExternalOverlayStatePathArg = "-asmliteVisibleAutomationExternalOverlayStatePath";
        private const string ExternalOverlayAckPathArg = "-asmliteVisibleAutomationExternalOverlayAckPath";
        private const string StepDelaySecondsArg = "-asmliteVisibleAutomationStepDelaySeconds";
        private const string SessionStateKey = "ASMLite.VisibleAutomation.CommandLineConfiguration";
        private const string DefaultVisibleAutomationScenePath = "Assets/Click ME.unity";
        private const string DefaultVisibleAutomationAvatarName = "Oct25_Dress";
        private const string EditorOverlayTitle = "ASM-Lite visible smoke test";
        private const string PlayModeOverlayTitle = "ASM-Lite visible playmode smoke";
        private const string LaunchUnityOverlayTitle = "ASM-Lite visible launch UAT";
        private const float DefaultStepDelaySeconds = 1.0f;
        private const string StepDelayEnvVarName = "ASMLITE_VISIBLE_SMOKE_STEP_DELAY_SECONDS";
        private const string EditorFixtureName = "ASMLiteVisibleEditorSmokeTests";
        private const string EditorCaseName = "VisibleWindow_AddPrefab_PrimaryActionExecutesThroughRenderedWindow";
        private const string LaunchUnityCaseName = "VisibleWindow_LaunchUnity_LoadsClickMe_SelectsOct25Dress_AndWaitsForAcceptance";
        private const string PlayModeFixtureName = "ASMLiteVisiblePlayModeAutomation";
        private const string PlayModeCaseName = "VisibleWindow_AddPrefab_EntersPlayMode_AndWaitsForAcceptance";

        private static readonly string[] EditorChecklist =
        {
            "Open the ASM-Lite editor window",
            "Select the live avatar from the hierarchy",
            "Execute Add ASM-Lite Prefab from the visible primary action",
            "Attach the default ASM-Lite payload to the avatar",
            "Verify the rendered primary action updates to Rebuild",
            "Confirm the visible smoke run completed successfully",
        };

        private static readonly string[] LaunchUnityChecklist =
        {
            "Open the ASM-Lite editor window",
            "Load scene Assets/Click ME.unity and locate Oct25_Dress",
            "Select Oct25_Dress from the hierarchy",
            "Verify the ASM-Lite editor window remains focused and interactable",
            "Confirm the visible launch UAT completed successfully",
        };

        private static readonly string[] PlayModeChecklist =
        {
            "Open the ASM-Lite editor window",
            "Select the live avatar from the hierarchy",
            "Execute Add ASM-Lite Prefab from the visible primary action",
            "Attach the default ASM-Lite payload to the avatar",
            "Verify the rendered primary action updates to Rebuild",
            "Enter Play Mode and confirm the runtime component stays live",
            "Confirm the visible playmode smoke run completed successfully",
        };

        private static VisibleAutomationRunner s_runner;

        static ASMLiteVisibleAutomationCommandLine()
        {
            EditorApplication.delayCall += ResumePendingRunIfNeeded;
        }

        public static void RunFromCommandLine()
        {
            try
            {
                Start(ParseConfiguration(Environment.GetCommandLineArgs()), resumedFromSessionState: false);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void ResumePendingRunIfNeeded()
        {
            EditorApplication.delayCall -= ResumePendingRunIfNeeded;
            if (s_runner != null)
                return;

            var configuration = LoadPersistedConfiguration();
            if (configuration == null || string.IsNullOrWhiteSpace(configuration.resultsPath))
                return;

            Start(configuration, resumedFromSessionState: true);
        }

        private static void Start(AsmLiteVisibleAutomationCommandLineConfiguration configuration, bool resumedFromSessionState)
        {
            s_runner?.Dispose();
            s_runner = new VisibleAutomationRunner(configuration, resumedFromSessionState, ClearRunner);
            s_runner.Start();
        }

        private static void ClearRunner()
        {
            s_runner = null;
        }

        internal static AsmLiteVisibleAutomationCommandLineConfiguration ParseConfiguration(string[] commandLineArgs)
        {
            if (commandLineArgs == null)
                throw new ArgumentNullException(nameof(commandLineArgs));

            string selector = GetCommandLineValue(commandLineArgs, SelectorArg);
            string modeRaw = GetCommandLineValue(commandLineArgs, ModeArg);
            string resultsPath = GetCommandLineValue(commandLineArgs, ResultsPathArg);
            string externalOverlayStatePath = GetCommandLineValue(commandLineArgs, ExternalOverlayStatePathArg);
            string externalOverlayAckPath = GetCommandLineValue(commandLineArgs, ExternalOverlayAckPathArg);
            string stepDelaySecondsRaw = GetCommandLineValue(commandLineArgs, StepDelaySecondsArg);

            if (string.IsNullOrWhiteSpace(resultsPath))
                throw new InvalidOperationException($"Missing required command-line argument '{ResultsPathArg}'.");

            var mode = ResolveModeSelector(string.IsNullOrWhiteSpace(modeRaw) ? selector : modeRaw);
            return new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = Path.GetFullPath(resultsPath.Trim()),
                selector = string.IsNullOrWhiteSpace(selector) ? string.Empty : selector.Trim(),
                mode = (int)mode,
                stage = (int)AsmLiteVisibleAutomationStage.OpeningWindow,
                startedUtcTicks = DateTime.UtcNow.Ticks,
                externalOverlayStatePath = string.IsNullOrWhiteSpace(externalOverlayStatePath)
                    ? string.Empty
                    : Path.GetFullPath(externalOverlayStatePath.Trim()),
                externalOverlayAckPath = string.IsNullOrWhiteSpace(externalOverlayAckPath)
                    ? string.Empty
                    : Path.GetFullPath(externalOverlayAckPath.Trim()),
                scenePath = DefaultVisibleAutomationScenePath,
                avatarName = DefaultVisibleAutomationAvatarName,
                hasStepDelaySeconds = true,
                stepDelaySeconds = ResolveConfiguredStepDelaySeconds(stepDelaySecondsRaw),
            };
        }

        internal static AsmLiteVisibleAutomationMode ResolveModeSelector(string selector)
        {
            if (string.IsNullOrWhiteSpace(selector))
                return AsmLiteVisibleAutomationMode.Editor;

            string normalized = selector.Trim();
            if (normalized.IndexOf("launch-unity", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("launchunity", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AsmLiteVisibleAutomationMode.LaunchUnity;
            }

            if (normalized.IndexOf("playmode", StringComparison.OrdinalIgnoreCase) >= 0
                || normalized.IndexOf("runtime", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return AsmLiteVisibleAutomationMode.PlayMode;
            }

            return AsmLiteVisibleAutomationMode.Editor;
        }

        internal static XmlDocument BuildResultDocument(
            AsmLiteVisibleAutomationCommandLineConfiguration configuration,
            string result,
            string failureMessage,
            string stackTrace,
            double durationSeconds,
            DateTimeOffset startedAtUtc,
            DateTimeOffset endedAtUtc)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));

            string normalizedResult = string.IsNullOrWhiteSpace(result) ? "Failed" : result.Trim();
            bool passed = string.Equals(normalizedResult, "Passed", StringComparison.OrdinalIgnoreCase);
            bool skipped = string.Equals(normalizedResult, "Skipped", StringComparison.OrdinalIgnoreCase);
            bool failed = !passed && !skipped;
            string fixtureName = GetFixtureName((AsmLiteVisibleAutomationMode)configuration.mode);
            string caseName = GetCaseName((AsmLiteVisibleAutomationMode)configuration.mode);
            string fullCaseName = $"ASMLite.Tests.Editor.{fixtureName}.{caseName}";
            string durationText = Math.Max(0d, durationSeconds).ToString("0.#######", CultureInfo.InvariantCulture);

            var document = new XmlDocument();
            document.AppendChild(document.CreateXmlDeclaration("1.0", "utf-8", null));

            XmlElement run = document.CreateElement("test-run");
            document.AppendChild(run);
            run.SetAttribute("id", "0");
            run.SetAttribute("testcasecount", "1");
            run.SetAttribute("result", normalizedResult);
            run.SetAttribute("total", "1");
            run.SetAttribute("passed", passed ? "1" : "0");
            run.SetAttribute("failed", failed ? "1" : "0");
            run.SetAttribute("inconclusive", "0");
            run.SetAttribute("skipped", skipped ? "1" : "0");
            run.SetAttribute("asserts", "0");
            run.SetAttribute("start-time", startedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture));
            run.SetAttribute("end-time", endedAtUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture));
            run.SetAttribute("duration", durationText);

            XmlElement assemblySuite = document.CreateElement("test-suite");
            run.AppendChild(assemblySuite);
            assemblySuite.SetAttribute("type", "Assembly");
            assemblySuite.SetAttribute("id", "1");
            assemblySuite.SetAttribute("name", "ASMLiteVisibleAutomation");
            assemblySuite.SetAttribute("fullname", "ASMLiteVisibleAutomation");
            assemblySuite.SetAttribute("result", normalizedResult);
            assemblySuite.SetAttribute("total", "1");
            assemblySuite.SetAttribute("passed", passed ? "1" : "0");
            assemblySuite.SetAttribute("failed", failed ? "1" : "0");
            assemblySuite.SetAttribute("inconclusive", "0");
            assemblySuite.SetAttribute("skipped", skipped ? "1" : "0");
            assemblySuite.SetAttribute("asserts", "0");
            assemblySuite.SetAttribute("duration", durationText);

            XmlElement fixtureSuite = document.CreateElement("test-suite");
            assemblySuite.AppendChild(fixtureSuite);
            fixtureSuite.SetAttribute("type", "TestFixture");
            fixtureSuite.SetAttribute("id", "2");
            fixtureSuite.SetAttribute("name", fixtureName);
            fixtureSuite.SetAttribute("fullname", $"ASMLite.Tests.Editor.{fixtureName}");
            fixtureSuite.SetAttribute("classname", $"ASMLite.Tests.Editor.{fixtureName}");
            fixtureSuite.SetAttribute("result", normalizedResult);
            fixtureSuite.SetAttribute("total", "1");
            fixtureSuite.SetAttribute("passed", passed ? "1" : "0");
            fixtureSuite.SetAttribute("failed", failed ? "1" : "0");
            fixtureSuite.SetAttribute("inconclusive", "0");
            fixtureSuite.SetAttribute("skipped", skipped ? "1" : "0");
            fixtureSuite.SetAttribute("asserts", "0");
            fixtureSuite.SetAttribute("duration", durationText);

            XmlElement testCase = document.CreateElement("test-case");
            fixtureSuite.AppendChild(testCase);
            testCase.SetAttribute("id", "3");
            testCase.SetAttribute("name", caseName);
            testCase.SetAttribute("fullname", fullCaseName);
            testCase.SetAttribute("classname", $"ASMLite.Tests.Editor.{fixtureName}");
            testCase.SetAttribute("methodname", caseName);
            testCase.SetAttribute("result", normalizedResult);
            testCase.SetAttribute("duration", durationText);
            testCase.SetAttribute("asserts", "0");

            if (failed)
            {
                XmlElement failure = document.CreateElement("failure");
                testCase.AppendChild(failure);

                XmlElement message = document.CreateElement("message");
                message.InnerText = string.IsNullOrWhiteSpace(failureMessage)
                    ? "Visible automation command-line harness failed."
                    : failureMessage.Trim();
                failure.AppendChild(message);

                XmlElement trace = document.CreateElement("stack-trace");
                trace.InnerText = string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace;
                failure.AppendChild(trace);
            }

            return document;
        }

        private static string GetCommandLineValue(string[] commandLineArgs, string key)
        {
            for (int i = 0; i < commandLineArgs.Length - 1; i++)
            {
                if (string.Equals(commandLineArgs[i], key, StringComparison.Ordinal))
                    return commandLineArgs[i + 1];
            }

            return string.Empty;
        }

        private static string GetFixtureName(AsmLiteVisibleAutomationMode mode)
        {
            return mode == AsmLiteVisibleAutomationMode.PlayMode
                ? PlayModeFixtureName
                : EditorFixtureName;
        }

        private static string GetCaseName(AsmLiteVisibleAutomationMode mode)
        {
            return mode switch
            {
                AsmLiteVisibleAutomationMode.PlayMode => PlayModeCaseName,
                AsmLiteVisibleAutomationMode.LaunchUnity => LaunchUnityCaseName,
                _ => EditorCaseName,
            };
        }

        private static AsmLiteVisibleAutomationCommandLineConfiguration LoadPersistedConfiguration()
        {
            string json = SessionState.GetString(SessionStateKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<AsmLiteVisibleAutomationCommandLineConfiguration>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Failed to restore visible automation command-line state: {ex.Message}");
                SessionState.EraseString(SessionStateKey);
                return null;
            }
        }

        private static void PersistConfiguration(AsmLiteVisibleAutomationCommandLineConfiguration configuration)
        {
            SessionState.SetString(SessionStateKey, JsonUtility.ToJson(configuration));
        }

        private static void ClearPersistedConfiguration()
        {
            SessionState.EraseString(SessionStateKey);
        }

        private sealed class VisibleAutomationRunner
        {
            private readonly Action _onDisposed;
            private AsmLiteVisibleAutomationCommandLineConfiguration _configuration;
            private ASMLiteWindow _window;
            private AsmLiteTestContext _context;
            private double _stageStartedAt;
            private bool _disposed;
            private bool _completionPendingAfterPlayModeExit;

            internal VisibleAutomationRunner(
                AsmLiteVisibleAutomationCommandLineConfiguration configuration,
                bool resumedFromSessionState,
                Action onDisposed)
            {
                _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
                _onDisposed = onDisposed;
                if (!resumedFromSessionState)
                    PersistConfiguration(_configuration);
            }

            internal void Start()
            {
                EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

                if ((AsmLiteVisibleAutomationStage)_configuration.stage == AsmLiteVisibleAutomationStage.OpeningWindow
                    && string.IsNullOrWhiteSpace(SessionState.GetString(SessionStateKey, string.Empty)))
                {
                    PersistConfiguration(_configuration);
                }

                if ((AsmLiteVisibleAutomationMode)_configuration.mode == AsmLiteVisibleAutomationMode.PlayMode
                    && EditorApplication.isPlaying)
                {
                    ResumePlayModeState();
                    return;
                }

                if ((AsmLiteVisibleAutomationStage)_configuration.stage == AsmLiteVisibleAutomationStage.OpeningWindow)
                {
                    BeginFreshRun();
                    return;
                }

                ResumeEditModeState();
            }

            internal void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                EditorApplication.update -= Tick;
                EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
                _onDisposed?.Invoke();
            }

            private void BeginFreshRun()
            {
                if (!CanRenderVisibleEditorWindow())
                {
                    FailAndExit("Visible automation requires a graphics-backed local Unity editor window.");
                    return;
                }

                if (!TryLoadConfiguredSceneAvatar(out _context, out string failureMessage))
                {
                    FailAndExit(failureMessage);
                    return;
                }

                Selection.activeGameObject = null;

                if ((AsmLiteVisibleAutomationMode)_configuration.mode != AsmLiteVisibleAutomationMode.LaunchUnity
                    && _context.Comp != null)
                {
                    UnityEngine.Object.DestroyImmediate(_context.Comp.gameObject);
                    _context.Comp = null;
                }

                EnsureWindow();
                if (_window == null)
                {
                    FailAndExit("Visible automation could not open the ASM-Lite editor window.");
                    return;
                }

                StartStage(AsmLiteVisibleAutomationStage.OpeningWindow);
            }

            private void ResumeEditModeState()
            {
                EnsureContextFromScene();
                EnsureWindow();
                ApplyStageVisuals((AsmLiteVisibleAutomationStage)_configuration.stage, resetStageTimer: true);
            }

            private void ResumePlayModeState()
            {
                EnsureContextFromScene();
                EnsureWindow();
                StartStage(AsmLiteVisibleAutomationStage.InspectingPlayMode);
            }

            private void Tick()
            {
                if (_disposed)
                    return;

                EnsureWindow();
                if (_window == null)
                {
                    FailAndExit("Visible automation lost the ASM-Lite editor window before the run completed.");
                    return;
                }

                switch ((AsmLiteVisibleAutomationStage)_configuration.stage)
                {
                    case AsmLiteVisibleAutomationStage.OpeningWindow:
                        TickOpeningWindow();
                        break;
                    case AsmLiteVisibleAutomationStage.SelectingAvatar:
                        TickSelectingAvatar();
                        break;
                    case AsmLiteVisibleAutomationStage.AddingPrefab:
                        TickAddingPrefab();
                        break;
                    case AsmLiteVisibleAutomationStage.AttachingDefaultPayload:
                        TickAttachingDefaultPayload();
                        break;
                    case AsmLiteVisibleAutomationStage.VerifyingRebuild:
                        TickVerifyingRebuild();
                        break;
                    case AsmLiteVisibleAutomationStage.WaitingForPlayMode:
                        if (EditorApplication.isPlaying)
                            StartStage(AsmLiteVisibleAutomationStage.InspectingPlayMode);
                        break;
                    case AsmLiteVisibleAutomationStage.InspectingPlayMode:
                        TickInspectingPlayMode();
                        break;
                    case AsmLiteVisibleAutomationStage.VerifyingInteractivity:
                        TickVerifyingInteractivity();
                        break;
                    case AsmLiteVisibleAutomationStage.AwaitingAcceptance:
                        TickAwaitingAcceptance();
                        break;
                    case AsmLiteVisibleAutomationStage.ExitingPlayModeAfterAcceptance:
                        if (!EditorApplication.isPlayingOrWillChangePlaymode)
                            CompleteSuccessAndExit();
                        break;
                }
            }

            private void TickOpeningWindow()
            {
                if (!EditorWindow.HasOpenInstances<ASMLiteWindow>())
                {
                    FailAndExit("Visible automation should keep the ASM-Lite editor window open on screen.");
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                Selection.activeGameObject = _context.AvatarGo;
                _window.SelectAvatarForAutomation(_context.AvDesc);
                StartStage(AsmLiteVisibleAutomationStage.SelectingAvatar);
            }

            private void TickSelectingAvatar()
            {
                if (_context?.AvDesc == null)
                {
                    FailAndExit("Visible automation lost the live avatar fixture before selection completed.");
                    return;
                }

                if (!ReferenceEquals(GetSelectedAvatar(), _context.AvDesc))
                {
                    Selection.activeGameObject = _context.AvatarGo;
                    _window.SelectAvatarForAutomation(_context.AvDesc);
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                if ((AsmLiteVisibleAutomationMode)_configuration.mode == AsmLiteVisibleAutomationMode.LaunchUnity)
                {
                    StartStage(AsmLiteVisibleAutomationStage.VerifyingInteractivity);
                    return;
                }

                var hierarchy = _window.GetActionHierarchyContract();
                if (!hierarchy.HasPrimaryAction(ASMLiteWindow.AsmLiteWindowAction.AddPrefab))
                {
                    FailAndExit("Visible automation expected Add ASM-Lite Prefab to be the visible primary action before installation.");
                    return;
                }

                _window.QueueVisibleAutomationAction(ASMLiteWindow.AsmLiteWindowAction.AddPrefab);
                StartStage(AsmLiteVisibleAutomationStage.AddingPrefab);
            }

            private void TickAddingPrefab()
            {
                var component = GetLiveComponent();
                if (component == null)
                {
                    if (HasTimedOut(30d))
                        FailAndExit("Visible automation did not add the ASM-Lite prefab within the allotted editor time.");
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                EnsureOverlayHostSnapshot(expectCompletionReviewWindow: false);
                StartStage(AsmLiteVisibleAutomationStage.AttachingDefaultPayload);
            }

            private void TickAttachingDefaultPayload()
            {
                if (_context?.AvDesc?.expressionsMenu == null)
                {
                    if (HasTimedOut(15d))
                        FailAndExit("Visible automation expected the avatar expressions menu to remain available while attaching the default ASM-Lite payload.");
                    return;
                }

                if (CountSettingsManagerControls(_context.AvDesc.expressionsMenu) != 1)
                {
                    if (HasTimedOut(15d))
                        FailAndExit("Visible automation expected the default ASM-Lite payload to attach exactly one Settings Manager control to the avatar menu after installation.");
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                StartStage(AsmLiteVisibleAutomationStage.VerifyingRebuild);
            }

            private void TickVerifyingRebuild()
            {
                var hierarchy = _window.GetActionHierarchyContract();
                if (!hierarchy.HasPrimaryAction(ASMLiteWindow.AsmLiteWindowAction.Rebuild))
                {
                    if (HasTimedOut(15d))
                        FailAndExit("Visible automation expected the rendered primary action to update to Rebuild after installation.");
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                if ((AsmLiteVisibleAutomationMode)_configuration.mode == AsmLiteVisibleAutomationMode.PlayMode)
                {
                    StartStage(AsmLiteVisibleAutomationStage.WaitingForPlayMode);
                    EditorApplication.isPlaying = true;
                    return;
                }

                ShowCompletionReviewForCurrentMode();
            }

            private void TickInspectingPlayMode()
            {
                if (!EditorApplication.isPlaying)
                {
                    if (HasTimedOut(20d))
                        FailAndExit("Visible automation expected Unity to enter Play Mode for runtime inspection.");
                    return;
                }

                if (_context?.AvDesc == null)
                {
                    EnsureContextFromScene();
                    if (_context?.AvDesc == null)
                    {
                        if (HasTimedOut(20d))
                            FailAndExit("Visible automation lost the harness avatar while entering Play Mode for runtime inspection.");
                        return;
                    }
                }

                if (GetLiveComponent() == null)
                {
                    if (HasTimedOut(20d))
                        FailAndExit("Visible automation expected the ASM-Lite component to stay live after entering Play Mode.");
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                ShowCompletionReviewForCurrentMode();
            }

            private void TickVerifyingInteractivity()
            {
                if (_context?.AvDesc == null)
                {
                    FailAndExit("Visible launch UAT lost the live avatar fixture before interactability verification completed.");
                    return;
                }

                if (!ReferenceEquals(GetSelectedAvatar(), _context.AvDesc))
                {
                    Selection.activeGameObject = _context.AvatarGo;
                    _window.SelectAvatarForAutomation(_context.AvDesc);
                    return;
                }

                if (!EditorWindow.HasOpenInstances<ASMLiteWindow>())
                {
                    FailAndExit("Visible launch UAT should keep the ASM-Lite editor window open while verifying interactability.");
                    return;
                }

                _window.Focus();
                var hierarchy = _window.GetActionHierarchyContract();
                if (!hierarchy.HasPrimaryAction(ASMLiteWindow.AsmLiteWindowAction.AddPrefab)
                    && !hierarchy.HasPrimaryAction(ASMLiteWindow.AsmLiteWindowAction.Rebuild))
                {
                    if (HasTimedOut(15d))
                        FailAndExit("Visible launch UAT expected the ASM-Lite editor window to expose an interactable primary action after selecting Oct25_Dress.");
                    return;
                }

                if (!HasSatisfiedStepDelay())
                    return;

                ShowCompletionReviewForCurrentMode();
            }

            private void TickAwaitingAcceptance()
            {
                if (_window.IsVisibleAutomationCompletionReviewVisibleForAutomation())
                {
                    EnsureOverlayHostSnapshot(expectCompletionReviewWindow: true);
                    return;
                }

                if (!_window.WasVisibleAutomationCompletionReviewAcknowledgedForAutomation())
                {
                    FailAndExit("Visible automation completion review was dismissed without explicit user acceptance.");
                    return;
                }

                if ((AsmLiteVisibleAutomationMode)_configuration.mode == AsmLiteVisibleAutomationMode.PlayMode && EditorApplication.isPlaying)
                {
                    _completionPendingAfterPlayModeExit = true;
                    StartStage(AsmLiteVisibleAutomationStage.ExitingPlayModeAfterAcceptance, resetTimer: false);
                    EditorApplication.isPlaying = false;
                    return;
                }

                CompleteSuccessAndExit();
            }

            private void HandlePlayModeStateChanged(PlayModeStateChange change)
            {
                if (_disposed)
                    return;

                if (change == PlayModeStateChange.EnteredPlayMode
                    && (AsmLiteVisibleAutomationStage)_configuration.stage == AsmLiteVisibleAutomationStage.WaitingForPlayMode)
                {
                    StartStage(AsmLiteVisibleAutomationStage.InspectingPlayMode);
                    return;
                }

                if (change == PlayModeStateChange.EnteredEditMode && _completionPendingAfterPlayModeExit)
                {
                    _completionPendingAfterPlayModeExit = false;
                    CompleteSuccessAndExit();
                }
            }

            private void StartStage(AsmLiteVisibleAutomationStage stage, bool resetTimer = true)
            {
                _configuration.stage = (int)stage;
                PersistConfiguration(_configuration);
                ApplyStageVisuals(stage, resetTimer);
            }

            private void ApplyStageVisuals(AsmLiteVisibleAutomationStage stage, bool resetStageTimer)
            {
                if (resetStageTimer)
                    _stageStartedAt = EditorApplication.timeSinceStartup;

                EnsureWindow();
                if (_window == null)
                    return;

                _window.position = new Rect(120f, 120f, 920f, 900f);
                _window.Show();
                if (stage == AsmLiteVisibleAutomationStage.OpeningWindow)
                    _window.Focus();

                switch (stage)
                {
                    case AsmLiteVisibleAutomationStage.OpeningWindow:
                        SetOverlayStep(1, "ASM-Lite window is opening");
                        break;
                    case AsmLiteVisibleAutomationStage.SelectingAvatar:
                        SetOverlayStep(2, GetSelectingAvatarStepText());
                        break;
                    case AsmLiteVisibleAutomationStage.AddingPrefab:
                        SetOverlayStep(3, "ASM-Lite scaffold is being added");
                        break;
                    case AsmLiteVisibleAutomationStage.AttachingDefaultPayload:
                        SetOverlayStep(4, "Default ASM-Lite setup is being attached");
                        break;
                    case AsmLiteVisibleAutomationStage.VerifyingRebuild:
                        SetOverlayStep(5, "Rebuild state is being verified");
                        break;
                    case AsmLiteVisibleAutomationStage.WaitingForPlayMode:
                        SetOverlayStep(6, "Play mode is being entered");
                        break;
                    case AsmLiteVisibleAutomationStage.InspectingPlayMode:
                        SetOverlayStep(6, "Play mode is being entered");
                        break;
                    case AsmLiteVisibleAutomationStage.VerifyingInteractivity:
                        SetOverlayStep(4, "ASM-Lite window focus is being verified");
                        break;
                    case AsmLiteVisibleAutomationStage.AwaitingAcceptance:
                        SetOverlayStep(GetTotalSteps(), GetCompletionSuccessStep(), ASMLiteWindow.VisibleAutomationOverlayState.Success);
                        break;
                    case AsmLiteVisibleAutomationStage.ExitingPlayModeAfterAcceptance:
                        SetOverlayStep(GetTotalSteps(), "Visible play mode smoke run is finishing", ASMLiteWindow.VisibleAutomationOverlayState.Success);
                        break;
                }

                _window.Repaint();
            }

            private string GetSelectingAvatarStepText()
            {
                return (AsmLiteVisibleAutomationMode)_configuration.mode == AsmLiteVisibleAutomationMode.LaunchUnity
                    ? "Click ME scene is loading and Oct25_Dress is being selected"
                    : "Avatar is selected in the editor";
            }

            private static int CountSettingsManagerControls(VRCExpressionsMenu rootMenu)
            {
                if (rootMenu?.controls == null)
                    return 0;

                return rootMenu.controls.Count(control => control != null
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal));
            }

            private void ShowCompletionReviewForCurrentMode()
            {
                SetOverlayStep(GetTotalSteps(), GetCompletionSuccessStep(), ASMLiteWindow.VisibleAutomationOverlayState.Success);
                _window.ShowVisibleAutomationCompletionReview();
                _window.Repaint();
                StartStage(AsmLiteVisibleAutomationStage.AwaitingAcceptance, resetTimer: false);
            }

            private void SetOverlayStep(int stepIndex, string step, ASMLiteWindow.VisibleAutomationOverlayState state = ASMLiteWindow.VisibleAutomationOverlayState.Running)
            {
                _window.SetVisibleAutomationOverlayStatus(
                    GetOverlayTitle(),
                    step,
                    stepIndex,
                    GetTotalSteps(),
                    state,
                    presentationMode: true,
                    checklistItems: GetChecklist());
            }

            private void EnsureOverlayHostSnapshot(bool expectCompletionReviewWindow)
            {
                var snapshot = _window.GetVisibleAutomationOverlayHostSnapshotForTesting();
                if (!string.IsNullOrWhiteSpace(_configuration.externalOverlayStatePath))
                {
                    if (snapshot.HostKind != ASMLiteWindow.VisibleAutomationOverlayHostKind.ExternalPythonProcess)
                    {
                        FailAndExit("Visible automation should publish overlay state through the external overlay host when external overlay paths are configured.");
                        return;
                    }

                    if (!string.Equals(snapshot.ExternalOverlayStatePath, _configuration.externalOverlayStatePath, StringComparison.Ordinal))
                    {
                        FailAndExit("Visible automation should preserve the configured external overlay state path while the external overlay host is active.");
                        return;
                    }

                    if (!string.Equals(snapshot.ExternalOverlayAckPath, _configuration.externalOverlayAckPath ?? string.Empty, StringComparison.Ordinal))
                    {
                        FailAndExit("Visible automation should preserve the configured external overlay acknowledgement path while the external overlay host is active.");
                        return;
                    }

                    if (!snapshot.ExternalOverlayStateFileExists)
                    {
                        FailAndExit("Visible automation should publish the external overlay state file while the external overlay host is active.");
                        return;
                    }

                    if (snapshot.HasStatusWindow || snapshot.HasChecklistWindow || snapshot.HasCompletionReviewWindow)
                    {
                        FailAndExit("Visible automation should disable detached auxiliary overlay windows while the external overlay host is active.");
                        return;
                    }

                    return;
                }

                if (!snapshot.HasStatusWindow || !snapshot.HasChecklistWindow)
                    FailAndExit("Visible automation should keep the detached status and checklist overlays alive while the run is active.");

                if (expectCompletionReviewWindow && !snapshot.HasCompletionReviewWindow)
                    FailAndExit("Visible automation should surface the completion review through a detached auxiliary overlay window.");
            }

            private void EnsureWindow()
            {
                if (_window == null)
                    _window = ASMLiteWindow.OpenForAutomation();

                if (_window != null)
                    _window.ConfigureExternalVisibleAutomationOverlay(_configuration.externalOverlayStatePath, _configuration.externalOverlayAckPath);
            }

            private void EnsureContextFromScene()
            {
                if (_context != null && _context.AvDesc != null)
                    return;

                string avatarName = GetConfiguredAvatarName();
                Scene configuredScene = GetConfiguredLoadedScene();
                var descriptor = FindAvatarDescriptor(configuredScene, avatarName)
                    ?? UnityEngine.Object.FindObjectsOfType<VRCAvatarDescriptor>(true)
                        .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == avatarName);

                if (descriptor == null)
                    return;

                _context = new AsmLiteTestContext
                {
                    AvatarGo = descriptor.gameObject,
                    AvDesc = descriptor,
                    Comp = descriptor.GetComponentInChildren<ASMLiteComponent>(true),
                };
            }

            private VRCAvatarDescriptor GetSelectedAvatar()
            {
                EnsureContextFromScene();
                return _context?.AvDesc;
            }

            private ASMLiteComponent GetLiveComponent()
            {
                EnsureContextFromScene();
                if (_context?.AvDesc == null)
                    return null;

                _context.Comp = _context.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                return _context.Comp;
            }

            private string GetConfiguredScenePath()
            {
                return string.IsNullOrWhiteSpace(_configuration.scenePath)
                    ? DefaultVisibleAutomationScenePath
                    : _configuration.scenePath.Trim();
            }

            private string GetConfiguredAvatarName()
            {
                return string.IsNullOrWhiteSpace(_configuration.avatarName)
                    ? DefaultVisibleAutomationAvatarName
                    : _configuration.avatarName.Trim();
            }

            private Scene GetConfiguredLoadedScene()
            {
                string scenePath = GetConfiguredScenePath();
                return string.IsNullOrWhiteSpace(scenePath)
                    ? default
                    : SceneManager.GetSceneByPath(scenePath);
            }

            private bool TryLoadConfiguredSceneAvatar(out AsmLiteTestContext context, out string failureMessage)
            {
                context = null;
                failureMessage = null;

                string scenePath = GetConfiguredScenePath();
                string avatarName = GetConfiguredAvatarName();
                if (string.IsNullOrWhiteSpace(scenePath))
                {
                    failureMessage = "Visible automation requires a configured scene path.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(avatarName))
                {
                    failureMessage = "Visible automation requires a configured avatar name.";
                    return false;
                }

                Scene scene;
                try
                {
                    scene = GetConfiguredLoadedScene();
                    if (!scene.IsValid() || !scene.isLoaded)
                        scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                }
                catch (Exception ex)
                {
                    failureMessage = $"Visible automation could not open scene '{scenePath}': {ex.Message}";
                    return false;
                }

                if (!scene.IsValid() || !scene.isLoaded)
                {
                    failureMessage = $"Visible automation could not load scene '{scenePath}'.";
                    return false;
                }

                EditorSceneManager.SetActiveScene(scene);

                var descriptor = FindAvatarDescriptor(scene, avatarName);
                if (descriptor == null)
                {
                    failureMessage = $"Visible automation could not find avatar '{avatarName}' in scene '{scenePath}'.";
                    return false;
                }

                context = new AsmLiteTestContext
                {
                    AvatarGo = descriptor.gameObject,
                    AvDesc = descriptor,
                    Comp = descriptor.GetComponentInChildren<ASMLiteComponent>(true),
                };

                return true;
            }

            private static VRCAvatarDescriptor FindAvatarDescriptor(Scene scene, string avatarName)
            {
                if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrWhiteSpace(avatarName))
                    return null;

                foreach (var rootObject in scene.GetRootGameObjects())
                {
                    var descriptor = rootObject.GetComponentsInChildren<VRCAvatarDescriptor>(true)
                        .FirstOrDefault(candidate => candidate != null && candidate.gameObject.name == avatarName);
                    if (descriptor != null)
                        return descriptor;
                }

                return null;
            }

            private bool HasSatisfiedStepDelay()
            {
                return EditorApplication.timeSinceStartup - _stageStartedAt >= GetConfiguredStepDelaySeconds();
            }

            private float GetConfiguredStepDelaySeconds()
            {
                return _configuration != null && _configuration.hasStepDelaySeconds
                    ? Mathf.Max(0f, _configuration.stepDelaySeconds)
                    : ResolveConfiguredStepDelaySeconds(Environment.GetEnvironmentVariable(StepDelayEnvVarName));
            }

            private bool HasTimedOut(double seconds)
            {
                return EditorApplication.timeSinceStartup - _stageStartedAt >= seconds;
            }

            private void CompleteSuccessAndExit()
            {
                WriteResultAndExit("Passed", null, null, 0);
            }

            private void FailAndExit(string message, Exception ex = null)
            {
                Debug.LogError($"[ASM-Lite] {message}");
                if (ex != null)
                    Debug.LogException(ex);

                WriteResultAndExit("Failed", message, ex?.ToString(), 1);
            }

            private void WriteResultAndExit(string result, string failureMessage, string stackTrace, int exitCode)
            {
                try
                {
                    if (_window != null)
                    {
                        _window.ClearVisibleAutomationOverlay();
                        _window.Close();
                    }

                    CleanupTemporaryHarnessScene();

                    var startedAtUtc = new DateTimeOffset(_configuration.startedUtcTicks, TimeSpan.Zero);
                    var endedAtUtc = DateTimeOffset.UtcNow;
                    double durationSeconds = Math.Max(0d, (endedAtUtc - startedAtUtc).TotalSeconds);
                    XmlDocument document = BuildResultDocument(_configuration, result, failureMessage, stackTrace, durationSeconds, startedAtUtc, endedAtUtc);
                    WriteResultDocument(_configuration.resultsPath, document);
                }
                finally
                {
                    ClearPersistedConfiguration();
                    Dispose();
                    EditorApplication.Exit(exitCode);
                }
            }

            private void CleanupTemporaryHarnessScene()
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    return;

                Scene configuredScene = GetConfiguredLoadedScene();
                if ((!configuredScene.IsValid() || !configuredScene.isLoaded) && _context?.AvatarGo != null)
                    configuredScene = _context.AvatarGo.scene;

                if (configuredScene.IsValid() && configuredScene.isLoaded)
                    EditorSceneManager.CloseScene(configuredScene, true);

                ASMLiteTestFixtures.ResetGeneratedExprParams();
            }

            private string GetOverlayTitle()
            {
                return (AsmLiteVisibleAutomationMode)_configuration.mode switch
                {
                    AsmLiteVisibleAutomationMode.PlayMode => PlayModeOverlayTitle,
                    AsmLiteVisibleAutomationMode.LaunchUnity => LaunchUnityOverlayTitle,
                    _ => EditorOverlayTitle,
                };
            }

            private int GetTotalSteps()
            {
                return (AsmLiteVisibleAutomationMode)_configuration.mode switch
                {
                    AsmLiteVisibleAutomationMode.PlayMode => PlayModeChecklist.Length,
                    AsmLiteVisibleAutomationMode.LaunchUnity => LaunchUnityChecklist.Length,
                    _ => EditorChecklist.Length,
                };
            }

            private string[] GetChecklist()
            {
                return (AsmLiteVisibleAutomationMode)_configuration.mode switch
                {
                    AsmLiteVisibleAutomationMode.PlayMode => PlayModeChecklist,
                    AsmLiteVisibleAutomationMode.LaunchUnity => LaunchUnityChecklist,
                    _ => EditorChecklist,
                };
            }

            private string GetCompletionSuccessStep()
            {
                return (AsmLiteVisibleAutomationMode)_configuration.mode switch
                {
                    AsmLiteVisibleAutomationMode.PlayMode => "Visible playmode smoke run completed successfully",
                    AsmLiteVisibleAutomationMode.LaunchUnity => "Visible launch UAT completed successfully",
                    _ => "Visible smoke test completed successfully",
                };
            }
        }

        private static void WriteResultDocument(string resultsPath, XmlDocument document)
        {
            if (string.IsNullOrWhiteSpace(resultsPath))
                throw new InvalidOperationException("Visible automation result path cannot be empty.");

            string fullPath = Path.GetFullPath(resultsPath);
            string directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            document.Save(fullPath);
        }

        private static bool CanRenderVisibleEditorWindow()
        {
            return !Application.isBatchMode && SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null;
        }

        private static float GetConfiguredStepDelaySeconds()
        {
            return ResolveConfiguredStepDelaySeconds(Environment.GetEnvironmentVariable(StepDelayEnvVarName));
        }

        private static float ResolveConfiguredStepDelaySeconds(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return DefaultStepDelaySeconds;

            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue) || parsedValue < 0f)
                return DefaultStepDelaySeconds;

            return parsedValue;
        }
    }

    [Serializable]
    internal sealed class AsmLiteBatchFilterDefinition
    {
        public string[] testNames = Array.Empty<string>();
        public string[] groupNames = Array.Empty<string>();
        public string[] categoryNames = Array.Empty<string>();
        public string[] assemblyNames = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class AsmLiteBatchRunConfiguration
    {
        public AsmLiteBatchRunDefinition[] runs = Array.Empty<AsmLiteBatchRunDefinition>();
    }

    [Serializable]
    internal sealed class AsmLiteBatchRunDefinition
    {
        public string name;
        public string suiteId;
        public string suiteLabel;
        public string resultFile;
        public string[] testNames = Array.Empty<string>();
        public string[] groupNames = Array.Empty<string>();
        public string[] categoryNames = Array.Empty<string>();
        public string[] assemblyNames = Array.Empty<string>();
        public AsmLiteBatchFilterDefinition[] filters = Array.Empty<AsmLiteBatchFilterDefinition>();
        public bool runSynchronously;
        public bool allowEmptySelection;
    }

    [Serializable]
    internal sealed class AsmLiteBatchOverlayChecklistItem
    {
        public string text;
        public string state;
    }

    [Serializable]
    internal sealed class AsmLiteBatchOverlayMeta
    {
        public string configuredStepDelaySeconds;
        public string selectionLabel;
        public string currentRunName;
        public string currentSuiteId;
        public string currentSuiteLabel;
        public string runnerStrategy;
    }

    [Serializable]
    internal sealed class AsmLiteBatchOverlayStateDocument
    {
        public string sessionId;
        public bool sessionActive;
        public string state;
        public bool presentationMode;
        public string title;
        public string step;
        public int stepIndex;
        public int totalSteps;
        public AsmLiteBatchOverlayChecklistItem[] checklist = Array.Empty<AsmLiteBatchOverlayChecklistItem>();
        public bool completionReviewVisible;
        public int completionReviewRequestId;
        public string completionReviewTitle;
        public string completionReviewMessage;
        public bool completionReviewAcknowledged;
        public long updatedUtcTicks;
        public AsmLiteBatchOverlayMeta meta;
    }

    [Serializable]
    internal sealed class AsmLiteBatchRunnerSessionState
    {
        public AsmLiteBatchRunDefinition[] runs = Array.Empty<AsmLiteBatchRunDefinition>();
        public string resultsDirectory;
        public string canonicalResultsPath;
        public string overlayStatePath;
        public string selectionLabel;
        public string overlaySessionId;
        public string overlayTitle;
        public float stepDelaySeconds;
        public int nextRunIndex;
        public int expectedRunCount;
        public int exitCode;
        public int activeRunIndex = -1;
    }

    [InitializeOnLoad]
    public static class ASMLiteBatchTestRunner
    {
        private const string RunsJsonEnv = "ASMLITE_BATCH_RUNS_JSON";
        private const string ResultsDirEnv = "ASMLITE_BATCH_RESULTS_DIR";
        private const string CanonicalResultsPathEnv = "ASMLITE_BATCH_CANONICAL_RESULTS_PATH";
        private const string OverlayStatePathEnv = "ASMLITE_BATCH_OVERLAY_STATE_PATH";
        private const string StepDelaySecondsEnv = "ASMLITE_BATCH_STEP_DELAY_SECONDS";
        private const string SelectionLabelEnv = "ASMLITE_BATCH_SELECTION_LABEL";
        private const string OverlaySessionIdEnv = "ASMLITE_BATCH_SESSION_ID";
        private const string OverlayTitleEnv = "ASMLITE_BATCH_OVERLAY_TITLE";
        private const string RunsJsonPathArg = "-asmliteBatchRunsJsonPath";
        private const string ResultsDirArg = "-asmliteBatchResultsDir";
        private const string CanonicalResultsPathArg = "-asmliteBatchCanonicalResultsPath";
        private const string OverlayStatePathArg = "-asmliteBatchOverlayStatePath";
        private const string StepDelaySecondsArg = "-asmliteBatchStepDelaySeconds";
        private const string SelectionLabelArg = "-asmliteBatchSelectionLabel";
        private const string OverlaySessionIdArg = "-asmliteBatchSessionId";
        private const string OverlayTitleArg = "-asmliteBatchOverlayTitle";
        private const string CanonicalResultsFileName = "editmode-results.xml";
        private const string NUnitResultsEngineVersion = "3.5.0.0";
        private const string NUnitResultsTimeFormat = "u";
        private const string DefaultOverlayTitle = "ASM-Lite UAT smoke suites";
        private const string DefaultSelectionLabel = "UAT checklist smoke suites";
        private const string SessionStateKey = "ASMLite.BatchTestRunner.SessionState";

        private static readonly Queue<AsmLiteBatchRunDefinition> s_pendingRuns = new Queue<AsmLiteBatchRunDefinition>();
        private static readonly List<AsmLiteBatchRunDefinition> s_allRuns = new List<AsmLiteBatchRunDefinition>();
        private static readonly List<CompletedRunResult> s_completedRunResults = new List<CompletedRunResult>();
        private static readonly List<OverlaySuiteStatus> s_overlaySuiteStatuses = new List<OverlaySuiteStatus>();

        private static TestRunnerApi s_testRunnerApi;
        private static CallbackForwarder s_callbackForwarder;
        private static string s_resultsDirectory;
        private static string s_canonicalResultsPath;
        private static string s_overlayStatePath;
        private static string s_selectionLabel;
        private static string s_overlaySessionId;
        private static string s_overlayTitle;
        private static int s_completedRunCount;
        private static int s_expectedRunCount;
        private static int s_exitCode;
        private static int s_nextRunIndex;
        private static int s_activeRunIndex;
        private static AsmLiteBatchRunDefinition s_activeRun;
        private static float s_stepDelaySeconds;
        private static double s_nextRunNotBefore;

        static ASMLiteBatchTestRunner()
        {
            EditorApplication.delayCall += ResumePendingRunIfNeeded;
        }

        public static void RunFromCommandLine()
        {
            try
            {
                Start(BuildSessionStateFromCommandLine(Environment.GetCommandLineArgs()), resumedFromSessionState: false);
            }
            catch (Exception ex)
            {
                ClearPersistedSessionState();
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void ResumePendingRunIfNeeded()
        {
            EditorApplication.delayCall -= ResumePendingRunIfNeeded;
            if (s_testRunnerApi != null || s_activeRun != null || s_pendingRuns.Count > 0)
                return;

            var sessionState = LoadPersistedSessionState();
            if (sessionState?.runs == null || sessionState.runs.Length == 0)
                return;

            Start(sessionState, resumedFromSessionState: true);
        }

        private static void Start(AsmLiteBatchRunnerSessionState sessionState, bool resumedFromSessionState)
        {
            ResetStaticState();
            RestoreFromSessionState(sessionState);
            PersistSessionState();

            WriteOverlayState(
                "Running",
                resumedFromSessionState
                    ? $"Resuming single Unity smoke suite session ({s_completedRunCount}/{s_expectedRunCount} completed)"
                    : "Preparing single Unity smoke suite session",
                s_completedRunCount,
                true);

            s_testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            s_callbackForwarder = new CallbackForwarder();
            s_testRunnerApi.RegisterCallbacks(s_callbackForwarder);

            EditorApplication.delayCall += StartNextRun;
        }

        private static void ResetStaticState()
        {
            s_pendingRuns.Clear();
            s_allRuns.Clear();
            ASMLiteTestFixtures.ClearRecordedGenerationWiringFailures();
            s_completedRunCount = 0;
            s_expectedRunCount = 0;
            s_exitCode = 0;
            s_nextRunIndex = 0;
            s_activeRunIndex = -1;
            s_activeRun = null;
            s_completedRunResults.Clear();

            if (s_testRunnerApi != null && s_callbackForwarder != null)
                s_testRunnerApi.UnregisterCallbacks(s_callbackForwarder);

            if (s_testRunnerApi != null)
                UnityEngine.Object.DestroyImmediate(s_testRunnerApi);

            s_testRunnerApi = null;
            s_callbackForwarder = null;
            s_resultsDirectory = null;
            s_canonicalResultsPath = null;
            s_overlayStatePath = null;
            s_selectionLabel = DefaultSelectionLabel;
            s_overlaySessionId = "asmlite-batch-runner";
            s_overlayTitle = DefaultOverlayTitle;
            s_stepDelaySeconds = 0f;
            s_nextRunNotBefore = 0d;
            s_overlaySuiteStatuses.Clear();
        }

        internal static AsmLiteBatchRunnerSessionState BuildSessionStateFromCommandLine(string[] commandLineArgs)
        {
            if (commandLineArgs == null)
                throw new ArgumentNullException(nameof(commandLineArgs));

            string rawJsonPath = ResolveBatchCommandLineValue(commandLineArgs, RunsJsonPathArg);
            string rawJson;
            if (!string.IsNullOrWhiteSpace(rawJsonPath))
            {
                rawJsonPath = ResolveRequiredPath(rawJsonPath);
                rawJson = File.ReadAllText(rawJsonPath, Encoding.UTF8);
            }
            else
            {
                rawJson = Environment.GetEnvironmentVariable(RunsJsonEnv);
            }

            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException($"Missing required command-line argument '{RunsJsonPathArg}' or environment variable '{RunsJsonEnv}'.");

            var configuration = JsonUtility.FromJson<AsmLiteBatchRunConfiguration>(rawJson);
            if (configuration == null || configuration.runs == null || configuration.runs.Length == 0)
                throw new InvalidOperationException("Batch test runner requires at least one run definition.");

            string rawResultsDir = ResolveBatchCommandLineValue(commandLineArgs, ResultsDirArg) ?? Environment.GetEnvironmentVariable(ResultsDirEnv);
            string resultsDirectory = string.IsNullOrWhiteSpace(rawResultsDir)
                ? Directory.GetCurrentDirectory()
                : ResolveRequiredPath(rawResultsDir);

            string rawCanonicalResultsPath = ResolveBatchCommandLineValue(commandLineArgs, CanonicalResultsPathArg) ?? Environment.GetEnvironmentVariable(CanonicalResultsPathEnv);
            string canonicalResultsPath = ResolveCanonicalResultsPath(rawCanonicalResultsPath, resultsDirectory);
            string overlayStatePath = ResolveOptionalPath(ResolveBatchCommandLineValue(commandLineArgs, OverlayStatePathArg) ?? Environment.GetEnvironmentVariable(OverlayStatePathEnv));
            string selectionLabel = ResolveOptionalValue(ResolveBatchCommandLineValue(commandLineArgs, SelectionLabelArg) ?? Environment.GetEnvironmentVariable(SelectionLabelEnv), DefaultSelectionLabel);
            string overlaySessionId = ResolveOptionalValue(ResolveBatchCommandLineValue(commandLineArgs, OverlaySessionIdArg) ?? Environment.GetEnvironmentVariable(OverlaySessionIdEnv), "asmlite-batch-runner");
            string overlayTitle = ResolveOptionalValue(ResolveBatchCommandLineValue(commandLineArgs, OverlayTitleArg) ?? Environment.GetEnvironmentVariable(OverlayTitleEnv), DefaultOverlayTitle);
            float stepDelaySeconds = ResolveConfiguredStepDelaySeconds(ResolveBatchCommandLineValue(commandLineArgs, StepDelaySecondsArg) ?? Environment.GetEnvironmentVariable(StepDelaySecondsEnv));

            var normalizedRuns = new AsmLiteBatchRunDefinition[configuration.runs.Length];
            var writtenResultPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(canonicalResultsPath),
            };

            for (int i = 0; i < configuration.runs.Length; i++)
            {
                var normalized = NormalizeRun(configuration.runs[i], i);
                string resolvedResultPath = Path.GetFullPath(ResolveResultPath(normalized.resultFile, resultsDirectory));
                if (string.Equals(resolvedResultPath, canonicalResultsPath, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Batch test runner reserves canonical result path '{canonicalResultsPath}'. Pick a different resultFile.");
                if (!writtenResultPaths.Add(resolvedResultPath))
                    throw new InvalidOperationException($"Batch test runner requires unique resultFile values. Duplicate path: '{resolvedResultPath}'.");
                if (!normalized.allowEmptySelection && !RunHasAnySelection(normalized))
                    throw new InvalidOperationException($"Batch test runner run '{normalized.name}' must declare at least one selector unless allowEmptySelection is true.");

                normalizedRuns[i] = normalized;
            }

            return new AsmLiteBatchRunnerSessionState
            {
                runs = normalizedRuns,
                resultsDirectory = Path.GetFullPath(resultsDirectory),
                canonicalResultsPath = canonicalResultsPath,
                overlayStatePath = overlayStatePath,
                selectionLabel = selectionLabel,
                overlaySessionId = overlaySessionId,
                overlayTitle = overlayTitle,
                stepDelaySeconds = stepDelaySeconds,
                nextRunIndex = 0,
                expectedRunCount = normalizedRuns.Length,
                exitCode = 0,
                activeRunIndex = -1,
            };
        }

        private static void RestoreFromSessionState(AsmLiteBatchRunnerSessionState sessionState)
        {
            if (sessionState == null)
                throw new ArgumentNullException(nameof(sessionState));

            s_resultsDirectory = string.IsNullOrWhiteSpace(sessionState.resultsDirectory)
                ? Directory.GetCurrentDirectory()
                : ResolveRequiredPath(sessionState.resultsDirectory);
            Directory.CreateDirectory(s_resultsDirectory);

            s_canonicalResultsPath = ResolveCanonicalResultsPath(sessionState.canonicalResultsPath, s_resultsDirectory);
            s_overlayStatePath = ResolveOptionalPath(sessionState.overlayStatePath);
            s_selectionLabel = ResolveOptionalValue(sessionState.selectionLabel, DefaultSelectionLabel);
            s_overlaySessionId = ResolveOptionalValue(sessionState.overlaySessionId, "asmlite-batch-runner");
            s_overlayTitle = ResolveOptionalValue(sessionState.overlayTitle, DefaultOverlayTitle);
            s_stepDelaySeconds = sessionState.stepDelaySeconds < 0f ? 0f : sessionState.stepDelaySeconds;
            s_exitCode = sessionState.exitCode;

            if (sessionState.runs == null || sessionState.runs.Length == 0)
                throw new InvalidOperationException("Batch test runner requires at least one persisted run definition to resume.");

            for (int i = 0; i < sessionState.runs.Length; i++)
                s_allRuns.Add(NormalizeRun(sessionState.runs[i], i));

            s_expectedRunCount = s_allRuns.Count;
            int requestedCompletedCount = Math.Max(0, Math.Min(sessionState.nextRunIndex, s_allRuns.Count));
            for (int i = 0; i < requestedCompletedCount; i++)
            {
                var completedRun = s_allRuns[i];
                string resultPath = ResolveResultPath(completedRun.resultFile);
                if (!File.Exists(resultPath))
                {
                    Debug.LogWarning($"[ASM-Lite] Missing persisted batch result for completed run '{completedRun.name}' at '{resultPath}'. Resuming from this run.");
                    break;
                }

                var document = new XmlDocument();
                document.Load(resultPath);
                s_completedRunResults.Add(new CompletedRunResult
                {
                    name = completedRun.name,
                    suiteId = completedRun.suiteId,
                    suiteLabel = completedRun.suiteLabel,
                    resultPath = resultPath,
                    document = document,
                });
                s_completedRunCount++;
            }

            int pendingStartIndex = s_completedRunCount;
            if (sessionState.activeRunIndex >= s_completedRunCount && sessionState.activeRunIndex < s_allRuns.Count)
                pendingStartIndex = sessionState.activeRunIndex;

            s_nextRunIndex = pendingStartIndex;
            s_activeRunIndex = -1;
            s_activeRun = null;
            s_nextRunNotBefore = 0d;

            RebuildOverlaySuiteStatuses(pendingStartIndex);

            for (int i = pendingStartIndex; i < s_allRuns.Count; i++)
                s_pendingRuns.Enqueue(s_allRuns[i]);
        }

        private static AsmLiteBatchRunnerSessionState LoadPersistedSessionState()
        {
            string json = SessionState.GetString(SessionStateKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<AsmLiteBatchRunnerSessionState>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Failed to restore batch test runner state: {ex.Message}");
                SessionState.EraseString(SessionStateKey);
                return null;
            }
        }

        private static void PersistSessionState()
        {
            if (s_allRuns.Count == 0)
                return;

            var sessionState = new AsmLiteBatchRunnerSessionState
            {
                runs = s_allRuns.ToArray(),
                resultsDirectory = s_resultsDirectory,
                canonicalResultsPath = s_canonicalResultsPath,
                overlayStatePath = s_overlayStatePath,
                selectionLabel = s_selectionLabel,
                overlaySessionId = s_overlaySessionId,
                overlayTitle = s_overlayTitle,
                stepDelaySeconds = s_stepDelaySeconds,
                nextRunIndex = s_nextRunIndex,
                expectedRunCount = s_expectedRunCount,
                exitCode = s_exitCode,
                activeRunIndex = s_activeRunIndex,
            };

            SessionState.SetString(SessionStateKey, JsonUtility.ToJson(sessionState));
        }

        private static void ClearPersistedSessionState()
        {
            SessionState.EraseString(SessionStateKey);
        }

        internal static AsmLiteBatchRunDefinition NormalizeRun(AsmLiteBatchRunDefinition run, int index)
        {
            var normalized = run ?? new AsmLiteBatchRunDefinition();
            normalized.name = string.IsNullOrWhiteSpace(normalized.name)
                ? $"run-{index + 1:D2}"
                : normalized.name.Trim();
            normalized.suiteId = string.IsNullOrWhiteSpace(normalized.suiteId)
                ? normalized.name
                : normalized.suiteId.Trim();
            normalized.suiteLabel = string.IsNullOrWhiteSpace(normalized.suiteLabel)
                ? normalized.name
                : normalized.suiteLabel.Trim();
            normalized.resultFile = string.IsNullOrWhiteSpace(normalized.resultFile)
                ? $"{normalized.name}.xml"
                : normalized.resultFile.Trim();
            normalized.testNames = NormalizeStringArray(normalized.testNames);
            normalized.groupNames = NormalizeStringArray(normalized.groupNames);
            normalized.categoryNames = NormalizeStringArray(normalized.categoryNames);
            normalized.assemblyNames = NormalizeStringArray(normalized.assemblyNames);
            normalized.filters = NormalizeFilters(normalized.filters);

            var legacyFilter = CreateFilterDefinition(
                normalized.testNames,
                normalized.groupNames,
                normalized.categoryNames,
                normalized.assemblyNames);

            if (normalized.filters.Length == 0)
            {
                if (HasAnySelection(legacyFilter))
                    normalized.filters = new[] { legacyFilter };
            }
            else if (HasAnySelection(legacyFilter))
            {
                var filtersWithLegacy = new AsmLiteBatchFilterDefinition[normalized.filters.Length + 1];
                normalized.filters.CopyTo(filtersWithLegacy, 0);
                filtersWithLegacy[normalized.filters.Length] = legacyFilter;
                normalized.filters = filtersWithLegacy;
            }

            return normalized;
        }

        internal static bool RunHasAnySelection(AsmLiteBatchRunDefinition run)
        {
            return run?.filters != null && run.filters.Any(HasAnySelection);
        }

        internal static Filter[] BuildExecutionFilters(AsmLiteBatchRunDefinition run)
        {
            if (run?.filters == null || run.filters.Length == 0)
            {
                return new[]
                {
                    new Filter
                    {
                        testMode = TestMode.EditMode,
                        testNames = Array.Empty<string>(),
                        groupNames = Array.Empty<string>(),
                        categoryNames = Array.Empty<string>(),
                        assemblyNames = Array.Empty<string>(),
                    }
                };
            }

            var filters = new Filter[run.filters.Length];
            for (int i = 0; i < run.filters.Length; i++)
            {
                var definition = run.filters[i] ?? new AsmLiteBatchFilterDefinition();
                filters[i] = new Filter
                {
                    testMode = TestMode.EditMode,
                    testNames = NormalizeStringArray(definition.testNames),
                    groupNames = NormalizeStringArray(definition.groupNames),
                    categoryNames = NormalizeStringArray(definition.categoryNames),
                    assemblyNames = NormalizeStringArray(definition.assemblyNames),
                };
            }

            return filters;
        }

        private static AsmLiteBatchFilterDefinition[] NormalizeFilters(AsmLiteBatchFilterDefinition[] filters)
        {
            if (filters == null || filters.Length == 0)
                return Array.Empty<AsmLiteBatchFilterDefinition>();

            return filters
                .Where(filter => filter != null)
                .Select(filter => CreateFilterDefinition(
                    filter.testNames,
                    filter.groupNames,
                    filter.categoryNames,
                    filter.assemblyNames))
                .ToArray();
        }

        private static AsmLiteBatchFilterDefinition CreateFilterDefinition(
            string[] testNames,
            string[] groupNames,
            string[] categoryNames,
            string[] assemblyNames)
        {
            return new AsmLiteBatchFilterDefinition
            {
                testNames = NormalizeStringArray(testNames),
                groupNames = NormalizeStringArray(groupNames),
                categoryNames = NormalizeStringArray(categoryNames),
                assemblyNames = NormalizeStringArray(assemblyNames),
            };
        }

        private static bool HasAnySelection(AsmLiteBatchFilterDefinition filter)
        {
            return filter != null
                && (filter.testNames.Length > 0
                    || filter.groupNames.Length > 0
                    || filter.categoryNames.Length > 0
                    || filter.assemblyNames.Length > 0);
        }

        private static string[] NormalizeStringArray(string[] values)
        {
            return values == null
                ? Array.Empty<string>()
                : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
        }

        internal static string ResolveBatchCommandLineValue(string[] commandLineArgs, string argumentName)
        {
            if (commandLineArgs == null || string.IsNullOrWhiteSpace(argumentName))
                return null;

            for (int i = 0; i < commandLineArgs.Length - 1; i++)
            {
                if (!string.Equals(commandLineArgs[i], argumentName, StringComparison.Ordinal))
                    continue;

                string value = commandLineArgs[i + 1];
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }

            return null;
        }

        private static string ResolveRequiredPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                throw new InvalidOperationException("Expected a non-empty batch runner path value.");

            return Path.GetFullPath(NormalizePlatformPath(rawPath));
        }

        private static void StartNextRun()
        {
            EditorApplication.delayCall -= StartNextRun;

            try
            {
                if (s_pendingRuns.Count == 0)
                {
                    WriteOverlayState(s_exitCode == 0 ? "Success" : "Failure", s_exitCode == 0 ? "Completed single Unity smoke suite session" : "Single Unity smoke suite session completed with failures", s_expectedRunCount, false);
                    CompleteAndExit();
                    return;
                }

                if (s_nextRunNotBefore > 0d && EditorApplication.timeSinceStartup < s_nextRunNotBefore)
                {
                    EditorApplication.update -= WaitForNextRunDelay;
                    EditorApplication.update += WaitForNextRunDelay;
                    return;
                }

                EditorApplication.update -= WaitForNextRunDelay;
                s_activeRun = s_pendingRuns.Dequeue();
                s_activeRunIndex = s_nextRunIndex;
                SetOverlaySuiteState(s_activeRun.suiteId, "Active", preserveFailed: true);
                PersistSessionState();
                WriteOverlayState(
                    "Running",
                    $"Running suite {s_completedRunCount + 1}/{s_expectedRunCount}: {s_activeRun.suiteLabel} — {s_activeRun.name}",
                    s_completedRunCount + 1,
                    true);

                var filters = BuildExecutionFilters(s_activeRun);
                var settings = new ExecutionSettings(filters)
                {
                    runSynchronously = s_activeRun.runSynchronously,
                };

                Debug.Log($"[ASM-Lite] Starting batch test run '{s_activeRun.name}' ({s_completedRunCount + 1} of {s_completedRunCount + s_pendingRuns.Count + 1}) with {filters.Length} filter selection(s).");
                s_testRunnerApi.Execute(settings);
            }
            catch (Exception ex)
            {
                s_exitCode = 1;
                Debug.LogException(ex);
                CompleteAndExit();
            }
        }

        private static void WaitForNextRunDelay()
        {
            if (EditorApplication.timeSinceStartup < s_nextRunNotBefore)
                return;

            EditorApplication.update -= WaitForNextRunDelay;
            StartNextRun();
        }

        private static void HandleRunFinished(ITestResultAdaptor result)
        {
            try
            {
                if (s_activeRun == null)
                    throw new InvalidOperationException("Batch test runner received RunFinished without an active run.");

                string resultPath = ResolveResultPath(s_activeRun.resultFile);
                XmlDocument resultDocument = ConvertResultToXmlDocument(result);
                WriteXmlDocument(resultDocument, resultPath);
                s_completedRunResults.Add(new CompletedRunResult
                {
                    name = s_activeRun.name,
                    suiteId = s_activeRun.suiteId,
                    suiteLabel = s_activeRun.suiteLabel,
                    resultPath = resultPath,
                    document = resultDocument,
                });
                s_completedRunCount++;
                s_nextRunIndex = s_completedRunCount;

                bool runFailed = result.FailCount > 0 || result.TestStatus == TestStatus.Failed || (result.ResultState?.StartsWith("Failed", StringComparison.Ordinal) ?? false);
                if (runFailed)
                    s_exitCode = 1;

                if (runFailed)
                {
                    SetOverlaySuiteState(s_activeRun.suiteId, "Failed", preserveFailed: true);
                }
                else if (HasPendingSuiteRuns(s_activeRun.suiteId))
                {
                    SetOverlaySuiteState(s_activeRun.suiteId, "Active", preserveFailed: true);
                }
                else
                {
                    SetOverlaySuiteState(s_activeRun.suiteId, "Completed", preserveFailed: true);
                }

                WriteOverlayState(
                    runFailed ? "Warning" : "Running",
                    runFailed
                        ? $"Suite failed {s_completedRunCount}/{s_expectedRunCount}: {s_activeRun.suiteLabel} — {s_activeRun.name}"
                        : $"Completed suite {s_completedRunCount}/{s_expectedRunCount}: {s_activeRun.suiteLabel} — {s_activeRun.name}",
                    s_completedRunCount,
                    true);

                if (s_stepDelaySeconds > 0f)
                    s_nextRunNotBefore = EditorApplication.timeSinceStartup + s_stepDelaySeconds;
                else
                    s_nextRunNotBefore = 0d;

                Debug.Log($"[ASM-Lite] Finished batch test run '{s_activeRun.name}' -> {resultPath}");
            }
            catch (Exception ex)
            {
                s_exitCode = 1;
                Debug.LogException(ex);
            }
            finally
            {
                s_activeRun = null;
                s_activeRunIndex = -1;
                PersistSessionState();
                EditorApplication.delayCall += StartNextRun;
            }
        }

        private static string ResolveResultPath(string resultFile)
        {
            return ResolveResultPath(resultFile, s_resultsDirectory);
        }

        private static string ResolveResultPath(string resultFile, string resultsDirectory)
        {
            string normalizedResultFile = NormalizePlatformPath(resultFile);
            if (Path.IsPathRooted(normalizedResultFile))
                return Path.GetFullPath(normalizedResultFile);

            return Path.Combine(resultsDirectory, normalizedResultFile);
        }

        private static string ResolveCanonicalResultsPath(string configuredPath)
        {
            return ResolveCanonicalResultsPath(configuredPath, s_resultsDirectory);
        }

        private static string ResolveCanonicalResultsPath(string configuredPath, string resultsDirectory)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return Path.GetFullPath(configuredPath.Trim());

            return Path.GetFullPath(ResolveResultPath(CanonicalResultsFileName, resultsDirectory));
        }

        private static string ResolveOptionalPath(string configuredPath)
        {
            return string.IsNullOrWhiteSpace(configuredPath)
                ? string.Empty
                : Path.GetFullPath(NormalizePlatformPath(configuredPath));
        }

        private static string NormalizePlatformPath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return string.Empty;

            string trimmedPath = rawPath.Trim();
            if (Path.DirectorySeparatorChar != '\\')
                return trimmedPath;

            if (!trimmedPath.StartsWith("/mnt/", StringComparison.OrdinalIgnoreCase) || trimmedPath.Length < 6)
                return trimmedPath;

            char driveLetter = trimmedPath[5];
            if (!char.IsLetter(driveLetter))
                return trimmedPath;

            if (trimmedPath.Length > 6 && trimmedPath[6] != '/')
                return trimmedPath;

            string relativePath = trimmedPath.Length > 7
                ? trimmedPath.Substring(7).Replace('/', '\\')
                : string.Empty;
            return string.IsNullOrEmpty(relativePath)
                ? $"{char.ToUpperInvariant(driveLetter)}:\\"
                : $"{char.ToUpperInvariant(driveLetter)}:\\{relativePath}";
        }

        private static string ResolveOptionalValue(string configuredValue, string fallback)
        {
            return string.IsNullOrWhiteSpace(configuredValue)
                ? fallback
                : configuredValue.Trim();
        }

        private static float ResolveConfiguredStepDelaySeconds(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return 0f;

            return float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsedValue) && parsedValue >= 0f
                ? parsedValue
                : 0f;
        }

        private static void RebuildOverlaySuiteStatuses(int pendingStartIndex)
        {
            s_overlaySuiteStatuses.Clear();
            foreach (var run in s_allRuns)
                EnsureOverlaySuiteStatus(run.suiteId, run.suiteLabel);

            foreach (var completedRun in s_completedRunResults)
            {
                bool runFailed = DidRunDocumentFail(completedRun.document);
                string state = runFailed
                    ? "Failed"
                    : HasPendingRunsForSuite(completedRun.suiteId, pendingStartIndex)
                        ? "Active"
                        : "Completed";
                SetOverlaySuiteState(completedRun.suiteId, state, preserveFailed: true);
            }

            if (pendingStartIndex >= 0 && pendingStartIndex < s_allRuns.Count)
                SetOverlaySuiteState(s_allRuns[pendingStartIndex].suiteId, "Active", preserveFailed: true);
        }

        private static bool DidRunDocumentFail(XmlDocument resultDocument)
        {
            XmlElement root = resultDocument?.DocumentElement;
            if (root == null)
                return true;

            XmlElement metricsRoot = ResolveCanonicalMetricsRoot(root);
            return ReadIntAttribute(metricsRoot, "failed") > 0
                || string.Equals(metricsRoot.GetAttribute("result"), "Failed", StringComparison.Ordinal)
                || metricsRoot.GetAttribute("result").StartsWith("Failed", StringComparison.Ordinal);
        }

        private static bool HasPendingRunsForSuite(string suiteId, int startIndex)
        {
            if (string.IsNullOrWhiteSpace(suiteId))
                return false;

            int normalizedStartIndex = Math.Max(0, startIndex);
            return s_allRuns.Skip(normalizedStartIndex).Any(run => string.Equals(run.suiteId, suiteId, StringComparison.Ordinal));
        }

        private static void EnsureOverlaySuiteStatus(string suiteId, string suiteLabel)
        {
            if (s_overlaySuiteStatuses.Any(item => string.Equals(item.suiteId, suiteId, StringComparison.Ordinal)))
                return;

            s_overlaySuiteStatuses.Add(new OverlaySuiteStatus
            {
                suiteId = suiteId,
                label = suiteLabel,
                state = "Pending",
            });
        }

        private static void SetOverlaySuiteState(string suiteId, string state, bool preserveFailed)
        {
            OverlaySuiteStatus suiteStatus = s_overlaySuiteStatuses.FirstOrDefault(item => string.Equals(item.suiteId, suiteId, StringComparison.Ordinal));
            if (suiteStatus == null)
                return;

            if (preserveFailed && string.Equals(suiteStatus.state, "Failed", StringComparison.Ordinal) && !string.Equals(state, "Failed", StringComparison.Ordinal))
                return;

            suiteStatus.state = state;
        }

        private static bool HasPendingSuiteRuns(string suiteId)
        {
            return s_pendingRuns.Any(run => string.Equals(run.suiteId, suiteId, StringComparison.Ordinal));
        }

        private static void WriteOverlayState(string overlayState, string step, int stepIndex, bool sessionActive)
        {
            if (string.IsNullOrWhiteSpace(s_overlayStatePath))
                return;

            string directory = Path.GetDirectoryName(s_overlayStatePath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var payload = new AsmLiteBatchOverlayStateDocument
            {
                sessionId = s_overlaySessionId,
                sessionActive = sessionActive,
                state = overlayState,
                presentationMode = true,
                title = s_overlayTitle,
                step = step,
                stepIndex = stepIndex,
                totalSteps = s_expectedRunCount,
                checklist = s_overlaySuiteStatuses.Select(item => new AsmLiteBatchOverlayChecklistItem
                {
                    text = item.label,
                    state = item.state,
                }).ToArray(),
                completionReviewVisible = false,
                completionReviewRequestId = 0,
                completionReviewTitle = string.Empty,
                completionReviewMessage = string.Empty,
                completionReviewAcknowledged = false,
                updatedUtcTicks = DateTime.UtcNow.Ticks,
                meta = new AsmLiteBatchOverlayMeta
                {
                    configuredStepDelaySeconds = s_stepDelaySeconds.ToString("0.###", CultureInfo.InvariantCulture),
                    selectionLabel = s_selectionLabel,
                    currentRunName = s_activeRun?.name ?? string.Empty,
                    currentSuiteId = s_activeRun?.suiteId ?? string.Empty,
                    currentSuiteLabel = s_activeRun?.suiteLabel ?? string.Empty,
                    runnerStrategy = "single_unity_instance",
                },
            };

            string tempPath = s_overlayStatePath + ".tmp";
            File.WriteAllText(tempPath, JsonUtility.ToJson(payload, true));
            if (File.Exists(s_overlayStatePath))
                File.Delete(s_overlayStatePath);
            File.Move(tempPath, s_overlayStatePath);
        }

        private static XmlDocument ConvertResultToXmlDocument(ITestResultAdaptor result)
        {
            if (result == null)
                throw new InvalidOperationException("Batch test runner received a null test result adaptor.");

            XmlDocument rawResultDocument = ConvertRawXmlObjectToDocument(result.ToXml(), result.GetType().FullName);
            return WrapResultDocumentWithResultMetrics(rawResultDocument, result);
        }

        internal static XmlDocument WrapResultDocumentWithResultMetrics(XmlDocument rawResultDocument, ITestResultAdaptor result)
        {
            if (result == null)
                throw new InvalidOperationException("Batch test runner requires result metrics when wrapping NUnit XML output.");

            XmlElement rawRoot = rawResultDocument?.DocumentElement
                ?? throw new InvalidOperationException("Batch test runner cannot wrap a result document without a root element.");

            if (string.Equals(rawRoot.Name, "test-run", StringComparison.Ordinal))
            {
                var wrappedRunDocument = new XmlDocument();
                wrappedRunDocument.LoadXml(rawResultDocument.OuterXml);
                ApplyResultMetricsToRunRoot(wrappedRunDocument.DocumentElement, result, rawRoot);
                return wrappedRunDocument;
            }

            var wrappedDocument = new XmlDocument();
            wrappedDocument.AppendChild(wrappedDocument.CreateXmlDeclaration("1.0", "utf-8", null));

            XmlElement wrappedRoot = wrappedDocument.CreateElement("test-run");
            wrappedDocument.AppendChild(wrappedRoot);
            ApplyResultMetricsToRunRoot(wrappedRoot, result, rawRoot);
            wrappedRoot.AppendChild(wrappedDocument.ImportNode(rawRoot, true));
            return wrappedDocument;
        }

        private static void ApplyResultMetricsToRunRoot(XmlElement runRoot, ITestResultAdaptor result, XmlElement rawRoot)
        {
            if (runRoot == null)
                throw new InvalidOperationException("Batch test runner requires a test-run root element before applying result metrics.");

            ResultMetrics effectiveMetrics = ResolveEffectiveResultMetrics(result, rawRoot);
            string existingId = runRoot.GetAttribute("id");
            runRoot.SetAttribute("id", string.IsNullOrWhiteSpace(existingId) ? "2" : existingId);
            runRoot.SetAttribute("testcasecount", effectiveMetrics.Total.ToString(CultureInfo.InvariantCulture));
            runRoot.SetAttribute("result", effectiveMetrics.ResultState);
            runRoot.SetAttribute("total", effectiveMetrics.Total.ToString(CultureInfo.InvariantCulture));
            runRoot.SetAttribute("passed", effectiveMetrics.Passed.ToString(CultureInfo.InvariantCulture));
            runRoot.SetAttribute("failed", effectiveMetrics.Failed.ToString(CultureInfo.InvariantCulture));
            runRoot.SetAttribute("inconclusive", effectiveMetrics.Inconclusive.ToString(CultureInfo.InvariantCulture));
            runRoot.SetAttribute("skipped", effectiveMetrics.Skipped.ToString(CultureInfo.InvariantCulture));
            runRoot.SetAttribute("asserts", effectiveMetrics.Asserts.ToString(CultureInfo.InvariantCulture));

            string existingEngineVersion = runRoot.GetAttribute("engine-version");
            runRoot.SetAttribute("engine-version", string.IsNullOrWhiteSpace(existingEngineVersion) ? NUnitResultsEngineVersion : existingEngineVersion);

            string existingClrVersion = runRoot.GetAttribute("clr-version");
            runRoot.SetAttribute("clr-version", string.IsNullOrWhiteSpace(existingClrVersion) ? Environment.Version.ToString() : existingClrVersion);
            runRoot.SetAttribute("start-time", effectiveMetrics.StartTime.ToUniversalTime().ToString(NUnitResultsTimeFormat, CultureInfo.InvariantCulture));
            runRoot.SetAttribute("end-time", effectiveMetrics.EndTime.ToUniversalTime().ToString(NUnitResultsTimeFormat, CultureInfo.InvariantCulture));
            runRoot.SetAttribute("duration", effectiveMetrics.Duration.ToString("0.#######", CultureInfo.InvariantCulture));
        }

        private static ResultMetrics ResolveEffectiveResultMetrics(ITestResultAdaptor result, XmlElement rawRoot)
        {
            ResultMetrics directMetrics = ResultMetrics.FromAdaptor(result);
            if (directMetrics.HasCounts)
                return directMetrics;

            ResultMetrics childMetrics = CollectResultMetricsFromChildren(result);
            if (childMetrics.HasCounts)
                return directMetrics.WithFallbackCounts(childMetrics);

            XmlElement xmlMetricsRoot = rawRoot == null ? null : ResolveCanonicalMetricsRoot(rawRoot);
            ResultMetrics xmlMetrics = ResultMetrics.FromXml(xmlMetricsRoot, result);
            return xmlMetrics.HasCounts
                ? directMetrics.WithFallbackCounts(xmlMetrics)
                : directMetrics;
        }

        private static ResultMetrics CollectResultMetricsFromChildren(ITestResultAdaptor result)
        {
            if (result?.Children == null)
                return ResultMetrics.Empty;

            ResultMetrics aggregate = ResultMetrics.Empty;
            bool sawChild = false;
            foreach (ITestResultAdaptor child in result.Children)
            {
                if (child == null)
                    continue;

                sawChild = true;
                ResultMetrics childMetrics = ResolveEffectiveResultMetrics(child, rawRoot: null);
                aggregate = aggregate.Accumulate(childMetrics);
            }

            return sawChild ? aggregate.FinalizeAggregatedState() : ResultMetrics.Empty;
        }

        private readonly struct ResultMetrics
        {
            public static readonly ResultMetrics Empty = new ResultMetrics(0, 0, 0, 0, 0, 0, 0d, default, default, string.Empty);

            public ResultMetrics(
                int total,
                int passed,
                int failed,
                int inconclusive,
                int skipped,
                int asserts,
                double duration,
                DateTime startTime,
                DateTime endTime,
                string resultState)
            {
                Total = total;
                Passed = passed;
                Failed = failed;
                Inconclusive = inconclusive;
                Skipped = skipped;
                Asserts = asserts;
                Duration = duration;
                StartTime = startTime;
                EndTime = endTime;
                ResultState = string.IsNullOrWhiteSpace(resultState)
                    ? DetermineResultState(failed, inconclusive, skipped, total)
                    : resultState;
            }

            public int Total { get; }
            public int Passed { get; }
            public int Failed { get; }
            public int Inconclusive { get; }
            public int Skipped { get; }
            public int Asserts { get; }
            public double Duration { get; }
            public DateTime StartTime { get; }
            public DateTime EndTime { get; }
            public string ResultState { get; }
            public bool HasCounts => Total > 0 || Passed > 0 || Failed > 0 || Inconclusive > 0 || Skipped > 0 || Asserts > 0;

            public static ResultMetrics FromAdaptor(ITestResultAdaptor result)
            {
                if (result == null)
                    return Empty;

                int total = result.PassCount + result.FailCount + result.SkipCount + result.InconclusiveCount;
                return new ResultMetrics(
                    total,
                    result.PassCount,
                    result.FailCount,
                    result.InconclusiveCount,
                    result.SkipCount,
                    result.AssertCount,
                    result.Duration,
                    result.StartTime,
                    result.EndTime,
                    string.IsNullOrWhiteSpace(result.ResultState) ? result.TestStatus.ToString() : result.ResultState);
            }

            public static ResultMetrics FromXml(XmlElement metricsRoot, ITestResultAdaptor fallbackResult)
            {
                if (metricsRoot == null)
                    return FromAdaptor(fallbackResult);

                int total = ReadIntAttribute(metricsRoot, "total");
                int passed = ReadIntAttribute(metricsRoot, "passed");
                int failed = ReadIntAttribute(metricsRoot, "failed");
                int inconclusive = ReadIntAttribute(metricsRoot, "inconclusive");
                int skipped = ReadIntAttribute(metricsRoot, "skipped");
                int asserts = ReadIntAttribute(metricsRoot, "asserts");
                double duration = ReadDoubleAttribute(metricsRoot, "duration");
                DateTime startTime = ReadDateTimeOffsetAttribute(metricsRoot, "start-time")?.UtcDateTime
                    ?? fallbackResult?.StartTime
                    ?? default;
                DateTime endTime = ReadDateTimeOffsetAttribute(metricsRoot, "end-time")?.UtcDateTime
                    ?? fallbackResult?.EndTime
                    ?? default;
                string resultState = metricsRoot.GetAttribute("result");
                if (string.IsNullOrWhiteSpace(resultState))
                    resultState = fallbackResult?.ResultState;

                return new ResultMetrics(total, passed, failed, inconclusive, skipped, asserts, duration, startTime, endTime, resultState);
            }

            public ResultMetrics WithFallbackCounts(ResultMetrics fallback)
            {
                return new ResultMetrics(
                    HasCounts ? Total : fallback.Total,
                    HasCounts ? Passed : fallback.Passed,
                    HasCounts ? Failed : fallback.Failed,
                    HasCounts ? Inconclusive : fallback.Inconclusive,
                    HasCounts ? Skipped : fallback.Skipped,
                    HasCounts ? Asserts : fallback.Asserts,
                    Duration > 0d ? Duration : fallback.Duration,
                    IsMeaningfulTime(StartTime) ? StartTime : fallback.StartTime,
                    IsMeaningfulTime(EndTime) ? EndTime : fallback.EndTime,
                    string.IsNullOrWhiteSpace(ResultState) ? fallback.ResultState : ResultState);
            }

            public ResultMetrics Accumulate(ResultMetrics other)
            {
                return new ResultMetrics(
                    Total + other.Total,
                    Passed + other.Passed,
                    Failed + other.Failed,
                    Inconclusive + other.Inconclusive,
                    Skipped + other.Skipped,
                    Asserts + other.Asserts,
                    Duration + other.Duration,
                    SelectEarlier(StartTime, other.StartTime),
                    SelectLater(EndTime, other.EndTime),
                    ResultState);
            }

            public ResultMetrics FinalizeAggregatedState()
            {
                return new ResultMetrics(Total, Passed, Failed, Inconclusive, Skipped, Asserts, Duration, StartTime, EndTime, DetermineResultState(Failed, Inconclusive, Skipped, Total));
            }

            private static DateTime SelectEarlier(DateTime left, DateTime right)
            {
                if (!IsMeaningfulTime(left))
                    return right;
                if (!IsMeaningfulTime(right))
                    return left;
                return left <= right ? left : right;
            }

            private static DateTime SelectLater(DateTime left, DateTime right)
            {
                if (!IsMeaningfulTime(left))
                    return right;
                if (!IsMeaningfulTime(right))
                    return left;
                return left >= right ? left : right;
            }

            private static bool IsMeaningfulTime(DateTime value)
            {
                return value != default;
            }

            private static string DetermineResultState(int failed, int inconclusive, int skipped, int total)
            {
                if (failed > 0)
                    return "Failed";
                if (inconclusive > 0)
                    return "Inconclusive";
                if (total == 0 && skipped > 0)
                    return "Skipped";
                return "Passed";
            }
        }

        internal static XmlDocument ConvertRawXmlObjectToDocument(object xmlObject, string ownerTypeName = null)
        {
            var document = new XmlDocument();
            switch (xmlObject)
            {
                case XmlDocument xmlDocument:
                    document.LoadXml(xmlDocument.OuterXml);
                    return document;
                case XmlNode xmlNode:
                    document.LoadXml(xmlNode.OuterXml);
                    return document;
            }

            string xmlText = ExtractXmlText(xmlObject);
            if (string.IsNullOrWhiteSpace(xmlText))
            {
                string sourceType = ownerTypeName;
                if (string.IsNullOrWhiteSpace(sourceType))
                    sourceType = xmlObject?.GetType().FullName ?? "<null>";

                throw new InvalidOperationException($"Test result adapter '{sourceType}' returned an empty XML payload.");
            }

            document.LoadXml(xmlText);
            return document;
        }

        private static string ExtractXmlText(object xmlObject)
        {
            if (xmlObject == null)
                return null;

            if (xmlObject is string xmlString)
                return xmlString;

            Type xmlType = xmlObject.GetType();
            PropertyInfo outerXmlProperty = xmlType.GetProperty("OuterXml", BindingFlags.Instance | BindingFlags.Public);
            if (outerXmlProperty?.PropertyType == typeof(string))
            {
                string outerXml = outerXmlProperty.GetValue(xmlObject) as string;
                if (!string.IsNullOrWhiteSpace(outerXml))
                    return outerXml;
            }

            MethodInfo writeToMethod = xmlType.GetMethod("WriteTo", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(XmlWriter) }, null);
            if (writeToMethod != null)
            {
                var builder = new StringBuilder();
                using (var stringWriter = new StringWriter(builder, CultureInfo.InvariantCulture))
                using (var xmlWriter = XmlWriter.Create(stringWriter, new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    ConformanceLevel = ConformanceLevel.Fragment,
                }))
                {
                    writeToMethod.Invoke(xmlObject, new object[] { xmlWriter });
                    xmlWriter.Flush();
                }

                string writtenXml = builder.ToString();
                if (!string.IsNullOrWhiteSpace(writtenXml))
                    return writtenXml;
            }

            return xmlObject.ToString();
        }

        private static void WriteXmlDocument(XmlDocument document, string resultPath)
        {
            string directory = Path.GetDirectoryName(resultPath) ?? s_resultsDirectory;
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, Path.GetFileName(resultPath) + ".tmp");
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            document.Save(tempPath);
            if (File.Exists(resultPath))
                File.Delete(resultPath);

            File.Move(tempPath, resultPath);
        }

        internal static XmlDocument BuildCanonicalResultsDocument(IReadOnlyList<XmlDocument> runDocuments)
        {
            if (runDocuments == null || runDocuments.Count == 0)
                throw new InvalidOperationException("Batch test runner requires at least one completed result document to build canonical NUnit output.");

            if (runDocuments.Count == 1)
            {
                var singleRunDocument = new XmlDocument();
                singleRunDocument.LoadXml(runDocuments[0].OuterXml);
                return singleRunDocument;
            }

            var aggregateDocument = new XmlDocument();
            aggregateDocument.AppendChild(aggregateDocument.CreateXmlDeclaration("1.0", "utf-8", null));

            XmlElement aggregateRoot = aggregateDocument.CreateElement("test-run");
            aggregateDocument.AppendChild(aggregateRoot);

            XmlElement firstRoot = runDocuments[0].DocumentElement
                ?? throw new InvalidOperationException("Batch test runner cannot aggregate a result document without a root element.");
            XmlElement firstRunRoot = ResolveCanonicalRunRoot(firstRoot);

            CopyAttributeIfPresent(firstRunRoot, aggregateRoot, "id");
            CopyAttributeIfPresent(firstRunRoot, aggregateRoot, "engine-version");
            CopyAttributeIfPresent(firstRunRoot, aggregateRoot, "clr-version");

            int testcaseCount = 0;
            int total = 0;
            int passed = 0;
            int failed = 0;
            int inconclusive = 0;
            int skipped = 0;
            int asserts = 0;
            double duration = 0d;
            DateTimeOffset? earliestStart = null;
            DateTimeOffset? latestEnd = null;

            foreach (XmlDocument runDocument in runDocuments)
            {
                XmlElement documentRoot = runDocument.DocumentElement
                    ?? throw new InvalidOperationException("Batch test runner cannot aggregate a result document without a root element.");
                XmlElement runRoot = ResolveCanonicalRunRoot(documentRoot);
                XmlElement metricsRoot = ResolveCanonicalMetricsRoot(documentRoot);

                testcaseCount += ReadEffectiveTestCaseCount(metricsRoot);
                total += ReadIntAttribute(metricsRoot, "total");
                passed += ReadIntAttribute(metricsRoot, "passed");
                failed += ReadIntAttribute(metricsRoot, "failed");
                inconclusive += ReadIntAttribute(metricsRoot, "inconclusive");
                skipped += ReadIntAttribute(metricsRoot, "skipped");
                asserts += ReadIntAttribute(metricsRoot, "asserts");
                duration += ReadDoubleAttribute(metricsRoot, "duration");

                DateTimeOffset? start = ReadDateTimeOffsetAttribute(metricsRoot, "start-time");
                if (!earliestStart.HasValue || (start.HasValue && start.Value < earliestStart.Value))
                    earliestStart = start;

                DateTimeOffset? end = ReadDateTimeOffsetAttribute(metricsRoot, "end-time");
                if (!latestEnd.HasValue || (end.HasValue && end.Value > latestEnd.Value))
                    latestEnd = end;

                foreach (XmlNode childNode in EnumerateCanonicalChildNodes(documentRoot, runRoot))
                {
                    aggregateRoot.AppendChild(aggregateDocument.ImportNode(childNode, true));
                }
            }

            aggregateRoot.SetAttribute("testcasecount", testcaseCount.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("total", total.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("passed", passed.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("failed", failed.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("inconclusive", inconclusive.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("skipped", skipped.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("asserts", asserts.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("duration", duration.ToString("0.#######", CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("result", failed > 0 ? "Failed" : inconclusive > 0 ? "Inconclusive" : "Passed");

            if (earliestStart.HasValue)
                aggregateRoot.SetAttribute("start-time", earliestStart.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture));
            if (latestEnd.HasValue)
                aggregateRoot.SetAttribute("end-time", latestEnd.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture));

            return aggregateDocument;
        }

        private static XmlElement ResolveCanonicalRunRoot(XmlElement root)
        {
            if (string.Equals(root.Name, "test-run", StringComparison.Ordinal))
                return root;

            if (string.Equals(root.Name, "test-suite", StringComparison.Ordinal))
                return root;

            throw new InvalidOperationException($"Batch test runner expected NUnit root element 'test-run' or 'test-suite' but received '{root.Name}'.");
        }

        private static XmlElement ResolveCanonicalMetricsRoot(XmlElement root)
        {
            XmlElement runRoot = ResolveCanonicalRunRoot(root);
            if (HasNonZeroResultMetrics(runRoot))
                return runRoot;

            XmlElement fallback = runRoot
                .SelectNodes(".//*[self::test-run or self::test-suite]")
                ?.Cast<XmlNode>()
                .OfType<XmlElement>()
                .Where(HasNonZeroResultMetrics)
                .OrderByDescending(element => ReadIntAttribute(element, "total"))
                .ThenByDescending(element => ReadIntAttribute(element, "passed") + ReadIntAttribute(element, "failed") + ReadIntAttribute(element, "skipped") + ReadIntAttribute(element, "inconclusive"))
                .ThenByDescending(element => ReadIntAttribute(element, "testcasecount"))
                .FirstOrDefault();

            return fallback ?? runRoot;
        }

        private static bool HasNonZeroResultMetrics(XmlElement element)
        {
            if (element == null)
                return false;

            return ReadIntAttribute(element, "total") > 0
                || ReadIntAttribute(element, "passed") > 0
                || ReadIntAttribute(element, "failed") > 0
                || ReadIntAttribute(element, "skipped") > 0
                || ReadIntAttribute(element, "inconclusive") > 0
                || ReadIntAttribute(element, "asserts") > 0;
        }

        private static int ReadEffectiveTestCaseCount(XmlElement element)
        {
            int total = ReadIntAttribute(element, "total");
            if (total > 0)
                return total;

            return ReadIntAttribute(element, "testcasecount");
        }

        private static IEnumerable<XmlNode> EnumerateCanonicalChildNodes(XmlElement documentRoot, XmlElement runRoot)
        {
            if (string.Equals(documentRoot.Name, "test-suite", StringComparison.Ordinal))
            {
                yield return documentRoot;
                yield break;
            }

            foreach (XmlNode childNode in runRoot.ChildNodes)
            {
                if (childNode.NodeType != XmlNodeType.Element)
                    continue;

                yield return childNode;
            }
        }

        private static void CopyAttributeIfPresent(XmlElement source, XmlElement destination, string attributeName)
        {
            string value = source.GetAttribute(attributeName);
            if (!string.IsNullOrWhiteSpace(value))
                destination.SetAttribute(attributeName, value);
        }

        private static int ReadIntAttribute(XmlElement element, string attributeName)
        {
            return int.TryParse(element.GetAttribute(attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        private static double ReadDoubleAttribute(XmlElement element, string attributeName)
        {
            return double.TryParse(element.GetAttribute(attributeName), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
                ? value
                : 0d;
        }

        private static DateTimeOffset? ReadDateTimeOffsetAttribute(XmlElement element, string attributeName)
        {
            return DateTimeOffset.TryParse(
                element.GetAttribute(attributeName),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset value)
                ? value
                : (DateTimeOffset?)null;
        }

        private static void WriteCanonicalResultsIfComplete()
        {
            if (s_expectedRunCount <= 0)
                return;

            if (s_completedRunResults.Count != s_expectedRunCount)
            {
                Debug.LogWarning($"[ASM-Lite] Skipping canonical NUnit result output because only {s_completedRunResults.Count} of {s_expectedRunCount} batch runs completed.");
                return;
            }

            var canonicalDocument = BuildCanonicalResultsDocument(s_completedRunResults.Select(run => run.document).ToArray());
            WriteXmlDocument(canonicalDocument, s_canonicalResultsPath);
        }

        internal static void WriteGenerationWiringSummaryArtifact(string resultsDirectory)
        {
            ASMLiteGenerationWiringSummaryWriter.Write(
                resultsDirectory,
                ASMLiteTestFixtures.GetRecordedGenerationWiringFailures());
        }

        private static void CompleteAndExit()
        {
            try
            {
                EditorApplication.update -= WaitForNextRunDelay;
                WriteCanonicalResultsIfComplete();
                WriteGenerationWiringSummaryArtifact(s_resultsDirectory);

                if (s_testRunnerApi != null && s_callbackForwarder != null)
                    s_testRunnerApi.UnregisterCallbacks(s_callbackForwarder);
            }
            catch (Exception ex)
            {
                s_exitCode = 1;
                Debug.LogException(ex);
            }
            finally
            {
                if (s_testRunnerApi != null)
                    UnityEngine.Object.DestroyImmediate(s_testRunnerApi);

                s_testRunnerApi = null;
                s_callbackForwarder = null;
                ClearPersistedSessionState();
                EditorApplication.Exit(s_exitCode);
            }
        }

        [Serializable]
        private sealed class OverlaySuiteStatus
        {
            public string suiteId;
            public string label;
            public string state;
        }

        private sealed class CompletedRunResult
        {
            public string name;
            public string suiteId;
            public string suiteLabel;
            public string resultPath;
            public XmlDocument document;
        }

        private sealed class CallbackForwarder : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                HandleRunFinished(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }

    [TestFixture]
    internal sealed class ASMLiteVisibleAutomationCommandLineTests
    {
        [TestCase(null, AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("", AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("ASMLiteVisibleEditorSmokeTests", AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("editor", AsmLiteVisibleAutomationMode.Editor)]
        [TestCase("playmode", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("ASMLiteVisiblePlayModeSmoke", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("runtime-review", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("VisibleRuntimeHarness", AsmLiteVisibleAutomationMode.PlayMode)]
        [TestCase("launch-unity", AsmLiteVisibleAutomationMode.LaunchUnity)]
        [TestCase("LaunchUnity", AsmLiteVisibleAutomationMode.LaunchUnity)]
        public void ResolveModeSelector_MapsSelectors_ToExpectedVisibleAutomationMode(string selector, AsmLiteVisibleAutomationMode expectedMode)
        {
            Assert.AreEqual(expectedMode, ASMLiteVisibleAutomationCommandLine.ResolveModeSelector(selector));
        }

        [Test]
        public void ParseConfiguration_DefaultsToEditorMode_WhenModeArgIsOmitted()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity",
                "-executeMethod",
                "ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine",
                "-asmliteVisibleAutomationResultsPath",
                "artifacts/visible-editor-smoke.xml",
                "-asmliteVisibleAutomationSelector",
                "ASMLiteVisibleEditorSmokeTests",
            });

            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.Editor, configuration.mode);
            Assert.AreEqual(Path.GetFullPath("artifacts/visible-editor-smoke.xml"), configuration.resultsPath);
            Assert.AreEqual("ASMLiteVisibleEditorSmokeTests", configuration.selector);
            Assert.AreEqual((int)AsmLiteVisibleAutomationStage.OpeningWindow, configuration.stage);
            Assert.Greater(configuration.startedUtcTicks, 0L);
        }

        [Test]
        public void ParseConfiguration_DefaultsToClickMeSceneAndOct25DressAvatar_WhenTargetArgsAreOmitted()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity",
                "-executeMethod",
                "ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine",
                "-asmliteVisibleAutomationResultsPath",
                "artifacts/visible-editor-smoke.xml",
            });

            Assert.AreEqual("Assets/Click ME.unity", configuration.scenePath);
            Assert.AreEqual("Oct25_Dress", configuration.avatarName);
        }

        [Test]
        public void ParseConfiguration_UsesExplicitStepDelayArgument_WhenProvided()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity",
                "-executeMethod",
                "ASMLite.Tests.Editor.ASMLiteVisibleAutomationCommandLine.RunFromCommandLine",
                "-asmliteVisibleAutomationResultsPath",
                "artifacts/visible-editor-smoke.xml",
                "-asmliteVisibleAutomationStepDelaySeconds",
                "2.5",
            });

            Assert.IsTrue(configuration.hasStepDelaySeconds);
            Assert.AreEqual(2.5f, configuration.stepDelaySeconds, 0.0001f);
        }

        [Test]
        public void ParseConfiguration_UsesSelectorToInferInitialModeAndStage()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity.exe",
                "-asmliteVisibleAutomationResultsPath",
                "C:/Temp/visible-playmode.xml",
                "-asmliteVisibleAutomationSelector",
                "playmode",
            });

            Assert.AreEqual(Path.GetFullPath("C:/Temp/visible-playmode.xml"), configuration.resultsPath);
            Assert.AreEqual("playmode", configuration.selector);
            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.PlayMode, configuration.mode);
            Assert.AreEqual((int)AsmLiteVisibleAutomationStage.OpeningWindow, configuration.stage);
            Assert.Greater(configuration.startedUtcTicks, 0L);
        }

        [Test]
        public void ParseConfiguration_PrefersExplicitModeArgumentOverSelectorText()
        {
            var configuration = ASMLiteVisibleAutomationCommandLine.ParseConfiguration(new[]
            {
                "Unity.exe",
                "-asmliteVisibleAutomationResultsPath",
                "C:/Temp/visible-editor.xml",
                "-asmliteVisibleAutomationSelector",
                "playmode",
                "-asmliteVisibleAutomationMode",
                "editor",
            });

            Assert.AreEqual((int)AsmLiteVisibleAutomationMode.Editor, configuration.mode);
            Assert.AreEqual("playmode", configuration.selector);
        }

        [Test]
        public void BuildResultDocument_UsesLegacyVisibleSmokeFixtureName_ForEditorMode()
        {
            var configuration = new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = "artifacts/visible-editor-smoke.xml",
                selector = "ASMLiteVisibleEditorSmokeTests",
                mode = (int)AsmLiteVisibleAutomationMode.Editor,
                startedUtcTicks = DateTime.UtcNow.AddSeconds(-5d).Ticks,
            };

            XmlDocument document = ASMLiteVisibleAutomationCommandLine.BuildResultDocument(
                configuration,
                "Passed",
                null,
                null,
                5.25d,
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero).AddSeconds(5.25d));

            XmlElement fixture = document.SelectSingleNode("/test-run/test-suite/test-suite") as XmlElement;
            XmlElement testCase = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case") as XmlElement;

            Assert.IsNotNull(fixture);
            Assert.IsNotNull(testCase);
            Assert.AreEqual("ASMLiteVisibleEditorSmokeTests", fixture.GetAttribute("name"));
            Assert.AreEqual("VisibleWindow_AddPrefab_PrimaryActionExecutesThroughRenderedWindow", testCase.GetAttribute("name"));
            Assert.AreEqual("Passed", testCase.GetAttribute("result"));
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLiteVisibleEditorSmokeTests.VisibleWindow_AddPrefab_PrimaryActionExecutesThroughRenderedWindow",
                testCase.GetAttribute("fullname"));
            Assert.IsNull(document.SelectSingleNode("/test-run/test-suite/test-suite/test-case/failure"));
        }

        [Test]
        public void BuildResultDocument_UsesLaunchUnityCaseName_ForLaunchUnityRuns()
        {
            var configuration = new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = "artifacts/visible-launch-unity.xml",
                selector = "launch-unity",
                mode = (int)AsmLiteVisibleAutomationMode.LaunchUnity,
                startedUtcTicks = DateTime.UtcNow.AddSeconds(-4d).Ticks,
            };

            XmlDocument document = ASMLiteVisibleAutomationCommandLine.BuildResultDocument(
                configuration,
                "Passed",
                null,
                null,
                4d,
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero).AddSeconds(4d));

            XmlElement fixture = document.SelectSingleNode("/test-run/test-suite/test-suite") as XmlElement;
            XmlElement testCase = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case") as XmlElement;

            Assert.IsNotNull(fixture);
            Assert.IsNotNull(testCase);
            Assert.AreEqual("ASMLiteVisibleEditorSmokeTests", fixture.GetAttribute("name"));
            Assert.AreEqual("VisibleWindow_LaunchUnity_LoadsClickMe_SelectsOct25Dress_AndWaitsForAcceptance", testCase.GetAttribute("name"));
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLiteVisibleEditorSmokeTests.VisibleWindow_LaunchUnity_LoadsClickMe_SelectsOct25Dress_AndWaitsForAcceptance",
                testCase.GetAttribute("fullname"));
        }

        [Test]
        public void BuildResultDocument_UsesPlayModeFixtureName_ForPlayModeRuns()
        {
            var configuration = new AsmLiteVisibleAutomationCommandLineConfiguration
            {
                resultsPath = "artifacts/visible-editor-smoke.xml",
                selector = "playmode",
                mode = (int)AsmLiteVisibleAutomationMode.PlayMode,
                startedUtcTicks = DateTime.UtcNow.AddSeconds(-8d).Ticks,
            };

            XmlDocument document = ASMLiteVisibleAutomationCommandLine.BuildResultDocument(
                configuration,
                "Failed",
                "PlayMode never became active.",
                "stack-trace",
                8d,
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero),
                new DateTimeOffset(configuration.startedUtcTicks, TimeSpan.Zero).AddSeconds(8d));

            XmlElement fixture = document.SelectSingleNode("/test-run/test-suite/test-suite") as XmlElement;
            XmlElement testCase = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case") as XmlElement;
            XmlElement failure = document.SelectSingleNode("/test-run/test-suite/test-suite/test-case/failure") as XmlElement;

            Assert.IsNotNull(fixture);
            Assert.IsNotNull(testCase);
            Assert.IsNotNull(failure);
            Assert.AreEqual("ASMLiteVisiblePlayModeAutomation", fixture.GetAttribute("name"));
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLiteVisiblePlayModeAutomation.VisibleWindow_AddPrefab_EntersPlayMode_AndWaitsForAcceptance",
                testCase.GetAttribute("fullname"));
            StringAssert.Contains("PlayMode never became active.", failure.InnerText);
        }
    }

    [TestFixture]
    internal sealed class ASMLiteBatchTestRunnerTests
    {
        [Test]
        public void NormalizeRun_UsesExplicitFilters_AndAppendsLegacySelectors()
        {
            var run = new AsmLiteBatchRunDefinition
            {
                name = "  mixed-run  ",
                resultFile = "  custom-results.xml  ",
                filters = new[]
                {
                    new AsmLiteBatchFilterDefinition
                    {
                        groupNames = new[] { "  ^ASMLite\\.Tests\\.Editor\\.ASMLiteBuilderTests(?:\\.|$)  " }
                    }
                },
                testNames = new[] { "  ASMLite.Tests.Editor.ASMLitePrefabWiringTests.W02_HasStalePrmsEntry_DetectsLegacyPrmsNames_AndIgnoresOtherNames  " },
            };

            var normalized = ASMLiteBatchTestRunner.NormalizeRun(run, 0);

            Assert.AreEqual("mixed-run", normalized.name);
            Assert.AreEqual("custom-results.xml", normalized.resultFile);
            Assert.AreEqual(2, normalized.filters.Length,
                "Legacy selector fields should be appended as an extra OR filter when explicit filters are present.");
            Assert.AreEqual("^ASMLite\\.Tests\\.Editor\\.ASMLiteBuilderTests(?:\\.|$)", normalized.filters[0].groupNames[0]);
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLitePrefabWiringTests.W02_HasStalePrmsEntry_DetectsLegacyPrmsNames_AndIgnoresOtherNames",
                normalized.filters[1].testNames[0]);
        }

        [Test]
        public void NormalizeRun_WhenNoSelectorsProvided_LeavesFiltersEmpty_AndReportsNoSelection()
        {
            var run = ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition(), 1);

            Assert.IsEmpty(run.filters,
                "Runs without explicit selectors should stay empty after normalization so accidental full-suite execution is rejected by default.");
            Assert.IsFalse(ASMLiteBatchTestRunner.RunHasAnySelection(run),
                "Runs without selectors should report no configured selection after normalization.");
        }

        [Test]
        public void BuildExecutionFilters_WhenNoSelectorsProvided_ReturnsSingleDefaultEditModeFilter()
        {
            var run = ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
            {
                allowEmptySelection = true,
            }, 1);

            var filters = ASMLiteBatchTestRunner.BuildExecutionFilters(run);

            Assert.AreEqual(1, filters.Length);
            Assert.AreEqual(TestMode.EditMode, filters[0].testMode);
            Assert.IsEmpty(filters[0].testNames);
            Assert.IsEmpty(filters[0].groupNames);
            Assert.IsEmpty(filters[0].categoryNames);
            Assert.IsEmpty(filters[0].assemblyNames);
        }

        public void WrapResultDocumentWithResultMetrics_WrapsSuiteXml_WithAdaptorCounts()
        {
            var rawDocument = new XmlDocument();
            rawDocument.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" result=""Failed"" testcasecount=""287"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" />");

            DateTime startTime = new DateTime(2026, 4, 19, 17, 8, 14, DateTimeKind.Utc);
            DateTime endTime = startTime.AddSeconds(1.25d);
            XmlDocument wrapped = ASMLiteBatchTestRunner.WrapResultDocumentWithResultMetrics(
                rawDocument,
                new FakeTestResultAdaptor(
                    resultState: "Failed",
                    testStatus: TestStatus.Failed,
                    passCount: 2,
                    failCount: 1,
                    skipCount: 0,
                    inconclusiveCount: 0,
                    assertCount: 4,
                    duration: 1.25d,
                    startTime: startTime,
                    endTime: endTime));

            XmlElement root = wrapped.DocumentElement;
            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("3", root.GetAttribute("testcasecount"));
            Assert.AreEqual("3", root.GetAttribute("total"));
            Assert.AreEqual("2", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("4", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-19 17:08:14Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-19 17:08:15Z", root.GetAttribute("end-time"));
            Assert.AreEqual(1,
                root.ChildNodes.Cast<XmlNode>().Count(node => node.NodeType == XmlNodeType.Element && string.Equals(node.Name, "test-suite", StringComparison.Ordinal)));
            Assert.AreEqual("ASMLite.Tests.Editor.dll", ((XmlElement)root.SelectSingleNode("test-suite"))?.GetAttribute("name"));
        }

        [Test]
        public void WrapResultDocumentWithResultMetrics_UsesChildResultMetrics_WhenTopLevelAdaptorReportsZeroCounts()
        {
            var rawDocument = new XmlDocument();
            rawDocument.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""TestSuite"" id=""1355"" name=""Saryu"" fullname=""Saryu"" runstate=""Runnable"" testcasecount=""288"" result=""Passed"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
  <test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" result=""Passed"" testcasecount=""8"" total=""8"" passed=""8"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""2"" />
</test-suite>");

            DateTime startTime = new DateTime(2026, 4, 19, 17, 17, 0, DateTimeKind.Utc);
            DateTime endTime = startTime.AddSeconds(0.75d);
            var child = new FakeTestResultAdaptor(
                resultState: "Passed",
                testStatus: TestStatus.Passed,
                passCount: 8,
                failCount: 0,
                skipCount: 0,
                inconclusiveCount: 0,
                assertCount: 2,
                duration: 0.75d,
                startTime: startTime,
                endTime: endTime);
            XmlDocument wrapped = ASMLiteBatchTestRunner.WrapResultDocumentWithResultMetrics(
                rawDocument,
                new FakeTestResultAdaptor(
                    resultState: "Passed",
                    testStatus: TestStatus.Passed,
                    passCount: 0,
                    failCount: 0,
                    skipCount: 0,
                    inconclusiveCount: 0,
                    assertCount: 0,
                    duration: 0.75d,
                    startTime: startTime,
                    endTime: endTime,
                    children: new[] { child }));

            XmlElement root = wrapped.DocumentElement;
            Assert.IsNotNull(root);
            Assert.AreEqual("8", root.GetAttribute("testcasecount"));
            Assert.AreEqual("8", root.GetAttribute("total"));
            Assert.AreEqual("8", root.GetAttribute("passed"));
            Assert.AreEqual("0", root.GetAttribute("failed"));
            Assert.AreEqual("2", root.GetAttribute("asserts"));
            Assert.AreEqual("Passed", root.GetAttribute("result"));
        }

        [Test]
        public void ConvertRawXmlObjectToDocument_UsesOuterXmlProperty_WhenToStringIsNotXml()
        {
            XmlDocument converted = ASMLiteBatchTestRunner.ConvertRawXmlObjectToDocument(
                new OuterXmlOnlyResultNode("<test-run id=\"batch\" result=\"Passed\" total=\"1\" passed=\"1\" failed=\"0\" />"),
                "FakeBatchResult");

            Assert.AreEqual("test-run", converted.DocumentElement?.Name);
            Assert.AreEqual("batch", converted.DocumentElement?.GetAttribute("id"));
            Assert.AreEqual("Passed", converted.DocumentElement?.GetAttribute("result"));
        }

        [Test]
        public void ConvertRawXmlObjectToDocument_UsesWriteTo_WhenAvailable()
        {
            XmlDocument converted = ASMLiteBatchTestRunner.ConvertRawXmlObjectToDocument(
                new WriteToOnlyResultNode("<test-run id=\"writer\" result=\"Passed\" total=\"2\" passed=\"2\" failed=\"0\" />"),
                "FakeBatchResult");

            Assert.AreEqual("test-run", converted.DocumentElement?.Name);
            Assert.AreEqual("writer", converted.DocumentElement?.GetAttribute("id"));
            Assert.AreEqual("2", converted.DocumentElement?.GetAttribute("total"));
        }

        [Test]
        public void BuildSessionStateFromCommandLine_UsesExplicitCliArguments_ForBatchTransport()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "asmlite-batch-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string planPath = Path.Combine(tempDirectory, "plan.json");
                string resultsDirectory = Path.Combine(tempDirectory, "results");
                string canonicalResultsPath = Path.Combine(tempDirectory, "canonical.xml");
                string overlayStatePath = Path.Combine(tempDirectory, "overlay", "state.payload");
                File.WriteAllText(planPath, @"{
  ""runs"": [
    {
      ""name"": ""batch-runner"",
      ""suiteId"": ""runner"",
      ""suiteLabel"": ""Batch Runner"",
      ""resultFile"": ""results/runner.xml"",
      ""filters"": [
        {
          ""testNames"": [""ASMLiteBatchTestRunnerTests""]
        }
      ]
    }
  ]
}", Encoding.UTF8);

                var sessionState = ASMLiteBatchTestRunner.BuildSessionStateFromCommandLine(new[]
                {
                    "Unity.exe",
                    "-executeMethod",
                    "ASMLite.Tests.Editor.ASMLiteBatchTestRunner.RunFromCommandLine",
                    "-asmliteBatchRunsJsonPath",
                    planPath,
                    "-asmliteBatchResultsDir",
                    resultsDirectory,
                    "-asmliteBatchCanonicalResultsPath",
                    canonicalResultsPath,
                    "-asmliteBatchOverlayStatePath",
                    overlayStatePath,
                    "-asmliteBatchStepDelaySeconds",
                    "2.5",
                    "-asmliteBatchSelectionLabel",
                    "Visible smoke suites",
                    "-asmliteBatchSessionId",
                    "visible-suite-session",
                    "-asmliteBatchOverlayTitle",
                    "ASM-Lite smoke suite overlay",
                });

                Assert.AreEqual(1, sessionState.runs.Length);
                Assert.AreEqual("batch-runner", sessionState.runs[0].name);
                Assert.AreEqual("runner", sessionState.runs[0].suiteId);
                Assert.AreEqual("Batch Runner", sessionState.runs[0].suiteLabel);
                Assert.AreEqual("results/runner.xml", sessionState.runs[0].resultFile);
                Assert.AreEqual("ASMLiteBatchTestRunnerTests", sessionState.runs[0].filters[0].testNames[0]);
                Assert.AreEqual(Path.GetFullPath(resultsDirectory), sessionState.resultsDirectory);
                Assert.AreEqual(Path.GetFullPath(canonicalResultsPath), sessionState.canonicalResultsPath);
                Assert.AreEqual(Path.GetFullPath(overlayStatePath), sessionState.overlayStatePath);
                Assert.AreEqual("Visible smoke suites", sessionState.selectionLabel);
                Assert.AreEqual("visible-suite-session", sessionState.overlaySessionId);
                Assert.AreEqual("ASM-Lite smoke suite overlay", sessionState.overlayTitle);
                Assert.AreEqual(2.5f, sessionState.stepDelaySeconds, 0.0001f);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void BuildCanonicalResultsDocument_UsesNestedAssemblyMetrics_WhenUnityWrappersReportZeroTotals()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""1"" testcasecount=""285"" result=""Passed"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" start-time=""2026-04-19 16:07:00Z"" end-time=""2026-04-19 16:07:03Z"" duration=""0.01"">
  <test-suite type=""TestSuite"" id=""1352"" name=""Saryu"" fullname=""Saryu"" result=""Passed"" testcasecount=""285"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
    <test-suite type=""Assembly"" id=""1666"" name=""ASMLite.Tests.Editor.dll"" result=""Passed"" testcasecount=""8"" total=""8"" passed=""8"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" />
  </test-suite>
</test-run>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""2"" testcasecount=""285"" result=""Failed"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" start-time=""2026-04-19 16:07:04Z"" end-time=""2026-04-19 16:07:08Z"" duration=""0.02"">
  <test-suite type=""TestSuite"" id=""2056"" name=""Saryu"" fullname=""Saryu"" result=""Failed"" testcasecount=""285"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
    <test-suite type=""Assembly"" id=""2666"" name=""ASMLite.Build.Tests.Editor.dll"" result=""Failed"" testcasecount=""3"" total=""3"" passed=""2"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""4"" />
  </test-suite>
</test-run>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("11", root.GetAttribute("testcasecount"));
            Assert.AreEqual("11", root.GetAttribute("total"));
            Assert.AreEqual("10", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("5", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual(2, root.SelectNodes("test-suite")?.Count ?? 0,
                "Canonical aggregation should preserve each imported wrapper suite while still using the nested assembly metrics for totals.");
        }

        [Test]
        public void BuildCanonicalResultsDocument_MergesCompletedRuns_IntoCanonicalNUnitOutput()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""1"" testcasecount=""2"" result=""Passed"" total=""2"" passed=""2"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" engine-version=""3.5.0.0"" clr-version=""4.0.30319.42000"" start-time=""2026-04-17 01:00:00Z"" end-time=""2026-04-17 01:00:10Z"" duration=""10.5"">
  <test-suite type=""TestSuite"" id=""11"" name=""core"" result=""Passed"" total=""2"" passed=""2"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" />
</test-run>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""2"" testcasecount=""1"" result=""Failed"" total=""1"" passed=""0"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"" engine-version=""3.5.0.0"" clr-version=""4.0.30319.42000"" start-time=""2026-04-17 01:00:05Z"" end-time=""2026-04-17 01:00:20Z"" duration=""2.25"">
  <test-suite type=""TestSuite"" id=""12"" name=""integration"" result=""Failed"" total=""1"" passed=""0"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"" />
</test-run>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("3", root.GetAttribute("testcasecount"));
            Assert.AreEqual("3", root.GetAttribute("total"));
            Assert.AreEqual("2", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("3", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-17 01:00:00Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-17 01:00:20Z", root.GetAttribute("end-time"));
            Assert.AreEqual(2, root.SelectNodes("test-suite")?.Count ?? 0,
                "Canonical NUnit output should preserve each completed run as a top-level imported suite.");
        }

        [Test]
        public void BuildCanonicalResultsDocument_AcceptsTestSuiteRootDocuments_FromUnityBatchCallbacks()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" fullname=""C:/Temp/ASMLite.Tests.Editor.dll"" runstate=""Runnable"" testcasecount=""6"" result=""Passed"" start-time=""2026-04-19 15:35:43Z"" end-time=""2026-04-19 15:35:44Z"" duration=""0.080252"" total=""6"" passed=""6"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
  <properties>
    <property name=""platform"" value=""EditMode"" />
  </properties>
</test-suite>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1663"" name=""ASMLite.Build.Tests.Editor.dll"" fullname=""C:/Temp/ASMLite.Build.Tests.Editor.dll"" runstate=""Runnable"" testcasecount=""7"" result=""Failed"" start-time=""2026-04-19 15:36:10Z"" end-time=""2026-04-19 15:36:12Z"" duration=""0.125"" total=""7"" passed=""6"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"">
  <failure>
    <message><![CDATA[One test failed.]]></message>
  </failure>
</test-suite>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("13", root.GetAttribute("testcasecount"));
            Assert.AreEqual("13", root.GetAttribute("total"));
            Assert.AreEqual("12", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("2", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-19 15:35:43Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-19 15:36:12Z", root.GetAttribute("end-time"));
            Assert.AreEqual(2,
                root.ChildNodes.Cast<XmlNode>().Count(node => node.NodeType == XmlNodeType.Element && string.Equals(node.Name, "test-suite", StringComparison.Ordinal)),
                "Canonical NUnit output should preserve each test-suite root as a top-level imported suite when Unity returns suite-root result fragments.");
            Assert.AreEqual("ASMLite.Tests.Editor.dll", root.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().FirstOrDefault()?.GetAttribute("name"));
            Assert.AreEqual("ASMLite.Build.Tests.Editor.dll", root.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Skip(1).FirstOrDefault()?.GetAttribute("name"));
        }

        [Test]
        public void RestoreFromSessionState_RequeuesActiveRun_AndLoadsCompletedResultsFromDisk()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "asmlite-batch-resume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var runs = new[]
                {
                    ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
                    {
                        name = "run-1",
                        suiteId = "suite-a",
                        suiteLabel = "Suite A",
                        resultFile = "run-1.xml",
                        allowEmptySelection = true,
                    }, 0),
                    ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
                    {
                        name = "run-2",
                        suiteId = "suite-b",
                        suiteLabel = "Suite B",
                        resultFile = "run-2.xml",
                        allowEmptySelection = true,
                    }, 1),
                };

                var completedDocument = new XmlDocument();
                completedDocument.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" result=""Passed"" testcasecount=""1"" total=""1"" passed=""1"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" start-time=""2026-04-19 15:35:43Z"" end-time=""2026-04-19 15:35:44Z"" duration=""0.08"" />");
                completedDocument.Save(Path.Combine(tempDirectory, "run-1.xml"));

                var sessionState = new AsmLiteBatchRunnerSessionState
                {
                    runs = runs,
                    resultsDirectory = tempDirectory,
                    canonicalResultsPath = Path.Combine(tempDirectory, "editmode-results.xml"),
                    selectionLabel = "UAT checklist smoke suites",
                    overlaySessionId = "resume-session",
                    overlayTitle = "ASM-Lite UAT smoke suites",
                    stepDelaySeconds = 1.5f,
                    nextRunIndex = 1,
                    expectedRunCount = 2,
                    exitCode = 0,
                    activeRunIndex = 1,
                };

                InvokeBatchRunnerPrivateMethod("ResetStaticState");
                InvokeBatchRunnerPrivateMethod("RestoreFromSessionState", sessionState);

                var pendingRuns = (Queue<AsmLiteBatchRunDefinition>)GetBatchRunnerPrivateField("s_pendingRuns");
                int completedRunCount = (int)GetBatchRunnerPrivateField("s_completedRunCount");
                int expectedRunCount = (int)GetBatchRunnerPrivateField("s_expectedRunCount");
                int nextRunIndex = (int)GetBatchRunnerPrivateField("s_nextRunIndex");
                int activeRunIndex = (int)GetBatchRunnerPrivateField("s_activeRunIndex");
                int completedResultsCount = ((System.Collections.ICollection)GetBatchRunnerPrivateField("s_completedRunResults")).Count;

                Assert.AreEqual(1, completedRunCount);
                Assert.AreEqual(2, expectedRunCount);
                Assert.AreEqual(1, nextRunIndex,
                    "Resuming after a reload should preserve the next run index so the current suite restarts instead of starting over.");
                Assert.AreEqual(-1, activeRunIndex,
                    "Reload restore should leave no active run until the next EditorApplication tick starts it again.");
                Assert.AreEqual(1, completedResultsCount,
                    "Completed result XML should be reloaded from disk so the canonical merge still has the earlier runs available after reload.");
                Assert.AreEqual(1, pendingRuns.Count);
                Assert.AreEqual("run-2", pendingRuns.Peek().name,
                    "The in-flight run should be requeued first after a visible editor domain reload.");
            }
            finally
            {
                InvokeBatchRunnerPrivateMethod("ResetStaticState");
                SessionState.EraseString("ASMLite.BatchTestRunner.SessionState");
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private static object GetBatchRunnerPrivateField(string fieldName)
        {
            return typeof(ASMLiteBatchTestRunner)
                .GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null);
        }

        private static void InvokeBatchRunnerPrivateMethod(string methodName, params object[] arguments)
        {
            typeof(ASMLiteBatchTestRunner)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, arguments);
        }

        private sealed class FakeTestResultAdaptor : ITestResultAdaptor
        {
            public FakeTestResultAdaptor(
                string resultState,
                TestStatus testStatus,
                int passCount,
                int failCount,
                int skipCount,
                int inconclusiveCount,
                int assertCount,
                double duration,
                DateTime startTime,
                DateTime endTime,
                IEnumerable<ITestResultAdaptor> children = null)
            {
                ResultState = resultState;
                TestStatus = testStatus;
                PassCount = passCount;
                FailCount = failCount;
                SkipCount = skipCount;
                InconclusiveCount = inconclusiveCount;
                AssertCount = assertCount;
                Duration = duration;
                StartTime = startTime;
                EndTime = endTime;
                Children = children?.ToArray() ?? Array.Empty<ITestResultAdaptor>();
            }

            public ITestAdaptor Test => null;
            public string Name => "FakeResult";
            public string FullName => "ASMLite.Tests.Editor.FakeResult";
            public string ResultState { get; }
            public TestStatus TestStatus { get; }
            public double Duration { get; }
            public DateTime StartTime { get; }
            public DateTime EndTime { get; }
            public string Message => string.Empty;
            public string StackTrace => string.Empty;
            public int AssertCount { get; }
            public int FailCount { get; }
            public int PassCount { get; }
            public int SkipCount { get; }
            public int InconclusiveCount { get; }
            public bool HasChildren => false;
            public IEnumerable<ITestResultAdaptor> Children { get; }
            public string Output => string.Empty;

            public NUnit.Framework.Interfaces.TNode ToXml()
            {
                throw new NotSupportedException("FakeTestResultAdaptor.ToXml should not be used in this regression test.");
            }
        }

        private sealed class OuterXmlOnlyResultNode
        {
            public OuterXmlOnlyResultNode(string outerXml)
            {
                OuterXml = outerXml;
            }

            public string OuterXml { get; }

            public override string ToString() => "OuterXmlOnlyResultNode";
        }

        private sealed class WriteToOnlyResultNode
        {
            private readonly XmlDocument _document;

            public WriteToOnlyResultNode(string outerXml)
            {
                _document = new XmlDocument();
                _document.LoadXml(outerXml);
            }

            public void WriteTo(XmlWriter writer)
            {
                _document.DocumentElement?.WriteTo(writer);
            }

            public override string ToString() => "WriteToOnlyResultNode";
        }
    }
}
