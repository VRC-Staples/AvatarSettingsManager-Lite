using NUnit.Framework;
using UnityEditor;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Integration coverage for generated root wrapper naming overrides.
    /// Verifies effective-name resolution against the generated menu asset.
    /// </summary>
    [TestFixture]
    public class ASMLiteRootMenuOverrideTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "R081: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "R081: fixture did not create ASMLiteComponent.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        private VRCExpressionsMenu.Control BuildAndGetRootControl(string aid)
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult}.");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var rootMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(rootMenu,
                $"{aid}: generated root menu missing at '{ASMLiteAssetPaths.Menu}'.");
            Assert.IsNotNull(rootMenu.controls, $"{aid}: generated root menu controls list is null.");
            Assert.AreEqual(1, rootMenu.controls.Count,
                $"{aid}: generated root menu must contain exactly one wrapper control.");

            var rootControl = rootMenu.controls[0];
            Assert.IsNotNull(rootControl, $"{aid}: generated root wrapper control is null.");
            Assert.AreEqual(VRCExpressionsMenu.Control.ControlType.SubMenu, rootControl.type,
                $"{aid}: generated root wrapper control must be SubMenu.");
            Assert.IsNotNull(rootControl.subMenu,
                $"{aid}: generated root wrapper control must reference presets submenu.");
            return rootControl;
        }

        [Test, Category("Integration")]
        public void R081_DefaultRootName_WhenCustomToggleDisabled()
        {
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = "Creator Override";

            var rootControl = BuildAndGetRootControl("R081-default-disabled");
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, rootControl.name,
                "R081: disabled custom root name must fall back to Settings Manager.");
        }

        [Test, Category("Integration")]
        public void R081_CustomRootName_UsesTrimmedValue_WhenEnabled()
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "   My Presets   ";

            var rootControl = BuildAndGetRootControl("R081-trimmed-enabled");
            Assert.AreEqual("My Presets", rootControl.name,
                "R081: enabled custom root name must trim whitespace before applying.");
        }

        [Test, Category("Integration")]
        public void R081_WhitespaceCustomName_FallsBackToDefault_WhenEnabled()
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "   \t  \n ";

            var rootControl = BuildAndGetRootControl("R081-blank-enabled");
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, rootControl.name,
                "R081: blank/whitespace custom root name must fall back to Settings Manager.");
        }

        [Test, Category("Integration")]
        public void R081_RepeatedBuild_DisabledToggleResetsToDefaultAfterCustomName()
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  CustomOne  ";
            var firstRootControl = BuildAndGetRootControl("R081-rebuild-first");
            Assert.AreEqual("CustomOne", firstRootControl.name,
                "R081: first build should apply trimmed custom root name.");

            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = "Stale Name";
            var secondRootControl = BuildAndGetRootControl("R081-rebuild-second");
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, secondRootControl.name,
                "R081: disabled toggle must restore default root name on repeated build.");
        }
    }
}
