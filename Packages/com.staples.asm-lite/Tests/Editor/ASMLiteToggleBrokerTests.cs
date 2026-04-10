using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ASMLite.Editor;
using VRC.SDK3.Avatars.Components;

namespace VF.Model.Feature
{
    [System.Serializable]
    internal class Toggle
    {
        public bool useGlobalParam;
        public string globalParam;
        public string menuPath;
        public string name;
        public int untouchedCounter = 42;
    }

    [System.Serializable]
    internal class NotToggle
    {
        public string label;
    }

    [System.Serializable]
    internal class BrokenToggle
    {
        public bool wrongField;
        public int globalParam;
        public string menuPath;
    }
}

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteToggleBrokerTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLiteToggleNameBroker.ClearPendingRestoreState();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "TB00: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.AvatarGo, "TB00: fixture did not include avatar root.");
            Assert.IsNotNull(_ctx.Comp, "TB00: fixture did not include ASMLiteComponent.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteToggleNameBroker.ClearPendingRestoreState();
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        [Test]
        public void TB01_SanitizePathToken_NormalizesMalformedInput()
        {
            Assert.AreEqual("Unnamed", ASMLiteToggleNameBroker.SanitizePathToken(null),
                "TB01: null input should fail closed to Unnamed.");
            Assert.AreEqual("Unnamed", ASMLiteToggleNameBroker.SanitizePathToken("    "),
                "TB01: whitespace input should fail closed to Unnamed.");
            Assert.AreEqual("Hat_Menu", ASMLiteToggleNameBroker.SanitizePathToken("Hat/Menu"),
                "TB01: slash should be normalized to underscore.");
            Assert.AreEqual("_123_A", ASMLiteToggleNameBroker.SanitizePathToken("123 !@# A"),
                "TB01: digit-leading names should be prefixed and invalid chars collapsed.");
        }

        [Test]
        public void TB02_BuildDeterministicGlobalName_DedupesCollisionsWithinAvatar()
        {
            var reserved = new HashSet<string>(System.StringComparer.Ordinal);

            string first = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                "Menu/Clothing",
                "Avatar/Armature/Toggles",
                reserved);
            string second = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                "Menu Clothing",
                "Avatar Armature Toggles",
                reserved);

            StringAssert.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, first,
                "TB02: deterministic names must use ASM_VF_ prefix.");
            Assert.AreEqual(first + "_2", second,
                "TB02: duplicate sanitized names in one avatar must get deterministic numeric suffixes.");
        }

        [Test]
        public void TB03_Discovery_FailsClosedWhenReflectedTypeMissing()
        {
            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(
                _ctx.AvatarGo,
                toggleTypeFullName: "VF.Model.Feature.ToggleTypeThatDoesNotExist");

            Assert.IsNotNull(candidates, "TB03: discovery should return a list even when type resolution fails.");
            Assert.AreEqual(0, candidates.Count,
                "TB03: unresolved reflected Toggle type must fail closed without enrollment targets.");
        }

        [Test]
        public void TB04_Discovery_ScopesToAsmLiteAvatarAndSkipsNonToggleContent()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.NotToggle { label = "not a toggle" };

            var noAsmLiteAvatar = new GameObject("TB04_NoAsmLiteAvatar");
            try
            {
                var foreignVf = noAsmLiteAvatar.AddComponent<VF.Model.VRCFury>();
                foreignVf.content = new VF.Model.Feature.Toggle
                {
                    useGlobalParam = false,
                    globalParam = "",
                    menuPath = "Foreign/Menu",
                    name = "ForeignToggle",
                };

                var localCandidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
                var foreignCandidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(noAsmLiteAvatar);

                Assert.AreEqual(0, localCandidates.Count,
                    "TB04: candidate component without Toggle content should not be enrolled.");
                Assert.AreEqual(0, foreignCandidates.Count,
                    "TB04: avatars without ASMLite scope must not be scanned for enrollment.");
            }
            finally
            {
                Object.DestroyImmediate(noAsmLiteAvatar);
            }
        }

        [Test]
        public void TB05_Discovery_HandlesBlankGlobalNameBoundaryAndSchemaDrift()
        {
            var eligibleGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB05Eligible");
            var eligibleVf = eligibleGo.AddComponent<VF.Model.VRCFury>();
            eligibleVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "   ",
                menuPath = "Outfit/Coat",
                name = "Coat",
            };

            var brokenGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB05Broken");
            var brokenVf = brokenGo.AddComponent<VF.Model.VRCFury>();
            brokenVf.content = new VF.Model.Feature.BrokenToggle
            {
                wrongField = true,
                globalParam = 5,
                menuPath = "Broken/Schema",
            };

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(1, candidates.Count,
                "TB05: only valid Toggle schema with blank global name boundary should be eligible.");
            Assert.IsTrue(candidates[0].UseGlobalParam,
                "TB05: boundary case should retain original useGlobal=true in candidate snapshot.");
            Assert.IsTrue(string.IsNullOrWhiteSpace(candidates[0].GlobalParam),
                "TB05: boundary case should surface blank global name for deterministic replacement.");
        }

        [Test]
        public void TB05b_Discovery_AssignedGlobalParams_AreReportedForFallbackDiscovery()
        {
            var assignedGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB05bAssigned");
            var assignedVf = assignedGo.AddComponent<VF.Model.VRCFury>();
            assignedVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "VF300_Clothing/Rezz",
                menuPath = "Clothing/Rezz",
                name = "Rezz",
            };

            var blankGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB05bBlank");
            var blankVf = blankGo.AddComponent<VF.Model.VRCFury>();
            blankVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "   ",
                menuPath = "Clothing/Blank",
                name = "Blank",
            };

            var globals = ASMLiteToggleNameBroker.DiscoverAssignedToggleGlobalParams(_ctx.AvatarGo);
            Assert.AreEqual(1, globals.Count,
                "TB05b: assigned global discovery should report only non-empty useGlobal=true Toggle globals.");
            Assert.AreEqual("VF300_Clothing/Rezz", globals[0],
                "TB05b: assigned global discovery should preserve canonical VF global names unchanged.");
        }

        [Test]
        public void TB06_Mutation_UpdatesOnlySerializedToggleBoolAndStringFields()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            var toggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = "",
                menuPath = "Menu/Accessories",
                name = "Hat",
                untouchedCounter = 77,
            };
            vf.content = toggle;
            vf.untouchedMarker = "original";

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(1, candidates.Count,
                "TB06: expected one eligible Toggle candidate in fixture avatar.");

            var reserved = new HashSet<string>(System.StringComparer.Ordinal);
            string deterministicName = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                candidates[0].MenuPathHint,
                candidates[0].ObjectPath,
                reserved);

            bool enrolled = ASMLiteToggleNameBroker.TryEnrollToggleCandidate(candidates[0], deterministicName, out var record);
            Assert.IsTrue(enrolled, "TB06: enrollment should succeed for valid Toggle schema.");

            Assert.AreEqual(vf.GetInstanceID(), record.ComponentInstanceId,
                "TB06: mutation record must bind to exact source component instance.");
            Assert.AreEqual(false, record.OriginalUseGlobalParam,
                "TB06: mutation record should preserve original bool value for restore path.");
            Assert.AreEqual(string.Empty, record.OriginalGlobalParam,
                "TB06: mutation record should preserve original global name for restore path.");
            Assert.AreEqual(deterministicName, record.AssignedGlobalParam,
                "TB06: mutation record should capture deterministic name written to serialized payload.");

            Assert.IsTrue(toggle.useGlobalParam,
                "TB06: enrollment should set useGlobalParam=true.");
            Assert.AreEqual(deterministicName, toggle.globalParam,
                "TB06: enrollment should write deterministic global parameter name.");
            Assert.AreEqual(77, toggle.untouchedCounter,
                "TB06: enrollment must not mutate unrelated serialized fields in Toggle payload.");
            Assert.AreEqual("original", vf.untouchedMarker,
                "TB06: enrollment must not mutate unrelated parent component fields.");
        }

        [Test]
        public void TB07_Mutation_RejectsBlankAssignedGlobalName()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            var toggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = "",
                menuPath = "Menu/Hair",
                name = "Hair",
            };
            vf.content = toggle;

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(1, candidates.Count, "TB07: setup failure, expected one eligible candidate.");

            bool enrolled = ASMLiteToggleNameBroker.TryEnrollToggleCandidate(candidates[0], "   ", out _);
            Assert.IsFalse(enrolled,
                "TB07: broker must fail closed when assigned deterministic name is blank/malformed.");
            Assert.IsFalse(toggle.useGlobalParam,
                "TB07: failed enrollment must keep original bool value unchanged.");
            Assert.AreEqual(string.Empty, toggle.globalParam,
                "TB07: failed enrollment must keep original global name unchanged.");
        }

        [Test]
        public void TB08_Callback_EnrollsOnlyAsmLiteScopedAvatars_AndRestoresRoundTrip()
        {
            var asmVf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            var asmToggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Outfit/Hood",
                name = "Hood",
            };
            asmVf.content = asmToggle;

            var foreignAvatar = new GameObject("TB08_ForeignAvatar");
            var foreignDesc = foreignAvatar.AddComponent<VRCAvatarDescriptor>();
            Assert.IsNotNull(foreignDesc, "TB08 setup: foreign descriptor should exist.");
            var foreignVf = foreignAvatar.AddComponent<VF.Model.VRCFury>();
            var foreignToggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Foreign/Menu",
                name = "Foreign",
            };
            foreignVf.content = foreignToggle;

            try
            {
                var callback = new ASMLiteToggleBuildRequestedCallback();
                bool allowed = callback.OnBuildRequested("Avatar");
                Assert.IsTrue(allowed, "TB08: build-request callback should never block build pipeline.");

                Assert.IsTrue(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                    "TB08: successful enrollment should persist pending restore state for delayed cleanup.");
                Assert.IsTrue(asmToggle.useGlobalParam,
                    "TB08: ASM-Lite scoped toggle should be enrolled into global mode before bake.");
                StringAssert.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, asmToggle.globalParam,
                    "TB08: ASM-Lite scoped toggle should receive deterministic ASM_VF global name.");
                Assert.IsFalse(foreignToggle.useGlobalParam,
                    "TB08: non-ASM-Lite avatar toggle must not be touched by enrollment scan.");
                Assert.AreEqual(string.Empty, foreignToggle.globalParam,
                    "TB08: non-ASM-Lite avatar toggle global name must stay unchanged.");

                var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
                Assert.AreEqual(1, restore.RestoredCount,
                    "TB08: restore should round-trip exactly the enrolled ASM-Lite toggle.");
                Assert.AreEqual(0, restore.UnresolvedCount,
                    "TB08: restore should not report unresolved records for live fixture objects.");
                Assert.IsFalse(restore.MalformedPayload,
                    "TB08: valid restore payload must not be treated as malformed.");
                Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                    "TB08: restore should clear pending state after successful replay.");
                Assert.IsFalse(asmToggle.useGlobalParam,
                    "TB08: restore should return source toggle bool to original serialized value.");
                Assert.AreEqual(string.Empty, asmToggle.globalParam,
                    "TB08: restore should return source toggle global string to original value.");
            }
            finally
            {
                Object.DestroyImmediate(foreignAvatar);
            }
        }

        [Test]
        public void TB09_Restore_ClearsMalformedPayloadAndFailsClosed()
        {
            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests("{not-json");

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(0, restore.RestoredCount,
                "TB09: malformed payload should not attempt any restore writes.");
            Assert.AreEqual(0, restore.UnresolvedCount,
                "TB09: malformed payload should be cleared before record iteration.");
            Assert.IsTrue(restore.MalformedPayload,
                "TB09: malformed payload should be explicitly flagged for observability.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "TB09: malformed payload should be cleared to avoid replaying corrupt data forever.");
        }

        [Test]
        public void TB10_Restore_HandlesMissingInstanceIdsAsUnresolvedAndCleansUp()
        {
            const string missingRecordPayload = "{\"entries\":[{\"componentInstanceId\":2147483600,\"objectPath\":\"Avatar/Missing\",\"togglePropertyPath\":\"content\",\"originalUseGlobalParam\":false,\"originalGlobalParam\":\"\",\"assignedGlobalParam\":\"ASM_VF_Missing\"}]}";
            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests(missingRecordPayload);

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(0, restore.RestoredCount,
                "TB10: missing object records must not report successful restore writes.");
            Assert.AreEqual(1, restore.UnresolvedCount,
                "TB10: missing object records should be counted as unresolved cleanup cases.");
            Assert.IsFalse(restore.MalformedPayload,
                "TB10: syntactically valid payload with missing object should not be flagged malformed.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "TB10: unresolved restore pass must still clear pending state to keep cleanup idempotent.");
        }

        [Test]
        public void TB11_Enrollment_DuplicateInvocationPerSession_RestoresStaleThenReenrolls()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            var toggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Outfit/Jacket",
                name = "Jacket",
            };
            vf.content = toggle;

            var first = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(1, first.EnrolledCount,
                "TB11: setup failure, first enrollment should capture one eligible toggle.");
            string firstAssigned = toggle.globalParam;

            var second = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(1, second.StaleCleanupRestoredCount,
                "TB11: second enrollment should stale-cleanup previous pending restore before mutating again.");
            Assert.AreEqual(0, second.StaleCleanupUnresolvedCount,
                "TB11: stale cleanup should not report unresolved records for live fixture object.");
            Assert.AreEqual(1, second.EnrolledCount,
                "TB11: second enrollment should still re-enroll eligible toggle after stale cleanup.");
            Assert.AreEqual(firstAssigned, toggle.globalParam,
                "TB11: deterministic naming should remain stable across repeated enrollment in one session.");

            var finalRestore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(1, finalRestore.RestoredCount,
                "TB11: final restore should replay latest enrollment record exactly once.");
            Assert.IsFalse(toggle.useGlobalParam,
                "TB11: final restore should return bool field to original value.");
            Assert.AreEqual(string.Empty, toggle.globalParam,
                "TB11: final restore should return string field to original value.");

            var repeatRestore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(0, repeatRestore.RestoredCount,
                "TB11: repeated restore call should be idempotent no-op after cleanup.");
        }

        [Test]
        public void TB12_Enrollment_CollisionOwnership_RemainsStableAcrossSiblingReorder()
        {
            var collisionParent = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB12CollisionParent");

            var alphaGo = ASMLiteTestFixtures.CreateChild(collisionParent, "Node-1");
            var alphaVf = alphaGo.AddComponent<VF.Model.VRCFury>();
            var alphaToggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Menu 1",
                name = "Alpha",
            };
            alphaVf.content = alphaToggle;

            var betaGo = ASMLiteTestFixtures.CreateChild(collisionParent, "Node 1");
            var betaVf = betaGo.AddComponent<VF.Model.VRCFury>();
            var betaToggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Menu-1",
                name = "Beta",
            };
            betaVf.content = betaToggle;

            var gammaGo = ASMLiteTestFixtures.CreateChild(collisionParent, "Node_1");
            var gammaVf = gammaGo.AddComponent<VF.Model.VRCFury>();
            var gammaToggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Menu_1",
                name = "Gamma",
            };
            gammaVf.content = gammaToggle;

            var first = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(3, first.EnrolledCount,
                "TB12: setup failure, all three colliding toggles should enroll on first pass.");

            string alphaFirst = alphaToggle.globalParam;
            string betaFirst = betaToggle.globalParam;
            string gammaFirst = gammaToggle.globalParam;

            var assignedFirst = new HashSet<string>(System.StringComparer.Ordinal)
            {
                alphaFirst,
                betaFirst,
                gammaFirst,
            };
            Assert.AreEqual(3, assignedFirst.Count,
                "TB12: each colliding candidate should receive a unique deterministic suffix assignment.");

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(3, restore.RestoredCount,
                "TB12: restore should reset all enrolled collision candidates before reorder pass.");

            alphaGo.transform.SetSiblingIndex(2);
            betaGo.transform.SetSiblingIndex(0);
            gammaGo.transform.SetSiblingIndex(1);

            var second = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(3, second.EnrolledCount,
                "TB12: reordered sibling traversal should still enroll all collision candidates.");

            Assert.AreEqual(alphaFirst, alphaToggle.globalParam,
                "TB12: stable planner should keep Alpha collision ownership after sibling reorder.");
            Assert.AreEqual(betaFirst, betaToggle.globalParam,
                "TB12: stable planner should keep Beta collision ownership after sibling reorder.");
            Assert.AreEqual(gammaFirst, gammaToggle.globalParam,
                "TB12: stable planner should keep Gamma collision ownership after sibling reorder.");
        }

        [Test]
        public void TB13_Enrollment_PreflightNamespaceReservation_TracksCollisionCountersAndLatestReport()
        {
            var collisionParent = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB13CollisionParent");

            var alphaGo = ASMLiteTestFixtures.CreateChild(collisionParent, "Node-1");
            var alphaVf = alphaGo.AddComponent<VF.Model.VRCFury>();
            alphaVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Menu 1",
                name = "Alpha",
            };

            var betaGo = ASMLiteTestFixtures.CreateChild(collisionParent, "Node 1");
            var betaVf = betaGo.AddComponent<VF.Model.VRCFury>();
            betaVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Menu-1",
                name = "Beta",
            };

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(2, candidates.Count,
                "TB13: setup failure, expected two collision candidates before preflight reservation.");

            string baseName = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                candidates[0].MenuPathHint,
                candidates[0].ObjectPath,
                new HashSet<string>(System.StringComparer.Ordinal));

            ASMLiteTestFixtures.SetExpressionParams(
                _ctx,
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = null },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = string.Empty },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = "   " },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = baseName },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = baseName + "_2" });

            var report = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(2, report.PreReservedNameCount,
                "TB13: preflight should reserve only non-empty descriptor parameter names.");
            Assert.AreEqual(2, report.PreflightCollisionAdjustments,
                "TB13: base and first suffix should be counted as descriptor preflight collisions.");
            Assert.AreEqual(2, report.CandidateCollisionAdjustments,
                "TB13: collision group and occupied-name fallback should be counted as intra-candidate adjustments.");

            Assert.IsTrue(ASMLiteToggleNameBroker.TryGetLatestEnrollmentReport(out var latest),
                "TB13: latest enrollment report should be available for passive status UI rendering.");
            Assert.AreEqual(report.PreReservedNameCount, latest.PreReservedNameCount,
                "TB13: latest report snapshot should expose pre-reserved namespace count.");
            Assert.AreEqual(report.PreflightCollisionAdjustments, latest.PreflightCollisionAdjustments,
                "TB13: latest report snapshot should expose preflight adjustment counter.");
            Assert.AreEqual(report.CandidateCollisionAdjustments, latest.CandidateCollisionAdjustments,
                "TB13: latest report snapshot should expose candidate-collision adjustment counter.");

            var assigned = new HashSet<string>(System.StringComparer.Ordinal)
            {
                ((VF.Model.Feature.Toggle)alphaVf.content).globalParam,
                ((VF.Model.Feature.Toggle)betaVf.content).globalParam,
            };

            Assert.AreEqual(2, assigned.Count,
                "TB13: preflight collisions should still produce unique deterministic assignments.");
            Assert.IsTrue(assigned.Contains(baseName + "_3"),
                "TB13: descriptor namespace reservation should push first assignment beyond reserved base and _2 names.");
            Assert.IsTrue(assigned.Contains(baseName + "_4"),
                "TB13: second colliding candidate should deterministically advance to the next free suffix.");
        }

        [Test]
        public void TB14_StartupRestore_ReplaysPendingState_AndSuppressesDuplicateQueueing()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            var toggle = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Startup/Hat",
                name = "Hat",
            };
            vf.content = toggle;

            var enrollment = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(1, enrollment.EnrolledCount,
                "TB14: setup failure, expected one enrolled toggle before startup replay.");
            Assert.IsTrue(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "TB14: setup failure, enrollment should persist pending restore state.");

            bool queuedFirst = ASMLiteToggleNameBroker.TriggerStartupRestoreForTests();
            bool queuedSecond = ASMLiteToggleNameBroker.TriggerStartupRestoreForTests();
            Assert.IsTrue(queuedFirst,
                "TB14: first startup trigger should queue one delayed startup replay pass.");
            Assert.IsFalse(queuedSecond,
                "TB14: repeated startup trigger in same session should be deduped.");
            Assert.IsTrue(ASMLiteToggleNameBroker.IsStartupRestoreQueuedForTests(),
                "TB14: startup replay queue flag should remain set until callback execution.");

            var startupRestore = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(1, startupRestore.RestoredCount,
                "TB14: startup replay should restore the pending toggle mutation exactly once.");
            Assert.AreEqual(0, startupRestore.UnresolvedCount,
                "TB14: startup replay should not report unresolved records for live fixture objects.");
            Assert.IsFalse(startupRestore.MalformedPayload,
                "TB14: valid startup replay payload should not be marked malformed.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "TB14: startup replay should clear pending restore payload after restore.");
            Assert.IsFalse(toggle.useGlobalParam,
                "TB14: startup replay should return useGlobalParam to original false value.");
            Assert.AreEqual(string.Empty, toggle.globalParam,
                "TB14: startup replay should return globalParam to original empty value.");
            Assert.IsFalse(ASMLiteToggleNameBroker.IsStartupRestoreQueuedForTests(),
                "TB14: startup replay queue flag should clear after callback execution.");

            var startupNoOp = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, startupNoOp.RestoredCount,
                "TB14: repeated startup replay should become a no-op after first cleanup.");
            Assert.AreEqual(0, startupNoOp.UnresolvedCount,
                "TB14: repeated startup replay no-op should not report unresolved records.");
        }

        [Test]
        public void TB15_StartupRestore_NoPendingState_RemainsNoOp()
        {
            ASMLiteToggleNameBroker.ClearPendingRestoreState();

            bool queued = ASMLiteToggleNameBroker.TriggerStartupRestoreForTests();
            Assert.IsFalse(queued,
                "TB15: startup trigger should not queue delayed replay when no payload exists.");
            Assert.IsFalse(ASMLiteToggleNameBroker.IsStartupRestoreQueuedForTests(),
                "TB15: startup queue flag should remain false when no payload exists.");

            var startupNoOp = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, startupNoOp.RestoredCount,
                "TB15: startup replay with no payload should report zero restored records.");
            Assert.AreEqual(0, startupNoOp.UnresolvedCount,
                "TB15: startup replay with no payload should report zero unresolved records.");
            Assert.IsFalse(startupNoOp.MalformedPayload,
                "TB15: no-payload startup replay should not be classified as malformed.");
        }

        [Test]
        public void TB16_StartupRestore_CleansMalformedAndUnresolvedPayloads_Idempotently()
        {
            const string missingRecordPayload = "{\"entries\":[{\"componentInstanceId\":2147483600,\"objectPath\":\"Avatar/Missing\",\"togglePropertyPath\":\"content\",\"originalUseGlobalParam\":false,\"originalGlobalParam\":\"\",\"assignedGlobalParam\":\"ASM_VF_Missing\"}]}";

            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests("{not-json");
            var malformed = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, malformed.RestoredCount,
                "TB16: malformed startup payload should not restore any records.");
            Assert.AreEqual(0, malformed.UnresolvedCount,
                "TB16: malformed startup payload should clear before unresolved iteration.");
            Assert.IsTrue(malformed.MalformedPayload,
                "TB16: malformed startup payload should be flagged for diagnostics.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "TB16: malformed startup payload should be cleared immediately.");

            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests(missingRecordPayload);
            var unresolved = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, unresolved.RestoredCount,
                "TB16: missing-object startup payload should not restore any records.");
            Assert.AreEqual(1, unresolved.UnresolvedCount,
                "TB16: missing-object startup payload should count unresolved cleanup paths.");
            Assert.IsFalse(unresolved.MalformedPayload,
                "TB16: syntactically valid missing-object payload should not be malformed.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "TB16: unresolved startup cleanup should still clear pending payload state.");

            var repeatNoOp = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, repeatNoOp.RestoredCount,
                "TB16: repeated startup replay after cleanup should remain no-op.");
            Assert.AreEqual(0, repeatNoOp.UnresolvedCount,
                "TB16: repeated startup replay no-op should not retain unresolved residue.");
        }

        [Test]
        public void TB17_Discovery_FindsEligibleToggleInLegacyFeaturesArray()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.NotToggle { label = "content is not a toggle" };
            vf.features = new object[]
            {
                new VF.Model.Feature.Toggle
                {
                    useGlobalParam = false,
                    globalParam = string.Empty,
                    menuPath = "Legacy/Hat",
                    name = "Hat",
                },
            };

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(1, candidates.Count,
                "TB17: legacy features[] toggle should be discovered even when content is non-toggle.");
            StringAssert.Contains("features.Array.data[0]", candidates[0].TogglePropertyPath,
                "TB17: discovered legacy candidate should come from features[] managed reference path.");
            Assert.AreEqual("Legacy/Hat", candidates[0].MenuPathHint,
                "TB17: discovery should read menuPath hint from legacy features[] payload.");
        }

        [Test]
        public void TB18_Discovery_MixedContentAndLegacyFeatures_DoNotDuplicateOrEscapeScope()
        {
            var mixedGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "TB18Mixed");
            var mixedVf = mixedGo.AddComponent<VF.Model.VRCFury>();
            mixedVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = string.Empty,
                menuPath = "Content/Menu",
                name = "FromContent",
            };
            mixedVf.features = new object[]
            {
                new VF.Model.Feature.Toggle
                {
                    useGlobalParam = false,
                    globalParam = string.Empty,
                    menuPath = "Legacy/Menu",
                    name = "FromLegacyA",
                },
                new VF.Model.Feature.Toggle
                {
                    useGlobalParam = false,
                    globalParam = string.Empty,
                    menuPath = "Legacy/Menu",
                    name = "FromLegacyB",
                },
            };

            var foreignAvatar = new GameObject("TB18ForeignAvatar");
            try
            {
                var foreignVf = foreignAvatar.AddComponent<VF.Model.VRCFury>();
                foreignVf.content = new VF.Model.Feature.Toggle
                {
                    useGlobalParam = false,
                    globalParam = string.Empty,
                    menuPath = "Foreign/Menu",
                    name = "ForeignContent",
                };
                foreignVf.features = new object[]
                {
                    new VF.Model.Feature.Toggle
                    {
                        useGlobalParam = false,
                        globalParam = string.Empty,
                        menuPath = "Foreign/Legacy",
                        name = "ForeignLegacy",
                    },
                };

                var asmCandidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
                var foreignCandidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(foreignAvatar);

                Assert.AreEqual(3, asmCandidates.Count,
                    "TB18: mixed content + features[] payloads in ASM-Lite scope should all enroll once each.");
                Assert.AreEqual(3, asmCandidates.Select(c => c.TogglePropertyPath).Distinct(System.StringComparer.Ordinal).Count(),
                    "TB18: each discovered candidate should have a unique property path (no duplicate enrollment). ");
                Assert.IsTrue(asmCandidates.Any(c => string.Equals(c.TogglePropertyPath, "content", System.StringComparison.Ordinal)),
                    "TB18: mixed scan should include the content-root Toggle candidate.");
                Assert.IsTrue(asmCandidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[0]", System.StringComparison.Ordinal)),
                    "TB18: mixed scan should include the first legacy features[] Toggle candidate.");
                Assert.IsTrue(asmCandidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[1]", System.StringComparison.Ordinal)),
                    "TB18: mixed scan should include the second legacy features[] Toggle candidate.");
                Assert.AreEqual(0, foreignCandidates.Count,
                    "TB18: discovery must remain scoped to avatars containing ASM-Lite components.");
            }
            finally
            {
                Object.DestroyImmediate(foreignAvatar);
            }
        }

        [Test]
        public void TB19_Discovery_LegacyFeaturesMalformedMembers_FailClosedWithBoundaryCoverage()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            vf.features = new object[]
            {
                new VF.Model.Feature.NotToggle { label = "wrong managed reference type" },
                new VF.Model.Feature.BrokenToggle { wrongField = true, globalParam = 9, menuPath = "Broken/Legacy" },
                new VF.Model.Feature.Toggle
                {
                    useGlobalParam = true,
                    globalParam = "   ",
                    menuPath = "Boundary/BlankGlobal",
                    name = "NeedsReplacement",
                },
                new VF.Model.Feature.Toggle
                {
                    useGlobalParam = false,
                    globalParam = string.Empty,
                    menuPath = "Boundary/Valid",
                    name = "Valid",
                },
                new VF.Model.Feature.Toggle
                {
                    useGlobalParam = true,
                    globalParam = "ASM_VF_AlreadyAssigned",
                    menuPath = "Boundary/AlreadyGlobal",
                    name = "AlreadyGlobal",
                },
            };

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(2, candidates.Count,
                "TB19: malformed legacy members and already-global toggles should be skipped while valid/boundary entries remain.");
            Assert.IsTrue(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[2]", System.StringComparison.Ordinal)),
                "TB19: whitespace global-name boundary should remain eligible for deterministic replacement.");
            Assert.IsTrue(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[3]", System.StringComparison.Ordinal)),
                "TB19: valid legacy Toggle should remain eligible.");
            Assert.IsFalse(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[0]", System.StringComparison.Ordinal)),
                "TB19: non-toggle legacy managed references must fail closed.");
            Assert.IsFalse(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[1]", System.StringComparison.Ordinal)),
                "TB19: schema-drift legacy managed references missing required bool/string fields must fail closed.");
            Assert.IsFalse(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[4]", System.StringComparison.Ordinal)),
                "TB19: already-global toggles should be excluded from enrollment candidates.");
        }
    }
}
