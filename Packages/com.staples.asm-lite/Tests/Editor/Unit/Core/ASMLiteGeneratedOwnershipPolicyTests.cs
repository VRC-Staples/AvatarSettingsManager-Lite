using NUnit.Framework;
using UnityEditor;
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
        public void RootMenuControlPolicy_RequiresGeneratedSubmenuReference()
        {
            var generatedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteGeneratedOwnershipPolicy.GeneratedPresetsMenuPath);
            Assert.IsNotNull(generatedMenu, "Expected package-generated menu fixture to be importable.");

            Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl(new VRCExpressionsMenu.Control
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

        [TestCase(ASMLiteBuilder.DefaultRootControlName)]
        [TestCase("Inventory")]
        [TestCase("Renamed Wrapper")]
        public void RootMenuControlPolicy_PreservesUserLabelsAndPresetsFilename(string label)
        {
            string path = ASMLiteTestFixtures.TempDir + "/ASMLite_Presets_Menu.asset";
            ASMLiteTestFixtures.EnsureFixtureTempDir();
            var userMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            Assert.IsNull(AssetDatabase.LoadMainAssetAtPath(path), "Do not replace a pre-existing fixture asset.");
            AssetDatabase.CreateAsset(userMenu, path);
            try
            {
                Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedPresetsMenuPath(path));
                Assert.IsFalse(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl(new VRCExpressionsMenu.Control
                {
                    name = label,
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = userMenu,
                }));
            }
            finally
            {
                Assert.IsTrue(AssetDatabase.DeleteAsset(path));
            }
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



    }
}
