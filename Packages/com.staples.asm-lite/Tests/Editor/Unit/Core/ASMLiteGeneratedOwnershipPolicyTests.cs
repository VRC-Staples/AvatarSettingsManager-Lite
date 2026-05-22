using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteGeneratedOwnershipPolicyTests
    {

        [Test]
        public void GeneratedRuntimeNamePolicy_CoversDirectDeliveryMarkers()
        {
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName("ASMLite_Bak_S1_Smile"));
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(ASMLiteBuilder.CtrlParam));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName("User_ASMLite_Bak_S1_Smile"));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(""));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRuntimeName(null));
        }

        [Test]
        public void PathPolicy_NormalizesSlashesAndMatchesWholePrefixSegments()
        {
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.PathStartsWith("Assets/ASM-Lite/Avatar/GeneratedAssets/Menu.asset", "Assets\\ASM-Lite"));
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.PathStartsWith("Assets/ASM-Lite", "Assets/ASM-Lite"));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.PathStartsWith("Assets/ASM-LiteExtra/Menu.asset", "Assets/ASM-Lite"));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.PathStartsWith(null, "Assets/ASM-Lite"));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.PathStartsWith("Assets/ASM-Lite/Menu.asset", null));
        }

        [Test]
        public void ReferenceClassificationPolicy_SeparatesPackageVendorizedAndDirectDeliveryMarkers()
        {
            Assert.AreEqual(
                ASMLiteGeneratedReferenceKind.PackageManaged,
                ASMLiteGeneratedOwnershipPolicy.ClassifyAssetPath(ASMLiteAssetPaths.GeneratedDir + "/ASMLite_FX.controller"));
            Assert.AreEqual(
                ASMLiteGeneratedReferenceKind.Vendorized,
                ASMLiteGeneratedOwnershipPolicy.ClassifyAssetPath("Assets/ASM-Lite/Avatar/GeneratedAssets/ASMLite_FX.controller"));
            Assert.AreEqual(
                ASMLiteGeneratedReferenceKind.DirectDeliveryMarker,
                ASMLiteGeneratedOwnershipPolicy.ClassifyAssetPath("Assets/Avatar/Menus/ASMLite_Direct_Menu.asset"));
            Assert.AreEqual(
                ASMLiteGeneratedReferenceKind.None,
                ASMLiteGeneratedOwnershipPolicy.ClassifyAssetPath("Assets/Avatar/Menus/User_Menu.asset"));
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedMenuAssetPath("Assets/Avatar/Menus/ASMLite_Direct_Menu.asset"));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedMenuAssetPath("Assets/Avatar/Menus/User_Menu.asset"));
        }

        [Test]
        public void RootMenuControlPolicy_MatchesGeneratedNameAndPresetsMenuFilenameOnlyForSubmenus()
        {
            var generatedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteGeneratedOwnershipPolicy.GeneratedPresetsMenuPath);
            Assert.IsNotNull(generatedMenu, "Expected package-generated menu fixture to be importable.");

            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl(new VRCExpressionsMenu.Control
            {
                name = ASMLiteBuilder.DefaultRootControlName,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            }));
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl(new VRCExpressionsMenu.Control
            {
                name = "Renamed Wrapper",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = generatedMenu,
            }));
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl(new VRCExpressionsMenu.Control
            {
                name = ASMLiteBuilder.DefaultRootControlName,
                type = VRCExpressionsMenu.Control.ControlType.Toggle,
            }));
        }

        [Test]
        public void InjectedRootMenuPolicy_PreservesPresetsFilenameOutsideGeneratedPathWhenNameDoesNotMatch()
        {
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedPresetsMenuFileName(
                "Assets/User/Menus/ASMLite_Presets_Menu.asset"),
                "Cleanup keeps the historical broad filename predicate for stale generated root menu entries.");

            var userMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();

            var renamedWrapper = new VRCExpressionsMenu.Control
            {
                name = "Renamed Wrapper",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = userMenu,
            };

            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsInjectedRootMenuControl(renamedWrapper, "Other ASM-Lite Root"),
                "Build-time injection should preserve the previous exact generated-path/name behavior for non-package menus.");
        }

        [Test]
        public void RuntimeMarkerPolicy_DetectsDirectDeliverySubmenuAssetMarkers()
        {
            var avatarGo = new GameObject("OwnershipPolicyAvatar");
            var avatar = avatarGo.AddComponent<VRCAvatarDescriptor>();
            var rootMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var generatedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteGeneratedOwnershipPolicy.GeneratedPresetsMenuPath);

            try
            {
                Assert.IsNotNull(generatedMenu, "Expected package-generated menu fixture to be importable.");
                rootMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Generated Package Menu",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = generatedMenu,
                });
                avatar.expressionsMenu = rootMenu;

                Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.HasRuntimeMarkers(avatar));
            }
            finally
            {
                Object.DestroyImmediate(avatarGo);
            }
        }

        [Test]
        public void DescriptorReferencePolicy_TracesNestedMenuGraphsWithoutLooping()
        {
            string generatedRoot = ASMLiteAssetPaths.GeneratedDir;

            var avatarGo = new GameObject("OwnershipPolicyAvatar");
            var avatar = avatarGo.AddComponent<VRCAvatarDescriptor>();
            var rootMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var childMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var generatedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteGeneratedOwnershipPolicy.GeneratedPresetsMenuPath);
            var parameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);

            try
            {
                Assert.IsNotNull(generatedMenu, "Expected package-generated menu fixture to be importable.");
                Assert.IsNotNull(parameters, "Expected package-generated parameters fixture to be importable.");
                Assert.IsNotNull(controller, "Expected package-generated FX controller fixture to be importable.");

                childMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Loop",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = rootMenu,
                });
                childMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Generated",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = generatedMenu,
                });
                rootMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Child",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = childMenu,
                });
                avatar.expressionsMenu = rootMenu;
                avatar.expressionParameters = parameters;
                avatar.baseAnimationLayers = new[]
                {
                    new VRCAvatarDescriptor.CustomAnimLayer
                    {
                        type = VRCAvatarDescriptor.AnimLayerType.FX,
                        animatorController = controller,
                    }
                };

                Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.MenuReferencesPrefix(rootMenu, generatedRoot));
                Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.HasDescriptorGeneratedReferencesUnderPrefix(avatar, generatedRoot));
            }
            finally
            {
                Object.DestroyImmediate(avatarGo);
            }
        }


    }
}
