using System.IO;
using ASMLite.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public sealed class ASMLiteFullControllerWiringTests
    {
        private const string TempRoot = "Assets/ASMLiteTests_Temp";
        private const string RetargetMirrorFolder = "FullControllerWiringRetarget";

        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
            string mirrorDir = TempRoot + "/" + RetargetMirrorFolder;
            if (AssetDatabase.IsValidFolder(mirrorDir))
                AssetDatabase.DeleteAsset(mirrorDir);

            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        [Test]
        public void RefreshLiveFullControllerWiring_CreatesVrcFuryPayloadThroughDedicatedModule()
        {
            var result = ASMLiteFullControllerWiring.TryRefreshLiveFullControllerWiringWithDiagnostics(
                _ctx.Comp.gameObject,
                _ctx.Comp,
                "FullController Wiring Test Refresh");

            Assert.IsTrue(result.Success, result.ToLogString());

            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf, "Refresh should create a live VF.Model.VRCFury component through the dedicated wiring module.");
            AssertFullControllerReferencesPointAt(ASMLiteAssetPaths.FXController, ASMLiteAssetPaths.Menu, ASMLiteAssetPaths.ExprParams);
        }

        [Test]
        public void CaptureLiveFullControllerReferenceSnapshot_ReadsReferencesThroughDedicatedModule()
        {
            var refreshResult = ASMLiteFullControllerWiring.TryRefreshLiveFullControllerWiringWithDiagnostics(
                _ctx.Comp.gameObject,
                _ctx.Comp,
                "FullController Wiring Test Snapshot Refresh");
            Assert.IsTrue(refreshResult.Success, refreshResult.ToLogString());

            var snapshotResult = ASMLiteFullControllerWiring.TryCaptureLiveFullControllerReferenceSnapshot(
                _ctx.Comp,
                "FullController Wiring Test Snapshot",
                out ASMLiteFullControllerReferenceSnapshot snapshot);

            Assert.IsTrue(snapshotResult.Success, snapshotResult.ToLogString());
            Assert.AreEqual(ASMLiteAssetPaths.FXController, snapshot.ControllerAssetPath);
            Assert.AreEqual(ASMLiteAssetPaths.Menu, snapshot.MenuAssetPath);
            Assert.AreEqual(ASMLiteAssetPaths.ExprParams, snapshot.ParametersAssetPath);
        }

        [Test]
        public void RetargetLiveFullControllerGeneratedAssets_RepointsReferencesThroughDedicatedModule()
        {
            string mirrorDir = CreateGeneratedAssetMirror(RetargetMirrorFolder);

            var retargetResult = ASMLiteFullControllerWiring.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(
                _ctx.Comp,
                mirrorDir,
                "FullController Wiring Test Retarget");

            Assert.IsTrue(retargetResult.Success, retargetResult.ToLogString());

            var snapshotResult = ASMLiteFullControllerWiring.TryCaptureLiveFullControllerReferenceSnapshot(
                _ctx.Comp,
                "FullController Wiring Test Retarget Snapshot",
                out ASMLiteFullControllerReferenceSnapshot snapshot);

            Assert.IsTrue(snapshotResult.Success, snapshotResult.ToLogString());
            Assert.AreEqual(mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController), snapshot.ControllerAssetPath);
            Assert.AreEqual(mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu), snapshot.MenuAssetPath);
            Assert.AreEqual(mirrorDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams), snapshot.ParametersAssetPath);
        }

        private static string CreateGeneratedAssetMirror(string folderName)
        {
            string root = TempRoot;
            if (!AssetDatabase.IsValidFolder(root))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");

            string mirrorDir = root + "/" + folderName;
            if (AssetDatabase.IsValidFolder(mirrorDir))
                AssetDatabase.DeleteAsset(mirrorDir);

            AssetDatabase.CreateFolder(root, folderName);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.FXController, mirrorDir);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.Menu, mirrorDir);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.ExprParams, mirrorDir);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return mirrorDir;
        }

        private static void CopyPackageAssetToMirror(string sourceAssetPath, string targetFolder)
        {
            string destinationPath = targetFolder + "/" + Path.GetFileName(sourceAssetPath);
            AssetDatabase.DeleteAsset(destinationPath);
            Assert.IsTrue(AssetDatabase.CopyAsset(sourceAssetPath, destinationPath),
                $"Expected to copy '{sourceAssetPath}' to '{destinationPath}' for FullController retargeting seam tests.");
        }

        private void AssertFullControllerReferencesPointAt(string expectedController, string expectedMenu, string expectedParameters)
        {
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf, "Expected a live VF.Model.VRCFury component before reading FullController references.");

            var controllerReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath);
            var menuReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath);
            var parametersReference = ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(
                vf,
                ASMLiteDriftProbe.ParametersObjectRefPath,
                ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath);

            Assert.AreEqual(expectedController, controllerReference.AssetPath);
            Assert.AreEqual(expectedMenu, menuReference.AssetPath);
            Assert.AreEqual(expectedParameters, parametersReference.AssetPath);
        }
    }
}
