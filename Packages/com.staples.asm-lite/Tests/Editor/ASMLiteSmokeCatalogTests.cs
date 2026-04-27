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
            Assert.AreEqual(1, catalog.groups[0].suites.Length);
            Assert.AreEqual("lifecycle-roundtrip", catalog.groups[1].suites[0].suiteId);
            Assert.AreEqual("playmode-runtime-validation", catalog.groups[2].suites[0].suiteId);
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

        private static string LoadCanonicalCatalogJson()
        {
            return File.ReadAllText(ASMLiteSmokeContractPaths.GetCatalogPath(), Encoding.UTF8);
        }
    }
}
