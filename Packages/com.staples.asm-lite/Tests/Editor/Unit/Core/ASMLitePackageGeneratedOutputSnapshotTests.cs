using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLitePackageGeneratedOutputSnapshotTests
    {
        private ASMLitePackageGeneratedOutputSnapshot _snapshot;

        [SetUp]
        public void CaptureBaseline()
        {
            _snapshot = ASMLitePackageGeneratedOutputSnapshot.Capture();
        }

        [TearDown]
        public void RestoreBaseline()
        {
            _snapshot?.Restore();
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        [Test]
        public void CaptureRestore_RestoresGeneratedAssetsFolderBytes()
        {
            string assetPath = ToFullPath(ASMLiteAssetPaths.ExprParams);
            byte[] baselineBytes = File.ReadAllBytes(assetPath);
            byte[] mutatedBytes = baselineBytes.Concat(new byte[] { 0x0A, 0x23, 0x20, 0x6D, 0x75, 0x74, 0x61, 0x74, 0x65, 0x64 }).ToArray();

            File.WriteAllBytes(assetPath, mutatedBytes);
            Assert.IsFalse(baselineBytes.SequenceEqual(File.ReadAllBytes(assetPath)),
                "Test setup should mutate the generated asset before restore.");

            _snapshot.Restore();

            CollectionAssert.AreEqual(baselineBytes, File.ReadAllBytes(assetPath),
                "Restore should put generated asset bytes back exactly.");
        }

        [Test]
        public void CaptureRestore_RemovesNewGeneratedAssetsFiles()
        {
            string tempAssetPath = ToFullPath(ASMLiteAssetPaths.GeneratedDir + "/ASMLitePackageGeneratedOutputSnapshotTests_Temp.asset");
            Directory.CreateDirectory(Path.GetDirectoryName(tempAssetPath));
            File.WriteAllText(tempAssetPath, "temporary generated asset");
            Assert.IsTrue(File.Exists(tempAssetPath), "Test setup should create a new generated asset file.");

            _snapshot.Restore();

            Assert.IsFalse(File.Exists(tempAssetPath),
                "Restore should remove generated asset files that were not present when captured.");
        }

        [Test]
        public void CaptureRestore_RestoresPackagePrefabBytes()
        {
            string prefabPath = ToFullPath(ASMLiteAssetPaths.Prefab);
            byte[] baselineBytes = File.ReadAllBytes(prefabPath);
            byte[] mutatedBytes = baselineBytes.Concat(new byte[] { 0x0A, 0x23, 0x20, 0x6D, 0x75, 0x74, 0x61, 0x74, 0x65, 0x64 }).ToArray();

            File.WriteAllBytes(prefabPath, mutatedBytes);
            Assert.IsFalse(baselineBytes.SequenceEqual(File.ReadAllBytes(prefabPath)),
                "Test setup should mutate the package prefab before restore.");

            _snapshot.Restore();

            CollectionAssert.AreEqual(baselineBytes, File.ReadAllBytes(prefabPath),
                "Restore should put package prefab bytes back exactly.");
        }

        [Test]
        public void Restore_AfterDeletionFailure_RollsBackToPreRestoreBytesAndMetadata()
        {
            string asset = ToFullPath(ASMLiteAssetPaths.ExprParams);
            byte[] preRestore = File.ReadAllBytes(asset).Concat(System.Text.Encoding.UTF8.GetBytes("\n# pre-restore\n")).ToArray();
            byte[] metadata = File.ReadAllBytes(asset + ".meta");
            File.WriteAllBytes(asset, preRestore);
            bool replacementStarted = false;
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
            {
                if (phase == "replacement-deleted")
                {
                    replacementStarted = !File.Exists(asset);
                    throw new IOException("Injected replacement failure.");
                }
            }))
                Assert.Throws<IOException>(() => _snapshot.Restore());

            Assert.IsTrue(replacementStarted, "Failure must occur after destructive replacement starts.");
            Assert.IsTrue(File.Exists(asset), "Caught failure must restore pre-restore output, not leave it deleted.");
            CollectionAssert.AreEqual(preRestore, File.ReadAllBytes(asset));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(asset + ".meta"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Restore_InterruptedReplacement_RetainsBothRecoveryStates(bool failRollback)
        {
            string[] paths = { ASMLiteAssetPaths.ExprParams, ASMLiteAssetPaths.ExprParams + ".meta",
                ASMLiteAssetPaths.GeneratedDir + ".meta", ASMLiteAssetPaths.Prefab, ASMLiteAssetPaths.Prefab + ".meta" };
            var baseline = paths.ToDictionary(path => path, path => File.ReadAllBytes(ToFullPath(path)));
            File.AppendAllText(ToFullPath(ASMLiteAssetPaths.ExprParams), "\n# pre-restore state\n");
            var preRestore = paths.ToDictionary(path => path, path => File.ReadAllBytes(ToFullPath(path)));
            string recoveryRoot = Path.GetFullPath(".artifacts/asm-lite-recovery");
            string[] existing = Directory.Exists(recoveryRoot) ? Directory.GetDirectories(recoveryRoot) : new string[0];
            bool copiesExistedDuringInterruption = false;
            IOException failure;
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
            {
                if (phase == "replacement-deleted")
                {
                    string recovery = Directory.GetDirectories(recoveryRoot).Except(existing).Single();
                    copiesExistedDuringInterruption = paths.All(path =>
                    {
                        string relative = path.Substring("Packages/com.staples.asm-lite/".Length);
                        return baseline[path].SequenceEqual(File.ReadAllBytes(Path.Combine(recovery, "captured-baseline", relative)))
                            && preRestore[path].SequenceEqual(File.ReadAllBytes(Path.Combine(recovery, "pre-restore", relative)));
                    }) && File.Exists(Path.Combine(recovery, "RECOVERY.txt"));
                    throw new IOException("Interrupted replacement.");
                }
                if (phase == "rollback-deleted" && failRollback)
                    throw new IOException("Interrupted rollback.");
            }))
                failure = Assert.Throws<IOException>(() => _snapshot.Restore());

            Assert.IsTrue(copiesExistedDuringInterruption, "Both complete copies must exist before replacement can lose data.");
            string retained = (string)failure.Data["RecoveryDirectory"];
            StringAssert.Contains(retained, failure.Message);
            StringAssert.Contains(failRollback ? "Rollback also failed" : "Pre-restore package outputs recovered", failure.Message);
            foreach (string path in paths)
            {
                string relative = path.Substring("Packages/com.staples.asm-lite/".Length);
                CollectionAssert.AreEqual(baseline[path], File.ReadAllBytes(Path.Combine(retained, "captured-baseline", relative)));
                CollectionAssert.AreEqual(preRestore[path], File.ReadAllBytes(Path.Combine(retained, "pre-restore", relative)));
                if (!failRollback)
                    CollectionAssert.AreEqual(preRestore[path], File.ReadAllBytes(ToFullPath(path)));
            }
        }

        [Test]
        public void Restore_UnverifiedRecoveryCopy_LeavesCurrentOutputsUntouched()
        {
            string asset = ToFullPath(ASMLiteAssetPaths.ExprParams);
            File.AppendAllText(asset, "\n# current outputs\n");
            byte[] current = File.ReadAllBytes(asset);
            byte[] metadata = File.ReadAllBytes(asset + ".meta");
            string recoveryRoot = Path.GetFullPath(".artifacts/asm-lite-recovery");
            string[] existing = Directory.Exists(recoveryRoot) ? Directory.GetDirectories(recoveryRoot) : new string[0];
            bool replacementStarted = false;
            IOException failure;
            using (ASMLitePackageGeneratedOutputSnapshot.PushRestorePhaseForTesting(phase =>
            {
                if (phase == "recovery-copied")
                {
                    string directory = Directory.GetDirectories(recoveryRoot).Except(existing).Single();
                    string relative = ASMLiteAssetPaths.ExprParams.Substring("Packages/com.staples.asm-lite/".Length);
                    File.AppendAllText(Path.Combine(directory, "captured-baseline", relative), "corrupt recovery copy");
                }
                if (phase == "replacement-deleted")
                    replacementStarted = true;
            }))
                failure = Assert.Throws<IOException>(() => _snapshot.Restore());

            StringAssert.Contains("verification failed", failure.Message);
            Assert.IsFalse(replacementStarted);
            CollectionAssert.AreEqual(current, File.ReadAllBytes(asset));
            CollectionAssert.AreEqual(metadata, File.ReadAllBytes(asset + ".meta"));
        }

        private static string ToFullPath(string assetPath)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(assetPath);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            {
                string packagePrefix = $"Packages/{packageInfo.name}/";
                if (assetPath.StartsWith(packagePrefix))
                {
                    string relativePath = assetPath.Substring(packagePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
                    return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, relativePath));
                }
            }

            return Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));
        }
    }
}
