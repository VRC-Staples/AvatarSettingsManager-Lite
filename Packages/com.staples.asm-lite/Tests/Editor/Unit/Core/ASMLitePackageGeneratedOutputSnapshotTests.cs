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
