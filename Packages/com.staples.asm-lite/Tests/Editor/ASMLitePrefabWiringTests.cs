using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using ASMLite.Editor;

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

                var vf = go.GetComponent<VF.Model.VRCFury>();
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
