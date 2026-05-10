using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteComponentPreprocessTests
    {
        [Test]
        public void PreprocessOrder_RemainsNegativeTen()
        {
            var go = new GameObject("ASMLitePreprocessOrder");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                Assert.AreEqual(-10, component.PreprocessOrder,
                    "Preprocess callback order should remain fixed to preserve ASM-Lite callback sequencing.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void OnPreprocess_NoAvatarContext_DoesNotThrow()
        {
            var go = new GameObject("ASMLitePreprocessNoAvatarContext");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                LogAssert.Expect(LogType.Error,
                    "[ASM-Lite] Build failed: no VRCAvatarDescriptor found in parent hierarchy of 'ASMLitePreprocessNoAvatarContext'.");
                LogAssert.Expect(LogType.Error,
                    "[ASM-Lite] Build diagnostic BUILD-301: [ASM-Lite] Build failed: no VRCAvatarDescriptor found in parent hierarchy of 'ASMLitePreprocessNoAvatarContext'. Context: 'VRCAvatarDescriptor'. Remediation: Attach ASM-Lite under an avatar root that contains VRCAvatarDescriptor.");

                bool result = true;
                Assert.DoesNotThrow(() => result = component.OnPreprocess(),
                    "Preprocess callback should fail closed without throwing when avatar context is incomplete.");
                Assert.IsFalse(result,
                    "Preprocess callback should report failure when avatar context is incomplete.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
