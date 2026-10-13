using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Regression tests for ASMLiteAssetPaths: verifies array sizes, path
    /// format consistency, and alignment with enum definitions.
    /// ASMLiteAssetPaths is internal to ASMLite.Editor; tests live in the
    /// same assembly family via the test asmdef reference.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteAssetPathsTests
    {
        private const string PackagePrefix = "Packages/com.staples.asm-lite/";

        // ── GearIconPaths ─────────────────────────────────────────────────────

        [Test]
        public void GearPalette_ResolvesUniquePackageTextures_ForEverySlotIndex()
        {
            Assert.AreEqual(8, ASMLiteAssetPaths.GearIconPaths.Length);
            var seen = new System.Collections.Generic.HashSet<Texture2D>();
            for (int index = 0; index < 8; index++)
            {
                string path = ASMLiteAssetPaths.GearIconPaths[index];
                StringAssert.StartsWith(PackagePrefix, path, $"Gear index {index}");
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                Assert.IsNotNull(texture, $"Gear index {index}: {path}");
                Assert.IsTrue(seen.Add(texture), $"Duplicate gear texture at index {index}");
                string fixtureId = ASMLiteIconFixtureRegistry.ResolveSlotIconId(index + 1);
                Assert.AreSame(texture, ASMLiteIconFixtureRegistry.ResolveTexture(fixtureId),
                    $"Slot {index + 1} must resolve its palette texture.");
            }
        }

        // ── Critical asset paths are non-empty ────────────────────────────────

        [Test]
        public void CriticalAssetPaths_ResolveExpectedPackageAssets()
        {
            StringAssert.StartsWith(PackagePrefix, ASMLiteAssetPaths.GeneratedDir);
            Assert.IsTrue(AssetDatabase.IsValidFolder(ASMLiteAssetPaths.GeneratedDir));
            var assets = new (string role, string path, System.Type type)[]
            {
                ("FX controller", ASMLiteAssetPaths.FXController, typeof(UnityEditor.Animations.AnimatorController)),
                ("Prefab", ASMLiteAssetPaths.Prefab, typeof(GameObject)),
                ("Expression parameters", ASMLiteAssetPaths.ExprParams, typeof(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters)),
                ("Menu", ASMLiteAssetPaths.Menu, typeof(VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu)),
            };
            foreach (var (role, path, type) in assets)
            {
                StringAssert.StartsWith(PackagePrefix, path, role);
                Assert.IsNotNull(AssetDatabase.LoadAssetAtPath(path, type), $"{role}: {path}");
            }
        }

        // ── Action icon paths ────────────────────────────────────────────

        [Test]
        public void ActionIconPaths_AreNonEmpty_HavePackagePrefix_AndPngExtension()
        {
            var actionIcons = new (string name, string path)[]
            {
                ("IconSave",      ASMLiteAssetPaths.IconSave),
                ("IconLoad",      ASMLiteAssetPaths.IconLoad),
                ("IconReset",     ASMLiteAssetPaths.IconReset),
                ("IconPresets",   ASMLiteAssetPaths.IconPresets),
                ("IconBackArrow", ASMLiteAssetPaths.IconBackArrow),
            };

            foreach (var (name, path) in actionIcons)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(path),
                    $"{name} path must not be empty");
                StringAssert.StartsWith(PackagePrefix, path,
                    $"{name} path must start with package prefix");
                StringAssert.EndsWith(".png", path,
                    $"{name} path must end with .png");
            }
        }
    }
}
