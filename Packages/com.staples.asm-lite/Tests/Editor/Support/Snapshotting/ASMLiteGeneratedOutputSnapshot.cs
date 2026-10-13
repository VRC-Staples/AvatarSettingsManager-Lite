using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Animations;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    internal sealed class ASMLiteGeneratedOutputSnapshot
    {
        internal int BuildResult { get; }
        internal string ExprParamsText { get; }
        internal string MenuText { get; }
        internal string FxControllerMainObjectName { get; }
        internal string[] FxLayerNames { get; }
        internal string[] FxParameterNames { get; }
        internal string[] ExpressionParameterNames { get; }
        internal int FxDanglingLocalFileIdCount { get; }
        internal int SettingsManagerControlCount { get; }
        internal int LiveVrcFuryComponentCount { get; }
        internal string ControllerReferencePath { get; }
        internal string MenuReferencePath { get; }
        internal string ParameterReferenceResolvedPath { get; }
        internal string ParameterReferenceAssetPath { get; }

        private ASMLiteGeneratedOutputSnapshot(
            int buildResult,
            string exprParamsText,
            string menuText,
            string fxControllerMainObjectName,
            string[] fxLayerNames,
            string[] fxParameterNames,
            string[] expressionParameterNames,
            int fxDanglingLocalFileIdCount,
            int settingsManagerControlCount,
            int liveVrcFuryComponentCount,
            string controllerReferencePath,
            string menuReferencePath,
            string parameterReferenceResolvedPath,
            string parameterReferenceAssetPath)
        {
            BuildResult = buildResult;
            ExprParamsText = exprParamsText ?? string.Empty;
            MenuText = menuText ?? string.Empty;
            FxControllerMainObjectName = fxControllerMainObjectName ?? string.Empty;
            FxLayerNames = fxLayerNames ?? Array.Empty<string>();
            FxParameterNames = fxParameterNames ?? Array.Empty<string>();
            ExpressionParameterNames = expressionParameterNames ?? Array.Empty<string>();
            FxDanglingLocalFileIdCount = fxDanglingLocalFileIdCount;
            SettingsManagerControlCount = settingsManagerControlCount;
            LiveVrcFuryComponentCount = liveVrcFuryComponentCount;
            ControllerReferencePath = controllerReferencePath ?? string.Empty;
            MenuReferencePath = menuReferencePath ?? string.Empty;
            ParameterReferenceResolvedPath = parameterReferenceResolvedPath ?? string.Empty;
            ParameterReferenceAssetPath = parameterReferenceAssetPath ?? string.Empty;
        }

        internal static ASMLiteGeneratedOutputSnapshot Capture(ASMLiteComponent component, int buildResult)
        {
            var liveVrcFury = ASMLiteTestFixtures.FindLiveVrcFuryComponent(component != null ? component.gameObject : null);
            int liveVrcFuryCount = ASMLiteTestFixtures.FindLiveVrcFuryComponents(component != null ? component.gameObject : null).Length;
            var controllerRef = ASMLiteTestFixtures.ReadSerializedObjectReference(liveVrcFury, ASMLiteDriftProbe.ControllerObjectRefPath);
            var menuRef = ASMLiteTestFixtures.ReadSerializedObjectReference(liveVrcFury, ASMLiteDriftProbe.MenuObjectRefPath);
            var parameterRef = ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(
                liveVrcFury,
                ASMLiteDriftProbe.ParametersObjectRefPath,
                ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath);
            var fxController = controllerRef.Reference as AnimatorController;
            var exprParams = parameterRef.Reference as VRCExpressionParameters;
            var menu = menuRef.Reference as VRCExpressionsMenu;

            string[] fxLayerNames = fxController != null
                ? fxController.layers
                    .Where(layer => layer != null && !string.IsNullOrWhiteSpace(layer.name))
                    .Select(layer => layer.name.Trim())
                    .ToArray()
                : Array.Empty<string>();

            string[] fxParameterNames = fxController != null
                ? fxController.parameters
                    .Where(parameter => parameter != null && !string.IsNullOrWhiteSpace(parameter.name))
                    .Select(parameter => parameter.name.Trim())
                    .ToArray()
                : Array.Empty<string>();

            int settingsManagerControlCount = menu != null && menu.controls != null
                ? menu.controls.Count(control =>
                    control != null
                    && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && string.Equals(control.name, "Settings Manager", StringComparison.Ordinal))
                : 0;

            string fxText = ReadPackageAssetText(controllerRef.AssetPath);
            int danglingFxLocalFileIdCount = CountDanglingLocalFileIds(fxText);

            return new ASMLiteGeneratedOutputSnapshot(
                buildResult: buildResult,
                exprParamsText: ReadPackageAssetText(parameterRef.AssetPath),
                menuText: ReadMenuContent(menuRef.AssetPath),
                fxControllerMainObjectName: fxController != null ? fxController.name ?? string.Empty : string.Empty,
                fxLayerNames: fxLayerNames,
                fxParameterNames: fxParameterNames,
                expressionParameterNames: exprParams?.parameters?.Select(parameter => parameter?.name).ToArray(),
                fxDanglingLocalFileIdCount: danglingFxLocalFileIdCount,
                settingsManagerControlCount: settingsManagerControlCount,
                liveVrcFuryComponentCount: liveVrcFuryCount,
                controllerReferencePath: controllerRef.AssetPath,
                menuReferencePath: menuRef.AssetPath,
                parameterReferenceResolvedPath: parameterRef.PropertyPath,
                parameterReferenceAssetPath: parameterRef.AssetPath);
        }

        internal static string ReadPackageAssetText(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return string.Empty;

            string absolutePath = ResolveAssetAbsolutePath(assetPath);
            if (!File.Exists(absolutePath))
                return string.Empty;

            string text = File.ReadAllText(absolutePath);
            return NormalizeNewlines(text);
        }

        private static string ReadMenuContent(string assetPath)
        {
            // Rebuilt private submenu GUIDs can change. Keep their exact persistent paths and all payload fields.
            return Regex.Replace(ReadPackageAssetText(assetPath), @"guid: ([a-f0-9]{32})", match =>
            {
                string referencePath = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                return string.IsNullOrEmpty(referencePath) ? match.Value : "assetPath: " + referencePath;
            });
        }

        private static string ResolveAssetAbsolutePath(string assetPath)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, assetPath.Replace('\\', Path.DirectorySeparatorChar)));
        }

        internal static string NormalizeNewlines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace("\r", "\n");
        }

        private static int CountDanglingLocalFileIds(string fxText)
        {
            return ASMLiteCustomizationScaffoldTests.CountDanglingLocalFileIdsForTest(fxText);
        }
    }
}
