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
            Assert.AreEqual("asm-lite-readiness-check", catalog.groups[0].suites[0].suiteId);
            Assert.AreEqual("setup-scene-avatar", catalog.groups[1].suites[0].suiteId);
            CollectionAssert.AreEqual(
                new[]
                {
                    "open-scene",
                    "close-window",
                    "open-window",
                    "assert-window-focused",
                    "close-window",
                    "open-window",
                    "assert-window-focused",
                    "select-avatar",
                    "add-prefab",
                    "assert-primary-action",
                },
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
                new[] { "asm-lite-readiness-check", "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                suites.Where(suite => suite.defaultSelected).Select(suite => suite.suiteId).ToArray());
            Assert.IsTrue(suites.Where(suite => suite.defaultSelected).All(suite => !suite.IsDestructive));
            CollectionAssert.AreEquivalent(
                new[] { "asm-lite-readiness-check", "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                suites.Where(suite => suite.presetGroups.Contains("quick-default")).Select(suite => suite.suiteId).ToArray());
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "setup-scene-avatar",
                    "avatar-discovery-selection-regression",
                    "add-prefab-idempotency",
                    "installed-state-recognition",
                    "generated-asset-recovery-signals",
                    "generated-reference-ownership",
                    "negative-diagnostics",
                    "setup-prebuild-slots-matrix",
                    "setup-prebuild-path-matrix",
                    "destructive-recovery-reset",
                },
                suites.Where(suite => suite.presetGroups.Contains("all-setup")).Select(suite => suite.suiteId).ToArray());
            Assert.AreEqual("quick", suites.Single(suite => suite.suiteId == "setup-scene-avatar").speed);
            Assert.AreEqual("quick", suites.Single(suite => suite.suiteId == "asm-lite-readiness-check").speed);
            Assert.AreEqual("standard", suites.Single(suite => suite.suiteId == "lifecycle-roundtrip").speed);
        }

        [Test]
        public void LoadCanonical_moves_window_launch_smoke_into_default_setup_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToArray();

            ASMLiteSmokeSuiteDefinition packagePresence = suites.Single(suite => suite.suiteId == "asm-lite-readiness-check");
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
                "canonical-scene-already-open",
                "temp-unsaved-scene-open",
            }));

            ASMLiteSmokeStepArgs tempSceneArgs = packagePresence.cases.Single(item => item.caseId == "temp-unsaved-scene-open").steps.Single().args;
            Assert.AreEqual("temp-scene-setup-restore", tempSceneArgs.fixtureMutation);
            Assert.That(suites.Any(suite => suite.suiteId == "setup-scene-acquisition"), Is.False);

            Assert.That(suites.Any(suite => suite.suiteId == "setup-window-launch-focus"), Is.False);

            ASMLiteSmokeSuiteDefinition defaultSetup = suites.Single(suite => suite.suiteId == "setup-scene-avatar");
            Assert.That(defaultSetup.defaultSelected, Is.True);
            Assert.AreEqual("quick", defaultSetup.speed);
            CollectionAssert.Contains(defaultSetup.presetGroups, "all-setup");
            Assert.That(defaultSetup.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "setup-scene-avatar",
            }));
            CollectionAssert.AreEqual(
                new[]
                {
                    "open-scene",
                    "close-window",
                    "open-window",
                    "assert-window-focused",
                    "close-window",
                    "open-window",
                    "assert-window-focused",
                    "select-avatar",
                    "add-prefab",
                    "assert-primary-action",
                },
                defaultSetup.cases.Single().steps.Select(step => step.actionType).ToArray());
        }

        [Test]
        public void LoadCanonical_includes_phase05_avatar_discovery_selection_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToArray();

            ASMLiteSmokeSuiteDefinition avatarSelection = suites.Single(suite => suite.suiteId == "avatar-discovery-selection-regression");
            Assert.That(avatarSelection.defaultSelected, Is.False);
            Assert.AreEqual("standard", avatarSelection.speed);
            Assert.AreEqual("safe", avatarSelection.risk);
            Assert.AreEqual("Avatar Picking Regression Checks", avatarSelection.label);
            Assert.AreEqual(
                "Check that the smoke tools pick the avatar you meant to edit, and explain clearly when the target is missing, duplicated, hidden, or not a scene avatar.",
                avatarSelection.description);
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
            Assert.That(avatarSelection.cases.Select(item => item.label), Is.EqualTo(new[]
            {
                "Use the avatar I already picked",
                "Find the main avatar by name",
                "Tell me I picked the wrong object",
                "Respect the avatar I picked",
                "Allow the avatar I picked even if it is hidden",
                "Do not auto-pick a hidden avatar",
                "Ask me to choose when two avatars match",
                "Use my pick when names clash",
                "Reject a prefab from the Project window",
                "Explain when the avatar is missing",
                "Ignore same-name objects that are not avatars",
            }));
            Assert.That(avatarSelection.cases.Select(item => item.steps.Single().label), Is.EqualTo(new[]
            {
                "Selected avatar is used",
                "Avatar is found by name",
                "Non-avatar selection is reported",
                "Selected alternate avatar is used",
                "Selected inactive avatar is used",
                "Unselected inactive avatar is not used",
                "Duplicate avatar names are reported",
                "Selected avatar resolves duplicate names",
                "Prefab asset selection is reported",
                "Missing avatar is reported",
                "Same-name non-avatar objects are ignored",
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

            Assert.That(suites.ContainsKey("add-prefab-idempotency"), Is.True);
            Assert.That(suites.ContainsKey("installed-state-recognition"), Is.True);

            ASMLiteSmokeSuiteDefinition addPrefabIdempotency = suites["add-prefab-idempotency"];
            Assert.AreEqual("Add Prefab Idempotency", addPrefabIdempotency.label);
            Assert.That(addPrefabIdempotency.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "no-component-add-prefab-primary",
                "add-prefab-twice-idempotency",
            }));

            ASMLiteSmokeStepDefinition secondAddStep = addPrefabIdempotency.cases.Single(item => item.caseId == "add-prefab-twice-idempotency")
                .steps.Single(step => step.stepId == "add-prefab-second");
            Assert.That(string.IsNullOrEmpty(secondAddStep.args?.fixtureMutation), Is.True);

            ASMLiteSmokeStepArgs noComponentPrimaryArgs = addPrefabIdempotency.cases.Single(item => item.caseId == "no-component-add-prefab-primary")
                .steps.Single(step => step.stepId == "assert-primary-action-no-component").args;
            Assert.AreEqual("clean-add-baseline", noComponentPrimaryArgs.fixtureMutation);

            ASMLiteSmokeStepArgs firstAddArgs = addPrefabIdempotency.cases.Single(item => item.caseId == "add-prefab-twice-idempotency")
                .steps.Single(step => step.stepId == "add-prefab-first").args;
            Assert.AreEqual("clean-add-baseline", firstAddArgs.fixtureMutation);

            ASMLiteSmokeStepDefinition finalAssertStep = addPrefabIdempotency.cases.Single(item => item.caseId == "add-prefab-twice-idempotency")
                .steps.Single(step => step.stepId == "assert-primary-action-twice");
            Assert.That(string.IsNullOrEmpty(finalAssertStep.args?.fixtureMutation), Is.True);

            ASMLiteSmokeSuiteDefinition installedStateRecognition = suites["installed-state-recognition"];
            Assert.AreEqual("Installed State Recognition", installedStateRecognition.label);

            var existingCaseIds = installedStateRecognition.cases.Select(item => item.caseId).ToArray();
            CollectionAssert.AreEquivalent(
                new[]
                {
                    "package-managed-component-present",
                    "vendorized-state-present",
                    "detached-state-present",
                },
                existingCaseIds);
        }

        [Test]
        public void LoadCanonical_splits_generated_asset_recovery_and_reference_ownership_suites()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("generated-asset-recovery-signals"), Is.True);
            Assert.That(suites.ContainsKey("generated-reference-ownership"), Is.True);

            ASMLiteSmokeSuiteDefinition recovery = suites["generated-asset-recovery-signals"];
            Assert.That(recovery.defaultSelected, Is.False);
            Assert.AreEqual("standard", recovery.speed);
            Assert.AreEqual("safe", recovery.risk);
            CollectionAssert.Contains(recovery.presetGroups, "all-setup");
            Assert.That(recovery.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "clean-add-generation-ready-scaffold",
                "rebuild-action-available-after-add",
                "missing-generated-folder-handled",
                "stale-generated-folder-handled",
                "generated-folder-without-component",
            }));

            ASMLiteSmokeStepArgs cleanAddArgs = recovery.cases.Single(item => item.caseId == "clean-add-generation-ready-scaffold")
                .steps.Single(step => step.stepId == "add-prefab-clean-readiness").args;
            Assert.AreEqual("clean-add-baseline", cleanAddArgs.fixtureMutation);

            ASMLiteSmokeStepArgs rebuildArgs = recovery.cases.Single(item => item.caseId == "rebuild-action-available-after-add")
                .steps.Single(step => step.stepId == "assert-primary-action-after-readiness-add").args;
            Assert.AreEqual("Rebuild", rebuildArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs missingFolderArgs = recovery.cases.Single(item => item.caseId == "missing-generated-folder-handled")
                .steps.Single(step => step.stepId == "assert-primary-action-missing-generated-folder").args;
            Assert.AreEqual("missing-generated-folder", missingFolderArgs.fixtureMutation);
            Assert.AreEqual("Rebuild", missingFolderArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs staleFolderArgs = recovery.cases.Single(item => item.caseId == "stale-generated-folder-handled")
                .steps.Single(step => step.stepId == "assert-primary-action-stale-generated-folder").args;
            Assert.AreEqual("stale-generated-folder", staleFolderArgs.fixtureMutation);
            Assert.AreEqual("Rebuild", staleFolderArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs generatedFolderWithoutComponentArgs = recovery.cases.Single(item => item.caseId == "generated-folder-without-component")
                .steps.Single(step => step.stepId == "assert-primary-action-generated-folder-without-component").args;
            Assert.AreEqual("generated-folder-without-component", generatedFolderWithoutComponentArgs.fixtureMutation);
            Assert.AreEqual("Add Prefab", generatedFolderWithoutComponentArgs.expectedPrimaryAction);

            ASMLiteSmokeSuiteDefinition ownership = suites["generated-reference-ownership"];
            Assert.That(ownership.defaultSelected, Is.False);
            Assert.AreEqual("standard", ownership.speed);
            Assert.AreEqual("safe", ownership.risk);
            CollectionAssert.Contains(ownership.presetGroups, "all-setup");
            Assert.That(ownership.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "generated-references-package-managed-by-default",
            }));

            ASMLiteSmokeStepDefinition packageManagedStep = ownership.cases.Single(item => item.caseId == "generated-references-package-managed-by-default")
                .steps.Single(step => step.stepId == "assert-generated-references-package-managed");
            Assert.AreEqual("assert-generated-references-package-managed", packageManagedStep.actionType);

            ASMLiteSmokeStepArgs packageManagedAddArgs = ownership.cases.Single(item => item.caseId == "generated-references-package-managed-by-default")
                .steps.Single(step => step.stepId == "add-prefab-for-package-managed-references").args;
            Assert.AreEqual("clean-add-baseline", packageManagedAddArgs.fixtureMutation);
        }

        [Test]
        public void LoadCanonical_includes_phase07a_safe_negative_diagnostics_suite()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("negative-diagnostics"), Is.True);

            ASMLiteSmokeSuiteDefinition negatives = suites["negative-diagnostics"];
            Assert.That(negatives.defaultSelected, Is.False);
            Assert.AreEqual("standard", negatives.speed);
            Assert.AreEqual("safe", negatives.risk);
            CollectionAssert.AreEquivalent(new[] { "safe-negatives", "all-setup" }, negatives.presetGroups);
            Assert.That(negatives.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "missing-package-resource",
                "missing-avatar",
                "duplicate-avatar-ambiguity",
                "prefab-asset-avatar-selected",
                "wrong-object-selected",
            }));
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

            Assert.That(suites.ContainsKey("destructive-recovery-reset"), Is.True);

            ASMLiteSmokeSuiteDefinition destructive = suites["destructive-recovery-reset"];
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
        public void LoadCanonical_includes_phase1_prebuild_slot_and_path_matrices()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToDictionary(suite => suite.suiteId);

            Assert.That(suites.ContainsKey("setup-prebuild-slots-matrix"), Is.True);
            Assert.That(suites.ContainsKey("setup-prebuild-path-matrix"), Is.True);

            ASMLiteSmokeSuiteDefinition slots = suites["setup-prebuild-slots-matrix"];
            Assert.AreEqual("exhaustive", slots.speed);
            Assert.AreEqual("safe", slots.risk);
            CollectionAssert.Contains(slots.presetGroups, "all-setup");
            Assert.That(slots.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "S01-slot-count-1",
                "S02-slot-count-2",
                "S03-slot-count-3",
                "S04-slot-count-4",
                "S05-slot-count-5",
                "S06-slot-count-6",
                "S07-slot-count-7",
                "S08-slot-count-8",
            }));
            Assert.That(slots.cases.Select(item => item.steps[0].actionType).ToArray(),
                Is.All.EqualTo("prelude-recover-context"));
            AssertPhase1SlotCase(slots.cases[0], 1);
            AssertPhase1SlotCase(slots.cases[7], 8);

            ASMLiteSmokeSuiteDefinition paths = suites["setup-prebuild-path-matrix"];
            Assert.AreEqual("exhaustive", paths.speed);
            Assert.AreEqual("safe", paths.risk);
            CollectionAssert.Contains(paths.presetGroups, "all-setup");
            Assert.That(paths.cases.Select(item => item.caseId), Is.EqualTo(new[]
            {
                "P01-install-path-disabled",
                "P02-install-path-root-selected",
                "P03-install-path-simple",
                "P04-install-path-nested",
            }));
            Assert.That(paths.cases.Select(item => item.steps[0].actionType).ToArray(),
                Is.All.EqualTo("prelude-recover-context"));
            AssertPhase1PathCase(paths.cases[0], "disabled", expectedEnabled: false, expectedNormalizedPath: string.Empty);
            AssertPhase1PathCase(paths.cases[1], "root", expectedEnabled: true, expectedNormalizedPath: string.Empty);
            AssertPhase1PathCase(paths.cases[2], "simple", expectedEnabled: true, expectedNormalizedPath: "ASM-Lite");
            AssertPhase1PathCase(paths.cases[3], "nested", expectedEnabled: true, expectedNormalizedPath: "Avatars/ASM-Lite");
        }

        [Test]
        public void LoadCanonical_exhaustive_suites_start_with_recover_context_prelude()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var exhaustiveSuites = catalog.groups
                .SelectMany(group => group.suites)
                .Where(suite => string.Equals(suite.speed, "exhaustive", StringComparison.Ordinal))
                .ToArray();

            Assert.That(exhaustiveSuites.Select(suite => suite.suiteId), Is.Not.Empty);
            foreach (ASMLiteSmokeSuiteDefinition suite in exhaustiveSuites)
            {
                foreach (ASMLiteSmokeCaseDefinition item in suite.cases)
                {
                    Assert.That(item.steps, Is.Not.Empty, suite.suiteId + ":" + item.caseId);
                    Assert.AreEqual("prelude-recover-context", item.steps[0].actionType, suite.suiteId + ":" + item.caseId);
                }
            }
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
        public void LoadFromJson_allows_phase1_action_tokens_and_snapshot_step_args()
        {
            string[] phase1Actions =
            {
                "assert-no-component",
                "set-slot-count",
                "set-install-path-state",
                "assert-pending-customization-snapshot",
                "assert-attached-customization-snapshot",
            };

            foreach (string actionType in phase1Actions)
            {
                string argsJson = BuildPhase1ArgsJson(actionType);
                ASMLiteSmokeCatalogDocument catalog = ASMLiteSmokeCatalog.LoadFromJson(BuildSingleStepCatalogJson(argsJson, actionType));
                ASMLiteSmokeStepDefinition step = catalog.groups[0].suites[0].cases[0].steps[0];

                Assert.AreEqual(actionType, step.actionType);
                Assert.NotNull(step.args);
                if (string.Equals(actionType, "set-slot-count", StringComparison.Ordinal))
                    Assert.AreEqual(4, step.args.slotCount);
                if (string.Equals(actionType, "set-install-path-state", StringComparison.Ordinal))
                    Assert.AreEqual("nested", step.args.installPathPresetId);
                if (actionType.Contains("customization-snapshot"))
                {
                    Assert.AreEqual(6, step.args.slotCount);
                    Assert.AreEqual("simple", step.args.installPathPresetId);
                    Assert.IsTrue(step.args.expectedInstallPathEnabled);
                    Assert.AreEqual("ASM-Lite", step.args.expectedNormalizedEffectivePath);
                    Assert.IsTrue(step.args.expectedComponentPresent);
                    Assert.AreEqual("Rebuild", step.args.expectedPrimaryAction);
                }
            }
        }

        [Test]
        public void LoadFromJson_rejects_malformed_phase1_args_with_field_specific_paths()
        {
            var slotCountException = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(
                BuildSingleStepCatalogJson("\"args\": { \"slotCount\": 0 }\n", "set-slot-count")));
            StringAssert.Contains("args.slotCount", slotCountException.Message);

            var installPathException = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(
                BuildSingleStepCatalogJson("\"args\": { \"installPathPresetId\": \"sideways\" }\n", "set-install-path-state")));
            StringAssert.Contains("args.installPathPresetId", installPathException.Message);

            var snapshotException = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(
                BuildSingleStepCatalogJson(
                    "\"args\": { \"slotCount\": 2, \"expectedComponentPresent\": true, \"expectedInstallPathEnabled\": false }\n",
                    "assert-attached-customization-snapshot")));
            StringAssert.Contains("args.expectedPrimaryAction", snapshotException.Message);

            var forbiddenSlotArgsException = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(
                BuildSingleStepCatalogJson(
                    "\"args\": { \"slotCount\": 2, \"installPathPresetId\": \"simple\" }\n",
                    "set-slot-count")));
            StringAssert.Contains("args.installPathPresetId", forbiddenSlotArgsException.Message);

            var forbiddenPathArgsException = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(
                BuildSingleStepCatalogJson(
                    "\"args\": { \"slotCount\": 2, \"installPathPresetId\": \"simple\" }\n",
                    "set-install-path-state")));
            StringAssert.Contains("args.slotCount", forbiddenPathArgsException.Message);

            var inconsistentPresetException = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeCatalog.LoadFromJson(
                BuildSingleStepCatalogJson(
                    "\"args\": { \"slotCount\": 2, \"installPathPresetId\": \"root\", \"expectedInstallPathEnabled\": false, \"expectedNormalizedEffectivePath\": \"\", \"expectedPrimaryAction\": \"Add Prefab\" }\n",
                    "assert-pending-customization-snapshot")));
            StringAssert.Contains("args.expectedInstallPathEnabled", inconsistentPresetException.Message);
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

        private static void AssertPhase1SlotCase(ASMLiteSmokeCaseDefinition item, int expectedSlotCount)
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "prelude-recover-context",
                    "open-scene",
                    "open-window",
                    "assert-window-focused",
                    "select-avatar",
                    "assert-no-component",
                    "set-slot-count",
                    "assert-pending-customization-snapshot",
                    "add-prefab",
                    "assert-attached-customization-snapshot",
                    "assert-primary-action",
                },
                item.steps.Select(step => step.actionType).ToArray());

            ASMLiteSmokeStepArgs setArgs = item.steps.Single(step => step.actionType == "set-slot-count").args;
            Assert.AreEqual(expectedSlotCount, setArgs.slotCount);

            foreach (ASMLiteSmokeStepDefinition step in item.steps.Where(step => step.actionType.Contains("customization-snapshot")))
            {
                Assert.AreEqual(expectedSlotCount, step.args.slotCount);
                Assert.AreEqual("disabled", step.args.installPathPresetId);
                Assert.That(step.args.expectedInstallPathEnabled, Is.False);
                Assert.AreEqual(string.Empty, step.args.expectedNormalizedEffectivePath);
            }

            ASMLiteSmokeStepArgs pendingArgs = item.steps.Single(step => step.actionType == "assert-pending-customization-snapshot").args;
            Assert.That(pendingArgs.expectedComponentPresent, Is.False);
            Assert.AreEqual("Add Prefab", pendingArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs attachedArgs = item.steps.Single(step => step.actionType == "assert-attached-customization-snapshot").args;
            Assert.That(attachedArgs.expectedComponentPresent, Is.True);
            Assert.AreEqual("Rebuild", attachedArgs.expectedPrimaryAction);
        }

        private static void AssertPhase1PathCase(
            ASMLiteSmokeCaseDefinition item,
            string expectedPreset,
            bool expectedEnabled,
            string expectedNormalizedPath)
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "prelude-recover-context",
                    "open-scene",
                    "open-window",
                    "assert-window-focused",
                    "select-avatar",
                    "assert-no-component",
                    "set-install-path-state",
                    "assert-pending-customization-snapshot",
                    "add-prefab",
                    "assert-attached-customization-snapshot",
                    "assert-primary-action",
                },
                item.steps.Select(step => step.actionType).ToArray());

            Assert.AreEqual(expectedPreset, item.steps.Single(step => step.actionType == "set-install-path-state").args.installPathPresetId);

            foreach (ASMLiteSmokeStepDefinition step in item.steps.Where(step => step.actionType.Contains("customization-snapshot")))
            {
                Assert.AreEqual(4, step.args.slotCount);
                Assert.AreEqual(expectedPreset, step.args.installPathPresetId);
                Assert.AreEqual(expectedEnabled, step.args.expectedInstallPathEnabled);
                Assert.AreEqual(expectedNormalizedPath, step.args.expectedNormalizedEffectivePath);
            }

            ASMLiteSmokeStepArgs pendingArgs = item.steps.Single(step => step.actionType == "assert-pending-customization-snapshot").args;
            Assert.That(pendingArgs.expectedComponentPresent, Is.False);
            Assert.AreEqual("Add Prefab", pendingArgs.expectedPrimaryAction);

            ASMLiteSmokeStepArgs attachedArgs = item.steps.Single(step => step.actionType == "assert-attached-customization-snapshot").args;
            Assert.That(attachedArgs.expectedComponentPresent, Is.True);
            Assert.AreEqual("Rebuild", attachedArgs.expectedPrimaryAction);
        }

        private static string BuildSingleStepCatalogJson(string argsJson, string actionType = "assert-host-ready")
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
                + "          \"suiteId\": \"negative-diagnostics\",\n"
                + "          \"label\": \"Negative Diagnostics\",\n"
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
                + "                  \"actionType\": \"" + actionType + "\",\n"
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

        private static string BuildPhase1ArgsJson(string actionType)
        {
            switch (actionType)
            {
                case "set-slot-count":
                    return "\"args\": { \"slotCount\": 4 }\n";
                case "set-install-path-state":
                    return "\"args\": { \"installPathPresetId\": \"nested\" }\n";
                case "assert-pending-customization-snapshot":
                case "assert-attached-customization-snapshot":
                    return "\"args\": { "
                        + "\"slotCount\": 6, "
                        + "\"installPathPresetId\": \"simple\", "
                        + "\"expectedInstallPathEnabled\": true, "
                        + "\"expectedNormalizedEffectivePath\": \"ASM-Lite\", "
                        + "\"expectedComponentPresent\": true, "
                        + "\"expectedPrimaryAction\": \"Rebuild\" }\n";
                default:
                    return null;
            }
        }

        private static string LoadCanonicalCatalogJson()
        {
            return File.ReadAllText(ASMLiteSmokeContractPaths.GetCatalogPath(), Encoding.UTF8);
        }
    }
}
