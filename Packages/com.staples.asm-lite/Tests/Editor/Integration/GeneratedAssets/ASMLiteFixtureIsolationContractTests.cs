using System;
using System.Collections;
using System.IO;
using System.Linq;
using ASMLite.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteFixtureIsolationContractTests
    {
        [Test, Category("WindowsEditor")]
        public void FixtureIsolationScope_FailedTemplateRestore_BlocksCaptureUntilExplicitRetry()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
                Assert.Ignore("This regression exercises Windows read-only file restoration.");

            var original = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(nameof(FixtureIsolationScope_FailedTemplateRestore_BlocksCaptureUntilExplicitRetry),
                new[] { ASMLiteAssetPaths.ExprParams });
            var attributes = File.GetAttributes(ASMLiteAssetPaths.ExprParams);
            AsmLiteTestContext context = null;
            AsmLiteFixtureIsolationScope unexpected = null;
            try
            {
                var parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                    {
                        name = "FailedRestoreSentinel", valueType = VRCExpressionParameters.ValueType.Int,
                        defaultValue = 17f, saved = true, networkSynced = false,
                    },
                };
                EditorUtility.SetDirty(parameters);
                AssetDatabase.SaveAssets();
                var baseline = File.ReadAllBytes(ASMLiteAssetPaths.ExprParams);
                var baselineMeta = File.ReadAllBytes(ASMLiteAssetPaths.ExprParams + ".meta");
                context = ASMLiteTestFixtures.CreateTestAvatar();
                ASMLiteTestFixtures.ResetGeneratedExprParams();
                var working = File.ReadAllBytes(ASMLiteAssetPaths.ExprParams);
                File.SetAttributes(ASMLiteAssetPaths.ExprParams, attributes | FileAttributes.ReadOnly);

                Assert.Throws<UnauthorizedAccessException>(() => ASMLiteTestFixtures.TearDownTestAvatar(context.AvatarGo));
                Assert.IsTrue(context.AvatarGo == null, "Failed teardown must not require a still-live avatar to retain its owner.");
                File.SetAttributes(ASMLiteAssetPaths.ExprParams, attributes);
                Assert.Throws<InvalidOperationException>(() => unexpected = AsmLiteFixtureIsolationScope.Capture("After failed teardown"),
                    "A failed restore must block new fixtures, not silently skip restoration forever.");
                CollectionAssert.AreEqual(working, File.ReadAllBytes(ASMLiteAssetPaths.ExprParams),
                    "Admission must not automatically restore or replace the pending baseline.");

                AsmLiteFixtureIsolationScope.RetryFailedCleanup();
                CollectionAssert.AreEqual(baseline, File.ReadAllBytes(ASMLiteAssetPaths.ExprParams));
                CollectionAssert.AreEqual(baselineMeta, File.ReadAllBytes(ASMLiteAssetPaths.ExprParams + ".meta"));
                Assert.AreEqual("FailedRestoreSentinel", AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams).parameters.Single().name);
                unexpected = AsmLiteFixtureIsolationScope.Capture("After explicit retry");
                unexpected.Dispose();
                unexpected = null;
            }
            finally
            {
                File.SetAttributes(ASMLiteAssetPaths.ExprParams, attributes);
                try { unexpected?.Dispose(); }
                finally
                {
                    try { context?.FixtureIsolationScope.Dispose(); }
                    finally { original.Restore(); }
                }
            }
        }

        [Test, Category("Integration")]
        public void FixtureIsolationScope_ResetGeneratedParameters_ConcurrentFixturesRestoreFirstBaseline()
        {
            var original = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(nameof(FixtureIsolationScope_ResetGeneratedParameters_ConcurrentFixturesRestoreFirstBaseline),
                new[] { ASMLiteAssetPaths.ExprParams });
            AsmLiteTestContext first = null;
            AsmLiteTestContext second = null;
            try
            {
                var parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
                var savedBeforeEdit = File.ReadAllBytes(ASMLiteAssetPaths.ExprParams);
                parameters.parameters = new[]
                {
                    new VRCExpressionParameters.Parameter
                    {
                        name = "FixturePreservationSentinel", valueType = VRCExpressionParameters.ValueType.Float,
                        defaultValue = 0.625f, saved = true, networkSynced = false,
                    },
                };
                EditorUtility.SetDirty(parameters);
                Assert.Throws<InvalidOperationException>(() => AsmLiteFixtureIsolationScope.Capture("Unsaved parameter admission"));
                Assert.IsTrue(EditorUtility.IsDirty(parameters), "Rejected capture must preserve unsaved edits.");
                Assert.AreEqual(0.625f, parameters.parameters.Single().defaultValue);
                CollectionAssert.AreEqual(savedBeforeEdit, File.ReadAllBytes(ASMLiteAssetPaths.ExprParams));
                AssetDatabase.SaveAssets();
                var baseline = File.ReadAllBytes(ASMLiteAssetPaths.ExprParams);
                var baselineMeta = File.ReadAllBytes(ASMLiteAssetPaths.ExprParams + ".meta");

                first = ASMLiteTestFixtures.CreateTestAvatar();
                ASMLiteTestFixtures.ResetGeneratedExprParams();
                second = ASMLiteTestFixtures.CreateTestAvatar();
                ASMLiteTestFixtures.ResetGeneratedExprParams();
                Assert.IsEmpty(parameters.parameters, "Reset must still provide empty test parameters.");

                ASMLiteTestFixtures.TearDownTestAvatar(first.AvatarGo);
                first = null;
                Assert.IsEmpty(parameters.parameters, "An active fixture must keep its working parameter state.");
                ASMLiteTestFixtures.TearDownTestAvatar(second.AvatarGo);
                second = null;

                CollectionAssert.AreEqual(baseline, File.ReadAllBytes(ASMLiteAssetPaths.ExprParams),
                    "The last fixture must restore the first baseline, not a later empty snapshot.");
                CollectionAssert.AreEqual(baselineMeta, File.ReadAllBytes(ASMLiteAssetPaths.ExprParams + ".meta"));
                var restored = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams).parameters.Single();
                Assert.AreEqual("FixturePreservationSentinel", restored.name);
                Assert.AreEqual(VRCExpressionParameters.ValueType.Float, restored.valueType);
                Assert.AreEqual(0.625f, restored.defaultValue);
                Assert.IsTrue(restored.saved);
                Assert.IsFalse(restored.networkSynced);
            }
            finally
            {
                try { if (first?.AvatarGo != null) ASMLiteTestFixtures.TearDownTestAvatar(first.AvatarGo); }
                finally
                {
                    try { if (second?.AvatarGo != null) ASMLiteTestFixtures.TearDownTestAvatar(second.AvatarGo); }
                    finally { original.Restore(); }
                }
            }
        }

        [UnityTest, Category("InstalledNdmf")]
        public IEnumerator FixtureIsolationScope_Capture_InitializesDeferredNdmfSceneHelpersBeforeBaseline()
        {
            var activatorType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly.GetName().Name == "nadena.dev.ndmf.runtime")
                .Select(assembly => assembly.GetType("nadena.dev.ndmf.runtime.ApplyOnPlayGlobalActivator"))
                .SingleOrDefault(type => type != null);
            if (activatorType == null)
                Assert.Ignore("This integration regression requires installed NDMF.");

            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            // Unity's EditMode runner can start with an unsaved scene, which blocks additive creation.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AsmLiteFixtureIsolationScope scope = null;
            try
            {
                Assert.IsFalse(scene.GetRootGameObjects().Any(root => root.GetComponent(activatorType) != null),
                    "The scene-created callback must still be deferred before fixture capture.");
                scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Capture_InitializesDeferredNdmfSceneHelpersBeforeBaseline));
                var helper = scene.GetRootGameObjects().Single(root => root.GetComponent(activatorType) != null);
                var helperState = EditorJsonUtility.ToJson(helper);
                yield return null;
                Assert.AreSame(helper, scene.GetRootGameObjects().Single(root => root.GetComponent(activatorType) != null),
                    "Deferred NDMF initialization must reuse the captured helper, not create a late root.");
                Assert.AreEqual(helperState, EditorJsonUtility.ToJson(helper),
                    "Deferred framework setup must preserve the captured helper state.");
                Assert.DoesNotThrow(() => scope.Dispose(),
                    "Expected NDMF setup must precede the baseline without bypassing scene leak detection.");
                scope = null;
            }
            finally
            {
                try { scope?.Dispose(); }
                finally
                {
                    RestoreOriginalSceneSetup(originalSetup);
                }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        [Category("Integration")]
        public void FixtureTeardown_RenamedAvatar_RemovesOriginalWorkingAssetsAndPreservesOtherOwners(bool concurrentFixture)
        {
            if (!AssetDatabase.IsValidFolder("Assets/ASM-Lite"))
                AssetDatabase.CreateFolder("Assets", "ASM-Lite");
            string unrelated = "Assets/ASM-Lite/TestAvatar__Unrelated_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/ASM-Lite", Path.GetFileName(unrelated));
            string sentinelPath = unrelated + "/Preserve.txt";
            File.WriteAllText(sentinelPath, "pre-existing unrelated work");
            AssetDatabase.ImportAsset(sentinelPath, ImportAssetOptions.ForceSynchronousImport);
            string sentinelGuid = AssetDatabase.AssetPathToGUID(sentinelPath);
            AsmLiteTestContext first = null;
            AsmLiteTestContext second = null;
            string firstWorking = null;
            try
            {
                first = ASMLiteTestFixtures.CreateTestAvatar();
                ASMLiteTestFixtures.AddExpressionParam(first, "RenameSentinel", VRCExpressionParameters.ValueType.Bool);
                Assert.GreaterOrEqual(ASMLiteBuilder.Build(first.Comp), 0);
                firstWorking = ASMLiteAssetPaths.ResolveGeneratedDirectory(first.Comp);
                Assert.IsTrue(firstWorking.StartsWith("Assets/ASM-Lite/", StringComparison.Ordinal));
                Assert.IsTrue(AssetDatabase.IsValidFolder(firstWorking));
                string secondWorking = null;
                string secondControllerGuid = null;
                if (concurrentFixture)
                {
                    second = ASMLiteTestFixtures.CreateTestAvatar();
                    ASMLiteTestFixtures.AddExpressionParam(second, "OtherOwner", VRCExpressionParameters.ValueType.Bool);
                    Assert.GreaterOrEqual(ASMLiteBuilder.Build(second.Comp), 0);
                    secondWorking = ASMLiteAssetPaths.ResolveGeneratedDirectory(second.Comp);
                    Assert.AreNotEqual(firstWorking, secondWorking);
                    secondControllerGuid = AssetDatabase.AssetPathToGUID(secondWorking + "/ASMLite_FX.controller");
                }
                first.AvatarGo.name += "(Clone)";
                Assert.DoesNotThrow(() => ASMLiteTestFixtures.TearDownTestAvatar(first.AvatarGo));
                first = null;
                Assert.IsFalse(AssetDatabase.IsValidFolder(firstWorking), "Cleanup must use the recorded owner, not the renamed avatar's new path.");
                Assert.IsFalse(Directory.Exists(firstWorking));
                if (concurrentFixture)
                {
                    Assert.IsTrue(AssetDatabase.IsValidFolder(secondWorking));
                    Assert.AreEqual(secondControllerGuid, AssetDatabase.AssetPathToGUID(secondWorking + "/ASMLite_FX.controller"));
                }
                Assert.AreEqual("pre-existing unrelated work", File.ReadAllText(sentinelPath));
                Assert.AreEqual(sentinelGuid, AssetDatabase.AssetPathToGUID(sentinelPath));
            }
            finally
            {
                try { if (first?.AvatarGo != null) ASMLiteTestFixtures.TearDownTestAvatar(first.AvatarGo); }
                finally
                {
                    try { if (second?.AvatarGo != null) ASMLiteTestFixtures.TearDownTestAvatar(second.AvatarGo); }
                    finally
                    {
                        if (firstWorking != null) AssetDatabase.DeleteAsset(firstWorking);
                        AssetDatabase.DeleteAsset(unrelated);
                    }
                }
            }
        }

        [Test, Category("Integration")]
        public void FixtureIsolationScope_Dispose_WhenAnotherFixtureAvatarIsStillRegistered_DoesNotReportFixtureRootAsSceneLeak()
        {
            AsmLiteTestContext first = null;
            AsmLiteTestContext second = null;

            try
            {
                first = ASMLiteTestFixtures.CreateTestAvatar();
                second = ASMLiteTestFixtures.CreateTestAvatar();
                second.AvatarGo.name = "ConcurrentFixtureAvatar";

                var firstAvatar = first.AvatarGo;
                Assert.DoesNotThrow(() => ASMLiteTestFixtures.TearDownTestAvatar(firstAvatar));
                first = null;
                Assert.IsTrue(second.AvatarGo != null,
                    "Concurrent fixture avatar should remain live until its own teardown.");
            }
            finally
            {
                if (first?.AvatarGo != null)
                    ASMLiteTestFixtures.TearDownTestAvatar(first.AvatarGo);
                if (second?.AvatarGo != null)
                    ASMLiteTestFixtures.TearDownTestAvatar(second.AvatarGo);
            }
        }

        [Test, Category("Integration")]
        public void FixtureIsolationScope_Dispose_WhenGeneratedAssetAppearsUnderFixtureRoots_ReportsGeneratedAssetLeak()
        {
            const string assetPath = "Assets/ASMLiteTests_Temp/IsolationSentinel.asset";
            AssetDatabase.DeleteAsset(assetPath);
            AssetDatabase.DeleteAsset(ASMLiteTestFixtures.TempDir);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);

            AsmLiteFixtureIsolationScope scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Dispose_WhenGeneratedAssetAppearsUnderFixtureRoots_ReportsGeneratedAssetLeak));

            try
            {
                EnsureTempFolder();
                var sentinel = ScriptableObject.CreateInstance<ScriptableObject>();
                AssetDatabase.CreateAsset(sentinel, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath),
                    "setup should create a detectable generated-asset sentinel after fixture isolation capture.");

                var failure = Assert.Throws<AssertionException>(() => scope.Dispose());
                scope = null;
                StringAssert.Contains("generated asset leak", failure.Message);
                StringAssert.Contains(assetPath, failure.Message);
            }
            finally
            {
                AssetDatabase.DeleteAsset(assetPath);
                AssetDatabase.DeleteAsset(ASMLiteTestFixtures.TempDir);
                AssetDatabase.Refresh();
                scope?.Dispose();
            }
        }

        [Test, Category("Integration")]
        public void FixtureIsolationScope_Dispose_WhenSceneObjectSurvivesTeardown_ReportsSceneObjectLeak()
        {
            AsmLiteFixtureIsolationScope scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Dispose_WhenSceneObjectSurvivesTeardown_ReportsSceneObjectLeak));
            GameObject leakedObject = null;

            try
            {
                leakedObject = new GameObject("ASMLiteFixtureIsolationSceneLeakSentinel");

                var failure = Assert.Throws<AssertionException>(() => scope.Dispose());
                scope = null;
                StringAssert.Contains("scene object leak", failure.Message);
                StringAssert.Contains("ASMLiteFixtureIsolationSceneLeakSentinel", failure.Message);
            }
            finally
            {
                if (leakedObject != null)
                    Object.DestroyImmediate(leakedObject);
                scope?.Dispose();
            }
        }

        [Test, Category("Integration")]
        public void FixtureIsolationScope_Dispose_WhenSelectionChanges_ReportsSelectionLeakAndRestoresSelection()
        {
            var originalSelection = Selection.objects;
            AsmLiteFixtureIsolationScope scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Dispose_WhenSelectionChanges_ReportsSelectionLeakAndRestoresSelection));
            ScriptableObject selectedObject = null;

            try
            {
                selectedObject = ScriptableObject.CreateInstance<ScriptableObject>();
                Selection.activeObject = selectedObject;

                var failure = Assert.Throws<AssertionException>(() => scope.Dispose());
                scope = null;
                StringAssert.Contains("selection leak", failure.Message);
                Assert.AreEqual(originalSelection, Selection.objects, "Fixture isolation scope should restore the prior editor selection before reporting contamination.");
            }
            finally
            {
                if (selectedObject != null)
                    Object.DestroyImmediate(selectedObject);
                Selection.objects = originalSelection;
                scope?.Dispose();
            }
        }

        [Test, Category("Integration")]
        public void FixtureIsolationScope_Dispose_WhenOpenScenesChange_ReportsOpenSceneLeakAndRestoresSetup()
        {
            const string baselineScenePath = ASMLiteTestFixtures.TempDir + "/FixtureIsolationBaseline.unity";
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            SceneSetup[] scopedSetup = null;
            AsmLiteFixtureIsolationScope scope = null;

            try
            {
                EnsureTempFolder();
                var baselineScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Assert.IsTrue(EditorSceneManager.SaveScene(baselineScene, baselineScenePath),
                    "Fixture isolation open-scene contract needs a saved baseline scene before opening an additive scene.");
                scopedSetup = EditorSceneManager.GetSceneManagerSetup();
                scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Dispose_WhenOpenScenesChange_ReportsOpenSceneLeakAndRestoresSetup));

                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                var failure = Assert.Throws<AssertionException>(() => scope.Dispose());
                scope = null;
                StringAssert.Contains("open scene leak", failure.Message);
                Assert.AreEqual(scopedSetup.Length, EditorSceneManager.GetSceneManagerSetup().Length,
                    "Fixture isolation scope should restore the prior scene setup before reporting contamination.");
            }
            finally
            {
                scope?.Dispose();
                RestoreOriginalSceneSetup(originalSetup);
                AssetDatabase.DeleteAsset(baselineScenePath);
                AssetDatabase.DeleteAsset(ASMLiteTestFixtures.TempDir);
                AssetDatabase.Refresh();
            }
        }

        private static void RestoreOriginalSceneSetup(SceneSetup[] originalSetup)
        {
            if (CanRestoreSceneSetup(originalSetup))
            {
                EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static bool CanRestoreSceneSetup(SceneSetup[] setup)
        {
            if (setup == null || setup.Length == 0)
                return false;

            foreach (var scene in setup)
            {
                if (string.IsNullOrEmpty(scene.path))
                    return false;
            }

            return true;
        }

        private static void EnsureTempFolder()
        {
            ASMLiteTestFixtures.EnsureFixtureTempDir();
        }
    }
}
