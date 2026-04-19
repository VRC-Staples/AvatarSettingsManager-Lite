using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;
using ASMLite.Editor;

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
        public void SelectingAvatar_CopiesAndNormalizesCustomizationState()
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
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomRootIcon, "Selection sync should copy root icon toggle.");
                Assert.IsTrue(snapshot.UseCustomRootName, "Selection sync should copy root name toggle.");
                Assert.IsTrue(snapshot.UseCustomInstallPath, "Selection sync should copy install path toggle.");
                Assert.IsTrue(snapshot.UseParameterExclusions, "Selection sync should copy exclusion toggle.");

                Assert.AreEqual("Root Menu", snapshot.CustomRootName, "Selection sync should trim root name values.");
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath, "Blank install paths should normalize to empty.");
                CollectionAssert.AreEqual(new[] { "HatVisible", "BodyHue" }, snapshot.ExcludedParameterNames,
                    "Selection sync should trim, de-dup, and drop blank exclusions.");
                Assert.IsNotNull(snapshot.CustomIcons, "Selection sync should normalize null icon arrays to a safe empty array.");
                Assert.AreEqual(0, snapshot.CustomIcons.Length, "Selection sync should clear stale icon arrays when component has null icons.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingAvatar_UsesLiveInstallPathValueInVisibleCustomizationSnapshot()
        {
            _ctx.Comp.customInstallPath = "Tools/Live";

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var snapshot = window.GetPendingCustomizationSnapshotForTesting();

                Assert.AreEqual("Tools/Live", snapshot.CustomInstallPath,
                    "Visible customization state should reflect the live install path after avatar selection.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingAvatar_NormalizesBlankLiveInstallPathInVisibleCustomizationSnapshot()
        {
            _ctx.Comp.customInstallPath = "   ";

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var snapshot = window.GetPendingCustomizationSnapshotForTesting();

                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath,
                    "Visible customization state should normalize blank serialized install paths to an empty string.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RetargetLiveFullControllerGeneratedAssets_RestoresPresetParameterEnrollment()
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"Build should succeed before retargeting live FullController assets. result={buildResult}.");

            Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "Scaffold Retarget Setup"),
                "Setup should create a live VF.Model.VRCFury FullController payload before retarget validation.");

            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf,
                "Setup should leave a VF.Model.VRCFury component on the ASM-Lite object.");

            var beforeSo = new SerializedObject(vf);
            beforeSo.Update();
            var beforeControllers = beforeSo.FindProperty("content.controllers");
            Assert.IsNotNull(beforeControllers,
                "Expected serialized FullController controller registration array at content.controllers before retarget validation.");
            var beforeMenus = beforeSo.FindProperty("content.menus");
            Assert.IsNotNull(beforeMenus,
                "Expected serialized FullController menu registration array at content.menus before retarget validation.");
            var beforePrms = beforeSo.FindProperty("content.prms");
            Assert.IsNotNull(beforePrms,
                "Expected serialized FullController parameter registration array at content.prms before retarget validation.");
            var beforeGlobalParams = beforeSo.FindProperty("content.globalParams");
            Assert.IsNotNull(beforeGlobalParams,
                "Expected serialized FullController globalParams array at content.globalParams before retarget validation.");
            beforeControllers.arraySize = 0;
            beforeMenus.arraySize = 0;
            beforePrms.arraySize = 0;
            beforeGlobalParams.arraySize = 0;
            beforeSo.FindProperty("content.controller.objRef").objectReferenceValue = null;
            beforeSo.FindProperty("content.menu.objRef").objectReferenceValue = null;
            beforeSo.FindProperty("content.parameters.objRef").objectReferenceValue = null;
            beforeSo.ApplyModifiedPropertiesWithoutUndo();

            bool retargeted = ASMLiteWindow.TryRetargetLiveFullControllerGeneratedAssetsForTesting(_ctx.Comp, ASMLiteAssetPaths.GeneratedDir);
            Assert.IsTrue(retargeted,
                "Retargeting should succeed for package-managed generated assets when the live FullController payload exists.");

            var expectedController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(expectedController,
                "Retarget validation requires the generated FX controller asset to exist.");
            var expectedMenu = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.Menu);
            Assert.IsNotNull(expectedMenu,
                "Retarget validation requires the generated menu asset to exist.");
            var expectedParameters = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(expectedParameters,
                "Retarget validation requires the generated expression-parameters asset to exist.");

            var afterSo = new SerializedObject(vf);
            afterSo.Update();
            var afterControllers = afterSo.FindProperty("content.controllers");
            Assert.IsNotNull(afterControllers,
                "Expected serialized FullController controller registration array at content.controllers after retarget validation.");
            Assert.AreEqual(1, afterControllers.arraySize,
                "Retargeting should restore FullController controller registration so slot action transitions keep resolving against the generated FX controller.");
            Assert.AreEqual(expectedController, afterSo.FindProperty("content.controllers.Array.data[0].controller.objRef")?.objectReferenceValue,
                "Retargeting should restore the generated FX controller into content.controllers[0].controller.objRef.");

            var afterMenus = afterSo.FindProperty("content.menus");
            Assert.IsNotNull(afterMenus,
                "Expected serialized FullController menu registration array at content.menus after retarget validation.");
            Assert.AreEqual(1, afterMenus.arraySize,
                "Retargeting should restore FullController menu registration so preset actions use the generated menu tree.");
            Assert.AreEqual(expectedMenu, afterSo.FindProperty("content.menus.Array.data[0].menu.objRef")?.objectReferenceValue,
                "Retargeting should restore the generated menu asset into content.menus[0].menu.objRef.");

            var afterPrms = afterSo.FindProperty("content.prms");
            Assert.IsNotNull(afterPrms,
                "Expected serialized FullController parameter registration array at content.prms after retarget validation.");
            Assert.AreEqual(1, afterPrms.arraySize,
                "Retargeting should restore FullController parameter registration so preset action buttons keep resolving ASMLite_Ctrl and backup parameter bindings.");
            Assert.AreEqual(expectedParameters, afterSo.FindProperty("content.prms.Array.data[0].parameters.objRef")?.objectReferenceValue,
                "Retargeting should restore the generated expression-parameters asset into content.prms[0] so Save/Load/Clear menu actions keep their merged parameter registration.");

            var afterGlobalParams = afterSo.FindProperty("content.globalParams");
            Assert.IsNotNull(afterGlobalParams,
                "Expected serialized FullController globalParams array at content.globalParams after retarget validation.");
            Assert.AreEqual(1, afterGlobalParams.arraySize,
                "Retargeting should restore wildcard global-parameter enrollment so menu button triggers continue resolving against the generated FX controller.");
            Assert.AreEqual("*", afterSo.FindProperty("content.globalParams.Array.data[0]")?.stringValue,
                "Retargeting should restore the wildcard global-parameter registration consumed by VRCFury FullController merges.");
        }

        [Test]
        public void Build_AutoHealsMissingFullControllerParameterEnrollment_BeforeGeneratingAssets()
        {
            int initialBuildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(initialBuildResult, 0,
                $"Initial build should succeed before simulating stale FullController parameter wiring. result={initialBuildResult}.");

            Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "Build Auto-Heal Setup"),
                "Setup should create a live VF.Model.VRCFury FullController payload before simulating stale parameter wiring.");

            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf,
                "Setup should leave a VF.Model.VRCFury component on the ASM-Lite object.");

            var beforeSo = new SerializedObject(vf);
            beforeSo.Update();
            beforeSo.FindProperty("content.prms").arraySize = 0;
            beforeSo.FindProperty("content.globalParams").arraySize = 0;
            beforeSo.FindProperty("content.parameters.objRef").objectReferenceValue = null;
            beforeSo.ApplyModifiedPropertiesWithoutUndo();

            int healedBuildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(healedBuildResult, 0,
                $"Build should auto-heal stale FullController parameter wiring before generating assets. result={healedBuildResult}.");

            var expectedParameters = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(expectedParameters,
                "Auto-heal validation requires the generated expression-parameters asset to exist.");

            var afterSo = new SerializedObject(vf);
            afterSo.Update();
            var afterPrms = afterSo.FindProperty("content.prms");
            Assert.IsNotNull(afterPrms,
                "Expected serialized FullController parameter registration array at content.prms after Build() auto-heal.");
            Assert.AreEqual(1, afterPrms.arraySize,
                "Build() auto-heal should restore FullController parameter registration so VRCFury menu merges keep resolving ASMLite_Ctrl.");
            Assert.AreEqual(expectedParameters, afterSo.FindProperty("content.prms.Array.data[0].parameters.objRef")?.objectReferenceValue,
                "Build() auto-heal should restore the generated expression-parameters asset into content.prms[0].parameters.objRef.");

            var afterGlobalParams = afterSo.FindProperty("content.globalParams");
            Assert.IsNotNull(afterGlobalParams,
                "Expected serialized FullController globalParams array at content.globalParams after Build() auto-heal.");
            Assert.AreEqual(1, afterGlobalParams.arraySize,
                "Build() auto-heal should restore wildcard global-parameter enrollment before VRCFury FullController merges.");
            Assert.AreEqual("*", afterSo.FindProperty("content.globalParams.Array.data[0]")?.stringValue,
                "Build() auto-heal should restore wildcard global-parameter registration consumed by VRCFury FullController merges.");

            Assert.AreEqual(expectedParameters, afterSo.FindProperty("content.parameters.objRef")?.objectReferenceValue,
                "Build() auto-heal should also restore the top-level FullController parameters mirror for compatibility.");
        }

        [Test]
        public void RetargetLiveFullControllerGeneratedAssetsForTesting_FailsWhenRequiredControllerWiringIsMissing()
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"Build should succeed before validating retarget failure behavior. result={buildResult}.");

            Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "Scaffold Retarget Missing Wiring Setup"),
                "Setup should create a live VF.Model.VRCFury FullController payload before retarget failure validation.");

            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            Assert.IsNotNull(vf,
                "Setup should leave a live VF.Model.VRCFury component on the ASM-Lite object.");

            var so = new SerializedObject(vf);
            so.Update();
            so.FindProperty("content.controllers").arraySize = 0;
            so.FindProperty("content.menus").arraySize = 0;
            so.FindProperty("content.prms").arraySize = 0;
            so.FindProperty("content.globalParams").arraySize = 0;
            so.FindProperty("content.controller.objRef").objectReferenceValue = null;
            so.FindProperty("content.menu.objRef").objectReferenceValue = null;
            so.FindProperty("content.parameters.objRef").objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            var missingControllerAssetPath = ASMLiteAssetPaths.FXController + ".bak-test";
            AssetDatabase.MoveAsset(ASMLiteAssetPaths.FXController, missingControllerAssetPath);
            AssetDatabase.Refresh();
            try
            {
                LogAssert.Expect(LogType.Error,
                    $"[ASM-Lite] Retarget Generated Assets Generated FX Repair: Generated FX controller file was not found at '{ASMLiteAssetPaths.FXController}'.");

                bool retargeted = ASMLiteWindow.TryRetargetLiveFullControllerGeneratedAssetsForTesting(_ctx.Comp, ASMLiteAssetPaths.GeneratedDir);
                Assert.IsFalse(retargeted,
                    "Retargeting should fail closed when a required generated FullController asset is missing, instead of silently reporting success with partial preset-action wiring.");
            }
            finally
            {
                AssetDatabase.MoveAsset(missingControllerAssetPath, ASMLiteAssetPaths.FXController);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void Build_RepairsGeneratedFxController_WhenDanglingLocalFileIdsExist()
        {
            int baselineBuildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(baselineBuildResult, 0,
                $"Baseline build should succeed before simulating generated FX corruption. result={baselineBuildResult}.");

            string controllerPath = ASMLiteAssetPaths.FXController;
            string fullPath = Path.GetFullPath(controllerPath);
            Assert.IsTrue(File.Exists(fullPath),
                $"Generated FX controller must exist before corruption simulation at '{fullPath}'.");

            string originalText = File.ReadAllText(fullPath);
            int corruptionMarker = originalText.IndexOf("m_DefaultState: ", StringComparison.Ordinal);
            Assert.GreaterOrEqual(corruptionMarker, 0,
                "Corruption simulation expected to find an AnimatorStateMachine default-state reference to replace.");

            const string brokenDefaultStateLine = "m_DefaultState: {fileID: 5092237740239409533}";
            string corruptedText = originalText.Remove(corruptionMarker, originalText.IndexOf('\n', corruptionMarker) - corruptionMarker)
                .Insert(corruptionMarker, brokenDefaultStateLine);

            try
            {
                File.WriteAllText(fullPath, corruptedText);

                string corruptedOnDisk = File.ReadAllText(fullPath);
                Assert.That(CountDanglingLocalFileIdsForTest(corruptedOnDisk), Is.GreaterThan(0),
                    "Corruption setup should leave at least one dangling local fileID reference in the generated FX controller text.");

                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"^\[ASM-Lite\] Build: Detected \d+ dangling local fileID reference\(s\) in generated FX controller\. Clearing stale controller topology before rebuild\.$"));

                int repairedBuildResult = ASMLiteBuilder.Build(_ctx.Comp);
                Assert.GreaterOrEqual(repairedBuildResult, 0,
                    $"Build should auto-repair dangling generated FX controller references instead of failing. result={repairedBuildResult}.");

                string repairedText = File.ReadAllText(fullPath);
                Assert.AreEqual(0, CountDanglingLocalFileIdsForTest(repairedText),
                    "After repair and rebuild, the generated FX controller text should not contain dangling local fileID references.");
            }
            finally
            {
                File.WriteAllText(fullPath, originalText);
                AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void RetargetLiveFullControllerGeneratedAssets_RepairsCorruptedGeneratedControllerBeforeRebinding()
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"Build should succeed before retarget corruption validation. result={buildResult}.");

            Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "Retarget Corruption Setup"),
                "Setup should create a live VF.Model.VRCFury FullController payload before retarget corruption validation.");

            string controllerPath = ASMLiteAssetPaths.FXController;
            string fullPath = Path.GetFullPath(controllerPath);
            Assert.IsTrue(File.Exists(fullPath),
                $"Generated FX controller must exist before corruption simulation at '{fullPath}'.");

            string originalText = File.ReadAllText(fullPath);
            int corruptionMarker = originalText.IndexOf("m_DefaultState: ", StringComparison.Ordinal);
            Assert.GreaterOrEqual(corruptionMarker, 0,
                "Corruption simulation expected to find an AnimatorStateMachine default-state reference to replace.");

            const string brokenDefaultStateLine = "m_DefaultState: {fileID: 2225763566423020120}";
            string corruptedText = originalText.Remove(corruptionMarker, originalText.IndexOf('\n', corruptionMarker) - corruptionMarker)
                .Insert(corruptionMarker, brokenDefaultStateLine);

            try
            {
                File.WriteAllText(fullPath, corruptedText);

                Assert.That(CountDanglingLocalFileIdsForTest(File.ReadAllText(fullPath)), Is.GreaterThan(0),
                    "Corruption setup should leave a dangling local fileID reference before retarget recovery runs.");

                LogAssert.Expect(LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"^\[ASM-Lite\] Retarget Generated Assets Generated FX Repair: Detected \d+ dangling local fileID reference\(s\) in generated FX controller\. Clearing stale controller topology before rebuild\.$"));

                bool retargeted = ASMLiteWindow.TryRetargetLiveFullControllerGeneratedAssetsForTesting(_ctx.Comp, ASMLiteAssetPaths.GeneratedDir);
                Assert.IsTrue(retargeted,
                    "Retargeting should repair the corrupted generated FX controller before reapplying live FullController references.");

                Assert.AreEqual(0, CountDanglingLocalFileIdsForTest(File.ReadAllText(fullPath)),
                    "Retarget-driven repair should clear dangling local fileID references from the generated FX controller text.");
            }
            finally
            {
                File.WriteAllText(fullPath, originalText);
                AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void SelectingInstallPath_UpdatesSerializedAndVisibleCustomizationState()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = string.Empty;

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SelectInstallPathForAutomation("Tools/Selected");

                Assert.AreEqual("Tools/Selected", _ctx.Comp.customInstallPath,
                    "Picking an install path should commit the selected path onto the live component.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Picking an install path should keep custom install mode enabled in visible customization state.");
                Assert.AreEqual("Tools/Selected", snapshot.CustomInstallPath,
                    "Picking an install path should immediately update the visible customization snapshot.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }


        [Test]
        public void SelectingInstallPath_RootSelectionKeepsCustomizationEnabledAndClearsSerializedAndVisiblePath()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/PreviouslySelected";

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SelectInstallPathForAutomation(string.Empty);

                Assert.IsTrue(_ctx.Comp.useCustomInstallPath,
                    "Choosing the install-tree root should keep custom install mode enabled so the avatar root remains an explicit custom target.");
                Assert.AreEqual(string.Empty, _ctx.Comp.customInstallPath,
                    "Choosing the install-tree root should clear the serialized custom install path.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Choosing the install-tree root should keep visible customization state in custom install mode.");
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath,
                    "Choosing the install-tree root should clear the visible install-path draft.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingInstallPath_WithoutComponent_PreservesPendingCustomInstallModeAcrossRootSelection()
        {
            UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SelectInstallPathForAutomation("Tools/PreviouslySelected");
                window.SelectInstallPathForAutomation(string.Empty);

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Choosing install paths before ASM-Lite is attached should preserve the pending custom-install toggle.");
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath,
                    "Choosing the install-tree root before ASM-Lite is attached should clear the pending install-path draft.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void RemovePrefab_RemovesAsmLiteVrcFuryArtifactsFromAvatar()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                ASMLiteTestFixtures.EnsureLiveFullControllerPayload(_ctx.Comp);

                var routingRoot = new GameObject("ASM-Lite Install Path Routing");
                routingRoot.transform.SetParent(_ctx.AvatarGo.transform, false);
                var routingVf = routingRoot.AddComponent<VF.Model.VRCFury>();
                routingVf.content = new VF.Model.Feature.MoveMenuItem
                {
                    fromPath = "Settings Manager",
                    toPath = "Tools/Settings Manager",
                };

                Assert.AreEqual(2, _ctx.AvatarGo.GetComponentsInChildren<VF.Model.VRCFury>(true).Length,
                    "Removal regression setup should include both the live FullController VRCFury component and the install-path routing helper.");

                Assert.DoesNotThrow(() => window.RemovePrefabForAutomation(),
                    "RemovePrefab should safely remove ASM-Lite and any VRCFury helper artifacts without surfacing editor exceptions during teardown.");

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "RemovePrefab should remove the ASM-Lite component hierarchy from the avatar.");
                Assert.IsNull(_ctx.AvatarGo.transform.Find("ASM-Lite Install Path Routing"),
                    "RemovePrefab should also remove the install-path routing helper object so no orphaned VRCFury helper remains on the avatar.");
                Assert.AreEqual(0, _ctx.AvatarGo.GetComponentsInChildren<VF.Model.VRCFury>(true).Length,
                    "RemovePrefab should leave no ASM-Lite-owned VRCFury components behind after teardown.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void AddPrefab_UsesSelectedAvatarCustomizationSnapshot_WithoutArrayAliasing()
        {
            var sourceIcons = new Texture2D[] { null, null, null, null };
            var sourceExclusions = new[] { "ParamA", "", " ParamB ", "ParamA" };

            _ctx.Comp.slotCount = 4;
            _ctx.Comp.iconMode = IconMode.SameColor;
            _ctx.Comp.selectedGearIndex = 5;
            _ctx.Comp.actionIconMode = ActionIconMode.Custom;
            _ctx.Comp.customIcons = sourceIcons;
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Custom Root  ";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = null;
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = sourceExclusions;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                var snapshotBeforeAdd = window.GetPendingCustomizationSnapshotForTesting();

                if (_ctx.Comp != null)
                    UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);

                window.AddPrefabForAutomation();

                var component = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(component, "Add prefab should create an ASMLiteComponent under the selected avatar.");

                Assert.AreEqual(4, component.slotCount, "Selected avatar slotCount should copy into the newly added prefab.");
                Assert.AreEqual(IconMode.SameColor, component.iconMode, "Selected avatar icon mode should copy into the newly added prefab.");
                Assert.AreEqual(5, component.selectedGearIndex, "Selected avatar gear index should copy into the newly added prefab.");
                Assert.AreEqual(ActionIconMode.Custom, component.actionIconMode, "Selected avatar action icon mode should copy into the newly added prefab.");

                Assert.IsTrue(component.useCustomRootIcon, "Selected avatar root icon toggle should copy into the newly added prefab.");
                Assert.IsTrue(component.useCustomRootName, "Selected avatar root name toggle should copy into the newly added prefab.");
                Assert.IsTrue(component.useCustomInstallPath, "Selected avatar install path toggle should copy into the newly added prefab.");
                Assert.IsTrue(component.useParameterExclusions, "Selected avatar exclusion toggle should copy into the newly added prefab.");

                Assert.AreEqual("Custom Root", component.customRootName, "Selected avatar root name should be trimmed before serialization.");
                Assert.AreEqual(string.Empty, component.customInstallPath, "Null install path should normalize to empty before serialization.");
                CollectionAssert.AreEqual(new[] { "ParamA", "ParamB" }, component.excludedParameterNames,
                    "Selected avatar exclusions should be trimmed, de-duplicated, and scrubbed of blanks before serialization.");

                var wiredVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(component.gameObject);
                Assert.IsNotNull(wiredVf, "Add prefab should keep a live VF.Model.VRCFury component for bake wiring.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(wiredVf),
                    "Add prefab + immediate bake should normalize null install path to legacy empty FullController prefix.");

                Assert.AreNotSame(snapshotBeforeAdd.CustomIcons, component.customIcons,
                    "New prefab should receive its own icon array instead of reusing the selected-avatar customization snapshot array.");
                Assert.AreNotSame(snapshotBeforeAdd.ExcludedParameterNames, component.excludedParameterNames,
                    "New prefab should receive its own exclusion array instead of reusing the selected-avatar customization snapshot array.");

                snapshotBeforeAdd.CustomIcons[0] = Texture2D.blackTexture;
                snapshotBeforeAdd.ExcludedParameterNames[0] = "MutatedPending";

                Assert.IsNull(component.customIcons[0],
                    "Mutating the previously captured customization snapshot must not mutate serialized icon data on the newly added prefab.");
                CollectionAssert.AreEqual(new[] { "ParamA", "ParamB" }, component.excludedParameterNames,
                    "Mutating the previously captured customization snapshot must not mutate serialized exclusion data on the newly added prefab.");
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
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();

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

                var rebuiltVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(rebuilt.gameObject);
                Assert.IsNotNull(rebuiltVf, "Migration rebuild should preserve VF.Model.VRCFury delivery component.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(rebuiltVf),
                    "Migration rebuild should apply normalized blank install path as legacy empty FullController prefix.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void BakeAssets_RewritesLivePrefixDeterministically_WhenCustomizationFlipsEnabledDisabledAndBlank()
        {
            var vf = EnsureLiveFullControllerPayload(_ctx.Comp);

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "  Avatars/Primary  ";
                window.RebuildForAutomation();
                Assert.AreEqual("Avatars/Primary", ReadSerializedMenuPrefix(vf),
                    "Enabled custom install path should serialize trimmed FullController prefix on live bake.");

                _ctx.Comp.useCustomInstallPath = false;
                _ctx.Comp.customInstallPath = "Avatars/ShouldNotPersist";
                window.RebuildForAutomation();
                Assert.AreEqual(string.Empty, ReadSerializedMenuPrefix(vf),
                    "Disabled custom install path should reset FullController prefix to legacy empty value.");

                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "   ";
                window.RebuildForAutomation();
                Assert.AreEqual(string.Empty, ReadSerializedMenuPrefix(vf),
                    "Enabled whitespace-only custom install path should collapse to legacy empty FullController prefix.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void BakeAssets_MissingVrcFury_AutoHealsAndContinues()
        {
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "  Avatars/MissingVf  ";
            var staleVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
            if (staleVf != null)
                UnityEngine.Object.DestroyImmediate(staleVf);

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                LogAssert.Expect(LogType.Warning,
                    $"[ASM-Lite] Bake: VF.Model.VRCFury component was missing on '{_ctx.Comp.gameObject.name}'. Live FullController wiring was repaired automatically.");

                window.RebuildForAutomation();

                var repairedVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp.gameObject);
                Assert.IsNotNull(repairedVf,
                    "Bake should auto-heal missing VF.Model.VRCFury before refreshing install-path routing.");
                Assert.AreEqual("Avatars/MissingVf", ASMLiteTestFixtures.ReadSerializedMenuPrefix(repairedVf),
                    "Bake auto-heal should still apply normalized install-path prefix deterministically.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        private static int CountDanglingLocalFileIdsForTest(string controllerText)
        {
            Assert.IsNotNull(controllerText, "Controller text should not be null when scanning for dangling file IDs.");

            var definedIds = System.Text.RegularExpressions.Regex.Matches(controllerText, @"^--- !u!\d+ &(-?\d+)$", System.Text.RegularExpressions.RegexOptions.Multiline)
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value)
                .ToHashSet();

            int danglingCount = 0;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(controllerText, @"\{fileID: (-?\d+)\}"))
            {
                string fileId = match.Groups[1].Value;
                if (fileId == "0" || fileId == "9100000")
                    continue;
                if (definedIds.Contains(fileId))
                    continue;

                danglingCount++;
            }

            return danglingCount;
        }

        private static VF.Model.VRCFury EnsureLiveFullControllerPayload(ASMLiteComponent component)
        {
            var vf = component.GetComponent<VF.Model.VRCFury>();
            if (vf == null)
                vf = component.gameObject.AddComponent<VF.Model.VRCFury>();

            vf.content = new VF.Model.Feature.FullController
            {
                menus = new[]
                {
                    new VF.Model.Feature.MenuEntry()
                }
            };

            return vf;
        }

        private static string ReadSerializedMenuPrefix(VF.Model.VRCFury vf)
        {
            var serializedVf = new SerializedObject(vf);
            serializedVf.Update();

            var prefixProperty = serializedVf.FindProperty("content.menus.Array.data[0].prefix");
            Assert.IsNotNull(prefixProperty,
                "Expected serialized FullController menu prefix field at content.menus.Array.data[0].prefix.");

            return prefixProperty.stringValue;
        }
    }
}
