using NUnit.Framework;
using UnityEngine;
using ASMLite.Editor;

namespace VF.Model
{
    // Test-local stub that intentionally matches migration predicate full name.
    internal class VRCFury : MonoBehaviour
    {
    }
}

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A42-A45: Migration integration invariants for stale VRCFury component cleanup.
    /// These tests exercise MigrateStaleVRCFuryComponents() end-to-end against real
    /// ASMLite fixture graphs and exact VF.Model.VRCFury full-name matching semantics.
    /// </summary>
    [TestFixture]
    public class ASMLiteMigrationTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A42: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A42: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A42: fixture did not create VRCAvatarDescriptor.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        private static string FullNameOrNull(Component c)
            => c == null ? "<null>" : c.GetType().FullName ?? "<null>";

        // ── A42 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A42_Migration_RemovesExactVRCFuryTypeNameMatches()
        {
            var stale = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            Assert.IsNotNull(stale, "A42: setup failure, failed adding VF.Model.VRCFury stub component.");

            string fullName = stale.GetType().FullName;
            Assert.AreEqual("VF.Model.VRCFury", fullName,
                $"A42: malformed test setup, expected exact full name VF.Model.VRCFury but got '{fullName ?? "<null>"}'.");

            int beforeTargets = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c != null && c.GetType().FullName == "VF.Model.VRCFury")
                    beforeTargets++;
            }
            Assert.Greater(beforeTargets, 0,
                $"A42: setup failure, expected at least one stale VRCFury component before migration. beforeTargets={beforeTargets}.");

            ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp);

            int afterTargets = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c != null && c.GetType().FullName == "VF.Model.VRCFury")
                    afterTargets++;
            }

            Assert.AreEqual(0, afterTargets,
                $"A42: migration must remove all components whose FullName is exactly VF.Model.VRCFury. beforeTargets={beforeTargets}, afterTargets={afterTargets}.");
        }

        // ── A43 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A43_Migration_PreservesNonTargetComponentsInMixedSet()
        {
            var stale = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var preserved = _ctx.Comp.gameObject.AddComponent<BoxCollider>();

            Assert.IsNotNull(stale, "A43: setup failure, failed adding VF.Model.VRCFury stub component.");
            Assert.IsNotNull(preserved, "A43: setup failure, failed adding non-target BoxCollider component.");
            Assert.AreEqual("VF.Model.VRCFury", stale.GetType().FullName,
                $"A43: malformed test setup, expected exact stale full name VF.Model.VRCFury but got '{FullNameOrNull(stale)}'.");
            Assert.AreEqual(typeof(BoxCollider).FullName, preserved.GetType().FullName,
                $"A43: malformed test setup, expected BoxCollider full name '{typeof(BoxCollider).FullName}' but got '{FullNameOrNull(preserved)}'.");

            ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp);

            int staleCount = 0;
            int boxCount = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c == null) continue;
                string name = c.GetType().FullName;
                if (name == "VF.Model.VRCFury") staleCount++;
                if (name == typeof(BoxCollider).FullName) boxCount++;
            }

            Assert.AreEqual(0, staleCount,
                $"A43: migration must remove stale VF.Model.VRCFury in mixed component sets. staleCount={staleCount}.");
            Assert.GreaterOrEqual(boxCount, 1,
                $"A43: migration must preserve non-target components. expected BoxCollider survivor, boxCount={boxCount}.");
        }

        // ── A44 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A44_Migration_NullComponent_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ASMLiteBuilder.MigrateStaleVRCFuryComponents(null),
                "A44: migration must no-op when ASMLiteComponent is null.");
        }

        // ── A45 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A45_Migration_RepeatedCalls_AreIdempotentAndDoNotOverRemove()
        {
            var stale = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var preserved = _ctx.Comp.gameObject.AddComponent<BoxCollider>();

            Assert.IsNotNull(stale, "A45: setup failure, failed adding VF.Model.VRCFury stub component.");
            Assert.IsNotNull(preserved, "A45: setup failure, failed adding non-target BoxCollider component.");
            Assert.AreEqual("VF.Model.VRCFury", stale.GetType().FullName,
                $"A45: malformed test setup, expected exact stale full name VF.Model.VRCFury but got '{FullNameOrNull(stale)}'.");

            Assert.DoesNotThrow(() => ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp),
                "A45: first migration invocation must not throw.");
            Assert.DoesNotThrow(() => ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp),
                "A45: second migration invocation must not throw (idempotency).");

            int staleCount = 0;
            int boxCount = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c == null) continue;
                string name = c.GetType().FullName;
                if (name == "VF.Model.VRCFury") staleCount++;
                if (name == typeof(BoxCollider).FullName) boxCount++;
            }

            Assert.AreEqual(0, staleCount,
                $"A45: idempotent migration must leave zero stale VF.Model.VRCFury components after repeated calls. staleCount={staleCount}.");
            Assert.GreaterOrEqual(boxCount, 1,
                $"A45: repeated migration calls must not remove non-target components. boxCount={boxCount}.");
        }
    }
}
