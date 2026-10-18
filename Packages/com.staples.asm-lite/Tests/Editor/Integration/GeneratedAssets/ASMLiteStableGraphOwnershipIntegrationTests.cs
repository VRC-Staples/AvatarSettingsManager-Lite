using System.IO;
using ASMLite.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteStableGraphOwnershipIntegrationTests
    {
        [Test]
        public void RealRebuildAfterRename_ReusesOwnerGraph_AndAttachedReturnDeletesOnlyItsMirror()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLiteOwnershipActions.unity");
            // This intentional saved scene must be the fixture's open-scene baseline.
            Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath), Is.True);
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            var window = ScriptableObject.CreateInstance<ASMLiteWindow>();
            try
            {
                Object.DestroyImmediate(ctx.Comp.gameObject);
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath), Is.True);
                window.SelectAvatarForAutomation(ctx.AvDesc);
                window.AddPrefabForAutomation();
                ctx.Comp = ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.That(ctx.Comp, Is.Not.Null);
                string working = ASMLiteAssetPaths.ResolveGeneratedDirectory(ctx.Comp);
                StringAssert.StartsWith("Assets/ASM-Lite/", working);
                ctx.AvatarGo.name = "RenamedOwner";
                window.RebuildForAutomation();
                Assert.That(ASMLiteAssetPaths.ResolveGeneratedDirectory(ctx.Comp), Is.EqualTo(working));
                string generated = Path.GetDirectoryName(working).Replace('\\', '/') + "/GeneratedAssets";
                Assert.That(ctx.Comp.vendorizedGeneratedAssetsPath, Is.EqualTo(generated));
                Assert.That(ASMLiteGeneratedAssetMirrorService.ValidateVendorizedFolderForDelete(ctx.AvDesc, generated).Success, Is.True);
                window.ReturnToPackageManagedForAutomation();
                Assert.That(ctx.Comp.useVendorizedGeneratedAssets, Is.False);
                Assert.That(AssetDatabase.IsValidFolder(generated), Is.False);
                Assert.That(ASMLiteGeneratedAssetMirrorService.IsOwnedWorkingFolder(ctx.AvDesc, working), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(window);
                ASMLiteTestFixtures.TearDownTestAvatar(ctx.AvatarGo);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [Test]
        public void SavedOwner_RenameAndReload_PreservesLegacyGraphAndRejectsSameNameAvatar()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var avatarGo = new GameObject("TestAvatar");
            var avatar = avatarGo.AddComponent<VRCAvatarDescriptor>();
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLiteOwnership.unity");
            string ownerFolder = null;
            GameObject other = null;
            try
            {
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath), Is.True);
                var mirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(avatar);
                Assert.That(mirror.Success, Is.True, mirror.ToLogString());
                Assert.That(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(mirror).Success, Is.True);
                var working = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(avatar, workingAssets: true);
                Assert.That(working.Success, Is.True, working.ToLogString());
                Assert.That(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(working).Success, Is.True);
                ownerFolder = Path.GetDirectoryName(mirror.TargetPath).Replace('\\', '/');
                string legacyFolder = Path.GetDirectoryName(ownerFolder).Replace('\\', '/') + "/BeforeRename__"
                    + Path.GetFileName(ownerFolder).Split(new[] { "__" }, System.StringSplitOptions.None)[1];
                Assert.That(AssetDatabase.MoveAsset(ownerFolder, legacyFolder), Is.Empty);
                ownerFolder = legacyFolder;
                string generated = ownerFolder + "/GeneratedAssets";
                string guid = AssetDatabase.AssetPathToGUID(generated + "/ASMLite_FX.controller");
                avatarGo.name = "AfterRename";
                Assert.That(ASMLiteGeneratedAssetMirrorService.ValidateVendorizedFolderForDelete(avatar, generated).Success, Is.True);
                Assert.That(ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(avatar), Is.EqualTo(ownerFolder + "/WorkingAssets"));
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
                string id = GlobalObjectId.GetGlobalObjectIdSlow(avatarGo).ToString();
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                Assert.That(GlobalObjectId.TryParse(id, out var parsed), Is.True);
                avatarGo = (GameObject)GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed);
                avatar = avatarGo.GetComponent<VRCAvatarDescriptor>();
                Assert.That(ASMLiteGeneratedAssetMirrorService.ValidateVendorizedFolderForDelete(avatar, generated).Success, Is.True);
                Assert.That(ASMLiteGeneratedAssetMirrorService.IsOwnedWorkingFolder(avatar, ownerFolder + "/WorkingAssets"), Is.True);
                Assert.That(AssetDatabase.AssetPathToGUID(generated + "/ASMLite_FX.controller"), Is.EqualTo(guid));
                other = new GameObject(avatarGo.name);
                var otherAvatar = other.AddComponent<VRCAvatarDescriptor>();
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene()), Is.True);
                Assert.That(ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(otherAvatar), Is.Not.EqualTo(ownerFolder + "/WorkingAssets"));
                Assert.That(ASMLiteGeneratedAssetMirrorService.ValidateVendorizedFolderForDelete(otherAvatar, generated).Success, Is.False);
                var deletion = ASMLiteGeneratedAssetMirrorService.BackupVendorizedFolderForDelete(avatar, generated);
                Assert.That(deletion.Success, Is.True, deletion.ToLogString());
                Assert.That(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedFolderDelete(avatar, deletion).Success, Is.True);
                Assert.That(AssetDatabase.IsValidFolder(generated), Is.False);
                Directory.CreateDirectory(Path.GetFullPath(generated));
                File.WriteAllText(Path.GetFullPath(generated + "/unrelated.txt"), "not a generated payload");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                Assert.That(ASMLiteGeneratedAssetMirrorService.ValidateVendorizedFolderForDelete(avatar, generated).Success, Is.False);
            }
            finally
            {
                if (other != null) Object.DestroyImmediate(other);
                if (ownerFolder != null) AssetDatabase.DeleteAsset(ownerFolder);
                if (avatarGo != null) Object.DestroyImmediate(avatarGo);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(scenePath);
            }
        }

        [Test]
        public void UnsavedOwner_FirstSave_ReusesGraphInSession_CloneCannotClaimIt()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Scene baselineScene = SceneManager.GetActiveScene();
            string baselinePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLiteOwnershipOuter.unity");
            AsmLiteTestContext ctx = null;
            Scene ownershipScene = default;
            string scenePath = AssetDatabase.GenerateUniqueAssetPath("Assets/ASMLiteOwnershipTransition.unity");
            string ownerFolder = null;
            GameObject clone = null;
            try
            {
                // Unity cannot open an additive scene beside an unsaved untitled scene.
                // Capture the saved outer scene, then test first-save in the unsaved inner scene.
                Assert.That(EditorSceneManager.SaveScene(baselineScene, baselinePath), Is.True);
                ctx = ASMLiteTestFixtures.CreateTestAvatar();
                ownershipScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                // Unity returns false when the requested scene is already active.
                SceneManager.SetActiveScene(ownershipScene);
                Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(ownershipScene));
                SceneManager.MoveGameObjectToScene(ctx.AvatarGo, ownershipScene);
                Assert.That(ownershipScene.path, Is.Empty, "Graph generation must precede the inner scene's first save.");
                var graph = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(ctx.AvDesc, workingAssets: true);
                if (!string.IsNullOrEmpty(graph.TargetPath)) ownerFolder = Path.GetDirectoryName(graph.TargetPath).Replace('\\', '/');
                Assert.That(graph.Success, Is.True, graph.ToLogString());
                Assert.That(ASMLiteGeneratedAssetMirrorService.FinalizeVendorizedMirror(graph).Success, Is.True);
                ctx.AvatarGo.name = "RenamedUnsaved";
                Assert.That(ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(ctx.AvDesc), Is.EqualTo(graph.TargetPath));
                Assert.That(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), scenePath), Is.True);
                Assert.That(ctx.AvatarGo.scene.path, Is.EqualTo(scenePath));
                Assert.That(ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(ctx.AvDesc), Is.EqualTo(graph.TargetPath));
                Assert.That(ASMLiteGeneratedAssetMirrorService.IsOwnedWorkingFolder(ctx.AvDesc, graph.TargetPath), Is.True);
                clone = Object.Instantiate(ctx.AvatarGo);
                var cloneAvatar = clone.GetComponent<VRCAvatarDescriptor>();
                Assert.That(ASMLiteGeneratedAssetMirrorService.ResolveAvatarWorkingDirectory(cloneAvatar), Is.Not.EqualTo(graph.TargetPath));
                Assert.That(ASMLiteGeneratedAssetMirrorService.IsOwnedWorkingFolder(cloneAvatar, graph.TargetPath), Is.False);
            }
            finally
            {
                try
                {
                    if (clone != null) Object.DestroyImmediate(clone);
                    if (ownerFolder != null) AssetDatabase.DeleteAsset(ownerFolder);
                }
                finally
                {
                    try
                    {
                        // Restore only this test's scene transition before the common leak check.
                        if (ctx?.AvatarGo != null) SceneManager.MoveGameObjectToScene(ctx.AvatarGo, baselineScene);
                        SceneManager.SetActiveScene(baselineScene);
                        Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(baselineScene));
                        if (ownershipScene.IsValid() && ownershipScene.isLoaded)
                            Assert.That(EditorSceneManager.CloseScene(ownershipScene, true), Is.True);
                    }
                    finally
                    {
                        try
                        {
                            if (ctx != null) ASMLiteTestFixtures.TearDownTestAvatar(ctx.AvatarGo);
                        }
                        finally
                        {
                            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                            AssetDatabase.DeleteAsset(scenePath);
                            AssetDatabase.DeleteAsset(baselinePath);
                        }
                    }
                }
            }
        }
    }
}
