namespace ASMLite.Editor
{
    /// <summary>
    /// Centralizes all package-relative asset path constants used by ASM-Lite editor tools.
    /// All editor scripts that reference generated or package assets should use these
    /// constants rather than declaring their own string literals.
    /// </summary>
    internal static class ASMLiteAssetPaths
    {
        internal const string FXController  = "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_FX.controller";
        internal const string ExprParams    = "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_Params.asset";
        internal const string Menu          = "Packages/com.staples.asm-lite/GeneratedAssets/ASMLite_Menu.asset";
        internal const string Prefab        = "Packages/com.staples.asm-lite/Prefabs/ASM-Lite.prefab";
        internal const string GeneratedDir  = "Packages/com.staples.asm-lite/GeneratedAssets";
    }
}
