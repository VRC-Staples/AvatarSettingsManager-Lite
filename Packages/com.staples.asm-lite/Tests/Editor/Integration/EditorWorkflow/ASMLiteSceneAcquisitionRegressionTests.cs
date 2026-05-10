using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSceneAcquisitionRegressionTests
    {
        [Test]
        public void SceneAcquisitionRegression_RejectsMissingScenePath()
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "open-scene",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Missing.unity",
                    "Oct25_Dress",
                    out string detail,
                    out string stackTrace),
                Is.False);
            StringAssert.Contains("SETUP_SCENE_MISSING", detail);
            StringAssert.Contains("scene could not be found", detail);
            Assert.That(stackTrace, Is.Empty);
        }

        [Test]
        public void SceneAcquisitionRegression_RejectsNonScenePath()
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "open-scene",
                    new ASMLiteSmokeStepArgs(),
                    "Packages/com.staples.asm-lite/package.json",
                    "Oct25_Dress",
                    out string detail,
                    out string stackTrace),
                Is.False);
            StringAssert.Contains("SETUP_SCENE_PATH_INVALID", detail);
            StringAssert.Contains("not a Unity scene", detail);
            Assert.That(stackTrace, Is.Empty);
        }
    }
}