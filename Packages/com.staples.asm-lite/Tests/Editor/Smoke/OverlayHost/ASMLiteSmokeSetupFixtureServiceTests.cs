using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Smoke")]
    [Category("Headless")]
    public class ASMLiteSmokeSetupFixtureServiceTests
    {
        private AsmLiteTestContext _ctx;
        private ASMLiteSmokeSetupFixtureService _service;
        private readonly System.Collections.Generic.List<GameObject> _testOwnedObjects = new System.Collections.Generic.List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/FixtureBaseline.unity"), Is.True);
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            _ctx.AvatarGo.name = "FixtureAvatar";
            _service = new ASMLiteSmokeSetupFixtureService();
        }

        [TearDown]
        public void TearDown()
        {
            _service?.Reset(out _);
            string recovery = _service?.RecoveryPath;
            if (!string.IsNullOrEmpty(recovery) && Directory.Exists(recovery))
            {
                string archive = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(recovery)), "fixture-test-failure-evidence");
                Directory.CreateDirectory(archive);
                Directory.Move(recovery, Path.Combine(archive, Path.GetFileName(recovery)));
            }
            _service = null;
            foreach (GameObject value in _testOwnedObjects)
                if (value != null) UnityEngine.Object.DestroyImmediate(value);
            _testOwnedObjects.Clear();
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

            bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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

            bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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

            bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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

            bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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

                bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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

            bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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
                bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", evidenceRoot, out string detail);

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

                bool applied = ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail);

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

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
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

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
        }

        [Test]
        public void StaleVendorizedReferencesMutation_LeavesDetachedReferenceAndRestoresOriginal()
        {
            var original = _ctx.AvDesc.expressionParameters;
            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.StaleVendorizedReferences,
            };

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(_ctx.AvDesc.expressionParameters, Is.Not.SameAs(original));
            Assert.That(AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters),
                Does.StartWith("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets/"));
            Assert.That(ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                Is.EqualTo(ASMLite.Editor.ASMLiteInstallationState.Vendorized));

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);
            Assert.That(_ctx.AvDesc.expressionParameters, Is.SameAs(original));
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);
            Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.False);
        }

        [Test]
        public void DetachedStateBaselineMutation_RemovesComponentAndRestoresDetachedMarker()
        {
            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline,
            };

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(_ctx.ParamsAsset.parameters.Any(item => item != null && item.name == "ASMLite_FixtureDetached"), Is.True);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);
            Assert.That(_ctx.ParamsAsset.parameters.Any(item => item != null && item.name == "ASMLite_FixtureDetached"), Is.False);
        }

        [Test]
        public void DetachedStateBaselineMutation_WorksWithoutExistingComponent()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
            _ctx.Comp = null;
            int originalParameterCount = _ctx.ParamsAsset.parameters?.Length ?? 0;
            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline,
            };

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(_ctx.ParamsAsset.parameters.Any(item => item != null && item.name == "ASMLite_FixtureDetached"), Is.True);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(_ctx.ParamsAsset.parameters.Length, Is.EqualTo(originalParameterCount));
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
                ASMLite.Editor.ASMLiteInstallationState.Detached,
                ASMLite.Editor.ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                "Setup must model the real smoke failure shape: component-missing recovery markers should classify as Detached before the clean baseline mutation runs.");

            var args = new ASMLiteSmokeStepArgs
            {
                fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline,
            };

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.AreEqual(
                ASMLite.Editor.ASMLiteInstallationState.NotInstalled,
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

            Assert.That(ApplyAdmittedMutation(args, "Assets/Click ME.unity", "FixtureAvatar", out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Null);
            Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite"), Is.True);

            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);

            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(includeInactive: true), Is.Not.Null);
            Assert.That(AssetDatabase.IsValidFolder("Assets/ASM-Lite/FixtureAvatar/GeneratedAssets"), Is.False);
        }

        [Test]
        public void DirtyCoveredScene_IsRejectedBeforeRecoveryPublicationOrMutation()
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/FixtureBaseline.unity");
            _ctx.AvatarGo.transform.localPosition = new Vector3(7, 8, 9);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            var args = new ASMLiteSmokeStepArgs { avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent };
            Assert.That(_service.ApplyMutation(args, string.Empty, "FixtureAvatar", out string detail), Is.False);
            StringAssert.Contains("saved and clean", detail);
            Assert.That(_service.RecoveryPath, Is.Empty);
            Assert.That(_ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(true), Is.Not.Null);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);
        }

        [Test]
        public void CorruptRecoveryCopy_LatchesFailureAndEmptyResetCannotProveCleanup()
        {
            var earlierCleanService = new ASMLiteSmokeSetupFixtureService();
            Assert.That(earlierCleanService.Reset(out _), Is.True);
            Assert.That(earlierCleanService.HasCleanResetProof, Is.True);
            var args = new ASMLiteSmokeStepArgs { avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent };
            Assert.That(ApplyAdmittedMutation(args, string.Empty, "FixtureAvatar", out string applyDetail), Is.True, applyDetail);
            string copy = Directory.GetFiles(_service.RecoveryPath, "scene-*.unity").Single();
            File.AppendAllText(copy, "corruption");
            Assert.That(earlierCleanService.Reset(out _), Is.False);
            Assert.That(earlierCleanService.HasCleanResetProof, Is.False);
            Assert.That(_service.Reset(out _), Is.False);
            Assert.That(_service.Reset(out _), Is.False);
            Assert.That(_service.HasCleanResetProof, Is.False);
            Assert.That(new ASMLiteSmokeSetupFixtureService().Reset(out _), Is.False);
            Assert.That(ASMLiteSmokeSetupFixtureService.CheckRecoveryAdmission(out _), Is.False);
        }

        [Test]
        public void UnrelatedDirtySceneChange_IsPreservedAndBlocksTargetSave()
        {
            var unrelated = new GameObject("UnrelatedActionState");
            var args = new ASMLiteSmokeStepArgs { avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent };
            Assert.That(ApplyAdmittedMutation(args, string.Empty, "FixtureAvatar", out string applyDetail), Is.True, applyDetail);
            unrelated.transform.position = new Vector3(9, 8, 7);
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Assert.That(_service.Reset(out string detail), Is.False);
            StringAssert.Contains("unrelated dirty scene", detail);
            Assert.That(unrelated.transform.position, Is.EqualTo(new Vector3(9, 8, 7)));
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);
            UnityEngine.Object.DestroyImmediate(unrelated);
        }

        [Test]
        public void FirstBaselineSurvivesRepeatedMutation_AndFullSelectionIsRestored()
        {
            Selection.activeObject = _ctx.Comp.gameObject;
            Selection.objects = new UnityEngine.Object[] { _ctx.Comp.gameObject, _ctx.AvatarGo };
            Assert.That(Selection.objects.Length, Is.EqualTo(2));
            UnityEngine.Object[] selection = Selection.objects;
            UnityEngine.Object active = Selection.activeObject;
            var args = new ASMLiteSmokeStepArgs { avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.SelectedInactiveAvatar };
            Assert.That(ApplyAdmittedMutation(args, string.Empty, "FixtureAvatar", out string applyDetail), Is.True, applyDetail);
            Assert.That(_service.ApplyMutation(args, string.Empty, "FixtureAvatar", out string repeatDetail), Is.True, repeatDetail);
            Assert.That(_service.Reset(out string detail), Is.True, detail);
            Assert.That(_ctx.AvatarGo.activeSelf, Is.True);
            CollectionAssert.AreEqual(selection, Selection.objects);
            Assert.That(Selection.activeObject, Is.SameAs(active));
        }

        [Test]
        public void RemovedHierarchy_RestoresNonDefaultSdkManagedDataAndInboundReferences_PreservesSavedUnrelatedState()
        {
            Transform root = _ctx.Comp.transform;
            root.localPosition = new Vector3(1, 2, 3);
            root.localScale = new Vector3(2, 3, 4);
            var child = new GameObject("Duplicate");
            child.transform.SetParent(root, false);
            child.SetActive(false);
            var otherChild = new GameObject("Duplicate");
            otherChild.transform.SetParent(root, false);
            var unrelated = new GameObject("Unrelated");
            _testOwnedObjects.Add(unrelated);
            var outside = unrelated.AddComponent<ASMLiteSmokeFixtureReferenceProbe>();
            outside.internalReference = child;
            outside.number = 11;
            var inside = root.gameObject.AddComponent<ASMLiteSmokeFixtureReferenceProbe>();
            inside.number = 73;
            inside.text = "non-default";
            inside.internalReference = child;
            inside.externalReference = unrelated;
            inside.data = new ASMLiteSmokeFixtureReferenceProbe.Graph { value = 19, reference = child };
            inside.data.next = inside.data;
            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = "captured-non-default";
            // Migration tests define an editor-only stub with the same full name.
            // Native recovery must use the installed component, not that stub.
            System.Type vfType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly != typeof(VF.Model.VRCFury).Assembly)
                .Select(assembly => assembly.GetType("VF.Model.VRCFury")).FirstOrDefault(type => type != null);
            Assert.That(vfType, Is.Not.Null, "Native VRCFury dependency is required for this integration check.");
            Component vf = root.gameObject.AddComponent(vfType);
            var vfState = new SerializedObject(vf);
            vfState.FindProperty("content").managedReferenceValue = System.Activator.CreateInstance(
                vfType.Assembly.GetType("VF.Model.Feature.FullController"), true);
            vfState.ApplyModifiedPropertiesWithoutUndo();
            vfState.Update();
            vfState.FindProperty("content.toggleParam").stringValue = "CapturedToggle";
            vfState.FindProperty("content.rootObjOverride").objectReferenceValue = child;
            vfState.FindProperty("content.allowMissingAssets").boolValue = true;
            vfState.ApplyModifiedPropertiesWithoutUndo();
            Selection.activeObject = child;
            Selection.objects = new UnityEngine.Object[] { child, root.gameObject, inside };
            Assert.That(Selection.objects.Length, Is.EqualTo(3));
            Assert.That(Selection.activeObject, Is.SameAs(child));
            var args = new ASMLiteSmokeStepArgs { avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent };
            Assert.That(ApplyAdmittedMutation(args, string.Empty, "FixtureAvatar", out string applyDetail), Is.True, applyDetail);
            outside.number = 29;
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Assert.That(_service.Reset(out string detail), Is.True, detail);
            var restored = _ctx.AvatarGo.GetComponentInChildren<ASMLiteComponent>(true);
            var restoredProbe = restored.GetComponent<ASMLiteSmokeFixtureReferenceProbe>();
            Assert.That(restoredProbe.number, Is.EqualTo(73));
            Assert.That(restoredProbe.text, Is.EqualTo("non-default"));
            Assert.That(restoredProbe.data.next, Is.SameAs(restoredProbe.data));
            Assert.That(restoredProbe.internalReference, Is.SameAs(restoredProbe.data.reference));
            Assert.That(outside.internalReference, Is.SameAs(restoredProbe.internalReference));
            Assert.That(outside.number, Is.EqualTo(29));
            Assert.That(restored.transform.localPosition, Is.EqualTo(new Vector3(1, 2, 3)));
            Assert.That(restored.transform.localScale, Is.EqualTo(new Vector3(2, 3, 4)));
            Assert.That(restored.vendorizedGeneratedAssetsPath, Is.EqualTo("captured-non-default"));
            var restoredVf = new SerializedObject(restored.GetComponent(vfType));
            Assert.That(restoredVf.FindProperty("content.toggleParam").stringValue, Is.EqualTo("CapturedToggle"));
            Assert.That(restoredVf.FindProperty("content.rootObjOverride").objectReferenceValue, Is.SameAs(restoredProbe.internalReference));
            Assert.That(Selection.objects.Length, Is.EqualTo(3));
            Assert.That(Selection.activeObject, Is.SameAs(restoredProbe.internalReference));
            UnityEngine.Object.DestroyImmediate(unrelated);
        }

        [Test]
        public void ExplicitVerification_IsComparisonOnly_AndClearsLatchOnlyAfterSavedBaselineProof()
        {
            var args = new ASMLiteSmokeStepArgs { avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.SelectedInactiveAvatar };
            Assert.That(ApplyAdmittedMutation(args, string.Empty, "FixtureAvatar", out string applyDetail), Is.True, applyDetail);
            string recoveryPath = _service.RecoveryPath;
            byte[] sceneBytes = File.ReadAllBytes(Path.GetFullPath(SceneManager.GetActiveScene().path));
            UnityEngine.Object[] selection = Selection.objects;
            UnityEngine.Object active = Selection.activeObject;
            Assert.That(ASMLiteSmokeSetupFixtureService.VerifyRepairedFixtureBaseline(out _), Is.False);
            Assert.That(_ctx.AvatarGo.activeSelf, Is.False);
            CollectionAssert.AreEqual(sceneBytes, File.ReadAllBytes(Path.GetFullPath(SceneManager.GetActiveScene().path)));
            CollectionAssert.AreEqual(selection, Selection.objects);
            Assert.That(Selection.activeObject, Is.SameAs(active));
            Assert.That(File.Exists(Path.Combine(recoveryPath, "verified.json")), Is.False);
            // Operator repair is separate, explicit, and saved.
            _ctx.AvatarGo.SetActive(true);
            Selection.activeObject = null;
            Selection.objects = new UnityEngine.Object[0];
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            // Capture starts with this test's empty selection.
            Assert.That(ASMLiteSmokeSetupFixtureService.VerifyRepairedFixtureBaseline(out string detail), Is.True, detail);
            Assert.That(ASMLiteSmokeSetupFixtureService.CheckRecoveryAdmission(out _), Is.True);
            Assert.That(_service.Reset(out string resetDetail), Is.True, resetDetail);
            Assert.That(_service.HasCleanResetProof, Is.True);
        }

        [Test]
        public void ImportedShaderDirtyFlag_DoesNotBypassUnrelatedDirtyMaterialProtection()
        {
            string shaderPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLite_DirtyImportedControl.shader");
            string materialPath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLite_DirtyNativeControl.mat");
            try
            {
                File.WriteAllText(Path.GetFullPath(shaderPath), "Shader \"Hidden/ASMLite/DirtyImportedControl\" { SubShader { Pass {} } }");
                AssetDatabase.ImportAsset(shaderPath, ImportAssetOptions.ForceSynchronousImport);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
                Assert.That(shader, Is.Not.Null);
                Assert.That(AssetDatabase.IsNativeAsset(shader), Is.False);
                var material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
                AssetDatabase.SaveAssets();
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/FixtureBaseline.unity"), Is.True);
                byte[] materialBytes = File.ReadAllBytes(Path.GetFullPath(materialPath));
                byte[] shaderBytes = File.ReadAllBytes(Path.GetFullPath(shaderPath));
                byte[] shaderMeta = File.ReadAllBytes(Path.GetFullPath(shaderPath + ".meta"));
                EditorUtility.SetDirty(shader);
                material.renderQueue = 3001;
                EditorUtility.SetDirty(material);
                var args = new ASMLiteSmokeStepArgs
                {
                    avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline,
                };

                Assert.That(_service.ApplyMutation(args, string.Empty, "FixtureAvatar", string.Empty, out string blocked), Is.False);
                StringAssert.Contains(materialPath, blocked, "Admission must identify the unsaved native material, not a compiler-dirty imported shader.");
                CollectionAssert.AreEqual(materialBytes, File.ReadAllBytes(Path.GetFullPath(materialPath)));
                Assert.That(EditorUtility.IsDirty(material), Is.True);
                Assert.That(AssetDatabase.DeleteAsset(materialPath), Is.True);

                Assert.That(_service.ApplyMutation(args, string.Empty, "FixtureAvatar", string.Empty, out string admitted), Is.True, admitted);
                Assert.That(_service.Reset(out string restored), Is.True, restored);
                CollectionAssert.AreEqual(shaderBytes, File.ReadAllBytes(Path.GetFullPath(shaderPath)));
                CollectionAssert.AreEqual(shaderMeta, File.ReadAllBytes(Path.GetFullPath(shaderPath + ".meta")));
            }
            finally
            {
                DeleteTestAssetIfExists(materialPath);
                DeleteTestAssetIfExists(shaderPath);
            }
        }

        private bool ApplyAdmittedMutation(ASMLiteSmokeStepArgs args, string scene, string avatar, out string detail)
        {
            return ApplyAdmittedMutation(args, scene, avatar, string.Empty, out detail);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void GeneratedGraphFootprint_RealAddRebuildReset_RestoresBytesGuidsAndPriorAbsence(bool existingGraph)
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            string unrelated = "Assets/ASM-Lite/UnrelatedOwner_" + System.Guid.NewGuid().ToString("N");
            try
            {
                EnsureTestAssetFolder("Assets", "ASM-Lite");
                EnsureTestAssetFolder("Assets/ASM-Lite", Path.GetFileName(unrelated));
                string sentinel = unrelated + "/sentinel.txt";
                File.WriteAllText(Path.GetFullPath(sentinel), "preserve other owner");
                AssetDatabase.ImportAsset(sentinel, ImportAssetOptions.ForceSynchronousImport);
                byte[] sentinelMeta = File.ReadAllBytes(Path.GetFullPath(sentinel + ".meta"));
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
                string working = ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(_ctx.AvDesc);
                string owner = Path.GetDirectoryName(working).Replace('\\', '/');
                string generated = owner + "/GeneratedAssets";
                if (existingGraph)
                {
                    var staged = ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(_ctx.AvDesc, workingAssets: true);
                    Assert.That(staged.Success, Is.True, staged.ToLogString());
                    Assert.That(ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(staged).Success, Is.True);
                }
                if (!existingGraph)
                {
                    Object.DestroyImmediate(_ctx.Comp.gameObject);
                    _ctx.Comp = null;
                }
                AssetDatabase.SaveAssets();
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
                string descriptorBefore = EditorJsonUtility.ToJson(_ctx.AvDesc);
                Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.Null);
                Object[] baselineSelection = Selection.objects;
                Object baselineActive = Selection.activeObject;
                var before = Directory.Exists(Path.GetFullPath(owner))
                    ? Directory.GetFiles(Path.GetFullPath(owner), "*", SearchOption.AllDirectories)
                        .Concat(new[] { Path.GetFullPath(owner + ".meta") }).ToDictionary(path => path, File.ReadAllBytes)
                    : new System.Collections.Generic.Dictionary<string, byte[]>();
                string controllerGuid = AssetDatabase.AssetPathToGUID(working + "/ASMLite_FX.controller");
                Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string capture), Is.True, capture);
                string recovery = _service.RecoveryPath;
                Assert.That(File.Exists(Path.Combine(recovery, "pending-0000.json")), Is.True);
                if (existingGraph)
                {
                    Assert.That(_service.ApplyMutation(new ASMLiteSmokeStepArgs
                    {
                        avatarName = "FixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline,
                    }, string.Empty, "FixtureAvatar", out string clean), Is.True, clean);
                }
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.AddPrefabForAutomation();
                Assert.That(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true), Is.Not.Null);
                Assert.That(AssetDatabase.IsValidFolder(working), Is.True);
                // Exercise the stock SDK inspector reached by Add's avatar-selection restore,
                // even in a synchronous EditMode run without an Inspector repaint.
                var descriptorEditor = UnityEditor.Editor.CreateEditor(_ctx.AvDesc);
                Object.DestroyImmediate(descriptorEditor);
                Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.Not.Null);
                // A second admission must not replace the pre-add bytes or component absence.
                Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string extension), Is.True, extension);
                window.RebuildForAutomation();
                Assert.That(AssetDatabase.IsValidFolder(generated), Is.True, "Real rebuild must exercise the sibling generated graph.");
                // Keep a real tracked SDK inspector alive, not just a throwaway editor.
                ActiveEditorTracker.sharedTracker.ForceRebuild();
                Assert.That(ActiveEditorTracker.sharedTracker.activeEditors.Any(editor => editor != null && editor.target == _ctx.AvDesc), Is.True);
                _service.RecoveryPhaseForTesting = phase =>
                {
                    if (phase == "before-unrelated-dirty-comparison" || phase == "target-scenes-saved")
                    {
                        Assert.That(Selection.objects, Is.Empty, phase);
                        Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.Null, phase);
                        Assert.That(EditorJsonUtility.ToJson(_ctx.AvDesc), Is.EqualTo(descriptorBefore), phase);
                    }
                };
                Assert.That(_service.Reset(out string reset), Is.True, reset);
                Assert.That(_service.HasCleanResetProof, Is.True);
                Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.Null);
                Assert.That(EditorJsonUtility.ToJson(_ctx.AvDesc), Is.EqualTo(descriptorBefore));
                CollectionAssert.AreEqual(baselineSelection, Selection.objects);
                Assert.That(Selection.activeObject, Is.SameAs(baselineActive));
                Assert.That(AssetDatabase.IsValidFolder(generated), Is.False);
                Assert.That(File.Exists(Path.GetFullPath(generated + ".meta")), Is.False);
                if (existingGraph)
                {
                    CollectionAssert.AreEquivalent(before.Keys, Directory.GetFiles(Path.GetFullPath(owner), "*", SearchOption.AllDirectories)
                        .Concat(new[] { Path.GetFullPath(owner + ".meta") }));
                    foreach (var file in before) CollectionAssert.AreEqual(file.Value, File.ReadAllBytes(file.Key), file.Key);
                    Assert.That(AssetDatabase.AssetPathToGUID(working + "/ASMLite_FX.controller"), Is.EqualTo(controllerGuid));
                }
                else
                {
                    Assert.That(Directory.Exists(Path.GetFullPath(owner)), Is.False);
                    Assert.That(File.Exists(Path.GetFullPath(owner + ".meta")), Is.False);
                }
                Assert.That(File.ReadAllText(Path.GetFullPath(sentinel)), Is.EqualTo("preserve other owner"));
                CollectionAssert.AreEqual(sentinelMeta, File.ReadAllBytes(Path.GetFullPath(sentinel + ".meta")));
                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            }
            finally
            {
                Object.DestroyImmediate(window);
                AssetDatabase.DeleteAsset(unrelated);
            }
        }

        [Test]
        public void GeneratedGraphFootprint_OperatorPipelineData_BlocksResetWithoutDeletingIt()
        {
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
            Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.Null);
            Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string capture), Is.True, capture);
            var descriptorEditor = UnityEditor.Editor.CreateEditor(_ctx.AvDesc);
            Object.DestroyImmediate(descriptorEditor);
            var pipeline = _ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>();
            Assert.That(pipeline, Is.Not.Null);
            pipeline.blueprintId = "avtr_operator_data";
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeObject = _ctx.AvatarGo;
            Selection.objects = new Object[] { _ctx.AvatarGo, _ctx.Comp.gameObject };
            Object[] operatorSelection = Selection.objects;
            Object operatorActive = Selection.activeObject;
            Assert.That(_service.Reset(out string blocked), Is.False);
            StringAssert.Contains("unrelated pipeline data", blocked);
            Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.SameAs(pipeline));
            Assert.That(pipeline.blueprintId, Is.EqualTo("avtr_operator_data"));
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.True);
            Assert.That(_service.HasCleanResetProof, Is.False);
            CollectionAssert.AreEqual(operatorSelection, Selection.objects);
            Assert.That(Selection.activeObject, Is.SameAs(operatorActive));
        }

        [Test]
        public void GeneratedGraphFootprint_LiveDescriptorEditor_BlocksResetWithoutDestroyingIt()
        {
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
            Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string capture), Is.True, capture);
            // This editor is not owned by the selection tracker. Recovery must not destroy
            // it or change inspector locks to make an absent-pipeline baseline pass.
            var descriptorEditor = UnityEditor.Editor.CreateEditor(_ctx.AvDesc);
            try
            {
                var pipeline = _ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>();
                bool inspectorLocked = ActiveEditorTracker.sharedTracker.isLocked;
                Assert.That(pipeline, Is.Not.Null);
                Assert.That(_service.Reset(out string blocked), Is.False);
                StringAssert.Contains("editor lifetime is not controlled by selection", blocked);
                Assert.That(descriptorEditor != null, Is.True);
                Assert.That(descriptorEditor.target, Is.SameAs(_ctx.AvDesc));
                Assert.That(_ctx.AvDesc.GetComponent<VRC.Core.PipelineManager>(), Is.SameAs(pipeline));
                Assert.That(ActiveEditorTracker.sharedTracker.isLocked, Is.EqualTo(inspectorLocked));
                Assert.That(_service.HasCleanResetProof, Is.False);
            }
            finally { Object.DestroyImmediate(descriptorEditor); }
        }

        [Test]
        public void GeneratedGraphFootprint_UnownedOwnerContents_FailsBeforePublishingOrMutating()
        {
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
            string working = ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(_ctx.AvDesc);
            string owner = Path.GetDirectoryName(working).Replace('\\', '/');
            EnsureTestAssetFolder("Assets", "ASM-Lite");
            EnsureTestAssetFolder("Assets/ASM-Lite", Path.GetFileName(owner));
            string sentinel = owner + "/unrelated.txt";
            File.WriteAllText(Path.GetFullPath(sentinel), "do not claim");
            AssetDatabase.ImportAsset(sentinel, ImportAssetOptions.ForceSynchronousImport);
            Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string detail), Is.False);
            StringAssert.Contains("ownership", detail);
            Assert.That(_service.RecoveryPath, Is.Empty);
            Assert.That(File.ReadAllText(Path.GetFullPath(sentinel)), Is.EqualTo("do not claim"));
        }

        private bool ApplyAdmittedMutation(ASMLiteSmokeStepArgs args, string scene, string avatar, string evidence, out string detail)
        {
            // Explicit test preparation: production admission never saves a dirty baseline.
            AssetDatabase.SaveAssets();
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/FixtureBaseline.unity"), Is.True);
            return _service.ApplyMutation(args, scene, avatar, evidence, out detail);
        }

        [Test]
        public void GeneratedGraphFootprint_CorruptPendingPublication_BlocksNextAdmission()
        {
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
            Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string capture), Is.True, capture);
            File.WriteAllText(Path.Combine(_service.RecoveryPath, "pending-0000.json"), "invalid pending publication");
            Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string blocked), Is.False);
            StringAssert.Contains("pending publication", blocked);
            Assert.That(_service.HasCleanResetProof, Is.False);
        }

        [Test]
        public void GeneratedGraphFootprint_ForeignOwnerReference_RefusesCapture()
        {
            var other = new GameObject("ForeignGraphOwner");
            string owner = null;
            try
            {
                var avatar = other.AddComponent<VRCAvatarDescriptor>();
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
                var graph = ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(avatar);
                Assert.That(graph.Success, Is.True, graph.ToLogString());
                Assert.That(ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(graph).Success, Is.True);
                owner = Path.GetDirectoryName(graph.TargetPath).Replace('\\', '/');
                string parameters = graph.TargetPath + "/ASMLite_Params.asset";
                byte[] bytes = File.ReadAllBytes(Path.GetFullPath(parameters));
                byte[] meta = File.ReadAllBytes(Path.GetFullPath(parameters + ".meta"));
                _ctx.AvDesc.expressionParameters = AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>(parameters);
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
                Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string blocked), Is.False);
                StringAssert.Contains("foreign descriptor reference", blocked);
                Assert.That(_service.RecoveryPath, Is.Empty);
                CollectionAssert.AreEqual(bytes, File.ReadAllBytes(Path.GetFullPath(parameters)));
                CollectionAssert.AreEqual(meta, File.ReadAllBytes(Path.GetFullPath(parameters + ".meta")));
            }
            finally
            {
                Object.DestroyImmediate(other);
                if (owner != null) AssetDatabase.DeleteAsset(owner);
            }
        }

        [Test]
        public void GeneratedGraphFootprint_UnrelatedFileAddedAfterCapture_BlocksResetWithoutDeletingIt()
        {
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
            Assert.That(_service.CaptureGeneratedGraphFootprint(_ctx.AvDesc, out string capture), Is.True, capture);
            string working = ASMLite.Editor.ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(_ctx.AvDesc);
            string owner = Path.GetDirectoryName(working).Replace('\\', '/');
            Directory.CreateDirectory(Path.GetFullPath(owner));
            string sentinel = owner + "/operator.txt";
            File.WriteAllText(Path.GetFullPath(sentinel), "do not delete new unrelated work");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Assert.That(_service.Reset(out string blocked), Is.False);
            StringAssert.Contains("ownership", blocked);
            Assert.That(File.ReadAllText(Path.GetFullPath(sentinel)), Is.EqualTo("do not delete new unrelated work"));
            Assert.That(_service.HasCleanResetProof, Is.False);
        }

        private static void EnsureTestAssetFolder(string parent, string child)
        {
            string normalizedParent = (parent ?? string.Empty).Trim().Replace('\\', '/').TrimEnd('/');
            string path = normalizedParent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(normalizedParent, child);

            string relative = path.StartsWith("Assets/", System.StringComparison.Ordinal)
                ? path.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar)
                : path;
            string fullPath = Path.Combine(Application.dataPath, relative);
            Directory.CreateDirectory(fullPath);

            string metaPath = fullPath + ".meta";
            if (!File.Exists(metaPath))
            {
                File.WriteAllText(metaPath,
                    "fileFormatVersion: 2\n"
                    + $"guid: {System.Guid.NewGuid():N}\n"
                    + "folderAsset: yes\n"
                    + "DefaultImporter:\n"
                    + "  externalObjects: {}\n"
                    + "  userData:\n"
                    + "  assetBundleName:\n"
                    + "  assetBundleVariant:\n");
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
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
