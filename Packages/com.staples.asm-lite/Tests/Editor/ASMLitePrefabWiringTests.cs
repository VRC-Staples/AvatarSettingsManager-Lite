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
    }
}
