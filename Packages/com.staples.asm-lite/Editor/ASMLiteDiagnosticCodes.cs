using System;
using System.Collections.Generic;

namespace ASMLite.Editor
{
    internal static class ASMLiteDiagnosticCodes
    {
        internal static class Drift
        {
            internal const string MissingRequiredPath = "DRIFT-201";
            internal const string MissingParameterFallbackGroup = "DRIFT-202";
            internal const string MissingMenuPrefixPath = "DRIFT-203";
        }

        internal static class Build
        {
            internal const string ValidationFailed = "BUILD-301";
            internal const string FullControllerWiringFailed = "BUILD-302";
            internal const string InstallPrefixSyncFailed = "BUILD-303";
        }

        private static readonly Dictionary<string, string> s_messages = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { Drift.MissingRequiredPath, "Critical VRCFury FullController path is missing." },
            { Drift.MissingParameterFallbackGroup, "All critical VRCFury parameter fallback paths are missing." },
            { Drift.MissingMenuPrefixPath, "Critical VRCFury FullController menu prefix path is missing." },
            { Build.ValidationFailed, "ASM-Lite build validation failed." },
            { Build.FullControllerWiringFailed, "ASM-Lite build failed while applying critical FullController wiring." },
            { Build.InstallPrefixSyncFailed, "ASM-Lite build failed while syncing critical install-prefix wiring." },
        };

        internal static string GetMessage(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "No diagnostic code provided.";

            return s_messages.TryGetValue(code, out var message)
                ? message
                : $"Unknown diagnostic code '{code}'.";
        }

        internal static bool IsDriftCode(string code)
        {
            return string.Equals(code, Drift.MissingRequiredPath, StringComparison.Ordinal)
                || string.Equals(code, Drift.MissingParameterFallbackGroup, StringComparison.Ordinal)
                || string.Equals(code, Drift.MissingMenuPrefixPath, StringComparison.Ordinal);
        }

        internal static bool IsBuildCode(string code)
        {
            return string.Equals(code, Build.ValidationFailed, StringComparison.Ordinal)
                || string.Equals(code, Build.FullControllerWiringFailed, StringComparison.Ordinal)
                || string.Equals(code, Build.InstallPrefixSyncFailed, StringComparison.Ordinal);
        }
    }
}
