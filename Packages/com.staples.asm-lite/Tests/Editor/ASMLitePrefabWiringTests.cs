using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ASMLite.Editor;
using Object = UnityEngine.Object;

namespace VF.Model.Feature
{
    [Serializable]
    internal class BrokenFullControllerMissingParameterFallback
    {
        public ControllerEntry[] controllers = Array.Empty<ControllerEntry>();
        public MenuEntry[] menus = Array.Empty<MenuEntry>();
        public ParameterEntryWithoutAnyObjectRef[] prms = Array.Empty<ParameterEntryWithoutAnyObjectRef>();
        public string[] smoothedPrms = Array.Empty<string>();
        public string[] globalParams = Array.Empty<string>();
        public ObjectRef controller = new ObjectRef();
        public ObjectRef menu = new ObjectRef();
        public ObjectRef parameters = new ObjectRef();
    }

    [Serializable]
    internal class ParameterEntryWithoutAnyObjectRef
    {
        public string marker = string.Empty;
    }
}

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLitePrefabWiringTests
    {
        private const string SuiteName = nameof(ASMLitePrefabWiringTests);

        [Test, Category("Integration")]
        public void W01_PrefabWiring_UsesGeneratedAssetReferences_ForFullController()
        {
            var go = new GameObject("W01_WiringRoot");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();

                var configureMethod = typeof(ASMLitePrefabCreator).GetMethod(
                    "ConfigureVRCFuryFullController",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(configureMethod,
                    "Expected ASMLitePrefabCreator.ConfigureVRCFuryFullController private method was not found.");

                LogAssert.Expect(LogType.Log, "[ASM-Lite] FullController menu prefix resolved to empty (custom install path disabled or blank).");

                Assert.DoesNotThrow(() => configureMethod.Invoke(null, new object[] { go, component }),
                    "Prefab FullController wiring should not throw for the reflected VRCFury schema.");

                var vf = go.GetComponents<MonoBehaviour>()
                    .FirstOrDefault(mb => mb != null && string.Equals(mb.GetType().FullName, "VF.Model.VRCFury", StringComparison.Ordinal));
                Assert.IsNotNull(vf,
                    "Prefab FullController wiring should add VF.Model.VRCFury when reflected type is available.");

                var so = new SerializedObject(vf);
                so.Update();

                var fxController = AssetDatabase.LoadAssetAtPath<Object>(ASMLiteAssetPaths.FXController);
                var menu = AssetDatabase.LoadAssetAtPath<Object>(ASMLiteAssetPaths.Menu);
                var parameters = AssetDatabase.LoadAssetAtPath<Object>(ASMLiteAssetPaths.ExprParams);

                Assert.AreEqual(fxController, so.FindProperty("content.controllers.Array.data[0].controller.objRef")?.objectReferenceValue,
                    "Prefab wiring must reference generated FX controller asset.");
                Assert.AreEqual(menu, so.FindProperty("content.menus.Array.data[0].menu.objRef")?.objectReferenceValue,
                    "Prefab wiring must reference generated expressions menu asset.");
                Assert.AreEqual(parameters, so.FindProperty("content.prms.Array.data[0].parameters.objRef")?.objectReferenceValue,
                    "Prefab wiring must reference generated expression parameters asset through prms.");
                Assert.AreEqual("*", so.FindProperty("content.globalParams.Array.data[0]")?.stringValue,
                    "Prefab wiring must keep wildcard global parameter enrollment.");

                var allNonsyncedAreGlobal = so.FindProperty("content.allNonsyncedAreGlobal");
                if (allNonsyncedAreGlobal != null)
                {
                    Assert.IsTrue(allNonsyncedAreGlobal.boolValue,
                        "Prefab wiring must keep non-synced generated params global so Clear Preset default keys remain addressable without VF-local renaming.");
                }

                var ignoreSaved = so.FindProperty("content.ignoreSaved");
                if (ignoreSaved != null)
                {
                    Assert.IsFalse(ignoreSaved.boolValue,
                        "Prefab wiring must preserve saved flags from generated parameters asset rather than force-converting them.");
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void W03_PrefabWiring_MissingParameterFallbackGroup_ReturnsDrift202()
        {
            var go = new GameObject("W03_MissingParameterFallback");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                var vf = go.AddComponent<VF.Model.VRCFury>();
                vf.content = new VF.Model.Feature.BrokenFullControllerMissingParameterFallback
                {
                    controllers = new[] { new VF.Model.Feature.ControllerEntry() },
                    menus = new[] { new VF.Model.Feature.MenuEntry() },
                    prms = new[] { new VF.Model.Feature.ParameterEntryWithoutAnyObjectRef() },
                    globalParams = new[] { string.Empty },
                };

                var fxController = AssetDatabase.LoadAssetAtPath<Object>(ASMLiteAssetPaths.FXController);
                var menu = AssetDatabase.LoadAssetAtPath<Object>(ASMLiteAssetPaths.Menu);
                var parameters = AssetDatabase.LoadAssetAtPath<Object>(ASMLiteAssetPaths.ExprParams);

                Assert.IsNotNull(fxController, "W03: generated FX controller asset should exist for drift probe coverage.");
                Assert.IsNotNull(menu, "W03: generated menu asset should exist for drift probe coverage.");
                Assert.IsNotNull(parameters, "W03: generated expression-parameters asset should exist for drift probe coverage.");

                var serializedVf = new SerializedObject(vf);
                serializedVf.Update();

                var diagnostic = ASMLitePrefabCreator.TryApplyFullControllerAssetReferencesWithDiagnostics(
                    serializedVf,
                    component,
                    fxController,
                    menu,
                    parameters);

                ASMLiteTestFixtures.RecordBuildDiagnosticFailure(
                    SuiteName,
                    nameof(W03_PrefabWiring_MissingParameterFallbackGroup_ReturnsDrift202),
                    diagnostic);

                Assert.IsFalse(diagnostic.Success,
                    "W03: missing all parameter object-reference fallback fields must fail closed before FullController writes.");
                Assert.AreEqual(ASMLiteDiagnosticCodes.Drift.MissingParameterFallbackGroup, diagnostic.Code,
                    "W03: missing parameter fallback group must emit deterministic DRIFT-202.");
                Assert.AreEqual(ASMLiteDriftProbe.ParameterFallbackGroupKey, diagnostic.ContextPath,
                    "W03: DRIFT-202 diagnostics must identify the parameter fallback group key.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test, Category("Integration")]
        public void W04_PrefabWiring_SecondRefresh_IsNoOp_WithSingleVrcFuryComponent()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(ctx.Comp.gameObject, ctx.Comp, "W04 First Refresh"),
                    "W04: first live FullController refresh should succeed for a healthy ASM-Lite object.");
                AssertSingleCriticalWiringState(ctx.Comp, "W04 first refresh");

                Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(ctx.Comp.gameObject, ctx.Comp, "W04 Second Refresh"),
                    "W04: second live FullController refresh should succeed as a no-op on unchanged input.");
                AssertSingleCriticalWiringState(ctx.Comp, "W04 second refresh");
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [Test, Category("Integration")]
        public void W05_PrefabWiring_RepeatedRefresh_KeepsGeneratedFxMenuAndParameterRefsStable()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            try
            {
                Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(ctx.Comp.gameObject, ctx.Comp, "W05 First Refresh"),
                    "W05: first live FullController refresh should succeed for repeated-refresh characterization.");

                var firstSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(ctx.Comp, 0);
                var firstVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(ctx.Comp.gameObject);
                Assert.IsNotNull(firstVf,
                    "W05: first refresh should leave a live VF.Model.VRCFury component on the ASM-Lite object.");
                var firstGlobalParams = ASMLiteTestFixtures.ReadSerializedStringArray(firstVf, "content.globalParams");

                Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(ctx.Comp.gameObject, ctx.Comp, "W05 Second Refresh"),
                    "W05: second live FullController refresh should succeed for repeated-refresh characterization.");

                var secondSnapshot = ASMLiteGeneratedOutputSnapshot.Capture(ctx.Comp, 0);
                var secondVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(ctx.Comp.gameObject);
                Assert.IsNotNull(secondVf,
                    "W05: second refresh should keep a live VF.Model.VRCFury component on the ASM-Lite object.");
                var secondGlobalParams = ASMLiteTestFixtures.ReadSerializedStringArray(secondVf, "content.globalParams");

                AssertSingleCriticalWiringState(ctx.Comp, "W05 second refresh");
                Assert.AreEqual(firstSnapshot.ControllerReferencePath, secondSnapshot.ControllerReferencePath,
                    "W05: repeated refresh should keep the generated FX controller reference stable.");
                Assert.AreEqual(firstSnapshot.MenuReferencePath, secondSnapshot.MenuReferencePath,
                    "W05: repeated refresh should keep the generated menu reference stable.");
                Assert.AreEqual(firstSnapshot.ParameterReferenceResolvedPath, secondSnapshot.ParameterReferenceResolvedPath,
                    "W05: repeated refresh should keep the selected parameter fallback path stable.");
                Assert.AreEqual(firstSnapshot.ParameterReferenceAssetPath, secondSnapshot.ParameterReferenceAssetPath,
                    "W05: repeated refresh should keep the generated expression-parameters reference stable.");
                CollectionAssert.AreEqual(firstGlobalParams, secondGlobalParams,
                    "W05: repeated refresh should keep wildcard global parameter enrollment stable.");
                CollectionAssert.AreEqual(new[] { "*" }, secondGlobalParams,
                    "W05: repeated refresh should keep exactly one wildcard global parameter enrollment entry.");
            }
            finally
            {
                ASMLiteTestFixtures.TearDownTestAvatar(ctx?.AvatarGo);
            }
        }

        [Test]
        public void W02_HasStalePrmsEntry_DetectsLegacyPrmsNames_AndIgnoresOtherNames()
        {
            var root = new GameObject("Root");
            try
            {
                Assert.IsFalse(ASMLitePrefabCreator.HasStalePrmsEntry(root),
                    "No stale entry should be detected when there are no child transforms.");

                var neutral = new GameObject("NotPrms");
                neutral.transform.SetParent(root.transform);
                Assert.IsFalse(ASMLitePrefabCreator.HasStalePrmsEntry(root),
                    "Neutral child names should not trigger stale prms detection.");

                var lowerCasePrms = new GameObject("prms");
                lowerCasePrms.transform.SetParent(root.transform);
                Assert.IsTrue(ASMLitePrefabCreator.HasStalePrmsEntry(root),
                    "Lowercase 'prms' child must trigger stale entry detection.");

                Object.DestroyImmediate(lowerCasePrms);
                var prefixedPrms = new GameObject("ASMLite_prms");
                prefixedPrms.transform.SetParent(root.transform);
                Assert.IsTrue(ASMLitePrefabCreator.HasStalePrmsEntry(root),
                    "Prefixed 'ASMLite_prms' child must trigger stale entry detection.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AssertSingleCriticalWiringState(ASMLiteComponent component, string aid)
        {
            var liveVrcFuryComponents = ASMLiteTestFixtures.FindLiveVrcFuryComponents(component != null ? component.gameObject : null);
            Assert.AreEqual(1, liveVrcFuryComponents.Length,
                $"{aid}: repeated refresh should leave exactly one live VF.Model.VRCFury component on the ASM-Lite object.");

            var vf = liveVrcFuryComponents.Single();
            var serializedVf = new SerializedObject(vf);
            serializedVf.Update();

            var controllerArray = serializedVf.FindProperty(ASMLiteDriftProbe.ControllersArrayPath);
            var menuArray = serializedVf.FindProperty(ASMLiteDriftProbe.MenuArrayPath);
            var parameterArray = serializedVf.FindProperty(ASMLiteDriftProbe.ParametersArrayPath);
            Assert.IsNotNull(controllerArray,
                $"{aid}: repeated refresh should expose the FullController controllers array.");
            Assert.IsNotNull(menuArray,
                $"{aid}: repeated refresh should expose the FullController menus array.");
            Assert.IsNotNull(parameterArray,
                $"{aid}: repeated refresh should expose the FullController prms array.");
            Assert.AreEqual(1, controllerArray.arraySize,
                $"{aid}: repeated refresh should keep exactly one FullController controller entry.");
            Assert.AreEqual(1, menuArray.arraySize,
                $"{aid}: repeated refresh should keep exactly one FullController menu entry.");
            Assert.AreEqual(1, parameterArray.arraySize,
                $"{aid}: repeated refresh should keep exactly one FullController parameter entry.");

            var controllerReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath);
            var menuReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath);
            Assert.IsTrue(controllerReference.HasReference,
                $"{aid}: repeated refresh should keep the FullController controller reference populated.");
            Assert.IsTrue(menuReference.HasReference,
                $"{aid}: repeated refresh should keep the FullController menu reference populated.");

            int populatedParameterFallbackMembers = new[]
            {
                ASMLiteDriftProbe.ParametersObjectRefPath,
                ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath,
            }.Count(path => ASMLiteTestFixtures.ReadSerializedObjectReference(vf, path).HasReference);
            Assert.AreEqual(1, populatedParameterFallbackMembers,
                $"{aid}: repeated refresh should keep exactly one populated parameter fallback-group member.");
        }
    }
}
