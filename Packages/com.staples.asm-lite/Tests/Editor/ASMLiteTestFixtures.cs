using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Shared test fixtures for ASMLite integration tests.
    /// Call CreateTestAvatar() to get a fully-wired avatar, TearDownTestAvatar() to clean up.
    /// </summary>
    public static class ASMLiteTestFixtures
    {
        private const string TempDir = "Assets/ASMLiteTests_Temp";

        public static AsmLiteTestContext CreateTestAvatar()
        {
            // Create temp directory (guard against already existing)
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");

            // Create AnimatorController
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(
                TempDir + "/TestFX.controller");

            // Create VRCExpressionParameters
            var paramsAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            paramsAsset.parameters = new VRCExpressionParameters.Parameter[0];
            AssetDatabase.CreateAsset(paramsAsset, TempDir + "/TestParams.asset");

            // Create VRCExpressionsMenu
            var menuAsset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menuAsset.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menuAsset, TempDir + "/TestMenu.asset");

            AssetDatabase.SaveAssets();

            // Create avatar GameObject
            var avatarGo = new GameObject("TestAvatar");
            var avDesc = avatarGo.AddComponent<VRCAvatarDescriptor>();

            // Wire FX layer -- resize to at least 5 slots rather than indexing blindly
            var layers = avDesc.baseAnimationLayers;
            if (layers == null || layers.Length < 5)
            {
                var newLayers = new VRCAvatarDescriptor.CustomAnimLayer[5];
                if (layers != null)
                    for (int i = 0; i < layers.Length; i++)
                        newLayers[i] = layers[i];
                // Slot 4 is FX
                newLayers[4] = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    isEnabled = true,
                    animatorController = ctrl
                };
                avDesc.baseAnimationLayers = newLayers;
            }
            else
            {
                layers[4].animatorController = ctrl;
                layers[4].isDefault = false;
                layers[4].isEnabled = true;
                avDesc.baseAnimationLayers = layers;
            }

            avDesc.expressionParameters = paramsAsset;
            avDesc.expressionsMenu = menuAsset;

            // Add ASMLiteComponent as child
            var compGo = new GameObject("ASMLite");
            compGo.transform.SetParent(avatarGo.transform);
            var comp = compGo.AddComponent<ASMLiteComponent>();

            return new AsmLiteTestContext
            {
                AvatarGo = avatarGo,
                AvDesc = avDesc,
                Comp = comp,
                Ctrl = ctrl,
                ParamsAsset = paramsAsset,
                MenuAsset = menuAsset
            };
        }

        public static void TearDownTestAvatar(GameObject avatarGo)
        {
            AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.Refresh();
            if (avatarGo != null)
                Object.DestroyImmediate(avatarGo);
        }
    }

    /// <summary>
    /// Holds all objects created by CreateTestAvatar().
    /// </summary>
    public class AsmLiteTestContext
    {
        public GameObject AvatarGo;
        public VRCAvatarDescriptor AvDesc;
        public ASMLiteComponent Comp;
        public AnimatorController Ctrl;
        public VRCExpressionParameters ParamsAsset;
        public VRCExpressionsMenu MenuAsset;
    }
}
