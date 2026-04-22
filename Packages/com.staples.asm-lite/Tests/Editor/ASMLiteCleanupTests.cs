using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// A35-A41: Cleanup integration invariants for Remove Prefab flow.
    /// These tests seed avatar assets via Build(), then verify CleanUpAvatarAssets()
    /// removes only ASM-Lite generated content while preserving user-owned artifacts.
    /// </summary>
    [TestFixture]
    public class ASMLiteCleanupTests
    {
        private AsmLiteTestContext _ctx;
        private string _cleanupVendorizedAvatarFolder;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "A35: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "A35: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "A35: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.Ctrl, "A35: fixture did not create FX AnimatorController.");
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrWhiteSpace(_cleanupVendorizedAvatarFolder)
                && AssetDatabase.IsValidFolder(_cleanupVendorizedAvatarFolder))
            {
                AssetDatabase.DeleteAsset(_cleanupVendorizedAvatarFolder);
            }

            if (AssetDatabase.IsValidFolder("Assets/ASM-Lite")
                && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/ASM-Lite" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/ASM-Lite");
            }

            AssetDatabase.Refresh();
            _cleanupVendorizedAvatarFolder = null;
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
        }

        // ── Helpers ────────────────────────────────────────────────────────────

        private static void AddAvatarParam(AsmLiteTestContext ctx, string name, VRCExpressionParameters.ValueType type, float defaultValue = 0f)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRCExpressionParameters.Parameter[0];
            var updated = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = true,
                networkSynced = true,
            };
            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteLayers(AsmLiteTestContext ctx, int slotCount)
        {
            for (int slot = 1; slot <= slotCount; slot++)
            {
                ctx.Ctrl.AddLayer(new AnimatorControllerLayer
                {
                    name = $"ASMLite_Slot{slot}",
                    defaultWeight = 1f,
                    stateMachine = new AnimatorStateMachine { name = $"ASMLite_Slot{slot}_SM" }
                });
            }
            EditorUtility.SetDirty(ctx.Ctrl);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteFxParams(AsmLiteTestContext ctx, params string[] paramNames)
        {
            ctx.Ctrl.AddParameter("ASMLite_Ctrl", AnimatorControllerParameterType.Int);
            foreach (var name in paramNames)
                ctx.Ctrl.AddParameter($"ASMLite_Bak_S1_{name}", AnimatorControllerParameterType.Float);
            EditorUtility.SetDirty(ctx.Ctrl);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteExprParams(AsmLiteTestContext ctx, params string[] paramNames)
        {
            var existing = ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0];
            var list = new System.Collections.Generic.List<VRCExpressionParameters.Parameter>(existing);
            list.Add(new VRCExpressionParameters.Parameter { name = "ASMLite_Ctrl", valueType = VRCExpressionParameters.ValueType.Int });
            foreach (var name in paramNames)
                list.Add(new VRCExpressionParameters.Parameter { name = $"ASMLite_Bak_S1_{name}", valueType = VRCExpressionParameters.ValueType.Float });
            ctx.AvDesc.expressionParameters.parameters = list.ToArray();
            EditorUtility.SetDirty(ctx.AvDesc.expressionParameters);
            AssetDatabase.SaveAssets();
        }

        private static void SeedLegacyAsmLiteMenu(AsmLiteTestContext ctx)
        {
            ctx.AvDesc.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "Settings Manager",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
            });
            EditorUtility.SetDirty(ctx.AvDesc.expressionsMenu);
            AssetDatabase.SaveAssets();
        }

        private static int BuildOrFail(AsmLiteTestContext ctx, string aid)
        {
            int buildResult = ASMLiteBuilder.Build(ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"{aid}: Build() failed with result {buildResult}.");
            return buildResult;
        }

        private static int CountASMLiteLayers(AnimatorController ctrl)
            => ctrl.layers.Count(l => l.name != null && l.name.StartsWith("ASMLite_"));

        private static int CountASMLiteFxParams(AnimatorController ctrl)
            => ctrl.parameters.Count(p => p.name != null
                && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"));

        private static int CountASMLiteExprParams(VRCExpressionParameters exprParams)
        {
            var items = exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0];
            return items.Count(p => p != null
                && p.name != null
                && (p.name.StartsWith("ASMLite_") || p.name == "ASMLite_Ctrl"));
        }

        private static int CountSettingsManagerControls(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu?.controls == null) return 0;
            return rootMenu.controls.Count(c => c != null
                && c.name == "Settings Manager"
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu);
        }

        private static int FindFxLayerIndex(VRCAvatarDescriptor avDesc)
        {
            for (int i = 0; i < avDesc.baseAnimationLayers.Length; i++)
            {
                if (avDesc.baseAnimationLayers[i].type == VRCAvatarDescriptor.AnimLayerType.FX)
                    return i;
            }
            return -1;
        }

        private static string EnsureAssetFolder(string parent, string child)
        {
            string candidate = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(parent, child);
            return candidate;
        }

        private static string CreateVendorizedMirrorForTest(string avatarName)
        {
            string root = EnsureAssetFolder("Assets", "ASM-Lite");
            string avatarFolder = EnsureAssetFolder(root, avatarName);
            string generatedFolder = EnsureAssetFolder(avatarFolder, "GeneratedAssets");

            CopyPackageAssetToMirror(ASMLiteAssetPaths.FXController, generatedFolder);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.ExprParams, generatedFolder);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.Menu, generatedFolder);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return generatedFolder;
        }

        private static int CountMenuAssetsUnderPrefix(VRCExpressionsMenu menu, string assetPrefix, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu))
                return 0;

            int count = 0;
            string normalizedPrefix = assetPrefix.Replace('\\', '/').TrimEnd('/');
            string menuPath = AssetDatabase.GetAssetPath(menu)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(menuPath)
                && menuPath.StartsWith(normalizedPrefix, System.StringComparison.Ordinal))
            {
                count++;
            }

            if (menu.controls == null)
                return count;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control?.subMenu == null)
                    continue;

                count += CountMenuAssetsUnderPrefix(control.subMenu, normalizedPrefix, visited);
            }

            return count;
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
            Assert.IsTrue(controllerReference.AssetPath.StartsWith(normalizedPrefix, System.StringComparison.Ordinal),
                assertionMessage + " Expected the FullController FX controller reference to point at the expected generated-assets prefix.");
            Assert.IsTrue(menuReference.AssetPath.StartsWith(normalizedPrefix, System.StringComparison.Ordinal),
                assertionMessage + " Expected the FullController menu reference to point at the expected generated-assets prefix.");
            Assert.IsTrue(parametersReference.AssetPath.StartsWith(normalizedPrefix, System.StringComparison.Ordinal),
                assertionMessage + " Expected the FullController parameter reference to point at the expected generated-assets prefix.");
        }

        private static void CopyPackageAssetToMirror(string sourceAssetPath, string targetFolder)
        {
            string destinationPath = targetFolder + "/" + Path.GetFileName(sourceAssetPath);
            AssetDatabase.DeleteAsset(destinationPath);
            Assert.IsTrue(AssetDatabase.CopyAsset(sourceAssetPath, destinationPath),
                $"Expected to copy '{sourceAssetPath}' to '{destinationPath}' for vendorized cleanup regression setup.");
        }

        private static void DeleteVendorizedAvatarFolderIfPresent(string generatedAssetsFolder)
        {
            if (string.IsNullOrWhiteSpace(generatedAssetsFolder))
                return;

            string avatarFolder = Path.GetDirectoryName(generatedAssetsFolder)?.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(avatarFolder) && AssetDatabase.IsValidFolder(avatarFolder))
                AssetDatabase.DeleteAsset(avatarFolder);

            if (AssetDatabase.IsValidFolder("Assets/ASM-Lite")
                && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/ASM-Lite" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/ASM-Lite");
            }

            AssetDatabase.Refresh();
        }

        // ── A35 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A35_Cleanup_RemovesASMLiteFxlayers()
        {
            _ctx.Comp.slotCount = 2;
            AddAvatarParam(_ctx, "MyInt", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteLayers(_ctx, 2);

            int before = CountASMLiteLayers(_ctx.Ctrl);
            Assert.Greater(before, 0,
                $"A35: setup failure, expected seeded ASMLite_ FX layers before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteLayers(_ctx.Ctrl);
            Assert.AreEqual(0, after,
                $"A35: cleanup must remove all ASMLite_ FX layers. before={before}, after={after}.");
        }

        // ── A36 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A36_Cleanup_RemovesASMLiteFxParametersAndCtrlParam()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float);
            SeedLegacyAsmLiteFxParams(_ctx, "MyFloat");

            int before = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.Greater(before, 0,
                $"A36: setup failure, expected seeded ASMLite_ FX params before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.AreEqual(0, after,
                $"A36: cleanup must remove ASMLite_ FX params and ASMLite_Ctrl. before={before}, after={after}.");
        }

        // ── A37 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A37_Cleanup_RemovesASMLiteExpressionParametersAndCtrlParam()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyBool", VRCExpressionParameters.ValueType.Bool, 1f);
            SeedLegacyAsmLiteExprParams(_ctx, "MyBool");

            int before = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.Greater(before, 0,
                $"A37: setup failure, expected seeded ASMLite_ expression params before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(0, after,
                $"A37: cleanup must remove ASMLite_ expression params and ASMLite_Ctrl. before={before}, after={after}.");
        }

        // ── A38 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A38_Cleanup_RemovesSettingsManagerRootControl()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteMenu(_ctx);

            int before = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.Greater(before, 0,
                $"A38: setup failure, expected Settings Manager control before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(0, after,
                $"A38: cleanup must remove Settings Manager root control. before={before}, after={after}.");
        }

        [Test, Category("Integration")]
        public void A38b_Cleanup_RemovesCustomizedDetachedRootControlByPresetsSubmenuReference()
        {
            _ctx.Comp.slotCount = 1;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "My Custom Presets";
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            bool detached = ASMLiteBuilder.TryDetachToDirectDelivery(_ctx.Comp, out string detail);
            Assert.IsTrue(detached,
                $"A38b: detach setup failed. detail={detail}");

            int customizedRootCountBefore = _ctx.AvDesc.expressionsMenu.controls.Count(c => c != null
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                && c.name == "My Custom Presets");
            Assert.Greater(customizedRootCountBefore, 0,
                $"A38b: setup failure, expected customized detached root control before cleanup. count={customizedRootCountBefore}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int customizedRootCountAfter = _ctx.AvDesc.expressionsMenu.controls.Count(c => c != null
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                && c.name == "My Custom Presets");
            Assert.AreEqual(0, customizedRootCountAfter,
                $"A38b: cleanup must remove customized detached root control. before={customizedRootCountBefore}, after={customizedRootCountAfter}.");
        }

        // ── A39 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A39_Cleanup_PreservesUserOwnedFxParamsAndMenuEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "SeedParam", VRCExpressionParameters.ValueType.Float, 0.25f);
            BuildOrFail(_ctx, "A39");

            // Seed user-owned FX layer and parameter (non-ASMLite namespace)
            _ctx.Ctrl.AddLayer(new AnimatorControllerLayer
            {
                name = "User_CustomLayer",
                defaultWeight = 1f,
                stateMachine = new AnimatorStateMachine { name = "UserSM" }
            });
            _ctx.Ctrl.AddParameter("User_CustomParam", AnimatorControllerParameterType.Float);

            // Seed user-owned expression parameter
            var existingExpr = _ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0];
            var mergedExpr = new VRCExpressionParameters.Parameter[existingExpr.Length + 1];
            existingExpr.CopyTo(mergedExpr, 0);
            mergedExpr[existingExpr.Length] = new VRCExpressionParameters.Parameter
            {
                name = "UserExprParam",
                valueType = VRCExpressionParameters.ValueType.Float,
                defaultValue = 0.75f,
                saved = true,
                networkSynced = true,
            };
            _ctx.AvDesc.expressionParameters.parameters = mergedExpr;

            // Seed user-owned root menu control
            _ctx.AvDesc.expressionsMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = "User Control",
                type = VRCExpressionsMenu.Control.ControlType.Button,
            });

            EditorUtility.SetDirty(_ctx.Ctrl);
            EditorUtility.SetDirty(_ctx.AvDesc.expressionParameters);
            EditorUtility.SetDirty(_ctx.AvDesc.expressionsMenu);
            AssetDatabase.SaveAssets();

            // Preconditions must be explicit to avoid vacuous pass
            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "A39: setup failure, user FX layer missing before cleanup.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "A39: setup failure, user FX param missing before cleanup.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "A39: setup failure, user expression param missing before cleanup.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "A39: setup failure, user menu control missing before cleanup.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "A39: cleanup removed user FX layer unexpectedly.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "A39: cleanup removed user FX param unexpectedly.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "A39: cleanup removed user expression param unexpectedly.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "A39: cleanup removed user menu control unexpectedly.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "A39: cleanup must still remove Settings Manager while preserving user controls.");
        }

        // ── A40 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A40_Cleanup_NullAvatarDescriptor_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(null),
                "A40: cleanup must no-op when avatar descriptor is null.");
        }

        // ── A41 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A41_Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteExprParams(_ctx, "MyParam");
            SeedLegacyAsmLiteMenu(_ctx);

            Assert.Greater(CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), 0,
                "A41: setup failure, expected ASMLite expression params before cleanup.");
            Assert.Greater(CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu), 0,
                "A41: setup failure, expected Settings Manager before cleanup.");

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "A41: setup failure, FX layer not found in avatar descriptor.");

            // Scenario 1: null controller
            var nullCtrlLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            nullCtrlLayer.animatorController = null;
            nullCtrlLayer.isDefault = false;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = nullCtrlLayer;

            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc),
                "A41: cleanup must not throw when FX controller is null.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters),
                "A41: cleanup should still remove ASMLite expression params when FX controller is null.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "A41: cleanup should still remove Settings Manager when FX controller is null.");

            // Re-seed for scenario 2: default FX layer flag with null controller
            SeedLegacyAsmLiteExprParams(_ctx, "MyParam");
            SeedLegacyAsmLiteMenu(_ctx);
            Assert.Greater(CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), 0,
                "A41: re-seed failure, expected ASMLite expression params before default-FX cleanup.");

            var defaultLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            defaultLayer.isDefault = true;
            defaultLayer.animatorController = null;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = defaultLayer;

            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc),
                "A41: cleanup must not throw when FX layer is default/unassigned.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters),
                "A41: cleanup should still remove ASMLite expression params when FX layer is default/unassigned.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "A41: cleanup should still remove Settings Manager when FX layer is default/unassigned.");
        }

        // ── A54 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A54_Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps()
        {
            _ctx.Comp.slotCount = 2;
            AddAvatarParam(_ctx, "A54_Int", VRCExpressionParameters.ValueType.Int);
            AddAvatarParam(_ctx, "A54_Float", VRCExpressionParameters.ValueType.Float, 0.5f);
            SeedLegacyAsmLiteLayers(_ctx, 2);
            SeedLegacyAsmLiteFxParams(_ctx, "A54_Int", "A54_Float");
            SeedLegacyAsmLiteExprParams(_ctx, "A54_Int", "A54_Float");
            SeedLegacyAsmLiteMenu(_ctx);

            var first = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_ctx.AvDesc);
            int firstTotalRemoved = first.FxLayersRemoved + first.FxParamsRemoved + first.ExprParamsRemoved + first.MenuControlsRemoved;
            Assert.Greater(firstTotalRemoved, 0,
                $"A54: first cleanup should remove legacy direct-injection-era state. fxLayers={first.FxLayersRemoved}, fxParams={first.FxParamsRemoved}, expr={first.ExprParamsRemoved}, menu={first.MenuControlsRemoved}.");

            var second = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_ctx.AvDesc);
            Assert.AreEqual(0, second.FxLayersRemoved,
                $"A54: second cleanup should be a deterministic no-op for FX layers. removed={second.FxLayersRemoved}.");
            Assert.AreEqual(0, second.FxParamsRemoved,
                $"A54: second cleanup should be a deterministic no-op for FX params. removed={second.FxParamsRemoved}.");
            Assert.AreEqual(0, second.ExprParamsRemoved,
                $"A54: second cleanup should be a deterministic no-op for expression params. removed={second.ExprParamsRemoved}.");
            Assert.AreEqual(0, second.MenuControlsRemoved,
                $"A54: second cleanup should be a deterministic no-op for menu controls. removed={second.MenuControlsRemoved}.");

            Assert.AreEqual(0, CountASMLiteLayers(_ctx.Ctrl), "A54: ASMLite FX layers must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountASMLiteFxParams(_ctx.Ctrl), "A54: ASMLite FX params must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), "A54: ASMLite expression params must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu), "A54: Settings Manager control must remain absent after repeated cleanup.");
        }

        // ── A55 ────────────────────────────────────────────────────────────────

        [Test, Category("Integration")]
        public void A55_ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder()
        {
            const string avatarName = "CleanupVendorizedAvatar";

            _ctx.AvatarGo.name = avatarName;
            AddAvatarParam(_ctx, "A55_Int", VRCExpressionParameters.ValueType.Int);
            BuildOrFail(_ctx, "A55");

            string vendorizedDir = CreateVendorizedMirrorForTest(avatarName);
            _cleanupVendorizedAvatarFolder = "Assets/ASM-Lite/" + avatarName;

            Assert.IsTrue(AssetDatabase.IsValidFolder(vendorizedDir),
                $"A55: setup failure, expected vendorized folder '{vendorizedDir}' to exist before return-to-managed cleanup.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController)),
                "A55: setup failure, expected mirrored FX controller asset before return-to-managed cleanup.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams)),
                "A55: setup failure, expected mirrored expression params asset before return-to-managed cleanup.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu)),
                "A55: setup failure, expected mirrored menu asset before return-to-managed cleanup.");

            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = vendorizedDir;
            _ctx.AvDesc.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            _ctx.AvDesc.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "A55: setup failure, expected avatar FX layer before assigning mirrored controller.");

            var fxLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            fxLayer.isDefault = false;
            fxLayer.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = fxLayer;
            EditorUtility.SetDirty(_ctx.AvDesc);
            EditorUtility.SetDirty(_ctx.Comp);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, CountMenuAssetsUnderPrefix(_ctx.AvDesc.expressionsMenu, vendorizedDir, new System.Collections.Generic.HashSet<VRCExpressionsMenu>()),
                "A55: setup failure, expected mirrored menu root reference before return-to-managed cleanup.");

            bool restored = ASMLiteWindow.TryReturnAttachedVendorizedToPackageManaged(_ctx.Comp, _ctx.AvDesc);

            Assert.IsTrue(restored,
                "A55: attached vendorized return helper should succeed for a valid mirrored GeneratedAssets folder.");
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets,
                "A55: return-to-managed should clear attached vendorized mode on the component.");
            Assert.AreEqual(string.Empty, _ctx.Comp.vendorizedGeneratedAssetsPath,
                "A55: return-to-managed should clear the tracked vendorized folder path after cleanup.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(vendorizedDir),
                $"A55: return-to-managed should delete the vendorized GeneratedAssets folder '{vendorizedDir}'.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(_cleanupVendorizedAvatarFolder),
                $"A55: return-to-managed should prune the now-empty avatar vendorized folder '{_cleanupVendorizedAvatarFolder}'.");
            Assert.AreEqual(ASMLiteAssetPaths.ExprParams,
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/'),
                "A55: return-to-managed should restore avatar expression parameters back to package-managed generated assets.");
            Assert.AreEqual(ASMLiteAssetPaths.Menu,
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/'),
                "A55: return-to-managed should restore avatar expressions menu back to package-managed generated assets.");

            int restoredFxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(restoredFxIndex, 0,
                "A55: expected avatar FX layer after return-to-managed cleanup.");
            Assert.AreEqual(ASMLiteAssetPaths.FXController,
                AssetDatabase.GetAssetPath(_ctx.AvDesc.baseAnimationLayers[restoredFxIndex].animatorController)?.Replace('\\', '/'),
                "A55: return-to-managed should restore avatar FX controller back to package-managed generated assets.");
            Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.PackageManaged,
                ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp),
                "A55: attached avatar should resolve to PackageManaged state after vendorized cleanup completes.");
        }

        [Test, Category("Integration")]
        public void A56_ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState()
        {
            const string avatarName = "CleanupVendorizedRollbackAvatar";

            _ctx.AvatarGo.name = avatarName;
            AddAvatarParam(_ctx, "A56_Int", VRCExpressionParameters.ValueType.Int);
            BuildOrFail(_ctx, "A56");

            string vendorizedDir = CreateVendorizedMirrorForTest(avatarName);
            _cleanupVendorizedAvatarFolder = "Assets/ASM-Lite/" + avatarName;

            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = vendorizedDir;
            _ctx.AvDesc.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            _ctx.AvDesc.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "A56: setup failure, expected avatar FX layer before assigning mirrored controller.");

            var fxLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            fxLayer.isDefault = false;
            fxLayer.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = fxLayer;
            EditorUtility.SetDirty(_ctx.AvDesc);
            EditorUtility.SetDirty(_ctx.Comp);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "A56 Setup"),
                "A56: setup should create a live FullController payload before attached-return rollback validation.");
            Assert.IsTrue(ASMLiteWindow.TryRetargetLiveFullControllerGeneratedAssetsForTesting(_ctx.Comp, vendorizedDir),
                "A56: setup should retarget live FullController references to vendorized assets before attached-return rollback validation.");
            AssertLiveFullControllerReferencesUnderPrefix(vendorizedDir, "A56 setup should leave live FullController references on vendorized assets before return rollback validation.");

            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringVendorizedFolderDelete))
            {
                var result = ASMLiteLifecycleTransactionService.ExecuteAttachedReturnToPackageManaged(_ctx.Comp, _ctx.AvDesc);
                Assert.IsFalse(result.Success,
                    "A56: attached return should fail closed when vendorized-folder delete staging is injected to fail.");
                Assert.AreEqual(ASMLiteLifecycleTransactionStage.Execute, result.FailedStage,
                    "A56: delete-stage failure should surface as an execute-stage transaction failure.");
                Assert.IsTrue(result.RollbackAttempted,
                    "A56: attached return should attempt rollback after delete-stage failure.");
                Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.Vendorized, result.RollbackState,
                    "A56: attached return rollback state should resolve back to Vendorized.");
            }

            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets,
                "A56: rollback should preserve vendorized mode on the attached component after delete-stage failure.");
            Assert.AreEqual(vendorizedDir, _ctx.Comp.vendorizedGeneratedAssetsPath,
                "A56: rollback should preserve the tracked vendorized generated-assets path after delete-stage failure.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(vendorizedDir),
                "A56: rollback should restore the vendorized generated-assets folder after delete-stage failure.");
            Assert.AreEqual(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams),
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/'),
                "A56: rollback should restore avatar expression parameters back to vendorized assets after delete-stage failure.");
            Assert.AreEqual(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu),
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/'),
                "A56: rollback should restore avatar expressions menu back to vendorized assets after delete-stage failure.");

            int rollbackFxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(rollbackFxIndex, 0,
                "A56: expected avatar FX layer after delete-stage rollback.");
            Assert.AreEqual(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController),
                AssetDatabase.GetAssetPath(_ctx.AvDesc.baseAnimationLayers[rollbackFxIndex].animatorController)?.Replace('\\', '/'),
                "A56: rollback should restore avatar FX controller back to vendorized assets after delete-stage failure.");
            AssertLiveFullControllerReferencesUnderPrefix(vendorizedDir,
                "A56: rollback should restore live FullController references back to vendorized assets after delete-stage failure.");
            Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.Vendorized,
                ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp),
                "A56: attached avatar should resolve to Vendorized state after delete-stage rollback completes.");
        }

        [Test, Category("Integration")]
        public void A57_DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "Tools/RecoveryFailure";
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();
                var pendingSnapshot = ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp);
                window.DetachForAutomation();

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "A57: setup should leave the avatar detached before recovery failure injection.");
                Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.Detached,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "A57: setup should classify the detached avatar as Detached before recovery failure injection.");

                using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify))
                {
                    var result = ASMLiteLifecycleTransactionService.ExecuteDetachedReturnToPackageManagedRecovery(
                        _ctx.AvDesc,
                        pendingSnapshot);

                    Assert.IsFalse(result.Success,
                        "A57: detached recovery should fail closed when verify-stage failure is injected after reattachment completes.");
                    Assert.AreEqual(ASMLiteLifecycleTransactionStage.Verify, result.FailedStage,
                        "A57: injected detached recovery failure should surface as a verify-stage transaction failure.");
                    Assert.IsTrue(result.CleanupAttempted,
                        "A57: detached recovery should record that cleanup ran before the reattach attempt.");
                    Assert.IsTrue(result.CleanupSucceeded,
                        "A57: detached recovery should report cleanup success even when later verify-stage recovery fails.");
                    Assert.IsTrue(result.ReattachAttempted,
                        "A57: detached recovery should record the reattach attempt before verify-stage failure.");
                    Assert.IsFalse(result.ReattachSucceeded,
                        "A57: detached recovery should report reattach failure after verify-stage failure destroys the partial prefab.");
                    Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.NotInstalled, result.RecoveredState,
                        "A57: detached recovery should report the best-effort recovered state after tearing down the partial prefab.");
                }

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "A57: detached recovery verify failure should not leave a partial ASM-Lite prefab attached.");
                Assert.AreEqual(ASMLiteWindow.AsmLiteToolState.NotInstalled,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "A57: detached recovery verify failure should clean direct-delivery runtime markers instead of leaving the avatar half-restored.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test, Category("Integration")]
        public void A58_VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName()
        {
            var otherAvatarGo = new GameObject("CollisionAvatar");
            var otherAvatar = otherAvatarGo.AddComponent<VRCAvatarDescriptor>();
            ASMLiteGeneratedAssetMirrorResult firstMirror = null;
            ASMLiteGeneratedAssetMirrorResult secondMirror = null;

            try
            {
                _ctx.AvatarGo.name = "CollisionAvatar";

                firstMirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(_ctx.AvDesc);
                secondMirror = ASMLiteGeneratedAssetMirrorService.StageVendorizedMirror(otherAvatar);

                Assert.IsTrue(firstMirror != null && firstMirror.Success,
                    firstMirror?.Message ?? "A58: expected first vendorized mirror stage to succeed.");
                Assert.IsTrue(secondMirror != null && secondMirror.Success,
                    secondMirror?.Message ?? "A58: expected second vendorized mirror stage to succeed.");
                Assert.AreNotEqual(firstMirror.TargetPath, secondMirror.TargetPath,
                    "A58: distinct avatars that share the same GameObject name must not collide onto the same vendorized GeneratedAssets folder.");
                Assert.AreNotEqual(
                    Path.GetDirectoryName(firstMirror.TargetPath)?.Replace('\\', '/'),
                    Path.GetDirectoryName(secondMirror.TargetPath)?.Replace('\\', '/'),
                    "A58: distinct avatars that share the same GameObject name must receive separate vendorized avatar folders.");
                Assert.IsTrue(AssetDatabase.IsValidFolder(firstMirror.TargetPath),
                    $"A58: expected first vendorized GeneratedAssets folder '{firstMirror.TargetPath}' to exist.");
                Assert.IsTrue(AssetDatabase.IsValidFolder(secondMirror.TargetPath),
                    $"A58: expected second vendorized GeneratedAssets folder '{secondMirror.TargetPath}' to exist.");
            }
            finally
            {
                DeleteVendorizedAvatarFolderIfPresent(firstMirror?.TargetPath);
                DeleteVendorizedAvatarFolderIfPresent(secondMirror?.TargetPath);
                Object.DestroyImmediate(otherAvatarGo);
            }
        }
    }
}
