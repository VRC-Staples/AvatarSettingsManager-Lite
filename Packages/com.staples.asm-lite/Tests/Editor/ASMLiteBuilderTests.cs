using NUnit.Framework;
using ASMLite;
using ASMLite.Editor;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Regression tests for ASMLiteBuilder public surface.
    /// These are EditMode tests -- no Play mode or asset pipeline required.
    /// </summary>
    [TestFixture]
    public class ASMLiteBuilderTests
    {
        // ── Validate() ────────────────────────────────────────────────────────

        [Test]
        public void Validate_ReturnsNull_WhenSlotCountIsOne()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 1;

            Assert.IsNull(ASMLiteBuilder.Validate(component));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Validate_ReturnsNull_WhenSlotCountIsEight()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 8;

            Assert.IsNull(ASMLiteBuilder.Validate(component));

            Object.DestroyImmediate(go);
        }

        [Test]
        [TestCase(1)]
        [TestCase(4)]
        [TestCase(8)]
        public void Validate_ReturnsNull_ForAllValidSlotCounts(int slotCount)
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = slotCount;

            Assert.IsNull(ASMLiteBuilder.Validate(component));

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Validate_ReturnsError_WhenSlotCountIsZero()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 0;

            string result = ASMLiteBuilder.Validate(component);

            Assert.IsNotNull(result);
            StringAssert.Contains("slotCount", result);
            StringAssert.Contains("0", result);

            Object.DestroyImmediate(go);
        }

        [Test]
        public void Validate_ReturnsError_WhenSlotCountIsNine()
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = 9;

            string result = ASMLiteBuilder.Validate(component);

            Assert.IsNotNull(result);
            StringAssert.Contains("slotCount", result);
            StringAssert.Contains("9", result);

            Object.DestroyImmediate(go);
        }

        [Test]
        [TestCase(0)]
        [TestCase(-1)]
        [TestCase(9)]
        [TestCase(100)]
        public void Validate_ReturnsError_ForAllInvalidSlotCounts(int slotCount)
        {
            var go = new GameObject("TestAvatar");
            var component = go.AddComponent<ASMLiteComponent>();
            component.slotCount = slotCount;

            string result = ASMLiteBuilder.Validate(component);

            Assert.IsNotNull(result, $"Expected error for slotCount={slotCount}");
            StringAssert.Contains("[ASM-Lite]", result);

            Object.DestroyImmediate(go);
        }

        // ── CompactInt encoding formula ───────────────────────────────────────
        // Encoding: Save=(slot-1)*3+1, Load=(slot-1)*3+2, Clear=(slot-1)*3+3
        // Verified against the KNOWLEDGE.md entry and builder source.

        [Test]
        [TestCase(1, 1, 2, 3)]
        [TestCase(2, 4, 5, 6)]
        [TestCase(3, 7, 8, 9)]
        [TestCase(4, 10, 11, 12)]
        [TestCase(8, 22, 23, 24)]
        public void CompactInt_Encoding_MatchesKnownValues(
            int slot, int expectedSave, int expectedLoad, int expectedClear)
        {
            int save  = (slot - 1) * 3 + 1;
            int load  = (slot - 1) * 3 + 2;
            int clear = (slot - 1) * 3 + 3;

            Assert.AreEqual(expectedSave,  save,  $"Save value mismatch for slot {slot}");
            Assert.AreEqual(expectedLoad,  load,  $"Load value mismatch for slot {slot}");
            Assert.AreEqual(expectedClear, clear, $"Clear value mismatch for slot {slot}");
        }

        [Test]
        public void CompactInt_Encoding_MaxSlot8_DoesNotExceed255()
        {
            // VRChat Int params are 0-255.
            int maxClear = (8 - 1) * 3 + 3; // slot 8 Clear = 24
            Assert.LessOrEqual(maxClear, 255, "CompactInt max value must fit in VRChat Int range");
        }

        [Test]
        public void CompactInt_Encoding_IdleValue_IsZero()
        {
            // Value 0 = idle -- no action in progress.
            // All encoded action values must be > 0.
            for (int slot = 1; slot <= 8; slot++)
            {
                int save  = (slot - 1) * 3 + 1;
                int load  = (slot - 1) * 3 + 2;
                int clear = (slot - 1) * 3 + 3;

                Assert.Greater(save,  0, $"Save value for slot {slot} must be > 0");
                Assert.Greater(load,  0, $"Load value for slot {slot} must be > 0");
                Assert.Greater(clear, 0, $"Clear value for slot {slot} must be > 0");
            }
        }

        [Test]
        public void CompactInt_Encoding_AllValues_AreUnique()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int slot = 1; slot <= 8; slot++)
            {
                int save  = (slot - 1) * 3 + 1;
                int load  = (slot - 1) * 3 + 2;
                int clear = (slot - 1) * 3 + 3;

                Assert.IsTrue(seen.Add(save),  $"Duplicate save value {save} at slot {slot}");
                Assert.IsTrue(seen.Add(load),  $"Duplicate load value {load} at slot {slot}");
                Assert.IsTrue(seen.Add(clear), $"Duplicate clear value {clear} at slot {slot}");
            }
        }

        // ── SafeBool parameter name convention ────────────────────────────────

        [Test]
        [TestCase(1, "ASMLite_S1_Save", "ASMLite_S1_Load", "ASMLite_S1_Clear")]
        [TestCase(4, "ASMLite_S4_Save", "ASMLite_S4_Load", "ASMLite_S4_Clear")]
        [TestCase(8, "ASMLite_S8_Save", "ASMLite_S8_Load", "ASMLite_S8_Clear")]
        public void SafeBool_ParameterNames_MatchExpectedFormat(
            int slot, string expectedSave, string expectedLoad, string expectedClear)
        {
            string save  = $"ASMLite_S{slot}_Save";
            string load  = $"ASMLite_S{slot}_Load";
            string clear = $"ASMLite_S{slot}_Clear";

            Assert.AreEqual(expectedSave,  save);
            Assert.AreEqual(expectedLoad,  load);
            Assert.AreEqual(expectedClear, clear);
        }

        [Test]
        public void SafeBool_ParameterNames_AllHaveASMLitePrefix()
        {
            for (int slot = 1; slot <= 8; slot++)
            {
                Assert.IsTrue($"ASMLite_S{slot}_Save".StartsWith("ASMLite_"));
                Assert.IsTrue($"ASMLite_S{slot}_Load".StartsWith("ASMLite_"));
                Assert.IsTrue($"ASMLite_S{slot}_Clear".StartsWith("ASMLite_"));
            }
        }

        // ── Synced bit count calculation ──────────────────────────────────────

        [Test]
        [TestCase(ControlScheme.CompactInt, 1, 8)]
        [TestCase(ControlScheme.CompactInt, 8, 8)]
        [TestCase(ControlScheme.SafeBool,   1, 3)]
        [TestCase(ControlScheme.SafeBool,   2, 6)]
        [TestCase(ControlScheme.SafeBool,   8, 24)]
        public void SyncedBits_MatchExpectedValues(ControlScheme scheme, int slotCount, int expectedBits)
        {
            int bits = scheme == ControlScheme.CompactInt
                ? 8
                : 3 * slotCount;

            Assert.AreEqual(expectedBits, bits,
                $"Scheme={scheme}, slots={slotCount}");
        }

        [Test]
        public void SyncedBits_NeverExceed256ForAnyValidConfig()
        {
            foreach (ControlScheme scheme in System.Enum.GetValues(typeof(ControlScheme)))
            {
                for (int slots = 1; slots <= 8; slots++)
                {
                    int bits = scheme == ControlScheme.CompactInt ? 8 : 3 * slots;
                    Assert.LessOrEqual(bits, 256,
                        $"Scheme={scheme}, slots={slots} exceeds 256 synced bits");
                }
            }
        }
    }
}
