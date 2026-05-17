using System;
using System.IO;
using System.Linq;
using UnityEditor;

namespace ASMLite.Editor
{
    internal sealed class ASMLitePackageGeneratedOutputSnapshot
    {
        private const string PackageName = "com.staples.asm-lite";
        private const string PackagePrefix = "Packages/" + PackageName + "/";

        private readonly RootSnapshot[] _roots;

        private ASMLitePackageGeneratedOutputSnapshot(RootSnapshot[] roots)
        {
            _roots = roots ?? Array.Empty<RootSnapshot>();
        }

        internal static ASMLitePackageGeneratedOutputSnapshot Capture()
        {
            string packageRoot = ResolvePackageRoot();
            string[] rootAssetPaths =
            {
                ASMLiteAssetPaths.GeneratedDir,
                ASMLiteAssetPaths.GeneratedDir + ".meta",
                ASMLiteAssetPaths.Prefab,
                ASMLiteAssetPaths.Prefab + ".meta",
            };

            var roots = rootAssetPaths
                .Select(assetPath => RootSnapshot.Capture(ResolvePackagePath(packageRoot, assetPath)))
                .ToArray();

            return new ASMLitePackageGeneratedOutputSnapshot(roots);
        }

        internal void Restore()
        {
            foreach (RootSnapshot root in _roots)
                root.DeleteCurrent();

            foreach (RootSnapshot root in _roots)
                root.RestoreCaptured();

            ImportRestoredAssets();
        }

        private static void ImportRestoredAssets()
        {
            AssetDatabase.ImportAsset(ASMLiteAssetPaths.GeneratedDir, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ImportRecursive);
            AssetDatabase.ImportAsset(ASMLiteAssetPaths.Prefab, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        }

        private static string ResolvePackageRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(ASMLiteAssetPaths.Prefab);
            if (packageInfo != null && !string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
                return Path.GetFullPath(packageInfo.resolvedPath);

            return Path.GetFullPath(Path.Combine("Packages", PackageName));
        }

        private static string ResolvePackagePath(string packageRoot, string assetPath)
        {
            if (assetPath.StartsWith(PackagePrefix, StringComparison.Ordinal))
            {
                string relativePath = assetPath.Substring(PackagePrefix.Length).Replace('/', Path.DirectorySeparatorChar);
                return Path.GetFullPath(Path.Combine(packageRoot, relativePath));
            }

            return Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        private sealed class RootSnapshot
        {
            private readonly string _fullPath;
            private readonly bool _existed;
            private readonly bool _wasDirectory;
            private readonly CapturedFile[] _files;

            private RootSnapshot(string fullPath, bool existed, bool wasDirectory, CapturedFile[] files)
            {
                _fullPath = fullPath;
                _existed = existed;
                _wasDirectory = wasDirectory;
                _files = files ?? Array.Empty<CapturedFile>();
            }

            internal static RootSnapshot Capture(string fullPath)
            {
                if (Directory.Exists(fullPath))
                {
                    var files = Directory
                        .GetFiles(fullPath, "*", SearchOption.AllDirectories)
                        .Select(filePath => CapturedFile.Capture(fullPath, filePath))
                        .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
                        .ToArray();
                    return new RootSnapshot(fullPath, existed: true, wasDirectory: true, files: files);
                }

                if (File.Exists(fullPath))
                {
                    var files = new[] { CapturedFile.Capture(Path.GetDirectoryName(fullPath), fullPath) };
                    return new RootSnapshot(fullPath, existed: true, wasDirectory: false, files: files);
                }

                return new RootSnapshot(fullPath, existed: false, wasDirectory: false, files: Array.Empty<CapturedFile>());
            }

            internal void DeleteCurrent()
            {
                if (Directory.Exists(_fullPath))
                    Directory.Delete(_fullPath, recursive: true);
                else if (File.Exists(_fullPath))
                    File.Delete(_fullPath);
            }

            internal void RestoreCaptured()
            {
                if (!_existed)
                    return;

                if (_wasDirectory)
                    Directory.CreateDirectory(_fullPath);

                string basePath = _wasDirectory ? _fullPath : Path.GetDirectoryName(_fullPath);
                foreach (CapturedFile file in _files)
                {
                    string targetPath = Path.Combine(basePath, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                    string targetDirectory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);
                    File.WriteAllBytes(targetPath, file.Bytes);
                }
            }
        }

        private sealed class CapturedFile
        {
            private CapturedFile(string relativePath, byte[] bytes)
            {
                RelativePath = relativePath;
                Bytes = bytes;
            }

            internal string RelativePath { get; }
            internal byte[] Bytes { get; }

            internal static CapturedFile Capture(string rootPath, string filePath)
            {
                string normalizedRoot = Path.GetFullPath(rootPath)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string normalizedPath = Path.GetFullPath(filePath);
                string relativePath = normalizedPath.Substring(normalizedRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
                return new CapturedFile(relativePath, File.ReadAllBytes(filePath));
            }
        }
    }
}
