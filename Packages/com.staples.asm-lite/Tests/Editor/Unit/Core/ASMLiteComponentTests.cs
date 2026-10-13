using NUnit.Framework;
using ASMLite;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Regression tests for ASMLiteComponent defaults and enum definitions.
    /// Ensures factory defaults match documented behavior and enum ordinals
    /// match the window dropdown ordering.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteComponentTests
    {
        // ── Default field values ──────────────────────────────────────────────

        [Test]
        public void NewComponent_HasDefaultCustomizationState()
        {
            var go = new GameObject("TestAvatar");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();
                Assert.AreEqual(3, component.slotCount, "Default slot count");
                Assert.AreEqual(IconMode.MultiColor, component.iconMode, "Default gear mode");
                Assert.AreEqual(ActionIconMode.Default, component.actionIconMode, "Default action mode");
                Assert.AreEqual(0, component.selectedGearIndex, "Default gear index");
                Assert.IsNotNull(component.customIcons, "Default custom icons");
                Assert.IsEmpty(component.customIcons, "Default custom icons");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ── IconMode enum ordinals ────────────────────────────────────────────
        // MultiColor=0 must be first so the dropdown defaults to it.

        [Test]
        public void SerializedEnums_KeepCompatibleOrdinals()
        {
            Assert.AreEqual(0, (int)IconMode.MultiColor, "IconMode.MultiColor");
            Assert.AreEqual(1, (int)IconMode.SameColor, "IconMode.SameColor");
            Assert.AreEqual(2, (int)IconMode.Custom, "IconMode.Custom");
            Assert.AreEqual(0, (int)ActionIconMode.Default, "ActionIconMode.Default");
            Assert.AreEqual(1, (int)ActionIconMode.Custom, "ActionIconMode.Custom");
        }

        // ── ActionIconMode enum ordinals ──────────────────────────────────────

        // ── slotCount range sanity ────────────────────────────────────────────

        // ── customIcons default ──────────────────────────────────────────

        // ── IconMode default aligns with window pending field ─────────────────

    }
}
