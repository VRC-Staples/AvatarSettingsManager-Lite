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
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
        }

        [TearDown]
        public void TearDown()
        {
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
            string mirrorDir = ASMLiteAssetPaths.GeneratedDir;

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

        [TestCase(0, 1, 0)]
        [TestCase(1, 0, 1)]
        [TestCase(0, 0, 0)]
        public void CaptureRestoreLiveFullControllerReferenceSnapshot_PreservesEmptyArrays(int controllers, int menus, int parameters)
        {
            var refresh = ASMLiteFullControllerWiring.TryRefreshLiveFullControllerWiringWithDiagnostics(
                _ctx.Comp.gameObject, _ctx.Comp, "Empty Enrollment Setup");
            Assert.IsTrue(refresh.Success, refresh.ToLogString());
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            var serialized = new SerializedObject(vf);
            serialized.FindProperty(ASMLiteDriftProbe.ControllersArrayPath).arraySize = controllers;
            serialized.FindProperty(ASMLiteDriftProbe.MenuArrayPath).arraySize = menus;
            serialized.FindProperty(ASMLiteDriftProbe.ParametersArrayPath).arraySize = parameters;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            string baseline = EditorJsonUtility.ToJson(vf);

            var capture = ASMLiteFullControllerWiring.TryCaptureLiveFullControllerReferenceSnapshot(
                _ctx.Comp, "Empty Enrollment Capture", out var snapshot);
            Assert.IsTrue(capture.Success, capture.ToLogString());
            Assert.AreEqual(baseline, EditorJsonUtility.ToJson(vf), "Capture must not populate missing entries.");
            Assert.IsFalse(ASMLiteLifecycleVerification.VerifyAttachedReturnState(
                _ctx.Comp, _ctx.AvDesc, string.Empty, out _, out _),
                "A capturable rollback baseline with empty arrays must not certify completed delivery.");

            var repair = ASMLiteFullControllerWiring.TryWirePackageGeneratedAssetsWithDiagnostics(
                _ctx.Comp, "Empty Enrollment Repair");
            Assert.IsTrue(repair.Success, repair.ToLogString());
            var restore = ASMLiteFullControllerWiring.TryRestoreLiveFullControllerReferenceSnapshot(
                _ctx.Comp, snapshot, "Empty Enrollment Rollback");
            Assert.IsTrue(restore.Success, restore.ToLogString());
            vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.AreEqual(baseline, EditorJsonUtility.ToJson(vf),
                "Rollback must restore the original arrays and references, including empty enrollment.");
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
