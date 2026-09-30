using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.Avatars.Components;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    // Recovery belongs to the fixture service. This file contains its durable baseline,
    // not a second execution owner or a general-purpose transaction framework.
    internal sealed partial class ASMLiteSmokeSetupFixtureService
    {
        private FixtureBaseline _baseline;
        private bool _recoveryFailed;
        internal Action BeforeProofPublicationForTesting;
        internal string RecoveryPath => _baseline == null ? string.Empty : _baseline.directory;
        private static string RecoveryRoot => Path.Combine(ProjectRoot, ".artifacts/asm-lite-fixture-recovery");
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        [Serializable] private sealed class FixtureBaseline
        {
            public int version = 1;
            public string directory, active;
            public string[] selection;
            public SceneSetup[] scenes;
            public List<CoveredObject> objects = new List<CoveredObject>();
            public List<CoveredFile> files = new List<CoveredFile>();
            public List<CoveredTree> trees = new List<CoveredTree>();
            public List<CoveredProperty> properties = new List<CoveredProperty>();
            public List<SceneCopy> copies = new List<SceneCopy>();
            public List<AmbientObject> ambient = new List<AmbientObject>();
            public List<string> absentComponents = new List<string>();
            public List<string> ownedScenes = new List<string>();
            public List<IdentityPair> created = new List<IdentityPair>();
        }
        [Serializable] private sealed class CoveredObject
        {
            public string id, type, json, rawJson;
            public ReferenceValue[] references;
        }
        [Serializable] private sealed class ReferenceValue { public string path, target; }
        [Serializable] private sealed class CoveredProperty { public string id, path; public bool value; }
        [Serializable] private sealed class CoveredFile
        {
            public string path, copy, hash;
            public bool directory, exists, parentOnly;
            public string[] entries;
        }
        [Serializable] private sealed class CoveredTree
        {
            public string root, parent, avatar, scene, stage, stageGuid;
            public int sibling;
            public string[] members;
        }
        [Serializable] private sealed class SceneCopy { public string scene, guid, copy, hash, metaCopy, metaHash; }
        [Serializable] private sealed class AmbientObject { public string id, json, scene; }
        [Serializable] private sealed class Mapping { public string baselineHash; public List<IdentityPair> pairs = new List<IdentityPair>(); }
        [Serializable] private sealed class IdentityPair { public string original, current, source, type; }
        [Serializable] private sealed class Receipt { public int version = 1; public string baselineHash, mapHash; }
        [Serializable] private sealed class SetupValue { public SceneSetup[] scenes; }

        private readonly Dictionary<string, Object> _live = new Dictionary<string, Object>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _reverse = new Dictionary<string, string>(StringComparer.Ordinal);

        internal static bool CheckRecoveryAdmission(out string detail)
        {
            try
            {
                if (Directory.Exists(RecoveryRoot))
                    foreach (string directory in Directory.GetDirectories(RecoveryRoot))
                    {
                        FixtureBaseline baseline = ReadBaseline(directory);
                        ValidateRecoveryCopies(baseline);
                        string verified = Path.Combine(directory, "verified.json");
                        if (!File.Exists(verified))
                            throw new InvalidOperationException("Pending fixture restoration: " + directory);
                        Receipt receipt = JsonUtility.FromJson<Receipt>(File.ReadAllText(verified));
                        if (receipt == null || receipt.version != 1 || receipt.baselineHash != BaselineHash(directory)
                            || receipt.mapHash != FileHash(Path.Combine(directory, "correspondence.json")))
                            throw new InvalidDataException("Invalid fixture clearance: " + directory);
                        ValidateMapping(baseline, ReadMap(directory));
                        RequireNoStaging(baseline);
                    }
                detail = "Fixture recovery admission passed.";
                return true;
            }
            catch (Exception ex)
            {
                detail = "SETUP_FIXTURE_RECOVERY_BLOCKED: " + ex.Message;
                return false;
            }
        }

        private void CaptureBeforeMutation(string mutation, string avatarName)
        {
            ConsumeExplicitClearance();
            if (_recoveryFailed) throw new InvalidOperationException("Fixture recovery is blocked; repair and explicitly verify the retained baseline.");
            if (_baseline == null && !CheckRecoveryAdmission(out string admission))
                throw new InvalidOperationException(admission);
            bool first = _baseline == null;
            FixtureBaseline next = first ? new FixtureBaseline() : JsonUtility.FromJson<FixtureBaseline>(JsonUtility.ToJson(_baseline));
            if (first)
            {
                next.directory = Path.Combine(RecoveryRoot, Guid.NewGuid().ToString("N"));
                next.scenes = EditorSceneManager.GetSceneManagerSetup();
                if (next.scenes.Length == 0 || next.scenes.Any(scene => string.IsNullOrEmpty(scene.path)))
                    throw new InvalidOperationException("Fixture baseline requires saved, restart-resolvable scene setup.");
                next.selection = Selection.objects.Select(DurableId).ToArray();
                next.active = DurableId(Selection.activeObject);
            }
            VRCAvatarDescriptor avatar = FindSceneAvatarByName(avatarName, true);
            bool generated = mutation == ASMLiteSmokeSetupFixtureMutationIds.StaleGeneratedFolder
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.MissingGeneratedFolder
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.StaleVendorizedReferences
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.ControlledCorruptGeneratedAsset;
            bool component = mutation == ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.ExistingComponentBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.StaleVendorizedReferences
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent;
            if (generated)
            {
                if (string.IsNullOrWhiteSpace(avatarName) || avatarName.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0
                    || avatarName == "." || avatarName == "..")
                    throw new InvalidOperationException("Invalid generated-fixture avatar path segment.");
                CaptureFile(next, GeneratedRoot + "/" + avatarName + "/GeneratedAssets");
                CaptureFile(next, GeneratedRoot + "/" + avatarName + "/GeneratedAssets.meta");
                CaptureParent(next, GeneratedRoot + "/" + avatarName);
                CaptureParent(next, GeneratedRoot);
            }
            if (component && avatar != null)
            {
                CaptureScene(next, avatar.gameObject.scene);
                ASMLiteComponent original = avatar.GetComponentInChildren<ASMLiteComponent>(true);
                string avatarId = DurableId(avatar);
                if (original == null)
                {
                    if (!next.absentComponents.Contains(avatarId) && !next.trees.Any(tree => tree.avatar == avatarId))
                        next.absentComponents.Add(avatarId);
                }
                else if (!next.absentComponents.Contains(avatarId) && !next.trees.Any(tree => tree.avatar == avatarId))
                {
                    GameObject root = original.gameObject;
                    if (root == avatar.gameObject || PrefabUtility.IsPartOfPrefabInstance(root))
                        throw new InvalidOperationException("Fixture subtree recovery requires a non-prefab child hierarchy.");
                    string[] members = HierarchyObjects(root).Select(DurableId).ToArray();
                    foreach (Object value in HierarchyObjects(root)) CaptureObject(next, value);
                    next.trees.Add(new CoveredTree
                    {
                        root = DurableId(root), parent = DurableId(root.transform.parent), avatar = avatarId,
                        scene = root.scene.path, sibling = root.transform.GetSiblingIndex(), members = members,
                        stage = "Assets/ASMLiteFixtureStage-" + Guid.NewGuid().ToString("N") + ".unity",
                        stageGuid = Guid.NewGuid().ToString("N")
                    });
                    // Capture only inbound properties that point into the covered subtree.
                    foreach (Object owner in LoadedSceneObjects())
                        if (!members.Contains(DurableId(owner)))
                        {
                            ReferenceValue[] refs = ReferenceValues(owner);
                            ReferenceValue[] inbound = refs.Where(reference => members.Contains(reference.target)).ToArray();
                            if (inbound.Length > 0) CaptureReferencesOnly(next, owner, inbound);
                        }
                }
            }
            if (avatar != null && (mutation == ASMLiteSmokeSetupFixtureMutationIds.SelectedInactiveAvatar
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.UnselectedInactiveAvatar))
            {
                CaptureScene(next, avatar.gameObject.scene);
                string id = DurableId(avatar.gameObject);
                if (!next.properties.Any(property => property.id == id && property.path == "m_IsActive"))
                    next.properties.Add(new CoveredProperty { id = id, path = "m_IsActive", value = avatar.gameObject.activeSelf });
            }
            if (avatar != null && (mutation == ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.StaleVendorizedReferences))
            {
                CaptureScene(next, avatar.gameObject.scene);
                CaptureObject(next, avatar);
            }
            if (avatar != null && (mutation == ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline))
            {
                CaptureAsset(next, avatar.expressionParameters);
                if (mutation == ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline)
                {
                    CaptureAsset(next, avatar.expressionsMenu);
                    foreach (var layer in avatar.baseAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>())
                        if (layer.type == VRCAvatarDescriptor.AnimLayerType.FX) CaptureAsset(next, layer.animatorController);
                    // The existing cleanup API saves assets globally. Refuse rather than save operator edits.
                    foreach (Object asset in Resources.FindObjectsOfTypeAll<Object>())
                        if (asset != null && EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset)
                            && AssetDatabase.GetAssetPath(asset).StartsWith("Assets/", StringComparison.Ordinal)
                            && !next.files.Any(file => file.path == AssetDatabase.GetAssetPath(asset)))
                            throw new InvalidOperationException("Cleanup would save an unrelated dirty asset: " + AssetDatabase.GetAssetPath(asset));
                }
            }
            if (mutation == ASMLiteSmokeSetupFixtureMutationIds.TempSceneSetupRestore)
            {
                RequireSafeSceneReplacement();
                next.ownedScenes.Add("exclusively-fixture-owned-unsaved-" + Guid.NewGuid().ToString("N"));
            }
            // Mutations that create scene objects still require an admitted active scene.
            if (mutation == ASMLiteSmokeSetupFixtureMutationIds.DuplicateAvatarName
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.SelectedDuplicateAvatar
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.WrongAvatarSelection
                || mutation == ASMLiteSmokeSetupFixtureMutationIds.SameNameNonAvatar)
                CaptureScene(next, SceneManager.GetActiveScene());
            if (first)
                foreach (Object value in LoadedSceneObjects())
                    next.ambient.Add(new AmbientObject { id = DurableId(value), scene = SceneOf(value).path, json = CanonicalJson(value) });
            if (generated || mutation == ASMLiteSmokeSetupFixtureMutationIds.CleanAddBaseline)
                foreach (Object asset in Resources.FindObjectsOfTypeAll<Object>())
                    if (asset != null && EditorUtility.IsPersistent(asset) && EditorUtility.IsDirty(asset)
                        && AssetDatabase.GetAssetPath(asset).StartsWith("Assets/", StringComparison.Ordinal)
                        && !next.files.Any(file => file.path == AssetDatabase.GetAssetPath(asset)))
                        throw new InvalidOperationException("Fixture mutation would save an unrelated dirty asset.");
            // Admission and captures precede directory publication. Any interrupted preparation
            // after directory creation is deliberately visible as invalid, never implicitly cleared.
            Directory.CreateDirectory(next.directory);
            foreach (SceneCopy copy in next.copies)
            {
                CopyVerified(ToAbsoluteProjectPath(copy.scene), Path.Combine(next.directory, copy.copy), copy.hash);
                CopyVerified(ToAbsoluteProjectPath(copy.scene + ".meta"), Path.Combine(next.directory, copy.metaCopy), copy.metaHash);
            }
            foreach (CoveredFile file in next.files) PrepareFileCopy(next, file);
            int revision = Directory.GetFiles(next.directory, "baseline-*.json").Length;
            string manifest = Path.Combine(next.directory, "baseline-" + revision.ToString("D4") + ".json");
            DurableWrite(manifest, JsonUtility.ToJson(next, true));
            DurableWrite(Path.Combine(next.directory, "pending-" + revision.ToString("D4") + ".json"), FileHash(manifest));
            _baseline = next;
            foreach (Object value in LoadedSceneObjects()) Remember(value);
            foreach (Object value in Selection.objects) Remember(value);
            Remember(Selection.activeObject);
            ValidateRecoveryCopies(next);
            DurableWrite(Path.Combine(next.directory, "capture-correspondence-" + revision.ToString("D4") + ".json"),
                JsonUtility.ToJson(CreateMapping(true), true));
        }

        private void CaptureScene(FixtureBaseline baseline, Scene scene)
        {
            if (baseline.copies.Any(copy => copy.scene == scene.path)) return;
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.path) || scene.isDirty)
                throw new InvalidOperationException("Covered scene must be saved and clean before fixture mutation.");
            string suffix = baseline.copies.Count.ToString("D4");
            baseline.copies.Add(new SceneCopy { scene = scene.path, guid = AssetDatabase.AssetPathToGUID(scene.path),
                copy = "scene-" + suffix + ".unity", metaCopy = "scene-" + suffix + ".unity.meta",
                hash = FileHash(ToAbsoluteProjectPath(scene.path)), metaHash = FileHash(ToAbsoluteProjectPath(scene.path + ".meta")) });
        }

        private void CaptureObject(FixtureBaseline baseline, Object value)
        {
            string id = DurableId(value);
            CoveredObject existing = baseline.objects.FirstOrDefault(item => item.id == id);
            if (existing != null && !string.IsNullOrEmpty(existing.json)) return;
            if (existing != null) throw new InvalidOperationException("Later coverage cannot replace the first reference-only baseline.");
            if (PrefabUtility.IsPartOfPrefabInstance(value)) throw new InvalidOperationException("Prefab fixture state is not admitted by this recovery route.");
            baseline.objects.Add(new CoveredObject { id = id, type = value.GetType().AssemblyQualifiedName,
                json = CanonicalJson(value), rawJson = EditorJsonUtility.ToJson(value), references = ReferenceValues(value) });
            Remember(value);
        }

        private void CaptureReferencesOnly(FixtureBaseline baseline, Object owner, ReferenceValue[] inbound)
        {
            string id = DurableId(owner);
            CoveredObject existing = baseline.objects.FirstOrDefault(item => item.id == id);
            if (existing != null)
            {
                existing.references = existing.references.Concat(inbound.Where(value => !existing.references.Any(old => old.path == value.path))).ToArray();
                return;
            }
            baseline.objects.Add(new CoveredObject { id = id, type = owner.GetType().AssemblyQualifiedName,
                json = string.Empty, rawJson = string.Empty, references = inbound });
            Remember(owner);
        }

        private void CaptureAsset(FixtureBaseline baseline, Object asset)
        {
            if (asset == null) return;
            string path = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(path)) throw new InvalidOperationException("Covered assets must be saved.");
            foreach (Object value in AssetDatabase.LoadAllAssetsAtPath(path))
                if (EditorUtility.IsDirty(value)) throw new InvalidOperationException("Covered asset must be clean: " + path);
            CaptureFile(baseline, path);
            CaptureFile(baseline, path + ".meta");
            foreach (Object value in AssetDatabase.LoadAllAssetsAtPath(path)) CaptureObject(baseline, value);
        }

        private static void CaptureParent(FixtureBaseline baseline, string path)
        {
            // Existing parent contents are unrelated; only absent parent directories are owned.
            if (!Directory.Exists(ToAbsoluteProjectPath(path)) && !baseline.files.Any(file => file.path == path))
                baseline.files.Add(new CoveredFile { path = SafeAssetPath(path), directory = true, exists = false,
                    parentOnly = true, copy = "file-" + baseline.files.Count.ToString("D4"), entries = Array.Empty<string>() });
            CaptureFile(baseline, path + ".meta");
        }

        private static void CaptureFile(FixtureBaseline baseline, string path)
        {
            path = SafeAssetPath(path);
            if (baseline.files.Any(file => file.path == path || (file.directory && !file.parentOnly && path.StartsWith(file.path + "/", StringComparison.Ordinal)))) return;
            if (baseline.files.Any(file => file.path.StartsWith(path + "/", StringComparison.Ordinal)))
                throw new InvalidOperationException("Later coverage would replace an earlier file baseline.");
            string absolute = ToAbsoluteProjectPath(path);
            RejectReparse(absolute);
            bool directory = Directory.Exists(absolute) || (!File.Exists(absolute) && !Path.HasExtension(path));
            baseline.files.Add(new CoveredFile { path = path, directory = directory,
                exists = directory ? Directory.Exists(absolute) : File.Exists(absolute),
                copy = "file-" + baseline.files.Count.ToString("D4"),
                hash = directory ? DirectoryHash(absolute) : (File.Exists(absolute) ? FileHash(absolute) : string.Empty),
                entries = directory && Directory.Exists(absolute) ? DirectoryEntries(absolute) : Array.Empty<string>() });
        }

        private static void PrepareFileCopy(FixtureBaseline baseline, CoveredFile file)
        {
            if (!file.exists) return;
            string target = Path.Combine(baseline.directory, file.copy);
            if (file.directory)
            {
                if (Directory.Exists(target))
                {
                    if (DirectoryHash(target) != file.hash) throw new InvalidDataException("Recovery directory hash mismatch.");
                    return;
                }
                Directory.CreateDirectory(target);
                foreach (string entry in file.entries.Where(entry => entry.EndsWith("/", StringComparison.Ordinal)))
                    Directory.CreateDirectory(Path.Combine(target, entry.TrimEnd('/')));
                foreach (string entry in file.entries.Where(entry => !entry.EndsWith("/", StringComparison.Ordinal)))
                    CopyVerified(Path.Combine(ToAbsoluteProjectPath(file.path), entry), Path.Combine(target, entry), FileHash(Path.Combine(ToAbsoluteProjectPath(file.path), entry)));
                if (DirectoryHash(target) != file.hash) throw new InvalidDataException("Prepared recovery directory mismatch.");
            }
            else CopyVerified(ToAbsoluteProjectPath(file.path), target, file.hash);
        }

        private void ConsumeExplicitClearance()
        {
            if (_baseline == null || !File.Exists(Path.Combine(_baseline.directory, "verified.json"))) return;
            if (!CheckRecoveryAdmission(out string detail)) throw new InvalidOperationException(detail);
            _baseline = null;
            _recoveryFailed = false;
            _cleanupLedger.Clear();
            _live.Clear(); _reverse.Clear(); _ownedObjects.Clear();
            HasCleanResetProof = true;
        }

        private bool RestoreAndVerify(out string detail)
        {
            try
            {
                ConsumeExplicitClearance();
                if (_baseline == null)
                {
                    if (!CheckRecoveryAdmission(out detail)) { HasCleanResetProof = false; return false; }
                    HasCleanResetProof = true;
                    detail = "No pending fixture baseline; no restoration was performed.";
                    return true;
                }
                if (_recoveryFailed) throw new InvalidOperationException("Fixture reset is latched after failure. Manual repair and explicit verification are required.");
                ValidateRecoveryCopies(_baseline);
                var initiallyDirty = new HashSet<string>(_baseline.copies
                    .Where(copy => SceneManager.GetSceneByPath(copy.scene).isDirty).Select(copy => copy.scene));
                foreach (string avatarId in _baseline.absentComponents)
                {
                    var avatar = ResolveCaptured(avatarId) as VRCAvatarDescriptor;
                    if (avatar == null) throw new InvalidOperationException("Covered avatar correspondence is missing.");
                    foreach (ASMLiteComponent component in avatar.GetComponentsInChildren<ASMLiteComponent>(true))
                    {
                        if (component.gameObject == avatar.gameObject) throw new InvalidOperationException("Refusing to delete the avatar root.");
                        Object.DestroyImmediate(component.gameObject);
                    }
                }
                foreach (CoveredTree tree in _baseline.trees)
                    if (ResolveCaptured(tree.root) == null || tree.members.Any(id => ResolveCaptured(id) == null)) RestoreTree(tree);
                foreach (CoveredObject item in _baseline.objects)
                {
                    if (string.IsNullOrEmpty(item.rawJson)) continue;
                    Object target = ResolveCaptured(item.id);
                    if (target == null || target.GetType().AssemblyQualifiedName != item.type) throw new InvalidOperationException("Covered object correspondence is missing: " + item.id);
                    EditorJsonUtility.FromJsonOverwrite(item.rawJson, target);
                }
                foreach (CoveredProperty item in _baseline.properties)
                {
                    Object target = ResolveCaptured(item.id);
                    if (target == null) throw new InvalidOperationException("Covered property owner is missing.");
                    var serialized = new SerializedObject(target);
                    var property = serialized.FindProperty(item.path);
                    if (property == null) throw new InvalidOperationException("Covered property is missing.");
                    property.boolValue = item.value;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                foreach (CoveredTree tree in _baseline.trees)
                {
                    var root = (GameObject)ResolveCaptured(tree.root);
                    var parent = ResolveCaptured(tree.parent) as Transform;
                    if (parent == null) throw new InvalidOperationException("Covered subtree parent correspondence is missing.");
                    root.transform.SetParent(parent, false);
                    root.transform.SetSiblingIndex(tree.sibling);
                }
                RestoreFiles();
                RemapReferences();
                while (_cleanupLedger.Count > 0)
                {
                    CleanupEntry entry = _cleanupLedger.Pop();
                    entry.Cleanup?.Invoke();
                }
                ProtectUnrelatedDirtyState(initiallyDirty);
                PublishCorrespondenceAndSave();
                RestoreSceneSetupAndSelection();
                if (!CompareBaseline(out detail)) throw new InvalidOperationException(detail);
                BeforeProofPublicationForTesting?.Invoke();
                PublishClearance(_baseline);
                HasCleanResetProof = true;
                _baseline = null;
                _live.Clear(); _reverse.Clear();
                detail = "Setup fixture reset completed with captured baseline proof.";
                return true;
            }
            catch (Exception ex)
            {
                _recoveryFailed = true;
                HasCleanResetProof = false;
                detail = "SETUP_FIXTURE_RECOVERY_BLOCKED: " + ex.Message + " Recovery: " + RecoveryPath;
                return false;
            }
        }

        private void RestoreTree(CoveredTree tree)
        {
            // Only normal cleanup in the original process reaches this. Startup and manual
            // comparison never reconstruct objects or infer identity from reused original IDs.
            SceneCopy copy = _baseline.copies.Single(value => value.scene == tree.scene);
            string stage = ToAbsoluteProjectPath(tree.stage);
            if (File.Exists(stage) || File.Exists(stage + ".meta")) throw new InvalidOperationException("Recorded staging path is already present.");
            CopyVerified(Path.Combine(_baseline.directory, copy.copy), stage, copy.hash);
            string meta = File.ReadAllText(Path.Combine(_baseline.directory, copy.metaCopy));
            string line = "guid: " + copy.guid;
            if (meta.Split('\n').Count(value => value.Trim() == line) != 1) throw new InvalidDataException("Scene snapshot metadata GUID mismatch.");
            DurableWrite(stage + ".meta", meta.Replace(line, "guid: " + tree.stageGuid));
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Scene staging = EditorSceneManager.OpenScene(tree.stage, OpenSceneMode.Additive);
            var replacements = new Dictionary<string, Object>();
            foreach (string original in tree.members)
            {
                Object value = ResolveId(SnapshotIdentity(original, copy.guid, tree.stageGuid));
                CoveredObject captured = _baseline.objects.Single(item => item.id == original);
                if (value == null || value.GetType().AssemblyQualifiedName != captured.type) throw new InvalidDataException("Native snapshot correspondence is unverifiable.");
                replacements.Add(original, value);
            }
            var root = replacements[tree.root] as GameObject;
            if (root == null || !new HashSet<Object>(HierarchyObjects(root)).SetEquals(replacements.Values))
                throw new InvalidDataException("Native snapshot subtree object set does not match capture.");
            Object old = ResolveCaptured(tree.root);
            if (old != null) Object.DestroyImmediate(old);
            root.transform.SetParent(null, false);
            SceneManager.MoveGameObjectToScene(root, SceneManager.GetSceneByPath(tree.scene));
            foreach (var pair in replacements) _live[pair.Key] = pair.Value;
            // Staging is exclusively fixture-owned. Its remaining scene objects are never
            // part of the target; close it without importing them into the covered footprint.
            EditorSceneManager.CloseScene(staging, true);
        }

        private void RemapReferences()
        {
            foreach (CoveredObject item in _baseline.objects)
            {
                Object owner = ResolveCaptured(item.id);
                if (owner == null) throw new InvalidOperationException("Reference owner is missing: " + item.id);
                var serialized = new SerializedObject(owner);
                foreach (ReferenceValue reference in item.references)
                {
                    var property = serialized.FindProperty(reference.path);
                    if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                        throw new InvalidOperationException("Captured reference property is missing: " + reference.path);
                    Object target = ResolveCaptured(reference.target);
                    if (!string.IsNullOrEmpty(reference.target) && target == null) throw new InvalidOperationException("Captured reference target is missing.");
                    if (property.objectReferenceValue != target) property.objectReferenceValue = target;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private void RestoreFiles()
        {
            foreach (CoveredFile file in _baseline.files.OrderByDescending(item => item.path.Length))
            {
                string target = ToAbsoluteProjectPath(file.path);
                if (file.directory)
                {
                    if (Directory.Exists(target))
                    {
                        RejectReparse(target);
                        // Absent parent directories are deleted only if empty: unrelated new
                        // contents are protected, and their mismatch keeps recovery blocked.
                        bool parentOnly = file.parentOnly;
                        if (!parentOnly || !Directory.EnumerateFileSystemEntries(target).Any()) Directory.Delete(target, true);
                    }
                    if (file.exists) CopyDirectory(Path.Combine(_baseline.directory, file.copy), target);
                }
                else
                {
                    bool retainedParent = !file.exists && file.path.EndsWith(".meta", StringComparison.Ordinal)
                        && _baseline.files.Any(parent => parent.parentOnly && file.path == parent.path + ".meta"
                            && Directory.Exists(ToAbsoluteProjectPath(parent.path))
                            && Directory.EnumerateFileSystemEntries(ToAbsoluteProjectPath(parent.path)).Any());
                    if (retainedParent) continue;
                    if (File.Exists(target)) File.Delete(target);
                    if (file.exists)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        File.Copy(Path.Combine(_baseline.directory, file.copy), target);
                    }
                }
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            foreach (CoveredFile file in _baseline.files.Where(item => item.exists && !item.directory && !item.path.EndsWith(".meta", StringComparison.Ordinal)))
                AssetDatabase.ImportAsset(file.path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            // Asset imports may rebuild objects. Rebind only stable asset identities, not
            // deleted scene IDs that Unity may have reassigned to unrelated objects.
            foreach (CoveredObject item in _baseline.objects)
                if (GlobalObjectId.TryParse(item.id, out var id) && id.identifierType == 3) _live[item.id] = ResolveId(item.id);
        }

        private Mapping CreateMapping(bool capture)
        {
            _reverse.Clear();
            var map = new Mapping { baselineHash = BaselineHash(_baseline.directory) };
            foreach (string original in CapturedIdentities(_baseline))
            {
                Object value = ResolveCaptured(original);
                string type = value != null ? value.GetType().AssemblyQualifiedName
                    : _baseline.objects.FirstOrDefault(item => item.id == original)?.type;
                if (type == null || (!capture && value == null))
                    throw new InvalidOperationException("Captured identity cannot be mapped: " + original);
                string current = capture ? original : DurableId(value);
                CoveredTree tree = _baseline.trees.FirstOrDefault(item => item.members.Contains(original));
                string source = tree == null ? string.Empty : SnapshotIdentity(original,
                    _baseline.copies.Single(copy => copy.scene == tree.scene).guid, tree.stageGuid);
                map.pairs.Add(new IdentityPair { original = original, current = current, source = source, type = type });
                _reverse.Add(current, original);
            }
            ValidateMapping(_baseline, map);
            return map;
        }

        private void PublishCorrespondenceAndSave()
        {
            foreach (SceneCopy copy in _baseline.copies)
            {
                Scene scene = SceneManager.GetSceneByPath(copy.scene);
                if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Covered scene was closed; operator repair is required.");
                string path = Path.Combine(_baseline.directory, "prepared-" + Guid.NewGuid().ToString("N") + ".unity").Replace('\\', '/');
                if (!EditorSceneManager.SaveScene(scene, path, true)) throw new IOException("Native external scene preparation failed.");
            }
            Mapping map = CreateMapping(false);
            DurableWrite(Path.Combine(_baseline.directory, "correspondence.json"), JsonUtility.ToJson(map, true));
            foreach (SceneCopy copy in _baseline.copies)
            {
                Scene scene = SceneManager.GetSceneByPath(copy.scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Covered scene save failed.");
            }
            foreach (IdentityPair pair in map.pairs)
                if (DurableId(ResolveCaptured(pair.original)) != pair.current) throw new InvalidOperationException("Prepared correspondence changed during target save.");
            foreach (CoveredTree tree in _baseline.trees)
            {
                string path = ToAbsoluteProjectPath(tree.stage);
                if (!File.Exists(path) && !File.Exists(path + ".meta")) continue;
                if (AssetDatabase.AssetPathToGUID(tree.stage) != tree.stageGuid) throw new InvalidOperationException("Staging cleanup ownership mismatch.");
                if (!AssetDatabase.DeleteAsset(tree.stage)) throw new IOException("Owned staging asset cleanup failed.");
            }
        }

        private void RestoreSceneSetupAndSelection()
        {
            if (SceneSignature(EditorSceneManager.GetSceneManagerSetup()) != SceneSignature(_baseline.scenes))
            {
                RequireSafeSceneReplacement();
                EditorSceneManager.RestoreSceneManagerSetup(_baseline.scenes);
                LoadCorrespondence(_baseline);
            }
            // Setting activeObject replaces the selection; set it first, then the full set.
            Selection.activeObject = ResolveCaptured(_baseline.active);
            Selection.objects = _baseline.selection.Select(ResolveCaptured).ToArray();

        }

        private void ProtectUnrelatedDirtyState(HashSet<string> initiallyDirty)
        {
            foreach (SceneCopy copy in _baseline.copies)
            {
                Scene scene = SceneManager.GetSceneByPath(copy.scene);
                if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Covered scene is not loaded.");
                if (!initiallyDirty.Contains(copy.scene)) continue;
                var covered = new HashSet<string>(_baseline.objects.Where(item => !string.IsNullOrEmpty(item.json)).Select(item => item.id));
                foreach (Object value in LoadedSceneObjects().Where(value => SceneOf(value).path == copy.scene))
                {
                    string id = CanonicalId(value);
                    if (covered.Contains(id)) continue;
                    if (_ownedObjects.Contains(value)) continue;
                    AmbientObject before = _baseline.ambient.FirstOrDefault(item => item.id == id);
                    if (before == null || before.json != CanonicalJson(value))
                        throw new InvalidOperationException("Cleanup would save unrelated dirty scene changes. Save or repair them first: " + copy.scene);
                }
                foreach (AmbientObject before in _baseline.ambient.Where(item => item.scene == copy.scene && !covered.Contains(item.id)))
                    if (ResolveCaptured(before.id) == null)
                        throw new InvalidOperationException("Cleanup would save an unrelated scene deletion: " + copy.scene);
            }
        }

        private readonly HashSet<Object> _ownedObjects = new HashSet<Object>();
        private void RecordCreatedObject(GameObject value)
        {
            // Preparation assigns stable IDs without saving operator scene changes.
            string prepared = Path.Combine(_baseline.directory, "created-prepared-" + Guid.NewGuid().ToString("N") + ".unity").Replace('\\', '/');
            if (!EditorSceneManager.SaveScene(value.scene, prepared, true)) throw new IOException("Created-object preparation failed.");
            foreach (Object item in HierarchyObjects(value))
            {
                _ownedObjects.Add(item);
                string id = DurableId(item);
                _baseline.created.Add(new IdentityPair { original = id, current = id, type = item.GetType().AssemblyQualifiedName });
            }
            int revision = Directory.GetFiles(_baseline.directory, "baseline-*.json").Length;
            string manifest = Path.Combine(_baseline.directory, "baseline-" + revision.ToString("D4") + ".json");
            DurableWrite(manifest, JsonUtility.ToJson(_baseline, true));
            DurableWrite(Path.Combine(_baseline.directory, "pending-" + revision.ToString("D4") + ".json"), FileHash(manifest));
            DurableWrite(Path.Combine(_baseline.directory, "capture-correspondence-" + revision.ToString("D4") + ".json"),
                JsonUtility.ToJson(CreateMapping(true), true));
        }

        internal static void RequireSafeSceneReplacement()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && (scene.isDirty || string.IsNullOrEmpty(scene.path)))
                    throw new InvalidOperationException("Refusing to close or discard an unrelated unsaved/dirty scene: " + scene.name);
            }
        }

        private bool CompareBaseline(out string detail)
        {
            if (_baseline == null) return CheckRecoveryAdmission(out detail);
            try
            {
                ValidateRecoveryCopies(_baseline);
                foreach (SceneCopy copy in _baseline.copies)
                    if (!SceneManager.GetSceneByPath(copy.scene).isLoaded || SceneManager.GetSceneByPath(copy.scene).isDirty)
                        throw new InvalidOperationException("Covered scene must be loaded and saved for baseline proof.");
                if (SceneSignature(EditorSceneManager.GetSceneManagerSetup()) != SceneSignature(_baseline.scenes)) throw new InvalidOperationException("Scene order/load/active state differs from capture.");
                if (!Selection.objects.Select(CanonicalId).SequenceEqual(_baseline.selection) || CanonicalId(Selection.activeObject) != _baseline.active)
                    throw new InvalidOperationException("Full selection or active object differs from capture.");
                foreach (CoveredFile file in _baseline.files)
                {
                    string path = ToAbsoluteProjectPath(file.path);
                    bool exists = file.directory ? Directory.Exists(path) : File.Exists(path);
                    if (exists != file.exists || (exists && (file.directory ? DirectoryHash(path) : FileHash(path)) != file.hash))
                        throw new InvalidOperationException("Covered bytes/metadata differ: " + file.path);
                }
                foreach (CoveredObject item in _baseline.objects)
                {
                    Object value = ResolveCaptured(item.id);
                    if (value == null || value.GetType().AssemblyQualifiedName != item.type) throw new InvalidOperationException("Covered identity/type mismatch: " + item.id);
                    if (EditorUtility.IsPersistent(value) && EditorUtility.IsDirty(value))
                        throw new InvalidOperationException("Covered asset has unsaved state: " + item.id);
                    if (!string.IsNullOrEmpty(item.json) && CanonicalJson(value) != item.json) throw new InvalidOperationException("Covered serialized state differs: " + item.id);
                    var serialized = new SerializedObject(value);
                    foreach (ReferenceValue reference in item.references)
                    {
                        var property = serialized.FindProperty(reference.path);
                        if (property == null || CanonicalId(property.objectReferenceValue) != reference.target) throw new InvalidOperationException("Covered reference differs: " + reference.path);
                    }
                }
                foreach (CoveredProperty item in _baseline.properties)
                {
                    Object value = ResolveCaptured(item.id);
                    if (value == null || new SerializedObject(value).FindProperty(item.path)?.boolValue != item.value)
                        throw new InvalidOperationException("Covered property differs: " + item.path);
                }
                foreach (CoveredTree tree in _baseline.trees)
                {
                    var root = ResolveCaptured(tree.root) as GameObject;
                    if (root == null || root.scene.path != tree.scene || CanonicalId(root.transform.parent) != tree.parent
                        || root.transform.GetSiblingIndex() != tree.sibling || !new HashSet<string>(HierarchyObjects(root).Select(CanonicalId)).SetEquals(tree.members))
                        throw new InvalidOperationException("Covered hierarchy/correspondence differs from capture.");
                }
                foreach (string id in _baseline.absentComponents)
                    if (!(ResolveCaptured(id) is VRCAvatarDescriptor avatar) || avatar.GetComponentInChildren<ASMLiteComponent>(true) != null)
                        throw new InvalidOperationException("Covered component absence differs from capture.");
                foreach (IdentityPair created in _baseline.created)
                    if (ResolveId(created.current) != null)
                        throw new InvalidOperationException("Fixture-created object absence cannot be proved: " + created.current);
                RequireNoStaging(_baseline);
                detail = "Captured fixture baseline comparison passed.";
                return true;
            }
            catch (Exception ex) { detail = ex.Message; return false; }
        }

        [MenuItem("Tools/ASM-Lite/Smoke/Verify Repaired Fixture Baseline")]
        private static void VerifyRepairedFixtureBaselineMenu()
        {
            if (!VerifyRepairedFixtureBaseline(out string detail)) Debug.LogError(detail);
            else Debug.Log(detail);
        }

        internal static bool VerifyRepairedFixtureBaseline(out string detail)
        {
            try
            {
                if (!Directory.Exists(RecoveryRoot)) { detail = "No fixture recovery baseline exists."; return false; }
                string[] pending = Directory.GetDirectories(RecoveryRoot).Where(directory => !File.Exists(Path.Combine(directory, "verified.json"))).ToArray();
                if (pending.Length == 0) return CheckRecoveryAdmission(out detail);
                foreach (string directory in pending)
                {
                    var service = new ASMLiteSmokeSetupFixtureService { _baseline = ReadBaseline(directory) };
                    ValidateRecoveryCopies(service._baseline);
                    service.LoadCorrespondence(service._baseline);
                    if (!service.CompareBaseline(out detail)) return false;
                    PublishClearance(service._baseline);
                }
                return CheckRecoveryAdmission(out detail);
            }
            catch (Exception ex) { detail = "SETUP_FIXTURE_RECOVERY_BLOCKED: " + ex.Message; return false; }
        }

        private void LoadCorrespondence(FixtureBaseline baseline)
        {
            Mapping map = ReadMap(baseline.directory);
            ValidateMapping(baseline, map);
            _live.Clear(); _reverse.Clear();
            foreach (IdentityPair pair in map.pairs)
            {
                Object value = ResolveId(pair.current);
                if (value == null || value.GetType().AssemblyQualifiedName != pair.type) throw new InvalidDataException("Retained correspondence cannot resolve the captured type.");
                _live.Add(pair.original, value); _reverse.Add(pair.current, pair.original);
            }
        }

        private static IEnumerable<string> CapturedIdentities(FixtureBaseline baseline)
        {
            return baseline.objects.Select(item => item.id)
                .Concat(baseline.objects.SelectMany(item => item.references.Select(reference => reference.target)))
                .Concat(baseline.properties.Select(item => item.id)).Concat(baseline.absentComponents)
                .Concat(baseline.trees.Select(item => item.parent)).Concat(baseline.selection).Append(baseline.active)
                .Where(value => !string.IsNullOrEmpty(value)).Distinct(StringComparer.Ordinal);
        }

        private static void ValidateMapping(FixtureBaseline baseline, Mapping map)
        {
            string[] identities = CapturedIdentities(baseline).ToArray();
            if (map == null || map.baselineHash != BaselineHash(baseline.directory) || map.pairs == null || map.pairs.Count != identities.Length
                || !new HashSet<string>(map.pairs.Select(pair => pair.original)).SetEquals(identities)
                || map.pairs.Select(pair => pair.current).Distinct().Count() != identities.Length)
                throw new InvalidDataException("Incomplete or duplicate fixture correspondence.");
            foreach (IdentityPair pair in map.pairs)
            {
                if (!GlobalObjectId.TryParse(pair.current, out var current) || current.targetObjectId == 0 || string.IsNullOrEmpty(pair.type))
                    throw new InvalidDataException("Invalid mapped fixture identity.");
                CoveredTree tree = baseline.trees.FirstOrDefault(item => item.members.Contains(pair.original));
                if (tree != null && pair.source != SnapshotIdentity(pair.original, baseline.copies.Single(copy => copy.scene == tree.scene).guid, tree.stageGuid))
                    throw new InvalidDataException("Mapped fixture identity lacks snapshot provenance.");
                if (tree == null && pair.current != pair.original) throw new InvalidDataException("Uncovered identity must not be replaced.");
            }
        }

        private static void PublishClearance(FixtureBaseline baseline)
        {
            ValidateRecoveryCopies(baseline);
            ValidateMapping(baseline, ReadMap(baseline.directory));
            RequireNoStaging(baseline);
            string mapPath = Path.Combine(baseline.directory, "correspondence.json");
            if (!File.Exists(mapPath)) DurableWrite(mapPath, JsonUtility.ToJson(ReadMap(baseline.directory), true));
            DurableWrite(Path.Combine(baseline.directory, "verified.json"), JsonUtility.ToJson(new Receipt
            { baselineHash = BaselineHash(baseline.directory), mapHash = FileHash(Path.Combine(baseline.directory, "correspondence.json")) }));
        }

        private static FixtureBaseline ReadBaseline(string directory)
        {
            if (Directory.EnumerateFiles(directory, "*.preparing", SearchOption.AllDirectories).Any()) throw new InvalidDataException("Incomplete fixture recovery preparation: " + directory);
            string[] manifests = Directory.GetFiles(directory, "baseline-*.json").OrderBy(path => path, StringComparer.Ordinal).ToArray();
            if (manifests.Length == 0) throw new InvalidDataException("Missing fixture baseline: " + directory);
            for (int i = 0; i < manifests.Length; i++)
            {
                if (Path.GetFileName(manifests[i]) != "baseline-" + i.ToString("D4") + ".json"
                    || File.ReadAllText(Path.Combine(directory, "pending-" + i.ToString("D4") + ".json")) != FileHash(manifests[i]))
                    throw new InvalidDataException("Incomplete fixture pending publication.");
            }
            FixtureBaseline baseline = JsonUtility.FromJson<FixtureBaseline>(File.ReadAllText(manifests.Last()));
            if (baseline == null || baseline.version != 1 || baseline.scenes == null || baseline.selection == null
                || baseline.objects == null || baseline.files == null || baseline.copies == null || baseline.trees == null
                || baseline.properties == null || baseline.absentComponents == null || baseline.ambient == null || baseline.created == null || baseline.ownedScenes == null
                || !string.Equals(Path.GetFullPath(baseline.directory), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported or incomplete fixture baseline.");
            foreach (CoveredFile file in baseline.files) { SafeAssetPath(file.path); SafeRecoveryMember(directory, file.copy); }
            foreach (SceneCopy copy in baseline.copies) { SafeAssetPath(copy.scene); SafeRecoveryMember(directory, copy.copy); SafeRecoveryMember(directory, copy.metaCopy); }
            foreach (CoveredTree tree in baseline.trees)
                if (!Regex.IsMatch(tree.stage ?? string.Empty, @"\AAssets/ASMLiteFixtureStage-[0-9a-f]{32}\.unity\z") || !Regex.IsMatch(tree.stageGuid ?? string.Empty, @"\A[0-9a-f]{32}\z"))
                    throw new InvalidDataException("Invalid staging ownership.");
            if (baseline.objects.Select(item => item.id).Distinct().Count() != baseline.objects.Count
                || baseline.files.Select(item => item.path).Distinct().Count() != baseline.files.Count)
                throw new InvalidDataException("Duplicate baseline coverage.");
            return baseline;
        }

        private static void ValidateRecoveryCopies(FixtureBaseline baseline)
        {
            ReadBaseline(baseline.directory);
            foreach (SceneCopy copy in baseline.copies)
                if (FileHash(Path.Combine(baseline.directory, copy.copy)) != copy.hash || FileHash(Path.Combine(baseline.directory, copy.metaCopy)) != copy.metaHash)
                    throw new InvalidDataException("Immutable scene recovery copy is corrupt.");
            foreach (CoveredFile file in baseline.files.Where(item => item.exists))
                if ((file.directory ? DirectoryHash(Path.Combine(baseline.directory, file.copy)) : FileHash(Path.Combine(baseline.directory, file.copy))) != file.hash)
                    throw new InvalidDataException("Immutable file recovery copy is corrupt.");
        }

        private static void RequireNoStaging(FixtureBaseline baseline)
        {
            foreach (CoveredTree tree in baseline.trees)
                if (File.Exists(ToAbsoluteProjectPath(tree.stage)) || File.Exists(ToAbsoluteProjectPath(tree.stage + ".meta")) || SceneManager.GetSceneByPath(tree.stage).IsValid())
                    throw new InvalidOperationException("Recorded fixture staging has not been explicitly cleaned up.");
        }

        private static string BaselineHash(string directory)
        {
            return TextHash(string.Join("\n", Directory.GetFiles(directory, "baseline-*.json").OrderBy(path => path, StringComparer.Ordinal).Select(FileHash)));
        }
        private static Mapping ReadMap(string directory)
        {
            string path = Path.Combine(directory, "correspondence.json");
            if (!File.Exists(path)) path = Directory.GetFiles(directory, "capture-correspondence-*.json")
                .OrderBy(member => member, StringComparer.Ordinal).Last();
            return JsonUtility.FromJson<Mapping>(File.ReadAllText(path));
        }
        private static string SceneSignature(SceneSetup[] setup) => JsonUtility.ToJson(new SetupValue { scenes = setup });
        private void Remember(Object value) { if (value != null) _live[DurableId(value)] = value; }
        private Object ResolveCaptured(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_live.TryGetValue(id, out Object value)) return value;
            Object resolved = ResolveId(id);
            if (resolved != null && EditorUtility.IsPersistent(resolved)) return resolved;
            return null; // Never resolve a deleted scene ID into a possibly unrelated replacement.
        }
        private string CanonicalId(Object value)
        {
            if (value == null) return string.Empty;
            string id = DurableId(value);
            return _reverse.TryGetValue(id, out string original) ? original : id;
        }
        private static Object ResolveId(string id) => GlobalObjectId.TryParse(id, out var parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) : null;
        private static string DurableId(Object value)
        {
            if (value == null) return string.Empty;
            GlobalObjectId id = GlobalObjectId.GetGlobalObjectIdSlow(value);
            if (id.targetObjectId == 0 || GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id) != value)
                throw new InvalidOperationException("Object is not restart-resolvable; save a supported baseline before mutation: " + value.name + " type=" + value.GetType().FullName + " path=" + AssetDatabase.GetAssetPath(value) + " id=" + id);
            return id.ToString();
        }
        private static Scene SceneOf(Object value) => value is GameObject go ? go.scene : (value is Component component ? component.gameObject.scene : default);
        private static IEnumerable<Object> LoadedSceneObjects()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    foreach (Object value in HierarchyObjects(root)) yield return value;
            }
        }
        private static IEnumerable<Object> HierarchyObjects(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                yield return transform.gameObject;
                foreach (Component component in transform.GetComponents<Component>())
                {
                    if (component == null) throw new InvalidOperationException("Missing-script hierarchies are not supported fixture baselines.");
                    yield return component;
                }
            }
        }
        private static string SnapshotIdentity(string original, string guid, string stageGuid)
        {
            string[] parts = original.Split('-');
            if (parts.Length != 5 || parts[1] != "2" || parts[2] != guid || parts[4] != "0")
                throw new InvalidDataException("Snapshot correspondence supports only saved non-prefab scene objects.");
            parts[2] = stageGuid;
            return string.Join("-", parts);
        }
        private ReferenceValue[] ReferenceValues(Object value)
        {
            var result = new List<ReferenceValue>();
            var iterator = new SerializedObject(value).GetIterator();
            var visited = new HashSet<long>();
            bool enter = true;
            while (iterator.Next(enter))
            {
                enter = iterator.propertyType != SerializedPropertyType.ManagedReference || visited.Add(iterator.managedReferenceId);
                if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                {
                    try { result.Add(new ReferenceValue { path = iterator.propertyPath, target = CanonicalId(iterator.objectReferenceValue) }); }
                    catch (InvalidOperationException ex)
                    {
                        throw new InvalidOperationException(ex.Message + " owner=" + value.GetType().FullName + " property=" + iterator.propertyPath);
                    }
                }
            }
            return result.ToArray();
        }
        private string CanonicalJson(Object value)
        {
            string json = EditorJsonUtility.ToJson(value);
            return Regex.Replace(json, @"""instanceID""\s*:\s*(-?\d+)", match =>
            {
                int instance = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                Object reference = instance == 0 ? null : EditorUtility.InstanceIDToObject(instance);
                if (instance != 0 && reference == null) throw new InvalidOperationException("Serialized reference cannot be identified.");
                try { return "\"instanceID\":\"" + CanonicalId(reference) + "\""; }
                catch (InvalidOperationException ex)
                {
                    throw new InvalidOperationException(ex.Message + " owner=" + value.GetType().FullName);
                }
            });
        }
        private static string SafeAssetPath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) || path.Contains('\\')
                || path.Contains(':') || path.Split('/').Any(part => part == "." || part == ".." || string.IsNullOrEmpty(part)))
                throw new InvalidDataException("Recovery resource is not a confined Assets path.");
            RejectReparse(ToAbsoluteProjectPath(path));
            return path;
        }
        private static void SafeRecoveryMember(string directory, string name)
        {
            if (string.IsNullOrEmpty(name) || Path.GetFileName(name) != name || name == "." || name == "..") throw new InvalidDataException("Recovery member escapes its directory.");
            RejectReparse(Path.Combine(directory, name));
        }
        private static void RejectReparse(string path)
        {
            for (string current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Recovery does not follow reparse points: " + current);
        }
        private static string[] DirectoryEntries(string path)
        {
            RejectReparse(path);
            if (!Directory.Exists(path)) return Array.Empty<string>();
            var entries = new List<string>();
            foreach (string member in Directory.EnumerateFileSystemEntries(path))
            {
                RejectReparse(member);
                string name = Path.GetFileName(member);
                if (Directory.Exists(member))
                {
                    entries.Add(name + "/");
                    entries.AddRange(DirectoryEntries(member).Select(child => name + "/" + child));
                }
                else entries.Add(name);
            }
            return entries.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }
        private static string DirectoryHash(string path)
        {
            if (!Directory.Exists(path)) return string.Empty;
            return TextHash(string.Join("\n", DirectoryEntries(path).Select(entry => entry.EndsWith("/", StringComparison.Ordinal) ? entry : entry + "|" + FileHash(Path.Combine(path, entry)))));
        }
        private static string TextHash(string value)
        {
            using (var sha = SHA256.Create()) return Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
        }
        private static string FileHash(string path)
        {
            RejectReparse(path);
            using (var sha = SHA256.Create()) using (var input = File.OpenRead(path)) return Hex(sha.ComputeHash(input));
        }
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", string.Empty).ToLowerInvariant();
        private static void CopyVerified(string source, string target, string expected)
        {
            if (!File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                using (var input = File.OpenRead(source)) using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { input.CopyTo(output); output.Flush(true); }
            }
            if (FileHash(target) != expected) throw new InvalidDataException("Recovery copy verification failed: " + target);
        }
        private static void DurableWrite(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".preparing";
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            File.Move(temporary, path);
        }
    }
}
