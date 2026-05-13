using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Tests.PlayMode
{
    public enum ASMLiteAv3ParameterType
    {
        Bool,
        Int,
        Float,
    }

    public readonly struct ASMLiteAv3ParameterDescriptor
    {
        internal ASMLiteAv3ParameterDescriptor(string name, ASMLiteAv3ParameterType type)
        {
            Name = name ?? string.Empty;
            Type = type;
        }

        internal string Name { get; }
        internal ASMLiteAv3ParameterType Type { get; }
    }

    internal readonly struct ASMLiteAv3ParameterValue
    {
        private ASMLiteAv3ParameterValue(ASMLiteAv3ParameterType type, bool boolValue, int intValue, float floatValue)
        {
            Type = type;
            BoolValue = boolValue;
            IntValue = intValue;
            FloatValue = floatValue;
        }

        internal ASMLiteAv3ParameterType Type { get; }
        internal bool BoolValue { get; }
        internal int IntValue { get; }
        internal float FloatValue { get; }

        internal static ASMLiteAv3ParameterValue Bool(bool value)
        {
            return new ASMLiteAv3ParameterValue(ASMLiteAv3ParameterType.Bool, value, value ? 1 : 0, value ? 1f : 0f);
        }

        internal static ASMLiteAv3ParameterValue Int(int value)
        {
            return new ASMLiteAv3ParameterValue(ASMLiteAv3ParameterType.Int, value != 0, value, value);
        }

        internal static ASMLiteAv3ParameterValue Float(float value)
        {
            value = ASMLiteAv3RuntimeBridge.ClampFloatOnly(value);
            return new ASMLiteAv3ParameterValue(ASMLiteAv3ParameterType.Float, Math.Abs(value) > float.Epsilon, (int)value, value);
        }

        internal string ToDisplayString()
        {
            switch (Type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    return BoolValue ? "true" : "false";
                case ASMLiteAv3ParameterType.Int:
                    return IntValue.ToString(CultureInfo.InvariantCulture);
                case ASMLiteAv3ParameterType.Float:
                    return FloatValue.ToString("0.######", CultureInfo.InvariantCulture);
                default:
                    return string.Empty;
            }
        }
    }

    internal sealed class ASMLiteAv3ParameterSnapshot
    {
        private readonly Dictionary<string, ASMLiteAv3ParameterValue> _values;

        internal ASMLiteAv3ParameterSnapshot(IEnumerable<KeyValuePair<string, ASMLiteAv3ParameterValue>> values)
        {
            _values = new Dictionary<string, ASMLiteAv3ParameterValue>(StringComparer.Ordinal);
            foreach (var pair in values ?? Enumerable.Empty<KeyValuePair<string, ASMLiteAv3ParameterValue>>())
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                    _values[pair.Key] = pair.Value;
            }
        }

        internal IReadOnlyDictionary<string, ASMLiteAv3ParameterValue> Values => _values;
        internal IEnumerable<string> Names => _values.Keys;

        internal bool TryGetValue(string name, out ASMLiteAv3ParameterValue value)
        {
            return _values.TryGetValue(name, out value);
        }
    }

    internal readonly struct ASMLiteAv3SaveLoadRunContext
    {
        internal ASMLiteAv3SaveLoadRunContext(uint seed)
            : this(seed, seed, -1, 1)
        {
        }

        internal ASMLiteAv3SaveLoadRunContext(uint baseSeed, uint iterationSeed, int iterationIndex, int iterationCount)
        {
            BaseSeed = baseSeed;
            IterationSeed = iterationSeed;
            IterationIndex = iterationIndex;
            IterationCount = iterationCount > 0 ? iterationCount : 1;
        }

        internal uint BaseSeed { get; }
        internal uint IterationSeed { get; }
        internal int IterationIndex { get; }
        internal int IterationCount { get; }
        internal uint ValueSeed => IterationSeed;
        internal bool HasIteration => IterationIndex >= 0;

        internal string ToDisplayString()
        {
            if (!HasIteration)
                return "seed=" + FormatSeed(IterationSeed);

            return "seed=" + FormatSeed(BaseSeed)
                + " iteration=" + (IterationIndex + 1).ToString(CultureInfo.InvariantCulture) + "/" + IterationCount.ToString(CultureInfo.InvariantCulture)
                + " iterationSeed=" + FormatSeed(IterationSeed);
        }

        public override string ToString()
        {
            return ToDisplayString();
        }

        internal static string FormatSeed(uint seed)
        {
            return "0x" + seed.ToString("X8", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class ASMLiteAv3SaveLoadHarness
    {
        private const float MinimumDirtyFloatSeparation = 0.25f;
        private const double RuntimeParameterTimeoutSeconds = 10.0d;
        private const double ActionTimeoutSeconds = 10.0d;

        private readonly ASMLiteAv3ParameterDescriptor[] _savedParameters;
        private readonly ASMLiteAv3ParameterDescriptor[] _unsavedParameters;

        internal ASMLiteAv3SaveLoadHarness(
            IEnumerable<ASMLiteAv3ParameterDescriptor> savedParameters,
            IEnumerable<ASMLiteAv3ParameterDescriptor> unsavedParameters)
        {
            _savedParameters = (savedParameters ?? Enumerable.Empty<ASMLiteAv3ParameterDescriptor>()).ToArray();
            _unsavedParameters = (unsavedParameters ?? Enumerable.Empty<ASMLiteAv3ParameterDescriptor>()).ToArray();
        }

        internal IEnumerator RunCoreInvariant(GameObject avatar, uint seed)
        {
            return RunCoreInvariant(avatar, new ASMLiteAv3SaveLoadRunContext(seed));
        }

        internal IEnumerator RunCoreInvariant(GameObject avatar, ASMLiteAv3SaveLoadRunContext context)
        {
            object runtime = null;
            yield return WaitForRuntimeAndParameters(avatar, context, resolved => runtime = resolved);
            Assert.IsNotNull(runtime, $"{context.ToDisplayString()} phase=runtime-parameters param=<runtime> type=n/a expected=<resolved> actual=<null> tolerance=n/a delta=n/a");

            var savedSnapshot = GenerateSnapshot(_savedParameters, context, "saved-values", null);
            ApplySnapshot(runtime, context, "apply-saved-before-save", savedSnapshot);
            ASMLiteAv3SaveLoadAssertions.AssertSnapshotMatches(runtime, context, "saved-visible-before-save", savedSnapshot);
            yield return null;

            TriggerControl(runtime, context, "save-trigger", 1);
            yield return PollUntilControlIdle(runtime, context, "save-settle");
            ASMLiteAv3SaveLoadAssertions.AssertSnapshotMatches(runtime, context, "saved-after-save", savedSnapshot);

            var dirtySavedSnapshot = GenerateSnapshot(_savedParameters, context, "dirty-saved-values", savedSnapshot);
            var dirtyUnsavedSnapshot = GenerateSnapshot(_unsavedParameters, context, "dirty-unsaved-values", null);
            ApplySnapshot(runtime, context, "apply-dirty-saved-before-load", dirtySavedSnapshot);
            ApplySnapshot(runtime, context, "apply-dirty-unsaved-before-load", dirtyUnsavedSnapshot);
            ASMLiteAv3SaveLoadAssertions.AssertSnapshotMatches(runtime, context, "dirty-saved-visible-before-load", dirtySavedSnapshot);
            ASMLiteAv3SaveLoadAssertions.AssertSnapshotMatches(runtime, context, "dirty-unsaved-visible-before-load", dirtyUnsavedSnapshot);
            yield return null;

            TriggerControl(runtime, context, "load-trigger", 2);
            yield return PollUntilLoadSettled(runtime, context, savedSnapshot, dirtyUnsavedSnapshot);

            ASMLiteAv3SaveLoadAssertions.AssertSnapshotMatches(runtime, context, "load-saved-restored", savedSnapshot);
            ASMLiteAv3SaveLoadAssertions.AssertSnapshotMatches(runtime, context, "load-unsaved-preserved", dirtyUnsavedSnapshot);
        }

        internal static ASMLiteAv3ParameterDescriptor Descriptor(string name, VRCExpressionParameters.ValueType type)
        {
            switch (type)
            {
                case VRCExpressionParameters.ValueType.Bool:
                    return new ASMLiteAv3ParameterDescriptor(name, ASMLiteAv3ParameterType.Bool);
                case VRCExpressionParameters.ValueType.Int:
                    return new ASMLiteAv3ParameterDescriptor(name, ASMLiteAv3ParameterType.Int);
                case VRCExpressionParameters.ValueType.Float:
                    return new ASMLiteAv3ParameterDescriptor(name, ASMLiteAv3ParameterType.Float);
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported VRC expression parameter type.");
            }
        }

        private IEnumerator WaitForRuntimeAndParameters(GameObject avatar, ASMLiteAv3SaveLoadRunContext context, Action<object> setRuntime)
        {
            string[] requiredNames = _savedParameters
                .Concat(_unsavedParameters)
                .Select(descriptor => descriptor.Name)
                .Concat(new[] { ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName })
                .ToArray();

            string lastDiagnostic = string.Empty;
            object runtime = null;
            double deadline = EditorApplication.timeSinceStartup + RuntimeParameterTimeoutSeconds;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (ASMLiteAv3RuntimeBridge.TryFindRuntime(avatar, out runtime, out lastDiagnostic)
                    && _savedParameters.All(descriptor => ASMLiteAv3RuntimeBridge.HasParameter(runtime, descriptor.Name, descriptor.Type))
                    && _unsavedParameters.All(descriptor => ASMLiteAv3RuntimeBridge.HasParameter(runtime, descriptor.Name, descriptor.Type))
                    && ASMLiteAv3RuntimeBridge.HasParameter(runtime, ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName, ASMLiteAv3ParameterType.Int))
                {
                    setRuntime(runtime);
                    yield break;
                }

                yield return null;
            }

            ASMLiteAv3RuntimeBridge.ParameterSnapshot visibleSnapshot = default;
            ASMLiteAv3RuntimeBridge.TryCaptureVisibleParameters(avatar, out visibleSnapshot, out _);
            var missingNames = requiredNames
                .Where(name => !visibleSnapshot.Contains(name))
                .OrderBy(name => name, StringComparer.Ordinal);

            Assert.Fail(
                $"{context.ToDisplayString()} phase=runtime-parameters param=<all> type=n/a expected=<visible> actual=<missing> tolerance=n/a delta=n/a. "
                + $"Missing=[{string.Join(", ", missingNames)}]. "
                + $"LastDiagnostic={lastDiagnostic}. "
                + $"Visible=[{string.Join(", ", visibleSnapshot.AllNames.OrderBy(name => name, StringComparer.Ordinal))}]");
        }

        private static ASMLiteAv3ParameterSnapshot GenerateSnapshot(
            IEnumerable<ASMLiteAv3ParameterDescriptor> descriptors,
            ASMLiteAv3SaveLoadRunContext context,
            string phase,
            ASMLiteAv3ParameterSnapshot mustDifferFrom)
        {
            var values = new List<KeyValuePair<string, ASMLiteAv3ParameterValue>>();
            int index = 0;
            foreach (var descriptor in descriptors ?? Enumerable.Empty<ASMLiteAv3ParameterDescriptor>())
            {
                var value = GenerateValue(context.ValueSeed, phase, descriptor, index);
                if (mustDifferFrom != null && mustDifferFrom.TryGetValue(descriptor.Name, out var previous))
                {
                    value = EnsureDifferent(value, previous);
                    Assert.IsTrue(
                        IsSufficientlyDifferent(value, previous),
                        $"{context.ToDisplayString()} phase={phase} param={descriptor.Name} type={descriptor.Type} expected=<dirty-different> actual=saved={previous.ToDisplayString()} dirty={value.ToDisplayString()} tolerance=n/a delta=n/a");
                }

                values.Add(new KeyValuePair<string, ASMLiteAv3ParameterValue>(descriptor.Name, value));
                index++;
            }

            return new ASMLiteAv3ParameterSnapshot(values);
        }

        private static ASMLiteAv3ParameterValue GenerateValue(
            uint seed,
            string phase,
            ASMLiteAv3ParameterDescriptor descriptor,
            int index)
        {
            uint mixed = Mix(seed ^ StableHash(phase) ^ StableHash(descriptor.Name) ^ ((uint)index * 0x9E3779B9u));
            switch (descriptor.Type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    return ASMLiteAv3ParameterValue.Bool((mixed & 1u) != 0u);
                case ASMLiteAv3ParameterType.Int:
                    return ASMLiteAv3ParameterValue.Int(16 + (int)(mixed % 192u));
                case ASMLiteAv3ParameterType.Float:
                    return ASMLiteAv3ParameterValue.Float(((int)(mixed % 1801u) - 900) / 1000f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(descriptor), descriptor.Type, "Unsupported AV3 parameter descriptor type.");
            }
        }

        private static ASMLiteAv3ParameterValue EnsureDifferent(
            ASMLiteAv3ParameterValue candidate,
            ASMLiteAv3ParameterValue previous)
        {
            switch (candidate.Type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    return candidate.BoolValue != previous.BoolValue
                        ? candidate
                        : ASMLiteAv3ParameterValue.Bool(!previous.BoolValue);
                case ASMLiteAv3ParameterType.Int:
                    return candidate.IntValue != previous.IntValue
                        ? candidate
                        : ASMLiteAv3ParameterValue.Int((previous.IntValue + 73) % 256);
                case ASMLiteAv3ParameterType.Float:
                    return Math.Abs(candidate.FloatValue - previous.FloatValue) >= MinimumDirtyFloatSeparation
                        ? candidate
                        : ASMLiteAv3ParameterValue.Float(previous.FloatValue <= 0f ? previous.FloatValue + 0.5f : previous.FloatValue - 0.5f);
                default:
                    return candidate;
            }
        }

        private static bool IsSufficientlyDifferent(
            ASMLiteAv3ParameterValue candidate,
            ASMLiteAv3ParameterValue previous)
        {
            if (candidate.Type != previous.Type)
                return false;

            switch (candidate.Type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    return candidate.BoolValue != previous.BoolValue;
                case ASMLiteAv3ParameterType.Int:
                    return candidate.IntValue != previous.IntValue;
                case ASMLiteAv3ParameterType.Float:
                    return Math.Abs(candidate.FloatValue - previous.FloatValue) >= MinimumDirtyFloatSeparation;
                default:
                    return false;
            }
        }

        private static uint StableHash(string text)
        {
            unchecked
            {
                uint hash = 2166136261u;
                foreach (char c in text ?? string.Empty)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                return value;
            }
        }

        private static void ApplySnapshot(object runtime, ASMLiteAv3SaveLoadRunContext context, string phase, ASMLiteAv3ParameterSnapshot snapshot)
        {
            foreach (var pair in snapshot.Values)
            {
                Assert.IsTrue(
                    ASMLiteAv3RuntimeBridge.TryWriteParameter(runtime, pair.Key, pair.Value, out var diagnostic),
                    $"{context.ToDisplayString()} phase={phase} param={pair.Key} type={pair.Value.Type} expected={pair.Value.ToDisplayString()} actual=<write-failed> tolerance=n/a delta=n/a diagnostic={diagnostic}");
            }
        }

        private static void TriggerControl(object runtime, ASMLiteAv3SaveLoadRunContext context, string phase, int value)
        {
            Assert.IsTrue(
                ASMLiteAv3RuntimeBridge.TryWriteControl(runtime, value, out var writeDiagnostic),
                $"{context.ToDisplayString()} phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} type=Int expected={value} actual=<write-failed> tolerance=n/a delta=n/a diagnostic={writeDiagnostic}");

            Assert.IsTrue(
                ASMLiteAv3RuntimeBridge.TryReadControl(runtime, out int actual, out var readDiagnostic),
                $"{context.ToDisplayString()} phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} type=Int expected={value} actual=<read-failed> tolerance=n/a delta=n/a diagnostic={readDiagnostic}");

            Assert.AreEqual(
                value,
                actual,
                $"{context.ToDisplayString()} phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} type=Int expected={value} actual={actual} tolerance=n/a delta=n/a");
        }

        private static IEnumerator PollUntilControlIdle(object runtime, ASMLiteAv3SaveLoadRunContext context, string phase)
        {
            string lastDiagnostic = string.Empty;
            int lastActual = int.MinValue;
            double deadline = EditorApplication.timeSinceStartup + ActionTimeoutSeconds;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                if (ASMLiteAv3RuntimeBridge.TryReadControl(runtime, out lastActual, out lastDiagnostic) && lastActual == 0)
                    yield break;

                yield return null;
            }

            Assert.Fail(
                $"{context.ToDisplayString()} phase={phase} param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} type=Int expected=0 actual={(lastActual == int.MinValue ? "<read-failed>" : lastActual.ToString(CultureInfo.InvariantCulture))} tolerance=n/a delta=n/a diagnostic={lastDiagnostic}");
        }

        private static IEnumerator PollUntilLoadSettled(
            object runtime,
            ASMLiteAv3SaveLoadRunContext context,
            ASMLiteAv3ParameterSnapshot savedSnapshot,
            ASMLiteAv3ParameterSnapshot unsavedSnapshot)
        {
            string lastDiagnostic = string.Empty;
            int lastControl = int.MinValue;
            double deadline = EditorApplication.timeSinceStartup + ActionTimeoutSeconds;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                bool controlIdle = ASMLiteAv3RuntimeBridge.TryReadControl(runtime, out lastControl, out lastDiagnostic) && lastControl == 0;
                if (controlIdle
                    && ASMLiteAv3SaveLoadAssertions.SnapshotMatches(runtime, savedSnapshot)
                    && ASMLiteAv3SaveLoadAssertions.SnapshotMatches(runtime, unsavedSnapshot))
                {
                    yield break;
                }

                yield return null;
            }

            if (lastControl != 0)
            {
                Assert.Fail(
                    $"{context.ToDisplayString()} phase=load-settle param={ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName} type=Int expected=0 actual={(lastControl == int.MinValue ? "<read-failed>" : lastControl.ToString(CultureInfo.InvariantCulture))} tolerance=n/a delta=n/a diagnostic={lastDiagnostic}");
            }
        }

    }
}
