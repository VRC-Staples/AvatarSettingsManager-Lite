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
            var originalSetup = EditorSceneManager.GetSceneManagerSetup();
            AsmLiteFixtureIsolationScope scope = AsmLiteFixtureIsolationScope.Capture(nameof(FixtureIsolationScope_Dispose_WhenOpenScenesChange_ReportsOpenSceneLeakAndRestoresSetup));

            try
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);

                var failure = Assert.Throws<AssertionException>(() => scope.Dispose());
                scope = null;
                StringAssert.Contains("open scene leak", failure.Message);
                Assert.AreEqual(originalSetup.Length, EditorSceneManager.GetSceneManagerSetup().Length,
                    "Fixture isolation scope should restore the prior scene setup before reporting contamination.");
            }
            finally
            {
                if (originalSetup != null && originalSetup.Length > 0)
                    EditorSceneManager.RestoreSceneManagerSetup(originalSetup);
                scope?.Dispose();
            }
        }

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(ASMLiteTestFixtures.TempDir))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");
        }
    }
}
