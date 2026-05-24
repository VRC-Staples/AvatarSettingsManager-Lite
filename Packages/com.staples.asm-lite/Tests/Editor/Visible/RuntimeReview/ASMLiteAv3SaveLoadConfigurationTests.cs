using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public sealed class ASMLiteAv3SaveLoadConfigurationTests
    {
        private const string ExpectedRuntimeAssemblyName = "lyuma.av3emulator";
        private const string RealUatAvatarAssetPathEnvVar = "ASMLITE_AV3_UAT_AVATAR_ASSET_PATH";
        private const string RealUatAvatarNameEnvVar = "ASMLITE_AV3_UAT_AVATAR_NAME";
        private const string RealUatMaxSavedParametersEnvVar = "ASMLITE_AV3_UAT_MAX_SAVED_PARAMETERS";
        private const string RealUatMaxUnsavedParametersEnvVar = "ASMLITE_AV3_UAT_MAX_UNSAVED_PARAMETERS";
        private const string FuzzSeedEnvVar = "ASMLITE_AV3_SAVE_LOAD_FUZZ_SEED";
        private const string FuzzIterationsEnvVar = "ASMLITE_AV3_SAVE_LOAD_FUZZ_ITERATIONS";
        private const string RealUatAvatarAssetPathArg = "-asmliteAv3UatAvatarAssetPath";
        private const string RealUatAvatarNameArg = "-asmliteAv3UatAvatarName";
        private const string RealUatMaxSavedParametersArg = "-asmliteAv3UatMaxSavedParameters";
        private const string RealUatMaxUnsavedParametersArg = "-asmliteAv3UatMaxUnsavedParameters";
        private const string FuzzSeedArg = "-asmliteAv3SaveLoadFuzzSeed";
        private const string FuzzIterationsArg = "-asmliteAv3SaveLoadFuzzIterations";
        private const uint DefaultFuzzSeed = 0xA5A5F005u;
        private const int DefaultRealUatMaxSavedParameters = 6;
        private const int DefaultRealUatMaxUnsavedParameters = 6;

        [Test]
        public void RuntimeBridge_MissingRuntimeDiagnostic_NamesExpectedAv3AssemblyAndType()
        {
            var result = ResolveRuntimeType(
                "Lyuma.Av3Emulator.Runtime.DoesNotExistForASMLiteRuntime",
                ExpectedRuntimeAssemblyName);

            Assert.IsFalse(result.IsAvailable, "Runtime bridge: intentionally missing AV3 runtime type should not resolve.");
            StringAssert.Contains("Lyuma.Av3Emulator.Runtime.DoesNotExistForASMLiteRuntime", result.Diagnostic);
            StringAssert.Contains(ExpectedRuntimeAssemblyName, result.Diagnostic);
        }

        [Test]
        public void ExternalUatSelection_DefaultsToManualOptInDiagnostic()
        {
            var selection = ReadRealUatSelection(Array.Empty<string>(), _ => null);

            Assert.IsFalse(selection.IsConfigured, "External UAT: real avatar selection must stay manual/non-default.");
            StringAssert.Contains(RealUatAvatarAssetPathEnvVar, selection.Diagnostic);
            StringAssert.Contains(RealUatAvatarAssetPathArg, selection.Diagnostic);
        }

        [Test]
        public void FuzzReplayConfig_DefaultsToLocalOptInDisabledDiagnostic()
        {
            var config = FuzzReplayConfig.Read(Array.Empty<string>(), _ => null);

            Assert.IsFalse(config.IsEnabled, "Fuzz replay: scale coverage must stay local opt-in and disabled by default.");
            Assert.AreEqual(0, config.Iterations, "Fuzz replay: disabled default must not allocate any fuzz iterations.");
            StringAssert.Contains(FuzzSeedEnvVar, config.Diagnostic);
            StringAssert.Contains(FuzzIterationsEnvVar, config.Diagnostic);
            StringAssert.Contains(FuzzSeedArg, config.Diagnostic);
            StringAssert.Contains(FuzzIterationsArg, config.Diagnostic);
        }

        [Test]
        public void FuzzReplayConfig_ParsesReplayableSeedAndIterationsFromCommandLineBeforeEnvironment()
        {
            var config = FuzzReplayConfig.Read(
                new[]
                {
                    FuzzSeedArg, "0xA5A5F00D",
                    FuzzIterationsArg + "=7",
                },
                name =>
                {
                    if (name == FuzzSeedEnvVar)
                        return "0xDEADBEEF";
                    if (name == FuzzIterationsEnvVar)
                        return "3";
                    return null;
                });

            Assert.IsTrue(config.IsEnabled, "Fuzz replay: a valid seed plus positive iteration count should enable local fuzz coverage.");
            Assert.AreEqual(0xA5A5F00Du, config.Seed, "Fuzz replay: command-line seed should override environment seed for exact replay.");
            Assert.AreEqual(7, config.Iterations, "Fuzz replay: command-line iteration count should override environment iteration count for exact replay.");
            StringAssert.Contains("0xA5A5F00D", config.ToString());
            StringAssert.Contains("iterations=7", config.ToString());
        }

        private static RuntimeTypeResolution ResolveRuntimeType(string fullTypeName, string assemblyName)
        {
            var type = AppDomain.CurrentDomain
                .GetAssemblies()
                .Where(assembly => string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                .Select(assembly => assembly.GetType(fullTypeName, throwOnError: false))
                .FirstOrDefault(candidate => candidate != null);

            if (type != null)
                return RuntimeTypeResolution.Available(type);

            return RuntimeTypeResolution.Missing(
                $"AV3 runtime type '{fullTypeName}' was not found in assembly '{assemblyName}'. "
                + "Install/enable the AV3 emulator runtime package before running AV3 save/load runtime coverage.");
        }

        private static RealUatSelection ReadRealUatSelection(string[] commandLineArgs, Func<string, string> readEnvironmentVariable)
        {
            commandLineArgs = commandLineArgs ?? Array.Empty<string>();
            readEnvironmentVariable = readEnvironmentVariable ?? (_ => null);

            string rawAssetPath = FirstNonEmpty(
                ReadCommandLineValue(commandLineArgs, RealUatAvatarAssetPathArg),
                readEnvironmentVariable(RealUatAvatarAssetPathEnvVar));
            string avatarName = FirstNonEmpty(
                ReadCommandLineValue(commandLineArgs, RealUatAvatarNameArg),
                readEnvironmentVariable(RealUatAvatarNameEnvVar));

            int maxSavedParameters = ParsePositiveInt(
                FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, RealUatMaxSavedParametersArg),
                    readEnvironmentVariable(RealUatMaxSavedParametersEnvVar)),
                DefaultRealUatMaxSavedParameters);

            int maxUnsavedParameters = ParsePositiveInt(
                FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, RealUatMaxUnsavedParametersArg),
                    readEnvironmentVariable(RealUatMaxUnsavedParametersEnvVar)),
                DefaultRealUatMaxUnsavedParameters);

            if (string.IsNullOrWhiteSpace(rawAssetPath))
            {
                return RealUatSelection.Unconfigured(
                    "External UAT: real-avatar coverage is manual/non-default and did not run because no operator-selected avatar prefab was provided. "
                    + $"Set {RealUatAvatarAssetPathEnvVar}=Assets/Path/Avatar.prefab or pass {RealUatAvatarAssetPathArg} Assets/Path/Avatar.prefab. "
                    + $"Optional selector: {RealUatAvatarNameEnvVar} / {RealUatAvatarNameArg}. "
                    + "The test intentionally avoids broad project scans and never mutates the source prefab asset.");
            }

            string assetPath = NormalizeAssetPath(rawAssetPath);
            return RealUatSelection.Configured(assetPath, avatarName, maxSavedParameters, maxUnsavedParameters);
        }

        private static string NormalizeAssetPath(string rawAssetPath)
        {
            rawAssetPath = (rawAssetPath ?? string.Empty).Trim().Trim('\"');
            if (string.IsNullOrWhiteSpace(rawAssetPath))
                return string.Empty;

            string guidPath = AssetDatabase.GUIDToAssetPath(rawAssetPath);
            if (!string.IsNullOrWhiteSpace(guidPath))
                return guidPath;

            rawAssetPath = rawAssetPath.Replace('\\', '/');
            if (rawAssetPath.StartsWith("Assets/", StringComparison.Ordinal) || string.Equals(rawAssetPath, "Assets", StringComparison.Ordinal))
                return rawAssetPath;

            try
            {
                string fullPath = Path.GetFullPath(rawAssetPath).Replace('\\', '/');
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
                if (fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                    return fullPath.Substring(projectRoot.Length + 1);
            }
            catch
            {
                // Fall through to the raw value so the load assertion reports the exact input.
            }

            return rawAssetPath;
        }

        private static string ReadCommandLineValue(string[] args, string key)
        {
            if (args == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], key, StringComparison.Ordinal))
                    continue;

                if (i + 1 < args.Length)
                    return args[i + 1];
                return string.Empty;
            }

            string prefix = key + "=";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.Ordinal))
                    return args[i].Substring(prefix.Length);
            }

            return string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static int ParsePositiveInt(string text, int fallback)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
                return value;
            return fallback;
        }

        private static string FormatSeed(uint seed)
        {
            return "0x" + seed.ToString("X8", CultureInfo.InvariantCulture);
        }

        private readonly struct RuntimeTypeResolution
        {
            private RuntimeTypeResolution(Type type, string diagnostic)
            {
                Type = type;
                Diagnostic = diagnostic ?? string.Empty;
            }

            internal Type Type { get; }
            internal bool IsAvailable => Type != null;
            internal string Diagnostic { get; }

            internal static RuntimeTypeResolution Available(Type type)
            {
                return new RuntimeTypeResolution(type, string.Empty);
            }

            internal static RuntimeTypeResolution Missing(string diagnostic)
            {
                return new RuntimeTypeResolution(null, diagnostic);
            }
        }

        private readonly struct FuzzReplayConfig
        {
            private FuzzReplayConfig(bool isEnabled, uint seed, int iterations, string diagnostic)
            {
                IsEnabled = isEnabled;
                Seed = seed;
                Iterations = isEnabled && iterations > 0 ? iterations : 0;
                Diagnostic = diagnostic ?? string.Empty;
            }

            internal bool IsEnabled { get; }
            internal uint Seed { get; }
            internal int Iterations { get; }
            internal string Diagnostic { get; }

            internal static FuzzReplayConfig Read(string[] commandLineArgs, Func<string, string> readEnvironmentVariable)
            {
                commandLineArgs = commandLineArgs ?? Array.Empty<string>();
                readEnvironmentVariable = readEnvironmentVariable ?? (_ => null);

                string rawIterations = FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, FuzzIterationsArg),
                    readEnvironmentVariable(FuzzIterationsEnvVar));
                if (!int.TryParse(rawIterations, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations <= 0)
                {
                    return Disabled(
                        "Fuzz replay: AV3 save/load scale coverage is local-only opt-in and disabled by default. "
                        + $"Set {FuzzIterationsEnvVar}=N (or pass {FuzzIterationsArg} N) to enable; "
                        + $"optionally set {FuzzSeedEnvVar}=0xSEED (or pass {FuzzSeedArg} 0xSEED). "
                        + $"Default replay seed when omitted is {FormatSeed(DefaultFuzzSeed)}.");
                }

                string rawSeed = FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, FuzzSeedArg),
                    readEnvironmentVariable(FuzzSeedEnvVar));
                uint seed = DefaultFuzzSeed;
                if (!string.IsNullOrWhiteSpace(rawSeed) && !TryParseSeed(rawSeed, out seed))
                {
                    return Disabled(
                        $"Fuzz replay: Invalid AV3 save/load fuzz seed '{rawSeed}'. "
                        + $"Use decimal uint or hex 0xSEED via {FuzzSeedEnvVar} / {FuzzSeedArg}. "
                        + $"Iterations requested={iterations.ToString(CultureInfo.InvariantCulture)}; fuzz did not run.");
                }

                return new FuzzReplayConfig(
                    true,
                    seed,
                    iterations,
                    $"Fuzz replay: AV3 save/load scale coverage enabled. seed={FormatSeed(seed)} iterations={iterations.ToString(CultureInfo.InvariantCulture)}.");
            }

            public override string ToString()
            {
                return IsEnabled
                    ? $"seed={FormatSeed(Seed)} iterations={Iterations.ToString(CultureInfo.InvariantCulture)}"
                    : Diagnostic;
            }

            private static FuzzReplayConfig Disabled(string diagnostic)
            {
                return new FuzzReplayConfig(false, DefaultFuzzSeed, 0, diagnostic);
            }

            private static bool TryParseSeed(string text, out uint seed)
            {
                seed = 0u;
                text = (text ?? string.Empty).Trim().Trim('\"');
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return uint.TryParse(
                        text.Substring(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out seed);
                }

                return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
            }
        }

        private readonly struct RealUatSelection
        {
            private RealUatSelection(bool isConfigured, string assetPath, string avatarName, int maxSavedParameters, int maxUnsavedParameters, string diagnostic)
            {
                IsConfigured = isConfigured;
                AssetPath = assetPath ?? string.Empty;
                AvatarName = avatarName ?? string.Empty;
                MaxSavedParameters = maxSavedParameters > 0 ? maxSavedParameters : DefaultRealUatMaxSavedParameters;
                MaxUnsavedParameters = maxUnsavedParameters > 0 ? maxUnsavedParameters : DefaultRealUatMaxUnsavedParameters;
                Diagnostic = diagnostic ?? string.Empty;
            }

            internal bool IsConfigured { get; }
            internal string AssetPath { get; }
            internal string AvatarName { get; }
            internal int MaxSavedParameters { get; }
            internal int MaxUnsavedParameters { get; }
            internal string Diagnostic { get; }

            internal static RealUatSelection Configured(string assetPath, string avatarName, int maxSavedParameters, int maxUnsavedParameters)
            {
                return new RealUatSelection(true, assetPath, avatarName, maxSavedParameters, maxUnsavedParameters, string.Empty);
            }

            internal static RealUatSelection Unconfigured(string diagnostic)
            {
                return new RealUatSelection(false, string.Empty, string.Empty, DefaultRealUatMaxSavedParameters, DefaultRealUatMaxUnsavedParameters, diagnostic);
            }
        }
    }
}
