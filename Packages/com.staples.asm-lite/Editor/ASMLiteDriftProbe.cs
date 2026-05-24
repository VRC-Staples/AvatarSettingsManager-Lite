using System;
using UnityEditor;

namespace ASMLite.Editor
{
    internal static class ASMLiteDriftProbe
    {
        internal const string ControllersArrayPath = "content.controllers";
        internal const string ControllerObjectRefPath = "content.controllers.Array.data[0].controller.objRef";
        internal const string ControllerTypePath = "content.controllers.Array.data[0].type";
        internal const string MenuArrayPath = "content.menus";
        internal const string MenuObjectRefPath = "content.menus.Array.data[0].menu.objRef";
        internal const string MenuPrefixPath = "content.menus.Array.data[0].prefix";
        internal const string ParametersArrayPath = "content.prms";
        internal const string ParametersObjectRefPath = "content.prms.Array.data[0].parameters.objRef";
        internal const string ParameterObjectRefPath = "content.prms.Array.data[0].parameter.objRef";
        internal const string ParameterLegacyObjectRefPath = "content.prms.Array.data[0].objRef";
        internal const string ControllerMirrorPath = "content.controller.objRef";
        internal const string MenuMirrorPath = "content.menu.objRef";
        internal const string ParametersMirrorPath = "content.parameters.objRef";

        internal const string ParameterFallbackGroupKey = "content.prms[0].parameters.objRef:any-of";

        private static readonly string[] s_requiredPaths =
        {
            ControllersArrayPath,
            MenuArrayPath,
            ParametersArrayPath,
            ControllerMirrorPath,
            MenuMirrorPath,
            ParametersMirrorPath,
        };

        internal static ASMLiteDriftProbeResult ValidateCriticalFullControllerWritePaths(SerializedObject serializedVfComponent)
        {
            if (serializedVfComponent == null)
            {
                return ASMLiteDriftProbeResult.Fail(
                    ASMLiteDiagnosticCodes.Drift.MissingRequiredPath,
                    "serialized-vf-component",
                    "Pass a live SerializedObject for the VRCFury component before probing write paths.");
            }

            for (int i = 0; i < s_requiredPaths.Length; i++)
            {
                string path = s_requiredPaths[i];
                if (serializedVfComponent.FindProperty(path) != null)
                    continue;

                return ASMLiteDriftProbeResult.Fail(
                    GetRequiredPathCode(path),
                    path,
                    GetRequiredPathRemediation(path));
            }

            return ASMLiteDriftProbeResult.Pass();
        }

        internal static ASMLiteDriftProbeResult ValidateInstallPrefixWritePath(SerializedObject serializedVfComponent)
        {
            if (serializedVfComponent == null)
            {
                return ASMLiteDriftProbeResult.Fail(
                    ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath,
                    MenuPrefixPath,
                    "Pass a live SerializedObject for the VRCFury component before syncing install-prefix wiring.");
            }

            if (serializedVfComponent.FindProperty(MenuPrefixPath) != null)
                return ASMLiteDriftProbeResult.Pass();

            return ASMLiteDriftProbeResult.Fail(
                ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath,
                MenuPrefixPath,
                GetRequiredPathRemediation(MenuPrefixPath));
        }

        private static string GetRequiredPathCode(string path)
        {
            return string.Equals(path, MenuPrefixPath, StringComparison.Ordinal)
                ? ASMLiteDiagnosticCodes.Drift.MissingMenuPrefixPath
                : ASMLiteDiagnosticCodes.Drift.MissingRequiredPath;
        }

        private static string GetRequiredPathRemediation(string path)
        {
            if (string.Equals(path, MenuPrefixPath, StringComparison.Ordinal))
                return "Update VRCFury FullController schema mapping so menu prefix write path remains available.";

            return "Update ASM-Lite FullController path mapping for this VRCFury schema before applying writes.";
        }
    }

    internal readonly struct ASMLiteDriftProbeResult
    {
        internal ASMLiteDriftProbeResult(bool success, string code, string failingPathOrGroupKey, string remediation)
        {
            Success = success;
            Code = code ?? string.Empty;
            FailingPathOrGroupKey = failingPathOrGroupKey ?? string.Empty;
            Remediation = remediation ?? string.Empty;
        }

        internal bool Success { get; }
        internal string Code { get; }
        internal string FailingPathOrGroupKey { get; }
        internal string Remediation { get; }

        internal string Message => Success ? string.Empty : ASMLiteDiagnosticCodes.GetMessage(Code);

        internal static ASMLiteDriftProbeResult Pass()
        {
            return new ASMLiteDriftProbeResult(success: true, code: string.Empty, failingPathOrGroupKey: string.Empty, remediation: string.Empty);
        }

        internal static ASMLiteDriftProbeResult Fail(string code, string failingPathOrGroupKey, string remediation)
        {
            return new ASMLiteDriftProbeResult(success: false, code: code, failingPathOrGroupKey: failingPathOrGroupKey, remediation: remediation);
        }

        internal ASMLiteBuildDiagnosticResult ToDiagnosticResult()
        {
            if (Success)
                return ASMLiteBuildDiagnosticResult.Pass();

            return ASMLiteBuildDiagnosticResult.Fail(
                code: Code,
                contextPath: FailingPathOrGroupKey,
                remediation: Remediation,
                message: Message);
        }
    }
}
