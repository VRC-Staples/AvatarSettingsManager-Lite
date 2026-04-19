using System;
using System.Text.RegularExpressions;
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
        public ControllerEntry[] controllers = Array.Empty<ControllerEntry>();
        public MenuEntryWithoutPrefix[] menus = Array.Empty<MenuEntryWithoutPrefix>();
        public ParameterEntry[] prms = Array.Empty<ParameterEntry>();
        public string[] smoothedPrms = Array.Empty<string>();
        public string[] globalParams = Array.Empty<string>();
        public ObjectRef controller = new ObjectRef();
        public ObjectRef menu = new ObjectRef();
        public ObjectRef parameters = new ObjectRef();
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
            var resolvedPrefix = RefreshLiveFullControllerPrefixAndRead(useCustomInstallPath: true, customInstallPath: "  Avatars/ASM  ");

            Assert.AreEqual("Avatars/ASM", resolvedPrefix,
                "W03: enabled custom install path must trim and serialize the FullController menu prefix.");
        }

        [Test]
        public void W04_PrefabWiring_FallsBackToEmptyPrefix_WhenDisabled()
        {
            var resolvedPrefix = RefreshLiveFullControllerPrefixAndRead(useCustomInstallPath: false, customInstallPath: "Avatar/ShouldNotApply");

            Assert.AreEqual(string.Empty, resolvedPrefix,
                "W04: disabled custom install path must fail closed to empty FullController prefix even when text exists.");
        }

        [Test]
        public void W05_PrefabWiring_FallsBackToEmptyPrefix_WhenEnabledPathIsBlank()
        {
            var resolvedPrefix = RefreshLiveFullControllerPrefixAndRead(useCustomInstallPath: true, customInstallPath: "   ");

            Assert.AreEqual(string.Empty, resolvedPrefix,
                "W05: blank enabled custom install path must fail closed to empty FullController prefix.");
        }

        [Test]
        public void W06_LiveWiring_NullComponent_FailsClosedToEmptyPrefix()
        {
            var go = new GameObject("W06_NullComponent");
            try
            {
                Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(go, null, "W06 Null Component"),
                    "W06: live FullController wiring should fail closed and still complete when no ASM-Lite component is available.");

                var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(go);
                Assert.IsNotNull(vf,
                    "W06: live FullController wiring should still create a VF.Model.VRCFury payload for the target root.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(vf),
                    "W06: null component input must fail closed to empty prefix through the actual FullController wiring path.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void W07_FullControllerAssetReferenceSync_MissingPrefixField_FailsClosedWithoutThrowing()
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
                    controllers = new[] { new VF.Model.Feature.ControllerEntry() },
                    menus = new[] { new VF.Model.Feature.MenuEntryWithoutPrefix() },
                    prms = new[] { new VF.Model.Feature.ParameterEntry() },
                    globalParams = new[] { string.Empty }
                };

                var fxController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.FXController);
                var menu = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.Menu);
                var parameters = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.ExprParams);
                Assert.IsNotNull(fxController, "W07: generated FX controller asset should exist for schema-drift coverage.");
                Assert.IsNotNull(menu, "W07: generated menu asset should exist for schema-drift coverage.");
                Assert.IsNotNull(parameters, "W07: generated expression-parameters asset should exist for schema-drift coverage.");

                var serializedVf = new SerializedObject(vf);
                serializedVf.Update();

                LogAssert.Expect(LogType.Error,
                    new Regex(@"^\[ASM-Lite\] Expected VRCFury FullController menu prefix field was not found: 'content\.menus\.Array\.data\[0\]\.prefix'\.$"));

                Assert.DoesNotThrow(() =>
                {
                    var applied = ASMLitePrefabCreator.TryApplyFullControllerAssetReferences(serializedVf, component, fxController, menu, parameters);
                    Assert.IsFalse(applied,
                        "W07: schema drift with a missing FullController menu-prefix field must fail closed without writing partial state.");
                });
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void W08_BuildSync_PropagatesCustomInstallPathToLiveFullControllerPrefix()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                Assert.IsNotNull(ctx?.Comp, "W08: fixture creation failed to produce ASMLite component.");

                var component = ctx.Comp;
                component.useCustomInstallPath = true;
                component.customInstallPath = "  Avatars/UploadSync  ";

                var vf = component.gameObject.GetComponent<VF.Model.VRCFury>();
                if (vf == null)
                    vf = component.gameObject.AddComponent<VF.Model.VRCFury>();

                vf.content = new VF.Model.Feature.FullController
                {
                    menus = new[] { new VF.Model.Feature.MenuEntry() }
                };

                var beforeSo = new SerializedObject(vf);
                beforeSo.Update();
                var beforePrefix = beforeSo.FindProperty("content.menus.Array.data[0].prefix");
                Assert.IsNotNull(beforePrefix,
                    "W08: expected FullController prefix field at content.menus.Array.data[0].prefix before Build().");
                Assert.AreEqual(string.Empty, beforePrefix.stringValue,
                    "W08: setup should start from empty prefix before Build() sync.");

                int buildResult = ASMLiteBuilder.Build(component);
                Assert.GreaterOrEqual(buildResult, 0,
                    $"W08: Build() should succeed while syncing install prefix. result={buildResult}.");

                var afterSo = new SerializedObject(vf);
                afterSo.Update();
                var afterPrefix = afterSo.FindProperty("content.menus.Array.data[0].prefix");
                Assert.IsNotNull(afterPrefix,
                    "W08: expected FullController prefix field at content.menus.Array.data[0].prefix after Build().");
                Assert.AreEqual("Avatars/UploadSync", afterPrefix.stringValue,
                    "W08: Build() must propagate trimmed custom install path into live FullController menu prefix for preprocess/upload parity.");
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        private static string RefreshLiveFullControllerPrefixAndRead(bool useCustomInstallPath, string customInstallPath)
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                Assert.IsNotNull(ctx?.Comp, "Expected fixture creation to produce an ASM-Lite component.");

                ctx.Comp.useCustomInstallPath = useCustomInstallPath;
                ctx.Comp.customInstallPath = customInstallPath;

                Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(ctx.Comp.gameObject, ctx.Comp, "Install Path Wiring Test"),
                    "Live FullController wiring refresh should succeed for install-path wiring coverage.");

                var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(ctx.Comp.gameObject);
                Assert.IsNotNull(vf,
                    "Live FullController wiring refresh should leave a VF.Model.VRCFury component on the ASM-Lite object.");
                return ASMLiteTestFixtures.ReadSerializedMenuPrefix(vf);
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }
    }
}
