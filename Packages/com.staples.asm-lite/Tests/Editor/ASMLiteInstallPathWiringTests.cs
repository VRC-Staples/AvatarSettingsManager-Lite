using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ASMLite.Editor;

namespace VF.Model.Feature
{
    [Serializable]
    internal class FullController
    {
        public ControllerEntry[] controllers = Array.Empty<ControllerEntry>();
        public MenuEntry[] menus = Array.Empty<MenuEntry>();
        public ParameterEntry[] prms = Array.Empty<ParameterEntry>();
        public string[] smoothedPrms = Array.Empty<string>();
        public string[] globalParams = Array.Empty<string>();
        public ObjectRef controller = new ObjectRef();
        public ObjectRef menu = new ObjectRef();
        public ObjectRef parameters = new ObjectRef();
    }

    [Serializable]
    internal class BrokenFullController
    {
        public MenuEntryWithoutPrefix[] menus = Array.Empty<MenuEntryWithoutPrefix>();
    }

    [Serializable]
    internal class ControllerEntry
    {
        public ObjectRef controller = new ObjectRef();
        public int type;
    }

    [Serializable]
    internal class MenuEntry
    {
        public ObjectRef menu = new ObjectRef();
        public string prefix = string.Empty;
    }

    [Serializable]
    internal class MenuEntryWithoutPrefix
    {
        public ObjectRef menu = new ObjectRef();
    }

    [Serializable]
    internal class ParameterEntry
    {
        public ObjectRef parameters = new ObjectRef();
        public ObjectRef parameter = new ObjectRef();
        public UnityEngine.Object objRef;
    }

    [Serializable]
    internal class ObjectRef
    {
        public UnityEngine.Object objRef;
    }
}

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteInstallPathWiringTests
    {
        [Test]
        public void W03_PrefabWiring_UsesTrimmedCustomInstallPrefix_WhenEnabled()
        {
            LogAssert.Expect(LogType.Log, "[ASM-Lite] FullController menu prefix resolved to 'Avatars/ASM'.");

            var resolvedPrefix = ConfigurePrefabWiringAndReadPrefix(useCustomInstallPath: true, customInstallPath: "  Avatars/ASM  ");

            Assert.AreEqual("Avatars/ASM", resolvedPrefix,
                "W03: enabled custom install path must trim and serialize the FullController menu prefix.");
        }

        [Test]
        public void W04_PrefabWiring_FallsBackToEmptyPrefix_WhenDisabled()
        {
            LogAssert.Expect(LogType.Log, "[ASM-Lite] FullController menu prefix resolved to empty (custom install path disabled or blank).");

            var resolvedPrefix = ConfigurePrefabWiringAndReadPrefix(useCustomInstallPath: false, customInstallPath: "Avatar/ShouldNotApply");

            Assert.AreEqual(string.Empty, resolvedPrefix,
                "W04: disabled custom install path must fail closed to empty FullController prefix even when text exists.");
        }

        [Test]
        public void W05_PrefabWiring_FallsBackToEmptyPrefix_WhenEnabledPathIsBlank()
        {
            LogAssert.Expect(LogType.Log, "[ASM-Lite] FullController menu prefix resolved to empty (custom install path disabled or blank).");

            var resolvedPrefix = ConfigurePrefabWiringAndReadPrefix(useCustomInstallPath: true, customInstallPath: "   ");

            Assert.AreEqual(string.Empty, resolvedPrefix,
                "W05: blank enabled custom install path must fail closed to empty FullController prefix.");
        }

        [Test]
        public void W06_PrefixHelper_NullComponent_ResolvesToEmptyPrefix()
        {
            var helperType = typeof(ASMLitePrefabCreator).Assembly.GetType("ASMLite.Editor.ASMLiteFullControllerInstallPathHelper");
            Assert.IsNotNull(helperType, "W06: failed to locate install-path helper type.");

            var resolveMethod = helperType.GetMethod("ResolveEffectivePrefix", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(resolveMethod, "W06: failed to locate ResolveEffectivePrefix method.");

            var resolved = resolveMethod.Invoke(null, new object[] { null }) as string;
            Assert.AreEqual(string.Empty, resolved,
                "W06: null component input must fail closed to empty prefix.");
        }

        [Test]
        public void W07_PrefixHelper_MissingPrefixField_FailsClosedWithoutThrowing()
        {
            var go = new GameObject("W07_PrefixSchemaDrift");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                component.useCustomInstallPath = true;
                component.customInstallPath = "Avatars/Custom";

                var vf = go.AddComponent<VF.Model.VRCFury>();
                vf.content = new VF.Model.Feature.BrokenFullController
                {
                    menus = new[] { new VF.Model.Feature.MenuEntryWithoutPrefix() }
                };

                var helperType = typeof(ASMLitePrefabCreator).Assembly.GetType("ASMLite.Editor.ASMLiteFullControllerInstallPathHelper");
                Assert.IsNotNull(helperType, "W07: failed to locate install-path helper type.");

                var applyMethod = helperType.GetMethod("TryApplyMenuPrefix", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(applyMethod, "W07: failed to locate TryApplyMenuPrefix method.");

                var serializedVf = new SerializedObject(vf);
                serializedVf.Update();

                LogAssert.Expect(LogType.Error,
                    "[ASM-Lite] Expected VRCFury FullController menu prefix field was not found: 'content.menus.Array.data[0].prefix'.");

                Assert.DoesNotThrow(() =>
                {
                    var applied = (bool)applyMethod.Invoke(null, new object[] { serializedVf, component });
                    Assert.IsFalse(applied,
                        "W07: schema drift (missing content.menus[0].prefix) must fail closed without writing partial state.");
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        private static string ConfigurePrefabWiringAndReadPrefix(bool useCustomInstallPath, string customInstallPath)
        {
            var go = new GameObject("WiringRoot");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                component.useCustomInstallPath = useCustomInstallPath;
                component.customInstallPath = customInstallPath;

                var configureMethod = typeof(ASMLitePrefabCreator).GetMethod(
                    "ConfigureVRCFuryFullController",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(configureMethod,
                    "Expected ASMLitePrefabCreator.ConfigureVRCFuryFullController private method was not found.");

                Assert.DoesNotThrow(() => configureMethod.Invoke(null, new object[] { go, component }),
                    "Prefab FullController wiring should fail closed without throwing when reflected schema is available.");

                var vf = go.GetComponent<VF.Model.VRCFury>();
                Assert.IsNotNull(vf,
                    "Prefab FullController wiring should add VF.Model.VRCFury component when reflected type is available.");

                var so = new SerializedObject(vf);
                so.Update();

                var prefixProperty = so.FindProperty("content.menus.Array.data[0].prefix");
                Assert.IsNotNull(prefixProperty,
                    "Expected FullController prefix field 'content.menus.Array.data[0].prefix' was not serialized.");

                return prefixProperty.stringValue;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
