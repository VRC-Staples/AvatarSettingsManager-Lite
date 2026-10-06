using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    internal readonly struct ASMLiteParameterBudget
    {
        internal const int Limit = VRCExpressionParameters.MAX_PARAMETER_COUNT;
        internal const string Recovery = "Turn on Customize parameter backup and uncheck settings you do not need to save. Unchecked settings still work on your avatar; ASM-Lite will no longer save or restore them.";

        internal ASMLiteParameterBudget(int original, int contribution, bool complete, string problem = null, bool processed = false)
        {
            OriginalCount = original;
            Contribution = contribution;
            Complete = complete;
            Problem = problem;
            Processed = processed;
        }
        internal int OriginalCount { get; }
        internal int Contribution { get; }
        internal int Total => OriginalCount + Contribution;
        internal int Remaining => Limit - Total;
        internal int Excess => Math.Max(0, -Remaining);
        internal bool Complete { get; }
        internal string Problem { get; }
        internal bool Processed { get; }
        internal bool Readable => Problem == null;
        internal bool BlocksGeneration => !Readable || (Complete && Excess > 0);
        internal string CountText => $"{Total:N0} / {Limit:N0} parameters";
        internal string Message => !Readable ? Problem : !Complete
            ? $"Estimate with these settings: {CountText}. Estimated remaining capacity: {Remaining:N0} parameters. Build-time processors may add or remove parameters; capacity is not guaranteed. Final processed count will be checked."
            : Excess > 0 ? $"Too many avatar parameters: {CountText} — {Excess:N0} over the limit. "
                + (!Processed && OriginalCount > Limit ? "Original-avatar parameters alone exceed the limit; fix that excess outside ASM-Lite. " : string.Empty) + Recovery
            : Remaining == 0 ? "At the limit — no room for additional parameters."
            : $"{Remaining:N0} parameters remaining. This count is not an upload-safety check.";

        internal ASMLiteBuildDiagnosticResult ToDiagnostic()
        {
            if (!BlocksGeneration) return ASMLiteBuildDiagnosticResult.Pass();
            return ASMLiteBuildDiagnosticResult.Fail(
                Readable ? ASMLiteDiagnosticCodes.Build.ParameterLimitExceeded : ASMLiteDiagnosticCodes.Build.ParameterBudgetUnverifiable,
                "expressionParameters", Readable ? Recovery : "Assign readable parameter assets and resolve name/type conflicts before generation.",
                "[ASM-Lite] " + Message, parameterBudget: this);
        }
    }

    public static partial class ASMLiteBuilder
    {
        // Source-side replacement prediction. Never use this filter on processed output.
        internal static ASMLiteParameterBudget CalculateParameterBudget(
            VRCAvatarDescriptor avatar,
            ASMLiteMigrationContinuityService.ComponentCustomizationSnapshot settings)
        {
            if (avatar == null || settings.SlotCount < 1 || settings.SlotCount > 8)
                return new ASMLiteParameterBudget(0, 0, false, "Select an avatar and a preset count between 1 and 8.");
            if (EditorApplication.isUpdating || (avatar.expressionParameters != null && avatar.expressionParameters.parameters == null))
                return new ASMLiteParameterBudget(0, 0, false, "Calculating… Expression parameters are temporarily unreadable. Wait for import or assign a readable asset.");
            var payload = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (payload?.parameters == null)
                return new ASMLiteParameterBudget(0, 0, false, "Generated expression-parameter asset is unreadable. Restore the package asset before generation.");

            var excluded = ExpandExcludedNamesWithToggleMappings(new HashSet<string>(
                settings.UseParameterExclusions ? settings.ExcludedParameterNames ?? Array.Empty<string>() : Array.Empty<string>(), StringComparer.Ordinal));
            var originals = new List<VRCExpressionParameters.Parameter>(avatar.expressionParameters?.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>());
            bool clearlyBaked = ASMLiteParameterBudgetBuildGuard.HasClearBakedEvidence(avatar);
            // Only a matching known payload or the combined baked schema/menu signature is
            // replaceable. An arbitrary ASMLite_ prefix remains on the original-avatar side.
            var owned = new HashSet<string>(payload.parameters.Where(p => p != null).Select(p => p.name), StringComparer.Ordinal);
            bool hasAttached = avatar.GetComponentInChildren<ASMLiteComponent>(true) != null;
            originals.RemoveAll(p => p != null && ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(p.name)
                && ((hasAttached && owned.Contains(p.name)) || (clearlyBaked && IsKnownBudgetPayloadName(p.name))));
            string problem = FindParameterSchemaConflict(originals);
            if (problem != null) return new ASMLiteParameterBudget(originals.Count, 0, false, problem);
            int ignored = 0;
            MergeAssignedVrcFuryToggleParams(avatar, originals, null, ref ignored, requireAsmLiteScope: false);
            problem = FindParameterSchemaConflict(originals);
            if (problem != null) return new ASMLiteParameterBudget(originals.Count, 0, false, problem);
            // VRCFury keeps the first same-name entry. Preserve the builder's existing
            // duplicate-input behavior, but never hide unreadable or conflicting types.
            originals = originals.GroupBy(p => p.name, StringComparer.Ordinal).Select(group => group.First()).ToList();
            var selected = originals.Where(p => !ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(p.name) && !excluded.Contains(p.name)).ToList();
            var backupPlan = BuildBackupNamePlan(settings.SlotCount, selected,
                payload.parameters.Select(p => p?.name).ToArray(), ASMLiteToggleNameBroker.GetLatestGlobalParamMappings(), excluded);
            var generated = CreateExpressionSchema(selected, backupPlan.Names, payload.parameters);
            var combined = originals.Concat(generated).ToList();
            problem = FindParameterSchemaConflict(combined);
            if (problem != null) return new ASMLiteParameterBudget(originals.Count, generated.Length, false, problem);
            bool complete = true;
            foreach (var component in avatar.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null || component is ASMLiteComponent) continue;
                // Reuse broker discovery for estimates; do not claim to simulate VRCFury's
                // compression, opaque features, or another tool's build transformations.
                if (component.GetType().FullName == "VF.Model.VRCFury")
                {
                    using var serialized = new SerializedObject(component);
                    using var content = serialized.FindProperty("content");
                    bool ownFullController = component.GetComponent<ASMLiteComponent>() != null
                        && content?.propertyType == SerializedPropertyType.ManagedReference
                        && content.managedReferenceFullTypename.EndsWith(" VF.Model.Feature.FullController", StringComparison.Ordinal);
                    if (!ownFullController) complete = false;
                }
                else if (component is VRC.SDKBase.IEditorOnly) complete = false;
            }
            int mergedCount = combined.Select(p => p.name).Distinct(StringComparer.Ordinal).Count();
            return new ASMLiteParameterBudget(originals.Count, mergedCount - originals.Count, complete);
        }

        private static bool IsKnownBudgetPayloadName(string name)
            => name == CtrlParam || name.StartsWith("ASMLite_Def_", StringComparison.Ordinal) || TryParseBackupName(name, out _);

        private static string FindParameterSchemaConflict(IEnumerable<VRCExpressionParameters.Parameter> parameters)
        {
            var types = new Dictionary<string, VRCExpressionParameters.ValueType>(StringComparer.Ordinal);
            foreach (var p in parameters)
            {
                // Keep malformed source entries visible as failures, not a smaller green count.
                if (p == null || string.IsNullOrWhiteSpace(p.name)) return "Expression parameter schema contains an unreadable or unnamed entry.";
                if (types.TryGetValue(p.name, out var type) && type != p.valueType)
                    return $"Expression parameter '{p.name}' has conflicting types.";
                types[p.name] = p.valueType;
            }
            return null;
        }

        // Output-side measurement deliberately retains all existing entries, including
        // stale owned backups. SDK validation remains responsible for synced-bit cost.
        internal static ASMLiteParameterBudget MeasureParameterBudget(VRCExpressionParameters.Parameter[] parameters)
            => parameters == null ? new ASMLiteParameterBudget(0, 0, false, "Final processed expression schema is unreadable.")
                : new ASMLiteParameterBudget(parameters.Length, 0, true, processed: true);
    }
}
