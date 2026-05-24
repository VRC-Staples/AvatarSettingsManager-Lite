using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using ASMLite.Editor;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase.Editor.BuildPipeline;

namespace VF.Model.Feature
{
    [System.Serializable]
    internal class Toggle
    {
        public bool useGlobalParam;
        public string globalParam;
        public string menuPath;
        public string name;
        public bool slider;
        public bool useInt;
        public bool defaultOn;
        public float defaultSliderValue;
        public bool saved = true;
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
    [Category("Headless")]
    public class ASMLiteToggleBrokerTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            ASMLiteToggleNameBroker.ClearPendingRestoreState();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "fixture creation returned null context.");
            Assert.IsNotNull(_ctx.AvatarGo, "fixture did not include avatar root.");
            Assert.IsNotNull(_ctx.Comp, "fixture did not include ASMLiteComponent.");
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteToggleNameBroker.ClearPendingRestoreState();
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        [Test]
        public void BuildRequestedCallback_IsRegisteredWithVrChatSdkPipeline()
        {
            var callback = new ASMLiteToggleBuildRequestedCallback();

            Assert.IsInstanceOf<IVRCSDKBuildRequestedCallback>(callback,
                "regression guard: callback must implement VRChat SDK build-request interface or Unity/SDK will never invoke VRCFury toggle enrollment before play/build processing.");
            Assert.IsTrue(callback.OnBuildRequested(VRCSDKRequestedBuildType.Avatar),
                "avatar build requests should continue after deterministic toggle enrollment.");

            var report = ASMLiteToggleNameBroker.GetLatestGlobalParamMappings();
            Assert.IsNotNull(report,
                "callback path should be callable through the SDK interface without throwing.");
        }

        [Test]
        public void SanitizePathToken_NormalizesMalformedInput()
        {
            Assert.AreEqual("Unnamed", ASMLiteToggleNameBroker.SanitizePathToken(null),
                "null input should fail closed to Unnamed.");
            Assert.AreEqual("Unnamed", ASMLiteToggleNameBroker.SanitizePathToken("    "),
                "whitespace input should fail closed to Unnamed.");
            Assert.AreEqual("Hat_Menu", ASMLiteToggleNameBroker.SanitizePathToken("Hat/Menu"),
                "slash should be normalized to underscore.");
            Assert.AreEqual("_123_A", ASMLiteToggleNameBroker.SanitizePathToken("123 !@# A"),
                "digit-leading names should be prefixed and invalid chars collapsed.");
        }

        [Test]
        public void BuildDeterministicGlobalName_CollisionProducesDistinctNames()
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
                "deterministic names must use ASM_VF_ prefix.");
            StringAssert.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, second,
                "collision fallback names must keep the ASM_VF namespace.");
            Assert.AreNotEqual(first, second,
                "colliding sanitized names must receive distinct assigned globals within one avatar.");
        }

        [Test]
        public void Discovery_FailsClosedWhenReflectedTypeMissing()
        {
            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(
                _ctx.AvatarGo,
                toggleTypeFullName: "VF.Model.Feature.ToggleTypeThatDoesNotExist");

            Assert.IsNotNull(candidates, "discovery should return a list even when type resolution fails.");
            Assert.AreEqual(0, candidates.Count,
                "unresolved reflected Toggle type must fail closed without enrollment targets.");
        }

        [Test]
        public void Discovery_ScopesToAsmLiteAvatarAndSkipsNonToggleContent()
        {
            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.NotToggle { label = "not a toggle" };

            var noAsmLiteAvatar = new GameObject("NoAsmLiteAvatar");
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
                    "candidate component without Toggle content should not be enrolled.");
                Assert.AreEqual(0, foreignCandidates.Count,
                    "avatars without ASMLite scope must not be scanned for enrollment.");
            }
            finally
            {
                Object.DestroyImmediate(noAsmLiteAvatar);
            }
        }

        [Test]
        public void Discovery_HandlesBlankGlobalNameBoundaryAndSchemaDrift()
        {
            var eligibleGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "Eligible");
            var eligibleVf = eligibleGo.AddComponent<VF.Model.VRCFury>();
            eligibleVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "   ",
                menuPath = "Outfit/Coat",
                name = "Coat",
            };

            var brokenGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "Broken");
            var brokenVf = brokenGo.AddComponent<VF.Model.VRCFury>();
            brokenVf.content = new VF.Model.Feature.BrokenToggle
            {
                wrongField = true,
                globalParam = 5,
                menuPath = "Broken/Schema",
            };

            var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(_ctx.AvatarGo);
            Assert.AreEqual(1, candidates.Count,
                "only valid Toggle schema with blank global name boundary should be eligible.");
            Assert.IsTrue(candidates[0].UseGlobalParam,
                "boundary case should retain original useGlobal=true in candidate snapshot.");
            Assert.IsTrue(string.IsNullOrWhiteSpace(candidates[0].GlobalParam),
                "boundary case should surface blank global name for deterministic replacement.");
        }

        [Test]
        public void Discovery_AssignedGlobalParams_AreReportedForFallbackDiscovery()
        {
            var assignedGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "Assigned");
            var assignedVf = assignedGo.AddComponent<VF.Model.VRCFury>();
            assignedVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Brokered_Clothing/Rezz",
                menuPath = "Clothing/Rezz",
                name = "Rezz",
            };

            var blankGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "Blank");
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
                "assigned global discovery should report only non-empty useGlobal=true Toggle globals.");
            Assert.AreEqual("Brokered_Clothing/Rezz", globals[0],
                "assigned global discovery should preserve canonical VF global names unchanged.");
        }

        [Test]
        public void Discovery_AssignedToggleExpressionParameters_MirrorVrcFuryToggleBuilderFields()
        {
            var boolGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "BoolAssigned");
            var boolVf = boolGo.AddComponent<VF.Model.VRCFury>();
            boolVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Clothing/Rezz",
                menuPath = "Clothing/Rezz",
                name = "Rezz",
                defaultOn = true,
                saved = false,
            };

            var sliderGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "SliderAssigned");
            var sliderVf = sliderGo.AddComponent<VF.Model.VRCFury>();
            sliderVf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "Hair/Emission",
                menuPath = "Hair/Emission",
                name = "Emission",
                slider = true,
                defaultSliderValue = 0.4f,
                saved = true,
            };

            var discovered = ASMLiteToggleNameBroker.DiscoverAssignedToggleExpressionParameters(_ctx.AvatarGo);

            var boolParam = discovered.SingleOrDefault(p => p.name == "Clothing/Rezz");
            Assert.IsNotNull(boolParam, "assigned VRCFury bool toggle global must be projected as a backable expression parameter.");
            Assert.AreEqual(VRCExpressionParameters.ValueType.Bool, boolParam.valueType);
            Assert.AreEqual(1f, boolParam.defaultValue);
            Assert.IsFalse(boolParam.saved, "VRCFury Toggle saved field should be preserved.");
            Assert.IsTrue(boolParam.networkSynced, "VRCFury Toggle globals are synced parameters.");

            var sliderParam = discovered.SingleOrDefault(p => p.name == "Hair/Emission");
            Assert.IsNotNull(sliderParam, "assigned VRCFury slider global must be projected as a backable expression parameter.");
            Assert.AreEqual(VRCExpressionParameters.ValueType.Float, sliderParam.valueType);
            Assert.AreEqual(0.4f, sliderParam.defaultValue);
            Assert.IsTrue(sliderParam.saved);
            Assert.IsTrue(sliderParam.networkSynced);
        }

        [Test]
        public void Mutation_UpdatesOnlySerializedToggleBoolAndStringFields()
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
                "expected one eligible Toggle candidate in fixture avatar.");

            var reserved = new HashSet<string>(System.StringComparer.Ordinal);
            string deterministicName = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                candidates[0].MenuPathHint,
                candidates[0].ObjectPath,
                reserved);

            bool enrolled = ASMLiteToggleNameBroker.TryEnrollToggleCandidate(candidates[0], deterministicName, out var record);
            Assert.IsTrue(enrolled, "enrollment should succeed for valid Toggle schema.");

            Assert.AreEqual(vf.GetInstanceID(), record.ComponentInstanceId,
                "mutation record must bind to exact source component instance.");
            Assert.AreEqual(false, record.OriginalUseGlobalParam,
                "mutation record should preserve original bool value for restore path.");
            Assert.AreEqual(string.Empty, record.OriginalGlobalParam,
                "mutation record should preserve original global name for restore path.");
            Assert.AreEqual(deterministicName, record.AssignedGlobalParam,
                "mutation record should capture deterministic name written to serialized payload.");

            Assert.IsTrue(toggle.useGlobalParam,
                "enrollment should set useGlobalParam=true.");
            Assert.AreEqual(deterministicName, toggle.globalParam,
                "enrollment should write deterministic global parameter name.");
            Assert.AreEqual(77, toggle.untouchedCounter,
                "enrollment must not mutate unrelated serialized fields in Toggle payload.");
            Assert.AreEqual("original", vf.untouchedMarker,
                "enrollment must not mutate unrelated parent component fields.");
        }

        [Test]
        public void Mutation_RejectsBlankAssignedGlobalName()
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
            Assert.AreEqual(1, candidates.Count, "setup failure, expected one eligible candidate.");

            bool enrolled = ASMLiteToggleNameBroker.TryEnrollToggleCandidate(candidates[0], "   ", out _);
            Assert.IsFalse(enrolled,
                "broker must fail closed when assigned deterministic name is blank/malformed.");
            Assert.IsFalse(toggle.useGlobalParam,
                "failed enrollment must keep original bool value unchanged.");
            Assert.AreEqual(string.Empty, toggle.globalParam,
                "failed enrollment must keep original global name unchanged.");
        }

        [Test]
        public void Callback_EnrollsOnlyAsmLiteScopedAvatars_AndRestoresRoundTrip()
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

            var foreignAvatar = new GameObject("ForeignAvatar");
            var foreignDesc = foreignAvatar.AddComponent<VRCAvatarDescriptor>();
            Assert.IsNotNull(foreignDesc, "setup: foreign descriptor should exist.");
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
                Assert.IsTrue(allowed, "build-request callback should never block build pipeline.");

                Assert.IsTrue(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                    "successful enrollment should persist pending restore state for delayed cleanup.");
                Assert.IsTrue(asmToggle.useGlobalParam,
                    "ASM-Lite scoped toggle should be enrolled into global mode before bake.");
                StringAssert.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, asmToggle.globalParam,
                    "ASM-Lite scoped toggle should receive deterministic ASM_VF global name.");
                Assert.IsFalse(foreignToggle.useGlobalParam,
                    "non-ASM-Lite avatar toggle must not be touched by enrollment scan.");
                Assert.AreEqual(string.Empty, foreignToggle.globalParam,
                    "non-ASM-Lite avatar toggle global name must stay unchanged.");

                var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
                Assert.AreEqual(1, restore.RestoredCount,
                    "restore should round-trip exactly the enrolled ASM-Lite toggle.");
                Assert.AreEqual(0, restore.UnresolvedCount,
                    "restore should not report unresolved records for live fixture objects.");
                Assert.IsFalse(restore.MalformedPayload,
                    "valid restore payload must not be treated as malformed.");
                Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                    "restore should clear pending state after successful replay.");
                Assert.IsFalse(asmToggle.useGlobalParam,
                    "restore should return source toggle bool to original serialized value.");
                Assert.AreEqual(string.Empty, asmToggle.globalParam,
                    "restore should return source toggle global string to original value.");
            }
            finally
            {
                Object.DestroyImmediate(foreignAvatar);
            }
        }

        [Test]
        public void Restore_ClearsMalformedPayloadAndFailsClosed()
        {
            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests("{not-json");

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(0, restore.RestoredCount,
                "malformed payload should not attempt any restore writes.");
            Assert.AreEqual(0, restore.UnresolvedCount,
                "malformed payload should be cleared before record iteration.");
            Assert.IsTrue(restore.MalformedPayload,
                "malformed payload should be explicitly flagged for observability.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "malformed payload should be cleared to avoid replaying corrupt data forever.");
        }

        [Test]
        public void Restore_HandlesMissingInstanceIdsAsUnresolvedAndCleansUp()
        {
            const string missingRecordPayload = "{\"entries\":[{\"componentInstanceId\":2147483600,\"objectPath\":\"Avatar/Missing\",\"togglePropertyPath\":\"content\",\"originalUseGlobalParam\":false,\"originalGlobalParam\":\"\",\"assignedGlobalParam\":\"ASM_VF_Missing\"}]}";
            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests(missingRecordPayload);

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(0, restore.RestoredCount,
                "missing object records must not report successful restore writes.");
            Assert.AreEqual(1, restore.UnresolvedCount,
                "missing object records should be counted as unresolved cleanup cases.");
            Assert.IsFalse(restore.MalformedPayload,
                "syntactically valid payload with missing object should not be flagged malformed.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "unresolved restore pass must still clear pending state to keep cleanup idempotent.");
        }

        [Test]
        public void Enrollment_DuplicateInvocationPerSession_RestoresStaleThenReenrolls()
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
                "setup failure, first enrollment should capture one eligible toggle.");
            string firstAssigned = toggle.globalParam;

            var second = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(1, second.StaleCleanupRestoredCount,
                "second enrollment should stale-cleanup previous pending restore before mutating again.");
            Assert.AreEqual(0, second.StaleCleanupUnresolvedCount,
                "stale cleanup should not report unresolved records for live fixture object.");
            Assert.AreEqual(1, second.EnrolledCount,
                "second enrollment should still re-enroll eligible toggle after stale cleanup.");
            Assert.AreEqual(firstAssigned, toggle.globalParam,
                "deterministic naming should remain stable across repeated enrollment in one session.");

            var finalRestore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(1, finalRestore.RestoredCount,
                "final restore should replay latest enrollment record exactly once.");
            Assert.IsFalse(toggle.useGlobalParam,
                "final restore should return bool field to original value.");
            Assert.AreEqual(string.Empty, toggle.globalParam,
                "final restore should return string field to original value.");

            var repeatRestore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(0, repeatRestore.RestoredCount,
                "repeated restore call should be idempotent no-op after cleanup.");
        }

        [Test]
        public void Enrollment_ReorderedSiblingsStillAssignUniqueCollisionNames_AndRestore()
        {
            var collisionParent = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "CollisionParent");

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
                "setup failure, all three colliding toggles should enroll on first pass.");

            var firstAssigned = new HashSet<string>(System.StringComparer.Ordinal)
            {
                alphaToggle.globalParam,
                betaToggle.globalParam,
                gammaToggle.globalParam,
            };
            Assert.AreEqual(3, firstAssigned.Count,
                "colliding candidates should still receive unique assigned globals on the first pass.");
            Assert.IsTrue(firstAssigned.All(name => !string.IsNullOrWhiteSpace(name)),
                "enrolled collision names should be non-blank.");
            Assert.IsTrue(firstAssigned.All(name => name.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, System.StringComparison.Ordinal)),
                "enrolled collision names should stay in the ASM_VF namespace.");

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(3, restore.RestoredCount,
                "restore should reset all enrolled collision candidates before reorder pass.");
            Assert.IsFalse(alphaToggle.useGlobalParam,
                "restore should return Alpha to its original local mode.");
            Assert.IsFalse(betaToggle.useGlobalParam,
                "restore should return Beta to its original local mode.");
            Assert.IsFalse(gammaToggle.useGlobalParam,
                "restore should return Gamma to its original local mode.");

            alphaGo.transform.SetSiblingIndex(2);
            betaGo.transform.SetSiblingIndex(0);
            gammaGo.transform.SetSiblingIndex(1);

            var second = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.AreEqual(3, second.EnrolledCount,
                "reordered sibling traversal should still enroll all collision candidates.");

            var secondAssigned = new HashSet<string>(System.StringComparer.Ordinal)
            {
                alphaToggle.globalParam,
                betaToggle.globalParam,
                gammaToggle.globalParam,
            };
            Assert.AreEqual(3, secondAssigned.Count,
                "reordered siblings should still receive unique assigned globals.");
            Assert.IsTrue(secondAssigned.All(name => !string.IsNullOrWhiteSpace(name)),
                "reordered collision names should be non-blank.");
            Assert.IsTrue(secondAssigned.All(name => name.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, System.StringComparison.Ordinal)),
                "reordered collision names should stay in the ASM_VF namespace.");

            var finalRestore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(3, finalRestore.RestoredCount,
                "final restore should replay the reordered enrollment records.");
            Assert.IsFalse(alphaToggle.useGlobalParam,
                "final restore should return Alpha to its original bool value.");
            Assert.IsFalse(betaToggle.useGlobalParam,
                "final restore should return Beta to its original bool value.");
            Assert.IsFalse(gammaToggle.useGlobalParam,
                "final restore should return Gamma to its original bool value.");
            Assert.AreEqual(string.Empty, alphaToggle.globalParam,
                "final restore should return Alpha to its original global value.");
            Assert.AreEqual(string.Empty, betaToggle.globalParam,
                "final restore should return Beta to its original global value.");
            Assert.AreEqual(string.Empty, gammaToggle.globalParam,
                "final restore should return Gamma to its original global value.");
        }

        [Test]
        public void Enrollment_SkipsReservedDescriptorNamesWithoutBlockingUniqueAssignments()
        {
            var collisionParent = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "CollisionParent");

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
                "setup failure, expected two collision candidates before preflight reservation.");

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
            Assert.AreEqual(2, report.EnrolledCount,
                "reserved descriptor names should not prevent eligible collision candidates from enrolling.");

            var assigned = new HashSet<string>(System.StringComparer.Ordinal)
            {
                ((VF.Model.Feature.Toggle)alphaVf.content).globalParam,
                ((VF.Model.Feature.Toggle)betaVf.content).globalParam,
            };

            Assert.AreEqual(2, assigned.Count,
                "preflight reservations should still produce unique assigned globals.");
            Assert.IsFalse(assigned.Contains(baseName),
                "assigned globals should not reuse a reserved descriptor base name.");
            Assert.IsFalse(assigned.Contains(baseName + "_2"),
                "assigned globals should not reuse a reserved descriptor collision name.");
            Assert.IsTrue(assigned.All(name => !string.IsNullOrWhiteSpace(name)),
                "preflight-resolved globals should remain non-blank.");
            Assert.IsTrue(assigned.All(name => name.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, System.StringComparison.Ordinal)),
                "preflight-resolved globals should remain in the ASM_VF namespace.");
        }

        [Test]
        public void StartupRestore_ReplaysPendingState_AndSuppressesDuplicateQueueing()
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
                "setup failure, expected one enrolled toggle before startup replay.");
            Assert.IsTrue(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "setup failure, enrollment should persist pending restore state.");

            bool queuedFirst = ASMLiteToggleNameBroker.TriggerStartupRestoreForTests();
            bool queuedSecond = ASMLiteToggleNameBroker.TriggerStartupRestoreForTests();
            Assert.IsTrue(queuedFirst,
                "first startup trigger should queue one delayed startup replay pass.");
            Assert.IsFalse(queuedSecond,
                "repeated startup trigger in same session should be deduped.");
            Assert.IsTrue(ASMLiteToggleNameBroker.IsStartupRestoreQueuedForTests(),
                "startup replay queue flag should remain set until callback execution.");

            var startupRestore = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(1, startupRestore.RestoredCount,
                "startup replay should restore the pending toggle mutation exactly once.");
            Assert.AreEqual(0, startupRestore.UnresolvedCount,
                "startup replay should not report unresolved records for live fixture objects.");
            Assert.IsFalse(startupRestore.MalformedPayload,
                "valid startup replay payload should not be marked malformed.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "startup replay should clear pending restore payload after restore.");
            Assert.IsFalse(toggle.useGlobalParam,
                "startup replay should return useGlobalParam to original false value.");
            Assert.AreEqual(string.Empty, toggle.globalParam,
                "startup replay should return globalParam to original empty value.");
            Assert.IsFalse(ASMLiteToggleNameBroker.IsStartupRestoreQueuedForTests(),
                "startup replay queue flag should clear after callback execution.");

            var startupNoOp = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, startupNoOp.RestoredCount,
                "repeated startup replay should become a no-op after first cleanup.");
            Assert.AreEqual(0, startupNoOp.UnresolvedCount,
                "repeated startup replay no-op should not report unresolved records.");
        }

        [Test]
        public void StartupRestore_NoPendingState_RemainsNoOp()
        {
            ASMLiteToggleNameBroker.ClearPendingRestoreState();

            bool queued = ASMLiteToggleNameBroker.TriggerStartupRestoreForTests();
            Assert.IsFalse(queued,
                "startup trigger should not queue delayed replay when no payload exists.");
            Assert.IsFalse(ASMLiteToggleNameBroker.IsStartupRestoreQueuedForTests(),
                "startup queue flag should remain false when no payload exists.");

            var startupNoOp = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, startupNoOp.RestoredCount,
                "startup replay with no payload should report zero restored records.");
            Assert.AreEqual(0, startupNoOp.UnresolvedCount,
                "startup replay with no payload should report zero unresolved records.");
            Assert.IsFalse(startupNoOp.MalformedPayload,
                "no-payload startup replay should not be classified as malformed.");
        }

        [Test]
        public void StartupRestore_CleansMalformedAndUnresolvedPayloads_Idempotently()
        {
            const string missingRecordPayload = "{\"entries\":[{\"componentInstanceId\":2147483600,\"objectPath\":\"Avatar/Missing\",\"togglePropertyPath\":\"content\",\"originalUseGlobalParam\":false,\"originalGlobalParam\":\"\",\"assignedGlobalParam\":\"ASM_VF_Missing\"}]}";

            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests("{not-json");
            var malformed = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, malformed.RestoredCount,
                "malformed startup payload should not restore any records.");
            Assert.AreEqual(0, malformed.UnresolvedCount,
                "malformed startup payload should clear before unresolved iteration.");
            Assert.IsTrue(malformed.MalformedPayload,
                "malformed startup payload should be flagged for diagnostics.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "malformed startup payload should be cleared immediately.");

            ASMLiteToggleNameBroker.SetPendingRestorePayloadForTests(missingRecordPayload);
            var unresolved = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, unresolved.RestoredCount,
                "missing-object startup payload should not restore any records.");
            Assert.AreEqual(1, unresolved.UnresolvedCount,
                "missing-object startup payload should count unresolved cleanup paths.");
            Assert.IsFalse(unresolved.MalformedPayload,
                "syntactically valid missing-object payload should not be malformed.");
            Assert.IsFalse(ASMLiteToggleNameBroker.HasPendingRestoreState(),
                "unresolved startup cleanup should still clear pending payload state.");

            var repeatNoOp = ASMLiteToggleNameBroker.ExecuteStartupRestoreNowForTests();
            Assert.AreEqual(0, repeatNoOp.RestoredCount,
                "repeated startup replay after cleanup should remain no-op.");
            Assert.AreEqual(0, repeatNoOp.UnresolvedCount,
                "repeated startup replay no-op should not retain unresolved residue.");
        }

        [Test]
        public void Discovery_FindsEligibleToggleInLegacyFeaturesArray()
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
                "legacy features[] toggle should be discovered even when content is non-toggle.");
            StringAssert.Contains("features.Array.data[0]", candidates[0].TogglePropertyPath,
                "discovered legacy candidate should come from features[] managed reference path.");
            Assert.AreEqual("Legacy/Hat", candidates[0].MenuPathHint,
                "discovery should read menuPath hint from legacy features[] payload.");
        }

        [Test]
        public void Discovery_MixedContentAndLegacyFeatures_DoNotDuplicateOrEscapeScope()
        {
            var mixedGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "Mixed");
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

            var foreignAvatar = new GameObject("ForeignAvatar");
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
                    "mixed content + features[] payloads in ASM-Lite scope should all enroll once each.");
                Assert.AreEqual(3, asmCandidates.Select(c => c.TogglePropertyPath).Distinct(System.StringComparer.Ordinal).Count(),
                    "each discovered candidate should have a unique property path (no duplicate enrollment). ");
                Assert.IsTrue(asmCandidates.Any(c => string.Equals(c.TogglePropertyPath, "content", System.StringComparison.Ordinal)),
                    "mixed scan should include the content-root Toggle candidate.");
                Assert.IsTrue(asmCandidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[0]", System.StringComparison.Ordinal)),
                    "mixed scan should include the first legacy features[] Toggle candidate.");
                Assert.IsTrue(asmCandidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[1]", System.StringComparison.Ordinal)),
                    "mixed scan should include the second legacy features[] Toggle candidate.");
                Assert.AreEqual(0, foreignCandidates.Count,
                    "discovery must remain scoped to avatars containing ASM-Lite components.");
            }
            finally
            {
                Object.DestroyImmediate(foreignAvatar);
            }
        }

        [Test]
        public void Discovery_LegacyFeaturesMalformedMembers_FailClosedWithBoundaryCoverage()
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
                "malformed legacy members and already-global toggles should be skipped while valid/boundary entries remain.");
            Assert.IsTrue(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[2]", System.StringComparison.Ordinal)),
                "whitespace global-name boundary should remain eligible for deterministic replacement.");
            Assert.IsTrue(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[3]", System.StringComparison.Ordinal)),
                "valid legacy Toggle should remain eligible.");
            Assert.IsFalse(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[0]", System.StringComparison.Ordinal)),
                "non-toggle legacy managed references must fail closed.");
            Assert.IsFalse(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[1]", System.StringComparison.Ordinal)),
                "schema-drift legacy managed references missing required bool/string fields must fail closed.");
            Assert.IsFalse(candidates.Any(c => c.TogglePropertyPath.Contains("features.Array.data[4]", System.StringComparison.Ordinal)),
                "already-global toggles should be excluded from enrollment candidates.");
        }
    }
}
