using System.Linq;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeProtocolCompatibilityTests
    {
        [Test]
        public void ValidStartup_exact_protocol_match_allows_run_suite_acceptance()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var session = ASMLiteSmokeProtocol.LoadSessionFixture("session.valid.json");

            var compatibility = ASMLiteSmokeProtocol.EvaluateCompatibility(
                ASMLiteSmokeProtocol.SupportedProtocolVersion,
                session,
                catalog.protocolVersion);

            Assert.IsTrue(compatibility.isCompatible, compatibility.message);

            var hostState = ASMLiteSmokeProtocol.LoadHostStateFixture("host-state.ready.json");
            bool accepted = ASMLiteSmokeProtocol.CanAcceptRunSuite(hostState, out string reason);
            Assert.IsTrue(accepted, reason);

            var runSuiteCommand = ASMLiteSmokeProtocol.LoadCommandFixture("run-suite.json");
            Assert.AreEqual("run-suite", runSuiteCommand.commandType);
        }

        [Test]
        public void MismatchStartup_emits_protocol_error_contract_and_blocks_startup()
        {
            var catalog = ASMLiteSmokeCatalog.LoadCanonical();
            var mismatchSession = ASMLiteSmokeProtocol.LoadSessionFixture("session.protocol-mismatch.json");

            var compatibility = ASMLiteSmokeProtocol.EvaluateCompatibility(
                ASMLiteSmokeProtocol.SupportedProtocolVersion,
                mismatchSession,
                catalog.protocolVersion);

            Assert.IsFalse(compatibility.isCompatible);
            StringAssert.Contains("Protocol version mismatch", compatibility.message);
            StringAssert.Contains("Update overlay and host", compatibility.message);

            var protocolErrorState = ASMLiteSmokeProtocol.LoadHostStateFixture("host-state.protocol-error.json");
            Assert.AreEqual(ASMLiteSmokeProtocol.HostStateProtocolError, protocolErrorState.state);

            var protocolErrorEvents = ASMLiteSmokeProtocol.LoadEventFixture("events.protocol-error.ndjson");
            Assert.That(protocolErrorEvents.Any(item => item.eventType == ASMLiteSmokeProtocol.HostStateProtocolError), Is.True);
            Assert.That(protocolErrorEvents.Any(item => item.eventType == "command-rejected"), Is.True);
            Assert.That(protocolErrorEvents.Any(item => item.message.IndexOf("run-suite", System.StringComparison.Ordinal) >= 0), Is.True);
        }

        [Test]
        public void PostMismatch_run_suite_command_is_rejected_while_host_state_is_protocol_error()
        {
            var hostState = ASMLiteSmokeProtocol.LoadHostStateFixture("host-state.protocol-error.json");
            var runSuiteCommand = ASMLiteSmokeProtocol.LoadCommandFixture("run-suite.json");

            Assert.AreEqual("run-suite", runSuiteCommand.commandType);

            bool accepted = ASMLiteSmokeProtocol.CanAcceptRunSuite(hostState, out string reason);
            Assert.IsFalse(accepted);
            StringAssert.Contains("protocol", reason.ToLowerInvariant());
        }
    }
}
