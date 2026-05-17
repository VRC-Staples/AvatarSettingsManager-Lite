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
        public void UploadPreprocess_CallbackOrdersMatchKnownSdkSequence()
        {
            var buildRequested = new ASMLite.Editor.ASMLiteToggleBuildRequestedCallback();
            var togglePreprocess = new ASMLite.Editor.ASMLiteTogglePreprocessAvatarCallback();
            int sdkComponentPreprocessOrder = ResolveSdkComponentPreprocessCallbackOrder();

            Assert.AreEqual(int.MinValue + 1, buildRequested.callbackOrder,
                "build-request enrollment must stay immediately after the SDK/VRCFury int.MinValue guard slot.");
            Assert.AreEqual(-10001, togglePreprocess.callbackOrder,
                "toggle enrollment must stay at the neighboring SDK preprocess boundary used around VRCFury/NDMF hooks.");
            Assert.AreEqual(-2048, sdkComponentPreprocessOrder,
                "the SDK component-preprocess bridge order is the boundary where IPreprocessCallbackBehaviour components run.");

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
        public void VrChatSdk_PostprocessAvatarCallback_IsAvailableForDeferredCleanup()
        {
            var postprocessType = typeof(IVRCSDKPostprocessAvatarCallback);

            var orderedProperty = postprocessType.GetProperty("callbackOrder")
                ?? postprocessType.GetInterfaces()
                    .Select(type => type.GetProperty("callbackOrder"))
                    .FirstOrDefault(property => property != null);

            Assert.IsNotNull(orderedProperty,
                "postprocess callbacks expose callbackOrder and can be sorted after other SDK postprocess cleanup.");
            Assert.IsNotNull(postprocessType.GetMethod("OnPostprocessAvatar", Type.EmptyTypes),
                "postprocess avatar callbacks expose a no-argument cleanup hook after avatar preprocessing.");
        }

        private static int ResolveSdkComponentPreprocessCallbackOrder()
        {
            Type bridgeType = GetLoadableTypes()
                .FirstOrDefault(type => string.Equals(type.Name, "PreprocessCallbackBehaviours", StringComparison.Ordinal));
            Assert.IsNotNull(bridgeType,
                "VRCSDKBase-Editor should provide the component-preprocess bridge that invokes IPreprocessCallbackBehaviour components.");

            var callback = Activator.CreateInstance(bridgeType, nonPublic: true);
            var orderProperty = bridgeType.GetProperty("callbackOrder");
            Assert.IsNotNull(orderProperty,
                "the component-preprocess bridge must expose callbackOrder through the SDK ordered callback contract.");

            return (int)orderProperty.GetValue(callback);
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
