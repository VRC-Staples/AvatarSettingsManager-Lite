using System.IO;
using NUnit.Framework;
using UnityEditor;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLitePrefabContractTests
    {
        [Test, Category("Integration")]
        public void T02_Prefab_UsesGeneratedAssetReferences_ForFullController()
        {
            var prefabPath = ASMLiteAssetPaths.Prefab;
            var fullPath = Path.GetFullPath(prefabPath);
            Assert.IsTrue(File.Exists(fullPath), $"Prefab file not found at '{fullPath}'.");

            var yaml = File.ReadAllText(fullPath);

            var fxGuid = AssetDatabase.AssetPathToGUID(ASMLiteAssetPaths.FXController);
            var menuGuid = AssetDatabase.AssetPathToGUID(ASMLiteAssetPaths.Menu);
            var paramsGuid = AssetDatabase.AssetPathToGUID(ASMLiteAssetPaths.ExprParams);

            bool hasRealVfPayload = yaml.Contains("type: {class: FullController, ns: VF.Model.Feature, asm: VRCFury}");
            bool hasTestStubPayload = yaml.Contains("type: {class: FullController, ns: VF.Model.Feature, asm: ASMLite.Tests.Editor}");
            Assert.IsTrue(hasRealVfPayload || hasTestStubPayload,
                "Prefab must include a FullController payload using either real VRCFury assembly metadata or CI test-stub assembly metadata.");
            StringAssert.Contains("globalParams:", yaml,
                "Prefab FullController must declare globalParams.");
            Assert.IsTrue(
                yaml.Contains("- \"*\"") || yaml.Contains("- '*'"),
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
    }
}
