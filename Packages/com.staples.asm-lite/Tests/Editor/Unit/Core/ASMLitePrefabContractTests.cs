using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLitePrefabContractTests
    {
        [Test]
        public void Prefab_UsesGeneratedAssetReferences_ForFullController()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);
            Assert.IsNotNull(prefab, "The shipped prefab must resolve without refreshing its wiring.");
            bool found = false;
            foreach (var vf in ASMLiteTestFixtures.FindLiveVrcFuryComponents(prefab))
            {
                var serialized = new SerializedObject(vf);
                var iterator = serialized.GetIterator();
                while (iterator.Next(true))
                {
                    if (iterator.propertyType != SerializedPropertyType.ManagedReference
                        || !iterator.managedReferenceFullTypename.EndsWith(" VF.Model.Feature.FullController", StringComparison.Ordinal))
                        continue;

                    found = true;
                    string path = iterator.propertyPath;
                    CollectionAssert.Contains(ASMLiteTestFixtures.ReadSerializedStringArray(vf, path + ".globalParams"), "*");
                    Assert.IsTrue(serialized.FindProperty(path + ".allNonsyncedAreGlobal").boolValue);
                    Assert.IsFalse(serialized.FindProperty(path + ".ignoreSaved").boolValue);
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(ASMLiteAssetPaths.FXController),
                        ASMLiteTestFixtures.ReadSerializedObjectReference(vf, path + ".controllers.Array.data[0].controller.objRef").Reference);
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu>(ASMLiteAssetPaths.Menu),
                        ASMLiteTestFixtures.ReadSerializedObjectReference(vf, path + ".menus.Array.data[0].menu.objRef").Reference);
                    Assert.AreSame(AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams),
                        ASMLiteTestFixtures.ReadSerializedObjectReference(vf, path + ".prms.Array.data[0].parameters.objRef").Reference);
                }
            }
            Assert.IsTrue(found, "The shipped prefab must contain a resolvable FullController payload.");
        }
    }
}
