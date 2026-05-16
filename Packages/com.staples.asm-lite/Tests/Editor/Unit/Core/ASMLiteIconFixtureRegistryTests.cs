using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    internal sealed class ASMLiteIconFixtureRegistryTests
    {
        [Test]
        public void KnownFixtureIds_ResolveToExistingPackageTextures()
        {
            foreach (var fixture in ASMLiteIconFixtureRegistry.GetAllFixtures())
            {
                Assert.That(fixture.Id, Is.Not.Empty);
                Assert.That(fixture.AssetPath, Does.StartWith("Packages/com.staples.asm-lite/Icons/"));

                Assert.That(ASMLiteIconFixtureRegistry.TryResolveAssetPath(fixture.Id, out string resolvedPath), Is.True);
                Assert.That(resolvedPath, Is.EqualTo(fixture.AssetPath));

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(resolvedPath);
                Assert.That(texture, Is.Not.Null, fixture.ToString());
                Assert.That(ASMLiteIconFixtureRegistry.TryGetFixtureIdForTexture(texture, out string roundTripId), Is.True);
                Assert.That(roundTripId, Is.EqualTo(fixture.Id));
            }
        }

        [Test]
        public void SlotFixtureIds_AreStableAndOrdered()
        {
            string[] expectedIds =
            {
                "asm-lite-icon/slot-01",
                "asm-lite-icon/slot-02",
                "asm-lite-icon/slot-03",
                "asm-lite-icon/slot-04",
                "asm-lite-icon/slot-05",
                "asm-lite-icon/slot-06",
                "asm-lite-icon/slot-07",
                "asm-lite-icon/slot-08",
            };

            for (int slot = 1; slot <= expectedIds.Length; slot++)
            {
                string expectedId = expectedIds[slot - 1];
                Assert.That(ASMLiteIconFixtureRegistry.ResolveSlotIconId(slot), Is.EqualTo(expectedId));
                Assert.That(ASMLiteIconFixtureRegistry.ResolveAssetPath(expectedId), Is.EqualTo(ASMLiteAssetPaths.GearIconPaths[slot - 1]));
            }
        }

        [Test]
        public void RootAndActionFixtureIds_MapToCanonicalAssets()
        {
            Assert.That(ASMLiteIconFixtureRegistry.ResolveAssetPath("asm-lite-icon/root"), Is.EqualTo(ASMLiteAssetPaths.IconPresets));
            Assert.That(ASMLiteIconFixtureRegistry.ResolveAssetPath("asm-lite-icon/action-save"), Is.EqualTo(ASMLiteAssetPaths.IconSave));
            Assert.That(ASMLiteIconFixtureRegistry.ResolveAssetPath("asm-lite-icon/action-load"), Is.EqualTo(ASMLiteAssetPaths.IconLoad));
            Assert.That(ASMLiteIconFixtureRegistry.ResolveAssetPath("asm-lite-icon/action-clear"), Is.EqualTo(ASMLiteAssetPaths.IconReset));
        }

        [Test]
        public void FixtureIdLookupFromTextures_IsDeterministic()
        {
            var slotTexture = ASMLiteIconFixtureRegistry.ResolveTexture("asm-lite-icon/slot-01");
            var save = ASMLiteIconFixtureRegistry.ResolveTexture("asm-lite-icon/action-save");
            var customIcons = new[] { slotTexture, null, save };

            Assert.That(ASMLiteIconFixtureRegistry.GetFixtureIdOrEmpty(slotTexture), Is.EqualTo("asm-lite-icon/slot-01"));
            Assert.That(
                ASMLiteIconFixtureRegistry.GetFixtureIdsOrEmpty(customIcons),
                Is.EqualTo(new[] { "asm-lite-icon/slot-01", string.Empty, "asm-lite-icon/action-save" }));
        }

        [Test]
        public void UnknownFixtureId_FailsReadably()
        {
            var ex = Assert.Throws<ArgumentException>(() => ASMLiteIconFixtureRegistry.ResolveAssetPath("asm-lite-icon/not-real"));

            Assert.That(ex.Message, Does.Contain("asm-lite-icon/not-real"));
            Assert.That(ex.Message, Does.Contain("Known IDs"));
            Assert.That(ex.Message, Does.Contain("asm-lite-icon/slot-01"));
        }

        [Test]
        public void KnownFixtureIds_AreUnique()
        {
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenPaths = new HashSet<string>(StringComparer.Ordinal);

            foreach (var fixture in ASMLiteIconFixtureRegistry.GetAllFixtures())
            {
                Assert.That(seenIds.Add(fixture.Id), Is.True, fixture.Id);
                Assert.That(seenPaths.Add(fixture.AssetPath), Is.True, fixture.AssetPath);
            }
        }
    }
}
