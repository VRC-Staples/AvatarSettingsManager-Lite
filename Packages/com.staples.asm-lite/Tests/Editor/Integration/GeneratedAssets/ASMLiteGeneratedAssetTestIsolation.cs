using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteGeneratedAssetTestIsolation
    {
        private const string TempDir = "Assets/ASMLiteTests_Temp";

        internal static string[] BuiltInIconFixturePaths()
            => new[] { ASMLiteAssetPaths.IconPresets }
                .Concat(ASMLiteAssetPaths.GearIconPaths)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

        internal static Texture2D CopyIconFixtureOrFail(string sourceAssetPath, string aid, string label)
        {
            AssertReadableAssetFile(sourceAssetPath, aid);
            EnsureTempFolder();

            string safeLabel = SanitizeFileToken(label);
            string sourceFileName = Path.GetFileName(sourceAssetPath);
            string targetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{TempDir}/{aid}_{safeLabel}_{sourceFileName}");

            Assert.IsTrue(AssetDatabase.CopyAsset(sourceAssetPath, targetPath),
                $"{aid}: failed to copy icon fixture from '{sourceAssetPath}' to isolated path '{targetPath}'.");
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);

            var copiedIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(targetPath);
            Assert.IsNotNull(copiedIcon,
                $"{aid}: copied icon fixture at '{targetPath}' could not be loaded as Texture2D.");
            return copiedIcon;
        }

        internal static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempDir))
            {
                Assert.IsTrue(AssetDatabase.DeleteAsset(TempDir),
                    $"Failed to delete isolated custom icon temp folder '{TempDir}'.");
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        internal sealed class GeneratedAssetsSnapshot
        {
            private readonly Dictionary<string, byte[]> _filesByRelativePath;

            private GeneratedAssetsSnapshot(Dictionary<string, byte[]> filesByRelativePath)
            {
                _filesByRelativePath = filesByRelativePath;
            }

            internal static GeneratedAssetsSnapshot Capture(string suiteName)
            {
                string fullFolderPath = ToGeneratedAssetsFullPath();
                Assert.IsTrue(Directory.Exists(fullFolderPath),
                    $"{suiteName}: generated asset folder is missing at '{ASMLiteAssetPaths.GeneratedDir}'.");

                var filesByRelativePath = Directory
                    .GetFiles(fullFolderPath, "*", SearchOption.AllDirectories)
                    .ToDictionary(
                        filePath => ToRelativePath(fullFolderPath, filePath),
                        File.ReadAllBytes,
                        StringComparer.Ordinal);

                return new GeneratedAssetsSnapshot(filesByRelativePath);
            }

            internal void Restore()
            {
                string fullFolderPath = ToGeneratedAssetsFullPath();
                if (Directory.Exists(fullFolderPath))
                {
                    foreach (string filePath in Directory.GetFiles(fullFolderPath, "*", SearchOption.AllDirectories))
                        File.Delete(filePath);

                    foreach (string directoryPath in Directory.GetDirectories(fullFolderPath, "*", SearchOption.AllDirectories)
                                 .OrderByDescending(path => path.Length))
                    {
                        if (!Directory.EnumerateFileSystemEntries(directoryPath).Any())
                            Directory.Delete(directoryPath);
                    }
                }
                else
                {
                    Directory.CreateDirectory(fullFolderPath);
                }

                foreach (var file in _filesByRelativePath)
                {
                    string targetPath = Path.Combine(fullFolderPath, file.Key.Replace('/', Path.DirectorySeparatorChar));
                    string targetDirectory = Path.GetDirectoryName(targetPath);
                    if (!string.IsNullOrEmpty(targetDirectory))
                        Directory.CreateDirectory(targetDirectory);
                    File.WriteAllBytes(targetPath, file.Value);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        internal sealed class SourceAssetsSnapshot
        {
            private readonly Dictionary<string, byte[]> _filesByAssetPath;

            private SourceAssetsSnapshot(Dictionary<string, byte[]> filesByAssetPath)
            {
                _filesByAssetPath = filesByAssetPath;
            }

            internal static SourceAssetsSnapshot Capture(string suiteName, IEnumerable<string> assetPaths)
            {
                Assert.IsNotNull(assetPaths, $"{suiteName}: source asset snapshot requires fixture paths.");

                var filesByAssetPath = assetPaths
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.Ordinal)
                    .SelectMany(AssetAndMetaPaths)
                    .ToDictionary(
                        assetPath => assetPath,
                        assetPath => ReadAssetFileBytes(assetPath, suiteName),
                        StringComparer.Ordinal);

                return new SourceAssetsSnapshot(filesByAssetPath);
            }

            internal void Restore()
            {
                foreach (var entry in _filesByAssetPath)
                {
                    string fullPath = ToProjectFullPath(entry.Key);
                    string directory = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);
                    File.WriteAllBytes(fullPath, entry.Value);
                }

                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            internal void AssertUnchanged(string suiteName)
            {
                foreach (var entry in _filesByAssetPath)
                {
                    string fullPath = ToProjectFullPath(entry.Key);
                    Assert.IsTrue(File.Exists(fullPath),
                        $"{suiteName}: source fixture asset '{entry.Key}' was removed during the test.");

                    byte[] currentBytes = File.ReadAllBytes(fullPath);
                    Assert.IsTrue(entry.Value.SequenceEqual(currentBytes),
                        $"{suiteName}: source fixture asset '{entry.Key}' changed during the test; copy or generate test inputs under '{TempDir}' instead.");
                }
            }
        }

        internal static GeneratedAssetsSnapshot CaptureGeneratedAssets(string suiteName)
            => GeneratedAssetsSnapshot.Capture(suiteName);

        internal static SourceAssetsSnapshot CaptureSourceAssets(string suiteName, IEnumerable<string> assetPaths)
            => SourceAssetsSnapshot.Capture(suiteName, assetPaths);

        private static void EnsureTempFolder()
        {
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");
        }

        private static void AssertReadableAssetFile(string assetPath, string aid)
        {
            string fullPath = ToProjectFullPath(assetPath);
            Assert.IsTrue(File.Exists(fullPath),
                $"{aid}: expected source fixture asset at '{assetPath}' but no file exists at '{fullPath}'.");
        }

        private static byte[] ReadAssetFileBytes(string assetPath, string suiteName)
        {
            string fullPath = ToProjectFullPath(assetPath);
            Assert.IsTrue(File.Exists(fullPath),
                $"{suiteName}: source fixture asset '{assetPath}' is missing at '{fullPath}'.");
            return File.ReadAllBytes(fullPath);
        }

        private static IEnumerable<string> AssetAndMetaPaths(string assetPath)
        {
            yield return assetPath;

            string metaPath = assetPath + ".meta";
            if (File.Exists(ToProjectFullPath(metaPath)))
                yield return metaPath;
        }

        private static string ToGeneratedAssetsFullPath()
            => Path.GetFullPath(ASMLiteAssetPaths.GeneratedDir);

        private static string ToProjectFullPath(string assetPath)
            => Path.GetFullPath(assetPath.Replace('/', Path.DirectorySeparatorChar));

        private static string ToRelativePath(string fullFolderPath, string filePath)
        {
            string normalizedFolder = Path.GetFullPath(fullFolderPath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedFile = Path.GetFullPath(filePath);
            return normalizedFile.Substring(normalizedFolder.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        private static string SanitizeFileToken(string value)
        {
            string token = string.IsNullOrWhiteSpace(value) ? "Icon" : value;
            return new string(token.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }
    }
}
