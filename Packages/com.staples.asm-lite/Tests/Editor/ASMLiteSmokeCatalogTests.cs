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
                new[] { "editor-window", "lifecycle", "playmode-runtime" },
                catalog.groups.Select(group => group.groupId).ToArray());
            Assert.AreEqual("Assets/Click ME.unity", catalog.fixture.scenePath);
            Assert.AreEqual("Oct25_Dress", catalog.fixture.avatarName);
            Assert.AreEqual("setup-scene-avatar", catalog.groups[0].suites[0].suiteId);
            CollectionAssert.AreEqual(
                new[] { "open-scene", "open-window", "select-avatar", "add-prefab", "assert-primary-action" },
                catalog.groups[0].suites[0].cases[0].steps.Select(step => step.actionType).ToArray());
            CollectionAssert.IsSubsetOf(
                new[] { "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                catalog.groups.SelectMany(group => group.suites).Select(suite => suite.suiteId).ToArray());
            Assert.GreaterOrEqual(catalog.groups.Sum(group => group.suites.Length), 3);
            Assert.AreEqual("lifecycle-roundtrip", catalog.groups[1].suites[0].suiteId);
            Assert.AreEqual("playmode-runtime-validation", catalog.groups[2].suites[0].suiteId);
        }

        [Test]
        public void LoadCanonical_parses_suite_metadata_for_default_presets_and_destructive_gating()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var suites = catalog.groups.SelectMany(group => group.suites).ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "setup-scene-avatar", "lifecycle-roundtrip", "playmode-runtime-validation" },
                suites.Where(suite => suite.defaultSelected).Select(suite => suite.suiteId).ToArray());
            Assert.IsTrue(suites.All(suite => !suite.IsDestructive));
            Assert.IsTrue(suites.All(suite => suite.presetGroups.Contains("quick-default")));
            Assert.IsTrue(suites.Single(suite => suite.suiteId == "setup-scene-avatar").presetGroups.Contains("all-setup"));
            Assert.IsTrue(suites.Where(suite => suite.suiteId != "setup-scene-avatar").All(suite => !suite.presetGroups.Contains("all-setup")));
            Assert.AreEqual("quick", suites.Single(suite => suite.suiteId == "setup-scene-avatar").speed);
            Assert.AreEqual("standard", suites.Single(suite => suite.suiteId == "lifecycle-roundtrip").speed);
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
