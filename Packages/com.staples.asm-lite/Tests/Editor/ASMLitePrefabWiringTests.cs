using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLitePrefabWiringTests
    {
        [Test, Category("Integration")]
        public void W01_Prefab_UsesGeneratedAssetReferences_ForFullController()
        {
            var prefabPath = ASMLiteAssetPaths.Prefab;
            var fullPath = Path.GetFullPath(prefabPath);
            Assert.IsTrue(File.Exists(fullPath), $"Prefab file not found at '{fullPath}'.");

            var yaml = File.ReadAllText(fullPath);

            var fxGuid = AssetDatabase.AssetPathToGUID(ASMLiteAssetPaths.FXController);
            var menuGuid = AssetDatabase.AssetPathToGUID(ASMLiteAssetPaths.Menu);
            var paramsGuid = AssetDatabase.AssetPathToGUID(ASMLiteAssetPaths.ExprParams);

            StringAssert.Contains("type: {class: FullController, ns: VF.Model.Feature, asm: VRCFury}", yaml,
                "Prefab must include a VRCFury FullController payload.");
            StringAssert.Contains("globalParams:", yaml,
                "Prefab FullController must declare globalParams.");
            StringAssert.Contains("- \"*\"", yaml,
                "Prefab FullController must bind wildcard global params to avoid VF-local name isolation.");
            StringAssert.Contains("prms:", yaml,
                "Prefab FullController must declare prms wiring.");
            StringAssert.Contains("- parameters:", yaml,
                "Prefab FullController prms must include at least one parameters entry.");

            StringAssert.Contains($"objRef: {{fileID: 9100000, guid: {fxGuid}, type: 2}}", yaml,
                "Prefab must reference the generated FX controller asset.");
            StringAssert.Contains($"objRef: {{fileID: 11400000, guid: {menuGuid}, type: 2}}", yaml,
                "Prefab must reference the generated expressions menu asset.");
            StringAssert.Contains($"objRef: {{fileID: 11400000, guid: {paramsGuid}, type: 2}}", yaml,
                "Prefab must reference the generated expression parameters asset.");
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
