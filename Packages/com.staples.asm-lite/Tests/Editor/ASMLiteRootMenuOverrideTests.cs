using NUnit.Framework;
using UnityEditor;
using UnityEngine;
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

        private static Texture2D LoadIconOrFail(string path, string aid)
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(icon, $"{aid}: expected icon asset at '{path}' but LoadAssetAtPath returned null.");
            return icon;
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

        [Test, Category("Integration")]
        public void R080_DefaultRootIcon_WhenCustomIconToggleDisabled()
        {
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "R080-default-icon-disabled");
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[1], "R080-default-icon-disabled");

            var rootControl = BuildAndGetRootControl("R080-default-icon-disabled");
            Assert.AreSame(fallbackIcon, rootControl.icon,
                "R080: disabled custom root icon must fall back to bundled presets icon.");
        }

        [Test, Category("Integration")]
        public void R080_CustomRootIcon_UsesExactTextureReference_WhenEnabled()
        {
            var customIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[6], "R080-custom-icon-enabled");
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = customIcon;

            var rootControl = BuildAndGetRootControl("R080-custom-icon-enabled");
            Assert.AreSame(customIcon, rootControl.icon,
                "R080: enabled custom root icon must use the exact supplied Texture2D reference.");
        }

        [Test, Category("Integration")]
        public void R084_CustomRootIconFallback_WhenEnabledButNull()
        {
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "R084-null-icon-fallback");
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;

            var rootControl = BuildAndGetRootControl("R084-null-icon-fallback");
            Assert.AreSame(fallbackIcon, rootControl.icon,
                "R084: enabled custom icon toggle with null texture must fall back to bundled presets icon.");
        }

        [Test, Category("Integration")]
        public void R080_CombinedNameAndIconOverride_AppliesTogetherWithoutWrapperDrift()
        {
            var customIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[3], "R080-combined-name-icon");
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Creator Settings  ";
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = customIcon;

            var rootControl = BuildAndGetRootControl("R080-combined-name-icon");
            Assert.AreEqual("Creator Settings", rootControl.name,
                "R080: combined override must apply trimmed custom root name.");
            Assert.AreSame(customIcon, rootControl.icon,
                "R080: combined override must apply the custom root icon reference.");
            Assert.IsNotNull(rootControl.subMenu,
                "R080: combined override must preserve root submenu wiring.");
        }

        [Test, Category("Integration")]
        public void R084_RepeatedBuild_CustomRootIconThenDisabledToggle_RestoresFallbackIcon()
        {
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "R084-rebuild-icon-fallback");
            var customIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[5], "R084-rebuild-icon-fallback");

            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = customIcon;
            var firstRootControl = BuildAndGetRootControl("R084-rebuild-icon-first");
            Assert.AreSame(customIcon, firstRootControl.icon,
                "R084: first build should apply custom root icon reference.");

            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = customIcon;
            var secondRootControl = BuildAndGetRootControl("R084-rebuild-icon-second");
            Assert.AreSame(fallbackIcon, secondRootControl.icon,
                "R084: disabling root icon override must restore fallback icon on repeated build.");
        }
    }
}
