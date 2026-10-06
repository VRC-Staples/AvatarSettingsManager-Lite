using System;
using System.IO;
using System.Linq;
using System.Reflection;
using ASMLite.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("GraphicsRequired")]
    public sealed class ASMLiteVRCFuryPrefabOverrideTests
    {
        private string _folder;
        private GameObject _instance;
        private ASMLiteComponent _component;
        private AnimatorController _controller;
        private VRCExpressionsMenu _menu;
        private VRCExpressionParameters _parameters;
        private byte[] _prefabBefore;


        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/__ASMLitePrefabOverrides_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(_folder));
            _controller = new AnimatorController();
            _menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            _parameters = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            AssetDatabase.CreateAsset(_controller, _folder + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            AssetDatabase.CreateAsset(_menu, _folder + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            AssetDatabase.CreateAsset(_parameters, _folder + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            _prefabBefore = File.ReadAllBytes(ASMLiteAssetPaths.Prefab);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);
            Assert.IsNotNull(prefab);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _component = _instance.GetComponent<ASMLiteComponent>();
            Assert.IsNotNull(_component);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
                Object.DestroyImmediate(_instance);
            AssetDatabase.DeleteAsset(_folder);
            CollectionAssert.AreEqual(_prefabBefore, File.ReadAllBytes(ASMLiteAssetPaths.Prefab),
                "Instance wiring must never modify the shared source prefab.");
        }

        [Test]
        public void RetargetLinkedPrefab_PreservesLocalWiringThroughFixerAndPrefabReload()
        {
            var originalComponent = _component;
            Retarget();
            var vf = FindVrcFury();
            Assert.IsNull(PrefabUtility.GetCorrespondingObjectFromSource(vf),
                "Editable VRCFury feature data must belong to the instance, not be inherited overrides.");
            Assert.IsTrue(PrefabUtility.IsPartOfPrefabInstance(_instance),
                "Keep the ASM-Lite prefab connection; do not unpack the avatar or module.");
            Assert.AreSame(originalComponent, _component);
            Assert.IsNotNull(PrefabUtility.GetCorrespondingObjectFromSource(_component));
            Assert.AreEqual(0, GetOverrides(vf).Count);
            Retarget();
            Assert.AreSame(vf, FindVrcFury(), "Repeated retargeting must not replace an already local component.");
            Assert.AreEqual(1, CountVrcFury());
            RunPrefabFixer();
            AssertLocalReferences();

            // Round-trip the nested prefab representation without saving or replacing
            // the test runner's untitled scene.
            var savedPrefab = PrefabUtility.SaveAsPrefabAsset(_instance, _folder + "/Wiring.prefab");
            Assert.IsNotNull(savedPrefab);
            Object.DestroyImmediate(_instance);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(savedPrefab);
            _component = _instance.GetComponent<ASMLiteComponent>();
            Assert.AreEqual(1, CountVrcFury());
            Assert.AreEqual(0, GetOverrides(FindVrcFury()).Count);
            RunPrefabFixer();
            AssertLocalReferences();
        }

        [Test]
        public void ExistingFeatureOverrides_MigrationPreservesSettingsAndOtherComponents()
        {
            var oldVf = FindVrcFury();
            var so = new SerializedObject(oldVf);
            foreach (string path in new[] { ASMLiteDriftProbe.ControllerObjectRefPath, ASMLiteDriftProbe.ControllerMirrorPath })
                so.FindProperty(path).objectReferenceValue = _controller;
            foreach (string path in new[] { ASMLiteDriftProbe.MenuObjectRefPath, ASMLiteDriftProbe.MenuMirrorPath })
                so.FindProperty(path).objectReferenceValue = _menu;
            foreach (string path in new[] { ASMLiteDriftProbe.ParametersObjectRefPath, ASMLiteDriftProbe.ParametersMirrorPath })
                so.FindProperty(path).objectReferenceValue = _parameters;
            so.FindProperty("content.toggleParam").stringValue = "PreserveAuthoredFeatureSetting";
            so.ApplyModifiedPropertiesWithoutUndo();
            oldVf.enabled = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(oldVf);
            Assert.GreaterOrEqual(GetOverrides(oldVf).Count, 6);
            _component.useVendorizedGeneratedAssets = true;
            _component.vendorizedGeneratedAssetsPath = _folder;
            var transform = _instance.transform;
            var collider = _instance.AddComponent<BoxCollider>();
            collider.center = new Vector3(1, 2, 3);

            Retarget();
            var vf = FindVrcFury();
            Assert.IsFalse(vf.enabled, "Localizing feature data must preserve the component enabled state.");
            Assert.AreEqual("PreserveAuthoredFeatureSetting", new SerializedObject(vf).FindProperty("content.toggleParam").stringValue);
            Assert.IsTrue(_component.useVendorizedGeneratedAssets);
            Assert.AreEqual(_folder, _component.vendorizedGeneratedAssetsPath);
            Assert.AreSame(transform, _instance.transform);
            Assert.AreSame(collider, _instance.GetComponent<BoxCollider>());
            Assert.AreEqual(new Vector3(1, 2, 3), collider.center);
            Assert.AreEqual(1, CountVrcFury());
            Assert.AreEqual(0, GetOverrides(vf).Count);
            RunPrefabFixer();
            AssertLocalReferences();
            Assert.IsFalse(vf.enabled);
            Assert.AreEqual("PreserveAuthoredFeatureSetting", new SerializedObject(vf).FindProperty("content.toggleParam").stringValue);
        }

        [Test]
        public void MissingRetargetAssets_LeavesInheritedComponentUntouched()
        {
            var vf = FindVrcFury();
            string before = EditorJsonUtility.ToJson(vf);
            var result = ASMLiteFullControllerWiring.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(
                _component, _folder + "/Missing", "Missing Assets Regression");
            Assert.IsFalse(result.Success);
            Assert.AreSame(vf, FindVrcFury());
            Assert.AreEqual(before, EditorJsonUtility.ToJson(vf));
            Assert.IsNotNull(PrefabUtility.GetCorrespondingObjectFromSource(vf));
            Assert.AreEqual(1, CountVrcFury());
        }

        [Test]
        public void UnreadablePrefix_FailedEditPreservesInheritedFeatureData()
        {
            var vf = FindVrcFury();
            var so = new SerializedObject(vf);
            so.FindProperty(ASMLiteDriftProbe.MenuArrayPath).arraySize = 0;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(vf);
            string before = EditorJsonUtility.ToJson(vf);
            var result = ASMLiteFullControllerWiring.TrySyncLiveMenuPrefixWithDiagnostics(_component);
            Assert.IsFalse(result.Success);
            Assert.AreSame(vf, FindVrcFury());
            Assert.AreEqual(before, EditorJsonUtility.ToJson(vf));
            Assert.IsNotNull(PrefabUtility.GetCorrespondingObjectFromSource(vf));
            Assert.AreEqual(1, CountVrcFury());
        }

        [Test]
        public void ClearingLegacyPrefix_PreservesOtherReferencesWithoutPrefabFeatureOverrides()
        {
            var so = new SerializedObject(FindVrcFury());
            so.FindProperty(ASMLiteDriftProbe.MenuPrefixPath).stringValue = "Stale/Prefix";
            so.FindProperty(ASMLiteDriftProbe.ParametersObjectRefPath).objectReferenceValue = _parameters;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.RecordPrefabInstancePropertyModifications(FindVrcFury());
            Assert.IsTrue(ASMLiteFullControllerWiring.TryClearLiveFullControllerMenuPrefixOverride(_component));
            Assert.AreEqual(0, GetOverrides(FindVrcFury()).Count);
            RunPrefabFixer();
            so = new SerializedObject(FindVrcFury());
            Assert.AreEqual(string.Empty, so.FindProperty(ASMLiteDriftProbe.MenuPrefixPath).stringValue);
            Assert.AreEqual(_parameters, so.FindProperty(ASMLiteDriftProbe.ParametersObjectRefPath).objectReferenceValue);
        }

        [Test]
        public void InstanceOwnedMigration_UndoAndRedoPreserveSingleFeatureComponent()
        {
            Retarget();
            Undo.PerformUndo();
            Assert.AreEqual(1, CountVrcFury());
            Assert.IsNotNull(PrefabUtility.GetCorrespondingObjectFromSource(FindVrcFury()));
            Undo.PerformRedo();
            Assert.AreEqual(1, CountVrcFury());
            Assert.AreEqual(0, GetOverrides(FindVrcFury()).Count);
            RunPrefabFixer();
            AssertLocalReferences();
        }

        private void Retarget()
        {
            var result = ASMLiteFullControllerWiring.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics(
                _component, _folder, "Prefab Override Regression");
            Assert.IsTrue(result.Success, result.ToLogString());
        }

        private MonoBehaviour FindVrcFury()
        {
            return _instance.GetComponents<MonoBehaviour>().Single(c => c != null && c.GetType().FullName == "VF.Model.VRCFury");
        }

        private int CountVrcFury()
        {
            return _instance.GetComponents<MonoBehaviour>().Count(c => c != null && c.GetType().FullName == "VF.Model.VRCFury");
        }

        private void AssertLocalReferences()
        {
            var so = new SerializedObject(FindVrcFury());
            Assert.AreEqual(_controller, so.FindProperty(ASMLiteDriftProbe.ControllerObjectRefPath).objectReferenceValue);
            Assert.AreEqual(_menu, so.FindProperty(ASMLiteDriftProbe.MenuObjectRefPath).objectReferenceValue);
            Assert.AreEqual(_parameters, so.FindProperty(ASMLiteDriftProbe.ParametersObjectRefPath).objectReferenceValue);
            Assert.AreEqual(_controller, so.FindProperty(ASMLiteDriftProbe.ControllerMirrorPath).objectReferenceValue);
            Assert.AreEqual(_menu, so.FindProperty(ASMLiteDriftProbe.MenuMirrorPath).objectReferenceValue);
            Assert.AreEqual(_parameters, so.FindProperty(ASMLiteDriftProbe.ParametersMirrorPath).objectReferenceValue);
        }

        private static Type FindInstalledType(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).First(t => t != null);
        }

        private static System.Collections.ICollection GetOverrides(MonoBehaviour vf)
        {
            return (System.Collections.ICollection)FindInstalledType("VF.Builder.VRCFPrefabFixer")
                .GetMethod("GetModifications", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { vf });
        }

        private void RunPrefabFixer()
        {
            var wrapper = FindInstalledType("VF.Utils.VFGameObject");
            var convert = wrapper.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m =>
                m.Name == "op_Implicit" && m.ReturnType == wrapper && m.GetParameters()[0].ParameterType == typeof(GameObject));
            var objects = Array.CreateInstance(wrapper, 1);
            objects.SetValue(convert.Invoke(null, new object[] { _instance }), 0);
            FindInstalledType("VF.Builder.VRCFPrefabFixer").GetMethod("Fix", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { objects });
        }
    }
}
