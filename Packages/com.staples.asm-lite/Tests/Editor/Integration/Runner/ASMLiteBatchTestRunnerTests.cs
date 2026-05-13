using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine.SceneManagement;
using NUnit.Framework;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    internal sealed class ASMLiteBatchTestRunnerTests
    {
        [Test]
        public void NormalizeRun_UsesExplicitFilters_AndAppendsLegacySelectors()
        {
            var run = new AsmLiteBatchRunDefinition
            {
                name = "  mixed-run  ",
                resultFile = "  custom-results.xml  ",
                filters = new[]
                {
                    new AsmLiteBatchFilterDefinition
                    {
                        groupNames = new[] { "  ^ASMLite\\.Tests\\.Editor\\.ASMLiteBuilderTests(?:\\.|$)  " }
                    }
                },
                testNames = new[] { "  ASMLite.Tests.Editor.ASMLitePrefabWiringTests.PrefabWiring_UsesGeneratedAssetReferences_ForFullController  " },
            };

            var normalized = ASMLiteBatchTestRunner.NormalizeRun(run, 0);

            Assert.AreEqual("mixed-run", normalized.name);
            Assert.AreEqual("custom-results.xml", normalized.resultFile);
            Assert.AreEqual(2, normalized.filters.Length,
                "Legacy selector fields should be appended as an extra OR filter when explicit filters are present.");
            Assert.AreEqual("^ASMLite\\.Tests\\.Editor\\.ASMLiteBuilderTests(?:\\.|$)", normalized.filters[0].groupNames[0]);
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLitePrefabWiringTests.PrefabWiring_UsesGeneratedAssetReferences_ForFullController",
                normalized.filters[1].testNames[0]);
        }

        [Test]
        public void NormalizeRun_WhenNoSelectorsProvided_LeavesFiltersEmpty_AndReportsNoSelection()
        {
            var run = ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition(), 1);

            Assert.IsEmpty(run.filters,
                "Runs without explicit selectors should stay empty after normalization so accidental full-suite execution is rejected by default.");
            Assert.IsFalse(ASMLiteBatchTestRunner.RunHasAnySelection(run),
                "Runs without selectors should report no configured selection after normalization.");
        }

        [Test]
        public void BuildExecutionFilters_WhenNoSelectorsProvided_ReturnsSingleDefaultEditModeFilter()
        {
            var run = ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
            {
                allowEmptySelection = true,
            }, 1);

            var filters = ASMLiteBatchTestRunner.BuildExecutionFilters(run);

            Assert.AreEqual(1, filters.Length);
            Assert.AreEqual(TestMode.EditMode, filters[0].testMode);
            Assert.IsEmpty(filters[0].testNames);
            Assert.IsEmpty(filters[0].groupNames);
            Assert.IsEmpty(filters[0].categoryNames);
            Assert.IsEmpty(filters[0].assemblyNames);
        }

        [Test]
        public void CanonicalSuiteMap_ExcludesBatchRunnerSelfTests()
        {
            string suiteMapPath = ASMLiteSmokeContractPaths.GetSuiteMapPath();
            string json = File.ReadAllText(suiteMapPath, Encoding.UTF8);
            var configuration = ASMLiteBatchTestRunner.BuildDefaultBatchRunConfigurationFromSuiteMapJson(json);

            Assert.AreEqual(5, configuration.runs.Length);
            StringAssert.DoesNotContain("ASMLiteBatchTestRunnerTests", JsonUtility.ToJson(configuration),
                "The canonical single-instance batch runs must not run the batch runner's own static-state tests inside the active batch runner callback session.");
        }

        public void WrapResultDocumentWithResultMetrics_WrapsSuiteXml_WithAdaptorCounts()
        {
            var rawDocument = new XmlDocument();
            rawDocument.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" result=""Failed"" testcasecount=""287"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" />");

            DateTime startTime = new DateTime(2026, 4, 19, 17, 8, 14, DateTimeKind.Utc);
            DateTime endTime = startTime.AddSeconds(1.25d);
            XmlDocument wrapped = ASMLiteBatchTestRunner.WrapResultDocumentWithResultMetrics(
                rawDocument,
                new FakeTestResultAdaptor(
                    resultState: "Failed",
                    testStatus: TestStatus.Failed,
                    passCount: 2,
                    failCount: 1,
                    skipCount: 0,
                    inconclusiveCount: 0,
                    assertCount: 4,
                    duration: 1.25d,
                    startTime: startTime,
                    endTime: endTime));

            XmlElement root = wrapped.DocumentElement;
            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("3", root.GetAttribute("testcasecount"));
            Assert.AreEqual("3", root.GetAttribute("total"));
            Assert.AreEqual("2", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("4", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-19 17:08:14Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-19 17:08:15Z", root.GetAttribute("end-time"));
            Assert.AreEqual(1,
                root.ChildNodes.Cast<XmlNode>().Count(node => node.NodeType == XmlNodeType.Element && string.Equals(node.Name, "test-suite", StringComparison.Ordinal)));
            Assert.AreEqual("ASMLite.Tests.Editor.dll", ((XmlElement)root.SelectSingleNode("test-suite"))?.GetAttribute("name"));
        }

        [Test]
        public void WrapResultDocumentWithResultMetrics_UsesChildResultMetrics_WhenTopLevelAdaptorReportsZeroCounts()
        {
            var rawDocument = new XmlDocument();
            rawDocument.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""TestSuite"" id=""1355"" name=""Saryu"" fullname=""Saryu"" runstate=""Runnable"" testcasecount=""288"" result=""Passed"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
  <test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" result=""Passed"" testcasecount=""8"" total=""8"" passed=""8"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""2"" />
</test-suite>");

            DateTime startTime = new DateTime(2026, 4, 19, 17, 17, 0, DateTimeKind.Utc);
            DateTime endTime = startTime.AddSeconds(0.75d);
            var child = new FakeTestResultAdaptor(
                resultState: "Passed",
                testStatus: TestStatus.Passed,
                passCount: 8,
                failCount: 0,
                skipCount: 0,
                inconclusiveCount: 0,
                assertCount: 2,
                duration: 0.75d,
                startTime: startTime,
                endTime: endTime);
            XmlDocument wrapped = ASMLiteBatchTestRunner.WrapResultDocumentWithResultMetrics(
                rawDocument,
                new FakeTestResultAdaptor(
                    resultState: "Passed",
                    testStatus: TestStatus.Passed,
                    passCount: 0,
                    failCount: 0,
                    skipCount: 0,
                    inconclusiveCount: 0,
                    assertCount: 0,
                    duration: 0.75d,
                    startTime: startTime,
                    endTime: endTime,
                    children: new[] { child }));

            XmlElement root = wrapped.DocumentElement;
            Assert.IsNotNull(root);
            Assert.AreEqual("8", root.GetAttribute("testcasecount"));
            Assert.AreEqual("8", root.GetAttribute("total"));
            Assert.AreEqual("8", root.GetAttribute("passed"));
            Assert.AreEqual("0", root.GetAttribute("failed"));
            Assert.AreEqual("2", root.GetAttribute("asserts"));
            Assert.AreEqual("Passed", root.GetAttribute("result"));
        }

        [Test]
        public void ConvertRawXmlObjectToDocument_UsesOuterXmlProperty_WhenToStringIsNotXml()
        {
            XmlDocument converted = ASMLiteBatchTestRunner.ConvertRawXmlObjectToDocument(
                new OuterXmlOnlyResultNode("<test-run id=\"batch\" result=\"Passed\" total=\"1\" passed=\"1\" failed=\"0\" />"),
                "FakeBatchResult");

            Assert.AreEqual("test-run", converted.DocumentElement?.Name);
            Assert.AreEqual("batch", converted.DocumentElement?.GetAttribute("id"));
            Assert.AreEqual("Passed", converted.DocumentElement?.GetAttribute("result"));
        }

        [Test]
        public void ConvertRawXmlObjectToDocument_UsesWriteTo_WhenAvailable()
        {
            XmlDocument converted = ASMLiteBatchTestRunner.ConvertRawXmlObjectToDocument(
                new WriteToOnlyResultNode("<test-run id=\"writer\" result=\"Passed\" total=\"2\" passed=\"2\" failed=\"0\" />"),
                "FakeBatchResult");

            Assert.AreEqual("test-run", converted.DocumentElement?.Name);
            Assert.AreEqual("writer", converted.DocumentElement?.GetAttribute("id"));
            Assert.AreEqual("2", converted.DocumentElement?.GetAttribute("total"));
        }

        [Test]
        public void BuildSessionStateFromCommandLine_UsesExplicitCliArguments_ForBatchTransport()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "asmlite-batch-cli-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                string planPath = Path.Combine(tempDirectory, "plan.json");
                string resultsDirectory = Path.Combine(tempDirectory, "results");
                string canonicalResultsPath = Path.Combine(tempDirectory, "canonical.xml");
                string overlayStatePath = Path.Combine(tempDirectory, "overlay", "state.payload");
                File.WriteAllText(planPath, @"{
  ""runs"": [
    {
      ""name"": ""batch-runner"",
      ""suiteId"": ""runner"",
      ""suiteLabel"": ""Batch Runner"",
      ""resultFile"": ""results/runner.xml"",
      ""filters"": [
        {
          ""testNames"": [""ASMLiteBatchTestRunnerTests""]
        }
      ]
    }
  ]
}", Encoding.UTF8);

                var sessionState = ASMLiteBatchTestRunner.BuildSessionStateFromCommandLine(new[]
                {
                    "Unity.exe",
                    "-executeMethod",
                    "ASMLite.Tests.Editor.ASMLiteBatchTestRunner.RunFromCommandLine",
                    "-asmliteBatchRunsJsonPath",
                    planPath,
                    "-asmliteBatchResultsDir",
                    resultsDirectory,
                    "-asmliteBatchCanonicalResultsPath",
                    canonicalResultsPath,
                    "-asmliteBatchOverlayStatePath",
                    overlayStatePath,
                    "-asmliteBatchStepDelaySeconds",
                    "2.5",
                    "-asmliteBatchSelectionLabel",
                    "Visible smoke suites",
                    "-asmliteBatchSessionId",
                    "visible-suite-session",
                    "-asmliteBatchOverlayTitle",
                    "ASM-Lite smoke suite overlay",
                });

                Assert.AreEqual(1, sessionState.runs.Length);
                Assert.AreEqual("batch-runner", sessionState.runs[0].name);
                Assert.AreEqual("runner", sessionState.runs[0].suiteId);
                Assert.AreEqual("Batch Runner", sessionState.runs[0].suiteLabel);
                Assert.AreEqual("results/runner.xml", sessionState.runs[0].resultFile);
                Assert.AreEqual("ASMLiteBatchTestRunnerTests", sessionState.runs[0].filters[0].testNames[0]);
                Assert.AreEqual(Path.GetFullPath(resultsDirectory), sessionState.resultsDirectory);
                Assert.AreEqual(Path.GetFullPath(canonicalResultsPath), sessionState.canonicalResultsPath);
                Assert.AreEqual(Path.GetFullPath(overlayStatePath), sessionState.overlayStatePath);
                Assert.AreEqual("Visible smoke suites", sessionState.selectionLabel);
                Assert.AreEqual("visible-suite-session", sessionState.overlaySessionId);
                Assert.AreEqual("ASM-Lite smoke suite overlay", sessionState.overlayTitle);
                Assert.AreEqual(2.5f, sessionState.stepDelaySeconds, 0.0001f);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
        }

        [Test]
        public void BuildCanonicalResultsDocument_UsesNestedAssemblyMetrics_WhenUnityWrappersReportZeroTotals()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""1"" testcasecount=""285"" result=""Passed"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" start-time=""2026-04-19 16:07:00Z"" end-time=""2026-04-19 16:07:03Z"" duration=""0.01"">
  <test-suite type=""TestSuite"" id=""1352"" name=""Saryu"" fullname=""Saryu"" result=""Passed"" testcasecount=""285"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
    <test-suite type=""Assembly"" id=""1666"" name=""ASMLite.Tests.Editor.dll"" result=""Passed"" testcasecount=""8"" total=""8"" passed=""8"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" />
  </test-suite>
</test-run>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""2"" testcasecount=""285"" result=""Failed"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" start-time=""2026-04-19 16:07:04Z"" end-time=""2026-04-19 16:07:08Z"" duration=""0.02"">
  <test-suite type=""TestSuite"" id=""2056"" name=""Saryu"" fullname=""Saryu"" result=""Failed"" testcasecount=""285"" total=""0"" passed=""0"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
    <test-suite type=""Assembly"" id=""2666"" name=""ASMLite.Build.Tests.Editor.dll"" result=""Failed"" testcasecount=""3"" total=""3"" passed=""2"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""4"" />
  </test-suite>
</test-run>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("11", root.GetAttribute("testcasecount"));
            Assert.AreEqual("11", root.GetAttribute("total"));
            Assert.AreEqual("10", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("5", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual(2, root.SelectNodes("test-suite")?.Count ?? 0,
                "Canonical aggregation should preserve each imported wrapper suite while still using the nested assembly metrics for totals.");
        }

        [Test]
        public void BuildCanonicalResultsDocument_MergesCompletedRuns_IntoCanonicalNUnitOutput()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""1"" testcasecount=""2"" result=""Passed"" total=""2"" passed=""2"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" engine-version=""3.5.0.0"" clr-version=""4.0.30319.42000"" start-time=""2026-04-17 01:00:00Z"" end-time=""2026-04-17 01:00:10Z"" duration=""10.5"">
  <test-suite type=""TestSuite"" id=""11"" name=""core"" result=""Passed"" total=""2"" passed=""2"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" />
</test-run>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""2"" testcasecount=""1"" result=""Failed"" total=""1"" passed=""0"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"" engine-version=""3.5.0.0"" clr-version=""4.0.30319.42000"" start-time=""2026-04-17 01:00:05Z"" end-time=""2026-04-17 01:00:20Z"" duration=""2.25"">
  <test-suite type=""TestSuite"" id=""12"" name=""integration"" result=""Failed"" total=""1"" passed=""0"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"" />
</test-run>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("3", root.GetAttribute("testcasecount"));
            Assert.AreEqual("3", root.GetAttribute("total"));
            Assert.AreEqual("2", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("3", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-17 01:00:00Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-17 01:00:20Z", root.GetAttribute("end-time"));
            Assert.AreEqual(2, root.SelectNodes("test-suite")?.Count ?? 0,
                "Canonical NUnit output should preserve each completed run as a top-level imported suite.");
        }

        [Test]
        public void BuildCanonicalResultsDocument_AcceptsTestSuiteRootDocuments_FromUnityBatchCallbacks()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" fullname=""C:/Temp/ASMLite.Tests.Editor.dll"" runstate=""Runnable"" testcasecount=""6"" result=""Passed"" start-time=""2026-04-19 15:35:43Z"" end-time=""2026-04-19 15:35:44Z"" duration=""0.080252"" total=""6"" passed=""6"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"">
  <properties>
    <property name=""platform"" value=""EditMode"" />
  </properties>
</test-suite>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1663"" name=""ASMLite.Build.Tests.Editor.dll"" fullname=""C:/Temp/ASMLite.Build.Tests.Editor.dll"" runstate=""Runnable"" testcasecount=""7"" result=""Failed"" start-time=""2026-04-19 15:36:10Z"" end-time=""2026-04-19 15:36:12Z"" duration=""0.125"" total=""7"" passed=""6"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"">
  <failure>
    <message><![CDATA[One test failed.]]></message>
  </failure>
</test-suite>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("13", root.GetAttribute("testcasecount"));
            Assert.AreEqual("13", root.GetAttribute("total"));
            Assert.AreEqual("12", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("2", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-19 15:35:43Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-19 15:36:12Z", root.GetAttribute("end-time"));
            Assert.AreEqual(2,
                root.ChildNodes.Cast<XmlNode>().Count(node => node.NodeType == XmlNodeType.Element && string.Equals(node.Name, "test-suite", StringComparison.Ordinal)),
                "Canonical NUnit output should preserve each test-suite root as a top-level imported suite when Unity returns suite-root result fragments.");
            Assert.AreEqual("ASMLite.Tests.Editor.dll", root.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().FirstOrDefault()?.GetAttribute("name"));
            Assert.AreEqual("ASMLite.Build.Tests.Editor.dll", root.ChildNodes.Cast<XmlNode>().OfType<XmlElement>().Skip(1).FirstOrDefault()?.GetAttribute("name"));
        }

        [Test]
        public void RestoreFromSessionState_RequeuesActiveRun_AndLoadsCompletedResultsFromDisk()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "asmlite-batch-resume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);

            try
            {
                var runs = new[]
                {
                    ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
                    {
                        name = "run-1",
                        suiteId = "suite-a",
                        suiteLabel = "Suite A",
                        resultFile = "run-1.xml",
                        allowEmptySelection = true,
                    }, 0),
                    ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
                    {
                        name = "run-2",
                        suiteId = "suite-b",
                        suiteLabel = "Suite B",
                        resultFile = "run-2.xml",
                        allowEmptySelection = true,
                    }, 1),
                };

                var completedDocument = new XmlDocument();
                completedDocument.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-suite type=""Assembly"" id=""1662"" name=""ASMLite.Tests.Editor.dll"" result=""Passed"" testcasecount=""1"" total=""1"" passed=""1"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""0"" start-time=""2026-04-19 15:35:43Z"" end-time=""2026-04-19 15:35:44Z"" duration=""0.08"" />");
                completedDocument.Save(Path.Combine(tempDirectory, "run-1.xml"));

                var sessionState = new AsmLiteBatchRunnerSessionState
                {
                    runs = runs,
                    resultsDirectory = tempDirectory,
                    canonicalResultsPath = Path.Combine(tempDirectory, "editmode-results.xml"),
                    selectionLabel = "UAT checklist smoke suites",
                    overlaySessionId = "resume-session",
                    overlayTitle = "ASM-Lite UAT smoke suites",
                    stepDelaySeconds = 1.5f,
                    nextRunIndex = 1,
                    expectedRunCount = 2,
                    exitCode = 0,
                    activeRunIndex = 1,
                };

                InvokeBatchRunnerPrivateMethod("ResetStaticState");
                InvokeBatchRunnerPrivateMethod("RestoreFromSessionState", sessionState);

                var pendingRuns = (Queue<AsmLiteBatchRunDefinition>)GetBatchRunnerPrivateField("s_pendingRuns");
                int completedRunCount = (int)GetBatchRunnerPrivateField("s_completedRunCount");
                int expectedRunCount = (int)GetBatchRunnerPrivateField("s_expectedRunCount");
                int nextRunIndex = (int)GetBatchRunnerPrivateField("s_nextRunIndex");
                int activeRunIndex = (int)GetBatchRunnerPrivateField("s_activeRunIndex");
                int completedResultsCount = ((System.Collections.ICollection)GetBatchRunnerPrivateField("s_completedRunResults")).Count;

                Assert.AreEqual(1, completedRunCount);
                Assert.AreEqual(2, expectedRunCount);
                Assert.AreEqual(1, nextRunIndex,
                    "Resuming after a reload should preserve the next run index so the current suite restarts instead of starting over.");
                Assert.AreEqual(-1, activeRunIndex,
                    "Reload restore should leave no active run until the next EditorApplication tick starts it again.");
                Assert.AreEqual(1, completedResultsCount,
                    "Completed result XML should be reloaded from disk so the canonical merge still has the earlier runs available after reload.");
                Assert.AreEqual(1, pendingRuns.Count);
                Assert.AreEqual("run-2", pendingRuns.Peek().name,
                    "The in-flight run should be requeued first after a visible editor domain reload.");
            }
            finally
            {
                InvokeBatchRunnerPrivateMethod("ResetStaticState");
                SessionState.EraseString("ASMLite.BatchTestRunner.SessionState");
                if (Directory.Exists(tempDirectory))
                    Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private static object GetBatchRunnerPrivateField(string fieldName)
        {
            return typeof(ASMLiteBatchTestRunner)
                .GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
                ?.GetValue(null);
        }

        private static void InvokeBatchRunnerPrivateMethod(string methodName, params object[] arguments)
        {
            typeof(ASMLiteBatchTestRunner)
                .GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic)
                ?.Invoke(null, arguments);
        }

        private sealed class FakeTestResultAdaptor : ITestResultAdaptor
        {
            public FakeTestResultAdaptor(
                string resultState,
                TestStatus testStatus,
                int passCount,
                int failCount,
                int skipCount,
                int inconclusiveCount,
                int assertCount,
                double duration,
                DateTime startTime,
                DateTime endTime,
                IEnumerable<ITestResultAdaptor> children = null)
            {
                ResultState = resultState;
                TestStatus = testStatus;
                PassCount = passCount;
                FailCount = failCount;
                SkipCount = skipCount;
                InconclusiveCount = inconclusiveCount;
                AssertCount = assertCount;
                Duration = duration;
                StartTime = startTime;
                EndTime = endTime;
                Children = children?.ToArray() ?? Array.Empty<ITestResultAdaptor>();
            }

            public ITestAdaptor Test => null;
            public string Name => "FakeResult";
            public string FullName => "ASMLite.Tests.Editor.FakeResult";
            public string ResultState { get; }
            public TestStatus TestStatus { get; }
            public double Duration { get; }
            public DateTime StartTime { get; }
            public DateTime EndTime { get; }
            public string Message => string.Empty;
            public string StackTrace => string.Empty;
            public int AssertCount { get; }
            public int FailCount { get; }
            public int PassCount { get; }
            public int SkipCount { get; }
            public int InconclusiveCount { get; }
            public bool HasChildren => false;
            public IEnumerable<ITestResultAdaptor> Children { get; }
            public string Output => string.Empty;

            public NUnit.Framework.Interfaces.TNode ToXml()
            {
                throw new NotSupportedException("FakeTestResultAdaptor.ToXml should not be used in this regression test.");
            }
        }

        private sealed class OuterXmlOnlyResultNode
        {
            public OuterXmlOnlyResultNode(string outerXml)
            {
                OuterXml = outerXml;
            }

            public string OuterXml { get; }

            public override string ToString() => "OuterXmlOnlyResultNode";
        }

        private sealed class WriteToOnlyResultNode
        {
            private readonly XmlDocument _document;

            public WriteToOnlyResultNode(string outerXml)
            {
                _document = new XmlDocument();
                _document.LoadXml(outerXml);
            }

            public void WriteTo(XmlWriter writer)
            {
                _document.DocumentElement?.WriteTo(writer);
            }

            public override string ToString() => "WriteToOnlyResultNode";
        }
    }
}
