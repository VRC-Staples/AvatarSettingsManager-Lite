using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using NUnit.Framework;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;

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

        public static void AddExpressionParam(
            AsmLiteTestContext ctx,
            string name,
            VRCExpressionParameters.ValueType valueType,
            float defaultValue = 0f,
            bool saved = true,
            bool networkSynced = true)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRCExpressionParameters.Parameter[0];
            var updated = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = valueType,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = networkSynced,
            };

            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        public static void SetExpressionParams(AsmLiteTestContext ctx, params VRCExpressionParameters.Parameter[] parameters)
        {
            Assert.IsNotNull(ctx, "SetExpressionParams requires a valid test context.");

            if (ctx.ParamsAsset == null && ctx.AvDesc != null && ctx.AvDesc.expressionParameters != null)
                ctx.ParamsAsset = ctx.AvDesc.expressionParameters;

            if (ctx.ParamsAsset == null)
            {
                var fallbackParamsAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                fallbackParamsAsset.parameters = new VRCExpressionParameters.Parameter[0];
                ctx.ParamsAsset = fallbackParamsAsset;

                if (ctx.AvDesc != null)
                {
                    ctx.AvDesc.expressionParameters = fallbackParamsAsset;
                    EditorUtility.SetDirty(ctx.AvDesc);
                }
            }

            Assert.IsTrue(ctx.ParamsAsset != null,
                "SetExpressionParams requires a live ParamsAsset reference. Ensure CreateTestAvatar() completed and fixture assets were not torn down before this helper call.");

            ctx.ParamsAsset.parameters = parameters ?? new VRCExpressionParameters.Parameter[0];
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        public static GameObject CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name ?? "Child");
            if (parent != null)
                child.transform.SetParent(parent.transform);
            return child;
        }

        internal static VF.Model.VRCFury EnsureLiveFullControllerPayload(ASMLiteComponent component)
        {
            if (component == null)
                return null;

            var vf = component.GetComponent<VF.Model.VRCFury>();
            if (vf == null)
                vf = component.gameObject.AddComponent<VF.Model.VRCFury>();

            vf.content = new VF.Model.Feature.FullController
            {
                menus = new[]
                {
                    new VF.Model.Feature.MenuEntry()
                }
            };

            return vf;
        }

        internal static string ReadSerializedMenuPrefix(VF.Model.VRCFury vf)
        {
            var serializedVf = new SerializedObject(vf);
            serializedVf.Update();

            var prefixProperty = serializedVf.FindProperty("content.menus.Array.data[0].prefix");
            Assert.IsNotNull(prefixProperty,
                "Expected serialized FullController menu prefix field at content.menus.Array.data[0].prefix.");

            return prefixProperty.stringValue;
        }

        public static void TearDownTestAvatar(GameObject avatarGo)
        {
            AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.Refresh();
            if (avatarGo != null)
                Object.DestroyImmediate(avatarGo);
        }

        public static void ResetGeneratedExprParams()
        {
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (generatedExpr == null)
                return;
            generatedExpr.parameters = new VRCExpressionParameters.Parameter[0];
            EditorUtility.SetDirty(generatedExpr);
            AssetDatabase.SaveAssets();
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
