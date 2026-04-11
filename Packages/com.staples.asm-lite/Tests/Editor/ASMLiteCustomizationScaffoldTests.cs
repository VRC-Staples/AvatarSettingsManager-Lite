using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteCustomizationScaffoldTests
    {
        private AsmLiteTestContext _ctx;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
        }

        [TearDown]
        public void TearDown()
        {
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            _ctx = null;
        }

        [Test]
        public void ScaffoldDefaults_AreDisabledOrEmpty()
        {
            var go = new GameObject("ScaffoldDefaults");
            try
            {
                var component = go.AddComponent<ASMLiteComponent>();

                Assert.IsFalse(component.useCustomRootIcon, "useCustomRootIcon should default to false.");
                Assert.IsNull(component.customRootIcon, "customRootIcon should default to null.");

                Assert.IsFalse(component.useCustomRootName, "useCustomRootName should default to false.");
                Assert.AreEqual(string.Empty, component.customRootName, "customRootName should default to empty string.");

                Assert.IsFalse(component.useCustomInstallPath, "useCustomInstallPath should default to false.");
                Assert.AreEqual(string.Empty, component.customInstallPath, "customInstallPath should default to empty string.");

                Assert.IsFalse(component.useParameterExclusions, "useParameterExclusions should default to false.");
                Assert.IsNotNull(component.excludedParameterNames, "excludedParameterNames should not be null.");
                Assert.AreEqual(0, component.excludedParameterNames.Length, "excludedParameterNames should default to empty array.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void SelectionSync_CopiesAndNormalizesCustomizationState()
        {
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Root Menu  ";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "   ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { " HatVisible ", "", null, "HatVisible", "BodyHue" };
            _ctx.Comp.customIcons = null;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);
                SetPrivateField(window, "_pendingCustomRootName", "stale-value");
                SetPrivateField(window, "_pendingCustomInstallPath", "stale-path");
                SetPrivateField(window, "_pendingExcludedParameterNames", new[] { "stale" });

                InvokePrivate(window, "SyncPendingSlotCountFromAvatar");

                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomRootIcon"), "Selection sync should copy root icon toggle.");
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomRootName"), "Selection sync should copy root name toggle.");
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomInstallPath"), "Selection sync should copy install path toggle.");
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseParameterExclusions"), "Selection sync should copy exclusion toggle.");

                Assert.AreEqual("Root Menu", GetPrivateField<string>(window, "_pendingCustomRootName"), "Selection sync should trim root name values.");
                Assert.AreEqual(string.Empty, GetPrivateField<string>(window, "_pendingCustomInstallPath"), "Blank install paths should normalize to empty.");

                var pendingExcluded = GetPrivateField<string[]>(window, "_pendingExcludedParameterNames");
                CollectionAssert.AreEqual(new[] { "HatVisible", "BodyHue" }, pendingExcluded,
                    "Selection sync should trim, de-dup, and drop blank exclusions.");

                var pendingIcons = GetPrivateField<Texture2D[]>(window, "_pendingCustomIcons");
                Assert.IsNotNull(pendingIcons, "Selection sync should normalize null icon arrays to a safe empty array.");
                Assert.AreEqual(0, pendingIcons.Length, "Selection sync should clear stale icon arrays when component has null icons.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AddPrefab_UsesPendingCustomizationState_WithoutArrayAliasing()
        {
            if (_ctx.Comp != null)
                UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);
                SetPrivateField(window, "_pendingSlotCount", 4);
                SetPrivateField(window, "_pendingIconMode", IconMode.SameColor);
                SetPrivateField(window, "_pendingSelectedGearIndex", 5);
                SetPrivateField(window, "_pendingActionIconMode", ActionIconMode.Custom);
                SetPrivateField(window, "_pendingCustomSaveIcon", null);
                SetPrivateField(window, "_pendingCustomLoadIcon", null);
                SetPrivateField(window, "_pendingCustomClearIcon", null);
                SetPrivateField(window, "_pendingCustomIcons", new Texture2D[] { null, null, null, null });

                SetPrivateField(window, "_pendingUseCustomRootIcon", true);
                SetPrivateField(window, "_pendingCustomRootIcon", null);
                SetPrivateField(window, "_pendingUseCustomRootName", true);
                SetPrivateField(window, "_pendingCustomRootName", "  Custom Root  ");
                SetPrivateField(window, "_pendingUseCustomInstallPath", true);
                SetPrivateField(window, "_pendingCustomInstallPath", null);
                SetPrivateField(window, "_pendingUseParameterExclusions", true);
                SetPrivateField(window, "_pendingExcludedParameterNames", new[] { "ParamA", "", " ParamB ", "ParamA" });

                InvokePrivate(window, "AddPrefabToAvatar");

                var component = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(component, "Add prefab should create an ASMLiteComponent under the selected avatar.");

                Assert.AreEqual(4, component.slotCount, "Pending slotCount should copy into newly added prefab.");
                Assert.AreEqual(IconMode.SameColor, component.iconMode, "Pending icon mode should copy into newly added prefab.");
                Assert.AreEqual(5, component.selectedGearIndex, "Pending gear index should copy into newly added prefab.");
                Assert.AreEqual(ActionIconMode.Custom, component.actionIconMode, "Pending action icon mode should copy into newly added prefab.");

                Assert.IsTrue(component.useCustomRootIcon, "Pending root icon toggle should copy into newly added prefab.");
                Assert.IsTrue(component.useCustomRootName, "Pending root name toggle should copy into newly added prefab.");
                Assert.IsTrue(component.useCustomInstallPath, "Pending install path toggle should copy into newly added prefab.");
                Assert.IsTrue(component.useParameterExclusions, "Pending exclusion toggle should copy into newly added prefab.");

                Assert.AreEqual("Custom Root", component.customRootName, "Pending root name should be trimmed before serialization.");
                Assert.AreEqual(string.Empty, component.customInstallPath, "Null install path should normalize to empty before serialization.");
                CollectionAssert.AreEqual(new[] { "ParamA", "ParamB" }, component.excludedParameterNames,
                    "Pending exclusions should be trimmed, de-duplicated, and scrubbed of blanks before serialization.");

                var pendingIcons = GetPrivateField<Texture2D[]>(window, "_pendingCustomIcons");
                Assert.AreNotSame(pendingIcons, component.customIcons,
                    "New prefab should receive a copy of pending icon array, not the same reference.");

                var pendingExclusions = GetPrivateField<string[]>(window, "_pendingExcludedParameterNames");
                Assert.AreNotSame(pendingExclusions, component.excludedParameterNames,
                    "New prefab should receive a copy of pending exclusions, not the same reference.");

                if (pendingExclusions.Length > 0)
                    pendingExclusions[0] = "MutatedPending";

                CollectionAssert.AreEqual(new[] { "ParamA", "ParamB" }, component.excludedParameterNames,
                    "Mutating pending exclusion state after add must not mutate serialized component data.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RebuildMigration_PreservesCustomizationState_WhenStalePrmsDetected()
        {
            _ctx.Comp.slotCount = 6;
            _ctx.Comp.iconMode = IconMode.Custom;
            _ctx.Comp.selectedGearIndex = 2;
            _ctx.Comp.actionIconMode = ActionIconMode.Custom;
            _ctx.Comp.customIcons = new Texture2D[] { null, null, null, null, null, null };
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Migrated Root  ";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = " ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "Mood", "", " Mood", "Hue " };

            var stale = new GameObject("prms");
            stale.transform.SetParent(_ctx.Comp.transform);

            int oldComponentInstanceId = _ctx.Comp.GetInstanceID();

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);
                InvokePrivate(window, "BakeAssets", _ctx.Comp);

                var rebuilt = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(rebuilt, "Migration rebuild should leave an ASMLiteComponent on the avatar.");
                Assert.AreNotEqual(oldComponentInstanceId, rebuilt.GetInstanceID(),
                    "Stale prms migration should replace the old component instance.");

                Assert.AreEqual(6, rebuilt.slotCount, "Migration rebuild should preserve slotCount.");
                Assert.AreEqual(IconMode.Custom, rebuilt.iconMode, "Migration rebuild should preserve icon mode.");
                Assert.AreEqual(2, rebuilt.selectedGearIndex, "Migration rebuild should preserve selected gear index.");
                Assert.AreEqual(ActionIconMode.Custom, rebuilt.actionIconMode, "Migration rebuild should preserve action icon mode.");

                Assert.IsTrue(rebuilt.useCustomRootIcon, "Migration rebuild should preserve root icon toggle.");
                Assert.IsTrue(rebuilt.useCustomRootName, "Migration rebuild should preserve root name toggle.");
                Assert.IsTrue(rebuilt.useCustomInstallPath, "Migration rebuild should preserve install path toggle.");
                Assert.IsTrue(rebuilt.useParameterExclusions, "Migration rebuild should preserve exclusions toggle.");

                Assert.AreEqual("Migrated Root", rebuilt.customRootName,
                    "Migration rebuild should normalize and preserve root name values.");
                Assert.AreEqual(string.Empty, rebuilt.customInstallPath,
                    "Migration rebuild should normalize blank install path values.");
                CollectionAssert.AreEqual(new[] { "Mood", "Hue" }, rebuilt.excludedParameterNames,
                    "Migration rebuild should preserve sanitized exclusion names.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field '{fieldName}' on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field '{fieldName}' on {target.GetType().Name}.");
            return (T)field.GetValue(target);
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Missing private method '{methodName}' on {target.GetType().Name}.");
            method.Invoke(target, args);
        }
    }
}
