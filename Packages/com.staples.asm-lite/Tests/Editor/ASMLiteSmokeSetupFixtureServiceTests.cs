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
        public void UnselectedInactiveAvatarMutation_PrefersActiveMatchWhenInactiveDuplicateExists()
        {
            GameObject activeDuplicate = null;
            _ctx.AvatarGo.SetActive(false);
            Selection.activeObject = null;
            var args = new ASMLiteSmokeStepArgs
            {
                avatarName = "FixtureAvatar",
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.UnselectedInactiveAvatar,
            };

            try
            {
                activeDuplicate = new GameObject("FixtureAvatar");
                activeDuplicate.AddComponent<VRCAvatarDescriptor>();

                bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

                Assert.That(applied, Is.True, detail);
                Assert.That(_ctx.AvatarGo.activeSelf, Is.False,
                    "The earlier inactive duplicate should stay inactive.");
                Assert.That(activeDuplicate.activeSelf, Is.False,
                    "The unselected inactive mutation must deactivate the active scene avatar match, not stop at an earlier inactive duplicate.");
                Assert.That(Selection.activeObject, Is.Null);

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string resolveDetail);

                Assert.That(resolved, Is.False, resolveDetail);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_NOT_FOUND", resolveDetail);
            }
            finally
            {
                if (activeDuplicate != null)
                    UnityEngine.Object.DestroyImmediate(activeDuplicate);
            }
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

        [Test]
        public void MissingGeneratedFolderMutation_RemovesGeneratedFolderAndRestoresBaseline()
        {
            try
            {
                EnsureTestAssetFolder("Assets", "ASM-Lite");
                EnsureTestAssetFolder("Assets/ASM-Lite", "FixtureAvatar");
                EnsureTestAssetFolder("Assets/ASM-Lite/FixtureAvatar", "GeneratedAssets");
                Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.True);

                var args = new ASMLiteSmokeStepArgs
                {
                    avatarName = "FixtureAvatar",
                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.MissingGeneratedFolder,
                };

                bool applied = _service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

                Assert.That(applied, Is.True, detail);
                Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.False);

                Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);
                Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.True);
            }
            finally
            {
                DeleteTestAssetIfExists("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets");
                DeleteTestAssetIfExists("Assets/ASM-Lite/FixtureAvatar");
                DeleteTestAssetIfExists("Assets/ASM-Lite");
            }
        }

        [Test]
        public void VendorizedStateBaselineMutation_MarksComponentVendorizedAndRestoresPackageManagedState()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline,
            };

            Assert.That(_service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.Comp.useVendorizedGeneratedAssets, Is.True);
            Assert.That(_ctx.Comp.vendorizedGeneratedAssetsPath, Does.Contain(_ctx.AvatarGo.name));

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.Comp.useVendorizedGeneratedAssets, Is.False);
            Assert.That(_ctx.Comp.vendorizedGeneratedAssetsPath, Is.Empty);
        }

        [Test]
        public void VendorizedStateBaselineMutation_RemovesTemporaryComponentWhenBaselineWasMissing()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);

            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline,
            };

            Assert.That(_service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
        }

        [Test]
        public void DetachedStateBaselineMutation_RemovesComponentAndRestoresDetachedMarker()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline,
            };

            Assert.That(_service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(_ctx.ParamsAsset.parameters.Any(item => item != null && item.name == "ASMLite_FixtureDetached"), Is.True);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);
            Assert.That(_ctx.ParamsAsset.parameters.Any(item => item != null && item.name == "ASMLite_FixtureDetached"), Is.False);
        }

        [Test]
        public void CleanAddBaselineMutation_RemovesComponentAndDetachedRuntimeMarkers()
        {
            var existingParameters = _ctx.ParamsAsset.parameters ?? new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter[0];
            _ctx.ParamsAsset.parameters = existingParameters
                .Concat(new[]
                {
                    new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                    {
                        name = ASMLite.Editor.ASMLiteBuilder.CtrlParam,
                        valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int,
                        defaultValue = 0f,
                        saved = false,
                        networkSynced = false,
                    },
                })
                .ToArray();
            EditorUtility.SetDirty(_ctx.ParamsAsset);

            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;

            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.Detached,
                ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                "Setup must model the real smoke failure shape: component-missing recovery markers should classify as Detached before the clean baseline mutation runs.");

            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline,
            };

            Assert.That(_service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteWindow.AsmLiteToolState.NotInstalled,
                ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                "Clean add baseline must remove detached ASM-Lite runtime markers so Add Prefab becomes the real primary action.");

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null,
                "Reset must restore the original detached shape when the mutation started from a component-missing baseline.");
            Assert.That(_ctx.ParamsAsset.parameters.Any(item => item != null && item.name == ASMLite.Editor.ASMLiteBuilder.CtrlParam), Is.True,
                "Reset must restore the original detached marker evidence after the clean baseline mutation is cleaned up.");
            Assert.That(_service.HasCleanResetProof, Is.True);
        }

        [Test]
        public void GeneratedFolderWithoutComponentMutation_CreatesFolderAndRemovesComponent()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent,
            };

            Assert.That(_service.ApplyMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite"), Is.True);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);
            Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.False);
        }

        private static void EnsureTestAssetFolder(string parent, string child)
        {
            string path = parent.TrimEnd('/') + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static void DeleteTestAssetIfExists(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
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
