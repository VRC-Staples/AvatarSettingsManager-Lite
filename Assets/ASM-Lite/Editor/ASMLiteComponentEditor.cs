using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ASMLite.Editor
{
    /// <summary>
    /// Custom inspector for ASMLiteComponent. Displays component status,
    /// slot count, and a brief help box directing users to the VRCFury
    /// Full Controller component on the same prefab.
    /// </summary>
    [CustomEditor(typeof(ASMLiteComponent))]
    public class ASMLiteComponentEditor : UnityEditor.Editor
    {
        // Serialized property cache
        private SerializedProperty _slotCountProp;

        private void OnEnable()
        {
            _slotCountProp = serializedObject.FindProperty("slotCount");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var component = (ASMLiteComponent)target;

            // ─── Header ───────────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("ASM-Lite Component", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            // ─── Help box ─────────────────────────────────────────────────────
            EditorGUILayout.HelpBox(
                "ASM-Lite manages avatar expression parameter slots via VRCFury. " +
                "Add a VRCFury Full Controller component to the same GameObject to link " +
                "your FX controller and expression menu/parameters.",
                MessageType.Info);

            EditorGUILayout.Space(6);

            // ─── Slot count field ─────────────────────────────────────────────
            EditorGUILayout.PropertyField(_slotCountProp, new GUIContent(
                "Slot Count",
                "Number of expression parameter slots managed by ASM-Lite (informational)."));

            EditorGUILayout.Space(4);

            // ─── Status row ───────────────────────────────────────────────────
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            bool hasAvatarDescriptor = component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>() != null;
            string avatarStatus = hasAvatarDescriptor
                ? "✓ Avatar descriptor found in parent hierarchy"
                : "⚠ No VRC Avatar Descriptor found in parent hierarchy";
            MessageType avatarMsgType = hasAvatarDescriptor ? MessageType.Info : MessageType.Warning;
            EditorGUILayout.HelpBox(avatarStatus, avatarMsgType);

            // ─── Parameter count & slot info ──────────────────────────────────
            if (hasAvatarDescriptor)
            {
                var descriptor = component.GetComponentInParent<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>();
                var exprParams = descriptor != null ? descriptor.expressionParameters : null;

                if (exprParams != null && exprParams.parameters != null)
                {
                    int customCount = exprParams.parameters
                        .Count(p => !string.IsNullOrEmpty(p.name) && !p.name.StartsWith("ASMLite_"));
                    int slotCount = component.slotCount;
                    EditorGUILayout.HelpBox(
                        string.Format("\u2713 {0} custom parameter(s) will be backed up across {1} slot(s).", customCount, slotCount),
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "\u26a0 No VRCExpressionParameters asset assigned on descriptor.",
                        MessageType.Warning);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
