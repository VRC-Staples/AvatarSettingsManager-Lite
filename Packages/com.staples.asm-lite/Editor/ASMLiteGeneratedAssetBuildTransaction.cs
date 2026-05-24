namespace ASMLite.Editor
{
    internal readonly struct ASMLiteGeneratedAssetBuildTransactionResult
    {
        private ASMLiteGeneratedAssetBuildTransactionResult(
            bool success,
            int discoveredParamCount,
            ASMLiteBuildDiagnosticResult diagnostic)
        {
            Success = success;
            DiscoveredParamCount = discoveredParamCount;
            Diagnostic = diagnostic ?? ASMLiteBuildDiagnosticResult.Pass();
        }

        internal bool Success { get; }
        internal int DiscoveredParamCount { get; }
        internal ASMLiteBuildDiagnosticResult Diagnostic { get; }
        internal string Message => Diagnostic.Message;
        internal string ContextPath => Diagnostic.ContextPath;
        internal string Remediation => Diagnostic.Remediation;

        internal static ASMLiteGeneratedAssetBuildTransactionResult Pass(int discoveredParamCount)
        {
            return new ASMLiteGeneratedAssetBuildTransactionResult(
                success: true,
                discoveredParamCount: discoveredParamCount,
                diagnostic: ASMLiteBuildDiagnosticResult.Pass());
        }

        internal static ASMLiteGeneratedAssetBuildTransactionResult Fail(
            int discoveredParamCount,
            ASMLiteBuildDiagnosticResult diagnostic)
        {
            return new ASMLiteGeneratedAssetBuildTransactionResult(
                success: false,
                discoveredParamCount: discoveredParamCount,
                diagnostic: diagnostic);
        }
    }

    internal static class ASMLiteGeneratedAssetBuildTransaction
    {
        internal static ASMLiteGeneratedAssetBuildTransactionResult Execute(ASMLiteComponent component)
        {
            int discoveredParamCount = ASMLiteBuilder.Build(component);
            var diagnostic = ASMLiteBuilder.GetLatestBuildDiagnosticResult();
            if (discoveredParamCount < 0 || diagnostic == null || !diagnostic.Success)
            {
                if (diagnostic == null || diagnostic.Success)
                {
                    diagnostic = ASMLiteBuildDiagnosticResult.Fail(
                        code: ASMLiteDiagnosticCodes.Build.ValidationFailed,
                        contextPath: ASMLiteAssetPaths.GeneratedDir,
                        remediation: "Inspect the generated asset build logs, fix the failing build input, then retry the lifecycle transaction.",
                        message: "[ASM-Lite] Generated asset build failed without a specific diagnostic.");
                }

                return ASMLiteGeneratedAssetBuildTransactionResult.Fail(discoveredParamCount, diagnostic);
            }

            return ASMLiteGeneratedAssetBuildTransactionResult.Pass(discoveredParamCount);
        }
    }
}
