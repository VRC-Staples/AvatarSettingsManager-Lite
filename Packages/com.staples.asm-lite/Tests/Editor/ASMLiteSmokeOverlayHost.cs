using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private string _currentState = ASMLiteSmokeProtocol.HostStateIdle;
        private string _currentMessage = "Unity host booting.";
        private bool _isRunning;
        private bool _isUpdateRegistered;
        private double _lastHeartbeatWriteAt;

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
                    RejectCommand(command, "run-suite received before Phase 08 suite executor is available.");
                    break;

                case "review-decision":
                    RejectCommand(command, "review-decision received before Phase 09 review gate is available.");
                    break;

                default:
                    RejectCommand(command, $"{command.commandType} is not implemented in this host phase.");
                    break;
            }
        }

        private void RejectCommand(ASMLiteSmokeProtocolCommand command, string reason)
        {
            int rejectSeq = _lastEventSeq + 1;
            PublishHostState(_currentState, reason, rejectSeq, _lastCommandSeq);
            AppendEventWithSequence(
                rejectSeq,
                "command-rejected",
                _currentState,
                reason,
                command.commandId);

            _lastEventSeq = rejectSeq;
            _currentMessage = reason;
            _processedCommandIds.Add(command.commandId);
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
                hostVersion = "asmlite-unity-host-phase06",
                unityVersion = unityVersion,
                heartbeatUtc = _runtime.GetUtcNowIso(),
                lastEventSeq = Math.Max(0, lastEventSeq),
                lastCommandSeq = Math.Max(0, lastCommandSeq),
                activeRunId = string.Empty,
                message = normalizedMessage,
            };

            ASMLiteSmokeProtocol.WriteHostStateDocumentAtomically(_paths.HostStatePath, hostState, prettyPrint: true);
        }

        private void AppendEventWithSequence(
            int eventSeq,
            string eventType,
            string hostState,
            string message,
            string commandId)
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
                runId = string.Empty,
                groupId = string.Empty,
                suiteId = string.Empty,
                caseId = string.Empty,
                stepId = string.Empty,
                effectiveResetPolicy = string.Empty,
                hostState = hostState,
                message = message,
                reviewDecisionOptions = Array.Empty<string>(),
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
