using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using ASMLite.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    internal sealed class ASMLiteSmokeOverlayHostConfiguration
    {
        internal string SessionRootPath;
        internal string CatalogPath;
        internal string ScenePath;
        internal string AvatarName;
        internal int StartupTimeoutSeconds;
        internal int HeartbeatSeconds;
        internal bool ExitOnReady;
    }

    internal static class ASMLiteSmokeOverlayHostCommandLine
    {
        private const string SessionRootArg = "-asmliteSmokeSessionRoot";
        private const string CatalogPathArg = "-asmliteSmokeCatalogPath";
        private const string ScenePathArg = "-asmliteSmokeScenePath";
        private const string AvatarNameArg = "-asmliteSmokeAvatarName";
        private const string StartupTimeoutArg = "-asmliteSmokeStartupTimeoutSeconds";
        private const string HeartbeatArg = "-asmliteSmokeHeartbeatSeconds";
        private const string ExitOnReadyArg = "-asmliteSmokeExitOnReady";

        internal const string DefaultScenePath = "Assets/Click ME.unity";
        internal const string DefaultAvatarName = "Oct25_Dress";
        internal const int DefaultStartupTimeoutSeconds = 120;
        internal const int DefaultHeartbeatSeconds = 5;

        internal static ASMLiteSmokeOverlayHostConfiguration ParseConfiguration(string[] commandLineArgs)
        {
            if (commandLineArgs == null)
                throw new ArgumentNullException(nameof(commandLineArgs));

            string sessionRootRaw = GetCommandLineValue(commandLineArgs, SessionRootArg);
            string catalogPathRaw = GetCommandLineValue(commandLineArgs, CatalogPathArg);
            string scenePathRaw = GetCommandLineValue(commandLineArgs, ScenePathArg);
            string avatarNameRaw = GetCommandLineValue(commandLineArgs, AvatarNameArg);
            string startupTimeoutRaw = GetCommandLineValue(commandLineArgs, StartupTimeoutArg);
            string heartbeatRaw = GetCommandLineValue(commandLineArgs, HeartbeatArg);
            string exitOnReadyRaw = GetCommandLineValue(commandLineArgs, ExitOnReadyArg);

            if (string.IsNullOrWhiteSpace(sessionRootRaw))
                throw new InvalidOperationException($"Missing required command-line argument '{SessionRootArg}'.");
            if (string.IsNullOrWhiteSpace(catalogPathRaw))
                throw new InvalidOperationException($"Missing required command-line argument '{CatalogPathArg}'.");

            return new ASMLiteSmokeOverlayHostConfiguration
            {
                SessionRootPath = Path.GetFullPath(sessionRootRaw.Trim()),
                CatalogPath = Path.GetFullPath(catalogPathRaw.Trim()),
                ScenePath = string.IsNullOrWhiteSpace(scenePathRaw) ? DefaultScenePath : scenePathRaw.Trim(),
                AvatarName = string.IsNullOrWhiteSpace(avatarNameRaw) ? DefaultAvatarName : avatarNameRaw.Trim(),
                StartupTimeoutSeconds = ParsePositiveIntOrDefault(startupTimeoutRaw, StartupTimeoutArg, DefaultStartupTimeoutSeconds),
                HeartbeatSeconds = ParsePositiveIntOrDefault(heartbeatRaw, HeartbeatArg, DefaultHeartbeatSeconds),
                ExitOnReady = ParseBoolOrDefault(exitOnReadyRaw, ExitOnReadyArg, defaultValue: false),
            };
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

        private static int ParsePositiveIntOrDefault(string rawValue, string argumentName, int defaultValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return defaultValue;

            if (!int.TryParse(rawValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                || parsed < 1)
            {
                throw new InvalidOperationException($"{argumentName} must be an integer >= 1.");
            }

            return parsed;
        }

        private static bool ParseBoolOrDefault(string rawValue, string argumentName, bool defaultValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return defaultValue;

            string token = rawValue.Trim();
            if (string.Equals(token, "true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (string.Equals(token, "false", StringComparison.OrdinalIgnoreCase))
                return false;

            throw new InvalidOperationException($"{argumentName} must be true or false.");
        }
    }

    internal interface IASMLiteSmokeOverlayHostRuntime
    {
        string GetActiveScenePath();
        void OpenScene(string scenePath);
        VRCAvatarDescriptor FindAvatarByName(string avatarName);
        void SelectAvatarForAutomation(VRCAvatarDescriptor avatar);
        void CloseAutomationWindowIfOpen();
        double GetTimeSinceStartup();
        string GetUtcNowIso();
        string GetUnityVersion();
        void RegisterUpdate(EditorApplication.CallbackFunction tick);
        void UnregisterUpdate(EditorApplication.CallbackFunction tick);
        string[] EnumerateCommandFiles(string commandsDirectoryPath);
        string ReadAllText(string path);
        bool ApplySetupFixtureMutation(
            ASMLiteSmokeStepArgs args,
            string defaultScenePath,
            string defaultAvatarName,
            string evidenceRootPath,
            out string detail,
            out string stackTrace);
        bool ResetSetupFixture(out string detail, out string stackTrace);
        bool ExecuteCatalogStep(
            string actionType,
            ASMLiteSmokeStepArgs args,
            string scenePath,
            string avatarName,
            out string detail,
            out string stackTrace);
        void StartConsoleErrorCapture();
        void StopConsoleErrorCapture();
        int GetConsoleErrorCheckpoint();
        bool TryGetConsoleErrorsSince(int checkpoint, out string detail, out string stackTrace);
        void ExitEditorWithoutSaving(int exitCode);
    }

    internal sealed class ASMLiteSmokeOverlayHostUnityRuntime : IASMLiteSmokeOverlayHostRuntime
    {
        internal static readonly ASMLiteSmokeOverlayHostUnityRuntime Instance = new ASMLiteSmokeOverlayHostUnityRuntime();

        private readonly List<ConsoleErrorRecord> _consoleErrors = new List<ConsoleErrorRecord>();
        private readonly ASMLiteSmokeSetupFixtureService _fixtureService = new ASMLiteSmokeSetupFixtureService();
        private bool _consoleErrorCaptureActive;

        private ASMLiteSmokeOverlayHostUnityRuntime()
        {
        }

        private sealed class ConsoleErrorRecord
        {
            internal string Message;
            internal string StackTrace;
            internal LogType Type;
        }

        public string GetActiveScenePath()
        {
            return SceneManager.GetActiveScene().path ?? string.Empty;
        }

        public void OpenScene(string scenePath)
        {
            EditorSceneManager.OpenScene(scenePath);
        }

        public VRCAvatarDescriptor FindAvatarByName(string avatarName)
        {
            string normalizedName = NormalizeUnityRuntimeName(avatarName);
            if (string.IsNullOrWhiteSpace(normalizedName))
                return null;

            var avatars = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>();
            VRCAvatarDescriptor fallback = null;
            VRCAvatarDescriptor activeSceneFallback = null;
            VRCAvatarDescriptor componentFallback = null;
            Scene activeScene = SceneManager.GetActiveScene();

            for (int i = 0; i < avatars.Length; i++)
            {
                VRCAvatarDescriptor avatar = avatars[i];
                if (!IsRuntimeSceneAvatarMatch(avatar, normalizedName))
                    continue;

                fallback ??= avatar;

                if (avatar.gameObject.scene == activeScene)
                    activeSceneFallback ??= avatar;

                if (avatar.GetComponentInChildren<ASMLiteComponent>(true) != null)
                {
                    componentFallback ??= avatar;
                    if (avatar.gameObject.scene == activeScene)
                        return avatar;
                }
            }

            return componentFallback ?? activeSceneFallback ?? fallback;
        }

        internal static bool TryResolveAvatarForSelection(string avatarName, out VRCAvatarDescriptor avatar, out string detail)
        {
            avatar = null;
            string normalizedName = NormalizeUnityRuntimeName(avatarName);

            GameObject selectedObject = GetSelectedGameObject();
            if (selectedObject != null)
            {
                VRCAvatarDescriptor selectedAvatar = selectedObject.GetComponent<VRCAvatarDescriptor>();
                if (selectedAvatar != null && EditorUtility.IsPersistent(selectedObject))
                {
                    detail = $"SETUP_AVATAR_PREFAB_ASSET: selected avatar target is a prefab asset, not a scene avatar instance: '{selectedObject.name}'.";
                    return false;
                }

                if (selectedAvatar == null || !IsLoadedSceneObject(selectedObject))
                {
                    detail = $"SETUP_SELECTED_OBJECT_NOT_AVATAR: selected object is not a valid avatar target: '{selectedObject.name}'.";
                    return false;
                }

                avatar = selectedAvatar;
                detail = $"Resolved selected avatar '{selectedObject.name}' for setup automation.";
                return true;
            }

            var matches = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>()
                .Where(item => IsRuntimeSceneAvatarMatch(item, normalizedName) && item.gameObject.activeInHierarchy)
                .ToArray();

            if (matches.Length == 1)
            {
                avatar = matches[0];
                detail = $"Resolved avatar '{avatar.gameObject.name}' found by fixture name '{normalizedName}'.";
                return true;
            }

            if (matches.Length > 1)
            {
                detail = $"SETUP_AVATAR_AMBIGUOUS: Multiple avatar descriptors named '{normalizedName}' were found; select one avatar to disambiguate.";
                return false;
            }

            detail = $"SETUP_AVATAR_NOT_FOUND: avatar could not be found for fixture name '{normalizedName}'.";
            return false;
        }

        private static GameObject GetSelectedGameObject()
        {
            UnityEngine.Object selected = Selection.activeObject;
            if (selected == null)
                return null;

            if (selected is GameObject gameObject)
                return gameObject;

            if (selected is Component component)
                return component.gameObject;

            return null;
        }

        private static bool IsLoadedSceneObject(GameObject gameObject)
        {
            if (gameObject == null || EditorUtility.IsPersistent(gameObject))
                return false;

            Scene scene = gameObject.scene;
            return scene.IsValid() && scene.isLoaded;
        }

        private static bool IsRuntimeSceneAvatarMatch(VRCAvatarDescriptor avatar, string normalizedName)
        {
            if (avatar == null || avatar.gameObject == null)
                return false;

            GameObject avatarObject = avatar.gameObject;
            if (!IsLoadedSceneObject(avatarObject))
                return false;

            return string.Equals(NormalizeUnityRuntimeName(avatarObject.name), normalizedName, StringComparison.Ordinal);
        }

        internal static string NormalizeUnityRuntimeName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return string.Empty;

            string normalized = objectName.Trim();
            const string cloneSuffix = "(Clone)";
            while (normalized.EndsWith(cloneSuffix, StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - cloneSuffix.Length).TrimEnd();
            return normalized;
        }

        public void SelectAvatarForAutomation(VRCAvatarDescriptor avatar)
        {
            var window = ASMLiteWindow.OpenForAutomation();
            window.SelectAvatarForAutomation(avatar);
        }

        public void CloseAutomationWindowIfOpen()
        {
            var windows = Resources.FindObjectsOfTypeAll<ASMLiteWindow>();
            if (windows == null)
                return;

            for (int i = 0; i < windows.Length; i++)
            {
                var window = windows[i];
                if (window != null)
                    window.Close();
            }
        }

        public double GetTimeSinceStartup()
        {
            return EditorApplication.timeSinceStartup;
        }

        public string GetUtcNowIso()
        {
            return DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        }

        public string GetUnityVersion()
        {
            return Application.unityVersion;
        }

        public void RegisterUpdate(EditorApplication.CallbackFunction tick)
        {
            EditorApplication.update += tick;
        }

        public void UnregisterUpdate(EditorApplication.CallbackFunction tick)
        {
            EditorApplication.update -= tick;
        }

        public string[] EnumerateCommandFiles(string commandsDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(commandsDirectoryPath) || !Directory.Exists(commandsDirectoryPath))
                return Array.Empty<string>();

            return Directory.GetFiles(commandsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly);
        }

        public string ReadAllText(string path)
        {
            return File.ReadAllText(path);
        }

        public bool ApplySetupFixtureMutation(
            ASMLiteSmokeStepArgs args,
            string defaultScenePath,
            string defaultAvatarName,
            string evidenceRootPath,
            out string detail,
            out string stackTrace)
        {
            stackTrace = string.Empty;
            return _fixtureService.ApplyMutation(args, defaultScenePath, defaultAvatarName, evidenceRootPath, out detail);
        }

        public bool ResetSetupFixture(out string detail, out string stackTrace)
        {
            stackTrace = string.Empty;
            return _fixtureService.Reset(out detail);
        }

        public bool ExecuteCatalogStep(
            string actionType,
            ASMLiteSmokeStepArgs args,
            string scenePath,
            string avatarName,
            out string detail,
            out string stackTrace)
        {
            detail = string.Empty;
            stackTrace = string.Empty;

            try
            {
                string normalizedAction = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim();
                ASMLiteWindow window;

                switch (normalizedAction)
                {
                    case "open-scene":
                        if (!TryValidateScenePath(scenePath, out detail))
                            return false;
                        if (!string.Equals(GetActiveScenePath(), scenePath, StringComparison.Ordinal))
                            OpenScene(scenePath);
                        detail = $"Opened scene '{scenePath}'.";
                        return true;

                    case "open-window":
                        window = ASMLiteWindow.OpenForAutomation();
                        detail = window == null
                            ? "ASM-Lite window could not be opened for automation."
                            : "ASM-Lite window opened and focused for automation.";
                        return window != null;

                    case "close-window":
                        CloseAutomationWindowIfOpen();
                        detail = "ASM-Lite automation window closed if present.";
                        return true;

                    case "assert-window-focused":
                        return AssertWindowFocused(out detail);

                    case "assert-package-resource-present":
                        return AssertPackageResourcePresent(args, out detail);

                    case "assert-catalog-loads":
                        return AssertCatalogLoads(out detail);

                    case "select-avatar":
                    {
                        window = ASMLiteWindow.OpenForAutomation();
                        if (!TryResolveAvatarForSelection(avatarName, out VRCAvatarDescriptor avatar, out detail))
                            return false;

                        window.SelectAvatarForAutomation(avatar);
                        return true;
                    }

                    case "add-prefab":
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        window.AddPrefabForAutomation();
                        detail = "ASM-Lite prefab added.";
                        return true;

                    case "rebuild":
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        window.RebuildForAutomation();
                        detail = "Rebuild completed.";
                        return true;

                    case "vendorize":
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        window.VendorizeForAutomation();
                        detail = "Vendorize completed.";
                        return true;

                    case "detach":
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        window.DetachForAutomation();
                        detail = "Detach completed.";
                        return true;

                    case "lifecycle-hygiene-cleanup":
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        window.ReturnToPackageManagedForAutomation();
                        detail = "Hygiene cleanup completed: known ASM-Lite lifecycle state returned to package-managed baseline.";
                        return true;

                    case "return-to-package-managed":
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        window.ReturnToPackageManagedForAutomation();
                        detail = "Return-to-package-managed completed.";
                        return true;

                    case "assert-primary-action":
                    {
                        window = ASMLiteWindow.OpenForAutomation();
                        SelectAvatarIfFound(window, avatarName);
                        var hierarchy = window.GetActionHierarchyContract();
                        var expectedAction = ResolveExpectedPrimaryAction(args);
                        if (!hierarchy.HasPrimaryAction(expectedAction))
                        {
                            detail = $"Primary action was not {FormatPrimaryAction(expectedAction)}.";
                            return false;
                        }

                        detail = $"Primary action is {FormatPrimaryAction(expectedAction)}.";
                        return true;
                    }

                    case "assert-generated-references-package-managed":
                    {
                        VRCAvatarDescriptor avatar = FindAvatarByName(avatarName);
                        return AssertGeneratedReferencesPackageManaged(avatarName, avatar, out detail);
                    }

                    case "enter-playmode":
                        EditorApplication.isPlaying = true;
                        detail = "Entered playmode.";
                        return true;

                    case "assert-runtime-component-valid":
                    {
                        VRCAvatarDescriptor avatar = FindAvatarByName(avatarName);
                        return ValidateRuntimeComponentState(avatarName, avatar, EditorApplication.isPlaying, out detail);
                    }

                    case "exit-playmode":
                        EditorApplication.isPlaying = false;
                        detail = "Exited playmode.";
                        return true;

                    case "assert-host-ready":
                        detail = "Host readiness check passed.";
                        return true;

                    default:
                        detail = $"Unsupported smoke actionType '{normalizedAction}'.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                detail = string.IsNullOrWhiteSpace(ex.Message) ? "Step execution failed." : ex.Message.Trim();
                stackTrace = string.IsNullOrWhiteSpace(ex.StackTrace) ? string.Empty : ex.StackTrace.Trim();
                return false;
            }
        }

        private static bool TryValidateScenePath(string scenePath, out string detail)
        {
            string normalizedPath = string.IsNullOrWhiteSpace(scenePath) ? string.Empty : scenePath.Trim();
            if (!string.Equals(Path.GetExtension(normalizedPath), ".unity", StringComparison.OrdinalIgnoreCase))
            {
                detail = $"SETUP_SCENE_PATH_INVALID: configured scene path is not a Unity scene: {normalizedPath}";
                return false;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(normalizedPath) == null)
            {
                detail = $"SETUP_SCENE_MISSING: configured scene could not be found at {normalizedPath}";
                return false;
            }

            detail = string.Empty;
            return true;
        }

        private static bool AssertPackageResourcePresent(ASMLiteSmokeStepArgs args, out string detail)
        {
            const string defaultPrefabPath = "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab";
            string prefabPath = args == null || string.IsNullOrWhiteSpace(args.objectName)
                ? defaultPrefabPath
                : args.objectName.Trim();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                detail = $"SETUP_PACKAGE_RESOURCE_MISSING: ASM-Lite prefab source was not found at {prefabPath}.";
                return false;
            }

            detail = $"Package resource check passed: ASM-Lite.prefab resolved at {prefabPath}.";
            return true;
        }

        private static bool AssertCatalogLoads(out string detail)
        {
            ASMLiteSmokeCatalogDocument catalog = ASMLiteSmokeCatalog.LoadCanonical();
            int suiteCount = catalog.groups.Sum(group => group.suites.Length);
            detail = $"Loaded canonical smoke catalog with {catalog.groups.Length} groups and {suiteCount} suites.";
            return true;
        }

        private static ASMLiteWindow.AsmLiteWindowAction ResolveExpectedPrimaryAction(ASMLiteSmokeStepArgs args)
        {
            string expected = args?.expectedPrimaryAction;
            if (string.IsNullOrWhiteSpace(expected))
                return ASMLiteWindow.AsmLiteWindowAction.Rebuild;

            switch (expected.Trim())
            {
                case "Add Prefab":
                case "AddPrefab":
                case "add-prefab":
                    return ASMLiteWindow.AsmLiteWindowAction.AddPrefab;
                case "Rebuild":
                case "rebuild":
                    return ASMLiteWindow.AsmLiteWindowAction.Rebuild;
                case "Return to Package Managed":
                case "ReturnToPackageManaged":
                case "return-to-package-managed":
                    return ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged;
                default:
                    throw new InvalidOperationException($"Unknown expected primary action: {expected}");
            }
        }

        private static string FormatPrimaryAction(ASMLiteWindow.AsmLiteWindowAction action)
        {
            switch (action)
            {
                case ASMLiteWindow.AsmLiteWindowAction.AddPrefab:
                    return "Add Prefab";
                case ASMLiteWindow.AsmLiteWindowAction.Rebuild:
                    return "Rebuild";
                case ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged:
                    return "Return to Package Managed";
                default:
                    return action.ToString();
            }
        }

        private static bool AssertWindowFocused(out string detail)
        {
            var focused = EditorWindow.focusedWindow as ASMLiteWindow;
            if (focused != null)
            {
                detail = "ASM-Lite window is focused for automation.";
                return true;
            }

            ASMLiteWindow availableWindow = Resources.FindObjectsOfTypeAll<ASMLiteWindow>().FirstOrDefault(window => window != null);
            if (availableWindow == null)
            {
                detail = "ASM-Lite window is not focused for automation because no automation window is open.";
                return false;
            }

            detail = "ASM-Lite window is available for automation; focus could not be observed in this editor mode.";
            return true;
        }

        internal static bool ValidateRuntimeComponentState(string avatarName, VRCAvatarDescriptor avatar, bool isPlaying, out string detail)
        {
            if (avatar == null)
            {
                detail = $"Runtime component check failed: avatar '{avatarName}' was not found in the loaded scene.";
                return false;
            }

            ASMLiteComponent component = avatar.GetComponentInChildren<ASMLiteComponent>(true);
            if (component != null)
            {
                detail = $"Runtime component check passed on avatar '{avatar.gameObject.name}' via component '{component.gameObject.name}'.";
                return true;
            }

            if (isPlaying)
            {
                detail = $"Runtime component check passed on avatar '{avatar.gameObject.name}': ASM-Lite editor component was stripped for playmode as expected.";
                return true;
            }

            detail = BuildRuntimeComponentFailureDetail(avatarName, avatar);
            return false;
        }

        private static bool AssertGeneratedReferencesPackageManaged(string avatarName, VRCAvatarDescriptor avatar, out string detail)
        {
            if (avatar == null)
            {
                detail = $"Generated reference check failed: avatar '{avatarName}' was not found in the loaded scene.";
                return false;
            }

            ASMLite.ASMLiteComponent component = avatar.GetComponentInChildren<ASMLite.ASMLiteComponent>(true);
            if (component == null)
            {
                detail = $"Generated reference check failed: ASM-Lite component was not found under avatar '{avatar.gameObject.name}'.";
                return false;
            }

            if (component.useVendorizedGeneratedAssets)
            {
                detail = "Generated reference check failed: component is configured to use vendorized generated assets.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(component.vendorizedGeneratedAssetsPath))
            {
                detail = $"Generated reference check failed: vendorized generated assets path is set to '{component.vendorizedGeneratedAssetsPath.Trim()}'.";
                return false;
            }

            detail = "Generated references are package-managed by default.";
            return true;
        }

        private static string BuildRuntimeComponentFailureDetail(string avatarName, VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return $"Runtime component check failed: avatar '{avatarName}' was not found in the loaded scene.";

            string scenePath = avatar.gameObject.scene.IsValid()
                ? avatar.gameObject.scene.path
                : string.Empty;
            string sceneLabel = string.IsNullOrWhiteSpace(scenePath) ? "unsaved scene" : scenePath;
            return $"Runtime component check failed: ASM-Lite component was not found under avatar '{avatar.gameObject.name}' in {sceneLabel}.";
        }

        private void SelectAvatarIfFound(ASMLiteWindow window, string avatarName)
        {
            if (window == null)
                return;

            VRCAvatarDescriptor avatar = FindAvatarByName(avatarName);
            if (avatar != null)
                window.SelectAvatarForAutomation(avatar);
        }

        public void StartConsoleErrorCapture()
        {
            if (_consoleErrorCaptureActive)
                return;

            _consoleErrors.Clear();
            Application.logMessageReceived += CaptureConsoleLog;
            _consoleErrorCaptureActive = true;
        }

        public void StopConsoleErrorCapture()
        {
            if (!_consoleErrorCaptureActive)
                return;

            Application.logMessageReceived -= CaptureConsoleLog;
            _consoleErrorCaptureActive = false;
        }

        public int GetConsoleErrorCheckpoint()
        {
            return _consoleErrors.Count;
        }

        public bool TryGetConsoleErrorsSince(int checkpoint, out string detail, out string stackTrace)
        {
            detail = string.Empty;
            stackTrace = string.Empty;

            int startIndex = Math.Max(0, Math.Min(checkpoint, _consoleErrors.Count));
            if (startIndex >= _consoleErrors.Count)
                return false;

            var errors = _consoleErrors.Skip(startIndex).ToArray();
            if (errors.Length == 0)
                return false;

            detail = string.Join("\n", errors.Select(item => $"Unity console {item.Type}: {item.Message}"));
            stackTrace = string.Join("\n\n", errors
                .Select(item => item.StackTrace)
                .Where(item => !string.IsNullOrWhiteSpace(item)));
            return true;
        }

        private void CaptureConsoleLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
                return;

            _consoleErrors.Add(new ConsoleErrorRecord
            {
                Message = string.IsNullOrWhiteSpace(condition) ? "Unity console error." : condition.Trim(),
                StackTrace = string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace.Trim(),
                Type = type,
            });
        }

        public void ExitEditorWithoutSaving(int exitCode)
        {
            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"ASM-Lite smoke host could not discard open scenes before exit: {exception.Message}");
            }

            EditorApplication.Exit(exitCode);
        }
    }

    internal static class ASMLiteSmokeOverlayHost
    {
        private static ASMLiteSmokeOverlayHostRunner s_activeRunner;

        public static void RunFromCommandLine()
        {
            ASMLiteSmokeOverlayHostConfiguration configuration = null;
            try
            {
                configuration = ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(Environment.GetCommandLineArgs());
                Start(configuration, ASMLiteSmokeOverlayHostUnityRuntime.Instance);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                if (configuration != null && configuration.ExitOnReady)
                    throw;
            }
        }

        internal static ASMLiteSmokeOverlayHostRunner Start(
            ASMLiteSmokeOverlayHostConfiguration configuration,
            IASMLiteSmokeOverlayHostRuntime runtime)
        {
            s_activeRunner?.StopForTesting();
            var runner = new ASMLiteSmokeOverlayHostRunner(configuration, runtime);
            s_activeRunner = runner;
            runner.Start();
            return runner;
        }

        internal static ASMLiteSmokeOverlayHostRunner CreateRunnerForTesting(
            ASMLiteSmokeOverlayHostConfiguration configuration,
            IASMLiteSmokeOverlayHostRuntime runtime)
        {
            return new ASMLiteSmokeOverlayHostRunner(configuration, runtime);
        }
    }

    internal sealed class ASMLiteSmokeOverlayHostRunner
    {
        private const double PostLifecycleMutationSettleSeconds = 0.25d;

        private readonly ASMLiteSmokeOverlayHostConfiguration _configuration;
        private readonly IASMLiteSmokeOverlayHostRuntime _runtime;

        private ASMLiteSmokeSessionPaths _paths;
        private HashSet<string> _processedCommandIds = new HashSet<string>(StringComparer.Ordinal);

        private string _sessionId = "smoke-session";
        private int _lastEventSeq;
        private int _lastCommandSeq;
        private int _runOrdinal;
        private string _currentState = ASMLiteSmokeProtocol.HostStateIdle;
        private string _currentMessage = "Unity host booting.";
        private bool _isRunning;
        private bool _isUpdateRegistered;
        private double _lastHeartbeatWriteAt;
        private ActiveRunState _activeRun;

        private static readonly string[] s_reviewDecisionOptions =
        {
            "return-to-suite-list",
            "rerun-suite",
            "exit",
        };

        private enum ActiveRunPhase
        {
            SuiteStart,
            CaseStart,
            StepStart,
            StepExecute,
            StepSettle,
            SuitePassed,
            SuiteFailed,
            Complete,
        }

        private sealed class ActiveRunState
        {
            internal ASMLiteSmokeCatalogDocument Catalog;
            internal ASMLiteSmokeSuiteDefinition Suite;
            internal ASMLiteSmokeProtocolCommand Command;
            internal ASMLiteSmokeSuiteExecutionResult Result;
            internal string EffectiveResetPolicy;
            internal int RunOrdinal;
            internal string StartedAtUtc;
            internal double StartedAtSeconds;
            internal int FirstRunEventSeq;
            internal int LastRunEventSeq;
            internal int CaseIndex;
            internal int StepIndex;
            internal ActiveRunPhase Phase;
            internal double StepSleepSeconds;
            internal double NextStepExecuteAllowedAtSeconds;
            internal bool CaseHasAppliedFixtureMutation;
            internal string PendingSettledStepPassMessage;

            internal ASMLiteSmokeCaseDefinition CurrentCase
            {
                get
                {
                    ASMLiteSmokeCaseDefinition[] cases = Suite == null ? Array.Empty<ASMLiteSmokeCaseDefinition>() : Suite.cases ?? Array.Empty<ASMLiteSmokeCaseDefinition>();
                    return CaseIndex >= 0 && CaseIndex < cases.Length ? cases[CaseIndex] : null;
                }
            }

            internal ASMLiteSmokeStepDefinition CurrentStep
            {
                get
                {
                    ASMLiteSmokeCaseDefinition currentCase = CurrentCase;
                    ASMLiteSmokeStepDefinition[] steps = currentCase == null ? Array.Empty<ASMLiteSmokeStepDefinition>() : currentCase.steps ?? Array.Empty<ASMLiteSmokeStepDefinition>();
                    return StepIndex >= 0 && StepIndex < steps.Length ? steps[StepIndex] : null;
                }
            }
        }

        private string _activeReviewRunId = string.Empty;
        private string _activeReviewGroupId = string.Empty;
        private string _activeReviewSuiteId = string.Empty;
        private string _activeReviewCommandId = string.Empty;
        private string _activeReviewRequestedResetDefault = "SceneReload";
        private string _activeReviewRequestedBy = "operator";
        private string _activeReviewReason = "review-rerun";

        internal ASMLiteSmokeOverlayHostRunner(
            ASMLiteSmokeOverlayHostConfiguration configuration,
            IASMLiteSmokeOverlayHostRuntime runtime)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        internal bool IsRunningForTesting => _isRunning;

        internal void Start()
        {
            _paths = ASMLiteSmokeArtifactPaths.FromSessionRoot(_configuration.SessionRootPath);
            _paths.EnsureSessionLayout();
            _sessionId = ResolveSessionId(_paths.SessionRootPath);

            _processedCommandIds = ASMLiteSmokeProtocol.RecoverProcessedCommandIdsFromEventLog(_paths.EventsLogPath);
            _lastEventSeq = RecoverLastEventSequence(_paths.EventsLogPath);
            _lastCommandSeq = 0;
            _runOrdinal = 0;
            _activeRun = null;
            ClearActiveReviewContext();

            try
            {
                _runtime.CloseAutomationWindowIfOpen();
                _runtime.StartConsoleErrorCapture();

                PublishHostState(
                    ASMLiteSmokeProtocol.HostStateIdle,
                    "Unity host booting.",
                    _lastEventSeq,
                    _lastCommandSeq);

                int sessionStartedSeq = _lastEventSeq + 1;
                AppendEventWithSequence(
                    sessionStartedSeq,
                    "session-started",
                    ASMLiteSmokeProtocol.HostStateIdle,
                    "Session metadata validated and Unity host boot requested.",
                    commandId: string.Empty);
                _lastEventSeq = sessionStartedSeq;

                string readyMessage = "Unity host ready for suite commands.";
                int readyEventSeq = _lastEventSeq + 1;
                PublishHostState(
                    ASMLiteSmokeProtocol.HostStateReady,
                    readyMessage,
                    readyEventSeq,
                    _lastCommandSeq);
                AppendEventWithSequence(
                    readyEventSeq,
                    "unity-ready",
                    ASMLiteSmokeProtocol.HostStateReady,
                    readyMessage,
                    commandId: string.Empty);

                _lastEventSeq = readyEventSeq;
                _currentState = ASMLiteSmokeProtocol.HostStateReady;
                _currentMessage = readyMessage;
                _lastHeartbeatWriteAt = _runtime.GetTimeSinceStartup();

                if (_configuration.ExitOnReady)
                {
                    _isRunning = false;
                    _runtime.StopConsoleErrorCapture();
                    _runtime.ExitEditorWithoutSaving(0);
                    return;
                }

                _isRunning = true;
                RegisterUpdate();
            }
            catch (Exception ex)
            {
                _runtime.StopConsoleErrorCapture();
                PublishCrashed(ex);
                if (_configuration.ExitOnReady)
                    throw;
            }
        }

        internal void StopForTesting()
        {
            _isRunning = false;
            UnregisterUpdate();
            _runtime.StopConsoleErrorCapture();
        }

        internal void TickForTesting()
        {
            Tick();
        }

        internal void PublishStalledForTesting(string message)
        {
            PublishStalled(message);
        }

        internal void PublishCrashedForTesting(Exception exception)
        {
            PublishCrashed(exception);
        }

        private void Tick()
        {
            if (!_isRunning)
                return;

            double now = _runtime.GetTimeSinceStartup();
            if ((now - _lastHeartbeatWriteAt) >= _configuration.HeartbeatSeconds)
            {
                PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                _lastHeartbeatWriteAt = now;
            }

            bool hadActiveRunAtTickStart = _activeRun != null;
            ProcessCommandFiles();

            if (hadActiveRunAtTickStart && _activeRun != null)
                TickActiveRun();
        }

        private void ProcessCommandFiles()
        {
            string[] commandFiles = _runtime.EnumerateCommandFiles(_paths.CommandsDirectoryPath);
            if (commandFiles == null || commandFiles.Length == 0)
                return;

            var parsedCommands = new List<Tuple<string, ASMLiteSmokeProtocolCommand>>();
            for (int i = 0; i < commandFiles.Length; i++)
            {
                string commandPath = commandFiles[i];
                string rawJson = string.Empty;
                try
                {
                    rawJson = _runtime.ReadAllText(commandPath);
                    ASMLiteSmokeProtocolCommand command = ASMLiteSmokeProtocol.LoadCommandFromJson(rawJson);
                    parsedCommands.Add(Tuple.Create(commandPath, command));
                }
                catch (Exception ex)
                {
                    TryHandleMalformedCommand(commandPath, rawJson, ex);
                }
            }

            parsedCommands.Sort((left, right) => left.Item2.commandSeq.CompareTo(right.Item2.commandSeq));
            for (int i = 0; i < parsedCommands.Count; i++)
            {
                ASMLiteSmokeProtocolCommand command = parsedCommands[i].Item2;
                if (_processedCommandIds.Contains(command.commandId))
                    continue;

                HandleCommand(command);
            }
        }

        private void TryHandleMalformedCommand(string commandPath, string rawJson, Exception parseException)
        {
            string commandId = string.Empty;
            int commandSeq = 0;

            try
            {
                var candidate = JsonUtility.FromJson<ASMLiteSmokeProtocolCommand>(rawJson);
                if (candidate != null)
                {
                    commandId = string.IsNullOrWhiteSpace(candidate.commandId) ? string.Empty : candidate.commandId.Trim();
                    commandSeq = Math.Max(0, candidate.commandSeq);
                }
            }
            catch
            {
                // Ignore secondary parse failures.
            }

            string message = $"Failed to parse command file '{Path.GetFileName(commandPath)}': {parseException.Message}";
            if (!string.IsNullOrWhiteSpace(commandId))
            {
                if (_processedCommandIds.Contains(commandId))
                    return;

                _lastCommandSeq = Math.Max(_lastCommandSeq, commandSeq);
                int rejectSeq = _lastEventSeq + 1;
                PublishHostState(_currentState, message, rejectSeq, _lastCommandSeq);
                AppendEventWithSequence(
                    rejectSeq,
                    "command-rejected",
                    _currentState,
                    message,
                    commandId);

                _lastEventSeq = rejectSeq;
                _processedCommandIds.Add(commandId);
                return;
            }

            PublishHostState(_currentState, message, _lastEventSeq, _lastCommandSeq);
            _currentMessage = message;
        }

        private void HandleCommand(ASMLiteSmokeProtocolCommand command)
        {
            _lastCommandSeq = Math.Max(_lastCommandSeq, command.commandSeq);

            switch (command.commandType)
            {
                case "shutdown-session":
                    HandleShutdownSession(command);
                    break;

                case "run-suite":
                    HandleRunSuite(command);
                    break;

                case "review-decision":
                    HandleReviewDecision(command);
                    break;

                case "abort-run":
                    HandleAbortRun(command);
                    break;

                default:
                    RejectCommand(command, $"{command.commandType} is not implemented in this host phase.");
                    break;
            }
        }

        private void HandleRunSuite(ASMLiteSmokeProtocolCommand command, bool allowDuringReviewRequired = false)
        {
            if (command == null || command.runSuite == null)
            {
                RejectCommand(command, "run-suite payload is required.");
                return;
            }

            if (_activeRun != null || string.Equals(_currentState, ASMLiteSmokeProtocol.HostStateRunning, StringComparison.Ordinal))
            {
                RejectCommand(command, "run-suite rejected while another suite is running.");
                return;
            }

            if (!allowDuringReviewRequired
                && string.Equals(_currentState, ASMLiteSmokeProtocol.HostStateReviewRequired, StringComparison.Ordinal))
            {
                RejectCommand(command, "run-suite rejected while review decision is pending.");
                return;
            }

            try
            {
                ASMLiteSmokeCatalogDocument catalog = ASMLiteSmokeCatalog.LoadFromPath(_configuration.CatalogPath);
                if (!catalog.TryGetSuite(command.runSuite.suiteId, out ASMLiteSmokeSuiteDefinition suite) || suite == null)
                {
                    RejectCommand(command, $"Unknown suiteId '{command.runSuite.suiteId}'.");
                    return;
                }

                string effectiveResetPolicy = ASMLiteSmokeResetService.ResolveEffectivePolicy(
                    command.runSuite.requestedResetDefault,
                    suite.resetOverride);

                int runOrdinal = _runOrdinal + 1;
                string runId = $"run-{command.commandSeq:D4}-{ASMLiteSmokeSessionPaths.NormalizePortableIdentifier(suite.suiteId, nameof(suite.suiteId))}";
                var result = new ASMLiteSmokeSuiteExecutionResult
                {
                    RunId = runId,
                    GroupId = FindGroupId(catalog, suite.suiteId),
                    SuiteId = suite.suiteId,
                    SuiteLabel = suite.label,
                    EffectiveResetPolicy = effectiveResetPolicy,
                    Succeeded = false,
                };

                _activeRun = new ActiveRunState
                {
                    Catalog = catalog,
                    Suite = suite,
                    Command = command,
                    Result = result,
                    EffectiveResetPolicy = effectiveResetPolicy,
                    RunOrdinal = runOrdinal,
                    StartedAtUtc = _runtime.GetUtcNowIso(),
                    StartedAtSeconds = _runtime.GetTimeSinceStartup(),
                    FirstRunEventSeq = 0,
                    LastRunEventSeq = 0,
                    CaseIndex = 0,
                    StepIndex = 0,
                    Phase = ActiveRunPhase.SuiteStart,
                    StepSleepSeconds = Math.Max(0d, command.runSuite.stepSleepSeconds),
                    NextStepExecuteAllowedAtSeconds = 0d,
                    CaseHasAppliedFixtureMutation = false,
                    PendingSettledStepPassMessage = string.Empty,
                };

                string runningMessage = $"Running suite '{suite.suiteId}' with effective reset policy '{effectiveResetPolicy}'.";
                _currentState = ASMLiteSmokeProtocol.HostStateRunning;
                _currentMessage = runningMessage;
                ClearActiveReviewContext();
                PublishHostState(
                    ASMLiteSmokeProtocol.HostStateRunning,
                    runningMessage,
                    _lastEventSeq,
                    _lastCommandSeq);

                _processedCommandIds.Add(command.commandId);
            }
            catch (Exception ex)
            {
                _activeRun = null;
                string message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "run-suite execution failed."
                    : $"run-suite execution failed: {ex.Message}";
                RejectCommand(command, message);
            }
        }

        private void TickActiveRun()
        {
            ActiveRunState activeRun = _activeRun;
            if (activeRun == null)
                return;

            try
            {
                switch (activeRun.Phase)
                {
                    case ActiveRunPhase.SuiteStart:
                        AppendActiveRunEvent(
                            activeRun,
                            "suite-started",
                            activeRun.Suite.suiteId,
                            string.Empty,
                            string.Empty,
                            $"Suite '{activeRun.Suite.suiteId}' started.");
                        AdvanceActiveRunToNextCaseOrSuitePassed(activeRun);
                        PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                        return;

                    case ActiveRunPhase.CaseStart:
                    {
                        ASMLiteSmokeCaseDefinition suiteCase = activeRun.CurrentCase;
                        if (suiteCase == null)
                        {
                            AdvanceActiveRunToNextCaseOrSuitePassed(activeRun);
                            PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                            return;
                        }

                        AppendActiveRunEvent(
                            activeRun,
                            "case-started",
                            activeRun.Suite.suiteId,
                            suiteCase.caseId,
                            string.Empty,
                            $"Case '{suiteCase.caseId}' started.");
                        AdvanceActiveRunToNextStepOrCase(activeRun);
                        PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                        return;
                    }

                    case ActiveRunPhase.StepStart:
                    {
                        ASMLiteSmokeCaseDefinition suiteCase = activeRun.CurrentCase;
                        ASMLiteSmokeStepDefinition step = activeRun.CurrentStep;
                        if (suiteCase == null || step == null)
                        {
                            AdvanceActiveRunToNextStepOrCase(activeRun);
                            PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                            return;
                        }

                        AppendActiveRunEvent(
                            activeRun,
                            "step-started",
                            activeRun.Suite.suiteId,
                            suiteCase.caseId,
                            step.stepId,
                            $"Step '{step.stepId}' started ({step.actionType}).");
                        activeRun.NextStepExecuteAllowedAtSeconds = _runtime.GetTimeSinceStartup() + activeRun.StepSleepSeconds;
                        activeRun.Phase = ActiveRunPhase.StepExecute;
                        PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                        return;
                    }

                    case ActiveRunPhase.StepExecute:
                        if (_runtime.GetTimeSinceStartup() < activeRun.NextStepExecuteAllowedAtSeconds)
                        {
                            PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                            return;
                        }

                        ExecuteActiveRunStep(activeRun);
                        PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                        return;

                    case ActiveRunPhase.StepSettle:
                        if (_runtime.GetTimeSinceStartup() < activeRun.NextStepExecuteAllowedAtSeconds)
                        {
                            PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                            return;
                        }

                        CompleteSettledActiveRunStep(activeRun);
                        PublishHostState(_currentState, _currentMessage, _lastEventSeq, _lastCommandSeq);
                        return;

                    case ActiveRunPhase.SuitePassed:
                        activeRun.Result.Succeeded = true;
                        AppendActiveRunEvent(
                            activeRun,
                            "suite-passed",
                            activeRun.Suite.suiteId,
                            string.Empty,
                            string.Empty,
                            $"Suite '{activeRun.Suite.suiteId}' passed.",
                            ASMLiteSmokeProtocol.HostStateReady);
                        CompleteActiveRun(activeRun, "passed", activeRun.Command.commandId);
                        return;

                    case ActiveRunPhase.SuiteFailed:
                    {
                        ASMLiteSmokeExecutionFailure failure = activeRun.Result.Failure;
                        string caseId = failure == null ? string.Empty : failure.CaseId;
                        string stepId = failure == null ? string.Empty : failure.StepId;
                        AppendActiveRunEvent(
                            activeRun,
                            "suite-failed",
                            activeRun.Suite.suiteId,
                            caseId,
                            stepId,
                            string.IsNullOrWhiteSpace(stepId)
                                ? $"Suite '{activeRun.Suite.suiteId}' failed."
                                : $"Suite '{activeRun.Suite.suiteId}' failed at step '{stepId}'.");
                        CompleteActiveRun(activeRun, "failed", activeRun.Command.commandId);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                string message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "run-suite tick failed."
                    : $"run-suite tick failed: {ex.Message}";
                RejectCommand(activeRun.Command, message);
                _activeRun = null;
            }
        }

        private void ExecuteActiveRunStep(ActiveRunState activeRun)
        {
            ASMLiteSmokeCaseDefinition suiteCase = activeRun.CurrentCase;
            ASMLiteSmokeStepDefinition step = activeRun.CurrentStep;
            if (suiteCase == null || step == null)
            {
                AdvanceActiveRunToNextStepOrCase(activeRun);
                return;
            }

            bool stepPassed = ExecuteCatalogStep(activeRun, step, out string detail, out string stackTrace);
            bool expectsStepFailure = ASMLiteSmokeExpectedDiagnosticMatcher.ExpectsStepFailure(step);
            if (stepPassed)
            {
                if (expectsStepFailure)
                {
                    string unexpectedSuccessMessage = ASMLiteSmokeExpectedDiagnosticMatcher.BuildUnexpectedSuccessMessage(step);
                    FailActiveRunStep(activeRun, suiteCase, step, unexpectedSuccessMessage, stackTrace);
                    return;
                }

                string cleanResetMessage = string.Empty;
                string cleanResetStackTrace = string.Empty;
                if (RequiresCleanReset(step))
                {
                    if (!TryRunRequiredCleanReset(out cleanResetMessage, out cleanResetStackTrace))
                    {
                        FailActiveRunStep(activeRun, suiteCase, step, cleanResetMessage, cleanResetStackTrace);
                        return;
                    }

                    activeRun.CaseHasAppliedFixtureMutation = false;
                }

                string passMessage = string.IsNullOrWhiteSpace(detail)
                    ? $"Step '{step.stepId}' passed."
                    : detail.Trim();
                PassActiveRunStep(activeRun, suiteCase, step, AppendCleanResetPassMessage(passMessage, cleanResetMessage));
                return;
            }

            string failureMessage = string.IsNullOrWhiteSpace(detail)
                ? $"Step '{step.stepId}' failed."
                : detail.Trim();

            if (expectsStepFailure)
            {
                if (ASMLiteSmokeExpectedDiagnosticMatcher.MatchesExpectedDiagnostic(
                    step,
                    failureMessage,
                    out string expectedFailurePassMessage,
                    out string expectedFailureMismatchMessage))
                {
                    string cleanResetMessage = string.Empty;
                    string cleanResetStackTrace = string.Empty;
                    if (RequiresCleanReset(step))
                    {
                        if (!TryRunRequiredCleanReset(out cleanResetMessage, out cleanResetStackTrace))
                        {
                            FailActiveRunStep(activeRun, suiteCase, step, cleanResetMessage, cleanResetStackTrace);
                            return;
                        }

                        activeRun.CaseHasAppliedFixtureMutation = false;
                    }

                    PassActiveRunStep(activeRun, suiteCase, step, AppendCleanResetPassMessage(expectedFailurePassMessage, cleanResetMessage));
                    return;
                }

                failureMessage = expectedFailureMismatchMessage;
            }
            AppendActiveRunEvent(
                activeRun,
                "step-failed",
                activeRun.Suite.suiteId,
                suiteCase.caseId,
                step.stepId,
                failureMessage);

            activeRun.Result.Failure = new ASMLiteSmokeExecutionFailure
            {
                CaseId = suiteCase.caseId,
                CaseLabel = suiteCase.label,
                StepId = step.stepId,
                StepLabel = step.label,
                FailureMessage = failureMessage,
                StackTrace = string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace.Trim(),
            };
            activeRun.Result.Succeeded = false;
            activeRun.Phase = ActiveRunPhase.SuiteFailed;
        }

        private void PassActiveRunStep(
            ActiveRunState activeRun,
            ASMLiteSmokeCaseDefinition suiteCase,
            ASMLiteSmokeStepDefinition step,
            string message)
        {
            string normalizedMessage = string.IsNullOrWhiteSpace(message) ? $"Step '{step.stepId}' passed." : message.Trim();
            bool settlesAfterStep = ShouldSettleAfterStep(step);
            if (settlesAfterStep)
            {
                activeRun.PendingSettledStepPassMessage = normalizedMessage;
                activeRun.NextStepExecuteAllowedAtSeconds = _runtime.GetTimeSinceStartup() + PostLifecycleMutationSettleSeconds;
                activeRun.Phase = ActiveRunPhase.StepSettle;
                return;
            }

            string caseFixtureResetMessage = string.Empty;
            if (ShouldResetFixtureAfterCase(activeRun, suiteCase, step))
            {
                if (!TryResetSetupFixtureAfterCase(out caseFixtureResetMessage, out string caseFixtureResetStackTrace))
                {
                    FailActiveRunStep(activeRun, suiteCase, step, caseFixtureResetMessage, caseFixtureResetStackTrace);
                    return;
                }

                activeRun.CaseHasAppliedFixtureMutation = false;
            }

            AppendActiveRunEvent(
                activeRun,
                "step-passed",
                activeRun.Suite.suiteId,
                suiteCase.caseId,
                step.stepId,
                AppendCaseFixtureResetPassMessage(normalizedMessage, caseFixtureResetMessage));
            activeRun.StepIndex++;
            AdvanceActiveRunToNextStepOrCase(activeRun);
        }

        private void CompleteSettledActiveRunStep(ActiveRunState activeRun)
        {
            ASMLiteSmokeCaseDefinition suiteCase = activeRun.CurrentCase;
            ASMLiteSmokeStepDefinition step = activeRun.CurrentStep;
            if (suiteCase == null || step == null)
            {
                activeRun.PendingSettledStepPassMessage = string.Empty;
                AdvanceActiveRunToNextStepOrCase(activeRun);
                return;
            }

            string caseFixtureResetMessage = string.Empty;
            if (ShouldResetFixtureAfterCase(activeRun, suiteCase, step))
            {
                if (!TryResetSetupFixtureAfterCase(out caseFixtureResetMessage, out string caseFixtureResetStackTrace))
                {
                    activeRun.PendingSettledStepPassMessage = string.Empty;
                    FailActiveRunStep(activeRun, suiteCase, step, caseFixtureResetMessage, caseFixtureResetStackTrace);
                    return;
                }

                activeRun.CaseHasAppliedFixtureMutation = false;
            }

            string normalizedMessage = string.IsNullOrWhiteSpace(activeRun.PendingSettledStepPassMessage)
                ? $"Step '{step.stepId}' passed."
                : activeRun.PendingSettledStepPassMessage.Trim();
            activeRun.PendingSettledStepPassMessage = string.Empty;
            AppendActiveRunEvent(
                activeRun,
                "step-passed",
                activeRun.Suite.suiteId,
                suiteCase.caseId,
                step.stepId,
                AppendCaseFixtureResetPassMessage(normalizedMessage, caseFixtureResetMessage));
            activeRun.StepIndex++;
            AdvanceActiveRunToNextStepOrCase(activeRun);
        }

        private void FailActiveRunStep(
            ActiveRunState activeRun,
            ASMLiteSmokeCaseDefinition suiteCase,
            ASMLiteSmokeStepDefinition step,
            string failureMessage,
            string stackTrace)
        {
            string normalizedFailureMessage = string.IsNullOrWhiteSpace(failureMessage)
                ? $"Step '{step.stepId}' failed."
                : failureMessage.Trim();
            AppendActiveRunEvent(
                activeRun,
                "step-failed",
                activeRun.Suite.suiteId,
                suiteCase.caseId,
                step.stepId,
                normalizedFailureMessage);

            activeRun.Result.Failure = new ASMLiteSmokeExecutionFailure
            {
                CaseId = suiteCase.caseId,
                CaseLabel = suiteCase.label,
                StepId = step.stepId,
                StepLabel = step.label,
                FailureMessage = normalizedFailureMessage,
                StackTrace = string.IsNullOrWhiteSpace(stackTrace) ? string.Empty : stackTrace.Trim(),
            };
            activeRun.Result.Succeeded = false;
            activeRun.Phase = ActiveRunPhase.SuiteFailed;
        }

        private static bool RequiresCleanReset(ASMLiteSmokeStepDefinition step)
        {
            return step != null && step.args != null && step.args.requireCleanReset;
        }

        private bool TryRunRequiredCleanReset(out string detail, out string stackTrace)
        {
            bool reset = _runtime.ResetSetupFixture(out string resetDetail, out stackTrace);
            string normalizedDetail = string.IsNullOrWhiteSpace(resetDetail) ? string.Empty : resetDetail.Trim();
            if (reset)
            {
                detail = string.IsNullOrWhiteSpace(normalizedDetail)
                    ? "Clean reset passed."
                    : $"Clean reset passed: {normalizedDetail}";
                return true;
            }

            detail = string.IsNullOrWhiteSpace(normalizedDetail)
                ? "SETUP_DESTRUCTIVE_RESET_FAILED: clean reset failed after destructive setup case."
                : $"SETUP_DESTRUCTIVE_RESET_FAILED: clean reset failed after destructive setup case. {normalizedDetail}";
            return false;
        }

        private static string AppendCleanResetPassMessage(string stepMessage, string cleanResetMessage)
        {
            string normalizedStepMessage = string.IsNullOrWhiteSpace(stepMessage)
                ? "Step passed."
                : stepMessage.Trim();
            string normalizedCleanResetMessage = string.IsNullOrWhiteSpace(cleanResetMessage)
                ? string.Empty
                : cleanResetMessage.Trim();
            return string.IsNullOrWhiteSpace(normalizedCleanResetMessage)
                ? normalizedStepMessage
                : $"{normalizedStepMessage} {normalizedCleanResetMessage}";
        }

        private static bool ShouldSettleAfterStep(ASMLiteSmokeStepDefinition step)
        {
            if (step == null || string.IsNullOrWhiteSpace(step.actionType))
                return false;

            switch (step.actionType.Trim())
            {
                case "add-prefab":
                case "rebuild":
                case "vendorize":
                case "detach":
                case "lifecycle-hygiene-cleanup":
                case "return-to-package-managed":
                    return true;
                default:
                    return false;
            }
        }

        private static bool ShouldResetFixtureAfterCase(
            ActiveRunState activeRun,
            ASMLiteSmokeCaseDefinition suiteCase,
            ASMLiteSmokeStepDefinition step)
        {
            return activeRun != null
                && activeRun.CaseHasAppliedFixtureMutation
                && !RequiresCleanReset(step)
                && IsLastRunnableStepInCase(suiteCase, activeRun.StepIndex);
        }

        private static bool IsLastRunnableStepInCase(ASMLiteSmokeCaseDefinition suiteCase, int currentStepIndex)
        {
            ASMLiteSmokeStepDefinition[] steps = suiteCase == null
                ? Array.Empty<ASMLiteSmokeStepDefinition>()
                : suiteCase.steps ?? Array.Empty<ASMLiteSmokeStepDefinition>();

            for (int index = currentStepIndex + 1; index < steps.Length; index++)
            {
                if (steps[index] != null)
                    return false;
            }

            return true;
        }

        private bool TryResetSetupFixtureAfterCase(out string detail, out string stackTrace)
        {
            bool reset = _runtime.ResetSetupFixture(out string resetDetail, out stackTrace);
            if (reset)
            {
                detail = string.IsNullOrWhiteSpace(resetDetail)
                    ? "Fixture state reset after case completion."
                    : resetDetail.Trim();
                return true;
            }

            detail = string.IsNullOrWhiteSpace(resetDetail)
                ? "Fixture state reset failed after case completion."
                : resetDetail.Trim();
            return false;
        }

        private static string AppendCaseFixtureResetPassMessage(string stepMessage, string resetMessage)
        {
            string normalizedStepMessage = string.IsNullOrWhiteSpace(stepMessage)
                ? string.Empty
                : stepMessage.Trim();
            string normalizedResetMessage = string.IsNullOrWhiteSpace(resetMessage)
                ? string.Empty
                : resetMessage.Trim();
            return string.IsNullOrWhiteSpace(normalizedResetMessage)
                ? normalizedStepMessage
                : $"{normalizedStepMessage} {normalizedResetMessage}";
        }

        private void AdvanceActiveRunToNextCaseOrSuitePassed(ActiveRunState activeRun)
        {
            ASMLiteSmokeCaseDefinition[] cases = activeRun.Suite == null
                ? Array.Empty<ASMLiteSmokeCaseDefinition>()
                : activeRun.Suite.cases ?? Array.Empty<ASMLiteSmokeCaseDefinition>();

            while (activeRun.CaseIndex < cases.Length && cases[activeRun.CaseIndex] == null)
                activeRun.CaseIndex++;

            activeRun.StepIndex = 0;
            activeRun.CaseHasAppliedFixtureMutation = false;
            activeRun.PendingSettledStepPassMessage = string.Empty;
            activeRun.Phase = activeRun.CaseIndex < cases.Length
                ? ActiveRunPhase.CaseStart
                : ActiveRunPhase.SuitePassed;
        }

        private void AdvanceActiveRunToNextStepOrCase(ActiveRunState activeRun)
        {
            ASMLiteSmokeCaseDefinition suiteCase = activeRun.CurrentCase;
            ASMLiteSmokeStepDefinition[] steps = suiteCase == null
                ? Array.Empty<ASMLiteSmokeStepDefinition>()
                : suiteCase.steps ?? Array.Empty<ASMLiteSmokeStepDefinition>();

            while (activeRun.StepIndex < steps.Length && steps[activeRun.StepIndex] == null)
                activeRun.StepIndex++;

            if (activeRun.StepIndex < steps.Length)
            {
                activeRun.Phase = ActiveRunPhase.StepStart;
                return;
            }

            activeRun.CaseIndex++;
            activeRun.StepIndex = 0;
            AdvanceActiveRunToNextCaseOrSuitePassed(activeRun);
        }

        private void AppendActiveRunEvent(
            ActiveRunState activeRun,
            string eventType,
            string suiteId,
            string caseId,
            string stepId,
            string message,
            string eventHostState = null,
            string commandId = null)
        {
            var payload = new ASMLiteSmokeExecutionEventPayload
            {
                EventType = eventType,
                Message = message,
                GroupId = activeRun.Result.GroupId,
                SuiteId = suiteId,
                CaseId = caseId,
                StepId = stepId,
                EffectiveResetPolicy = activeRun.EffectiveResetPolicy,
            };
            activeRun.Result.Events.Add(payload);

            int eventSeq = _lastEventSeq + 1;
            if (activeRun.FirstRunEventSeq <= 0)
                activeRun.FirstRunEventSeq = eventSeq;

            AppendEventWithSequence(
                eventSeq,
                eventType,
                string.IsNullOrWhiteSpace(eventHostState) ? ASMLiteSmokeProtocol.HostStateRunning : eventHostState,
                message,
                string.IsNullOrWhiteSpace(commandId) ? activeRun.Command.commandId : commandId,
                runId: activeRun.Result.RunId,
                groupId: activeRun.Result.GroupId,
                suiteId: suiteId,
                caseId: caseId,
                stepId: stepId,
                effectiveResetPolicy: activeRun.EffectiveResetPolicy);
            _lastEventSeq = eventSeq;
            activeRun.LastRunEventSeq = eventSeq;
        }

        private void CompleteActiveRun(ActiveRunState activeRun, string resultStatus, string reviewCommandId)
        {
            string runEndedAtUtc = _runtime.GetUtcNowIso();
            double runEndedAtSeconds = _runtime.GetTimeSinceStartup();
            int firstRunEventSeq = activeRun.FirstRunEventSeq <= 0 ? _lastEventSeq : activeRun.FirstRunEventSeq;
            int lastRunEventSeq = activeRun.LastRunEventSeq <= 0 ? _lastEventSeq : activeRun.LastRunEventSeq;

            EmitRunArtifacts(
                activeRun.Catalog,
                activeRun.Suite,
                activeRun.Command,
                activeRun.Result,
                activeRun.RunOrdinal,
                activeRun.StartedAtUtc,
                runEndedAtUtc,
                activeRun.StartedAtSeconds,
                runEndedAtSeconds,
                firstRunEventSeq,
                lastRunEventSeq,
                resultStatus);

            string completionMessage = BuildRunCompletionMessage(activeRun.Result, resultStatus);
            bool succeeded = string.Equals(resultStatus, "passed", StringComparison.Ordinal) || activeRun.Result.Succeeded;
            _activeRun = null;

            if (succeeded)
            {
                ClearActiveReviewContext();
                int idleSeq = _lastEventSeq + 1;
                AppendEventWithSequence(
                    idleSeq,
                    "session-idle",
                    ASMLiteSmokeProtocol.HostStateIdle,
                    "Suite passed; returning to suite selection.",
                    reviewCommandId,
                    runId: activeRun.Result.RunId,
                    groupId: activeRun.Result.GroupId,
                    suiteId: activeRun.Result.SuiteId);
                _lastEventSeq = idleSeq;

                _currentState = ASMLiteSmokeProtocol.HostStateIdle;
                _currentMessage = completionMessage;
                PublishHostState(
                    ASMLiteSmokeProtocol.HostStateIdle,
                    completionMessage,
                    _lastEventSeq,
                    _lastCommandSeq);

                _runOrdinal = activeRun.RunOrdinal;
                return;
            }

            CaptureActiveReviewContext(activeRun.Command, activeRun.Result);

            _currentState = ASMLiteSmokeProtocol.HostStateReviewRequired;
            _currentMessage = completionMessage;

            int reviewRequiredSeq = _lastEventSeq + 1;
            AppendEventWithSequence(
                reviewRequiredSeq,
                "review-required",
                ASMLiteSmokeProtocol.HostStateReviewRequired,
                "Choose Return to Suite List, Rerun Suite, or Exit.",
                reviewCommandId,
                runId: activeRun.Result.RunId,
                groupId: activeRun.Result.GroupId,
                suiteId: activeRun.Result.SuiteId,
                reviewDecisionOptions: s_reviewDecisionOptions);
            _lastEventSeq = reviewRequiredSeq;

            PublishHostState(
                ASMLiteSmokeProtocol.HostStateReviewRequired,
                completionMessage,
                _lastEventSeq,
                _lastCommandSeq);

            _runOrdinal = activeRun.RunOrdinal;
        }

        private string BuildRunCompletionMessage(ASMLiteSmokeSuiteExecutionResult result, string resultStatus)
        {
            if (string.Equals(resultStatus, "aborted", StringComparison.Ordinal))
                return $"Suite '{result.SuiteId}' was aborted and is waiting for operator review.";

            if (string.Equals(resultStatus, "passed", StringComparison.Ordinal) || result.Succeeded)
                return $"Suite '{result.SuiteId}' passed; returned to suite selection.";

            string failedStep = result.Failure == null ? string.Empty : result.Failure.StepId;
            return string.IsNullOrWhiteSpace(failedStep)
                ? $"Suite '{result.SuiteId}' failed and is waiting for operator review."
                : $"Suite '{result.SuiteId}' failed at step '{failedStep}' and is waiting for operator review.";
        }

        private void HandleAbortRun(ASMLiteSmokeProtocolCommand command)
        {
            if (command == null || command.abortRun == null)
            {
                RejectCommand(command, "abort-run payload is required.");
                return;
            }

            if (_activeRun == null || !string.Equals(_currentState, ASMLiteSmokeProtocol.HostStateRunning, StringComparison.Ordinal))
            {
                RejectCommand(command, "abort-run rejected because no active run is running.");
                return;
            }

            ASMLiteSmokeAbortRunPayload payload = command.abortRun;
            if (!string.Equals(payload.runId, _activeRun.Result.RunId, StringComparison.Ordinal)
                || !string.Equals(payload.suiteId, _activeRun.Result.SuiteId, StringComparison.Ordinal))
            {
                RejectCommand(command, "abort-run runId/suiteId does not match the active run context.");
                return;
            }

            ActiveRunState activeRun = _activeRun;
            string requestedBy = string.IsNullOrWhiteSpace(payload.requestedBy) ? "operator" : payload.requestedBy.Trim();
            string reason = string.IsNullOrWhiteSpace(payload.reason) ? "operator-abort" : payload.reason.Trim();
            AppendActiveRunEvent(
                activeRun,
                "abort-requested",
                activeRun.Result.SuiteId,
                string.Empty,
                string.Empty,
                $"Abort requested by '{requestedBy}': {reason}",
                commandId: command.commandId);
            AppendActiveRunEvent(
                activeRun,
                "run-aborted",
                activeRun.Result.SuiteId,
                string.Empty,
                string.Empty,
                $"Run '{activeRun.Result.RunId}' aborted before the next smoke step.",
                commandId: command.commandId);

            activeRun.Result.Succeeded = false;
            CompleteActiveRun(activeRun, "aborted", command.commandId);
            _processedCommandIds.Add(command.commandId);
        }

        private static string FindGroupId(ASMLiteSmokeCatalogDocument catalog, string suiteId)
        {
            ASMLiteSmokeGroupDefinition[] groups = catalog == null ? Array.Empty<ASMLiteSmokeGroupDefinition>() : catalog.groups ?? Array.Empty<ASMLiteSmokeGroupDefinition>();
            for (int i = 0; i < groups.Length; i++)
            {
                ASMLiteSmokeGroupDefinition group = groups[i];
                if (group == null || group.suites == null)
                    continue;

                for (int j = 0; j < group.suites.Length; j++)
                {
                    ASMLiteSmokeSuiteDefinition suite = group.suites[j];
                    if (suite != null && string.Equals(suite.suiteId, suiteId, StringComparison.Ordinal))
                        return group.groupId;
                }
            }

            return string.Empty;
        }

        private void HandleReviewDecision(ASMLiteSmokeProtocolCommand command)
        {
            if (command == null || command.reviewDecision == null)
            {
                RejectCommand(command, "review-decision payload is required.");
                return;
            }

            if (!string.Equals(_currentState, ASMLiteSmokeProtocol.HostStateReviewRequired, StringComparison.Ordinal)
                || !HasActiveReviewContext())
            {
                RejectCommand(command, "review-decision rejected because no active review is pending.");
                return;
            }

            ASMLiteSmokeReviewDecisionPayload payload = command.reviewDecision;
            if (!string.Equals(payload.runId, _activeReviewRunId, StringComparison.Ordinal)
                || !string.Equals(payload.suiteId, _activeReviewSuiteId, StringComparison.Ordinal))
            {
                RejectCommand(command, "review-decision runId/suiteId does not match the active review context.");
                return;
            }

            string decision = string.IsNullOrWhiteSpace(payload.decision)
                ? string.Empty
                : payload.decision.Trim();
            if (!s_reviewDecisionOptions.Contains(decision, StringComparer.Ordinal))
            {
                RejectCommand(command, $"Unsupported review decision '{payload.decision}'.");
                return;
            }

            switch (decision)
            {
                case "return-to-suite-list":
                {
                    const string idleMessage = "Review decision applied; waiting for the next suite selection.";
                    int idleSeq = _lastEventSeq + 1;
                    PublishHostState(
                        ASMLiteSmokeProtocol.HostStateIdle,
                        idleMessage,
                        idleSeq,
                        _lastCommandSeq);
                    AppendEventWithSequence(
                        idleSeq,
                        "session-idle",
                        ASMLiteSmokeProtocol.HostStateIdle,
                        idleMessage,
                        command.commandId,
                        runId: _activeReviewRunId,
                        groupId: _activeReviewGroupId,
                        suiteId: _activeReviewSuiteId);

                    _lastEventSeq = idleSeq;
                    _currentState = ASMLiteSmokeProtocol.HostStateIdle;
                    _currentMessage = idleMessage;
                    ClearActiveReviewContext();
                    _processedCommandIds.Add(command.commandId);
                    return;
                }

                case "rerun-suite":
                {
                    string rerunReason = string.IsNullOrWhiteSpace(payload.notes)
                        ? _activeReviewReason
                        : payload.notes.Trim();
                    string rerunRequestedBy = string.IsNullOrWhiteSpace(payload.requestedBy)
                        ? _activeReviewRequestedBy
                        : payload.requestedBy.Trim();

                    var rerunCommand = new ASMLiteSmokeProtocolCommand
                    {
                        protocolVersion = command.protocolVersion,
                        sessionId = command.sessionId,
                        commandId = command.commandId,
                        commandSeq = command.commandSeq,
                        commandType = "run-suite",
                        createdAtUtc = _runtime.GetUtcNowIso(),
                        launchSession = null,
                        runSuite = new ASMLiteSmokeRunSuitePayload
                        {
                            suiteId = _activeReviewSuiteId,
                            requestedBy = rerunRequestedBy,
                            requestedResetDefault = _activeReviewRequestedResetDefault,
                            reason = rerunReason,
                        },
                        reviewDecision = null,
                        abortRun = null,
                    };

                    HandleRunSuite(rerunCommand, allowDuringReviewRequired: true);
                    return;
                }

                case "exit":
                    ClearActiveReviewContext();
                    HandleShutdownSession(command);
                    return;
            }
        }

        private bool HasActiveReviewContext()
        {
            return !string.IsNullOrWhiteSpace(_activeReviewRunId)
                && !string.IsNullOrWhiteSpace(_activeReviewSuiteId)
                && !string.IsNullOrWhiteSpace(_activeReviewCommandId);
        }

        private void CaptureActiveReviewContext(ASMLiteSmokeProtocolCommand command, ASMLiteSmokeSuiteExecutionResult result)
        {
            _activeReviewRunId = result == null || string.IsNullOrWhiteSpace(result.RunId)
                ? string.Empty
                : result.RunId.Trim();
            _activeReviewGroupId = result == null || string.IsNullOrWhiteSpace(result.GroupId)
                ? string.Empty
                : result.GroupId.Trim();
            _activeReviewSuiteId = result == null || string.IsNullOrWhiteSpace(result.SuiteId)
                ? string.Empty
                : result.SuiteId.Trim();
            _activeReviewCommandId = command == null || string.IsNullOrWhiteSpace(command.commandId)
                ? string.Empty
                : command.commandId.Trim();
            _activeReviewRequestedResetDefault = command?.runSuite == null || string.IsNullOrWhiteSpace(command.runSuite.requestedResetDefault)
                ? "SceneReload"
                : command.runSuite.requestedResetDefault.Trim();
            _activeReviewRequestedBy = command?.runSuite == null || string.IsNullOrWhiteSpace(command.runSuite.requestedBy)
                ? "operator"
                : command.runSuite.requestedBy.Trim();
            _activeReviewReason = command?.runSuite == null || string.IsNullOrWhiteSpace(command.runSuite.reason)
                ? "review-rerun"
                : command.runSuite.reason.Trim();
        }

        private void ClearActiveReviewContext()
        {
            _activeReviewRunId = string.Empty;
            _activeReviewGroupId = string.Empty;
            _activeReviewSuiteId = string.Empty;
            _activeReviewCommandId = string.Empty;
            _activeReviewRequestedResetDefault = "SceneReload";
            _activeReviewRequestedBy = "operator";
            _activeReviewReason = "review-rerun";
        }

        private void EmitRunArtifacts(
            ASMLiteSmokeCatalogDocument catalog,
            ASMLiteSmokeSuiteDefinition suite,
            ASMLiteSmokeProtocolCommand command,
            ASMLiteSmokeSuiteExecutionResult result,
            int runOrdinal,
            string startedAtUtc,
            string endedAtUtc,
            double startedAtSeconds,
            double endedAtSeconds,
            int firstRunEventSeq,
            int lastRunEventSeq,
            string resultStatus = null)
        {
            string runDirectoryPath = _paths.GetRunDirectoryPath(runOrdinal, result.SuiteId);
            Directory.CreateDirectory(runDirectoryPath);

            string resultPath = _paths.GetResultPath(runOrdinal, result.SuiteId);
            string failurePath = _paths.GetFailurePath(runOrdinal, result.SuiteId);
            string eventsSlicePath = _paths.GetEventsSlicePath(runOrdinal, result.SuiteId);
            string nunitPath = _paths.GetNUnitPath(runOrdinal, result.SuiteId);

            ASMLiteSmokeProtocolEvent[] allEvents =
                ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(_paths.EventsLogPath);
            ASMLiteSmokeProtocolEvent[] runEvents = allEvents
                .Where(evt => evt.eventSeq >= firstRunEventSeq && evt.eventSeq <= lastRunEventSeq)
                .ToArray();

            string sliceNdjson = ASMLiteSmokeProtocol.ToNdjson(runEvents);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(eventsSlicePath, sliceNdjson);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(nunitPath, BuildNUnitXml(result, command.commandId, startedAtUtc, endedAtUtc));

            string groupLabel = result.GroupId;
            if (catalog != null && catalog.TryGetGroup(result.GroupId, out ASMLiteSmokeGroupDefinition group) && group != null)
            {
                if (!string.IsNullOrWhiteSpace(group.label))
                    groupLabel = group.label;
            }

            if (string.IsNullOrWhiteSpace(groupLabel))
                groupLabel = "unknown-group";

            string normalizedResultStatus = string.IsNullOrWhiteSpace(resultStatus)
                ? (result.Succeeded ? "passed" : "failed")
                : resultStatus.Trim();
            bool shouldWriteFailureArtifact = string.Equals(normalizedResultStatus, "failed", StringComparison.Ordinal);

            ASMLiteSmokeArtifactReferences artifactRefs = new ASMLiteSmokeArtifactReferences
            {
                resultPath = GetSessionRelativePath(resultPath),
                failurePath = shouldWriteFailureArtifact ? GetSessionRelativePath(failurePath) : string.Empty,
                eventsSlicePath = GetSessionRelativePath(eventsSlicePath),
                nunitPath = GetSessionRelativePath(nunitPath),
                debugSummaryPath = string.Empty
            };

            double durationSeconds = Math.Max(0d, endedAtSeconds - startedAtSeconds);
            ASMLiteSmokeRunResultDocument resultDocument = new ASMLiteSmokeRunResultDocument
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = _sessionId,
                runId = result.RunId,
                result = normalizedResultStatus,
                groupId = result.GroupId,
                groupLabel = groupLabel,
                suiteId = result.SuiteId,
                suiteLabel = string.IsNullOrWhiteSpace(suite.label) ? result.SuiteId : suite.label,
                effectiveResetPolicy = result.EffectiveResetPolicy,
                startedAtUtc = startedAtUtc,
                endedAtUtc = endedAtUtc,
                durationSeconds = durationSeconds,
                firstEventSeq = firstRunEventSeq,
                lastEventSeq = lastRunEventSeq,
                artifactPaths = artifactRefs,
                catalogSnapshotPath = GetSessionRelativePath(_paths.CatalogSnapshotPath)
            };
            ASMLiteSmokeArtifactPaths.WriteResultDocumentAtomically(resultPath, resultDocument, true);

            if (shouldWriteFailureArtifact)
            {
                string[] lastEvents = runEvents.Select(evt => evt.message)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();
                if (lastEvents.Length > 5)
                    lastEvents = lastEvents.Skip(lastEvents.Length - 5).ToArray();

                ASMLiteSmokeFailureDocument failureDocument = ASMLiteSmokeFailureReport.Build(
                    _sessionId,
                    command.commandId,
                    result,
                    _configuration.ScenePath,
                    _configuration.AvatarName,
                    artifactRefs,
                    firstRunEventSeq,
                    lastRunEventSeq,
                    endedAtUtc,
                    lastEvents);
                ASMLiteSmokeArtifactPaths.WriteFailureDocumentAtomically(failurePath, failureDocument, true);
            }
        }

        private static string BuildNUnitXml(
            ASMLiteSmokeSuiteExecutionResult result,
            string commandId,
            string startedAtUtc,
            string endedAtUtc)
        {
            string normalizedCommandId = string.IsNullOrWhiteSpace(commandId)
                ? "cmd_000000_unknown"
                : commandId.Trim();
            string status = result.Succeeded ? "Passed" : "Failed";
            int passed = result.Succeeded ? 1 : 0;
            int failed = result.Succeeded ? 0 : 1;
            string failureMessage = result.Failure == null ? string.Empty : result.Failure.FailureMessage;

            var builder = new StringBuilder();
            builder.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            builder.AppendLine($"<test-run id=\"{EscapeXml(result.RunId)}\" testcasecount=\"1\" result=\"{status}\" total=\"1\" passed=\"{passed}\" failed=\"{failed}\">");
            builder.AppendLine($"  <test-suite type=\"TestSuite\" id=\"{EscapeXml(result.SuiteId)}\" name=\"{EscapeXml(result.SuiteId)}\" result=\"{status}\">");
            builder.AppendLine($"    <properties><property name=\"commandId\" value=\"{EscapeXml(normalizedCommandId)}\" /></properties>");
            builder.AppendLine("    <results>");
            builder.AppendLine($"      <test-case id=\"{EscapeXml(result.SuiteId)}::run\" name=\"{EscapeXml(result.SuiteId)}\" result=\"{status}\">");
            builder.AppendLine($"        <properties><property name=\"startedAtUtc\" value=\"{EscapeXml(startedAtUtc)}\" /><property name=\"endedAtUtc\" value=\"{EscapeXml(endedAtUtc)}\" /></properties>");
            if (!result.Succeeded)
            {
                builder.AppendLine("        <failure>");
                builder.AppendLine($"          <message>{EscapeXml(failureMessage)}</message>");
                builder.AppendLine("        </failure>");
            }
            builder.AppendLine("      </test-case>");
            builder.AppendLine("    </results>");
            builder.AppendLine("  </test-suite>");
            builder.AppendLine("</test-run>");
            return builder.ToString();
        }

        private string GetSessionRelativePath(string absolutePath)
        {
            string rootPath = Path.GetFullPath(_paths.SessionRootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullPath = Path.GetFullPath(absolutePath);
            if (!fullPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Artifact path '{fullPath}' is outside session root '{rootPath}'.");

            string relative = fullPath.Substring(rootPath.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return relative.Replace('\\', '/');
        }

        private static string EscapeXml(string value)
        {
            string escaped = SecurityElement.Escape(value ?? string.Empty);
            return escaped ?? string.Empty;
        }

        private bool ExecuteCatalogStep(ActiveRunState activeRun, ASMLiteSmokeStepDefinition step, out string detail, out string stackTrace)
        {
            if (step == null)
            {
                detail = "Step definition is required.";
                stackTrace = string.Empty;
                return false;
            }

            ASMLiteSmokeStepArgs args = step.args ?? new ASMLiteSmokeStepArgs();
            string scenePath = string.IsNullOrWhiteSpace(args.scenePath) ? _configuration.ScenePath : args.scenePath.Trim();
            string avatarName = string.IsNullOrWhiteSpace(args.avatarName) ? _configuration.AvatarName : args.avatarName.Trim();

            if (!string.IsNullOrWhiteSpace(args.fixtureMutation))
            {
                string evidenceRootPath = Path.Combine(_paths.SessionRootPath, "fixture-evidence");
                bool fixturePassed = _runtime.ApplySetupFixtureMutation(
                    args,
                    _configuration.ScenePath,
                    _configuration.AvatarName,
                    evidenceRootPath,
                    out detail,
                    out stackTrace);
                if (!fixturePassed)
                    return false;

                if (activeRun != null)
                    activeRun.CaseHasAppliedFixtureMutation = true;
            }

            int consoleErrorCheckpoint = _runtime.GetConsoleErrorCheckpoint();
            bool stepPassed = _runtime.ExecuteCatalogStep(step.actionType, args, scenePath, avatarName, out detail, out stackTrace);
            if (_runtime.TryGetConsoleErrorsSince(consoleErrorCheckpoint, out string consoleErrorDetail, out string consoleErrorStackTrace))
            {
                detail = consoleErrorDetail;
                stackTrace = consoleErrorStackTrace;
                return false;
            }

            return stepPassed;
        }

        private void RejectCommand(ASMLiteSmokeProtocolCommand command, string reason)
        {
            string commandId = command == null || string.IsNullOrWhiteSpace(command.commandId)
                ? "cmd_000000_host-bootstrap"
                : command.commandId.Trim();

            int rejectSeq = _lastEventSeq + 1;
            PublishHostState(_currentState, reason, rejectSeq, _lastCommandSeq);
            AppendEventWithSequence(
                rejectSeq,
                "command-rejected",
                _currentState,
                reason,
                commandId);

            _lastEventSeq = rejectSeq;
            _currentMessage = reason;
            if (!string.IsNullOrWhiteSpace(commandId))
                _processedCommandIds.Add(commandId);
        }

        private void HandleShutdownSession(ASMLiteSmokeProtocolCommand command)
        {
            int exitSeq = _lastEventSeq + 1;
            const string exitingMessage = "shutdown-session command accepted; Unity host exiting.";
            PublishHostState(
                ASMLiteSmokeProtocol.HostStateExiting,
                exitingMessage,
                exitSeq,
                _lastCommandSeq);
            AppendEventWithSequence(
                exitSeq,
                "session-exiting",
                ASMLiteSmokeProtocol.HostStateExiting,
                exitingMessage,
                command.commandId);

            _lastEventSeq = exitSeq;
            _currentState = ASMLiteSmokeProtocol.HostStateExiting;
            _currentMessage = exitingMessage;
            _processedCommandIds.Add(command.commandId);

            _isRunning = false;
            UnregisterUpdate();
            _runtime.StopConsoleErrorCapture();
            _runtime.ExitEditorWithoutSaving(0);
        }

        private void PublishStalled(string message)
        {
            string normalizedMessage = string.IsNullOrWhiteSpace(message)
                ? "Unity host heartbeat stalled."
                : message.Trim();

            int stalledSeq = _lastEventSeq + 1;
            PublishHostState(
                ASMLiteSmokeProtocol.HostStateStalled,
                normalizedMessage,
                stalledSeq,
                _lastCommandSeq);
            AppendEventWithSequence(
                stalledSeq,
                "host-stalled",
                ASMLiteSmokeProtocol.HostStateStalled,
                normalizedMessage,
                commandId: string.Empty);

            _lastEventSeq = stalledSeq;
            _currentState = ASMLiteSmokeProtocol.HostStateStalled;
            _currentMessage = normalizedMessage;
        }

        private void PublishCrashed(Exception exception)
        {
            string errorType = exception == null ? "Exception" : exception.GetType().Name;
            string errorMessage = exception == null ? "Unknown crash." : exception.Message;
            string normalizedMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? $"{errorType} while booting Unity host."
                : $"{errorType}: {errorMessage}";

            int crashSeq = _lastEventSeq + 1;
            PublishHostState(
                ASMLiteSmokeProtocol.HostStateCrashed,
                normalizedMessage,
                crashSeq,
                _lastCommandSeq);
            AppendEventWithSequence(
                crashSeq,
                "host-crashed",
                ASMLiteSmokeProtocol.HostStateCrashed,
                normalizedMessage,
                commandId: string.Empty);

            _lastEventSeq = crashSeq;
            _currentState = ASMLiteSmokeProtocol.HostStateCrashed;
            _currentMessage = normalizedMessage;
            _isRunning = false;
            UnregisterUpdate();
        }

        private void RegisterUpdate()
        {
            if (_isUpdateRegistered)
                return;

            _runtime.RegisterUpdate(Tick);
            _isUpdateRegistered = true;
        }

        private void UnregisterUpdate()
        {
            if (!_isUpdateRegistered)
                return;

            _runtime.UnregisterUpdate(Tick);
            _isUpdateRegistered = false;
        }

        private void PublishHostState(string state, string message, int lastEventSeq, int lastCommandSeq)
        {
            string normalizedState = string.IsNullOrWhiteSpace(state) ? ASMLiteSmokeProtocol.HostStateIdle : state.Trim();
            string normalizedMessage = string.IsNullOrWhiteSpace(message) ? "Unity host status update." : message.Trim();
            string unityVersion = _runtime.GetUnityVersion();
            if (string.IsNullOrWhiteSpace(unityVersion))
                unityVersion = "unknown";

            var hostState = new ASMLiteSmokeHostStateDocument
            {
                sessionId = _sessionId,
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                state = normalizedState,
                hostVersion = "asmlite-unity-host-phase09",
                unityVersion = unityVersion,
                heartbeatUtc = _runtime.GetUtcNowIso(),
                lastEventSeq = Math.Max(0, lastEventSeq),
                lastCommandSeq = Math.Max(0, lastCommandSeq),
                activeRunId = ResolveActiveRunIdForHostState(normalizedState),
                message = normalizedMessage,
            };

            ASMLiteSmokeProtocol.WriteHostStateDocumentAtomically(_paths.HostStatePath, hostState, prettyPrint: true);
        }

        private string ResolveActiveRunIdForHostState(string normalizedState)
        {
            if (string.Equals(normalizedState, ASMLiteSmokeProtocol.HostStateRunning, StringComparison.Ordinal)
                && _activeRun != null
                && _activeRun.Result != null
                && !string.IsNullOrWhiteSpace(_activeRun.Result.RunId))
                return _activeRun.Result.RunId;

            if (string.Equals(normalizedState, ASMLiteSmokeProtocol.HostStateReviewRequired, StringComparison.Ordinal))
                return _activeReviewRunId;

            return string.Empty;
        }

        private void AppendEventWithSequence(
            int eventSeq,
            string eventType,
            string hostState,
            string message,
            string commandId,
            string runId = "",
            string groupId = "",
            string suiteId = "",
            string caseId = "",
            string stepId = "",
            string effectiveResetPolicy = "",
            string[] reviewDecisionOptions = null)
        {
            string normalizedCommandId = string.IsNullOrWhiteSpace(commandId)
                ? "cmd_000000_host-bootstrap"
                : commandId.Trim();

            var protocolEvent = new ASMLiteSmokeProtocolEvent
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = _sessionId,
                eventId = $"evt_{eventSeq:D6}_{eventType}",
                eventSeq = eventSeq,
                eventType = eventType,
                timestampUtc = _runtime.GetUtcNowIso(),
                commandId = normalizedCommandId,
                runId = string.IsNullOrWhiteSpace(runId) ? string.Empty : runId.Trim(),
                groupId = string.IsNullOrWhiteSpace(groupId) ? string.Empty : groupId.Trim(),
                suiteId = string.IsNullOrWhiteSpace(suiteId) ? string.Empty : suiteId.Trim(),
                caseId = string.IsNullOrWhiteSpace(caseId) ? string.Empty : caseId.Trim(),
                stepId = string.IsNullOrWhiteSpace(stepId) ? string.Empty : stepId.Trim(),
                effectiveResetPolicy = string.IsNullOrWhiteSpace(effectiveResetPolicy) ? string.Empty : effectiveResetPolicy.Trim(),
                hostState = hostState,
                message = message,
                reviewDecisionOptions = reviewDecisionOptions == null
                    ? Array.Empty<string>()
                    : reviewDecisionOptions
                        .Where(option => !string.IsNullOrWhiteSpace(option))
                        .Select(option => option.Trim())
                        .ToArray(),
                supportedCapabilities = new[]
                {
                    "suiteCatalogV1",
                    "sessionArtifactPaths",
                    "reviewDecision",
                    "testFilterNamespaces",
                },
            };

            ASMLiteSmokeProtocol.AppendEventLine(_paths.EventsLogPath, protocolEvent);
        }

        private static string ResolveSessionId(string sessionRootPath)
        {
            if (string.IsNullOrWhiteSpace(sessionRootPath))
                return "smoke-session";

            string trimmed = sessionRootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string leaf = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(leaf) ? "smoke-session" : leaf;
        }

        private static int RecoverLastEventSequence(string eventsLogPath)
        {
            try
            {
                ASMLiteSmokeProtocolEvent[] events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(eventsLogPath);
                if (events == null || events.Length == 0)
                    return 0;

                return events.Max(item => item != null ? item.eventSeq : 0);
            }
            catch
            {
                return 0;
            }
        }
    }
}
