using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteAv3RuntimeBridge
    {
        internal const string ExpectedRuntimeAssemblyName = "lyuma.av3emulator";
        internal const string ExpectedRuntimeTypeName = "Lyuma.Av3Emulator.Runtime.LyumaAv3Runtime";
        internal const string ExpectedEmulatorTypeName = "Lyuma.Av3Emulator.Runtime.LyumaAv3Emulator";
        internal const string ASMLiteControlParameterName = "ASMLite_Ctrl";

        private const BindingFlags InstanceBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticBindingFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const string EmulatorObjectName = "ASMLite_AV3_SaveLoad_P0_Emulator";

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
                diagnostic = "P0: Cannot capture AV3 parameters because the avatar GameObject is null.";
                return false;
            }

            var runtime = avatar.GetComponent(resolution.Type)
                ?? avatar.GetComponentsInChildren(resolution.Type, includeInactive: true).FirstOrDefault();
            if (runtime == null)
            {
                diagnostic = $"P0: Avatar '{avatar.name}' does not yet have AV3 runtime component {ExpectedRuntimeTypeName}.";
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
            diagnostic = $"P0: Captured {names.Count} AV3 runtime parameter name(s).";
            return names.Count > 0;
        }

        private static RuntimeTypeResolution ResolveType(string fullTypeName, string assemblyName)
        {
            if (string.IsNullOrEmpty(fullTypeName))
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName, "P0: AV3 runtime type name was empty.");

            Type resolved = null;
            var typeSpec = string.IsNullOrEmpty(assemblyName) ? fullTypeName : fullTypeName + ", " + assemblyName;

            try
            {
                resolved = Type.GetType(typeSpec, throwOnError: false);
            }
            catch (Exception ex)
            {
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName,
                    $"P0: AV3 runtime reflection failed while resolving '{typeSpec}': {ex.GetType().Name}: {ex.Message}");
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
                    $"P0: Optional AV3 runtime type '{fullTypeName}' was not found in assembly '{assemblyName}'. "
                    + "Install/resolve Lyuma Av3Emulator for the play-mode visibility spike; ASM-Lite tests intentionally avoid a hard asmdef reference.");
            }

            if (!typeof(Component).IsAssignableFrom(resolved))
            {
                return RuntimeTypeResolution.Missing(fullTypeName, assemblyName,
                    $"P0: Resolved AV3 runtime type '{fullTypeName}' from assembly '{resolved.Assembly.GetName().Name}', but it is not a Unity Component.");
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
                    $"P0: Resolved optional AV3 runtime type '{fullTypeName}' from assembly '{resolvedAssembly}'.");
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
