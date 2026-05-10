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
    public class ASMLiteGeneratedOwnershipPolicyTests
    {
        private const string TestAssetRoot = "Assets/ASMLiteGeneratedOwnershipPolicyTests";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TestAssetRoot))
                AssetDatabase.DeleteAsset(TestAssetRoot);

            AssetDatabase.Refresh();
        }

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
            EnsureFolder("Assets", "ASMLiteGeneratedOwnershipPolicyTests");
            var generatedMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            AssetDatabase.CreateAsset(generatedMenu, TestAssetRoot + "/ASMLite_Presets_Menu.asset");

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
            EnsureFolder("Assets", "ASMLiteGeneratedOwnershipPolicyTests");
            var generatedMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            AssetDatabase.CreateAsset(generatedMenu, TestAssetRoot + "/ASMLite_Presets_Menu.asset");

            var renamedWrapper = new VRCExpressionsMenu.Control
            {
                name = "Renamed Wrapper",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = generatedMenu,
            };

            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl(renamedWrapper),
                "Cleanup keeps the historical broad filename predicate for stale generated root menu entries.");
            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsInjectedRootMenuControl(renamedWrapper, "Other ASM-Lite Root"),
                "Build-time injection should preserve the previous exact generated-path/name behavior for non-package menus.");
        }

        [Test]
        public void RuntimeMarkerPolicy_DetectsDirectDeliverySubmenuAssetMarkers()
        {
            EnsureFolder("Assets", "ASMLiteGeneratedOwnershipPolicyTests");
            var avatarGo = new GameObject("OwnershipPolicyAvatar");
            var avatar = avatarGo.AddComponent<VRCAvatarDescriptor>();
            var rootMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var directDeliveryMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();

            try
            {
                AssetDatabase.CreateAsset(rootMenu, TestAssetRoot + "/Root.asset");
                AssetDatabase.CreateAsset(directDeliveryMenu, TestAssetRoot + "/ASMLite_Direct_Menu.asset");
                rootMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Direct Delivery",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = directDeliveryMenu,
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
            EnsureFolder("Assets", "ASMLiteGeneratedOwnershipPolicyTests");
            string generatedRoot = EnsureFolder(TestAssetRoot, "GeneratedAssets");

            var avatarGo = new GameObject("OwnershipPolicyAvatar");
            var avatar = avatarGo.AddComponent<VRCAvatarDescriptor>();
            var rootMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var childMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var generatedMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            var parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var controller = new AnimatorController();

            try
            {
                AssetDatabase.CreateAsset(rootMenu, TestAssetRoot + "/Root.asset");
                AssetDatabase.CreateAsset(childMenu, TestAssetRoot + "/Child.asset");
                AssetDatabase.CreateAsset(generatedMenu, generatedRoot + "/ASMLite_Presets_Menu.asset");
                AssetDatabase.CreateAsset(parameters, generatedRoot + "/ASMLite_Parameters.asset");
                AssetDatabase.CreateAsset(controller, generatedRoot + "/ASMLite_FX.controller");

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
                generatedMenu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "Loop",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = rootMenu,
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

        private static string EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
            return path;
        }
    }
}
