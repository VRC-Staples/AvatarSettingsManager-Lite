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

        // ─── Gear icon assets ─────────────────────────────────────────────────

        /// <summary>
        /// Package-relative path to the folder containing the built-in gear icons.
        /// </summary>
        internal const string GearsDir = "Packages/com.staples.asm-lite/Icons/Gears";

        /// <summary>
        /// Ordered list of package-relative paths to the built-in gear PNG icons.
        /// Index matches IconMode colour indices: 0=Blue, 1=Red, 2=Green, 3=Purple,
        /// 4=Cyan, 5=Orange, 6=Pink, 7=Yellow.
        /// </summary>
        internal static readonly string[] GearIconPaths = new string[]
        {
            "Packages/com.staples.asm-lite/Icons/Gears/BlueGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/RedGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/GreenGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/PurpleGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/CyanGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/OrangeGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/PinkGear.png",
            "Packages/com.staples.asm-lite/Icons/Gears/YellowGear.png",
        };
    }
}
