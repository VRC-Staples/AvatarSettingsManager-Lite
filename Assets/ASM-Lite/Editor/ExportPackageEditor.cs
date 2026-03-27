using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    public static class ExportPackageEditor
    {
        private const string ExportPath = "Dist/ASM-Lite.unitypackage";
        private const string ExportRoot = "Assets/ASM-Lite";

        [MenuItem("ASM-Lite/Export Package")]
        public static void Export()
        {
            System.IO.Directory.CreateDirectory("Dist");
            ASMLitePrefabCreator.CreatePrefab();
            AssetDatabase.Refresh();
            AssetDatabase.ExportPackage(
                ExportRoot,
                ExportPath,
                ExportPackageOptions.Recurse);
            Debug.Log($"[ASM-Lite] Package exported to {ExportPath}");
        }
    }
}
