using System;
using System.IO;
using NUnit.Framework;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    public class ASMLiteGenerationWiringSummaryWriterTests
    {
        [Test]
        public void CreateDiagnosticFailure_MapsDiagnosticFieldsAndInnerDiagnostic()
        {
            var inner = ASMLiteBuildDiagnosticResult.Fail(
                code: "DRIFT-203",
                contextPath: "content.menus.Array.data[0].prefix",
                remediation: "Restore menu prefix path.",
                message: "Missing menu prefix path.");

            var diagnostic = ASMLiteBuildDiagnosticResult.Fail(
                code: "BUILD-302",
                contextPath: "content",
                remediation: "Repair FullController schema.",
                message: "FullController wiring failed.",
                innerDiagnostic: inner);

            var failure = ASMLiteGenerationWiringSummaryWriter.CreateDiagnosticFailure(
                suiteName: "SuiteA",
                testName: "CaseA",
                diagnostic: diagnostic,
                resultsFile: "artifacts/editmode-core-results.xml");

            Assert.IsNotNull(failure);
            Assert.AreEqual("diagnostic", failure.type);
            Assert.AreEqual("SuiteA", failure.suiteName);
            Assert.AreEqual("CaseA", failure.testName);
            Assert.AreEqual("BUILD-302", failure.code);
            Assert.AreEqual("content", failure.contextPath);
            Assert.AreEqual("FullController wiring failed.", failure.message);
            Assert.AreEqual("Repair FullController schema.", failure.remediation);
            Assert.AreEqual("DRIFT-203", failure.innerCode);
            Assert.AreEqual("content.menus.Array.data[0].prefix", failure.innerContextPath);
        }

        [Test]
        public void CreateDeterminismFailure_UsesDeterminismContractFields()
        {
            var failure = ASMLiteGenerationWiringSummaryWriter.CreateDeterminismFailure(
                suiteName: "SuiteB",
                testName: "CaseB",
                contextPath: "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_FX.controller",
                message: "Repeated build changed serialized layer order.",
                resultsFile: "artifacts/editmode-integration-results.xml");

            Assert.IsNotNull(failure);
            Assert.AreEqual("determinism", failure.type);
            Assert.AreEqual("DETERMINISM", failure.code);
            Assert.AreEqual("SuiteB", failure.suiteName);
            Assert.AreEqual("CaseB", failure.testName);
            Assert.AreEqual("Repeated build changed serialized layer order.", failure.message);
        }

        [Test]
        public void Write_EmitsCanonicalSummaryArtifact()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "asmlite-generation-wiring-summary-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                var failure = ASMLiteGenerationWiringSummaryWriter.CreateDeterminismFailure(
                    suiteName: "SuiteC",
                    testName: "CaseC",
                    contextPath: "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_Params.asset",
                    message: "Expected deterministic parameter ordering.");

                ASMLiteGenerationWiringSummaryWriter.Write(tempDir, new[] { failure });

                string outputPath = Path.Combine(tempDir, ASMLiteGenerationWiringSummaryWriter.ArtifactFileName);
                Assert.IsTrue(File.Exists(outputPath),
                    "Writer should emit artifacts/asmlite-generation-wiring-summary.json equivalent output.");

                string json = File.ReadAllText(outputPath);
                Assert.IsTrue(json.Contains("\"failureCount\": 1"),
                    "Summary payload should include the exact failureCount.");
                Assert.IsTrue(json.Contains("\"suiteName\": \"SuiteC\""),
                    "Summary payload should include serialized suite names.");
                Assert.IsTrue(json.Contains("\"testName\": \"CaseC\""),
                    "Summary payload should include serialized test names.");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
