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
    public class ASMLiteComponentTests
    {
        // ── Default field values ──────────────────────────────────────────────

        [Test]
        public void DefaultSlotCount_IsThree()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            Assert.AreEqual(3, component.slotCount,
                "Default slotCount should be 3");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void DefaultIconMode_IsMultiColor()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            Assert.AreEqual(IconMode.MultiColor, component.iconMode,
                "Default iconMode should be MultiColor");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void DefaultActionIconMode_IsDefault()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            Assert.AreEqual(ActionIconMode.Default, component.actionIconMode,
                "Default actionIconMode should be Default");

            Object.DestroyImmediate(go);
        }

        [Test]
        public void DefaultSelectedGearIndex_IsZero()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            Assert.AreEqual(0, component.selectedGearIndex,
                "Default selectedGearIndex should be 0 (Blue)");

            Object.DestroyImmediate(go);
        }

        // ── IconMode enum ordinals ────────────────────────────────────────────
        // MultiColor=0 must be first so the dropdown defaults to it.

        [Test]
        public void IconMode_MultiColor_IsZero()
        {
            Assert.AreEqual(0, (int)IconMode.MultiColor,
                "MultiColor must be ordinal 0: it is the default and first in dropdown");
        }

        [Test]
        public void IconMode_SameColor_IsOne()
        {
            Assert.AreEqual(1, (int)IconMode.SameColor,
                "SameColor must be ordinal 1");
        }

        [Test]
        public void IconMode_Custom_IsTwo()
        {
            Assert.AreEqual(2, (int)IconMode.Custom,
                "Custom must be ordinal 2");
        }

        // ── ActionIconMode enum ordinals ──────────────────────────────────────

        [Test]
        public void ActionIconMode_Default_IsZero()
        {
            Assert.AreEqual(0, (int)ActionIconMode.Default,
                "ActionIconMode.Default must be ordinal 0");
        }

        [Test]
        public void ActionIconMode_Custom_IsOne()
        {
            Assert.AreEqual(1, (int)ActionIconMode.Custom,
                "ActionIconMode.Custom must be ordinal 1");
        }

        // ── slotCount range sanity ────────────────────────────────────────────

        [Test]
        public void SlotCount_CanBeSetToOneThroughEight()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            for (int i = 1; i <= 8; i++)
            {
                component.slotCount = i;
                Assert.AreEqual(i, component.slotCount,
                    $"slotCount should accept value {i}");
            }

            Object.DestroyImmediate(go);
        }

        // ── P09: customIcons default ──────────────────────────────────────────

        [Test]
        public void P09_CustomIcons_DefaultIsEmptyArray_NotNull()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            Assert.IsNotNull(component.customIcons,
                "customIcons must not be null by default");
            Assert.AreEqual(0, component.customIcons.Length,
                "customIcons must be empty (length 0) by default");

            Object.DestroyImmediate(go);
        }

        // ── IconMode default aligns with window pending field ─────────────────

        [Test]
        public void IconMode_DefaultValue_MatchesWindowPendingDefault()
        {
            // The window sets _pendingIconMode = IconMode.MultiColor.
            // The component default must match so a fresh add is consistent.
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();

            Assert.AreEqual(IconMode.MultiColor, component.iconMode,
                "Component default iconMode must match window _pendingIconMode default");

            Object.DestroyImmediate(go);
        }
    }
}
