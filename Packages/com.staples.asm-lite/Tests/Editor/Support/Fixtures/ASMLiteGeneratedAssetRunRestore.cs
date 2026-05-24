using System;
using System.IO;
using System.Linq;
using System.Text;
using Debug = UnityEngine.Debug;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [InitializeOnLoad]
    internal static class ASMLiteGeneratedAssetRunRestore
    {
        private static readonly string[] PackageRelativeRestorePaths =
        {
            "GeneratedAssets",
            "GeneratedAssets.meta",
            "Prefabs/ASM-Lite.prefab",
            "Prefabs/ASM-Lite.prefab.meta",
        };

        private static readonly string[] PackageRelativeCleanPaths =
        {
            "GeneratedAssets",
            "GeneratedAssets.meta",
        };

        private static readonly TestRunnerApi s_testRunnerApi;
        private static readonly CallbackForwarder s_callbackForwarder;

        static ASMLiteGeneratedAssetRunRestore()
        {
            s_testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
            s_callbackForwarder = new CallbackForwarder(RestoreTrackedPackageOutputs);
            s_testRunnerApi.RegisterCallbacks(s_callbackForwarder);
        }

        internal static RestoreResult RestoreTrackedPackageOutputs(string reason)
            => RestoreTrackedPackageOutputs(ResolvePackageRoot(), reason, refreshAssetDatabase: true);

        internal static RestoreResult RestoreTrackedPackageOutputs(string projectOrPackageRoot, string reason, bool refreshAssetDatabase)
        {
            if (string.IsNullOrWhiteSpace(projectOrPackageRoot))
                return RestoreResult.Failed("Project or package root was empty.");

            string packageRoot = ResolvePackageRootFromInput(projectOrPackageRoot);
            if (!Directory.Exists(packageRoot))
                return RestoreResult.Failed($"Package root does not exist: {packageRoot}");

            string gitRoot = FindGitRoot(packageRoot);
            if (string.IsNullOrEmpty(gitRoot))
                return RestoreResult.Failed($"No git root found for package root: {packageRoot}");

            string[] restorePathspecs = BuildGitPathspecs(gitRoot, packageRoot, PackageRelativeRestorePaths);
            string[] cleanPathspecs = BuildGitPathspecs(gitRoot, packageRoot, PackageRelativeCleanPaths);

            var restoreResult = RunGit(gitRoot, "restore", restorePathspecs);
            if (!restoreResult.Succeeded)
                return restoreResult;

            var cleanResult = RunGit(gitRoot, "clean", new[] { "-fd", "--" }.Concat(cleanPathspecs).ToArray(), includeSeparator: false);
            if (!cleanResult.Succeeded)
                return cleanResult;

            if (refreshAssetDatabase)
                RefreshRestoredAssets();

            Debug.Log($"[ASM-Lite] Restored generated package outputs for {reason}.");
            return RestoreResult.SucceededResult;
        }

        private static RestoreResult RunGit(string projectRoot, string command, string[] pathspecs, bool includeSeparator = true)
        {
            var arguments = new StringBuilder();
            arguments.Append("-C ").Append(Quote(projectRoot)).Append(' ').Append(command).Append(' ');
            if (includeSeparator)
                arguments.Append("-- ");
            arguments.Append(string.Join(" ", pathspecs.Select(Quote)));

            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return RestoreResult.Failed("Unable to start git process.");

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                        return RestoreResult.SucceededResult;

                    return RestoreResult.Failed($"git {command} exited {process.ExitCode}: {stdout}{stderr}".Trim());
                }
            }
            catch (Exception ex)
            {
                return RestoreResult.Failed($"git {command} failed: {ex.Message}");
            }
        }

        private static void RefreshRestoredAssets()
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

            return Path.GetFullPath(Path.Combine(ResolveProjectRoot(), "Packages", "com.staples.asm-lite"));
        }

        private static string ResolvePackageRootFromInput(string projectOrPackageRoot)
        {
            string root = Path.GetFullPath(projectOrPackageRoot);
            string nestedPackageRoot = Path.Combine(root, "Packages", "com.staples.asm-lite");
            return Directory.Exists(nestedPackageRoot)
                ? Path.GetFullPath(nestedPackageRoot)
                : root;
        }

        private static string FindGitRoot(string startPath)
        {
            var current = new DirectoryInfo(Path.GetFullPath(startPath));
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git")))
                    return current.FullName;
                current = current.Parent;
            }

            return string.Empty;
        }

        private static string[] BuildGitPathspecs(string gitRoot, string packageRoot, string[] packageRelativePaths)
            => packageRelativePaths
                .Select(path => ToGitPathspec(gitRoot, Path.Combine(packageRoot, path.Replace('/', Path.DirectorySeparatorChar))))
                .ToArray();

        private static string ToGitPathspec(string gitRoot, string absolutePath)
        {
            string normalizedGitRoot = Path.GetFullPath(gitRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string normalizedPath = Path.GetFullPath(absolutePath);
            var rootUri = new Uri(normalizedGitRoot);
            var pathUri = new Uri(normalizedPath);
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('\\', '/');
        }

        private static string ResolveProjectRoot()
            => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static string Quote(string value)
        {
            string safeValue = value ?? string.Empty;
            return "\"" + safeValue.Replace("\"", "\\\"") + "\"";
        }

        internal readonly struct RestoreResult
        {
            private RestoreResult(bool succeeded, string message)
            {
                Succeeded = succeeded;
                Message = message ?? string.Empty;
            }

            internal bool Succeeded { get; }
            internal string Message { get; }

            internal static RestoreResult SucceededResult => new RestoreResult(true, string.Empty);
            internal static RestoreResult Failed(string message) => new RestoreResult(false, message);
        }

        internal sealed class CallbackForwarder : ICallbacks
        {
            private readonly Func<string, RestoreResult> _restore;

            internal CallbackForwarder(Func<string, RestoreResult> restore)
            {
                _restore = restore ?? (_ => RestoreResult.SucceededResult);
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                RestoreOrWarn("test-run start");
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                RestoreOrWarn("test-run finish");
            }

            private void RestoreOrWarn(string reason)
            {
                var result = _restore(reason);
                if (!result.Succeeded)
                    Debug.LogWarning($"[ASM-Lite] Unable to restore generated package outputs for {reason}: {result.Message}");
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }
}
