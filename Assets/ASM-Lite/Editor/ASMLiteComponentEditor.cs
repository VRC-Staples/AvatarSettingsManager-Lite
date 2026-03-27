using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// Minimal inspector for ASMLiteComponent.
    /// Full status and avatar diagnostics are shown in the ASM-Lite editor window
    /// (Tools → .Staples. → ASM-Lite).
    /// </summary>
    [CustomEditor(typeof(ASMLiteComponent))]
    public class ASMLiteComponentEditor : UnityEditor.Editor
    {
        private SerializedProperty _slotCountProp;

        private void OnEnable()
        {
            _slotCountProp = serializedObject.FindProperty("slotCount");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("ASM-Lite Component", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            EditorGUILayout.PropertyField(_slotCountProp, new GUIContent(
                "Slot Count",
                "Number of expression parameter slots managed by ASM-Lite."));

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Open Tools → .Staples. → ASM-Lite to manage this component and view status.",
                MessageType.Info);

            if (GUILayout.Button("Open ASM-Lite Window"))
                ASMLiteWindow.Open();

            serializedObject.ApplyModifiedProperties();
        }
    }
}
