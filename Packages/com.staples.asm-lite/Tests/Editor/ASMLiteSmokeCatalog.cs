using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ASMLite.Editor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteSmokeContractPaths
    {
        private const string CatalogRelativePath = "Tools/ci/smoke/suite-catalog.json";
        private const string ProtocolFixtureDirectoryRelativePath = "Tools/ci/smoke/protocol-fixtures";

        internal static string GetRepositoryRootPath()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(ASMLiteSmokeContractPaths).Assembly);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "..", ".."));

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
        }

        internal static string GetCatalogPath()
        {
            return Path.Combine(GetRepositoryRootPath(), CatalogRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        internal static string GetProtocolFixtureDirectory()
        {
            return Path.Combine(GetRepositoryRootPath(), ProtocolFixtureDirectoryRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    [Serializable]
    internal sealed class ASMLiteSmokeCatalogDocument
    {
        public int catalogVersion;
        public string protocolVersion;
        public ASMLiteSmokeFixtureDefinition fixture;
        public ASMLiteSmokeGroupDefinition[] groups = Array.Empty<ASMLiteSmokeGroupDefinition>();

        [NonSerialized] private Dictionary<string, ASMLiteSmokeGroupDefinition> _groupsById = new Dictionary<string, ASMLiteSmokeGroupDefinition>(StringComparer.Ordinal);
        [NonSerialized] private Dictionary<string, ASMLiteSmokeSuiteDefinition> _suitesById = new Dictionary<string, ASMLiteSmokeSuiteDefinition>(StringComparer.Ordinal);

        internal bool TryGetGroup(string groupId, out ASMLiteSmokeGroupDefinition group)
        {
            return _groupsById.TryGetValue(groupId ?? string.Empty, out group);
        }

        internal bool TryGetSuite(string suiteId, out ASMLiteSmokeSuiteDefinition suite)
        {
            return _suitesById.TryGetValue(suiteId ?? string.Empty, out suite);
        }

        internal void RebuildLookups()
        {
            _groupsById = new Dictionary<string, ASMLiteSmokeGroupDefinition>(StringComparer.Ordinal);
            _suitesById = new Dictionary<string, ASMLiteSmokeSuiteDefinition>(StringComparer.Ordinal);

            foreach (var group in groups ?? Array.Empty<ASMLiteSmokeGroupDefinition>())
            {
                if (group == null)
                    continue;

                _groupsById[group.groupId] = group;
                group.RebuildLookups();
                foreach (var suite in group.suites ?? Array.Empty<ASMLiteSmokeSuiteDefinition>())
                {
                    if (suite == null)
                        continue;

                    _suitesById[suite.suiteId] = suite;
                }
            }
        }
    }

    [Serializable]
    internal sealed class ASMLiteSmokeFixtureDefinition
    {
        public string scenePath;
        public string avatarName;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeGroupDefinition
    {
        public string groupId;
        public string label;
        public string description;
        public ASMLiteSmokeSuiteDefinition[] suites = Array.Empty<ASMLiteSmokeSuiteDefinition>();

        [NonSerialized] private Dictionary<string, ASMLiteSmokeSuiteDefinition> _suitesById = new Dictionary<string, ASMLiteSmokeSuiteDefinition>(StringComparer.Ordinal);

        internal bool TryGetSuite(string suiteId, out ASMLiteSmokeSuiteDefinition suite)
        {
            return _suitesById.TryGetValue(suiteId ?? string.Empty, out suite);
        }

        internal void RebuildLookups()
        {
            _suitesById = new Dictionary<string, ASMLiteSmokeSuiteDefinition>(StringComparer.Ordinal);
            foreach (var suite in suites ?? Array.Empty<ASMLiteSmokeSuiteDefinition>())
            {
                if (suite == null)
                    continue;

                _suitesById[suite.suiteId] = suite;
                suite.RebuildLookups();
            }
        }
    }

    [Serializable]
    internal sealed class ASMLiteSmokeSuiteDefinition
    {
        public string suiteId;
        public string label;
        public string description;
        public string resetOverride;
        public string speed;
        public string risk;
        public bool defaultSelected;
        public string[] presetGroups = Array.Empty<string>();
        public bool requiresPlayMode;
        public bool stopOnFirstFailure;
        public string expectedOutcome;
        public string debugHint;
        public ASMLiteSmokeCaseDefinition[] cases = Array.Empty<ASMLiteSmokeCaseDefinition>();

        internal bool IsDestructive => string.Equals(risk, "destructive", StringComparison.Ordinal);

        [NonSerialized] private Dictionary<string, ASMLiteSmokeCaseDefinition> _casesById = new Dictionary<string, ASMLiteSmokeCaseDefinition>(StringComparer.Ordinal);

        internal bool TryGetCase(string caseId, out ASMLiteSmokeCaseDefinition item)
        {
            return _casesById.TryGetValue(caseId ?? string.Empty, out item);
        }

        internal void RebuildLookups()
        {
            _casesById = new Dictionary<string, ASMLiteSmokeCaseDefinition>(StringComparer.Ordinal);
            foreach (var item in cases ?? Array.Empty<ASMLiteSmokeCaseDefinition>())
            {
                if (item == null)
                    continue;

                _casesById[item.caseId] = item;
                item.RebuildLookups();
            }
        }
    }

    [Serializable]
    internal sealed class ASMLiteSmokeCaseDefinition
    {
        public string caseId;
        public string label;
        public string description;
        public string expectedOutcome;
        public string debugHint;
        public ASMLiteSmokeStepDefinition[] steps = Array.Empty<ASMLiteSmokeStepDefinition>();

        [NonSerialized] private Dictionary<string, ASMLiteSmokeStepDefinition> _stepsById = new Dictionary<string, ASMLiteSmokeStepDefinition>(StringComparer.Ordinal);

        internal bool TryGetStep(string stepId, out ASMLiteSmokeStepDefinition step)
        {
            return _stepsById.TryGetValue(stepId ?? string.Empty, out step);
        }

        internal void RebuildLookups()
        {
            _stepsById = new Dictionary<string, ASMLiteSmokeStepDefinition>(StringComparer.Ordinal);
            foreach (var step in steps ?? Array.Empty<ASMLiteSmokeStepDefinition>())
            {
                if (step == null)
                    continue;

                _stepsById[step.stepId] = step;
            }
        }
    }

    [Serializable]
    internal sealed class ASMLiteSmokeStepDefinition
    {
        public string stepId;
        public string label;
        public string description;
        public string actionType;
        public ASMLiteSmokeStepArgs args;
        public string expectedOutcome;
        public string debugHint;
    }

    [Serializable]
    internal sealed class ASMLiteSmokeStepArgs
    {
        public string scenePath;
        public string avatarName;
        public string objectName;
        public string fixtureMutation;
        public string expectedPrimaryAction;
        public string expectedDiagnosticCode;
        public string expectedDiagnosticContains;
        public string expectedState;
        public int slotCount;
        public string installPathPresetId;
        public string iconMode;
        public int selectedGearIndex = -1;
        public string gearColor;
        public bool useCustomSlotIcons;
        public string rootIconFixtureId;
        public string[] slotIconFixtureIds = Array.Empty<string>();
        public string[] slotIconFixtureIdsBySlot = Array.Empty<string>();
        public bool clearExistingIconMask;
        public string actionIconMode;
        public string saveIconFixtureId;
        public string loadIconFixtureId;
        public string clearIconFixtureId;
        public bool expectedInstallPathEnabled;
        public string expectedNormalizedEffectivePath;
        public bool expectedComponentPresent;
        public bool useParameterExclusions;
        public string parameterBackupPresetId;
        public string[] excludedParameterNames = Array.Empty<string>();
        public bool customRootNameEnabled;
        public string customRootName;
        public string[] customPresetNames;
        public bool clearExistingNameMask;
        public string customSaveLabel;
        public string customLoadLabel;
        public string customClearLabel;
        public string customConfirmLabel;
        public bool expectStepFailure;
        public bool preserveFailureEvidence;
        public bool requireCleanReset;

        internal void Normalize()
        {
            scenePath = NormalizeOptional(scenePath);
            avatarName = NormalizeOptional(avatarName);
            objectName = NormalizeOptional(objectName);
            fixtureMutation = NormalizeOptional(fixtureMutation);
            expectedPrimaryAction = NormalizeOptional(expectedPrimaryAction);
            expectedDiagnosticCode = NormalizeOptional(expectedDiagnosticCode);
            expectedDiagnosticContains = NormalizeOptional(expectedDiagnosticContains);
            expectedState = NormalizeOptional(expectedState);
            installPathPresetId = NormalizeOptional(installPathPresetId);
            iconMode = NormalizeOptional(iconMode);
            gearColor = NormalizeOptional(gearColor);
            rootIconFixtureId = NormalizeOptional(rootIconFixtureId);
            slotIconFixtureIds = NormalizeOptionalArray(slotIconFixtureIds);
            slotIconFixtureIdsBySlot = NormalizeOptionalArray(slotIconFixtureIdsBySlot);
            actionIconMode = NormalizeOptional(actionIconMode);
            saveIconFixtureId = NormalizeOptional(saveIconFixtureId);
            loadIconFixtureId = NormalizeOptional(loadIconFixtureId);
            clearIconFixtureId = NormalizeOptional(clearIconFixtureId);
            expectedNormalizedEffectivePath = NormalizeInstallPath(expectedNormalizedEffectivePath);
            parameterBackupPresetId = NormalizeOptional(parameterBackupPresetId);
            excludedParameterNames = NormalizeOptionalArray(excludedParameterNames)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            customRootName = NormalizeOptional(customRootName);
            customPresetNames = NormalizeOptionalArray(customPresetNames);
            customSaveLabel = NormalizeOptional(customSaveLabel);
            customLoadLabel = NormalizeOptional(customLoadLabel);
            customClearLabel = NormalizeOptional(customClearLabel);
            customConfirmLabel = NormalizeOptional(customConfirmLabel);
        }

        internal static string NormalizeInstallPath(string value)
        {
            string normalized = NormalizeOptional(value).Replace('\\', '/');
            while (normalized.Contains("//"))
                normalized = normalized.Replace("//", "/");
            normalized = normalized.Trim('/');
            return normalized;
        }

        private static string NormalizeOptional(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string[] NormalizeOptionalArray(string[] values)
        {
            if (values == null || values.Length == 0)
                return Array.Empty<string>();


            var normalized = new string[values.Length];
            for (int index = 0; index < values.Length; index++)
                normalized[index] = NormalizeOptional(values[index]);
            return normalized;
        }
    }

    internal static class ASMLiteSmokeCatalog
    {
        private static readonly HashSet<string> s_allowedActionTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "open-scene",
            "open-window",
            "select-avatar",
            "add-prefab",
            "rebuild",
            "vendorize",
            "detach",
            "lifecycle-hygiene-cleanup",
            "return-to-package-managed",
            "enter-playmode",
            "exit-playmode",
            "assert-primary-action",
            "assert-generated-references-package-managed",
            "assert-runtime-component-valid",
            "assert-package-resource-present",
            "assert-catalog-loads",
            "assert-window-focused",
            "close-window",
            "assert-host-ready",
            "prelude-recover-context",
            "assert-no-component",
            "set-slot-count",
            "set-install-path-state",
            "set-root-name-state",
            "set-preset-name-mask",
            "set-action-label-mask",
            "set-icon-mode",
            "set-gear-color",
            "set-custom-icons-enabled",
            "set-root-icon-fixture",
            "set-slot-icon-mask",
            "set-action-icon-mask",
            "assert-parameter-backup-option-present",
            "set-parameter-backup-state",
            "assert-pending-customization-snapshot",
            "assert-attached-customization-snapshot",
        };

        internal static ASMLiteSmokeCatalogDocument LoadCanonical()
        {
            return LoadFromPath(ASMLiteSmokeContractPaths.GetCatalogPath());
        }

        internal static ASMLiteSmokeCatalogDocument LoadFromPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("Catalog path is required.");

            string rawJson = File.ReadAllText(path, Encoding.UTF8);
            return LoadFromJson(rawJson);
        }

        internal static ASMLiteSmokeCatalogDocument LoadFromJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                throw new InvalidOperationException("Smoke suite catalog JSON is required.");

            var catalog = JsonUtility.FromJson<ASMLiteSmokeCatalogDocument>(rawJson);
            if (catalog == null)
                throw new InvalidOperationException("Smoke suite catalog JSON did not deserialize.");

            NormalizeAndValidate(catalog);
            catalog.RebuildLookups();
            return catalog;
        }

        private static void NormalizeAndValidate(ASMLiteSmokeCatalogDocument catalog)
        {
            if (catalog.catalogVersion <= 0)
                throw new InvalidOperationException("Smoke suite catalog requires a positive catalogVersion.");

            catalog.protocolVersion = RequireNonBlank(catalog.protocolVersion, "protocolVersion");
            catalog.fixture = catalog.fixture ?? throw new InvalidOperationException("Smoke suite catalog requires fixture metadata.");
            catalog.fixture.scenePath = RequireNonBlank(catalog.fixture.scenePath, "fixture.scenePath");
            catalog.fixture.avatarName = RequireNonBlank(catalog.fixture.avatarName, "fixture.avatarName");
            catalog.groups = catalog.groups ?? Array.Empty<ASMLiteSmokeGroupDefinition>();
            if (catalog.groups.Length == 0)
                throw new InvalidOperationException("Smoke suite catalog requires at least one group.");

            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            var suiteIds = new HashSet<string>(StringComparer.Ordinal);
            for (int groupIndex = 0; groupIndex < catalog.groups.Length; groupIndex++)
            {
                var group = catalog.groups[groupIndex] ?? throw new InvalidOperationException($"groups[{groupIndex}] is required.");
                group.groupId = NormalizeUniqueId(group.groupId, $"groups[{groupIndex}].groupId", groupIds);
                group.label = RequireNonBlank(group.label, $"groups[{groupIndex}].label");
                group.description = RequireNonBlank(group.description, $"groups[{groupIndex}].description");
                group.suites = group.suites ?? Array.Empty<ASMLiteSmokeSuiteDefinition>();
                if (group.suites.Length == 0)
                    throw new InvalidOperationException($"groups[{groupIndex}].suites must not be empty.");

                for (int suiteIndex = 0; suiteIndex < group.suites.Length; suiteIndex++)
                    NormalizeAndValidateSuite(group.suites[suiteIndex], groupIndex, suiteIndex, suiteIds);
            }
        }

        private static void NormalizeAndValidateSuite(
            ASMLiteSmokeSuiteDefinition suite,
            int groupIndex,
            int suiteIndex,
            HashSet<string> suiteIds)
        {
            if (suite == null)
                throw new InvalidOperationException($"groups[{groupIndex}].suites[{suiteIndex}] is required.");

            string path = $"groups[{groupIndex}].suites[{suiteIndex}]";
            suite.suiteId = NormalizeUniqueId(suite.suiteId, path + ".suiteId", suiteIds);
            suite.label = RequireNonBlank(suite.label, path + ".label");
            suite.description = RequireNonBlank(suite.description, path + ".description");
            suite.resetOverride = string.IsNullOrWhiteSpace(suite.resetOverride)
                ? "Inherit"
                : suite.resetOverride.Trim();
            suite.speed = NormalizeEnumValue(
                suite.speed,
                path + ".speed",
                new[] { "quick", "standard", "exhaustive", "destructive", "manual-only" });
            suite.risk = NormalizeEnumValue(
                suite.risk,
                path + ".risk",
                new[] { "safe", "destructive" });
            suite.presetGroups = NormalizePresetGroups(suite.presetGroups, path + ".presetGroups");
            suite.expectedOutcome = RequireNonBlank(suite.expectedOutcome, path + ".expectedOutcome");
            suite.debugHint = RequireNonBlank(suite.debugHint, path + ".debugHint");
            suite.cases = suite.cases ?? Array.Empty<ASMLiteSmokeCaseDefinition>();
            if (suite.cases.Length == 0)
                throw new InvalidOperationException(path + ".cases must not be empty.");

            var caseIds = new HashSet<string>(StringComparer.Ordinal);
            for (int caseIndex = 0; caseIndex < suite.cases.Length; caseIndex++)
                NormalizeAndValidateCase(suite.cases[caseIndex], path, caseIndex, caseIds);
        }

        private static void NormalizeAndValidateCase(
            ASMLiteSmokeCaseDefinition item,
            string suitePath,
            int caseIndex,
            HashSet<string> caseIds)
        {
            if (item == null)
                throw new InvalidOperationException($"{suitePath}.cases[{caseIndex}] is required.");

            string path = $"{suitePath}.cases[{caseIndex}]";
            item.caseId = NormalizeUniqueId(item.caseId, path + ".caseId", caseIds);
            item.label = RequireNonBlank(item.label, path + ".label");
            item.description = RequireNonBlank(item.description, path + ".description");
            item.expectedOutcome = RequireNonBlank(item.expectedOutcome, path + ".expectedOutcome");
            item.debugHint = RequireNonBlank(item.debugHint, path + ".debugHint");
            item.steps = item.steps ?? Array.Empty<ASMLiteSmokeStepDefinition>();
            if (item.steps.Length == 0)
                throw new InvalidOperationException(path + ".steps must not be empty.");

            var stepIds = new HashSet<string>(StringComparer.Ordinal);
            for (int stepIndex = 0; stepIndex < item.steps.Length; stepIndex++)
                NormalizeAndValidateStep(item.steps[stepIndex], path, stepIndex, stepIds);
        }

        private static void NormalizeAndValidateStep(
            ASMLiteSmokeStepDefinition step,
            string casePath,
            int stepIndex,
            HashSet<string> stepIds)
        {
            if (step == null)
                throw new InvalidOperationException($"{casePath}.steps[{stepIndex}] is required.");

            string path = $"{casePath}.steps[{stepIndex}]";
            step.stepId = NormalizeUniqueId(step.stepId, path + ".stepId", stepIds);
            step.label = RequireNonBlank(step.label, path + ".label");
            step.description = RequireNonBlank(step.description, path + ".description");
            step.actionType = RequireNonBlank(step.actionType, path + ".actionType");
            if (!s_allowedActionTypes.Contains(step.actionType))
                throw new InvalidOperationException($"{path}.actionType '{step.actionType}' is not supported.");
            step.args = step.args ?? new ASMLiteSmokeStepArgs();
            step.args.Normalize();
            if (step.args.expectStepFailure)
            {
                step.args.expectedDiagnosticCode = RequireNonBlank(
                    step.args.expectedDiagnosticCode,
                    path + ".args.expectedDiagnosticCode");
                step.args.expectedDiagnosticContains = RequireNonBlank(
                    step.args.expectedDiagnosticContains,
                    path + ".args.expectedDiagnosticContains");
            }
            ValidateIconFixtureIds(step.args, path + ".args");
            ValidatePhase1StepArgs(step.actionType, step.args, path + ".args");
            step.expectedOutcome = RequireNonBlank(step.expectedOutcome, path + ".expectedOutcome");
            step.debugHint = RequireNonBlank(step.debugHint, path + ".debugHint");
        }

        private static string NormalizeUniqueId(string value, string path, HashSet<string> seenIds)
        {
            string normalized = RequireNonBlank(value, path);
            if (!seenIds.Add(normalized))
                throw new InvalidOperationException($"{path} '{normalized}' must be unique within its scope.");
            return normalized;
        }

        private static void ValidateIconFixtureIds(ASMLiteSmokeStepArgs args, string argsPath)
        {
            if (args == null)
                return;

            ValidateOptionalIconFixtureId(args.rootIconFixtureId, argsPath + ".rootIconFixtureId");
            ValidateOptionalIconFixtureId(args.saveIconFixtureId, argsPath + ".saveIconFixtureId");
            ValidateOptionalIconFixtureId(args.loadIconFixtureId, argsPath + ".loadIconFixtureId");
            ValidateOptionalIconFixtureId(args.clearIconFixtureId, argsPath + ".clearIconFixtureId");

            string[] slotIconFixtureIds = args.slotIconFixtureIds ?? Array.Empty<string>();
            for (int index = 0; index < slotIconFixtureIds.Length; index++)
                ValidateOptionalIconFixtureId(slotIconFixtureIds[index], $"{argsPath}.slotIconFixtureIds[{index}]");

            string[] slotIconFixtureIdsBySlot = args.slotIconFixtureIdsBySlot ?? Array.Empty<string>();
            for (int index = 0; index < slotIconFixtureIdsBySlot.Length; index++)
                ValidateOptionalIconFixtureId(slotIconFixtureIdsBySlot[index], $"{argsPath}.slotIconFixtureIdsBySlot[{index}]");
        }

        private static void ValidateOptionalIconFixtureId(string fixtureId, string path)
        {
            if (string.IsNullOrWhiteSpace(fixtureId))
                return;

            try
            {
                ASMLiteIconFixtureRegistry.ResolveAssetPath(fixtureId);
            }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException($"{path} has unsupported icon fixture ID '{fixtureId}'. {ex.Message}", ex);
            }
        }

        private static void ValidatePhase1StepArgs(string actionType, ASMLiteSmokeStepArgs args, string argsPath)
        {
            if (args == null)
                return;

            switch (actionType)
            {
                case "assert-no-component":
                    RejectPresentPhase1Arg(args.slotCount != 0, argsPath + ".slotCount");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.installPathPresetId), argsPath + ".installPathPresetId");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.expectedPrimaryAction), argsPath + ".expectedPrimaryAction");
                    RejectPresentPhase1Arg(args.expectedComponentPresent, argsPath + ".expectedComponentPresent");
                    RejectPresentPhase1Arg(args.expectedInstallPathEnabled, argsPath + ".expectedInstallPathEnabled");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.expectedNormalizedEffectivePath), argsPath + ".expectedNormalizedEffectivePath");
                    return;
                case "set-slot-count":
                    RequireSlotCountInRange(args.slotCount, argsPath + ".slotCount");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.installPathPresetId), argsPath + ".installPathPresetId");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.expectedPrimaryAction), argsPath + ".expectedPrimaryAction");
                    RejectPresentPhase1Arg(args.expectedComponentPresent, argsPath + ".expectedComponentPresent");
                    RejectPresentPhase1Arg(args.expectedInstallPathEnabled, argsPath + ".expectedInstallPathEnabled");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.expectedNormalizedEffectivePath), argsPath + ".expectedNormalizedEffectivePath");
                    return;
                case "set-install-path-state":
                    RejectPresentPhase1Arg(args.slotCount != 0, argsPath + ".slotCount");
                    RequireInstallPathPreset(args.installPathPresetId, argsPath + ".installPathPresetId");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.expectedPrimaryAction), argsPath + ".expectedPrimaryAction");
                    RejectPresentPhase1Arg(args.expectedComponentPresent, argsPath + ".expectedComponentPresent");
                    RejectPresentPhase1Arg(args.expectedInstallPathEnabled, argsPath + ".expectedInstallPathEnabled");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.expectedNormalizedEffectivePath), argsPath + ".expectedNormalizedEffectivePath");
                    return;
                case "set-root-name-state":
                    RejectPresentPhase1Arg(args.slotCount != 0, argsPath + ".slotCount");
                    if (args.customRootNameEnabled)
                        args.customRootName = RequireNonBlank(args.customRootName, argsPath + ".customRootName");
                    else
                        RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.customRootName), argsPath + ".customRootName");
                    RejectPresentPhase1Arg(HasAny(args.customPresetNames), argsPath + ".customPresetNames");
                    RejectPresentPhase1Arg(HasAnyActionLabel(args), argsPath + ".customSaveLabel/customLoadLabel/customClearLabel/customConfirmLabel");
                    return;
                case "set-preset-name-mask":
                    RejectPresentPhase1Arg(args.slotCount != 0, argsPath + ".slotCount");
                    RejectPresentPhase1Arg(!args.customRootNameEnabled && !string.IsNullOrWhiteSpace(args.customRootName), argsPath + ".customRootName");
                    ValidatePresetNameMask(args.customPresetNames, 8, argsPath + ".customPresetNames", requireAnyValue: true);
                    RejectPresentPhase1Arg(HasAnyActionLabel(args), argsPath + ".customSaveLabel/customLoadLabel/customClearLabel/customConfirmLabel");
                    return;
                case "set-action-label-mask":
                    RejectPresentPhase1Arg(args.slotCount != 0, argsPath + ".slotCount");
                    RejectPresentPhase1Arg(!args.customRootNameEnabled && !string.IsNullOrWhiteSpace(args.customRootName), argsPath + ".customRootName");
                    RejectPresentPhase1Arg(HasAny(args.customPresetNames), argsPath + ".customPresetNames");
                    if (!HasAnyActionLabel(args))
                        throw new InvalidOperationException(argsPath + ".customSaveLabel/customLoadLabel/customClearLabel/customConfirmLabel must include at least one label value.");
                    return;
                case "set-icon-mode":
                    RequireIconMode(args.iconMode, argsPath + ".iconMode");
                    RejectPresentPhase1Arg(args.selectedGearIndex >= 0, argsPath + ".selectedGearIndex");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.gearColor), argsPath + ".gearColor");
                    return;
                case "set-gear-color":
                    RequireGearColorSelection(args, argsPath);
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.iconMode), argsPath + ".iconMode");
                    return;
                case "set-custom-icons-enabled":
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.iconMode), argsPath + ".iconMode");
                    RejectPresentPhase1Arg(HasAnyIconFixture(args), argsPath + ".rootIconFixtureId/slotIconFixtureIdsBySlot/saveIconFixtureId/loadIconFixtureId/clearIconFixtureId");
                    return;
                case "set-root-icon-fixture":
                    RejectPresentPhase1Arg(string.IsNullOrWhiteSpace(args.rootIconFixtureId), argsPath + ".rootIconFixtureId");
                    RejectPresentPhase1Arg(HasAny(args.slotIconFixtureIds) || HasAny(args.slotIconFixtureIdsBySlot), argsPath + ".slotIconFixtureIdsBySlot");
                    RejectPresentPhase1Arg(HasAnyActionIconFixture(args), argsPath + ".saveIconFixtureId/loadIconFixtureId/clearIconFixtureId");
                    return;
                case "set-slot-icon-mask":
                    ValidateSlotIconMask(GetSlotIconFixtureIdsBySlot(args), 8, argsPath + ".slotIconFixtureIdsBySlot", requireAnyValue: true);
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.rootIconFixtureId), argsPath + ".rootIconFixtureId");
                    RejectPresentPhase1Arg(HasAnyActionIconFixture(args), argsPath + ".saveIconFixtureId/loadIconFixtureId/clearIconFixtureId");
                    return;
                case "set-action-icon-mask":
                    if (!string.IsNullOrWhiteSpace(args.actionIconMode))
                        RequireActionIconMode(args.actionIconMode, argsPath + ".actionIconMode");
                    if (!HasAnyActionIconFixture(args) && string.IsNullOrWhiteSpace(args.actionIconMode))
                        throw new InvalidOperationException(argsPath + ".actionIconMode or saveIconFixtureId/loadIconFixtureId/clearIconFixtureId must include a value.");
                    RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.rootIconFixtureId), argsPath + ".rootIconFixtureId");
                    RejectPresentPhase1Arg(HasAny(args.slotIconFixtureIds) || HasAny(args.slotIconFixtureIdsBySlot), argsPath + ".slotIconFixtureIdsBySlot");
                    return;
                case "assert-parameter-backup-option-present":
                    RejectPresentPhase1Arg(args.useParameterExclusions, argsPath + ".useParameterExclusions");
                    RequireParameterBackupSelector(args, argsPath, requireAnyValue: true);
                    return;
                case "set-parameter-backup-state":
                    RejectPresentPhase1Arg(args.slotCount != 0, argsPath + ".slotCount");
                    if (args.useParameterExclusions)
                        RequireParameterBackupSelector(args, argsPath, requireAnyValue: false);
                    else
                        RejectPresentPhase1Arg(HasParameterBackupSelector(args), argsPath + ".parameterBackupPresetId/excludedParameterNames");
                    return;
                case "assert-pending-customization-snapshot":
                case "assert-attached-customization-snapshot":
                    RequireSlotCountInRange(args.slotCount, argsPath + ".slotCount");
                    args.expectedPrimaryAction = RequireNonBlank(args.expectedPrimaryAction, argsPath + ".expectedPrimaryAction");
                    string presetId = RequireInstallPathPreset(args.installPathPresetId, argsPath + ".installPathPresetId");
                    ValidateExpectedInstallPathMatchesPreset(args, presetId, argsPath);
                    if (args.customRootNameEnabled)
                        args.customRootName = RequireNonBlank(args.customRootName, argsPath + ".customRootName");
                    else
                        RejectPresentPhase1Arg(!string.IsNullOrWhiteSpace(args.customRootName), argsPath + ".customRootName");
                    ValidatePresetNameMask(args.customPresetNames, args.slotCount, argsPath + ".customPresetNames", requireAnyValue: false);
                    if (!string.IsNullOrWhiteSpace(args.iconMode))
                        RequireIconMode(args.iconMode, argsPath + ".iconMode");
                    if (!string.IsNullOrWhiteSpace(args.actionIconMode))
                        RequireActionIconMode(args.actionIconMode, argsPath + ".actionIconMode");
                    if (args.selectedGearIndex >= 0 || !string.IsNullOrWhiteSpace(args.gearColor))
                        RequireGearColorSelection(args, argsPath);
                    ValidateSlotIconMask(GetSlotIconFixtureIdsBySlot(args), args.slotCount, argsPath + ".slotIconFixtureIdsBySlot", requireAnyValue: false);
                    ValidateExcludedParameterNames(args.excludedParameterNames, argsPath + ".excludedParameterNames", requireAnyValue: args.useParameterExclusions && string.IsNullOrWhiteSpace(args.parameterBackupPresetId));
                    if (!string.IsNullOrWhiteSpace(args.parameterBackupPresetId))
                        RequireParameterBackupPreset(args.parameterBackupPresetId, argsPath + ".parameterBackupPresetId");
                    return;
                default:
                    return;
            }
        }

        private static string[] GetSlotIconFixtureIdsBySlot(ASMLiteSmokeStepArgs args)
        {
            if (args == null)
                return Array.Empty<string>();

            return args.slotIconFixtureIdsBySlot != null && args.slotIconFixtureIdsBySlot.Length > 0
                ? args.slotIconFixtureIdsBySlot
                : args.slotIconFixtureIds ?? Array.Empty<string>();
        }

        private static bool HasAny(string[] values)
        {
            return values != null && values.Any(value => !string.IsNullOrWhiteSpace(value));
        }

        private static bool HasAnyActionLabel(ASMLiteSmokeStepArgs args)
        {
            return args != null
                && (!string.IsNullOrWhiteSpace(args.customSaveLabel)
                    || !string.IsNullOrWhiteSpace(args.customLoadLabel)
                    || !string.IsNullOrWhiteSpace(args.customClearLabel)
                    || !string.IsNullOrWhiteSpace(args.customConfirmLabel));
        }

        private static bool HasAnyIconFixture(ASMLiteSmokeStepArgs args)
        {
            return args != null
                && (!string.IsNullOrWhiteSpace(args.rootIconFixtureId)
                    || HasAny(args.slotIconFixtureIds)
                    || HasAny(args.slotIconFixtureIdsBySlot)
                    || HasAnyActionIconFixture(args));
        }

        private static bool HasAnyActionIconFixture(ASMLiteSmokeStepArgs args)
        {
            return args != null
                && (!string.IsNullOrWhiteSpace(args.saveIconFixtureId)
                    || !string.IsNullOrWhiteSpace(args.loadIconFixtureId)
                    || !string.IsNullOrWhiteSpace(args.clearIconFixtureId));
        }

        private static bool HasParameterBackupSelector(ASMLiteSmokeStepArgs args)
        {
            return args != null
                && (!string.IsNullOrWhiteSpace(args.parameterBackupPresetId)
                    || HasAny(args.excludedParameterNames));
        }

        private static void RequireParameterBackupSelector(ASMLiteSmokeStepArgs args, string argsPath, bool requireAnyValue)
        {
            bool hasPreset = args != null && !string.IsNullOrWhiteSpace(args.parameterBackupPresetId);
            bool hasNames = args != null && HasAny(args.excludedParameterNames);
            if (!hasPreset && !hasNames && requireAnyValue)
                throw new InvalidOperationException(argsPath + ".parameterBackupPresetId or .excludedParameterNames is required.");
            if (hasPreset && hasNames)
                throw new InvalidOperationException(argsPath + ".parameterBackupPresetId and .excludedParameterNames cannot both be set.");
            if (hasPreset)
                RequireParameterBackupPreset(args.parameterBackupPresetId, argsPath + ".parameterBackupPresetId");
            ValidateExcludedParameterNames(args?.excludedParameterNames, argsPath + ".excludedParameterNames", requireAnyValue: false);
        }

        private static void ValidateExcludedParameterNames(string[] values, string path, bool requireAnyValue)
        {
            values = values ?? Array.Empty<string>();
            if (requireAnyValue && !HasAny(values))
                throw new InvalidOperationException(path + " must include at least one parameter name.");
            if (values.Any(string.IsNullOrWhiteSpace))
                throw new InvalidOperationException(path + " cannot contain blank parameter names.");
        }

        private static string RequireParameterBackupPreset(string value, string path)
        {
            return NormalizeEnumValue(
                value,
                path,
                ASMLite.Editor.ASMLiteParameterBackupPresetResolver.StablePresetIds.ToArray());
        }

        private static void ValidatePresetNameMask(string[] values, int slotCount, string path, bool requireAnyValue)
        {
            values = values ?? Array.Empty<string>();
            if (values.Length > slotCount)
                throw new InvalidOperationException($"{path} cannot contain more than {slotCount} value(s).");

            if (requireAnyValue && !HasAny(values))
                throw new InvalidOperationException(path + " must include at least one preset name value.");
        }

        private static void ValidateSlotIconMask(string[] values, int slotCount, string path, bool requireAnyValue)
        {
            values = values ?? Array.Empty<string>();
            if (values.Length > slotCount)
                throw new InvalidOperationException($"{path} cannot contain more than {slotCount} value(s).");

            if (requireAnyValue && !HasAny(values))
                throw new InvalidOperationException(path + " must include at least one icon fixture ID.");
        }

        private static string RequireIconMode(string value, string path)
        {
            return NormalizeEnumValue(value, path, new[] { "multiColor", "sameColor" });
        }

        private static string RequireActionIconMode(string value, string path)
        {
            return NormalizeEnumValue(value, path, new[] { "default", "custom" });
        }

        private static int RequireGearColorSelection(ASMLiteSmokeStepArgs args, string argsPath)
        {
            bool hasIndex = args != null && args.selectedGearIndex >= 0;
            bool hasColor = args != null && !string.IsNullOrWhiteSpace(args.gearColor);
            if (!hasIndex && !hasColor)
                throw new InvalidOperationException(argsPath + ".gearColor or .selectedGearIndex is required.");

            int index = hasIndex ? RequireGearIndex(args.selectedGearIndex, argsPath + ".selectedGearIndex") : ResolveGearColorIndex(args.gearColor, argsPath + ".gearColor");
            if (hasColor)
            {
                int colorIndex = ResolveGearColorIndex(args.gearColor, argsPath + ".gearColor");
                if (hasIndex && colorIndex != index)
                    throw new InvalidOperationException($"{argsPath}.gearColor does not match selectedGearIndex {index}.");
            }

            return index;
        }

        private static int RequireGearIndex(int value, string path)
        {
            if (value < 0 || value > 7)
                throw new InvalidOperationException(path + " must be between 0 and 7.");
            return value;
        }

        private static int ResolveGearColorIndex(string value, string path)
        {
            switch (RequireNonBlank(value, path).ToLowerInvariant())
            {
                case "blue": return 0;
                case "red": return 1;
                case "green": return 2;
                case "purple": return 3;
                case "cyan": return 4;
                case "orange": return 5;
                case "pink": return 6;
                case "yellow": return 7;
                default:
                    throw new InvalidOperationException(path + " must be one of Blue, Red, Green, Purple, Cyan, Orange, Pink, or Yellow.");
            }
        }

        private static void RejectPresentPhase1Arg(bool isPresent, string path)
        {
            if (isPresent)
                throw new InvalidOperationException(path + " is not valid for this actionType.");
        }

        private static void ValidateExpectedInstallPathMatchesPreset(ASMLiteSmokeStepArgs args, string presetId, string argsPath)
        {
            bool expectedEnabled;
            string expectedPath;
            ResolveInstallPathPreset(presetId, out expectedEnabled, out expectedPath);

            if (args.expectedInstallPathEnabled != expectedEnabled)
            {
                throw new InvalidOperationException(
                    $"{argsPath}.expectedInstallPathEnabled must be {expectedEnabled.ToString().ToLowerInvariant()} for installPathPresetId '{presetId}'.");
            }

            string normalizedExpectedPath = ASMLiteSmokeStepArgs.NormalizeInstallPath(expectedPath);
            if (!string.Equals(args.expectedNormalizedEffectivePath, normalizedExpectedPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{argsPath}.expectedNormalizedEffectivePath must be '{normalizedExpectedPath}' for installPathPresetId '{presetId}'.");
            }
        }

        private static void ResolveInstallPathPreset(string presetId, out bool expectedEnabled, out string expectedNormalizedEffectivePath)
        {
            switch (presetId)
            {
                case "disabled":
                    expectedEnabled = false;
                    expectedNormalizedEffectivePath = string.Empty;
                    return;
                case "root":
                    expectedEnabled = true;
                    expectedNormalizedEffectivePath = string.Empty;
                    return;
                case "simple":
                    expectedEnabled = true;
                    expectedNormalizedEffectivePath = "ASM-Lite";
                    return;
                case "nested":
                    expectedEnabled = true;
                    expectedNormalizedEffectivePath = "Avatars/ASM-Lite";
                    return;
                default:
                    throw new InvalidOperationException($"installPathPresetId '{presetId}' is not supported.");
            }
        }

        private static void RequireSlotCountInRange(int slotCount, string path)
        {
            if (slotCount < 1 || slotCount > 8)
                throw new InvalidOperationException(path + " must be between 1 and 8.");
        }

        private static string RequireInstallPathPreset(string value, string path)
        {
            return NormalizeEnumValue(value, path, new[] { "disabled", "root", "simple", "nested" });
        }

        private static string NormalizeEnumValue(string value, string path, string[] allowedValues)
        {
            string normalized = RequireNonBlank(value, path);
            if (!allowedValues.Contains(normalized, StringComparer.Ordinal))
                throw new InvalidOperationException($"{path} '{normalized}' is not supported.");
            return normalized;
        }

        private static string[] NormalizePresetGroups(string[] values, string path)
        {
            values = values ?? Array.Empty<string>();
            if (values.Length == 0)
                throw new InvalidOperationException(path + " must include at least one preset group.");

            var normalized = new string[values.Length];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < values.Length; index++)
            {
                string item = NormalizeUniqueId(values[index], $"{path}[{index}]", seen);
                normalized[index] = item;
            }

            return normalized;
        }

        private static string RequireNonBlank(string value, string path)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(path + " must not be blank.");

            return value.Trim();
        }
    }
}
