using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteUploadPreprocessCleanupStrategyTests
    {
        [Test]
        public void UploadPreprocess_CallbackOrdersMatchAsmLiteBoundaries()
        {
            var buildRequested = new ASMLite.Editor.ASMLiteToggleBuildRequestedCallback();
            var togglePreprocess = new ASMLite.Editor.ASMLiteTogglePreprocessAvatarCallback();

            Assert.AreEqual(int.MinValue + 1, buildRequested.callbackOrder,
                "build-request enrollment must stay immediately after the SDK/VRCFury int.MinValue guard slot.");
            Assert.AreEqual(-10001, togglePreprocess.callbackOrder,
                "toggle enrollment must stay at the neighboring SDK preprocess boundary used around VRCFury/NDMF hooks.");

            var go = new GameObject("ASMLiteUploadPreprocessOrder");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                Assert.AreEqual(-10, component.PreprocessOrder,
                    "ASM-Lite's package-output rebuild remains inside the SDK component-preprocess bridge.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void VrChatSdk_PostprocessAvatarCallback_IsNotUsedForDeferredCleanup()
        {
            var postprocessType = GetLoadableTypes()
                .FirstOrDefault(type => string.Equals(type.Name, "IVRCSDKPostprocessAvatarCallback", StringComparison.Ordinal));

            if (postprocessType == null)
                return;

            var asmLitePostprocessCallbacks = GetLoadableTypes()
                .Where(type => string.Equals(type.Namespace, "ASMLite.Editor", StringComparison.Ordinal)
                    && postprocessType.IsAssignableFrom(type))
                .Select(type => type.FullName)
                .ToArray();

            Assert.AreEqual(0, asmLitePostprocessCallbacks.Length,
                "ASM-Lite package-output cleanup must remain covered by preprocess/lifecycle restore paths, not a deferred SDK postprocess callback: "
                + string.Join(", ", asmLitePostprocessCallbacks));
        }

        private static IEnumerable<Type> GetLoadableTypes()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (System.Reflection.ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(type => type != null).ToArray();
                }

                foreach (var type in types)
                    yield return type;
            }
        }
    }
}
