using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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
        private const string SuiteName = nameof(ASMLiteInstallPathWiringTests);

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

                var serializedVf = new SerializedObject(vf);
                serializedVf.Update();

                Assert.DoesNotThrow(() =>
                {
                    var diagnostic = ASMLiteFullControllerInstallPathHelper.TryApplyMenuPrefixWithDiagnostics(serializedVf, component);
                    ASMLiteTestFixtures.RecordBuildDiagnosticFailure(
                        SuiteName,
                        nameof(W07_FullControllerAssetReferenceSync_MissingPrefixField_FailsClosedWithoutThrowing),
                        diagnostic);
                    Assert.IsFalse(diagnostic.Success,
                        "W07: schema drift with a missing FullController menu-prefix field must fail closed without writing partial state.");
                    Assert.AreEqual(ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath, diagnostic.Code,
                        "W07: missing FullController prefix path must emit deterministic DRIFT-203.");
                    Assert.AreEqual(ASMLiteDriftProbe.MenuPrefixPath, diagnostic.ContextPath,
                        "W07: DRIFT-203 diagnostics must include the exact missing prefix path.");
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

        [Test, Category("Integration")]
        public void W09_BuildSync_PrefabInstance_UsesRoutingHelperAndClearsPrefixOverride()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            ASMLite.Editor.ASMLiteWindow window = null;
            try
            {
                UnityEngine.Object.DestroyImmediate(ctx.Comp.gameObject);
                ctx.Comp = null;

                window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
                window.SelectAvatarForAutomation(ctx.AvDesc);
                window.AddPrefabForAutomation();

                var component = ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(component,
                    "W09: prefab-instance rebuild characterization requires AddPrefabForAutomation() to attach ASM-Lite first.");
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(component.gameObject),
                    "W09: AddPrefabForAutomation() should attach ASM-Lite as a prefab instance before rebuild routing checks.");

                component.useCustomInstallPath = true;
                component.customInstallPath = "  Tools/PrefabRouting  ";
                window.SelectAvatarForAutomation(ctx.AvDesc);
                window.RebuildForAutomation();

                var liveVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(component.gameObject);
                Assert.IsNotNull(liveVf,
                    "W09: prefab-instance rebuild should keep a live VF.Model.VRCFury component on the ASM-Lite object.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(liveVf),
                    "W09: prefab-instance rebuilds must clear direct FullController menu-prefix overrides after routing through the helper object.");
                AssertRoutingHelperPaths(ctx.AvDesc, "Settings Manager", "Tools/PrefabRouting/Settings Manager",
                    "W09: prefab-instance rebuild should route install-path changes through the deterministic avatar helper object.");
            }
            finally
            {
                if (window != null)
                    UnityEngine.Object.DestroyImmediate(window);
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [Test]
        public void W10_BuildSync_PrefabInstance_CustomInstallPathRequiresRoutingSuccess()
        {
            string prefabPath = string.Empty;
            var prefabSource = new GameObject("W10_RoutingFailureSource");
            GameObject prefabInstance = null;

            try
            {
                var sourceComponent = prefabSource.AddComponent<ASMLiteComponent>();
                sourceComponent.useCustomInstallPath = true;
                sourceComponent.customInstallPath = "Tools/BlockedRouting";

                if (!AssetDatabase.IsValidFolder("Assets/ASMLiteTests_Temp"))
                    AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");

                prefabPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLiteTests_Temp/W10_RoutingFailure.prefab");
                var prefabAsset = PrefabUtility.SaveAsPrefabAsset(prefabSource, prefabPath);
                Assert.IsNotNull(prefabAsset,
                    $"W10: expected prefab asset at '{prefabPath}' for prefab-instance routing failure coverage.");

                prefabInstance = PrefabUtility.InstantiatePrefab(prefabAsset) as GameObject;
                Assert.IsNotNull(prefabInstance,
                    "W10: expected prefab instantiation to produce a GameObject instance.");

                var component = prefabInstance.GetComponent<ASMLiteComponent>();
                var liveVf = prefabInstance.GetComponent<VF.Model.VRCFury>();
                if (liveVf == null)
                    liveVf = prefabInstance.AddComponent<VF.Model.VRCFury>();

                liveVf.content = new VF.Model.Feature.FullController
                {
                    menus = new[]
                    {
                        new VF.Model.Feature.MenuEntry
                        {
                            prefix = "Stale/Prefix"
                        }
                    }
                };

                Assert.IsNotNull(component,
                    "W10: expected prefab instance to preserve the ASMLiteComponent.");
                Assert.IsNotNull(liveVf,
                    "W10: expected prefab instance to provide a live VF.Model.VRCFury payload for stale-prefix clearing coverage.");
                Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(component.gameObject),
                    "W10: regression coverage requires the target component to be part of a prefab instance.");
                Assert.AreEqual("Stale/Prefix", ASMLiteTestFixtures.ReadSerializedMenuPrefix(liveVf),
                    "W10: setup should start with a stale direct FullController prefix override before sync.");

                var diagnostic = ASMLiteBuilder.TrySyncInstallPathRoutingWithDiagnostics(component);
                Assert.IsFalse(diagnostic.Success,
                    "W10: prefab-instance install-path sync must fail closed when a custom install prefix is enabled but MoveMenu routing cannot be created.");
                Assert.AreEqual(ASMLiteDiagnosticCodes.Build.InstallPrefixSyncFailed, diagnostic.Code,
                    "W10: routing failure should surface the deterministic build diagnostic for install-prefix sync failure.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(liveVf),
                    "W10: stale direct FullController prefixes may still be cleared, but clearing alone must not report success when routing failed.");
                Assert.IsNull(prefabInstance.transform.Find("ASM-Lite Install Path Routing"),
                    "W10: sync should not fabricate a routing helper when no avatar descriptor exists to parent it.");
            }
            finally
            {
                if (prefabInstance != null)
                    UnityEngine.Object.DestroyImmediate(prefabInstance);
                if (prefabSource != null)
                    UnityEngine.Object.DestroyImmediate(prefabSource);
                if (!string.IsNullOrWhiteSpace(prefabPath) && AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                    AssetDatabase.DeleteAsset(prefabPath);
            }
        }

        [Test]
        public void SetInstallPathStateForAutomation_DisabledBranchIsDistinctFromRootSelection()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(ctx.AvDesc);

                window.SetInstallPathStateForAutomation(false, "Tools/IgnoredWhenDisabled");
                var disabled = window.GetPendingCustomizationSnapshotForAutomation();

                window.SetInstallPathStateForAutomation(true, string.Empty);
                var rootSelected = window.GetPendingCustomizationSnapshotForAutomation();

                Assert.IsFalse(disabled.UseCustomInstallPath,
                    "Disabled install-path state must remain an explicit branch, not collapse into root-selected custom mode.");
                Assert.IsTrue(rootSelected.UseCustomInstallPath,
                    "Selecting the avatar root should keep custom install-path mode enabled with an empty effective path.");
                Assert.AreEqual(string.Empty, disabled.EffectiveInstallPath,
                    "Disabled install-path state should fail closed to an empty effective prefix.");
                Assert.AreEqual(string.Empty, rootSelected.EffectiveInstallPath,
                    "Root-selected custom install path should also resolve to an empty effective prefix while preserving the enabled branch.");
                Assert.AreNotEqual(disabled.UseCustomInstallPath, rootSelected.UseCustomInstallPath,
                    "The automation snapshot contract must expose the branch distinction even when both states resolve to the same effective prefix.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [TestCase("  Tools  ", "Tools")]
        [TestCase(" /Tools//Nested/ ", "Tools/Nested")]
        public void SetInstallPathStateForAutomation_NormalizesSimpleAndNestedEffectivePath(string rawPath, string expectedEffectivePath)
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(ctx.AvDesc);

                window.SetInstallPathStateForAutomation(true, rawPath);

                var snapshot = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Setting a custom install path through automation should leave custom install mode enabled.");
                Assert.AreEqual(expectedEffectivePath, snapshot.EffectiveInstallPath,
                    "The pending snapshot should report the normalized effective install path, not the raw editor text.");
                Assert.AreNotEqual(rawPath, snapshot.EffectiveInstallPath,
                    "EffectiveInstallPath must not leak untrimmed or uncollapsed raw text from the automation seam.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [Test]
        public void RebuildForAutomation_UsesNormalizedEffectiveInstallPath_NotRawText()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(ctx.AvDesc);
                window.SetInstallPathStateForAutomation(true, " /Tools//Nested/ ");

                window.RebuildForAutomation();

                var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(ctx.Comp.gameObject);
                Assert.IsNotNull(vf,
                    "Rebuild should leave live FullController wiring available for install-path prefix assertions.");
                Assert.AreEqual("Tools/Nested", ASMLiteTestFixtures.ReadSerializedMenuPrefix(vf),
                    "Rebuild should serialize the normalized effective install path into FullController wiring, never the raw automation text.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
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

        private static void AssertRoutingHelperPaths(
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar,
            string expectedFromPath,
            string expectedToPath,
            string aid)
        {
            var routingTransform = avatar != null ? avatar.transform.Find("ASM-Lite Install Path Routing") : null;
            Assert.IsNotNull(routingTransform,
                aid + " Expected the ASM-Lite install-path routing helper object to exist on the avatar.");

            var routingVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(routingTransform.gameObject);
            Assert.IsNotNull(routingVf,
                aid + " Expected the install-path routing helper object to carry a VF.Model.VRCFury component.");

            var serializedRouting = new SerializedObject(routingVf);
            serializedRouting.Update();

            var fromPathProperty = serializedRouting.FindProperty("content.fromPath");
            var toPathProperty = serializedRouting.FindProperty("content.toPath");
            Assert.IsNotNull(fromPathProperty,
                aid + " Expected MoveMenuItem fromPath to be serialized on the routing helper.");
            Assert.IsNotNull(toPathProperty,
                aid + " Expected MoveMenuItem toPath to be serialized on the routing helper.");
            Assert.AreEqual(expectedFromPath, fromPathProperty.stringValue,
                aid + " Unexpected routing helper source path.");
            Assert.AreEqual(expectedToPath, toPathProperty.stringValue,
                aid + " Unexpected routing helper destination path.");
        }
    }
}
