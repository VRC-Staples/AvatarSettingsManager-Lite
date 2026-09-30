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

        private bool ApplyAdmittedMutation(ASMLiteSmokeStepArgs args, string scene, string avatar, out string detail)
        {
            return ApplyAdmittedMutation(args, scene, avatar, string.Empty, out detail);
        }

        private bool ApplyAdmittedMutation(ASMLiteSmokeStepArgs args, string scene, string avatar, string evidence, out string detail)
        {
            // Explicit test preparation: production admission never saves a dirty baseline.
            AssetDatabase.SaveAssets();
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), "Assets/FixtureBaseline.unity"), Is.True);
            return _service.ApplyMutation(args, scene, avatar, evidence, out detail);
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
