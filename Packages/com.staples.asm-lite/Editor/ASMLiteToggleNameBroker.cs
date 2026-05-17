using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using ASMLite;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ASMLite.Editor
{
    /// <summary>
    /// Reflection-safe helper for deterministic VRCFury Toggle global-name enrollment.
    ///
    /// Discovery scope: discovery + deterministic naming + serialized field mutation records.
    /// Build-request scope: build-request enrollment callback wiring + delayed restore lifecycle.
    /// </summary>
    internal static class ASMLiteToggleNameBroker
    {
        internal const string GlobalPrefix = "ASM_VF_";
        internal const string DefaultToggleTypeFullName = "VF.Model.Feature.Toggle";
        internal const string DefaultFullControllerTypeFullName = "VF.Model.Feature.FullController";

        private const string SessionRestoreKey = "ASMLite.ToggleBroker.PendingRestore";
        private const string FullControllerGlobalRestoreKey = "ASMLite.FullControllerGlobals.PendingRestore";
        private static bool s_restoreDelayQueued;
        private static bool s_startupRestoreQueued;
        private static bool s_hasLatestEnrollmentReport;
        private static EnrollmentReport s_latestEnrollmentReport;
        private static GlobalParamMapping[] s_latestGlobalParamMappings = Array.Empty<GlobalParamMapping>();
        private static FullControllerGlobalEnrollmentReport s_latestFullControllerGlobalEnrollmentReport;

        private static readonly string[] s_menuPathCandidateFields =
        {
            "menuPath",
            "name",
            "label",
            "paramName",
        };

        private static readonly string[] s_useGlobalCandidateFields = { "useGlobalParam" };
        private static readonly string[] s_globalParamCandidateFields = { "globalParam" };
        private static readonly string[] s_sliderCandidateFields = { "slider" };
        private static readonly string[] s_useIntCandidateFields = { "useInt" };
        private static readonly string[] s_defaultOnCandidateFields = { "defaultOn" };
        private static readonly string[] s_defaultSliderValueCandidateFields = { "defaultSliderValue" };
        private static readonly string[] s_savedCandidateFields = { "saved" };
        private static readonly Regex s_restoreEntryRegex = new Regex(
            "\\{\\s*\\\"componentInstanceId\\\"\\s*:\\s*(?<id>-?\\d+)\\s*,\\s*\\\"objectPath\\\"\\s*:\\s*\\\"(?<objectPath>(?:\\\\.|[^\\\"\\\\])*)\\\"\\s*,\\s*\\\"togglePropertyPath\\\"\\s*:\\s*\\\"(?<togglePath>(?:\\\\.|[^\\\"\\\\])*)\\\"\\s*,\\s*\\\"originalUseGlobalParam\\\"\\s*:\\s*(?<useGlobal>true|false)\\s*,\\s*\\\"originalGlobalParam\\\"\\s*:\\s*\\\"(?<originalGlobal>(?:\\\\.|[^\\\"\\\\])*)\\\"\\s*,\\s*\\\"assignedGlobalParam\\\"\\s*:\\s*\\\"(?<assignedGlobal>(?:\\\\.|[^\\\"\\\\])*)\\\"\\s*\\}",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        [Serializable]
        private sealed class RestorePayload
        {
            public RestoreEntry[] entries;
        }

        [Serializable]
        private sealed class RestoreEntry
        {
            public int componentInstanceId;
            public string objectPath;
            public string togglePropertyPath;
            public bool originalUseGlobalParam;
            public string originalGlobalParam;
            public string assignedGlobalParam;
        }

        [Serializable]
        private sealed class FullControllerGlobalRestorePayload
        {
            public FullControllerGlobalRestoreEntry[] entries;
        }

        [Serializable]
        private sealed class FullControllerGlobalRestoreEntry
        {
            public int componentInstanceId;
            public string objectPath;
            public string fullControllerPropertyPath;
            public string[] originalGlobalParams;
        }

        private readonly struct PlannedCandidateAssignment
        {
            public PlannedCandidateAssignment(int originalIndex, string baseName, string tieBreakKey, int tieBreakComponentId)
            {
                OriginalIndex = originalIndex;
                BaseName = baseName;
                TieBreakKey = tieBreakKey;
                TieBreakComponentId = tieBreakComponentId;
            }

            public int OriginalIndex { get; }
            public string BaseName { get; }
            public string TieBreakKey { get; }
            public int TieBreakComponentId { get; }
        }

        internal readonly struct ToggleCandidate
        {
            public ToggleCandidate(Component component, string togglePropertyPath, string menuPathHint, string objectPath, bool useGlobalParam, string globalParam)
            {
                Component = component;
                TogglePropertyPath = togglePropertyPath;
                MenuPathHint = menuPathHint;
                ObjectPath = objectPath;
                UseGlobalParam = useGlobalParam;
                GlobalParam = globalParam;
            }

            public Component Component { get; }
            public string TogglePropertyPath { get; }
            public string MenuPathHint { get; }
            public string ObjectPath { get; }
            public bool UseGlobalParam { get; }
            public string GlobalParam { get; }
        }

        internal readonly struct ToggleMutationRecord
        {
            public ToggleMutationRecord(int componentInstanceId, string objectPath, string togglePropertyPath, bool originalUseGlobalParam, string originalGlobalParam, string assignedGlobalParam)
            {
                ComponentInstanceId = componentInstanceId;
                ObjectPath = objectPath;
                TogglePropertyPath = togglePropertyPath;
                OriginalUseGlobalParam = originalUseGlobalParam;
                OriginalGlobalParam = originalGlobalParam;
                AssignedGlobalParam = assignedGlobalParam;
            }

            public int ComponentInstanceId { get; }
            public string ObjectPath { get; }
            public string TogglePropertyPath { get; }
            public bool OriginalUseGlobalParam { get; }
            public string OriginalGlobalParam { get; }
            public string AssignedGlobalParam { get; }
        }

        internal readonly struct GlobalParamMapping
        {
            public GlobalParamMapping(string originalGlobalParam, string assignedGlobalParam)
            {
                OriginalGlobalParam = originalGlobalParam;
                AssignedGlobalParam = assignedGlobalParam;
            }

            public string OriginalGlobalParam { get; }
            public string AssignedGlobalParam { get; }
        }

        internal readonly struct EnrollmentReport
        {
            public EnrollmentReport(
                int avatarScanCount,
                int candidateCount,
                int enrolledCount,
                int preReservedNameCount,
                int preflightCollisionAdjustments,
                int candidateCollisionAdjustments,
                int staleCleanupRestoredCount,
                int staleCleanupUnresolvedCount)
            {
                AvatarScanCount = avatarScanCount;
                CandidateCount = candidateCount;
                EnrolledCount = enrolledCount;
                PreReservedNameCount = preReservedNameCount;
                PreflightCollisionAdjustments = preflightCollisionAdjustments;
                CandidateCollisionAdjustments = candidateCollisionAdjustments;
                StaleCleanupRestoredCount = staleCleanupRestoredCount;
                StaleCleanupUnresolvedCount = staleCleanupUnresolvedCount;
            }

            public int AvatarScanCount { get; }
            public int CandidateCount { get; }
            public int EnrolledCount { get; }
            public int PreReservedNameCount { get; }
            public int PreflightCollisionAdjustments { get; }
            public int CandidateCollisionAdjustments { get; }
            public int StaleCleanupRestoredCount { get; }
            public int StaleCleanupUnresolvedCount { get; }
        }

        internal readonly struct FullControllerGlobalEnrollmentReport
        {
            public FullControllerGlobalEnrollmentReport(int avatarScanCount, int fullControllerCount, int promotedParameterCount, int restoredCount, int unresolvedCount)
            {
                AvatarScanCount = avatarScanCount;
                FullControllerCount = fullControllerCount;
                PromotedParameterCount = promotedParameterCount;
                RestoredCount = restoredCount;
                UnresolvedCount = unresolvedCount;
            }

            public int AvatarScanCount { get; }
            public int FullControllerCount { get; }
            public int PromotedParameterCount { get; }
            public int RestoredCount { get; }
            public int UnresolvedCount { get; }
        }

        internal readonly struct RestoreReport
        {
            public RestoreReport(int restoredCount, int unresolvedCount, bool malformedPayload)
            {
                RestoredCount = restoredCount;
                UnresolvedCount = unresolvedCount;
                MalformedPayload = malformedPayload;
            }

            public int RestoredCount { get; }
            public int UnresolvedCount { get; }
            public bool MalformedPayload { get; }
        }

        internal static bool HasAsmLiteScope(GameObject avatarRoot)
        {
            if (avatarRoot == null)
                return false;

            return avatarRoot.GetComponentInChildren<ASMLiteComponent>(includeInactive: true) != null;
        }

        internal static List<VRCAvatarDescriptor> FindAsmLiteAvatarDescriptors()
        {
            var descriptors = UnityEngine.Object.FindObjectsByType<VRCAvatarDescriptor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var result = new List<VRCAvatarDescriptor>(descriptors.Length);

            for (int i = 0; i < descriptors.Length; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor == null)
                    continue;

                if (HasAsmLiteScope(descriptor.gameObject))
                    result.Add(descriptor);
            }

            return result;
        }

        internal static EnrollmentReport EnrollForBuildRequest()
        {
            // If a previous build request crashed or skipped delayCall restore, clean it before mutating again.
            var staleCleanup = RestorePendingMutations(warnOnNoData: false);
            return EnrollDescriptors(FindAsmLiteAvatarDescriptors(), staleCleanup, RestoreScheduling.Delayed);
        }

        internal static EnrollmentReport EnrollAvatarForPreprocess(GameObject avatarRoot)
        {
            // Play Mode enters through OnPreprocessAvatar without a preceding SDK build-request event.
            // Enroll the avatar currently being preprocessed so VRCFury consumes deterministic globals.
            var staleCleanup = RestorePendingMutations(warnOnNoData: false);
            CancelDelayedRestoreQueue();
            var descriptors = new List<VRCAvatarDescriptor>(1);
            var descriptor = avatarRoot != null ? avatarRoot.GetComponent<VRCAvatarDescriptor>() : null;
            if (descriptor != null && HasAsmLiteScope(descriptor.gameObject))
                descriptors.Add(descriptor);

            return EnrollDescriptors(descriptors, staleCleanup, RestoreScheduling.Delayed);
        }

        private enum RestoreScheduling
        {
            Delayed,
        }

        private static EnrollmentReport EnrollDescriptors(
            IReadOnlyList<VRCAvatarDescriptor> descriptors,
            RestoreReport staleCleanup,
            RestoreScheduling restoreScheduling)
        {
            int descriptorCount = descriptors?.Count ?? 0;
            int candidateCount = 0;
            int enrolledCount = 0;
            int preReservedNameCount = 0;
            int preflightCollisionAdjustments = 0;
            int candidateCollisionAdjustments = 0;
            var allMutations = new List<ToggleMutationRecord>();

            for (int i = 0; i < descriptorCount; i++)
            {
                var descriptor = descriptors[i];
                if (descriptor == null)
                    continue;

                var candidates = DiscoverEligibleToggleCandidates(descriptor.gameObject);
                candidateCount += candidates.Count;

                var preReservedNames = BuildPreReservedDescriptorNames(descriptor, out int descriptorReservedCount);
                preReservedNameCount += descriptorReservedCount;

                string[] plannedNames = PlanDeterministicGlobalNames(
                    candidates,
                    preReservedNames,
                    out int descriptorPreflightAdjustments,
                    out int descriptorCandidateAdjustments);
                preflightCollisionAdjustments += descriptorPreflightAdjustments;
                candidateCollisionAdjustments += descriptorCandidateAdjustments;

                for (int c = 0; c < candidates.Count; c++)
                {
                    var candidate = candidates[c];
                    string deterministicName = plannedNames[c];

                    if (TryEnrollToggleCandidate(candidate, deterministicName, out var record))
                    {
                        allMutations.Add(record);
                        enrolledCount++;
                    }
                }
            }

            if (allMutations.Count > 0)
            {
                PersistPendingRestoreRecords(allMutations);
                if (restoreScheduling == RestoreScheduling.Delayed)
                    QueueDelayedRestore();
            }
            else
            {
                SessionState.EraseString(SessionRestoreKey);
            }

            s_latestGlobalParamMappings = BuildLatestGlobalParamMappings(allMutations);

            var report = new EnrollmentReport(
                descriptorCount,
                candidateCount,
                enrolledCount,
                preReservedNameCount,
                preflightCollisionAdjustments,
                candidateCollisionAdjustments,
                staleCleanup.RestoredCount,
                staleCleanup.UnresolvedCount);
            s_latestEnrollmentReport = report;
            s_hasLatestEnrollmentReport = true;

            Debug.Log(
                $"[ASM-Lite] Toggle broker enrollment: avatars={report.AvatarScanCount}, candidates={report.CandidateCount}, enrolled={report.EnrolledCount}, preReserved={report.PreReservedNameCount}, preflightCollisionAdjustments={report.PreflightCollisionAdjustments}, candidateCollisionAdjustments={report.CandidateCollisionAdjustments}, staleCleanupRestored={report.StaleCleanupRestoredCount}, staleCleanupUnresolved={report.StaleCleanupUnresolvedCount}.");

            return report;
        }

        internal static RestoreReport RestorePendingMutations(bool warnOnNoData = true)
        {
            string payload = SessionState.GetString(SessionRestoreKey, string.Empty);
            if (string.IsNullOrEmpty(payload))
            {
                if (warnOnNoData)
                    Debug.LogWarning("[ASM-Lite] Toggle broker restore no-op: no pending restore state was found.");
                return new RestoreReport(0, 0, malformedPayload: false);
            }

            if (!TryDeserializeMutationRecords(payload, out var records))
            {
                Debug.LogWarning("[ASM-Lite] Toggle broker restore skipped malformed payload and cleared stale restore state.");
                SessionState.EraseString(SessionRestoreKey);
                return new RestoreReport(0, 0, malformedPayload: true);
            }

            int restoredCount = 0;
            int unresolvedCount = 0;

            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (!TryRestoreMutationRecord(record))
                {
                    unresolvedCount++;
                    Debug.LogWarning(
                        $"[ASM-Lite] Toggle broker restore unresolved object path '{record.ObjectPath}' for parameter '{record.AssignedGlobalParam}'. Record skipped.");
                    continue;
                }

                restoredCount++;
            }

            SessionState.EraseString(SessionRestoreKey);
            Debug.Log($"[ASM-Lite] Toggle broker restore complete: restored={restoredCount}, unresolved={unresolvedCount}.");
            return new RestoreReport(restoredCount, unresolvedCount, malformedPayload: false);
        }

        internal static FullControllerGlobalEnrollmentReport EnrollFullControllerParametersForPreprocess(GameObject avatarRoot)
        {
            var staleRestore = RestorePendingFullControllerGlobalMutations(warnOnNoData: false);
            int avatarCount = avatarRoot != null && HasAsmLiteScope(avatarRoot) ? 1 : 0;
            int fullControllerCount = 0;
            int promotedCount = 0;
            var restoreEntries = new List<FullControllerGlobalRestoreEntry>();

            if (avatarCount > 0)
            {
                var fullControllerType = FindTypeByFullName(DefaultFullControllerTypeFullName);
                if (fullControllerType != null)
                {
                    var components = avatarRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
                    for (int i = 0; i < components.Length; i++)
                    {
                        var component = components[i];
                        if (component == null)
                            continue;

                        EnrollFullControllerParametersForComponent(
                            component,
                            avatarRoot.transform,
                            fullControllerType,
                            restoreEntries,
                            ref fullControllerCount,
                            ref promotedCount);
                    }
                }
                else
                {
                    Debug.LogWarning($"[ASM-Lite] FullController parameter enrollment skipped because reflected type '{DefaultFullControllerTypeFullName}' was not found.");
                }
            }

            if (restoreEntries.Count > 0)
            {
                PersistPendingFullControllerGlobalRestoreRecords(restoreEntries);
                QueueDelayedRestore();
            }
            else
            {
                SessionState.EraseString(FullControllerGlobalRestoreKey);
            }

            var report = new FullControllerGlobalEnrollmentReport(
                avatarCount,
                fullControllerCount,
                promotedCount,
                staleRestore.RestoredCount,
                staleRestore.UnresolvedCount);
            s_latestFullControllerGlobalEnrollmentReport = report;

            Debug.Log(
                $"[ASM-Lite] FullController parameter enrollment: avatars={report.AvatarScanCount}, fullControllers={report.FullControllerCount}, promoted={report.PromotedParameterCount}, staleRestoreRestored={report.RestoredCount}, staleRestoreUnresolved={report.UnresolvedCount}.");

            return report;
        }

        internal static RestoreReport RestorePendingFullControllerGlobalMutations(bool warnOnNoData = true)
        {
            string payload = SessionState.GetString(FullControllerGlobalRestoreKey, string.Empty);
            if (string.IsNullOrEmpty(payload))
            {
                if (warnOnNoData)
                    Debug.LogWarning("[ASM-Lite] FullController parameter restore no-op: no pending restore state was found.");
                return new RestoreReport(0, 0, malformedPayload: false);
            }

            FullControllerGlobalRestorePayload data;
            try
            {
                data = JsonUtility.FromJson<FullControllerGlobalRestorePayload>(payload);
            }
            catch
            {
                SessionState.EraseString(FullControllerGlobalRestoreKey);
                Debug.LogWarning("[ASM-Lite] FullController parameter restore skipped malformed payload and cleared stale restore state.");
                return new RestoreReport(0, 0, malformedPayload: true);
            }

            if (data?.entries == null)
            {
                SessionState.EraseString(FullControllerGlobalRestoreKey);
                return new RestoreReport(0, 0, malformedPayload: true);
            }

            int restoredCount = 0;
            int unresolvedCount = 0;
            for (int i = 0; i < data.entries.Length; i++)
            {
                var entry = data.entries[i];
                if (!TryRestoreFullControllerGlobalMutation(entry))
                {
                    unresolvedCount++;
                    Debug.LogWarning($"[ASM-Lite] FullController parameter restore unresolved object path '{entry?.objectPath}'. Record skipped.");
                    continue;
                }

                restoredCount++;
            }

            SessionState.EraseString(FullControllerGlobalRestoreKey);
            Debug.Log($"[ASM-Lite] FullController parameter restore complete: restored={restoredCount}, unresolved={unresolvedCount}.");
            return new RestoreReport(restoredCount, unresolvedCount, malformedPayload: false);
        }

        internal static void ClearPendingRestoreState()
        {
            SessionState.EraseString(SessionRestoreKey);
            SessionState.EraseString(FullControllerGlobalRestoreKey);
        }

        internal static bool HasPendingRestoreState()
        {
            string payload = SessionState.GetString(SessionRestoreKey, string.Empty);
            string fullControllerPayload = SessionState.GetString(FullControllerGlobalRestoreKey, string.Empty);
            return !string.IsNullOrEmpty(payload) || !string.IsNullOrEmpty(fullControllerPayload);
        }

        internal static bool TryGetLatestEnrollmentReport(out EnrollmentReport report)
        {
            report = s_latestEnrollmentReport;
            return s_hasLatestEnrollmentReport;
        }

        internal static GlobalParamMapping[] GetLatestGlobalParamMappings()
        {
            if (s_latestGlobalParamMappings == null || s_latestGlobalParamMappings.Length == 0)
                return Array.Empty<GlobalParamMapping>();

            var copy = new GlobalParamMapping[s_latestGlobalParamMappings.Length];
            Array.Copy(s_latestGlobalParamMappings, copy, s_latestGlobalParamMappings.Length);
            return copy;
        }

        internal static void SetPendingRestorePayloadForTests(string payload)
        {
            if (string.IsNullOrEmpty(payload))
            {
                SessionState.EraseString(SessionRestoreKey);
                return;
            }

            SessionState.SetString(SessionRestoreKey, payload);
        }

        internal static string GetPendingRestorePayloadForTests()
        {
            return SessionState.GetString(SessionRestoreKey, string.Empty);
        }

        internal static void ResetLatestEnrollmentStateForTests()
        {
            s_hasLatestEnrollmentReport = false;
            s_latestEnrollmentReport = default;
            s_latestGlobalParamMappings = Array.Empty<GlobalParamMapping>();
            s_latestFullControllerGlobalEnrollmentReport = default;
            CancelDelayedRestoreQueue();
        }

        internal static void ExecuteDelayedRestoreNowForTests()
        {
            OnDelayedRestore();
        }

        internal static bool IsDelayedRestoreQueuedForTests()
        {
            return s_restoreDelayQueued;
        }

        internal static bool TriggerStartupRestoreForTests()
        {
            return TryQueueStartupRestore();
        }

        internal static RestoreReport ExecuteStartupRestoreNowForTests()
        {
            return ExecuteStartupRestoreAttempt();
        }

        internal static bool IsStartupRestoreQueuedForTests()
        {
            return s_startupRestoreQueued;
        }

        internal static List<ToggleCandidate> DiscoverEligibleToggleCandidates(GameObject avatarRoot, string toggleTypeFullName = DefaultToggleTypeFullName)
        {
            var result = new List<ToggleCandidate>();

            if (avatarRoot == null || !HasAsmLiteScope(avatarRoot))
                return result;

            var toggleType = FindTypeByFullName(toggleTypeFullName);
            if (toggleType == null)
            {
                Debug.LogWarning($"[ASM-Lite] Toggle broker skipped enrollment because reflected type '{toggleTypeFullName}' was not found.");
                return result;
            }

            var components = avatarRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                CollectCandidatesForComponent(component, avatarRoot.transform, toggleType, result);
            }

            return result;
        }

        internal static List<string> DiscoverAssignedToggleGlobalParams(GameObject avatarRoot, string toggleTypeFullName = DefaultToggleTypeFullName)
        {
            var result = new List<string>();
            if (avatarRoot == null || !HasAsmLiteScope(avatarRoot))
                return result;

            var toggleType = FindTypeByFullName(toggleTypeFullName);
            if (toggleType == null)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var components = avatarRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                CollectAssignedGlobalsForComponent(component, toggleType, result, seen);
            }

            return result;
        }

        internal static List<VRCExpressionParameters.Parameter> DiscoverAssignedToggleExpressionParameters(GameObject avatarRoot, string toggleTypeFullName = DefaultToggleTypeFullName)
        {
            var result = new List<VRCExpressionParameters.Parameter>();
            if (avatarRoot == null || !HasAsmLiteScope(avatarRoot))
                return result;

            var toggleType = FindTypeByFullName(toggleTypeFullName);
            if (toggleType == null)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var components = avatarRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                CollectAssignedExpressionParametersForComponent(component, toggleType, result, seen);
            }

            return result;
        }

        internal static List<VRCExpressionParameters.Parameter> DiscoverPlannedToggleExpressionParameters(
            GameObject avatarRoot,
            VRCAvatarDescriptor descriptor = null,
            string toggleTypeFullName = DefaultToggleTypeFullName)
        {
            var result = new List<VRCExpressionParameters.Parameter>();
            if (avatarRoot == null || !HasAsmLiteScope(avatarRoot))
                return result;

            var candidates = DiscoverEligibleToggleCandidates(avatarRoot, toggleTypeFullName);
            if (candidates.Count == 0)
                return result;

            if (descriptor == null)
                descriptor = avatarRoot.GetComponent<VRCAvatarDescriptor>();

            var preReservedNames = BuildPreReservedDescriptorNames(descriptor, out _);
            string[] plannedNames = PlanDeterministicGlobalNames(
                candidates,
                preReservedNames,
                out _,
                out _);

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count && i < plannedNames.Length; i++)
            {
                var candidate = candidates[i];
                string plannedName = plannedNames[i];
                if (string.IsNullOrWhiteSpace(plannedName))
                    continue;

                plannedName = plannedName.Trim();
                if (!seen.Add(plannedName))
                    continue;

                var so = new SerializedObject(candidate.Component);
                result.Add(BuildAssignedExpressionParameter(so, candidate.TogglePropertyPath, plannedName));
            }

            return result;
        }

        internal static List<VRCExpressionParameters.Parameter> DiscoverStableFullControllerExpressionParameters(GameObject avatarRoot, string fullControllerTypeFullName = DefaultFullControllerTypeFullName)
        {
            return DiscoverFullControllerExpressionParameters(avatarRoot, true, fullControllerTypeFullName);
        }

        internal static List<VRCExpressionParameters.Parameter> DiscoverPlannedFullControllerExpressionParameters(GameObject avatarRoot, string fullControllerTypeFullName = DefaultFullControllerTypeFullName)
        {
            return DiscoverFullControllerExpressionParameters(avatarRoot, false, fullControllerTypeFullName);
        }

        private static List<VRCExpressionParameters.Parameter> DiscoverFullControllerExpressionParameters(GameObject avatarRoot, bool requireGlobalPattern, string fullControllerTypeFullName)
        {
            var result = new List<VRCExpressionParameters.Parameter>();
            if (avatarRoot == null || !HasAsmLiteScope(avatarRoot))
                return result;

            var fullControllerType = FindTypeByFullName(fullControllerTypeFullName);
            if (fullControllerType == null)
                return result;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var components = avatarRoot.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < components.Length; i++)
            {
                var component = components[i];
                if (component == null)
                    continue;

                CollectFullControllerExpressionParametersForComponent(component, fullControllerType, result, seen, requireGlobalPattern);
            }

            return result;
        }

        internal static string SanitizePathToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Unnamed";

            var sb = new StringBuilder(value.Length);
            bool previousUnderscore = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                bool keep = char.IsLetterOrDigit(c) || c == '_';
                if (!keep)
                    c = '_';

                if (c == '_')
                {
                    if (previousUnderscore)
                        continue;

                    previousUnderscore = true;
                    sb.Append('_');
                    continue;
                }

                previousUnderscore = false;
                sb.Append(c);
            }

            string cleaned = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(cleaned))
                cleaned = "Unnamed";

            if (char.IsDigit(cleaned[0]))
                cleaned = "_" + cleaned;

            return cleaned;
        }

        internal static string BuildDeterministicGlobalName(string menuPath, string objectPath, ISet<string> reservedNames = null)
        {
            string baseName = BuildDeterministicBaseName(menuPath, objectPath);

            if (reservedNames == null)
                return baseName;

            string candidate = baseName;
            int suffix = 2;
            while (!reservedNames.Add(candidate))
            {
                candidate = $"{baseName}_{suffix}";
                suffix++;
            }

            return candidate;
        }

        private static GlobalParamMapping[] BuildLatestGlobalParamMappings(IReadOnlyList<ToggleMutationRecord> records)
        {
            if (records == null || records.Count == 0)
                return Array.Empty<GlobalParamMapping>();

            var seenOriginal = new HashSet<string>(StringComparer.Ordinal);
            var mappings = new List<GlobalParamMapping>(records.Count);
            for (int i = 0; i < records.Count; i++)
            {
                var record = records[i];
                if (string.IsNullOrWhiteSpace(record.OriginalGlobalParam))
                    continue;
                if (string.IsNullOrWhiteSpace(record.AssignedGlobalParam))
                    continue;

                string original = record.OriginalGlobalParam.Trim();
                if (!seenOriginal.Add(original))
                    continue;

                mappings.Add(new GlobalParamMapping(original, record.AssignedGlobalParam));
            }

            return mappings.Count == 0 ? Array.Empty<GlobalParamMapping>() : mappings.ToArray();
        }

        private static string[] PlanDeterministicGlobalNames(
            IReadOnlyList<ToggleCandidate> candidates,
            ISet<string> preReservedNames,
            out int preflightCollisionAdjustments,
            out int candidateCollisionAdjustments)
        {
            preflightCollisionAdjustments = 0;
            candidateCollisionAdjustments = 0;

            if (candidates == null || candidates.Count == 0)
                return Array.Empty<string>();

            var groupedByBaseName = new Dictionary<string, List<PlannedCandidateAssignment>>(StringComparer.Ordinal);
            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                string baseName = BuildDeterministicBaseName(candidate.MenuPathHint, candidate.ObjectPath);
                string tieBreakKey = BuildStableCollisionTieBreakKey(candidate);
                int tieBreakComponentId = candidate.Component != null ? candidate.Component.GetInstanceID() : 0;

                var planned = new PlannedCandidateAssignment(i, baseName, tieBreakKey, tieBreakComponentId);
                if (!groupedByBaseName.TryGetValue(baseName, out var group))
                {
                    group = new List<PlannedCandidateAssignment>();
                    groupedByBaseName.Add(baseName, group);
                }

                group.Add(planned);
            }

            var occupiedNames = preReservedNames != null
                ? new HashSet<string>(preReservedNames, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            var orderedBaseNames = new List<string>(groupedByBaseName.Keys);
            orderedBaseNames.Sort(StringComparer.Ordinal);

            var assignedNames = new string[candidates.Count];
            for (int baseIndex = 0; baseIndex < orderedBaseNames.Count; baseIndex++)
            {
                string baseName = orderedBaseNames[baseIndex];
                var group = groupedByBaseName[baseName];
                group.Sort(ComparePlannedCandidateAssignments);

                for (int rank = 0; rank < group.Count; rank++)
                {
                    var planned = group[rank];
                    string preferredName = rank == 0
                        ? planned.BaseName
                        : $"{planned.BaseName}_{rank + 2}";

                    if (rank > 0)
                        candidateCollisionAdjustments++;

                    int nextSuffix = rank == 0 ? 2 : rank + 3;
                    string assignedName = preferredName;
                    while (!occupiedNames.Add(assignedName))
                    {
                        if (preReservedNames != null && preReservedNames.Contains(assignedName))
                            preflightCollisionAdjustments++;
                        else
                            candidateCollisionAdjustments++;

                        assignedName = $"{planned.BaseName}_{nextSuffix}";
                        nextSuffix++;
                    }

                    assignedNames[planned.OriginalIndex] = assignedName;
                }
            }

            return assignedNames;
        }

        private static int ComparePlannedCandidateAssignments(PlannedCandidateAssignment left, PlannedCandidateAssignment right)
        {
            int tieBreakComparison = string.Compare(left.TieBreakKey, right.TieBreakKey, StringComparison.Ordinal);
            if (tieBreakComparison != 0)
                return tieBreakComparison;

            int componentComparison = left.TieBreakComponentId.CompareTo(right.TieBreakComponentId);
            if (componentComparison != 0)
                return componentComparison;

            return left.OriginalIndex.CompareTo(right.OriginalIndex);
        }

        private static string BuildDeterministicBaseName(string menuPath, string objectPath)
        {
            string sanitizedMenuPath = SanitizePathToken(menuPath);
            string sanitizedObjectPath = SanitizePathToken(objectPath);
            return $"{GlobalPrefix}{sanitizedMenuPath}__{sanitizedObjectPath}";
        }

        private static string BuildStableCollisionTieBreakKey(ToggleCandidate candidate)
        {
            string menuPath = candidate.MenuPathHint ?? string.Empty;
            string objectPath = candidate.ObjectPath ?? string.Empty;
            string togglePropertyPath = candidate.TogglePropertyPath ?? string.Empty;
            return string.Concat(menuPath, "\u001F", objectPath, "\u001F", togglePropertyPath);
        }

        private static HashSet<string> BuildPreReservedDescriptorNames(VRCAvatarDescriptor descriptor, out int reservedCount)
        {
            reservedCount = 0;
            var reservedNames = new HashSet<string>(StringComparer.Ordinal);

            if (descriptor == null)
                return reservedNames;

            try
            {
                var expressionParameters = descriptor.expressionParameters;
                var parameters = expressionParameters != null ? expressionParameters.parameters : null;
                if (parameters == null)
                    return reservedNames;

                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    string parameterName = parameter != null ? parameter.name : null;
                    if (string.IsNullOrWhiteSpace(parameterName))
                        continue;

                    if (reservedNames.Add(parameterName))
                        reservedCount++;
                }
            }
            catch (Exception ex)
            {
                reservedNames.Clear();
                reservedCount = 0;
                string descriptorName = descriptor.gameObject != null ? descriptor.gameObject.name : "<null-avatar>";
                Debug.LogWarning($"[ASM-Lite] Toggle broker preflight namespace scan failed for avatar '{descriptorName}'. Falling back to zero reserved names. ({ex.GetType().Name}: {ex.Message})");
            }

            return reservedNames;
        }

        internal static bool TryEnrollToggleCandidate(ToggleCandidate candidate, string globalName, out ToggleMutationRecord record)
        {
            record = default;

            if (candidate.Component == null)
                return false;

            if (string.IsNullOrWhiteSpace(globalName))
            {
                Debug.LogWarning("[ASM-Lite] Toggle broker rejected enrollment because the computed global parameter name was blank.");
                return false;
            }

            var so = new SerializedObject(candidate.Component);
            if (!TryResolveToggleProperties(so, candidate.TogglePropertyPath, out var useGlobalProp, out var globalParamProp))
                return false;

            bool originalUseGlobal = useGlobalProp.boolValue;
            string originalGlobal = globalParamProp.stringValue;

            useGlobalProp.boolValue = true;
            globalParamProp.stringValue = globalName;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(candidate.Component);

            var readBack = new SerializedObject(candidate.Component);
            if (!TryResolveToggleProperties(readBack, candidate.TogglePropertyPath, out var useGlobalVerify, out var globalParamVerify))
            {
                Debug.LogWarning($"[ASM-Lite] Toggle broker could not verify enrollment on '{candidate.ObjectPath}'. Mutation was not persisted.");
                return false;
            }

            if (!useGlobalVerify.boolValue || !string.Equals(globalParamVerify.stringValue, globalName, StringComparison.Ordinal))
            {
                Debug.LogWarning($"[ASM-Lite] Toggle broker detected a serialized write mismatch on '{candidate.ObjectPath}'. Enrollment was skipped.");
                return false;
            }

            record = new ToggleMutationRecord(
                candidate.Component.GetInstanceID(),
                candidate.ObjectPath,
                candidate.TogglePropertyPath,
                originalUseGlobal,
                originalGlobal,
                globalName);
            return true;
        }

        private static bool TryRestoreMutationRecord(ToggleMutationRecord record)
        {
            if (record.ComponentInstanceId == 0)
                return false;

            var component = EditorUtility.InstanceIDToObject(record.ComponentInstanceId) as Component;
            if (component == null)
                return false;

            var so = new SerializedObject(component);
            if (!TryResolveToggleProperties(so, record.TogglePropertyPath, out var useGlobalProp, out var globalParamProp))
                return false;

            useGlobalProp.boolValue = record.OriginalUseGlobalParam;
            globalParamProp.stringValue = record.OriginalGlobalParam ?? string.Empty;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);

            return true;
        }

        internal static void QueueStartupRestoreForEditorLoad()
        {
            TryQueueStartupRestore();
        }

        private static bool TryQueueStartupRestore()
        {
            if (!HasPendingRestoreState())
                return false;

            if (s_startupRestoreQueued)
                return false;

            s_startupRestoreQueued = true;
            EditorApplication.delayCall += OnStartupRestore;
            return true;
        }

        private static void OnStartupRestore()
        {
            ExecuteStartupRestoreAttempt();
        }

        private static RestoreReport ExecuteStartupRestoreAttempt()
        {
            s_startupRestoreQueued = false;

            try
            {
                var toggleRestore = RestorePendingMutations(warnOnNoData: false);
                var fullControllerRestore = RestorePendingFullControllerGlobalMutations(warnOnNoData: false);
                var restore = new RestoreReport(
                    toggleRestore.RestoredCount + fullControllerRestore.RestoredCount,
                    toggleRestore.UnresolvedCount + fullControllerRestore.UnresolvedCount,
                    toggleRestore.MalformedPayload || fullControllerRestore.MalformedPayload);
                if (restore.MalformedPayload)
                {
                    Debug.LogWarning("[ASM-Lite] Toggle broker startup restore skipped malformed payload and cleared stale restore state.");
                }
                else if (restore.RestoredCount == 0 && restore.UnresolvedCount == 0)
                {
                    Debug.Log("[ASM-Lite] Toggle broker startup restore no-op: no pending restore state was found.");
                }
                else
                {
                    Debug.Log($"[ASM-Lite] Toggle broker startup restore complete: restored={restore.RestoredCount}, unresolved={restore.UnresolvedCount}.");
                }

                return restore;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] Toggle broker startup restore failed and left pending state for next enrollment cleanup. ({ex.GetType().Name}: {ex.Message})");
                return new RestoreReport(0, 0, malformedPayload: false);
            }
        }

        private static void QueueDelayedRestore()
        {
            if (s_restoreDelayQueued)
                return;

            s_restoreDelayQueued = true;
            EditorApplication.delayCall += OnDelayedRestore;
        }

        private static void CancelDelayedRestoreQueue()
        {
            if (!s_restoreDelayQueued)
                return;

            EditorApplication.delayCall -= OnDelayedRestore;
            s_restoreDelayQueued = false;
        }

        private static void OnDelayedRestore()
        {
            s_restoreDelayQueued = false;
            RestorePendingMutations(warnOnNoData: false);
            RestorePendingFullControllerGlobalMutations(warnOnNoData: false);
        }

        private static void PersistPendingRestoreRecords(List<ToggleMutationRecord> records)
        {
            string payload = SerializeMutationRecords(records);
            if (string.IsNullOrEmpty(payload))
            {
                SessionState.EraseString(SessionRestoreKey);
                return;
            }

            SessionState.SetString(SessionRestoreKey, payload);
        }

        private static string SerializeMutationRecords(List<ToggleMutationRecord> records)
        {
            if (records == null || records.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            sb.Append("{\"entries\":[");
            for (int i = 0; i < records.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');

                var record = records[i];
                sb.Append('{')
                    .Append("\"componentInstanceId\":").Append(record.ComponentInstanceId).Append(',')
                    .Append("\"objectPath\":\"").Append(EscapeJson(record.ObjectPath)).Append("\",")
                    .Append("\"togglePropertyPath\":\"").Append(EscapeJson(record.TogglePropertyPath)).Append("\",")
                    .Append("\"originalUseGlobalParam\":").Append(record.OriginalUseGlobalParam ? "true" : "false").Append(',')
                    .Append("\"originalGlobalParam\":\"").Append(EscapeJson(record.OriginalGlobalParam)).Append("\",")
                    .Append("\"assignedGlobalParam\":\"").Append(EscapeJson(record.AssignedGlobalParam)).Append("\"")
                    .Append('}');
            }

            sb.Append("]}");
            return sb.ToString();
        }

        private static bool TryDeserializeMutationRecords(string payload, out List<ToggleMutationRecord> records)
        {
            records = new List<ToggleMutationRecord>();

            if (string.IsNullOrWhiteSpace(payload))
                return false;

            MatchCollection matches = s_restoreEntryRegex.Matches(payload);
            if (matches.Count == 0)
                return false;

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (!match.Success)
                    continue;

                if (!int.TryParse(match.Groups["id"].Value, out int componentInstanceId) || componentInstanceId == 0)
                    continue;

                string togglePropertyPath = UnescapeJson(match.Groups["togglePath"].Value);
                if (string.IsNullOrEmpty(togglePropertyPath))
                    continue;

                records.Add(new ToggleMutationRecord(
                    componentInstanceId,
                    UnescapeJson(match.Groups["objectPath"].Value),
                    togglePropertyPath,
                    string.Equals(match.Groups["useGlobal"].Value, "true", StringComparison.Ordinal),
                    UnescapeJson(match.Groups["originalGlobal"].Value),
                    UnescapeJson(match.Groups["assignedGlobal"].Value)));
            }

            return records.Count > 0;
        }

        private static void CollectAssignedGlobalsForComponent(Component component, Type toggleType, List<string> result, HashSet<string> seen)
        {
            if (component == null || toggleType == null || result == null || seen == null)
                return;

            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            if (!iterator.NextVisible(true))
                return;

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                if (!IsToggleManagedReference(iterator, toggleType))
                    continue;

                string togglePropertyPath = iterator.propertyPath;
                if (!seenPaths.Add(togglePropertyPath))
                    continue;

                if (!TryResolveToggleProperties(so, togglePropertyPath, out var useGlobalProp, out var globalParamProp))
                    continue;

                if (!useGlobalProp.boolValue)
                    continue;

                string globalParam = globalParamProp.stringValue;
                if (string.IsNullOrWhiteSpace(globalParam))
                    continue;

                if (seen.Add(globalParam))
                    result.Add(globalParam);
            } while (iterator.NextVisible(true));
        }

        private static void CollectFullControllerExpressionParametersForComponent(Component component, Type fullControllerType, List<VRCExpressionParameters.Parameter> result, HashSet<string> seen, bool requireGlobalPattern)
        {
            if (component == null || fullControllerType == null || result == null || seen == null)
                return;

            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            if (!iterator.NextVisible(true))
                return;

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                if (!IsManagedReferenceOfType(iterator, fullControllerType))
                    continue;

                string fullControllerPropertyPath = iterator.propertyPath;
                if (!seenPaths.Add(fullControllerPropertyPath))
                    continue;

                CollectFullControllerExpressionParameters(so, fullControllerPropertyPath, result, seen, requireGlobalPattern);
            } while (iterator.NextVisible(true));
        }

        private static void EnrollFullControllerParametersForComponent(
            Component component,
            Transform avatarRoot,
            Type fullControllerType,
            List<FullControllerGlobalRestoreEntry> restoreEntries,
            ref int fullControllerCount,
            ref int promotedCount)
        {
            if (component == null || fullControllerType == null || restoreEntries == null)
                return;

            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            if (!iterator.NextVisible(true))
                return;

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                if (!IsManagedReferenceOfType(iterator, fullControllerType))
                    continue;

                string fullControllerPropertyPath = iterator.propertyPath;
                if (!seenPaths.Add(fullControllerPropertyPath))
                    continue;

                fullControllerCount++;
                promotedCount += EnrollFullControllerParameters(
                    so,
                    component,
                    avatarRoot,
                    fullControllerPropertyPath,
                    restoreEntries);
            } while (iterator.NextVisible(true));
        }

        private static int EnrollFullControllerParameters(
            SerializedObject so,
            Component component,
            Transform avatarRoot,
            string fullControllerPropertyPath,
            List<FullControllerGlobalRestoreEntry> restoreEntries)
        {
            var globalParamsProperty = so.FindProperty(fullControllerPropertyPath + ".globalParams");
            if (globalParamsProperty == null || !globalParamsProperty.isArray)
                return 0;

            var desiredNames = CollectPromotableFullControllerParameterNames(so, fullControllerPropertyPath);
            if (desiredNames.Count == 0)
                return 0;

            var existingGlobals = ReadStringArray(globalParamsProperty);
            var mergedGlobals = new List<string>(existingGlobals);
            var seenGlobals = new HashSet<string>(existingGlobals, StringComparer.Ordinal);
            int addedCount = 0;
            for (int i = 0; i < desiredNames.Count; i++)
            {
                string desiredName = desiredNames[i];
                if (string.IsNullOrWhiteSpace(desiredName))
                    continue;

                if (!seenGlobals.Add(desiredName))
                    continue;

                mergedGlobals.Add(desiredName);
                addedCount++;
            }

            if (addedCount == 0)
                return 0;

            restoreEntries.Add(new FullControllerGlobalRestoreEntry
            {
                componentInstanceId = component.GetInstanceID(),
                objectPath = BuildSceneObjectPath(component.transform, avatarRoot),
                fullControllerPropertyPath = fullControllerPropertyPath,
                originalGlobalParams = existingGlobals.ToArray(),
            });

            WriteStringArray(globalParamsProperty, mergedGlobals);
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            return addedCount;
        }

        private static List<string> CollectPromotableFullControllerParameterNames(SerializedObject so, string fullControllerPropertyPath)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            var prms = so.FindProperty(fullControllerPropertyPath + ".prms");
            if (prms != null && prms.isArray)
            {
                for (int i = 0; i < prms.arraySize; i++)
                {
                    var entry = prms.GetArrayElementAtIndex(i);
                    if (entry == null)
                        continue;

                    var parameters = ReadGuidWrappedExpressionParameters(so, entry.propertyPath + ".parameters");
                    CollectPromotableFullControllerParameterNames(parameters, result, seen);
                }
            }

            var legacyParameters = ReadGuidWrappedExpressionParameters(so, fullControllerPropertyPath + ".parameters");
            CollectPromotableFullControllerParameterNames(legacyParameters, result, seen);
            return result;
        }

        private static void CollectPromotableFullControllerParameterNames(VRCExpressionParameters parameters, List<string> result, HashSet<string> seen)
        {
            if (parameters?.parameters == null || result == null || seen == null)
                return;

            for (int i = 0; i < parameters.parameters.Length; i++)
            {
                var parameter = parameters.parameters[i];
                string name = parameter?.name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                name = name.Trim();
                if (ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(name))
                    continue;
                if (!IsBackableExpressionParameter(parameter))
                    continue;
                if (seen.Add(name))
                    result.Add(name);
            }
        }

        private static bool IsBackableExpressionParameter(VRCExpressionParameters.Parameter parameter)
        {
            if (parameter == null)
                return false;

            return parameter.valueType == VRCExpressionParameters.ValueType.Bool
                || parameter.valueType == VRCExpressionParameters.ValueType.Int
                || parameter.valueType == VRCExpressionParameters.ValueType.Float;
        }

        private static void CollectFullControllerExpressionParameters(SerializedObject so, string fullControllerPropertyPath, List<VRCExpressionParameters.Parameter> result, HashSet<string> seen, bool requireGlobalPattern)
        {
            if (so == null || string.IsNullOrEmpty(fullControllerPropertyPath))
                return;

            var globalPatterns = ReadStringArray(so.FindProperty(fullControllerPropertyPath + ".globalParams"));
            bool ignoreSaved = ReadBool(so, fullControllerPropertyPath, new[] { "ignoreSaved" }, false);

            var prms = so.FindProperty(fullControllerPropertyPath + ".prms");
            if (prms != null && prms.isArray)
            {
                for (int i = 0; i < prms.arraySize; i++)
                {
                    var entry = prms.GetArrayElementAtIndex(i);
                    if (entry == null)
                        continue;

                    var parameters = ReadGuidWrappedExpressionParameters(so, entry.propertyPath + ".parameters");
                    CollectFullControllerExpressionParameters(parameters, globalPatterns, ignoreSaved, result, seen, requireGlobalPattern);
                }
            }

            var legacyParameters = ReadGuidWrappedExpressionParameters(so, fullControllerPropertyPath + ".parameters");
            CollectFullControllerExpressionParameters(legacyParameters, globalPatterns, ignoreSaved, result, seen, requireGlobalPattern);
        }

        private static void CollectFullControllerExpressionParameters(
            VRCExpressionParameters parameters,
            IReadOnlyList<string> globalPatterns,
            bool ignoreSaved,
            List<VRCExpressionParameters.Parameter> result,
            HashSet<string> seen,
            bool requireGlobalPattern)
        {
            if (parameters?.parameters == null)
                return;

            for (int i = 0; i < parameters.parameters.Length; i++)
            {
                var parameter = parameters.parameters[i];
                string name = parameter?.name;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                name = name.Trim();
                if (!IsBackableExpressionParameter(parameter))
                    continue;
                if (ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(name))
                    continue;
                if (requireGlobalPattern && !IsStableFullControllerParameterName(parameter, globalPatterns))
                    continue;

                if (!seen.Add(name))
                    continue;

                result.Add(new VRCExpressionParameters.Parameter
                {
                    name = name,
                    valueType = parameter.valueType,
                    defaultValue = parameter.defaultValue,
                    saved = ignoreSaved ? false : parameter.saved,
                    networkSynced = parameter.networkSynced,
                });
            }
        }

        private static VRCExpressionParameters ReadGuidWrappedExpressionParameters(SerializedObject so, string wrapperPath)
        {
            if (so == null || string.IsNullOrEmpty(wrapperPath))
                return null;

            var objRef = so.FindProperty(wrapperPath + ".objRef");
            if (objRef != null && objRef.propertyType == SerializedPropertyType.ObjectReference)
                return objRef.objectReferenceValue as VRCExpressionParameters;

            return null;
        }

        private static bool IsStableFullControllerParameterName(
            VRCExpressionParameters.Parameter parameter,
            IReadOnlyList<string> globalPatterns)
        {
            string name = parameter?.name;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            if (globalPatterns == null)
                return false;

            bool isGlobal = false;
            for (int i = 0; i < globalPatterns.Count; i++)
            {
                string global = globalPatterns[i];
                if (string.IsNullOrWhiteSpace(global))
                    continue;

                global = global.Trim();
                bool negative = false;
                bool wildcard = false;
                if (global.StartsWith("!", StringComparison.Ordinal))
                {
                    negative = true;
                    global = global.Substring(1);
                }

                if (global.EndsWith("*", StringComparison.Ordinal))
                {
                    wildcard = true;
                    global = global.Substring(0, global.Length - 1);
                }

                if (string.Equals(name, global, StringComparison.Ordinal)
                    || (wildcard && name.StartsWith(global, StringComparison.Ordinal)))
                {
                    if (negative)
                    {
                        isGlobal = false;
                        break;
                    }

                    isGlobal = true;
                }
            }

            return isGlobal;
        }

        private static List<string> ReadStringArray(SerializedProperty property)
        {
            var result = new List<string>();
            if (property == null || !property.isArray)
                return result;

            for (int i = 0; i < property.arraySize; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                if (element != null && element.propertyType == SerializedPropertyType.String)
                    result.Add(element.stringValue);
            }

            return result;
        }

        private static void WriteStringArray(SerializedProperty property, IReadOnlyList<string> values)
        {
            if (property == null || !property.isArray)
                return;

            int count = values?.Count ?? 0;
            property.arraySize = count;
            for (int i = 0; i < count; i++)
            {
                var element = property.GetArrayElementAtIndex(i);
                if (element != null && element.propertyType == SerializedPropertyType.String)
                    element.stringValue = values[i] ?? string.Empty;
            }
        }

        private static void PersistPendingFullControllerGlobalRestoreRecords(List<FullControllerGlobalRestoreEntry> records)
        {
            if (records == null || records.Count == 0)
            {
                SessionState.EraseString(FullControllerGlobalRestoreKey);
                return;
            }

            var payload = new FullControllerGlobalRestorePayload { entries = records.ToArray() };
            SessionState.SetString(FullControllerGlobalRestoreKey, JsonUtility.ToJson(payload));
        }

        private static bool TryRestoreFullControllerGlobalMutation(FullControllerGlobalRestoreEntry entry)
        {
            if (entry == null || entry.componentInstanceId == 0 || string.IsNullOrEmpty(entry.fullControllerPropertyPath))
                return false;

            var component = EditorUtility.InstanceIDToObject(entry.componentInstanceId) as Component;
            if (component == null)
                return false;

            var so = new SerializedObject(component);
            var globalParamsProperty = so.FindProperty(entry.fullControllerPropertyPath + ".globalParams");
            if (globalParamsProperty == null || !globalParamsProperty.isArray)
                return false;

            WriteStringArray(globalParamsProperty, entry.originalGlobalParams ?? Array.Empty<string>());
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(component);
            return true;
        }

        private static void CollectAssignedExpressionParametersForComponent(Component component, Type toggleType, List<VRCExpressionParameters.Parameter> result, HashSet<string> seen)
        {
            if (component == null || toggleType == null || result == null || seen == null)
                return;

            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            if (!iterator.NextVisible(true))
                return;

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                if (!IsToggleManagedReference(iterator, toggleType))
                    continue;

                string togglePropertyPath = iterator.propertyPath;
                if (!seenPaths.Add(togglePropertyPath))
                    continue;

                if (!TryResolveToggleProperties(so, togglePropertyPath, out var useGlobalProp, out var globalParamProp))
                    continue;

                if (!useGlobalProp.boolValue)
                    continue;

                string globalParam = globalParamProp.stringValue;
                if (string.IsNullOrWhiteSpace(globalParam))
                    continue;

                globalParam = globalParam.Trim();
                if (!seen.Add(globalParam))
                    continue;

                result.Add(BuildAssignedExpressionParameter(so, togglePropertyPath, globalParam));
            } while (iterator.NextVisible(true));
        }

        private static VRCExpressionParameters.Parameter BuildAssignedExpressionParameter(SerializedObject so, string togglePropertyPath, string globalParam)
        {
            bool slider = ReadBool(so, togglePropertyPath, s_sliderCandidateFields, false);
            bool useInt = ReadBool(so, togglePropertyPath, s_useIntCandidateFields, false);
            bool defaultOn = ReadBool(so, togglePropertyPath, s_defaultOnCandidateFields, false);
            bool saved = ReadBool(so, togglePropertyPath, s_savedCandidateFields, true);

            var valueType = VRCExpressionParameters.ValueType.Bool;
            float defaultValue = defaultOn ? 1f : 0f;
            if (slider)
            {
                valueType = VRCExpressionParameters.ValueType.Float;
                defaultValue = ReadFloat(so, togglePropertyPath, s_defaultSliderValueCandidateFields, defaultValue);
            }
            else if (useInt)
            {
                valueType = VRCExpressionParameters.ValueType.Int;
            }

            return new VRCExpressionParameters.Parameter
            {
                name = globalParam,
                valueType = valueType,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = true,
            };
        }

        private static void CollectCandidatesForComponent(Component component, Transform avatarRoot, Type toggleType, List<ToggleCandidate> result)
        {
            var so = new SerializedObject(component);
            var iterator = so.GetIterator();
            if (!iterator.NextVisible(true))
                return;

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                if (!IsToggleManagedReference(iterator, toggleType))
                    continue;

                string togglePropertyPath = iterator.propertyPath;
                if (!seenPaths.Add(togglePropertyPath))
                    continue;

                if (!TryResolveToggleProperties(so, togglePropertyPath, out var useGlobalProp, out var globalParamProp))
                {
                    Debug.LogWarning($"[ASM-Lite] Toggle broker skipped unsupported Toggle schema on '{BuildSceneObjectPath(component.transform, avatarRoot)}' at '{togglePropertyPath}'.");
                    continue;
                }

                bool useGlobal = useGlobalProp.boolValue;
                string globalParam = globalParamProp.stringValue;
                if (useGlobal && !string.IsNullOrWhiteSpace(globalParam))
                    continue;

                string menuPathHint = ReadFirstNonEmptyString(so, togglePropertyPath, s_menuPathCandidateFields);
                string objectPath = BuildSceneObjectPath(component.transform, avatarRoot);
                result.Add(new ToggleCandidate(component, togglePropertyPath, menuPathHint, objectPath, useGlobal, globalParam));
            } while (iterator.NextVisible(true));
        }

        private static bool IsToggleManagedReference(SerializedProperty property, Type toggleType)
        {
            return IsManagedReferenceOfType(property, toggleType);
        }

        private static bool IsManagedReferenceOfType(SerializedProperty property, Type expectedType)
        {
            if (property == null || expectedType == null)
                return false;

            string fullTypeName = property.managedReferenceFullTypename;
            if (string.IsNullOrEmpty(fullTypeName))
                return false;

            // managedReferenceFullTypename format: "AssemblyName Full.Namespace.TypeName"
            int separator = fullTypeName.IndexOf(' ');
            if (separator < 0 || separator >= fullTypeName.Length - 1)
                return false;

            string qualifiedTypeName = fullTypeName.Substring(separator + 1);
            return string.Equals(qualifiedTypeName, expectedType.FullName, StringComparison.Ordinal);
        }

        private static bool TryResolveToggleProperties(SerializedObject so, string togglePropertyPath, out SerializedProperty useGlobalProp, out SerializedProperty globalParamProp)
        {
            useGlobalProp = FindPropertyBySuffix(so, togglePropertyPath, s_useGlobalCandidateFields);
            globalParamProp = FindPropertyBySuffix(so, togglePropertyPath, s_globalParamCandidateFields);

            if (useGlobalProp == null || globalParamProp == null)
                return false;

            if (useGlobalProp.propertyType != SerializedPropertyType.Boolean)
                return false;

            if (globalParamProp.propertyType != SerializedPropertyType.String)
                return false;

            return true;
        }

        private static SerializedProperty FindPropertyBySuffix(SerializedObject so, string basePath, string[] candidateSuffixes)
        {
            if (so == null || string.IsNullOrEmpty(basePath) || candidateSuffixes == null)
                return null;

            for (int i = 0; i < candidateSuffixes.Length; i++)
            {
                string suffix = candidateSuffixes[i];
                if (string.IsNullOrEmpty(suffix))
                    continue;

                var property = so.FindProperty(basePath + "." + suffix);
                if (property != null)
                    return property;
            }

            return null;
        }

        private static string ReadFirstNonEmptyString(SerializedObject so, string basePath, string[] candidateSuffixes)
        {
            var property = FindPropertyBySuffix(so, basePath, candidateSuffixes);
            if (property == null || property.propertyType != SerializedPropertyType.String)
                return string.Empty;

            return property.stringValue ?? string.Empty;
        }

        private static bool ReadBool(SerializedObject so, string basePath, string[] candidateSuffixes, bool fallback)
        {
            var property = FindPropertyBySuffix(so, basePath, candidateSuffixes);
            if (property == null || property.propertyType != SerializedPropertyType.Boolean)
                return fallback;

            return property.boolValue;
        }

        private static float ReadFloat(SerializedObject so, string basePath, string[] candidateSuffixes, float fallback)
        {
            var property = FindPropertyBySuffix(so, basePath, candidateSuffixes);
            if (property == null)
                return fallback;

            switch (property.propertyType)
            {
                case SerializedPropertyType.Float:
                    return property.floatValue;
                case SerializedPropertyType.Integer:
                    return property.intValue;
                default:
                    return fallback;
            }
        }

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length + 8);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            return sb.ToString();
        }

        private static string UnescapeJson(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i == value.Length - 1)
                {
                    sb.Append(c);
                    continue;
                }

                char next = value[++i];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '"':
                        sb.Append('"');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    default:
                        sb.Append(next);
                        break;
                }
            }

            return sb.ToString();
        }

        private static string BuildSceneObjectPath(Transform current, Transform root)
        {
            if (current == null)
                return string.Empty;

            var stack = new Stack<string>();
            var cursor = current;
            while (cursor != null)
            {
                stack.Push(NormalizeSceneObjectPathSegment(cursor.name ?? "<null>"));
                if (cursor == root)
                    break;
                cursor = cursor.parent;
            }

            if (stack.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            while (stack.Count > 0)
            {
                if (sb.Length > 0)
                    sb.Append('/');
                sb.Append(stack.Pop());
            }

            return sb.ToString();
        }

        private static string NormalizeSceneObjectPathSegment(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            bool stripped;
            do
            {
                stripped = false;
                if (name.EndsWith(" (Clone)", StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - " (Clone)".Length);
                    stripped = true;
                }
                else if (name.EndsWith("(Clone)", StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - "(Clone)".Length);
                    stripped = true;
                }
            } while (stripped);

            return string.IsNullOrEmpty(name) ? "<null>" : name;
        }

        private static Type FindTypeByFullName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                var assembly = assemblies[i];
                if (assembly == null)
                    continue;

                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            return null;
        }
    }

    [InitializeOnLoad]
    internal static class ASMLiteToggleBrokerStartupRestoreBootstrap
    {
        static ASMLiteToggleBrokerStartupRestoreBootstrap()
        {
            ASMLiteToggleNameBroker.QueueStartupRestoreForEditorLoad();
        }
    }

    public sealed class ASMLiteTogglePreprocessAvatarCallback : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => -10001;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            ASMLiteToggleNameBroker.EnrollAvatarForPreprocess(avatarGameObject);
            ASMLiteToggleNameBroker.EnrollFullControllerParametersForPreprocess(avatarGameObject);
            return true;
        }
    }

    public sealed class ASMLiteToggleBuildRequestedCallback : IVRCSDKBuildRequestedCallback
    {
        private const string AvatarBuildTypeName = "Avatar";

        public int callbackOrder => int.MinValue + 1;

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            return OnBuildRequested(requestedBuildType.ToString());
        }

        internal bool OnBuildRequested(object requestedBuildType)
        {
            string requestedBuildTypeName = requestedBuildType?.ToString() ?? string.Empty;
            return OnBuildRequested(requestedBuildTypeName);
        }

        internal bool OnBuildRequested(string requestedBuildTypeName)
        {
            if (!string.Equals(requestedBuildTypeName, AvatarBuildTypeName, StringComparison.Ordinal))
                return true;

            ASMLiteToggleNameBroker.EnrollForBuildRequest();
            return true;
        }
    }
}
