using System;
using System.Collections.Generic;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    internal enum ASMLiteSmokeCommandDispatchMode
    {
        StartupOnly,
        PolledCommand,
    }

    internal sealed class ASMLiteSmokeCommandDefinition
    {
        internal ASMLiteSmokeCommandDefinition(string commandType, string payloadFieldName, ASMLiteSmokeCommandDispatchMode dispatchMode)
        {
            this.commandType = RequireToken(commandType, nameof(commandType));
            this.payloadFieldName = string.IsNullOrWhiteSpace(payloadFieldName) ? string.Empty : payloadFieldName.Trim();
            this.dispatchMode = dispatchMode;
        }

        public string commandType;
        public string payloadFieldName;
        public ASMLiteSmokeCommandDispatchMode dispatchMode;

        private static string RequireToken(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(fieldName + " must not be blank.");
            return value.Trim();
        }
    }

    internal static class ASMLiteSmokeCommandRegistry
    {
        internal const string LaunchSession = "launch-session";
        internal const string RunSuite = "run-suite";
        internal const string ReviewDecision = "review-decision";
        internal const string AbortRun = "abort-run";
        internal const string ShutdownSession = "shutdown-session";

        private static readonly ASMLiteSmokeCommandDefinition[] s_definitions =
        {
            new ASMLiteSmokeCommandDefinition(LaunchSession, "launchSession", ASMLiteSmokeCommandDispatchMode.StartupOnly),
            new ASMLiteSmokeCommandDefinition(RunSuite, "runSuite", ASMLiteSmokeCommandDispatchMode.PolledCommand),
            new ASMLiteSmokeCommandDefinition(ReviewDecision, "reviewDecision", ASMLiteSmokeCommandDispatchMode.PolledCommand),
            new ASMLiteSmokeCommandDefinition(AbortRun, "abortRun", ASMLiteSmokeCommandDispatchMode.PolledCommand),
            new ASMLiteSmokeCommandDefinition(ShutdownSession, string.Empty, ASMLiteSmokeCommandDispatchMode.PolledCommand),
        };

        private static readonly Dictionary<string, ASMLiteSmokeCommandDefinition> s_byType = s_definitions
            .ToDictionary(definition => definition.commandType, StringComparer.Ordinal);

        internal static IReadOnlyList<ASMLiteSmokeCommandDefinition> AllDefinitions => s_definitions;

        internal static IEnumerable<string> AllCommandTypes => s_definitions.Select(definition => definition.commandType);

        internal static IEnumerable<string> PolledCommandTypes => s_definitions
            .Where(definition => definition.dispatchMode == ASMLiteSmokeCommandDispatchMode.PolledCommand)
            .Select(definition => definition.commandType);

        internal static bool IsSupported(string commandType)
        {
            string normalized = NormalizeToken(commandType);
            return normalized.Length > 0 && s_byType.ContainsKey(normalized);
        }

        internal static bool IsPolledCommand(string commandType)
        {
            ASMLiteSmokeCommandDefinition definition;
            return TryGet(commandType, out definition)
                && definition.dispatchMode == ASMLiteSmokeCommandDispatchMode.PolledCommand;
        }

        internal static ASMLiteSmokeCommandDefinition GetRequired(string commandType)
        {
            ASMLiteSmokeCommandDefinition definition;
            if (!TryGet(commandType, out definition))
                throw new InvalidOperationException($"commandType '{NormalizeToken(commandType)}' is not supported.");
            return definition;
        }

        internal static bool TryGet(string commandType, out ASMLiteSmokeCommandDefinition definition)
        {
            return s_byType.TryGetValue(NormalizeToken(commandType), out definition);
        }

        internal static string NormalizeToken(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }

    internal sealed class ASMLiteSmokeActionDefinition
    {
        internal ASMLiteSmokeActionDefinition(string actionType)
        {
            this.actionType = ASMLiteSmokeActionRegistry.NormalizeActionType(actionType);
            if (this.actionType.Length == 0)
                throw new InvalidOperationException("actionType must not be blank.");
        }

        public string actionType;
    }

    internal static class ASMLiteSmokeActionRegistry
    {
        private static readonly ASMLiteSmokeActionDefinition[] s_definitions =
        {
            new ASMLiteSmokeActionDefinition("open-scene"),
            new ASMLiteSmokeActionDefinition("open-window"),
            new ASMLiteSmokeActionDefinition("select-avatar"),
            new ASMLiteSmokeActionDefinition("add-prefab"),
            new ASMLiteSmokeActionDefinition("rebuild"),
            new ASMLiteSmokeActionDefinition("vendorize"),
            new ASMLiteSmokeActionDefinition("detach"),
            new ASMLiteSmokeActionDefinition("lifecycle-hygiene-cleanup"),
            new ASMLiteSmokeActionDefinition("return-to-package-managed"),
            new ASMLiteSmokeActionDefinition("enter-playmode"),
            new ASMLiteSmokeActionDefinition("exit-playmode"),
            new ASMLiteSmokeActionDefinition("run-av3-save-load-harness"),
            new ASMLiteSmokeActionDefinition("assert-av3-save-load-result"),
            new ASMLiteSmokeActionDefinition("assert-primary-action"),
            new ASMLiteSmokeActionDefinition("assert-generated-references-package-managed"),
            new ASMLiteSmokeActionDefinition("assert-runtime-component-valid"),
            new ASMLiteSmokeActionDefinition("assert-package-resource-present"),
            new ASMLiteSmokeActionDefinition("assert-catalog-loads"),
            new ASMLiteSmokeActionDefinition("assert-window-focused"),
            new ASMLiteSmokeActionDefinition("close-window"),
            new ASMLiteSmokeActionDefinition("assert-host-ready"),
            new ASMLiteSmokeActionDefinition("prelude-recover-context"),
            new ASMLiteSmokeActionDefinition("assert-no-component"),
            new ASMLiteSmokeActionDefinition("set-slot-count"),
            new ASMLiteSmokeActionDefinition("set-install-path-state"),
            new ASMLiteSmokeActionDefinition("set-root-name-state"),
            new ASMLiteSmokeActionDefinition("set-preset-name-mask"),
            new ASMLiteSmokeActionDefinition("set-action-label-mask"),
            new ASMLiteSmokeActionDefinition("set-icon-mode"),
            new ASMLiteSmokeActionDefinition("set-gear-color"),
            new ASMLiteSmokeActionDefinition("set-custom-icons-enabled"),
            new ASMLiteSmokeActionDefinition("set-root-icon-fixture"),
            new ASMLiteSmokeActionDefinition("set-slot-icon-mask"),
            new ASMLiteSmokeActionDefinition("set-action-icon-mask"),
            new ASMLiteSmokeActionDefinition("assert-parameter-backup-option-present"),
            new ASMLiteSmokeActionDefinition("set-parameter-backup-state"),
            new ASMLiteSmokeActionDefinition("assert-pending-customization-snapshot"),
            new ASMLiteSmokeActionDefinition("assert-attached-customization-snapshot"),
        };

        private static readonly HashSet<string> s_supportedActionTypes = new HashSet<string>(
            s_definitions.Select(definition => definition.actionType),
            StringComparer.Ordinal);

        internal static IReadOnlyList<ASMLiteSmokeActionDefinition> AllDefinitions => s_definitions;

        internal static IEnumerable<string> AllActionTypes => s_definitions.Select(definition => definition.actionType);

        internal static bool IsSupported(string actionType)
        {
            string normalized = NormalizeActionType(actionType);
            return normalized.Length > 0 && s_supportedActionTypes.Contains(normalized);
        }

        internal static string NormalizeActionType(string actionType)
        {
            return string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim();
        }
    }

    internal sealed class ASMLiteSmokeStepCommand
    {
        private ASMLiteSmokeStepCommand(string actionType, ASMLiteSmokeStepArgs args, string scenePath, string avatarName)
        {
            ActionType = actionType;
            Args = args;
            ScenePath = scenePath;
            AvatarName = avatarName;
        }

        internal string ActionType { get; }
        internal ASMLiteSmokeStepArgs Args { get; }
        internal string ScenePath { get; }
        internal string AvatarName { get; }

        internal static ASMLiteSmokeStepCommand FromStep(
            ASMLiteSmokeStepDefinition step,
            string defaultScenePath,
            string defaultAvatarName)
        {
            if (step == null)
                throw new InvalidOperationException("Step definition is required.");

            string actionType = ASMLiteSmokeActionRegistry.NormalizeActionType(step.actionType);
            if (!ASMLiteSmokeActionRegistry.IsSupported(actionType))
                throw new InvalidOperationException($"Smoke actionType '{actionType}' is not supported.");

            ASMLiteSmokeStepArgs args = step.args ?? new ASMLiteSmokeStepArgs();
            args.Normalize();

            string scenePath = string.IsNullOrWhiteSpace(args.scenePath)
                ? RequireNonBlank(defaultScenePath, nameof(defaultScenePath))
                : args.scenePath.Trim();
            string avatarName = string.IsNullOrWhiteSpace(args.avatarName)
                ? RequireNonBlank(defaultAvatarName, nameof(defaultAvatarName))
                : args.avatarName.Trim();

            return new ASMLiteSmokeStepCommand(actionType, args, scenePath, avatarName);
        }

        private static string RequireNonBlank(string value, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(fieldName + " must not be blank.");
            return value.Trim();
        }
    }

    internal sealed class ASMLiteSmokeHostStateMachine
    {
        internal ASMLiteSmokeHostStateMachine(string state, string message)
        {
            State = NormalizeState(state);
            Message = NormalizeMessage(message);
        }

        internal string State { get; private set; }
        internal string Message { get; private set; }

        internal void TransitionTo(string state, string message)
        {
            State = NormalizeState(state);
            Message = NormalizeMessage(message);
        }

        private static string NormalizeState(string state)
        {
            string normalized = string.IsNullOrWhiteSpace(state) ? string.Empty : state.Trim();
            if (!ASMLiteSmokeProtocol.IsSupportedHostState(normalized))
                throw new InvalidOperationException($"host state '{normalized}' is not supported.");
            return normalized;
        }

        private static string NormalizeMessage(string message)
        {
            return string.IsNullOrWhiteSpace(message) ? "Smoke host state updated." : message.Trim();
        }
    }
}
