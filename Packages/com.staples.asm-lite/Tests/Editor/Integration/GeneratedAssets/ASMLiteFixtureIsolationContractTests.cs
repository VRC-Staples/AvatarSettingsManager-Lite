using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteFixtureIsolationContractTests
    {
        [Test]
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

        [Test]
        public void FixtureIsolationScope_Dispose_WhenGeneratedAssetAppearsUnderFixtureRoots_ReportsGeneratedAssetLeak()
        {
            AsmLiteFixtureIsolationScope scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Dispose_WhenGeneratedAssetAppearsUnderFixtureRoots_ReportsGeneratedAssetLeak));
            const string assetPath = "Assets/ASMLiteTests_Temp/IsolationSentinel.asset";

            try
            {
                EnsureTempFolder();
                var material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, assetPath);
                AssetDatabase.SaveAssets();

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

        [Test]
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

        [Test]
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

        [Test]
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
            if (!AssetDatabase.IsValidFolder(ASMLiteTestFixtures.TempDir))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");
        }
    }
}
