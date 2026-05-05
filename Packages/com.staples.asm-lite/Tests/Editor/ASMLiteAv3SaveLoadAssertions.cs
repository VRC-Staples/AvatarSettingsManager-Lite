using System;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteAv3SaveLoadAssertions
    {
        internal static bool SnapshotMatches(object runtime, ASMLiteAv3ParameterSnapshot expectedSnapshot)
        {
            foreach (var pair in expectedSnapshot.Values)
            {
                if (!ASMLiteAv3RuntimeBridge.TryReadParameter(runtime, pair.Key, pair.Value.Type, out var actual, out _))
                    return false;

                if (!ValueMatches(runtime, pair.Value, actual))
                    return false;
            }

            return true;
        }

        internal static void AssertSnapshotMatches(
            object runtime,
            uint seed,
            string phase,
            ASMLiteAv3ParameterSnapshot expectedSnapshot)
        {
            var failures = new List<string>();
            foreach (var pair in expectedSnapshot.Values)
            {
                if (!ASMLiteAv3RuntimeBridge.TryReadParameter(runtime, pair.Key, pair.Value.Type, out var actual, out var diagnostic))
                {
                    failures.Add(FormatFailure(seed, phase, pair.Key, pair.Value, null, diagnostic));
                    continue;
                }

                if (!ValueMatches(runtime, pair.Value, actual))
                    failures.Add(FormatFailure(seed, phase, pair.Key, pair.Value, actual, diagnostic));
            }

            if (failures.Count > 0)
                Assert.Fail(string.Join("\n", failures));
        }

        private static bool ValueMatches(
            object runtime,
            ASMLiteAv3ParameterValue expected,
            ASMLiteAv3ParameterValue actual)
        {
            if (expected.Type != actual.Type)
                return false;

            switch (expected.Type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    return expected.BoolValue == actual.BoolValue;
                case ASMLiteAv3ParameterType.Int:
                    return expected.IntValue == actual.IntValue;
                case ASMLiteAv3ParameterType.Float:
                    return Math.Abs(expected.FloatValue - actual.FloatValue) <= ASMLiteAv3RuntimeBridge.FloatToleranceFor(runtime);
                default:
                    return false;
            }
        }

        private static string FormatFailure(
            uint seed,
            string phase,
            string parameterName,
            ASMLiteAv3ParameterValue expected,
            ASMLiteAv3ParameterValue? actual,
            string diagnostic)
        {
            string tolerance;
            string delta;
            string actualText;

            if (actual.HasValue)
            {
                actualText = actual.Value.ToDisplayString();
                if (expected.Type == ASMLiteAv3ParameterType.Float)
                {
                    float actualFloat = actual.Value.FloatValue;
                    float expectedFloat = expected.FloatValue;
                    tolerance = ASMLiteAv3RuntimeBridge.FloatToleranceFor(null).ToString("0.######", CultureInfo.InvariantCulture);
                    delta = Math.Abs(expectedFloat - actualFloat).ToString("0.######", CultureInfo.InvariantCulture);
                }
                else
                {
                    tolerance = "n/a";
                    delta = "n/a";
                }
            }
            else
            {
                actualText = "<read-failed>";
                tolerance = expected.Type == ASMLiteAv3ParameterType.Float
                    ? ASMLiteAv3RuntimeBridge.FloatToleranceFor(null).ToString("0.######", CultureInfo.InvariantCulture)
                    : "n/a";
                delta = "n/a";
            }

            return $"seed={FormatSeed(seed)} phase={phase} param={parameterName} type={expected.Type} expected={expected.ToDisplayString()} actual={actualText} tolerance={tolerance} delta={delta} diagnostic={diagnostic}";
        }

        private static string FormatSeed(uint seed)
        {
            return "0x" + seed.ToString("X8", CultureInfo.InvariantCulture);
        }
    }
}
