using System;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    internal sealed class ASMLiteDirectDeliveryRollbackSnapshot : IDisposable
    {
        private readonly VRCExpressionParameters _expressionParametersAsset;
        private readonly VRCExpressionParameters _expressionParametersClone;
        private readonly VRCExpressionsMenu _expressionsMenuAsset;
        private readonly VRCExpressionsMenu _expressionsMenuClone;
        private readonly AnimatorController _fxControllerAsset;
        private readonly AnimatorController _fxControllerClone;
        private readonly int _fxLayerIndex;
        private readonly VRCAvatarDescriptor.CustomAnimLayer _originalFxLayer;
        private bool _disposed;

        private ASMLiteDirectDeliveryRollbackSnapshot(VRCAvatarDescriptor avatar)
        {
            _expressionParametersAsset = avatar != null ? avatar.expressionParameters : null;
            _expressionParametersClone = CloneForRollback(_expressionParametersAsset);
            _expressionsMenuAsset = avatar != null ? avatar.expressionsMenu : null;
            _expressionsMenuClone = CloneForRollback(_expressionsMenuAsset);
            _fxLayerIndex = FindFxLayerIndex(avatar);
            if (_fxLayerIndex >= 0 && avatar != null)
            {
                _originalFxLayer = avatar.baseAnimationLayers[_fxLayerIndex];
                _fxControllerAsset = _originalFxLayer.animatorController as AnimatorController;
                _fxControllerClone = CloneForRollback(_fxControllerAsset);
            }
            else
            {
                _originalFxLayer = default;
            }
        }

        internal static ASMLiteDirectDeliveryRollbackSnapshot Capture(VRCAvatarDescriptor avatar)
        {
            return new ASMLiteDirectDeliveryRollbackSnapshot(avatar);
        }

        internal bool TryRestore(VRCAvatarDescriptor avatar, out string failureContext, out string failureRemediation)
        {
            failureContext = string.Empty;
            failureRemediation = string.Empty;
            if (avatar == null)
            {
                failureContext = "avatar";
                failureRemediation = "Capture a valid direct-delivery rollback snapshot before attempting detach rollback.";
                return false;
            }

            try
            {
                Restore(avatar);
                return true;
            }
            catch (Exception ex)
            {
                failureContext = ex.GetType().Name;
                failureRemediation = ex.Message;
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_expressionParametersClone != null)
                UnityEngine.Object.DestroyImmediate(_expressionParametersClone);
            if (_expressionsMenuClone != null)
                UnityEngine.Object.DestroyImmediate(_expressionsMenuClone);
            if (_fxControllerClone != null)
                UnityEngine.Object.DestroyImmediate(_fxControllerClone);
        }

        private void Restore(VRCAvatarDescriptor avatar)
        {
            if (_expressionParametersAsset != null && _expressionParametersClone != null)
            {
                EditorUtility.CopySerialized(_expressionParametersClone, _expressionParametersAsset);
                if (avatar.expressionParameters != _expressionParametersAsset)
                    avatar.expressionParameters = _expressionParametersAsset;
                EditorUtility.SetDirty(_expressionParametersAsset);
            }

            if (_expressionsMenuAsset != null && _expressionsMenuClone != null)
            {
                EditorUtility.CopySerialized(_expressionsMenuClone, _expressionsMenuAsset);
                if (avatar.expressionsMenu != _expressionsMenuAsset)
                    avatar.expressionsMenu = _expressionsMenuAsset;
                EditorUtility.SetDirty(_expressionsMenuAsset);
            }

            if (_fxLayerIndex >= 0 && _fxLayerIndex < avatar.baseAnimationLayers.Length)
            {
                if (_fxControllerAsset != null && _fxControllerClone != null)
                {
                    EditorUtility.CopySerialized(_fxControllerClone, _fxControllerAsset);
                    EditorUtility.SetDirty(_fxControllerAsset);
                }

                var restoredLayer = _originalFxLayer;
                restoredLayer.animatorController = _originalFxLayer.animatorController;
                avatar.baseAnimationLayers[_fxLayerIndex] = restoredLayer;
            }

            EditorUtility.SetDirty(avatar);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        private static T CloneForRollback<T>(T source)
            where T : UnityEngine.Object
        {
            if (source == null)
                return null;

            var clone = UnityEngine.Object.Instantiate(source);
            clone.hideFlags = HideFlags.HideAndDontSave;
            return clone;
        }

        private static int FindFxLayerIndex(VRCAvatarDescriptor avatar)
        {
            if (avatar == null || avatar.baseAnimationLayers == null)
                return -1;

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                if (avatar.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    return i;
            }

            return -1;
        }
    }
}
