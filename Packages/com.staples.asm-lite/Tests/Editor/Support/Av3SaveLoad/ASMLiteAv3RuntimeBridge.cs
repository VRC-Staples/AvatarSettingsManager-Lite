using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ASMLite.Tests.PlayMode
{
    internal static class ASMLiteAv3RuntimeBridge
    {
        internal const string ExpectedRuntimeAssemblyName = "lyuma.av3emulator";
        internal const string ExpectedRuntimeTypeName = "Lyuma.Av3Emulator.Runtime.LyumaAv3Runtime";
        internal const string ExpectedEmulatorTypeName = "Lyuma.Av3Emulator.Runtime.LyumaAv3Emulator";
        internal const string ASMLiteControlParameterName = "ASMLite_Ctrl";

        private const BindingFlags InstanceBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string EmulatorObjectName = "ASMLite_AV3_SaveLoad_Runtime_Emulator";

        internal static RuntimeTypeResolution ResolveRuntimeType(
            string fullTypeName = ExpectedRuntimeTypeName,
            string assemblyName = ExpectedRuntimeAssemblyName)
        {
            return ResolveType(fullTypeName, assemblyName);
        }

        internal static RuntimeTypeResolution ResolveEmulatorType()
        {
            return ResolveType(ExpectedEmulatorTypeName, ExpectedRuntimeAssemblyName);
        }

        internal static GameObject EnsureEmulatorControlObject()
        {
            var resolution = ResolveEmulatorType();
            if (!resolution.IsAvailable)
                throw new InvalidOperationException(resolution.Diagnostic);

            var existing = UnityEngine.Object.FindObjectOfType(resolution.Type);
            if (existing != null)
                return ((Component)existing).gameObject;

            var controlObject = GameObject.Find(EmulatorObjectName) ?? new GameObject(EmulatorObjectName);
            var component = controlObject.GetComponent(resolution.Type) ?? controlObject.AddComponent(resolution.Type);

            SetBoolField(component, "RunPreprocessAvatarHook", false);
            SetBoolField(component, "DisableShadowClone", true);
            SetBoolField(component, "DisableMirrorClone", true);
            SetBoolField(component, "CreateNonLocalClone", false);
            SetIntField(component, "CreateNonLocalCloneCount", 0);
            SetBoolField(component, "SelectAvatarOnStartup", false);
            SetBoolField(component, "EnableAvatarOSC", false);

            return controlObject;
        }

        internal static bool TryCaptureVisibleParameters(
            GameObject avatar,
            out ParameterSnapshot snapshot,
            out string diagnostic)
        {
            snapshot = ParameterSnapshot.Empty;

            var resolution = ResolveRuntimeType();
            if (!resolution.IsAvailable)
            {
                diagnostic = resolution.Diagnostic;
                return false;
            }

            if (avatar == null)
            {
                diagnostic = "Runtime: Cannot capture AV3 parameters because the avatar GameObject is null.";
                return false;
            }

            var runtime = avatar.GetComponent(resolution.Type)
                ?? avatar.GetComponentsInChildren(resolution.Type, includeInactive: true).FirstOrDefault();
            if (runtime == null)
            {
                diagnostic = $"Runtime: Avatar '{avatar.name}' does not yet have AV3 runtime component {ExpectedRuntimeTypeName}.";
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            CaptureDictionaryKeys(runtime, "BoolToIndex", names);
            CaptureDictionaryKeys(runtime, "IntToIndex", names);
            CaptureDictionaryKeys(runtime, "FloatToIndex", names);
            CaptureListParameterNames(runtime, "Bools", names);
            CaptureListParameterNames(runtime, "Ints", names);
            CaptureListParameterNames(runtime, "Floats", names);

            snapshot = new ParameterSnapshot(names);
            diagnostic = $"Runtime: Captured {names.Count} AV3 runtime parameter name(s).";
            return names.Count > 0;
        }

        internal static bool TryFindRuntime(
            GameObject avatar,
            out object runtime,
            out string diagnostic)
        {
            runtime = null;

            var resolution = ResolveRuntimeType();
            if (!resolution.IsAvailable)
            {
                diagnostic = resolution.Diagnostic;
                return false;
            }

            if (avatar == null)
            {
                diagnostic = "Runtime: Cannot locate AV3 runtime because the avatar GameObject is null.";
                return false;
            }

            runtime = avatar.GetComponent(resolution.Type)
                ?? avatar.GetComponentsInChildren(resolution.Type, includeInactive: true).FirstOrDefault();

            if (runtime == null)
            {
                diagnostic = $"Runtime: Avatar '{avatar.name}' does not yet have AV3 runtime component {ExpectedRuntimeTypeName}.";
                return false;
            }

            diagnostic = $"Runtime: Found AV3 runtime component {ExpectedRuntimeTypeName} on avatar '{avatar.name}'.";
            return true;
        }

        internal static bool HasParameter(object runtime, string name, ASMLiteAv3ParameterType type)
        {
            return TryGetRuntimeParameter(runtime, name, type, out _);
        }

        internal static bool TryReadParameter(
            object runtime,
            string name,
            ASMLiteAv3ParameterType type,
            out ASMLiteAv3ParameterValue value,
            out string diagnostic)
        {
            value = default;

            if (!TryGetRuntimeParameter(runtime, name, type, out var parameter))
            {
                diagnostic = $"Runtime: AV3 runtime parameter '{name}' of type {type} was not found.";
                return false;
            }

            switch (type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    if (TryReadMember(parameter, "value", out var boolObject) && boolObject is bool boolValue)
                    {
                        value = ASMLiteAv3ParameterValue.Bool(boolValue);
                        diagnostic = string.Empty;
                        return true;
                    }

                    diagnostic = $"Runtime: AV3 bool parameter '{name}' did not expose bool field 'value'.";
                    return false;

                case ASMLiteAv3ParameterType.Int:
                    if (TryReadMember(parameter, "value", out var intObject) && intObject is int intValue)
                    {
                        value = ASMLiteAv3ParameterValue.Int(intValue);
                        diagnostic = string.Empty;
                        return true;
                    }

                    diagnostic = $"Runtime: AV3 int parameter '{name}' did not expose int field 'value'.";
                    return false;

                case ASMLiteAv3ParameterType.Float:
                    if (TryReadMember(parameter, "exportedValue", out var floatObject) && floatObject is float floatValue)
                    {
                        value = ASMLiteAv3ParameterValue.Float(floatValue);
                        diagnostic = string.Empty;
                        return true;
                    }

                    diagnostic = $"Runtime: AV3 float parameter '{name}' did not expose float member 'exportedValue'.";
                    return false;

                default:
                    diagnostic = $"Runtime: Unsupported AV3 parameter type '{type}' for '{name}'.";
                    return false;
            }
        }

        internal static bool TryWriteParameter(
            object runtime,
            string name,
            ASMLiteAv3ParameterValue value,
            out string diagnostic)
        {
            if (!TryGetRuntimeParameter(runtime, name, value.Type, out var parameter))
            {
                diagnostic = $"Runtime: AV3 runtime parameter '{name}' of type {value.Type} was not found for write.";
                return false;
            }

            switch (value.Type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    if (TryWriteMember(parameter, "value", value.BoolValue))
                    {
                        diagnostic = string.Empty;
                        return true;
                    }

                    diagnostic = $"Runtime: AV3 bool parameter '{name}' did not accept write to field 'value'.";
                    return false;

                case ASMLiteAv3ParameterType.Int:
                    if (TryWriteMember(parameter, "value", value.IntValue))
                    {
                        diagnostic = string.Empty;
                        return true;
                    }

                    diagnostic = $"Runtime: AV3 int parameter '{name}' did not accept write to field 'value'.";
                    return false;

                case ASMLiteAv3ParameterType.Float:
                    float clampedFloat = ClampFloatOnly(value.FloatValue);
                    if (TryWriteMember(parameter, "exportedValue", clampedFloat))
                    {
                        diagnostic = string.Empty;
                        return true;
                    }

                    if (TryWriteMember(parameter, "value", clampedFloat))
                    {
                        diagnostic = string.Empty;
                        return true;
                    }

                    diagnostic = $"Runtime: AV3 float parameter '{name}' did not accept write to member 'exportedValue' or 'value'.";
                    return false;

                default:
                    diagnostic = $"Runtime: Unsupported AV3 parameter type '{value.Type}' for '{name}'.";
                    return false;
            }
        }

        internal static bool TryReadControl(object runtime, out int value, out string diagnostic)
        {
            if (TryReadParameter(
                    runtime,
                    ASMLiteControlParameterName,
                    ASMLiteAv3ParameterType.Int,
                    out var parameterValue,
                    out diagnostic))
            {
                value = parameterValue.IntValue;
                return true;
            }

            value = 0;
            return false;
        }

        internal static bool TryWriteControl(object runtime, int value, out string diagnostic)
        {
            return TryWriteParameter(
                runtime,
                ASMLiteControlParameterName,
                ASMLiteAv3ParameterValue.Int(value),
                out diagnostic);
        }

        internal static float ClampFloatOnly(float value)
        {
            if (value < -1f)
                return -1f;
            if (value > 1f)
                return 1f;
            return value;
        }

        internal static float FloatToleranceFor(object runtime)
        {
            return 0.0001f;
        }

        private static RuntimeTypeResolution ResolveType(string fullTypeName, string assemblyName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName, "Runtime: AV3 runtime type name was empty.");

            Type resolved = null;
            var typeSpec = string.IsNullOrEmpty(assemblyName) ? fullTypeName : fullTypeName + ", " + assemblyName;

            try
            {
                resolved = Type.GetType(typeSpec, throwOnError: false);
            }
            catch (Exception ex)
            {
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName,
                    $"Runtime: AV3 runtime reflection failed while resolving '{typeSpec}': {ex.GetType().Name}: {ex.Message}");
            }

            if (resolved == null)
            {
                resolved = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly =>
                    {
                        try
                        {
                            return assembly.GetType(fullTypeName, throwOnError: false);
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .FirstOrDefault(type => type != null);
            }

            if (resolved == null && !string.IsNullOrEmpty(assemblyName))
            {
                try
                {
                    var assembly = Assembly.Load(assemblyName);
                    resolved = assembly.GetType(fullTypeName, throwOnError: false);
                }
                catch
                {
                    // Optional package may not be installed. Return a deterministic diagnostic below.
                }
            }

            if (resolved == null)
            {
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName,
                    $"Runtime: Optional AV3 runtime type '{fullTypeName}' was not found in assembly '{assemblyName}'. "
                    + "Install/resolve Lyuma Av3Emulator for the play-mode visibility spike; ASM-Lite tests intentionally avoid a hard asmdef reference.");
            }

            if (!typeof(Component).IsAssignableFrom(resolved))
            {
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName,
                    $"Runtime: Resolved AV3 runtime type '{fullTypeName}' from assembly '{resolved.Assembly.GetName().Name}', but it is not a Unity Component.");
            }

            return RuntimeTypeResolution.Available(resolved, fullTypeName, assemblyName);
        }

        private static void CaptureDictionaryKeys(object runtime, string fieldName, HashSet<string> names)
        {
            var dictionary = ReadFieldValue(runtime, fieldName) as IDictionary;
            if (dictionary == null)
                return;

            foreach (object key in dictionary.Keys)
            {
                var name = key as string;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }

        private static void CaptureListParameterNames(object runtime, string fieldName, HashSet<string> names)
        {
            var list = ReadFieldValue(runtime, fieldName) as IEnumerable;
            if (list == null)
                return;

            foreach (object item in list)
            {
                if (item == null)
                    continue;

                var nameField = item.GetType().GetField("name", InstanceBindingFlags);
                var name = nameField != null ? nameField.GetValue(item) as string : null;
                if (!string.IsNullOrEmpty(name))
                    names.Add(name);
            }
        }

        private static object ReadFieldValue(object target, string fieldName)
        {
            if (target == null || string.IsNullOrEmpty(fieldName))
                return null;

            var field = target.GetType().GetField(fieldName, InstanceBindingFlags);
            return field != null ? field.GetValue(target) : null;
        }

        private static bool TryGetRuntimeParameter(
            object runtime,
            string name,
            ASMLiteAv3ParameterType type,
            out object parameter)
        {
            parameter = null;
            if (runtime == null || string.IsNullOrWhiteSpace(name))
                return false;

            switch (type)
            {
                case ASMLiteAv3ParameterType.Bool:
                    return TryGetRuntimeParameter(runtime, "BoolToIndex", "Bools", name, out parameter);
                case ASMLiteAv3ParameterType.Int:
                    return TryGetRuntimeParameter(runtime, "IntToIndex", "Ints", name, out parameter);
                case ASMLiteAv3ParameterType.Float:
                    return TryGetRuntimeParameter(runtime, "FloatToIndex", "Floats", name, out parameter);
                default:
                    return false;
            }
        }

        private static bool TryGetRuntimeParameter(
            object runtime,
            string dictionaryFieldName,
            string listFieldName,
            string name,
            out object parameter)
        {
            parameter = null;

            var dictionary = ReadFieldValue(runtime, dictionaryFieldName) as IDictionary;
            var list = ReadFieldValue(runtime, listFieldName) as IList;
            if (dictionary == null || list == null || !dictionary.Contains(name))
                return false;

            var rawIndex = dictionary[name];
            if (!(rawIndex is int index) || index < 0 || index >= list.Count)
                return false;

            parameter = list[index];
            return parameter != null;
        }

        private static bool TryReadMember(object target, string memberName, out object value)
        {
            value = null;
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var type = target.GetType();
            var property = type.GetProperty(memberName, InstanceBindingFlags);
            if (property != null && property.CanRead)
            {
                value = property.GetValue(target, null);
                return true;
            }

            var field = type.GetField(memberName, InstanceBindingFlags);
            if (field != null)
            {
                value = field.GetValue(target);
                return true;
            }

            return false;
        }

        private static bool TryWriteMember(object target, string memberName, object value)
        {
            if (target == null || string.IsNullOrWhiteSpace(memberName))
                return false;

            var type = target.GetType();
            var property = type.GetProperty(memberName, InstanceBindingFlags);
            if (property != null && property.CanWrite && CanAssign(property.PropertyType, value))
            {
                property.SetValue(target, value, null);
                return true;
            }

            var field = type.GetField(memberName, InstanceBindingFlags);
            if (field != null && CanAssign(field.FieldType, value))
            {
                field.SetValue(target, value);
                return true;
            }

            return false;
        }

        private static bool CanAssign(Type targetType, object value)
        {
            if (targetType == null)
                return false;

            if (value == null)
                return !targetType.IsValueType;

            return targetType.IsInstanceOfType(value)
                || string.Equals(targetType.FullName, value.GetType().FullName, StringComparison.Ordinal);
        }

        private static void SetBoolField(object target, string fieldName, bool value)
        {
            SetField(target, fieldName, typeof(bool), value);
        }

        private static void SetIntField(object target, string fieldName, int value)
        {
            SetField(target, fieldName, typeof(int), value);
        }

        private static void SetField(object target, string fieldName, Type expectedType, object value)
        {
            if (target == null)
                return;

            var field = target.GetType().GetField(fieldName, InstanceBindingFlags | StaticBindingFlags);
            if (field == null || field.FieldType != expectedType)
                return;

            field.SetValue(field.IsStatic ? null : target, value);
        }

        internal readonly struct RuntimeTypeResolution
        {
            private RuntimeTypeResolution(Type type, string fullTypeName, string assemblyName, string diagnostic)
            {
                Type = type;
                FullTypeName = fullTypeName ?? string.Empty;
                AssemblyName = assemblyName ?? string.Empty;
                Diagnostic = diagnostic ?? string.Empty;
            }

            internal Type Type { get; }
            internal string FullTypeName { get; }
            internal string AssemblyName { get; }
            internal string Diagnostic { get; }
            internal bool IsAvailable => Type != null;

            internal static RuntimeTypeResolution Available(Type type, string fullTypeName, string assemblyName)
            {
                var resolvedAssembly = type != null ? type.Assembly.GetName().Name : string.Empty;
                return new RuntimeTypeResolution(type, fullTypeName, assemblyName,
                    $"Runtime: Resolved optional AV3 runtime type '{fullTypeName}' from assembly '{resolvedAssembly}'.");
            }

            internal static RuntimeTypeResolution Missing(string fullTypeName, string assemblyName, string diagnostic)
            {
                return new RuntimeTypeResolution(null, fullTypeName, assemblyName, diagnostic);
            }
        }

        internal readonly struct ParameterSnapshot
        {
            internal static readonly ParameterSnapshot Empty = new ParameterSnapshot(new HashSet<string>(StringComparer.Ordinal));

            private readonly HashSet<string> _names;

            internal ParameterSnapshot(IEnumerable<string> names)
            {
                _names = new HashSet<string>(names ?? Array.Empty<string>(), StringComparer.Ordinal);
            }

            internal IEnumerable<string> AllNames => _names ?? Enumerable.Empty<string>();

            internal bool Contains(string name)
            {
                return !string.IsNullOrEmpty(name) && _names != null && _names.Contains(name);
            }
        }
    }
}
