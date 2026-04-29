using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeSetupFixtureServiceTests
    {
        private AsmLiteTestContext _ctx;
        private ASMLiteSmokeSetupFixtureService _service;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            _ctx.AvatarGo.name = "FixtureAvatar";
            _service = new ASMLiteSmokeSetupFixtureService();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Reset(out _);
            _service = null;
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test]
        public void DuplicateAvatarNameMutation_RecordsCleanupAndRestoresSingleDescriptor()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                avatarName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.DuplicateAvatarName,
            };

            bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

            Assert.That(applied, Is.True, detail);
            Assert.That(_service.CleanupLedgerCount, Is.GreaterThan(0));
            Assert.That(CountSceneAvatarsNamed("FixtureAvatar"), Is.EqualTo(2));

            bool reset = _service.Reset(out string resetDetail);

            Assert.That(reset, Is.True, resetDetail);
            Assert.That(_service.CleanupLedgerCount, Is.EqualTo(0));
            Assert.That(_service.HasCleanResetProof, Is.True);
            Assert.That(CountSceneAvatarsNamed("FixtureAvatar"), Is.EqualTo(1));
        }

        [Test]
        public void SelectedInactiveAvatarMutation_SelectsInactiveAvatarAndRestoresActiveState()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                avatarName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.SelectedInactiveAvatar,
            };

            bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

            Assert.That(applied, Is.True, detail);
            Assert.That(_ctx.AvatarGo.activeSelf, Is.False);
            Assert.That(Selection.activeGameObject, Is.SameAs(_ctx.AvatarGo));

            bool reset = _service.Reset(out string resetDetail);

            Assert.That(reset, Is.True, resetDetail);
            Assert.That(_ctx.AvatarGo.activeSelf, Is.True);
            Assert.That(Selection.activeGameObject, Is.Not.SameAs(_ctx.AvatarGo));
            Assert.That(_service.HasCleanResetProof, Is.True);
        }

        [Test]
        public void SelectedDuplicateAvatarMutation_SelectsCanonicalAvatarAndRestoresSingleDescriptor()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                avatarName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.SelectedDuplicateAvatar,
            };

            bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

            Assert.That(applied, Is.True, detail);
            Assert.That(CountSceneAvatarsNamed("FixtureAvatar"), Is.EqualTo(2));
            Assert.That(Selection.activeGameObject, Is.SameAs(_ctx.AvatarGo));

            bool reset = _service.Reset(out string resetDetail);

            Assert.That(reset, Is.True, resetDetail);
            Assert.That(CountSceneAvatarsNamed("FixtureAvatar"), Is.EqualTo(1));
            Assert.That(Selection.activeGameObject, Is.Not.SameAs(_ctx.AvatarGo));
        }

        [Test]
        public void UnselectedInactiveAvatarMutation_ClearsSelectionAndRestoresActiveState()
        {
            Selection.activeObject = _ctx.AvatarGo;
            var args = new ASMLiteSmokeStepArgs
            {
                avatarName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.UnselectedInactiveAvatar,
            };

            bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

            Assert.That(applied, Is.True, detail);
            Assert.That(_ctx.AvatarGo.activeSelf, Is.False);
            Assert.That(Selection.activeObject, Is.Null);

            bool reset = _service.Reset(out string resetDetail);

            Assert.That(reset, Is.True, resetDetail);
            Assert.That(_ctx.AvatarGo.activeSelf, Is.True);
            Assert.That(Selection.activeGameObject, Is.SameAs(_ctx.AvatarGo));
        }

        [Test]
        public void SameNameNonAvatarMutation_CreatesNonAvatarWithoutSelectingIt()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                objectName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.SameNameNonAvatar,
            };

            bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

            Assert.That(applied, Is.True, detail);
            Assert.That(Selection.activeObject, Is.Null);
            Assert.That(GameObject.FindObjectsOfType<GameObject>().Any(item => item.name == "FixtureAvatar" && item.GetComponent<VRCAvatarDescriptor>() == null), Is.True);

            bool reset = _service.Reset(out string resetDetail);

            Assert.That(reset, Is.True, resetDetail);
            Assert.That(GameObject.FindObjectsOfType<GameObject>().Any(item => item.name == "FixtureAvatar" && item.GetComponent<VRCAvatarDescriptor>() == null), Is.False);
        }

        [Test]
        public void StaleGeneratedFolderMutation_SnapshotsEvidenceBeforeCleanup()
        {
            string evidenceRoot = Path.Combine(Path.GetTempPath(), "asmlite-fixture-evidence-" + System.Guid.NewGuid().ToString("N"));
            var args = new ASMLiteSmokeStepArgs
            {
                avatarName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.StaleGeneratedFolder,
                preserveFailureEvidence = true,
            };

            try
            {
                bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", evidenceRoot, out string detail);

                Assert.That(applied, Is.True, detail);
                Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.True);
                Assert.That(_service.LastEvidenceSnapshotPath, Is.Not.Empty);
                Assert.That(Directory.Exists(_service.LastEvidenceSnapshotPath), Is.True);

                bool reset = _service.Reset(out string resetDetail);

                Assert.That(reset, Is.True, resetDetail);
                Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.False);
                Assert.That(_service.HasCleanResetProof, Is.True);
            }
            finally
            {
                if (Directory.Exists(evidenceRoot))
                    Directory.Delete(evidenceRoot, recursive: true);
            }
        }

        private static int CountSceneAvatarsNamed(string avatarName)
        {
            return Resources.FindObjectsOfTypeAll<VRCAvatarDescriptor>()
                .Count(item => item != null
                    && item.gameObject != null
                    && !EditorUtility.IsPersistent(item.gameObject)
                    && item.gameObject.scene.IsValid()
                    && item.gameObject.scene.isLoaded
                    && item.gameObject.name == avatarName);
        }
    }
}
