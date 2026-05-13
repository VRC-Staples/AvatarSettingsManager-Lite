using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Integration coverage for generated root wrapper custom naming behavior.
    /// Verifies effective-name resolution against the generated menu asset.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteRootMenuOverrideIntegrationTests
    {
        private const string SuiteName = nameof(ASMLiteRootMenuOverrideIntegrationTests);
        private static ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _testGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.SourceAssetsSnapshot _sourceIconAssetsBaseline;
        private AsmLiteTestContext _ctx;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = null;
        }

        [SetUp]
        public void SetUp()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _sourceIconAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(
                SuiteName,
                ASMLiteGeneratedAssetTestIsolation.BuiltInIconFixturePaths());
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "DefaultRootName_WhenCustomToggleDisabled: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "DefaultRootName_WhenCustomToggleDisabled: fixture did not create ASMLiteComponent.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
                _sourceIconAssetsBaseline?.AssertUnchanged(SuiteName);
            }
            finally
            {
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
                _testGeneratedAssetsBaseline = null;
                _sourceIconAssetsBaseline = null;
                _ctx = null;
            }
        }

        private VRCExpressionsMenu.Control BuildAndGetRootControl(string aid)
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult}.");

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
        public void DefaultRootName_WhenCustomToggleDisabled()
        {
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = "Creator Custom";

            var rootControl = BuildAndGetRootControl("DefaultRootName_WhenCustomToggleDisabled-default-disabled");
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, rootControl.name,
                "DefaultRootName_WhenCustomToggleDisabled: disabled custom root name must fall back to Settings Manager.");
        }

        [Test, Category("Integration")]
        public void CustomRootName_UsesTrimmedValue_WhenEnabled()
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "   My Presets   ";

            var rootControl = BuildAndGetRootControl("CustomRootName_UsesTrimmedValue_WhenEnabled-trimmed-enabled");
            Assert.AreEqual("My Presets", rootControl.name,
                "CustomRootName_UsesTrimmedValue_WhenEnabled: enabled custom root name must trim whitespace before applying.");
        }

        [Test, Category("Integration")]
        public void WhitespaceCustomName_FallsBackToDefault_WhenEnabled()
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "   \t  \n ";

            var rootControl = BuildAndGetRootControl("WhitespaceCustomName_FallsBackToDefault_WhenEnabled-blank-enabled");
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, rootControl.name,
                "WhitespaceCustomName_FallsBackToDefault_WhenEnabled: blank/whitespace custom root name must fall back to Settings Manager.");
        }

        [Test, Category("Integration")]
        public void RepeatedBuild_DisabledToggleResetsToDefaultAfterCustomName()
        {
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  CustomOne  ";
            var firstRootControl = BuildAndGetRootControl("RepeatedBuild_DisabledToggleResetsToDefaultAfterCustomName-rebuild-first");
            Assert.AreEqual("CustomOne", firstRootControl.name,
                "RepeatedBuild_DisabledToggleResetsToDefaultAfterCustomName: first build should apply trimmed custom root name.");

            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = "Stale Name";
            var secondRootControl = BuildAndGetRootControl("RepeatedBuild_DisabledToggleResetsToDefaultAfterCustomName-rebuild-second");
            Assert.AreEqual(ASMLiteBuilder.DefaultRootControlName, secondRootControl.name,
                "RepeatedBuild_DisabledToggleResetsToDefaultAfterCustomName: disabled toggle must restore default root name on repeated build.");
        }

        [Test, Category("Integration")]
        public void DefaultRootIcon_WhenCustomIconsDisabled()
        {
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "DefaultRootIcon_WhenCustomIconsDisabled-default-icon-disabled");
            _ctx.Comp.useCustomSlotIcons = false;
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[1], "DefaultRootIcon_WhenCustomIconsDisabled-default-icon-disabled");

            var rootControl = BuildAndGetRootControl("DefaultRootIcon_WhenCustomIconsDisabled-default-icon-disabled");
            Assert.AreSame(fallbackIcon, rootControl.icon,
                "DefaultRootIcon_WhenCustomIconsDisabled: disabled custom icons must fall back to bundled presets icon even when a legacy root icon toggle remains enabled.");
        }

        [Test, Category("Integration")]
        public void CustomRootIcon_UsesExactTextureReference_WhenCustomIconsEnabled()
        {
            var customIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[6], "CustomRootIcon_UsesExactTextureReference_WhenCustomIconsEnabled-custom-icon-enabled");
            _ctx.Comp.useCustomSlotIcons = true;
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = customIcon;

            var rootControl = BuildAndGetRootControl("CustomRootIcon_UsesExactTextureReference_WhenCustomIconsEnabled-custom-icon-enabled");
            Assert.AreSame(customIcon, rootControl.icon,
                "CustomRootIcon_UsesExactTextureReference_WhenCustomIconsEnabled: enabled custom icons must apply the supplied root icon without requiring a separate root-icon toggle.");
        }

        [Test, Category("Integration")]
        public void CustomRootIconFallback_WhenCustomIconsEnabledButNull()
        {
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "CustomRootIconFallback_WhenCustomIconsEnabledButNull-null-icon-fallback");
            _ctx.Comp.useCustomSlotIcons = true;
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;

            var rootControl = BuildAndGetRootControl("CustomRootIconFallback_WhenCustomIconsEnabledButNull-null-icon-fallback");
            Assert.AreSame(fallbackIcon, rootControl.icon,
                "CustomRootIconFallback_WhenCustomIconsEnabledButNull: enabled custom icons with no assigned root texture must fall back to bundled presets icon.");
        }

        [Test, Category("Integration")]
        public void CombinedNameAndIconCustomSettings_ApplyTogetherWithoutWrapperDrift()
        {
            var customIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[3], "CombinedNameAndIconCustomSettings_ApplyTogetherWithoutWrapperDrift-combined-name-icon");
            _ctx.Comp.useCustomSlotIcons = true;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Creator Settings  ";
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = customIcon;

            var rootControl = BuildAndGetRootControl("CombinedNameAndIconCustomSettings_ApplyTogetherWithoutWrapperDrift-combined-name-icon");
            Assert.AreEqual("Creator Settings", rootControl.name,
                "CombinedNameAndIconCustomSettings_ApplyTogetherWithoutWrapperDrift: combined custom settings must apply trimmed custom root name.");
            Assert.AreSame(customIcon, rootControl.icon,
                "CombinedNameAndIconCustomSettings_ApplyTogetherWithoutWrapperDrift: combined custom settings must apply the custom root icon reference.");
            Assert.IsNotNull(rootControl.subMenu,
                "CombinedNameAndIconCustomSettings_ApplyTogetherWithoutWrapperDrift: combined custom settings must preserve root submenu wiring.");
        }

        [Test, Category("Integration")]
        public void RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon()
        {
            var fallbackIcon = LoadIconOrFail(ASMLiteAssetPaths.IconPresets, "RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon-rebuild-icon-fallback");
            var customIcon = LoadIconOrFail(ASMLiteAssetPaths.GearIconPaths[5], "RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon-rebuild-icon-fallback");

            _ctx.Comp.useCustomSlotIcons = true;
            _ctx.Comp.useCustomRootIcon = false;
            _ctx.Comp.customRootIcon = customIcon;
            var firstRootControl = BuildAndGetRootControl("RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon-rebuild-icon-first");
            Assert.AreSame(customIcon, firstRootControl.icon,
                "RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon: first build should apply custom root icon reference.");

            _ctx.Comp.useCustomSlotIcons = false;
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = customIcon;
            var secondRootControl = BuildAndGetRootControl("RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon-rebuild-icon-second");
            Assert.AreSame(fallbackIcon, secondRootControl.icon,
                "RepeatedBuild_CustomRootIconThenDisabledCustomIcons_RestoresFallbackIcon: disabling custom icons must restore fallback icon on repeated build.");
        }
    }
}
