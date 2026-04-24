using System;
using System.IO;
using System.Linq;
using System.Text;
using ASMLite.Editor;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [Serializable]
    internal sealed class ASMLiteGenerationWiringFailure
    {
        public string type = string.Empty;
        public string suiteName = string.Empty;
        public string testName = string.Empty;
        public string code = string.Empty;
        public string contextPath = string.Empty;
        public string message = string.Empty;
        public string remediation = string.Empty;
        public string resultsFile = string.Empty;
        public string capturedAtUtc = string.Empty;
        public string innerCode = string.Empty;
        public string innerContextPath = string.Empty;
        public string innerMessage = string.Empty;
        public string innerRemediation = string.Empty;
    }

    [Serializable]
    internal sealed class ASMLiteGenerationWiringSummaryDocument
    {
        public string generatedAtUtc = string.Empty;
        public int failureCount;
        public ASMLiteGenerationWiringFailure[] failures = Array.Empty<ASMLiteGenerationWiringFailure>();
    }

    internal static class ASMLiteGenerationWiringSummaryWriter
    {
        internal const string ArtifactFileName = "asmlite-generation-wiring-summary.json";

        internal static ASMLiteGenerationWiringFailure CreateDiagnosticFailure(
            string suiteName,
            string testName,
            ASMLiteBuildDiagnosticResult diagnostic,
            string resultsFile = null)
        {
            if (diagnostic == null || diagnostic.Success)
                return null;

            var failure = new ASMLiteGenerationWiringFailure
            {
                type = "diagnostic",
                suiteName = suiteName ?? string.Empty,
                testName = testName ?? string.Empty,
                code = diagnostic.Code ?? string.Empty,
                contextPath = diagnostic.ContextPath ?? string.Empty,
                message = diagnostic.Message ?? string.Empty,
                remediation = diagnostic.Remediation ?? string.Empty,
                resultsFile = resultsFile ?? string.Empty,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
            };

            if (diagnostic.InnerDiagnostic != null && !diagnostic.InnerDiagnostic.Success)
            {
                failure.innerCode = diagnostic.InnerDiagnostic.Code ?? string.Empty;
                failure.innerContextPath = diagnostic.InnerDiagnostic.ContextPath ?? string.Empty;
                failure.innerMessage = diagnostic.InnerDiagnostic.Message ?? string.Empty;
                failure.innerRemediation = diagnostic.InnerDiagnostic.Remediation ?? string.Empty;
            }

            return failure;
        }

        internal static ASMLiteGenerationWiringFailure CreateDeterminismFailure(
            string suiteName,
            string testName,
            string contextPath,
            string message,
            string resultsFile = null)
        {
            return new ASMLiteGenerationWiringFailure
            {
                type = "determinism",
                suiteName = suiteName ?? string.Empty,
                testName = testName ?? string.Empty,
                code = "DETERMINISM",
                contextPath = contextPath ?? string.Empty,
                message = message ?? string.Empty,
                remediation = string.Empty,
                resultsFile = resultsFile ?? string.Empty,
                capturedAtUtc = DateTime.UtcNow.ToString("o"),
            };
        }

        internal static void Write(string resultsDirectory, ASMLiteGenerationWiringFailure[] failures)
        {
            string outputDirectory = string.IsNullOrWhiteSpace(resultsDirectory)
                ? "."
                : resultsDirectory;

            Directory.CreateDirectory(outputDirectory);

            var normalizedFailures = (failures ?? Array.Empty<ASMLiteGenerationWiringFailure>())
                .Where(failure => failure != null)
                .ToArray();

            var payload = new ASMLiteGenerationWiringSummaryDocument
            {
                generatedAtUtc = DateTime.UtcNow.ToString("o"),
                failureCount = normalizedFailures.Length,
                failures = normalizedFailures,
            };

            string outputPath = Path.Combine(outputDirectory, ArtifactFileName);
            string json = JsonUtility.ToJson(payload, prettyPrint: true);
            File.WriteAllText(outputPath, json + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
