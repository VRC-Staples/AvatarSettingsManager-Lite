using NUnit.Framework;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
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
                Assert.DoesNotThrow(() => component.OnPreprocess(),
                    "Preprocess callback should fail closed without throwing when avatar context is incomplete.");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
