using NUnit.Framework;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Regression tests for ASMLiteAssetPaths -- verifies array sizes, path
    /// format consistency, and alignment with enum definitions.
    /// ASMLiteAssetPaths is internal to ASMLite.Editor; tests live in the
    /// same assembly family via the test asmdef reference.
    /// </summary>
    [TestFixture]
    public class ASMLiteAssetPathsTests
    {
        private const string PackagePrefix = "Packages/com.staples.asm-lite/";

        // ── GearIconPaths ─────────────────────────────────────────────────────

        [Test]
        public void GearIconPaths_HasExactlyEightEntries()
        {
            Assert.AreEqual(8, ASMLiteAssetPaths.GearIconPaths.Length,
                "GearIconPaths must have 8 entries -- one per gear color");
        }

        [Test]
        public void GearIconPaths_AllPathsAreNonEmpty()
        {
            foreach (string path in ASMLiteAssetPaths.GearIconPaths)
                Assert.IsFalse(string.IsNullOrWhiteSpace(path),
                    "Every GearIconPath entry must be a non-empty string");
        }

        [Test]
        public void GearIconPaths_AllPathsArePngFiles()
        {
            foreach (string path in ASMLiteAssetPaths.GearIconPaths)
                StringAssert.EndsWith(".png", path,
                    $"Expected .png extension: {path}");
        }

        [Test]
        public void GearIconPaths_AllPathsStartWithPackagePrefix()
        {
            foreach (string path in ASMLiteAssetPaths.GearIconPaths)
                StringAssert.StartsWith(PackagePrefix, path,
                    $"Path must use package-relative prefix: {path}");
        }

        [Test]
        public void GearIconPaths_AllPathsAreUnique()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (string path in ASMLiteAssetPaths.GearIconPaths)
                Assert.IsTrue(seen.Add(path), $"Duplicate path found: {path}");
        }

        [Test]
        public void GearIconPaths_ContainsExpectedColorNames()
        {
            // Documented order: Blue, Red, Green, Purple, Cyan, Orange, Pink, Yellow
            string[] expectedColors = { "Blue", "Red", "Green", "Purple", "Cyan", "Orange", "Pink", "Yellow" };
            for (int i = 0; i < expectedColors.Length; i++)
                StringAssert.Contains(expectedColors[i], ASMLiteAssetPaths.GearIconPaths[i],
                    $"GearIconPaths[{i}] should reference the '{expectedColors[i]}' gear");
        }

        [Test]
        public void GearIconPaths_CountMatchesColorNamesInWindow()
        {
            // The window hardcodes 8 color names. Must stay in sync with GearIconPaths.
            string[] colorNames = { "Blue", "Red", "Green", "Purple", "Cyan", "Orange", "Pink", "Yellow" };
            Assert.AreEqual(colorNames.Length, ASMLiteAssetPaths.GearIconPaths.Length,
                "GearIconPaths count must match the colorNames array in ASMLiteWindow");
        }

        // ── Critical asset paths are non-empty ────────────────────────────────

        [Test]
        public void FXController_PathIsNonEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ASMLiteAssetPaths.FXController),
                "FXController path must not be empty");
        }

        [Test]
        public void Prefab_PathIsNonEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ASMLiteAssetPaths.Prefab),
                "Prefab path must not be empty");
        }

        [Test]
        public void ExprParams_PathIsNonEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ASMLiteAssetPaths.ExprParams),
                "ExprParams path must not be empty");
        }

        [Test]
        public void Menu_PathIsNonEmpty()
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(ASMLiteAssetPaths.Menu),
                "Menu path must not be empty");
        }

        [Test]
        public void AllCriticalPaths_StartWithPackagePrefix()
        {
            Assert.IsTrue(ASMLiteAssetPaths.FXController.StartsWith(PackagePrefix),  "FXController");
            Assert.IsTrue(ASMLiteAssetPaths.Prefab.StartsWith(PackagePrefix),        "Prefab");
            Assert.IsTrue(ASMLiteAssetPaths.ExprParams.StartsWith(PackagePrefix),    "ExprParams");
            Assert.IsTrue(ASMLiteAssetPaths.Menu.StartsWith(PackagePrefix),          "Menu");
            Assert.IsTrue(ASMLiteAssetPaths.GeneratedDir.StartsWith(PackagePrefix),  "GeneratedDir");
        }

        [Test]
        public void FXController_PathEndsWithControllerExtension()
        {
            StringAssert.EndsWith(".controller", ASMLiteAssetPaths.FXController,
                "FXController path must end with .controller");
        }

        [Test]
        public void ExprParams_PathEndsWithAssetExtension()
        {
            StringAssert.EndsWith(".asset", ASMLiteAssetPaths.ExprParams,
                "ExprParams path must end with .asset");
        }

        [Test]
        public void Menu_PathEndsWithAssetExtension()
        {
            StringAssert.EndsWith(".asset", ASMLiteAssetPaths.Menu,
                "Menu path must end with .asset");
        }

        [Test]
        public void Prefab_PathEndsWithPrefabExtension()
        {
            StringAssert.EndsWith(".prefab", ASMLiteAssetPaths.Prefab,
                "Prefab path must end with .prefab");
        }
    }
}
