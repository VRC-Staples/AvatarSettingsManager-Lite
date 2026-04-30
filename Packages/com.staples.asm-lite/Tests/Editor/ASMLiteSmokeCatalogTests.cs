using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeCatalogTests
    {
        [Test]
        public void LoadCanonical_preserves_expected_group_order_and_fixture_metadata()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();

            CollectionAssert.AreEqual(
                new[] { "preflight", "editor-window", "lifecycle", "playmode-runtime" },
                catalog.groups.Select(group => group.groupId).ToArray());
            Assert.AreEqual("Assets/Click ME.unity", catalog.fixture.scenePath);
            Assert.AreEqual("Oct25_Dress", catalog.fixture.avatarName);
            Assert.AreEqual("setup-package-presence", catalog.groups[0].suites[0].suiteId);
            Assert.AreEqual("setup-scene-avatar", catalog.groups[1].suites[0].suiteId);
            CollectionAssert.AreEqual(
                new[] { "open-scene", "open-window", "select-avatar", "add-prefab", "assert-primary-action" },
                catalog.groups[1].suites[0].cases[0].steps.Select(step => step.actionType).ToArray());
            CollectionAssert.IsSubsetOf(
                new[] { "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                catalog.groups.SelectMany(group => group.suites).Select(suite => suite.suiteId).ToArray());
            Assert.GreaterOrEqual(catalog.groups.Sum(group => group.suites.Length), 3);
            Assert.AreEqual("lifecycle-roundtrip", catalog.groups[2].suites[0].suiteId);
            Assert.AreEqual("playmode-runtime-validation", catalog.groups[3].suites[0].suiteId);
        }

        [Test]
        public void LoadCanonical_parses_suite_metadata_for_default_presets_and_destructive_gating()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "setup-package-presence", "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                suites.Where(suite => suite.defaultSelected).Select(suite => suite.suiteId).ToArray());
            Assert.IsTrue(suites.Where(suite => suite.defaultSelected).All(suite => !suite.IsDestructive));
            CollectionAssert.AreEquivalent(
                new[] { "setup-package-presence", "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                suites.Where(suite => suite.presetGroups.Contains("quick-default")).Select(suite => suite.suiteId).ToArray());
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "setup-scene-avatar",
                    "setup-scene-acquisition",
                    "setup-window-launch-focus",
                    "setup-avatar-discovery-selection",
                    "setup-scaffold-add-idempotency",
                    "setup-existing-state-recognition",
                    "setup-generated-asset-readiness",
                    "setup-negative-diagnostics",
                    "setup-destructive-recovery-reset",
                },
                suites.Where(suite => suite.presetGroups.Contains("all-setup")).Select(suite => suite.suiteId).ToArray());
            Assert.AreEqual("quick", suites.Single(suite => suite.suiteId == "setup-scene-avatar").speed);
            Assert.AreEqual("quick", suites.Single(suite => suite.suiteId == "setup-package-presence").speed);
            Assert.AreEqual("standard", suites.Single(suite => suite.suiteId == "lifecycle-roundtrip").speed);
        }

        [Test]
        public void LoadCanonical_includes_phase04_package_scene_and_window_suites()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToArray();

            ASMLiteSmokeSuiteDefinition packagePresence = suites.Single(suite => suite.suiteId == "setup-package-presence");
            Assert.That(packagePresence.defaultSelected, Is.True);
            Assert.AreEqual("quick", packagePresence.speed);
            CollectionAssert.AreEquivalent(new[] { "quick-default" }, packagePresence.presetGroups);
            Assert.AreEqual("ASM-Lite Readiness Check", packagePresence.label);
            Assert.That(packagePresence.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "window-menu-opens",
                "package-resources-present",
                "catalog-loads",
                "host-ready",
            }));

            ASMLiteSmokeSuiteDefinition sceneAcquisition = suites.Single(suite => suite.suiteId == "setup-scene-acquisition");
            Assert.That(sceneAcquisition.defaultSelected, Is.False);
            Assert.AreEqual("standard", sceneAcquisition.speed);
            CollectionAssert.Contains(sceneAcquisition.presetGroups, "all-setup");
            Assert.That(sceneAcquisition.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "canonical-scene-already-open",
                "different-scene-open",
                "temp-unsaved-scene-open",
                "missing-scene",
                "non-scene-path",
            }));
            ASMLiteSmokeStepArgs differentSceneArgs = sceneAcquisition.cases.Single(item => item.caseId == "different-scene-open").steps.Single().args;
            Assert.AreEqual("temp-scene-setup-restore", differentSceneArgs.fixtureMutation);
            ASMLiteSmokeStepArgs tempSceneArgs = sceneAcquisition.cases.Single(item => item.caseId == "temp-unsaved-scene-open").steps.Single().args;
            Assert.AreEqual("temp-scene-setup-restore", tempSceneArgs.fixtureMutation);
            ASMLiteSmokeStepArgs missingSceneArgs = sceneAcquisition.cases.Single(item => item.caseId == "missing-scene").steps.Single().args;
            Assert.That(missingSceneArgs.expectStepFailure, Is.True);
            Assert.AreEqual("SETUP_SCENE_MISSING", missingSceneArgs.expectedDiagnosticCode);
            StringAssert.Contains("scene could not be found", missingSceneArgs.expectedDiagnosticContains);
            ASMLiteSmokeStepArgs nonSceneArgs = sceneAcquisition.cases.Single(item => item.caseId == "non-scene-path").steps.Single().args;
            Assert.That(nonSceneArgs.expectStepFailure, Is.True);
            Assert.AreEqual("SETUP_SCENE_PATH_INVALID", nonSceneArgs.expectedDiagnosticCode);
            StringAssert.Contains("not a Unity scene", nonSceneArgs.expectedDiagnosticContains);

            ASMLiteSmokeSuiteDefinition windowLaunch = suites.Single(suite => suite.suiteId == "setup-window-launch-focus");
            Assert.That(windowLaunch.defaultSelected, Is.False);
            Assert.AreEqual("standard", windowLaunch.speed);
            CollectionAssert.Contains(windowLaunch.presetGroups, "all-setup");
            Assert.That(windowLaunch.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "closed-to-open",
                "already-open-reuse-focus",
                "stale-closed-window-handled",
            }));
        }

        [Test]
        public void LoadCanonical_includes_phase05_avatar_discovery_selection_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToArray();

            ASMLiteSmokeSuiteDefinition avatarSelection = suites.Single(suite => suite.suiteId == "setup-avatar-discovery-selection");
            Assert.That(avatarSelection.defaultSelected, Is.False);
            Assert.AreEqual("standard", avatarSelection.speed);
            Assert.AreEqual("safe", avatarSelection.risk);
            CollectionAssert.Contains(avatarSelection.presetGroups, "all-setup");
            Assert.That(avatarSelection.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "selected-canonical-avatar",
                "find-by-fixture-name",
                "wrong-object-selected",
                "wrong-avatar-selected",
                "selected-inactive-avatar",
                "unselected-inactive-avatar",
                "duplicate-avatar-name",
                "selected-duplicate-avatar-disambiguates",
                "prefab-asset-avatar-selected",
                "missing-avatar",
                "same-name-non-avatar-ignored",
            }));

            ASMLiteSmokeStepArgs wrongObjectArgs = avatarSelection.cases.Single(item => item.caseId == "wrong-object-selected").steps.Single().args;
            Assert.AreEqual("wrong-object-selection", wrongObjectArgs.fixtureMutation);
            Assert.That(wrongObjectArgs.expectStepFailure, Is.True);
            Assert.AreEqual("SETUP_SELECTED_OBJECT_NOT_AVATAR", wrongObjectArgs.expectedDiagnosticCode);
            StringAssert.Contains("selected object is not a valid avatar", wrongObjectArgs.expectedDiagnosticContains);

            ASMLiteSmokeStepArgs duplicateArgs = avatarSelection.cases.Single(item => item.caseId == "duplicate-avatar-name").steps.Single().args;
            Assert.AreEqual("duplicate-avatar-name", duplicateArgs.fixtureMutation);
            Assert.That(duplicateArgs.expectStepFailure, Is.True);
            Assert.AreEqual("SETUP_AVATAR_AMBIGUOUS", duplicateArgs.expectedDiagnosticCode);

            ASMLiteSmokeStepArgs prefabArgs = avatarSelection.cases.Single(item => item.caseId == "prefab-asset-avatar-selected").steps.Single().args;
            Assert.AreEqual("selected-prefab-asset", prefabArgs.fixtureMutation);
            Assert.That(prefabArgs.expectStepFailure, Is.True);
            Assert.AreEqual("SETUP_AVATAR_PREFAB_ASSET", prefabArgs.expectedDiagnosticCode);

            ASMLiteSmokeStepArgs missingArgs = avatarSelection.cases.Single(item => item.caseId == "missing-avatar").steps.Single().args;
            Assert.AreEqual("missing-avatar-by-override-name", missingArgs.fixtureMutation);
            Assert.That(missingArgs.expectStepFailure, Is.True);
            Assert.AreEqual("SETUP_AVATAR_NOT_FOUND", missingArgs.expectedDiagnosticCode);
        }

        [Test]
        public void LoadCanonical_includes_phase06_scaffold_existing_state_suites()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("setup-scaffold-add-idempotency"), Is.True);
            Assert.That(suites.ContainsKey("setup-existing-state-recognition"), Is.True);

            var scaffoldCaseIds = suites["setup-scaffold-add-idempotency"].cases.Select(item => item.caseId).ToArray();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "no-component-add-prefab-primary",
                    "add-prefab-succeeds",
                    "add-prefab-twice-idempotency",
                    "existing-component-rebuild-primary",
                    "selected-avatar-change-preserves-prior-avatar",
                },
                scaffoldCaseIds);

            var existingCaseIds = suites["setup-existing-state-recognition"].cases.Select(item => item.caseId).ToArray();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "package-managed-component-present",
                    "vendorized-state-present",
                    "detached-state-present",
                    "stale-generated-assets-present",
                    "generated-folder-without-component",
                },
                existingCaseIds);
        }

        [Test]
        public void LoadCanonical_includes_phase07b_generated_asset_readiness_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("setup-generated-asset-readiness"), Is.True);

            ASMLiteSmokeSuiteDefinition readiness = suites["setup-generated-asset-readiness"];
            Assert.That(readiness.defaultSelected, Is.False);
            Assert.AreEqual("standard", readiness.speed);
            Assert.AreEqual("safe", readiness.risk);
            CollectionAssert.Contains(readiness.presetGroups, "all-setup");
            Assert.That(readiness.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "clean-add-generation-ready-scaffold",
                "rebuild-action-available-after-add",
                "missing-generated-folder-handled",
                "stale-generated-folder-handled",
                "generated-references-package-managed-by-default",
            }));

            ASMLiteSmokeStepArgs cleanAddArgs = readiness.cases.Single(item => item.caseId == "clean-add-generation-ready-scaffold")
                .steps.Single(step => step.stepId == "add-prefab-clean-readiness").args;
            Assert.AreEqual("remove-component", cleanAddArgs.fixtureMutation);

            ASMLiteSmokeStepArgs rebuildArgs = readiness.cases.Single(item => item.caseId == "rebuild-action-available-after-add")
                .steps.Single(step => step.stepId == "assert-primary-action-after-readiness-add").args;
            Assert.AreEqual("Rebuild", rebuildArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs missingFolderArgs = readiness.cases.Single(item => item.caseId == "missing-generated-folder-handled")
                .steps.Single(step => step.stepId == "assert-primary-action-missing-generated-folder").args;
            Assert.AreEqual("missing-generated-folder", missingFolderArgs.fixtureMutation);
            Assert.AreEqual("Rebuild", missingFolderArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs staleFolderArgs = readiness.cases.Single(item => item.caseId == "stale-generated-folder-handled")
                .steps.Single(step => step.stepId == "assert-primary-action-stale-generated-folder").args;
            Assert.AreEqual("stale-generated-folder", staleFolderArgs.fixtureMutation);
            Assert.AreEqual("Rebuild", staleFolderArgs.expectedPrimaryAction);

            ASMLiteSmokeStepDefinition packageManagedStep = readiness.cases.Single(item => item.caseId == "generated-references-package-managed-by-default")
                .steps.Single(step => step.stepId == "assert-generated-references-package-managed");
            Assert.AreEqual("assert-generated-references-package-managed", packageManagedStep.actionType);
        }

        [Test]
        public void LoadCanonical_includes_phase07a_safe_negative_diagnostics_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("setup-negative-diagnostics"), Is.True);

            ASMLiteSmokeSuiteDefinition negatives = suites["setup-negative-diagnostics"];
            Assert.That(negatives.defaultSelected, Is.False);
            Assert.AreEqual("standard", negatives.speed);
            Assert.AreEqual("safe", negatives.risk);
            CollectionAssert.AreEquivalent(new[] { "safe-negatives", "all-setup" }, negatives.presetGroups);
            Assert.That(negatives.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "missing-scene",
                "non-scene-path",
                "missing-package-resource",
                "missing-avatar",
                "duplicate-avatar-ambiguity",
                "prefab-asset-avatar-selected",
                "wrong-object-selected",
            }));

            AssertExpectedDiagnostic(
                negatives,
                "missing-scene",
                "SETUP_SCENE_MISSING",
                "scene could not be found",
                expectedActionType: "open-scene",
                expectedScenePath: "Assets/Missing.unity");
            AssertExpectedDiagnostic(
                negatives,
                "non-scene-path",
                "SETUP_SCENE_PATH_INVALID",
                "not a Unity scene",
                expectedActionType: "open-scene",
                expectedScenePath: "Packages/com.staples.asm-lite/package.json");
            AssertExpectedDiagnostic(
                negatives,
                "missing-package-resource",
                "SETUP_PACKAGE_RESOURCE_MISSING",
                "prefab source was not found",
                expectedActionType: "assert-package-resource-present",
                expectedObjectName: "Packages/com.staples.asm-lite/Missing.prefab");
            AssertExpectedDiagnostic(
                negatives,
                "missing-avatar",
                "SETUP_AVATAR_NOT_FOUND",
                "No descriptor-bearing avatar",
                expectedMutation: "missing-avatar-by-override-name");
            AssertExpectedDiagnostic(
                negatives,
                "duplicate-avatar-ambiguity",
                "SETUP_AVATAR_AMBIGUOUS",
                "Multiple avatars match",
                expectedMutation: "duplicate-avatar-name");
            AssertExpectedDiagnostic(
                negatives,
                "prefab-asset-avatar-selected",
                "SETUP_AVATAR_PREFAB_ASSET",
                "prefab asset",
                expectedMutation: "selected-prefab-asset");
            AssertExpectedDiagnostic(
                negatives,
                "wrong-object-selected",
                "SETUP_SELECTED_OBJECT_NOT_AVATAR",
                "selected object is not a valid avatar",
                expectedMutation: "wrong-object-selection");
        }

        [Test]
        public void LoadCanonical_includes_phase08_destructive_recovery_reset_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("setup-destructive-recovery-reset"), Is.True);

            ASMLiteSmokeSuiteDefinition destructive = suites["setup-destructive-recovery-reset"];
            Assert.That(destructive.defaultSelected, Is.False);
            Assert.AreEqual("destructive", destructive.speed);
            Assert.AreEqual("destructive", destructive.risk);
            Assert.That(destructive.IsDestructive, Is.True);
            CollectionAssert.AreEquivalent(new[] { "all-setup", "destructive-drills" }, destructive.presetGroups);
            Assert.That(destructive.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "controlled-corrupt-generated-asset",
                "stale-vendorized-references",
                "removed-generated-assets-after-component",
                "removed-component-after-generated-assets",
                "interrupted-detached-state",
            }));

            AssertDestructiveResetCase(
                destructive,
                "controlled-corrupt-generated-asset",
                "controlled-corrupt-generated-asset",
                "Rebuild");
            AssertDestructiveResetCase(
                destructive,
                "stale-vendorized-references",
                "vendorized-state-baseline",
                "ReturnToPackageManaged");
            AssertDestructiveResetCase(
                destructive,
                "removed-generated-assets-after-component",
                "missing-generated-folder",
                "Rebuild");
            AssertDestructiveResetCase(
                destructive,
                "removed-component-after-generated-assets",
                "generated-folder-without-component",
                "AddPrefab");
            AssertDestructiveResetCase(
                destructive,
                "interrupted-detached-state",
                "detached-state-baseline",
                "AddPrefab");
        }

        [Test]
        public void LoadFromJson_rejects_unknown_suite_metadata_values()
        {
            string rawJson = LoadCanonicalCatalogJson().Replace("\"risk\": \"safe\"", "\"risk\": \"maybe\"", StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("risk", exception.Message);
        }

        [Test]
        public void LoadFromJson_rejects_blank_preset_groups()
        {
            string rawJson = LoadCanonicalCatalogJson().Replace("\"quick-default\"", "\"   \"", StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("presetGroups", exception.Message);
        }

        [Test]
        public void LoadFromJson_deserializes_typed_step_args()
        {
            string rawJson = BuildSingleStepCatalogJson(
                "\"args\": { "
                + "\"scenePath\": \"Assets/Setup.unity\", "
                + "\"avatarName\": \"Oct25_Dress\", "
                + "\"objectName\": \"Setup Host\", "
                + "\"fixtureMutation\": \"missing-scene\", "
                + "\"expectedPrimaryAction\": \"Add Prefab\", "
                + "\"expectedDiagnosticCode\": \"SETUP_SCENE_MISSING\", "
                + "\"expectedDiagnosticContains\": \"scene could not be found\", "
                + "\"expectedState\": \"host-ready\", "
                + "\"expectStepFailure\": true, "
                + "\"preserveFailureEvidence\": true, "
                + "\"requireCleanReset\": true }\n");

            var catalog = ASMLiteSmokeCatalog.LoadFromJson(rawJson);
            var args = catalog.groups[0].suites[0].cases[0].steps[0].args;

            Assert.AreEqual("Assets/Setup.unity", args.scenePath);
            Assert.AreEqual("Oct25_Dress", args.avatarName);
            Assert.AreEqual("Setup Host", args.objectName);
            Assert.AreEqual("missing-scene", args.fixtureMutation);
            Assert.AreEqual("Add Prefab", args.expectedPrimaryAction);
            Assert.AreEqual("SETUP_SCENE_MISSING", args.expectedDiagnosticCode);
            Assert.AreEqual("scene could not be found", args.expectedDiagnosticContains);
            Assert.AreEqual("host-ready", args.expectedState);
            Assert.IsTrue(args.expectStepFailure);
            Assert.IsTrue(args.preserveFailureEvidence);
            Assert.IsTrue(args.requireCleanReset);
        }

        [Test]
        public void LoadFromJson_allows_omitted_step_args_as_empty_typed_args()
        {
            var catalog = ASMLiteSmokeCatalog.LoadFromJson(BuildSingleStepCatalogJson(argsJson: null));
            var args = catalog.groups[0].suites[0].cases[0].steps[0].args;

            Assert.NotNull(args);
            Assert.AreEqual(string.Empty, args.scenePath);
            Assert.AreEqual(string.Empty, args.expectedDiagnosticCode);
            Assert.IsFalse(args.expectStepFailure);
        }

        [Test]
        public void LoadFromJson_rejects_expected_failure_without_code_and_text()
        {
            string rawJson = BuildSingleStepCatalogJson("\"args\": { \"expectStepFailure\": true, \"expectedDiagnosticCode\": \"SETUP_SCENE_MISSING\" }\n");

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("expectedDiagnosticContains", exception.Message);
        }

        [Test]
        public void LoadFromJson_rejects_blank_group_ids()
        {
            string rawJson = LoadCanonicalCatalogJson().Replace("\"groupId\": \"editor-window\"", "\"groupId\": \"   \"", StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("groupId", exception.Message);
        }

        [Test]
        public void LoadFromJson_rejects_duplicate_suite_ids()
        {
            string rawJson = LoadCanonicalCatalogJson().Replace("\"suiteId\": \"lifecycle-roundtrip\"", "\"suiteId\": \"setup-scene-avatar\"", StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("suiteId", exception.Message);
        }

        [Test]
        public void LoadFromJson_rejects_unknown_action_types()
        {
            string rawJson = LoadCanonicalCatalogJson().Replace("\"actionType\": \"open-window\"", "\"actionType\": \"mystery-action\"", StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("actionType", exception.Message);
        }

        [Test]
        public void LoadFromJson_rejects_empty_step_arrays()
        {
            const string rawJson = "{\n"
                + "  \"catalogVersion\": 1,\n"
                + "  \"protocolVersion\": \"1.0.0\",\n"
                + "  \"fixture\": { \"scenePath\": \"Assets/Click ME.unity\", \"avatarName\": \"Oct25_Dress\" },\n"
                + "  \"groups\": [\n"
                + "    {\n"
                + "      \"groupId\": \"editor-window\",\n"
                + "      \"label\": \"Editor Window\",\n"
                + "      \"description\": \"desc\",\n"
                + "      \"suites\": [\n"
                + "        {\n"
                + "          \"suiteId\": \"open-select-add\",\n"
                + "          \"label\": \"Open\",\n"
                + "          \"description\": \"desc\",\n"
                + "          \"resetOverride\": \"Inherit\",\n"
                + "          \"speed\": \"quick\",\n"
                + "          \"risk\": \"safe\",\n"
                + "          \"defaultSelected\": true,\n"
                + "          \"presetGroups\": [\"quick-default\"],\n"
                + "          \"requiresPlayMode\": false,\n"
                + "          \"stopOnFirstFailure\": true,\n"
                + "          \"expectedOutcome\": \"ok\",\n"
                + "          \"debugHint\": \"hint\",\n"
                + "          \"cases\": [\n"
                + "            {\n"
                + "              \"caseId\": \"window-scaffold\",\n"
                + "              \"label\": \"Case\",\n"
                + "              \"description\": \"desc\",\n"
                + "              \"expectedOutcome\": \"ok\",\n"
                + "              \"debugHint\": \"hint\",\n"
                + "              \"steps\": []\n"
                + "            }\n"
                + "          ]\n"
                + "        }\n"
                + "      ]\n"
                + "    }\n"
                + "  ]\n"
                + "}";

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(rawJson));
            StringAssert.Contains("steps", exception.Message);
        }

        private static void AssertExpectedDiagnostic(
            ASMLiteSmokeSuiteDefinition suite,
            string caseId,
            string expectedCode,
            string expectedText,
            string expectedActionType = "select-avatar",
            string expectedScenePath = null,
            string expectedObjectName = null,
            string expectedMutation = null)
        {
            ASMLiteSmokeStepDefinition step = suite.cases.Single(item => item.caseId == caseId).steps.Single();
            Assert.AreEqual(expectedActionType, step.actionType);
            Assert.That(step.args.expectStepFailure, Is.True);
            Assert.That(step.args.preserveFailureEvidence, Is.True);
            Assert.AreEqual(expectedCode, step.args.expectedDiagnosticCode);
            StringAssert.Contains(expectedText, step.args.expectedDiagnosticContains);
            if (expectedScenePath != null)
                Assert.AreEqual(expectedScenePath, step.args.scenePath);
            if (expectedObjectName != null)
                Assert.AreEqual(expectedObjectName, step.args.objectName);
            if (expectedMutation != null)
                Assert.AreEqual(expectedMutation, step.args.fixtureMutation);
        }

        private static void AssertDestructiveResetCase(
            ASMLiteSmokeSuiteDefinition suite,
            string caseId,
            string expectedMutation,
            string expectedPrimaryAction)
        {
            ASMLiteSmokeStepDefinition step = suite.cases.Single(item => item.caseId == caseId).steps.Single();
            Assert.AreEqual("assert-primary-action", step.actionType);
            Assert.AreEqual(expectedMutation, step.args.fixtureMutation);
            Assert.AreEqual(expectedPrimaryAction, step.args.expectedPrimaryAction);
            Assert.That(step.args.preserveFailureEvidence, Is.True);
            Assert.That(step.args.requireCleanReset, Is.True);
        }

        private static string BuildSingleStepCatalogJson(string argsJson)
        {
            string argsLine = string.IsNullOrWhiteSpace(argsJson) ? string.Empty : "              " + argsJson.Trim() + ",\n";
            return "{\n"
                + "  \"catalogVersion\": 1,\n"
                + "  \"protocolVersion\": \"1.0.0\",\n"
                + "  \"fixture\": { \"scenePath\": \"Assets/Click ME.unity\", \"avatarName\": \"Oct25_Dress\" },\n"
                + "  \"groups\": [\n"
                + "    {\n"
                + "      \"groupId\": \"editor-window\",\n"
                + "      \"label\": \"Editor Window\",\n"
                + "      \"description\": \"desc\",\n"
                + "      \"suites\": [\n"
                + "        {\n"
                + "          \"suiteId\": \"setup-negative-diagnostics\",\n"
                + "          \"label\": \"Setup Negative Diagnostics\",\n"
                + "          \"description\": \"desc\",\n"
                + "          \"resetOverride\": \"Inherit\",\n"
                + "          \"speed\": \"standard\",\n"
                + "          \"risk\": \"safe\",\n"
                + "          \"defaultSelected\": false,\n"
                + "          \"presetGroups\": [\"safe-negatives\"],\n"
                + "          \"requiresPlayMode\": false,\n"
                + "          \"stopOnFirstFailure\": true,\n"
                + "          \"expectedOutcome\": \"expected diagnostics match.\",\n"
                + "          \"debugHint\": \"hint\",\n"
                + "          \"cases\": [\n"
                + "            {\n"
                + "              \"caseId\": \"missing-scene\",\n"
                + "              \"label\": \"Missing scene\",\n"
                + "              \"description\": \"desc\",\n"
                + "              \"expectedOutcome\": \"diagnostic appears.\",\n"
                + "              \"debugHint\": \"hint\",\n"
                + "              \"steps\": [\n"
                + "                {\n"
                + "                  \"stepId\": \"assert-missing-scene\",\n"
                + "                  \"label\": \"Assert Missing Scene\",\n"
                + "                  \"description\": \"desc\",\n"
                + "                  \"actionType\": \"assert-host-ready\",\n"
                + argsLine
                + "                  \"expectedOutcome\": \"diagnostic appears.\",\n"
                + "                  \"debugHint\": \"hint\"\n"
                + "                }\n"
                + "              ]\n"
                + "            }\n"
                + "          ]\n"
                + "        }\n"
                + "      ]\n"
                + "    }\n"
                + "  ]\n"
                + "}";
        }

        private static string LoadCanonicalCatalogJson()
        {
            return File.ReadAllText(ASMLiteSmokeContractPaths.GetCatalogPath(), Encoding.UTF8);
        }
    }
}
