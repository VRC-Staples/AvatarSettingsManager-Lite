using System;

namespace ASMLite.Tests.Editor
{
    internal static class ASMLiteSmokeResetService
    {
        internal const string PolicyInherit = "Inherit";
        internal const string PolicySceneReload = "SceneReload";
        internal const string PolicyFullPackageRebuild = "FullPackageRebuild";

        internal static string ResolveEffectivePolicy(string requestedResetDefault, string suiteResetOverride)
        {
            string normalizedDefault = NormalizeDefault(requestedResetDefault);
            string normalizedOverride = NormalizeOverride(suiteResetOverride);

            if (string.Equals(normalizedOverride, PolicyInherit, StringComparison.Ordinal))
                return normalizedDefault;

            return normalizedOverride;
        }

        private static string NormalizeDefault(string requestedResetDefault)
        {
            if (string.IsNullOrWhiteSpace(requestedResetDefault))
                return PolicySceneReload;

            string token = requestedResetDefault.Trim();
            if (string.Equals(token, PolicySceneReload, StringComparison.Ordinal))
                return PolicySceneReload;
            if (string.Equals(token, PolicyFullPackageRebuild, StringComparison.Ordinal))
                return PolicyFullPackageRebuild;

            throw new InvalidOperationException($"Unsupported global reset default '{requestedResetDefault}'.");
        }

        private static string NormalizeOverride(string suiteResetOverride)
        {
            if (string.IsNullOrWhiteSpace(suiteResetOverride))
                return PolicyInherit;

            string token = suiteResetOverride.Trim();
            if (string.Equals(token, PolicyInherit, StringComparison.Ordinal))
                return PolicyInherit;
            if (string.Equals(token, PolicySceneReload, StringComparison.Ordinal))
                return PolicySceneReload;
            if (string.Equals(token, PolicyFullPackageRebuild, StringComparison.Ordinal))
                return PolicyFullPackageRebuild;

            throw new InvalidOperationException($"Unsupported suite resetOverride '{suiteResetOverride}'.");
        }
    }
}
