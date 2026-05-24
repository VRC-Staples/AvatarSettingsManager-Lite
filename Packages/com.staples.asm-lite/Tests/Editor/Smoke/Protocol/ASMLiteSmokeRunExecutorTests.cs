using System;
using System.Linq;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Smoke")]
    [Category("Headless")]
    public class ASMLiteSmokeRunExecutorTests
    {
        [Test]
        public void Execute_ExpectedFailurePassesWhenDiagnosticCodeAndTextMatch()
        {
            var catalog = BuildSingleStepCatalog(expectStepFailure: true);
            var result = ASMLiteSmokeRunExecutor.Execute(
                catalog,
                BuildRunSuiteCommand(),
                "SceneReload",
                FailingStep("SETUP_SCENE_MISSING: configured scene could not be found at Assets/Missing.unity"));

            Assert.IsTrue(result.Succeeded);
            Assert.IsNull(result.Failure);
            CollectionAssert.Contains(result.Events.Select(item => item.EventType).ToArray(), "step-passed");
            CollectionAssert.Contains(result.Events.Select(item => item.EventType).ToArray(), "suite-passed");
        }

        [Test]
        public void Execute_ExpectedFailureFailsWhenStepSucceedsUnexpectedly()
        {
            var catalog = BuildSingleStepCatalog(expectStepFailure: true);
            var result = ASMLiteSmokeRunExecutor.Execute(
                catalog,
                BuildRunSuiteCommand(),
                "SceneReload",
                PassingStep("host ready"));

            Assert.IsFalse(result.Succeeded);
            Assert.NotNull(result.Failure);
            StringAssert.Contains("expected to fail", result.Failure.FailureMessage);
            StringAssert.Contains("SETUP_SCENE_MISSING", result.Failure.FailureMessage);
        }

        [Test]
        public void Execute_ExpectedFailureFailsWhenDiagnosticCodeOrTextIsWrong()
        {
            var catalog = BuildSingleStepCatalog(expectStepFailure: true);
            var result = ASMLiteSmokeRunExecutor.Execute(
                catalog,
                BuildRunSuiteCommand(),
                "SceneReload",
                FailingStep("SETUP_SCENE_PATH_INVALID: configured scene could not be found at Assets/Missing.unity"));

            Assert.IsFalse(result.Succeeded);
            Assert.NotNull(result.Failure);
            StringAssert.Contains("Expected diagnostic", result.Failure.FailureMessage);
            StringAssert.Contains("SETUP_SCENE_MISSING", result.Failure.FailureMessage);
            StringAssert.Contains("SETUP_SCENE_PATH_INVALID", result.Failure.FailureMessage);
        }

        [Test]
        public void Execute_RegularStepFailureStillFailsWithoutExpectedDiagnosticArgs()
        {
            var catalog = BuildSingleStepCatalog(expectStepFailure: false);
            var result = ASMLiteSmokeRunExecutor.Execute(
                catalog,
                BuildRunSuiteCommand(),
                "SceneReload",
                FailingStep("ordinary failure"));

            Assert.IsFalse(result.Succeeded);
            Assert.NotNull(result.Failure);
            Assert.AreEqual("ordinary failure", result.Failure.FailureMessage);
        }

        private static ASMLiteSmokeRunExecutor.StepExecutor PassingStep(string message)
        {
            return (ASMLiteSmokeStepDefinition step, out string detail, out string stackTrace) =>
            {
                detail = message;
                stackTrace = string.Empty;
                return true;
            };
        }

        private static ASMLiteSmokeRunExecutor.StepExecutor FailingStep(string message)
        {
            return (ASMLiteSmokeStepDefinition step, out string detail, out string stackTrace) =>
            {
                detail = message;
                stackTrace = "stack";
                return false;
            };
        }

        private static ASMLiteSmokeCatalogDocument BuildSingleStepCatalog(bool expectStepFailure)
        {
            var catalog = new ASMLiteSmokeCatalogDocument
            {
                catalogVersion = 1,
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                fixture = new ASMLiteSmokeFixtureDefinition
                {
                    scenePath = "Assets/Click ME.unity",
                    avatarName = "CanonicalDress",
                },
                groups = new[]
                {
                    new ASMLiteSmokeGroupDefinition
                    {
                        groupId = "setup",
                        label = "Setup",
                        description = "Setup suites.",
                        suites = new[]
                        {
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "negative-diagnostics",
                                label = "Negative Diagnostics",
                                description = "Expected diagnostic cases.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                defaultSelected = false,
                                presetGroups = new[] { "safe-negatives" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Expected diagnostic matches.",
                                debugHint = "Inspect diagnostic output.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "missing-scene",
                                        label = "Missing scene",
                                        description = "Missing scene diagnostic.",
                                        expectedOutcome = "Expected diagnostic matches.",
                                        debugHint = "Inspect scene path.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-missing-scene",
                                                label = "Missing scene is reported",
                                                description = "Assert missing scene diagnostic.",
                                                actionType = "assert-host-ready",
                                                expectedOutcome = "Expected diagnostic matches.",
                                                debugHint = "Inspect host readiness.",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    expectStepFailure = expectStepFailure,
                                                    expectedDiagnosticCode = expectStepFailure ? "SETUP_SCENE_MISSING" : string.Empty,
                                                    expectedDiagnosticContains = expectStepFailure ? "scene could not be found" : string.Empty,
                                                },
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };
            catalog.RebuildLookups();
            return catalog;
        }

        private static ASMLiteSmokeProtocolCommand BuildRunSuiteCommand()
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "session",
                commandId = "cmd_000001_run-suite",
                commandSeq = 1,
                commandType = "run-suite",
                createdAtUtc = "2026-04-23T00:00:00Z",
                runSuite = new ASMLiteSmokeRunSuitePayload
                {
                    suiteId = "negative-diagnostics",
                    requestedBy = "operator",
                    requestedResetDefault = "SceneReload",
                    reason = "test",
                    stepSleepSeconds = 0d,
                },
            };
        }
    }
}
