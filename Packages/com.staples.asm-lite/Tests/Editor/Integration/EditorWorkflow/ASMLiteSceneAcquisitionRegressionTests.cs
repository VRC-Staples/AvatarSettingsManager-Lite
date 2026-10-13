using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteSceneAcquisitionRegressionTests
    {
        [TestCase("Assets/Missing.unity", "SETUP_SCENE_MISSING", "scene could not be found", TestName = "SceneAcquisition_RejectsMissingScene")]
        [TestCase("Packages/com.staples.asm-lite/package.json", "SETUP_SCENE_PATH_INVALID", "not a Unity scene", TestName = "SceneAcquisition_RejectsNonSceneFile")]
        public void SceneAcquisitionRegression_RejectsInvalidPath(string path, string code, string message)
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "open-scene", new ASMLiteSmokeStepArgs(), path, "CanonicalDress",
                    out string detail, out string stackTrace), Is.False);
            StringAssert.Contains(code, detail);
            StringAssert.Contains(message, detail);
            Assert.That(stackTrace, Is.Empty);
        }

    }
}
