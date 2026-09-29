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

        private static string s_restoreFailureMessageForTesting;
        private static Action<string> s_restorePhaseForTesting;

        private readonly RootSnapshot[] _roots;

        private ASMLitePackageGeneratedOutputSnapshot(RootSnapshot[] roots)
        {
            _roots = roots ?? Array.Empty<RootSnapshot>();
        }

        internal static IDisposable PushRestoreFailureForTesting(string message)
        {
            string previous = s_restoreFailureMessageForTesting;
            s_restoreFailureMessageForTesting = string.IsNullOrWhiteSpace(message)
                ? "Injected package-output restore failure."
                : message;
            return new ScopedRestoreFailure(() => s_restoreFailureMessageForTesting = previous);
        }

        internal static IDisposable PushRestorePhaseForTesting(Action<string> callback)
        {
            var previous = s_restorePhaseForTesting;
            s_restorePhaseForTesting = callback;
            return new ScopedRestoreFailure(() => s_restorePhaseForTesting = previous);
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
            if (!string.IsNullOrEmpty(s_restoreFailureMessageForTesting))
                throw new InvalidOperationException(s_restoreFailureMessageForTesting);

            var preRestore = _roots.Select(root => RootSnapshot.Capture(root.FullPath)).ToArray();
            string recoveryPath = Path.GetFullPath(Path.Combine(".artifacts", "asm-lite-recovery", Guid.NewGuid().ToString("N")));
            bool replacementStarted = false;
            AssetDatabase.DisallowAutoRefresh();
            try
            {
                string packageRoot = ResolvePackageRoot();
                SaveRecoveryCopy(_roots, Path.Combine(recoveryPath, "captured-baseline"), packageRoot);
                SaveRecoveryCopy(preRestore, Path.Combine(recoveryPath, "pre-restore"), packageRoot);
                WriteDurableFile(Path.Combine(recoveryPath, "RECOVERY.txt"), System.Text.Encoding.UTF8.GetBytes(
                    "Package output recovery\nTarget package: " + packageRoot +
                    "\ncaptured-baseline: package outputs captured before the build.\n" +
                    "pre-restore: package outputs immediately before this restore attempt.\n" +
                    "Close Unity before manual recovery. Choose ONE copy, remove each listed target root, then copy its saved root and .meta files together.\n" +
                    "Use roots.txt in that copy: absent roots must remain absent. Keep this recovery directory until verified.\n"));
                s_restorePhaseForTesting?.Invoke("prepared");

                replacementStarted = true;
                ReplaceRoots(_roots, "replacement-deleted");
                ImportRestoredAssets();
            }
            catch (Exception restoreError)
            {
                Exception rollbackError = null;
                if (replacementStarted)
                {
                    try
                    {
                        ReplaceRoots(preRestore, "rollback-deleted");
                        ImportRestoredAssets();
                    }
                    catch (Exception ex)
                    {
                        rollbackError = ex;
                    }
                }

                string status = !replacementStarted ? "Package outputs were not replaced."
                    : rollbackError == null ? "Pre-restore package outputs recovered."
                    : "Rollback also failed: " + rollbackError.Message;
                string recoveryStatus = replacementStarted
                    ? $"Verified recovery copies retained at '{recoveryPath}'. See RECOVERY.txt; captured-baseline and pre-restore are different states."
                    : $"Recovery preparation did not finish. Any partial material at '{recoveryPath}' must not be used as a complete recovery copy.";
                var failure = new IOException($"Package-output restore failed: {restoreError.Message} {status} {recoveryStatus}",
                    rollbackError == null ? restoreError : new AggregateException(restoreError, rollbackError));
                failure.Data["RecoveryDirectory"] = recoveryPath;
                throw failure;
            }
            finally
            {
                AssetDatabase.AllowAutoRefresh();
            }

            // Cleanup failure does not undo an already verified successful restoration.
            try { Directory.Delete(recoveryPath, recursive: true); }
            catch (Exception ex) { UnityEngine.Debug.LogWarning($"[ASM-Lite] Outputs restored; recovery copy retained at '{recoveryPath}': {ex.Message}"); }
        }

        private static void ReplaceRoots(RootSnapshot[] roots, string phase)
        {
            foreach (var root in roots)
                root.DeleteCurrent();
            s_restorePhaseForTesting?.Invoke(phase);
            foreach (var root in roots)
                root.RestoreCaptured();
            foreach (var root in roots)
                root.Verify();
        }

        private static void SaveRecoveryCopy(RootSnapshot[] roots, string directory, string packageRoot)
        {
            Directory.CreateDirectory(directory);
            string manifest = string.Empty;
            foreach (var root in roots)
            {
                string relativePath = root.FullPath.Substring(packageRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var copy = root.AtPath(Path.Combine(directory, relativePath));
                copy.RestoreCaptured(durable: true);
                s_restorePhaseForTesting?.Invoke("recovery-copied");
                copy.Verify();
                manifest += relativePath + "\t" + root.Kind + "\n";
            }
            WriteDurableFile(Path.Combine(directory, "roots.txt"), System.Text.Encoding.UTF8.GetBytes(manifest));
        }

        private static void WriteDurableFile(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }
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

        private sealed class ScopedRestoreFailure : IDisposable
        {
            private readonly Action _restore;
            private bool _disposed;

            internal ScopedRestoreFailure(Action restore)
            {
                _restore = restore;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _restore?.Invoke();
            }
        }

        private sealed class RootSnapshot
        {
            internal string FullPath => _fullPath;
            internal string Kind => !_existed ? "absent" : _wasDirectory ? "directory" : "file";
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

            internal RootSnapshot AtPath(string path) => new RootSnapshot(path, _existed, _wasDirectory, _files);

            internal void Verify()
            {
                var actual = Capture(_fullPath);
                if (_existed != actual._existed || _wasDirectory != actual._wasDirectory
                    || _files.Length != actual._files.Length
                    || _files.Where((file, index) => file.RelativePath != actual._files[index].RelativePath
                        || !file.Bytes.SequenceEqual(actual._files[index].Bytes)).Any())
                    throw new IOException("Package-output verification failed: " + _fullPath);
            }

            internal void RestoreCaptured(bool durable = false)
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
                    if (durable)
                        WriteDurableFile(targetPath, file.Bytes);
                    else
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
