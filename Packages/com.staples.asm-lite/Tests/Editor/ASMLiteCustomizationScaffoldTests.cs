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
                LogAssert.Expect(LogType.Error,
                    $"[ASM-Lite] BUILD-302: [ASM-Lite] Retarget Generated Assets: Generated FX controller repair failed before live FullController refresh. Context: '{ASMLiteAssetPaths.FXController}'. Remediation: Repair the generated FX controller before refreshing FullController wiring.");

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
        public void SelectingAvatar_AdoptsMatchingMoveMenuInstallPath_WithoutTouchingVendorizedSnapshotState()
        {
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = string.Empty;
            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = "  Assets/ASM-Lite/TestAvatar/GeneratedAssets  ";

            ASMLiteTestFixtures.EnsureLiveFullControllerPayload(_ctx.Comp);

            var matchingHelperGo = new GameObject("Legacy Move Menu Helper");
            matchingHelperGo.transform.SetParent(_ctx.AvatarGo.transform, false);
            var matchingHelper = matchingHelperGo.AddComponent<VF.Model.VRCFury>();
            matchingHelper.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Settings Manager",
                toPath = "Tools/Settings Manager",
            };

            var unrelatedHelperGo = new GameObject("Unrelated Move Menu Helper");
            unrelatedHelperGo.transform.SetParent(_ctx.AvatarGo.transform, false);
            var unrelatedHelper = unrelatedHelperGo.AddComponent<VF.Model.VRCFury>();
            unrelatedHelper.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Other Root",
                toPath = "Elsewhere/Other Root",
            };

            var managedRoutingGo = new GameObject("ASM-Lite Install Path Routing");
            managedRoutingGo.transform.SetParent(_ctx.AvatarGo.transform, false);
            var managedRouting = managedRoutingGo.AddComponent<VF.Model.VRCFury>();
            managedRouting.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Settings Manager",
                toPath = "Managed/Settings Manager",
            };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                Assert.AreEqual(4, _ctx.AvatarGo.GetComponentsInChildren<VF.Model.VRCFury>(true).Length,
                    "Adoption setup should include the live payload plus matching, unrelated, and managed routing helpers.");

                window.SelectAvatarForAutomation(_ctx.AvDesc);

                Assert.IsTrue(_ctx.Comp.useCustomInstallPath,
                    "Move-menu adoption should enable custom install mode on the live component.");
                Assert.AreEqual("Tools", _ctx.Comp.customInstallPath,
                    "Move-menu adoption should write the helper-normalized prefix back onto the live component.");
                Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets,
                    "Move-menu adoption must preserve vendorized mode on the live component.");
                Assert.AreEqual("Assets/ASM-Lite/TestAvatar/GeneratedAssets", _ctx.Comp.vendorizedGeneratedAssetsPath,
                    "Move-menu adoption must preserve the normalized vendorized payload path on the live component.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Selection sync should surface the adopted install path in the pending snapshot.");
                Assert.AreEqual("Tools", snapshot.CustomInstallPath,
                    "Selection sync should report the adopted helper-normalized install prefix.");
                Assert.IsTrue(snapshot.UseVendorizedGeneratedAssets,
                    "Selection sync should preserve vendorized mode in the pending snapshot.");
                Assert.AreEqual("Assets/ASM-Lite/TestAvatar/GeneratedAssets", snapshot.VendorizedGeneratedAssetsPath,
                    "Selection sync should preserve the normalized vendorized payload path in the pending snapshot.");

                Assert.IsFalse(matchingHelperGo.TryGetComponent<VF.Model.VRCFury>(out _),
                    "Selection sync should remove only the matching detached MoveMenuItem helper component after adoption.");
                Assert.IsNotNull(_ctx.AvatarGo.transform.Find("Unrelated Move Menu Helper"),
                    "Selection sync should preserve unrelated MoveMenuItem helpers when their fromPath does not match ASM-Lite's root.");
                Assert.IsNotNull(_ctx.AvatarGo.transform.Find("ASM-Lite Install Path Routing"),
                    "Selection sync should preserve the managed install-path routing helper rather than deleting ASM-Lite-owned routing state.");
                Assert.AreEqual(3, _ctx.AvatarGo.GetComponentsInChildren<VF.Model.VRCFury>(true).Length,
                    "Selection sync should remove exactly one matching detached MoveMenuItem helper.");
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
        public void AddPrefab_CopiesPendingSlotCountToAttachedSnapshotAndComponent()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                if (_ctx.Comp != null)
                    UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
                _ctx.Comp = null;

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SetSlotCountForAutomation(8);

                var pendingBeforeAdd = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.AreEqual(8, pendingBeforeAdd.SlotCount,
                    "The pending customization snapshot should report the automation-selected slot count before the prefab is attached.");

                window.AddPrefabForAutomation();

                var component = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(component,
                    "AddPrefab should attach an ASMLiteComponent before attached snapshot assertions run.");
                Assert.AreEqual(8, component.slotCount,
                    "AddPrefab should copy the pending slot count into the newly attached component.");

                var attachedAfterAdd = window.GetAttachedCustomizationSnapshotForAutomation();
                Assert.AreEqual(8, attachedAfterAdd.SlotCount,
                    "The attached customization snapshot should mirror the component slot count immediately after AddPrefab.");
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
            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = "  Assets/ASM-Lite/TestAvatar/GeneratedAssets  ";

            var stale = new GameObject("prms");
            stale.transform.SetParent(_ctx.Comp.transform);

            int oldComponentInstanceId = _ctx.Comp.GetInstanceID();

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();
                window.SelectAvatarForAutomation(_ctx.AvDesc);

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
                Assert.IsTrue(rebuilt.useVendorizedGeneratedAssets, "Migration rebuild should preserve vendorized mode.");

                Assert.AreEqual("Migrated Root", rebuilt.customRootName,
                    "Migration rebuild should normalize and preserve root name values.");
                Assert.AreEqual(string.Empty, rebuilt.customInstallPath,
                    "Migration rebuild should normalize blank install path values.");
                CollectionAssert.AreEqual(new[] { "Mood", "Hue" }, rebuilt.excludedParameterNames,
                    "Migration rebuild should preserve sanitized exclusion names.");
                string rebuiltVendorizedPath = rebuilt.vendorizedGeneratedAssetsPath;
                AssertCanonicalVendorizedGeneratedAssetsPath(rebuiltVendorizedPath,
                    "Migration rebuild should preserve the canonical vendorized payload path.");

                var rebuiltVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(rebuilt.gameObject);
                Assert.IsNotNull(rebuiltVf, "Migration rebuild should preserve VF.Model.VRCFury delivery component.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(rebuiltVf),
                    "Migration rebuild should apply normalized blank install path as legacy empty FullController prefix.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Selection sync after migration rebuild should keep the normalized install-path toggle in the shared snapshot.");
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath,
                    "Selection sync after migration rebuild should keep the normalized blank install path in the shared snapshot.");
                Assert.IsTrue(snapshot.UseVendorizedGeneratedAssets,
                    "Selection sync after migration rebuild should keep vendorized mode in the shared snapshot.");
                Assert.AreEqual(rebuiltVendorizedPath, snapshot.VendorizedGeneratedAssetsPath,
                    "Selection sync after migration rebuild should keep the canonical vendorized payload path aligned with the rebuilt component.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void VendorizeTransaction_LiveRetargetFailure_RollsBackPackageManagedRefsAndRouting()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                if (_ctx.Comp != null)
                    UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
                _ctx.Comp = null;

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.AddPrefabForAutomation();
                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(_ctx.Comp,
                    "Vendorize rollback setup should attach a prefab-managed ASM-Lite component before failure injection.");
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/Rollback";
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();

                AssertInstallPathRoutingHelper("Settings Manager", "Tools/Rollback/Settings Manager",
                    "Vendorize rollback setup should establish deterministic install-path routing before failure injection.");
                AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                    "Vendorize rollback setup should leave live FullController references on package-managed generated assets.");
                string baselineExprPath = AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/');
                string baselineMenuPath = AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/');
                int baselineFxIndex = FindFxLayerIndex();
                Assert.GreaterOrEqual(baselineFxIndex, 0,
                    "Vendorize rollback setup should leave an FX layer on the avatar before failure injection.");
                var baselineFxController = _ctx.AvDesc.baseAnimationLayers[baselineFxIndex].animatorController;

                using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.AfterLiveFullControllerRetarget))
                {
                    var result = ASMLiteLifecycleTransactionService.ExecuteAttachedVendorize(_ctx.Comp, _ctx.AvDesc);
                    Assert.IsFalse(result.Success,
                        "Vendorize should fail closed when failure injection triggers after live FullController retarget.");
                    Assert.AreEqual(ASMLiteLifecycleTransactionStage.Execute, result.FailedStage,
                        "Live FullController retarget failure should surface as an execute-stage transaction failure.");
                    Assert.IsTrue(result.RollbackAttempted,
                        "Vendorize should attempt rollback after live FullController retarget failure.");
                    Assert.IsTrue(result.RollbackSucceeded,
                        "Vendorize rollback should restore the package-managed baseline after live FullController retarget failure.");
                    Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.PackageManaged, result.RollbackState,
                        "Vendorize rollback state should resolve back to PackageManaged after live FullController retarget failure.");
                }

                Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets,
                    "Vendorize rollback should clear vendorized mode on the attached component after live retarget failure.");
                Assert.AreEqual(string.Empty, _ctx.Comp.vendorizedGeneratedAssetsPath,
                    "Vendorize rollback should clear the tracked vendorized generated-assets path after live retarget failure.");
                Assert.AreEqual(baselineExprPath,
                    AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/'),
                    "Vendorize rollback should restore avatar expression parameters back to their baseline package-managed/user-owned asset after live retarget failure.");
                Assert.AreEqual(baselineMenuPath,
                    AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/'),
                    "Vendorize rollback should restore avatar expressions menu back to its baseline package-managed/user-owned asset after live retarget failure.");

                int fxIndex = FindFxLayerIndex();
                Assert.GreaterOrEqual(fxIndex, 0,
                    "Vendorize rollback should preserve the avatar FX layer after live retarget failure.");
                Assert.AreEqual(baselineFxController, _ctx.AvDesc.baseAnimationLayers[fxIndex].animatorController,
                    "Vendorize rollback should preserve the avatar FX controller assignment that existed before vendorize failure injection.");
                AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                    "Vendorize rollback should restore live FullController references back to package-managed assets after live retarget failure.");
                Assert.AreEqual(string.Empty, ASMLiteTestFixtures.ReadSerializedMenuPrefix(EnsureLiveFullControllerPayload(_ctx.Comp)),
                    "Vendorize rollback should keep prefab-instance FullController prefix overrides cleared after live retarget failure.");
                Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.PackageManaged,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp),
                    "Vendorize rollback should resolve the attached avatar back to PackageManaged tool state.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void DetachTransaction_VerifyFailure_RollsBackAttachedCustomizationAndRouting()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                if (_ctx.Comp != null)
                    UnityEngine.Object.DestroyImmediate(_ctx.Comp.gameObject);
                _ctx.Comp = null;

                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.AddPrefabForAutomation();
                _ctx.Comp = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(_ctx.Comp,
                    "Detach rollback setup should attach a prefab-managed ASM-Lite component before failure injection.");
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/DetachRollback";
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();

                AssertInstallPathRoutingHelper("Settings Manager", "Tools/DetachRollback/Settings Manager",
                    "Detach rollback setup should establish deterministic install-path routing before failure injection.");
                AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                    "Detach rollback setup should leave live FullController references on package-managed generated assets.");
                string baselineExprPath = AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/');
                string baselineMenuPath = AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/');
                int baselineFxIndex = FindFxLayerIndex();
                Assert.GreaterOrEqual(baselineFxIndex, 0,
                    "Detach rollback setup should leave an FX layer on the avatar before failure injection.");
                var baselineFxController = _ctx.AvDesc.baseAnimationLayers[baselineFxIndex].animatorController;

                using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify))
                {
                    var result = ASMLiteLifecycleTransactionService.ExecuteDetachToDirectDelivery(_ctx.Comp, _ctx.AvDesc);
                    Assert.IsFalse(result.Success,
                        "Detach should fail closed when verify-stage failure is injected after direct-delivery content is applied.");
                    Assert.AreEqual(ASMLiteLifecycleTransactionStage.Verify, result.FailedStage,
                        "Detach verify-stage failure should surface as a verify-stage transaction failure.");
                    Assert.IsTrue(result.RollbackAttempted,
                        "Detach should attempt rollback after verify-stage failure.");
                    Assert.IsTrue(result.RollbackSucceeded,
                        "Detach rollback should restore the attached package-managed baseline after verify-stage failure.");
                    Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.PackageManaged, result.RollbackState,
                        "Detach rollback state should resolve back to PackageManaged after verify-stage failure.");
                    Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.NotInstalled, result.RecoveredState,
                        "Detach rollback should report the detached baseline state after attached package-managed recovery.");
                }

                var rolledBackComponent = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
                Assert.IsNotNull(rolledBackComponent,
                    "Detach rollback should keep ASM-Lite attached after verify-stage failure.");
                Assert.AreEqual(baselineExprPath,
                    AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/'),
                    "Detach rollback should restore avatar expression parameters back to the baseline asset after verify-stage failure.");
                Assert.AreEqual(baselineMenuPath,
                    AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/'),
                    "Detach rollback should restore avatar expressions menu back to the baseline asset after verify-stage failure.");

                int fxIndex = FindFxLayerIndex();
                Assert.GreaterOrEqual(fxIndex, 0,
                    "Detach rollback should preserve the avatar FX layer after verify-stage failure.");
                Assert.AreEqual(baselineFxController, _ctx.AvDesc.baseAnimationLayers[fxIndex].animatorController,
                    "Detach rollback should preserve the avatar FX controller assignment that existed before failure injection.");
                AssertLiveFullControllerReferencesUnderPrefix(ASMLiteAssetPaths.GeneratedDir,
                    "Detach rollback should restore live FullController references back to package-managed assets after verify-stage failure.");
                AssertInstallPathRoutingHelper("Settings Manager", "Tools/DetachRollback/Settings Manager",
                    "Detach rollback should preserve install-path routing after verify-stage failure.");
                Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.NotInstalled,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "Detach rollback should remove detached runtime markers before leaving the avatar attached again.");
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

        private static void AssertCanonicalVendorizedGeneratedAssetsPath(string vendorizedPath, string assertionMessage)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(vendorizedPath),
                assertionMessage + " Expected a populated vendorized generated-assets path.");

            string normalizedPath = vendorizedPath.Replace('\\', '/').Trim();
            Assert.AreEqual(normalizedPath, vendorizedPath,
                assertionMessage + " Expected the vendorized generated-assets path to already be normalized.");
            Assert.IsTrue(normalizedPath.StartsWith("Assets/ASM-Lite/TestAvatar", StringComparison.Ordinal),
                assertionMessage + " Expected the vendorized generated-assets path to stay under the TestAvatar vendorized root.");
            Assert.IsTrue(normalizedPath.EndsWith("/GeneratedAssets", StringComparison.Ordinal),
                assertionMessage + " Expected the vendorized generated-assets path to end with '/GeneratedAssets'.");
        }

        private int FindFxLayerIndex()
        {
            for (int i = 0; i < _ctx.AvDesc.baseAnimationLayers.Length; i++)
            {
                if (_ctx.AvDesc.baseAnimationLayers[i].type == VRC.SDK3.Avatars.Components.VRCAvatarDescriptor.AnimLayerType.FX)
                    return i;
            }

            return -1;
        }

        private void AssertInstallPathRoutingHelper(string expectedFromPath, string expectedToPath, string assertionMessage)
        {
            var routingTransform = _ctx.AvDesc != null ? _ctx.AvDesc.transform.Find("ASM-Lite Install Path Routing") : null;
            Assert.IsNotNull(routingTransform,
                assertionMessage + " Expected the ASM-Lite install-path routing helper object to exist on the avatar.");

            var routingVf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(routingTransform.gameObject);
            Assert.IsNotNull(routingVf,
                assertionMessage + " Expected the routing helper object to carry a VF.Model.VRCFury component.");

            var serializedRouting = new SerializedObject(routingVf);
            serializedRouting.Update();
            var fromPathProperty = serializedRouting.FindProperty("content.fromPath");
            var toPathProperty = serializedRouting.FindProperty("content.toPath");
            Assert.IsNotNull(fromPathProperty,
                assertionMessage + " Expected MoveMenuItem fromPath to be serialized on the routing helper.");
            Assert.IsNotNull(toPathProperty,
                assertionMessage + " Expected MoveMenuItem toPath to be serialized on the routing helper.");
            Assert.AreEqual(expectedFromPath, fromPathProperty.stringValue,
                assertionMessage + " Unexpected routing helper source path.");
            Assert.AreEqual(expectedToPath, toPathProperty.stringValue,
                assertionMessage + " Unexpected routing helper destination path.");
        }

        private void AssertLiveFullControllerReferencesUnderPrefix(string expectedPrefix, string assertionMessage)
        {
            var vf = ASMLiteTestFixtures.FindLiveVrcFuryComponent(_ctx.Comp != null ? _ctx.Comp.gameObject : null);
            Assert.IsNotNull(vf,
                assertionMessage + " Expected a live VF.Model.VRCFury component on the ASM-Lite object.");

            string normalizedPrefix = expectedPrefix.Replace('\\', '/').TrimEnd('/');
            var controllerReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.ControllerObjectRefPath);
            var menuReference = ASMLiteTestFixtures.ReadSerializedObjectReference(vf, ASMLiteDriftProbe.MenuObjectRefPath);
            var parametersReference = ASMLiteTestFixtures.ReadSerializedObjectReferenceFromAnyPath(
                vf,
                ASMLiteDriftProbe.ParametersObjectRefPath,
                ASMLiteDriftProbe.ParameterObjectRefPath,
                ASMLiteDriftProbe.ParameterLegacyObjectRefPath);

            Assert.IsTrue(controllerReference.HasReference,
                assertionMessage + " Expected a populated FullController FX controller reference.");
            Assert.IsTrue(menuReference.HasReference,
                assertionMessage + " Expected a populated FullController menu reference.");
            Assert.IsTrue(parametersReference.HasReference,
                assertionMessage + " Expected a populated FullController parameter reference.");
            Assert.IsTrue(controllerReference.AssetPath.StartsWith(normalizedPrefix, StringComparison.Ordinal),
                assertionMessage + " Expected the FullController FX controller reference to point at the expected generated-assets prefix.");
            Assert.IsTrue(menuReference.AssetPath.StartsWith(normalizedPrefix, StringComparison.Ordinal),
                assertionMessage + " Expected the FullController menu reference to point at the expected generated-assets prefix.");
            Assert.IsTrue(parametersReference.AssetPath.StartsWith(normalizedPrefix, StringComparison.Ordinal),
                assertionMessage + " Expected the FullController parameter reference to point at the expected generated-assets prefix.");
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
