using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// Minimal inspector for ASMLiteComponent.
    /// Slot count and all configuration are managed in the ASM-Lite editor window
    /// (Tools → .Staples. → ASM-Lite).
    /// </summary>
    [CustomEditor(typeof(ASMLiteComponent))]
    public class ASMLiteComponentEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("ASM-Lite Component", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.HelpBox(
                "Open Tools → .Staples. → ASM-Lite to configure and manage this component.",
                MessageType.Info);

            if (GUILayout.Button("Open ASM-Lite Window"))
                ASMLiteWindow.Open();
        }
    }
}
