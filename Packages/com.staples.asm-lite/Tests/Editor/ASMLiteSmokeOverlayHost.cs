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
        double GetTimeSinceStartup();
        string GetUtcNowIso();
        string GetUnityVersion();
        void RegisterUpdate(EditorApplication.CallbackFunction tick);
        void UnregisterUpdate(EditorApplication.CallbackFunction tick);
        string[] EnumerateCommandFiles(string commandsDirectoryPath);
        string ReadAllText(string path);
        bool ExecuteCatalogStep(string actionType, string avatarName, out string detail, out string stackTrace);
        void ExitEditor(int exitCode);
    }

    internal sealed class ASMLiteSmokeOverlayHostUnityRuntime : IASMLiteSmokeOverlayHostRuntime
    {
        internal static readonly ASMLiteSmokeOverlayHostUnityRuntime Instance = new ASMLiteSmokeOverlayHostUnityRuntime();

        private ASMLiteSmokeOverlayHostUnityRuntime()
        {
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
            var avatars = Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>();
            for (int i = 0; i < avatars.Length; i++)
            {
                VRCAvatarDescriptor avatar = avatars[i];
                if (avatar != null
                    && avatar.gameObject != null
                    && string.Equals(avatar.gameObject.name, avatarName, StringComparison.Ordinal))
                {
                    return avatar;
                }
            }

            return null;
        }

        public void SelectAvatarForAutomation(VRCAvatarDescriptor avatar)
        {
            var window = ASMLiteWindow.OpenForAutomation();
            window.SelectAvatarForAutomation(avatar);
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

        public bool ExecuteCatalogStep(string actionType, string avatarName, out string detail, out string stackTrace)
        {
            detail = string.Empty;
            stackTrace = string.Empty;

            try
            {
                string normalizedAction = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim();
                var window = ASMLiteWindow.OpenForAutomation();

                switch (normalizedAction)
                {
                    case "open-window":
                        detail = "ASM-Lite window opened for automation.";
                        return true;

                    case "select-avatar":
                    {
                        VRCAvatarDescriptor avatar = FindAvatarByName(avatarName);
                        if (avatar == null)
                        {
                            detail = $"Avatar '{avatarName}' was not found.";
                            return false;
                        }

                        window.SelectAvatarForAutomation(avatar);
                        detail = $"Selected avatar '{avatarName}'.";
                        return true;
                    }

                    case "add-prefab":
                        SelectAvatarIfFound(window, avatarName);
                        window.AddPrefabForAutomation();
                        detail = "ASM-Lite prefab added.";
                        return true;

                    case "rebuild":
                        SelectAvatarIfFound(window, avatarName);
                        window.RebuildForAutomation();
                        detail = "Rebuild completed.";
                        return true;

                    case "vendorize":
                        SelectAvatarIfFound(window, avatarName);
                        window.VendorizeForAutomation();
                        detail = "Vendorize completed.";
                        return true;

                    case "detach":
                        SelectAvatarIfFound(window, avatarName);
                        window.DetachForAutomation();
                        detail = "Detach completed.";
                        return true;

                    case "return-to-package-managed":
                        SelectAvatarIfFound(window, avatarName);
                        window.ReturnToPackageManagedForAutomation();
                        detail = "Return-to-package-managed completed.";
                        return true;

                    case "assert-primary-action":
                    {
                        SelectAvatarIfFound(window, avatarName);
                        var hierarchy = window.GetActionHierarchyContract();
                        bool hasPrimaryRebuild = hierarchy.HasPrimaryAction(ASMLiteWindow.AsmLiteWindowAction.Rebuild);
                        detail = hasPrimaryRebuild
                            ? "Primary rebuild action is available."
                            : "Primary rebuild action is not available.";
                        return hasPrimaryRebuild;
                    }

                    case "enter-playmode":
                        EditorApplication.isPlaying = true;
                        detail = "Entered playmode.";
                        return true;

                    case "assert-runtime-component-valid":
                    {
                        VRCAvatarDescriptor avatar = FindAvatarByName(avatarName);
                        bool valid = avatar != null && avatar.GetComponentInChildren<ASMLiteComponent>(true) != null;
                        detail = valid
                            ? "Runtime component check passed."
                            : "Runtime component check failed: ASM-Lite component was not found on the selected avatar.";
                        return valid;
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

        private void SelectAvatarIfFound(ASMLiteWindow window, string avatarName)
        {
            if (window == null)
                return;

            VRCAvatarDescriptor avatar = FindAvatarByName(avatarName);
            if (avatar != null)
                window.SelectAvatarForAutomation(avatar);
        }

        public void ExitEditor(int exitCode)
        {
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

        private static readonly string[] s_reviewDecisionOptions =
        {
            "return-to-suite-list",
            "rerun-suite",
            "exit",
        };

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
            ClearActiveReviewContext();

            try
            {
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

                string activeScene = _runtime.GetActiveScenePath();
                if (!string.Equals(activeScene, _configuration.ScenePath, StringComparison.Ordinal))
                    _runtime.OpenScene(_configuration.ScenePath);

                VRCAvatarDescriptor avatar = _runtime.FindAvatarByName(_configuration.AvatarName);
                if (avatar == null)
                    throw new InvalidOperationException($"Avatar '{_configuration.AvatarName}' was not found in the loaded scene.");

                _runtime.SelectAvatarForAutomation(avatar);

                string readyMessage = $"Unity host loaded {_configuration.ScenePath} and selected {_configuration.AvatarName}.";
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
                    _runtime.ExitEditor(0);
                    return;
                }

                _isRunning = true;
                RegisterUpdate();
            }
            catch (Exception ex)
            {
                PublishCrashed(ex);
                if (_configuration.ExitOnReady)
                    throw;
            }
        }

        internal void StopForTesting()
        {
            _isRunning = false;
            UnregisterUpdate();
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

            ProcessCommandFiles();
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

                string runStartedAtUtc = _runtime.GetUtcNowIso();
                double runStartedAtSeconds = _runtime.GetTimeSinceStartup();
                int runOrdinal = _runOrdinal + 1;

                string runningMessage = $"Running suite '{suite.suiteId}' with effective reset policy '{effectiveResetPolicy}'.";
                PublishHostState(
                    ASMLiteSmokeProtocol.HostStateRunning,
                    runningMessage,
                    _lastEventSeq,
                    _lastCommandSeq);
                _currentState = ASMLiteSmokeProtocol.HostStateRunning;
                _currentMessage = runningMessage;

                ASMLiteSmokeSuiteExecutionResult result = ASMLiteSmokeRunExecutor.Execute(
                    catalog,
                    command,
                    effectiveResetPolicy,
                    ExecuteCatalogStep);

                int firstRunEventSeq = _lastEventSeq + 1;
                for (int i = 0; i < result.Events.Count; i++)
                {
                    ASMLiteSmokeExecutionEventPayload payload = result.Events[i];
                    int eventSeq = _lastEventSeq + 1;
                    string eventHostState = string.Equals(payload.EventType, "suite-passed", StringComparison.Ordinal)
                        ? ASMLiteSmokeProtocol.HostStateReady
                        : ASMLiteSmokeProtocol.HostStateRunning;

                    AppendEventWithSequence(
                        eventSeq,
                        payload.EventType,
                        eventHostState,
                        payload.Message,
                        command.commandId,
                        runId: result.RunId,
                        groupId: payload.GroupId,
                        suiteId: payload.SuiteId,
                        caseId: payload.CaseId,
                        stepId: payload.StepId,
                        effectiveResetPolicy: payload.EffectiveResetPolicy);
                    _lastEventSeq = eventSeq;
                }

                int lastRunEventSeq = _lastEventSeq;
                string runEndedAtUtc = _runtime.GetUtcNowIso();
                double runEndedAtSeconds = _runtime.GetTimeSinceStartup();
                EmitRunArtifacts(
                    catalog,
                    suite,
                    command,
                    result,
                    runOrdinal,
                    runStartedAtUtc,
                    runEndedAtUtc,
                    runStartedAtSeconds,
                    runEndedAtSeconds,
                    firstRunEventSeq,
                    lastRunEventSeq);

                string completionMessage;
                if (result.Succeeded)
                {
                    completionMessage = $"Suite '{result.SuiteId}' passed and is waiting for operator review.";
                }
                else
                {
                    string failedStep = result.Failure == null ? string.Empty : result.Failure.StepId;
                    completionMessage = string.IsNullOrWhiteSpace(failedStep)
                        ? $"Suite '{result.SuiteId}' failed and is waiting for operator review."
                        : $"Suite '{result.SuiteId}' failed at step '{failedStep}' and is waiting for operator review.";
                }

                CaptureActiveReviewContext(command, result);

                _currentState = ASMLiteSmokeProtocol.HostStateReviewRequired;
                _currentMessage = completionMessage;

                int reviewRequiredSeq = _lastEventSeq + 1;
                AppendEventWithSequence(
                    reviewRequiredSeq,
                    "review-required",
                    ASMLiteSmokeProtocol.HostStateReviewRequired,
                    "Choose Return to Suite List, Rerun Suite, or Exit.",
                    command.commandId,
                    runId: result.RunId,
                    groupId: result.GroupId,
                    suiteId: result.SuiteId,
                    reviewDecisionOptions: s_reviewDecisionOptions);
                _lastEventSeq = reviewRequiredSeq;

                PublishHostState(
                    ASMLiteSmokeProtocol.HostStateReviewRequired,
                    completionMessage,
                    _lastEventSeq,
                    _lastCommandSeq);

                _processedCommandIds.Add(command.commandId);
                _runOrdinal = runOrdinal;
            }
            catch (Exception ex)
            {
                string message = string.IsNullOrWhiteSpace(ex.Message)
                    ? "run-suite execution failed."
                    : $"run-suite execution failed: {ex.Message}";
                RejectCommand(command, message);
            }
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
            int lastRunEventSeq)
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

            ASMLiteSmokeArtifactReferences artifactRefs = new ASMLiteSmokeArtifactReferences
            {
                resultPath = GetSessionRelativePath(resultPath),
                failurePath = result.Succeeded ? string.Empty : GetSessionRelativePath(failurePath),
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
                result = result.Succeeded ? "passed" : "failed",
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

            if (!result.Succeeded)
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

        private bool ExecuteCatalogStep(ASMLiteSmokeStepDefinition step, out string detail, out string stackTrace)
        {
            if (step == null)
            {
                detail = "Step definition is required.";
                stackTrace = string.Empty;
                return false;
            }

            return _runtime.ExecuteCatalogStep(step.actionType, _configuration.AvatarName, out detail, out stackTrace);
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
            _runtime.ExitEditor(0);
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
                activeRunId = string.Equals(normalizedState, ASMLiteSmokeProtocol.HostStateReviewRequired, StringComparison.Ordinal)
                    ? _activeReviewRunId
                    : string.Empty,
                message = normalizedMessage,
            };

            ASMLiteSmokeProtocol.WriteHostStateDocumentAtomically(_paths.HostStatePath, hostState, prettyPrint: true);
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
