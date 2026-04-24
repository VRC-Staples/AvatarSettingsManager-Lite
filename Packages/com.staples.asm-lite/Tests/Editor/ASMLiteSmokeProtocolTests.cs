using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeProtocolTests
    {
        [Test]
        public void LoadCommandFixtures_round_trip_preserves_required_fields()
        {
            var launch = RoundTrip(ASMLiteSmokeProtocol.LoadCommandFixture("launch-session.json"));
            var runSuite = RoundTrip(ASMLiteSmokeProtocol.LoadCommandFixture("run-suite.json"));
            var review = RoundTrip(ASMLiteSmokeProtocol.LoadCommandFixture("review-decision.json"));

            Assert.AreEqual("launch-session", launch.commandType);
            Assert.AreEqual("SceneReload", launch.launchSession.globalResetDefault);
            Assert.AreEqual("run-suite", runSuite.commandType);
            Assert.AreEqual("lifecycle-roundtrip", runSuite.runSuite.suiteId);
            Assert.AreEqual("review-decision", review.commandType);
            Assert.AreEqual("return-to-suite-list", review.reviewDecision.decision);
        }

        [Test]
        public void LoadEventFixture_preserves_ordering_and_protocol_fields()
        {
            var events = ASMLiteSmokeProtocol.LoadEventFixture("events.sample.ndjson");

            Assert.That(events.Select(item => item.eventSeq).ToArray(), Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }));
            Assert.That(events.All(item => item.protocolVersion == "1.0.0"), Is.True);
            Assert.AreEqual("session-started", events[0].eventType);
            Assert.AreEqual("review-required", events[9].eventType);
            Assert.That(events[9].reviewDecisionOptions, Is.EqualTo(new[] { "return-to-suite-list", "rerun-suite", "exit" }));
            Assert.AreEqual("session-idle", events[10].eventType);

            var roundTripped = ASMLiteSmokeProtocol.LoadEventsFromNdjson(ASMLiteSmokeProtocol.ToNdjson(events));
            Assert.AreEqual(events.Length, roundTripped.Length);
            Assert.AreEqual(events[10].message, roundTripped[10].message);
        }

        [Test]
        public void LoadCommandFromJson_rejects_missing_protocol_version()
        {
            string rawJson = LoadFixtureJson("launch-session.json").Replace("\"protocolVersion\": \"1.0.0\",\n", string.Empty, StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeProtocol.LoadCommandFromJson(rawJson));
            StringAssert.Contains("protocolVersion", exception.Message);
        }

        [Test]
        public void LoadCommandFromJson_rejects_missing_typed_payload_for_command_type()
        {
            string rawJson = LoadFixtureJson("run-suite.json").Replace(
                "\"runSuite\": {\n    \"suiteId\": \"lifecycle-roundtrip\",\n    \"requestedBy\": \"operator\",\n    \"requestedResetDefault\": \"FullPackageRebuild\",\n    \"reason\": \"operator-selected\"\n  }",
                "\"reviewDecision\": {\n    \"runId\": \"run-0001-lifecycle-roundtrip\",\n    \"suiteId\": \"lifecycle-roundtrip\",\n    \"decision\": \"return-to-suite-list\",\n    \"requestedBy\": \"operator\",\n    \"notes\": \"wrong payload\"\n  }",
                StringComparison.Ordinal);

            var exception = Assert.Throws<InvalidOperationException>(() => ASMLiteSmokeProtocol.LoadCommandFromJson(rawJson));
            StringAssert.Contains("runSuite", exception.Message);
        }

        private static ASMLiteSmokeProtocolCommand RoundTrip(ASMLiteSmokeProtocolCommand command)
        {
            string json = ASMLiteSmokeProtocol.ToJson(command, prettyPrint: false);
            return ASMLiteSmokeProtocol.LoadCommandFromJson(json);
        }

        private static string LoadFixtureJson(string fileName)
        {
            string fixturePath = Path.Combine(ASMLiteSmokeContractPaths.GetProtocolFixtureDirectory(), fileName);
            return File.ReadAllText(fixturePath, Encoding.UTF8);
        }
    }
}
