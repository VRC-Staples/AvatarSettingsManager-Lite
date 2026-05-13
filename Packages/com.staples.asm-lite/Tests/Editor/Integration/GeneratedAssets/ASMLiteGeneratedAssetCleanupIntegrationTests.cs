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
    /// Cleanup_RemovesASMLiteFxlayers-Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: Cleanup integration invariants for Remove Prefab flow.
    /// These tests seed avatar assets via Build(), then verify CleanUpAvatarAssets()
    /// removes only ASM-Lite generated content while preserving user-owned artifacts.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteGeneratedAssetCleanupIntegrationTests
    {
        private AsmLiteTestContext _ctx;
        private readonly List<string> _ownedVendorizedAvatarFolders = new List<string>();
        private bool _asmLiteRootExistedBeforeTest;
        private PackageGeneratedAssetsSnapshot _packageGeneratedAssetsSnapshot;

        [SetUp]
        public void SetUp()
        {
            _ownedVendorizedAvatarFolders.Clear();
            _asmLiteRootExistedBeforeTest = AssetDatabase.IsValidFolder("Assets/ASM-Lite");
            _packageGeneratedAssetsSnapshot = PackageGeneratedAssetsSnapshot.Capture(ASMLiteAssetPaths.GeneratedDir);

            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "Cleanup_RemovesASMLiteFxlayers: fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "Cleanup_RemovesASMLiteFxlayers: fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "Cleanup_RemovesASMLiteFxlayers: fixture did not create VRCAvatarDescriptor.");
            Assert.IsNotNull(_ctx.Ctrl, "Cleanup_RemovesASMLiteFxlayers: fixture did not create FX AnimatorController.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                DeleteOwnedVendorizedAvatarFolders();
                _packageGeneratedAssetsSnapshot?.Restore();
            }
            finally
            {
                _packageGeneratedAssetsSnapshot = null;
                _ownedVendorizedAvatarFolders.Clear();
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
                _ctx = null;
            }
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
            => ctrl.layers.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxLayer);

        private static int CountASMLiteFxParams(AnimatorController ctrl)
            => ctrl.parameters.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxParameter);

        private static int CountASMLiteExprParams(VRCExpressionParameters exprParams)
        {
            var items = exprParams?.parameters ?? new VRCExpressionParameters.Parameter[0];
            return items.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter);
        }

        private static int CountSettingsManagerControls(VRCExpressionsMenu rootMenu)
        {
            if (rootMenu?.controls == null) return 0;
            return rootMenu.controls.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl);
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

        private string CreateVendorizedMirrorForTest(string avatarName, string aid)
        {
            string root = EnsureAssetFolder("Assets", "ASM-Lite");
            string avatarFolder = CreateUniqueOwnedAvatarFolder(root, avatarName, aid);
            string generatedFolder = EnsureAssetFolder(avatarFolder, "GeneratedAssets");

            CopyPackageAssetToMirror(ASMLiteAssetPaths.FXController, generatedFolder);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.ExprParams, generatedFolder);
            CopyPackageAssetToMirror(ASMLiteAssetPaths.Menu, generatedFolder);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return generatedFolder;
        }

        private string CreateUniqueOwnedAvatarFolder(string root, string avatarName, string aid)
        {
            string prefix = SanitizePathFragment(string.IsNullOrWhiteSpace(aid)
                ? avatarName
                : $"{aid}_{avatarName}");

            for (int attempt = 0; attempt < 20; attempt++)
            {
                string folderName = $"{prefix}_{System.Guid.NewGuid():N}";
                folderName = folderName.Substring(0, System.Math.Min(folderName.Length, prefix.Length + 9));
                string folderPath = root + "/" + folderName;
                if (AssetDatabase.IsValidFolder(folderPath))
                    continue;

                AssetDatabase.CreateFolder(root, folderName);
                _ownedVendorizedAvatarFolders.Add(folderPath);
                return folderPath;
            }

            Assert.Fail($"{aid}: could not allocate a unique test-owned vendorized avatar folder under '{root}'.");
            return null;
        }

        private static string SanitizePathFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "CleanupAvatar";

            var invalid = Path.GetInvalidFileNameChars();
            var chars = value
                .Trim()
                .Select(c => invalid.Contains(c) ? '_' : c)
                .ToArray();
            string sanitized = new string(chars);
            return string.IsNullOrWhiteSpace(sanitized) ? "CleanupAvatar" : sanitized;
        }

        private static int CountMenuAssetsUnderPrefix(VRCExpressionsMenu menu, string assetPrefix, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu))
                return 0;

            int count = 0;
            string normalizedPrefix = assetPrefix.Replace('\\', '/').TrimEnd('/');
            string menuPath = AssetDatabase.GetAssetPath(menu);
            if (ASMLiteGeneratedOwnershipPolicy.PathStartsWith(menuPath, normalizedPrefix))
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
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.PathStartsWith(controllerReference.AssetPath, normalizedPrefix),
                assertionMessage + " Expected the FullController FX controller reference to point at the expected generated-assets prefix.");
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.PathStartsWith(menuReference.AssetPath, normalizedPrefix),
                assertionMessage + " Expected the FullController menu reference to point at the expected generated-assets prefix.");
            Assert.IsTrue(ASMLiteGeneratedOwnershipPolicy.PathStartsWith(parametersReference.AssetPath, normalizedPrefix),
                assertionMessage + " Expected the FullController parameter reference to point at the expected generated-assets prefix.");
        }

        private static int CountMatchingMoveMenuHelpers(VRCAvatarDescriptor avatar, string fromPath, string toPath)
        {
            if (avatar == null)
                return 0;

            return avatar
                .GetComponentsInChildren<VF.Model.VRCFury>(true)
                .Count(component => component != null
                    && component.content is VF.Model.Feature.MoveMenuItem move
                    && string.Equals(move.fromPath, fromPath, System.StringComparison.Ordinal)
                    && string.Equals(move.toPath, toPath, System.StringComparison.Ordinal));
        }

        private static void CopyPackageAssetToMirror(string sourceAssetPath, string targetFolder)
        {
            string destinationPath = targetFolder + "/" + Path.GetFileName(sourceAssetPath);
            Assert.IsFalse(AssetDatabase.LoadMainAssetAtPath(destinationPath) != null || AssetDatabase.IsValidFolder(destinationPath),
                $"Expected test-owned mirror destination '{destinationPath}' to be empty before copying package asset '{sourceAssetPath}'.");
            Assert.IsTrue(AssetDatabase.CopyAsset(sourceAssetPath, destinationPath),
                $"Expected to copy '{sourceAssetPath}' to '{destinationPath}' for vendorized cleanup regression setup.");
        }

        private string CreateUserOwnedSentinelAsset(string avatarFolder, string fileName)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(avatarFolder),
                "Expected a test-owned avatar folder before creating a user-owned sentinel asset.");
            string assetPath = avatarFolder + "/" + fileName;
            var sentinel = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            sentinel.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "User Owned Sentinel",
                    type = VRCExpressionsMenu.Control.ControlType.Button,
                }
            };
            AssetDatabase.CreateAsset(sentinel, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return assetPath;
        }

        private void DeleteVendorizedAvatarFolderIfPresent(string generatedAssetsFolder)
        {
            if (string.IsNullOrWhiteSpace(generatedAssetsFolder))
                return;

            string avatarFolder = Path.GetDirectoryName(generatedAssetsFolder)?.Replace('\\', '/');
            DeleteOwnedVendorizedAvatarFolder(avatarFolder);
            PruneOwnedAsmLiteRootIfEmpty();
            AssetDatabase.Refresh();
        }

        private void DeleteOwnedVendorizedAvatarFolders()
        {
            foreach (string avatarFolder in _ownedVendorizedAvatarFolders
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .OrderByDescending(path => path.Length)
                .ToArray())
            {
                DeleteOwnedVendorizedAvatarFolder(avatarFolder);
            }

            PruneOwnedAsmLiteRootIfEmpty();
            AssetDatabase.Refresh();
        }

        private void DeleteOwnedVendorizedAvatarFolder(string avatarFolder)
        {
            if (string.IsNullOrWhiteSpace(avatarFolder))
                return;

            string normalized = avatarFolder.Replace('\\', '/').TrimEnd('/');
            if (!_ownedVendorizedAvatarFolders.Contains(normalized))
                _ownedVendorizedAvatarFolders.Add(normalized);

            if (AssetDatabase.IsValidFolder(normalized))
                AssetDatabase.DeleteAsset(normalized);
        }

        private void PruneOwnedAsmLiteRootIfEmpty()
        {
            if (_asmLiteRootExistedBeforeTest)
                return;

            if (AssetDatabase.IsValidFolder("Assets/ASM-Lite")
                && AssetDatabase.FindAssets(string.Empty, new[] { "Assets/ASM-Lite" }).Length == 0)
            {
                AssetDatabase.DeleteAsset("Assets/ASM-Lite");
            }
        }

        [Test, Category("Integration")]
        public void Cleanup_RemovesASMLiteFxlayers()
        {
            _ctx.Comp.slotCount = 2;
            AddAvatarParam(_ctx, "MyInt", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteLayers(_ctx, 2);

            int before = CountASMLiteLayers(_ctx.Ctrl);
            Assert.Greater(before, 0,
                $"Cleanup_RemovesASMLiteFxlayers: setup failure, expected seeded ASMLite_ FX layers before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteLayers(_ctx.Ctrl);
            Assert.AreEqual(0, after,
                $"Cleanup_RemovesASMLiteFxlayers: cleanup must remove all ASMLite_ FX layers. before={before}, after={after}.");
        }

        [Test, Category("Integration")]
        public void Cleanup_RemovesASMLiteFxParametersAndCtrlParam()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyFloat", VRCExpressionParameters.ValueType.Float);
            SeedLegacyAsmLiteFxParams(_ctx, "MyFloat");

            int before = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.Greater(before, 0,
                $"Cleanup_RemovesASMLiteFxParametersAndCtrlParam: setup failure, expected seeded ASMLite_ FX params before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteFxParams(_ctx.Ctrl);
            Assert.AreEqual(0, after,
                $"Cleanup_RemovesASMLiteFxParametersAndCtrlParam: cleanup must remove ASMLite_ FX params and ASMLite_Ctrl. before={before}, after={after}.");
        }

        [Test, Category("Integration")]
        public void Cleanup_RemovesASMLiteExpressionParametersAndCtrlParam()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyBool", VRCExpressionParameters.ValueType.Bool, 1f);
            SeedLegacyAsmLiteExprParams(_ctx, "MyBool");

            int before = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.Greater(before, 0,
                $"Cleanup_RemovesASMLiteExpressionParametersAndCtrlParam: setup failure, expected seeded ASMLite_ expression params before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountASMLiteExprParams(_ctx.AvDesc.expressionParameters);
            Assert.AreEqual(0, after,
                $"Cleanup_RemovesASMLiteExpressionParametersAndCtrlParam: cleanup must remove ASMLite_ expression params and ASMLite_Ctrl. before={before}, after={after}.");
        }

        [Test, Category("Integration")]
        public void Cleanup_RemovesSettingsManagerRootControl()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteMenu(_ctx);

            int before = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.Greater(before, 0,
                $"Cleanup_RemovesSettingsManagerRootControl: setup failure, expected Settings Manager control before cleanup. before={before}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int after = CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu);
            Assert.AreEqual(0, after,
                $"Cleanup_RemovesSettingsManagerRootControl: cleanup must remove Settings Manager root control. before={before}, after={after}.");
        }

        [Test, Category("Integration")]
        public void Cleanup_RemovesCustomizedDetachedRootControlByPresetsSubmenuReference()
        {
            _ctx.Comp.slotCount = 1;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "My Custom Presets";
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);

            bool detached = ASMLiteBuilder.TryDetachToDirectDelivery(_ctx.Comp, out string detail);
            Assert.IsTrue(detached,
                $"Cleanup_RemovesCustomizedDetachedRootControlByPresetsSubmenuReference: detach setup failed. detail={detail}");

            int customizedRootCountBefore = _ctx.AvDesc.expressionsMenu.controls.Count(c => c != null
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                && c.name == "My Custom Presets");
            Assert.Greater(customizedRootCountBefore, 0,
                $"Cleanup_RemovesCustomizedDetachedRootControlByPresetsSubmenuReference: setup failure, expected customized detached root control before cleanup. count={customizedRootCountBefore}.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            int customizedRootCountAfter = _ctx.AvDesc.expressionsMenu.controls.Count(c => c != null
                && c.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                && c.name == "My Custom Presets");
            Assert.AreEqual(0, customizedRootCountAfter,
                $"Cleanup_RemovesCustomizedDetachedRootControlByPresetsSubmenuReference: cleanup must remove customized detached root control. before={customizedRootCountBefore}, after={customizedRootCountAfter}.");
        }

        [Test, Category("Integration")]
        public void Cleanup_PreservesUserOwnedFxParamsAndMenuEntries()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "SeedParam", VRCExpressionParameters.ValueType.Float, 0.25f);
            BuildOrFail(_ctx, "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries");

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
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: setup failure, user FX layer missing before cleanup.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: setup failure, user FX param missing before cleanup.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: setup failure, user expression param missing before cleanup.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: setup failure, user menu control missing before cleanup.");

            ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc);

            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: cleanup removed user FX layer unexpectedly.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: cleanup removed user FX param unexpectedly.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: cleanup removed user expression param unexpectedly.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: cleanup removed user menu control unexpectedly.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "Cleanup_PreservesUserOwnedFxParamsAndMenuEntries: cleanup must still remove Settings Manager while preserving user controls.");
        }

        [Test, Category("Integration")]
        public void Cleanup_NullAvatarDescriptor_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(null),
                "Cleanup_NullAvatarDescriptor_DoesNotThrow: cleanup must no-op when avatar descriptor is null.");
        }

        [Test, Category("Integration")]
        public void Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams()
        {
            _ctx.Comp.slotCount = 1;
            AddAvatarParam(_ctx, "MyParam", VRCExpressionParameters.ValueType.Int);
            SeedLegacyAsmLiteExprParams(_ctx, "MyParam");
            SeedLegacyAsmLiteMenu(_ctx);

            Assert.Greater(CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), 0,
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: setup failure, expected ASMLite expression params before cleanup.");
            Assert.Greater(CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu), 0,
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: setup failure, expected Settings Manager before cleanup.");

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: setup failure, FX layer not found in avatar descriptor.");

            // Scenario 1: null controller
            var nullCtrlLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            nullCtrlLayer.animatorController = null;
            nullCtrlLayer.isDefault = false;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = nullCtrlLayer;

            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc),
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: cleanup must not throw when FX controller is null.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters),
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: cleanup should still remove ASMLite expression params when FX controller is null.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: cleanup should still remove Settings Manager when FX controller is null.");

            // Re-seed for scenario 2: default FX layer flag with null controller
            SeedLegacyAsmLiteExprParams(_ctx, "MyParam");
            SeedLegacyAsmLiteMenu(_ctx);
            Assert.Greater(CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), 0,
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: re-seed failure, expected ASMLite expression params before default-FX cleanup.");

            var defaultLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            defaultLayer.isDefault = true;
            defaultLayer.animatorController = null;
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = defaultLayer;

            Assert.DoesNotThrow(() => ASMLiteBuilder.CleanUpAvatarAssets(_ctx.AvDesc),
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: cleanup must not throw when FX layer is default/unassigned.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters),
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: cleanup should still remove ASMLite expression params when FX layer is default/unassigned.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu),
                "Cleanup_NullOrDefaultFxController_DoesNotThrowAndStillCleansMenuAndExprParams: cleanup should still remove Settings Manager when FX layer is default/unassigned.");
        }

        [Test, Category("Integration")]
        public void Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps()
        {
            _ctx.Comp.slotCount = 2;
            AddAvatarParam(_ctx, "CleanupReportRepeatedCalls_Int", VRCExpressionParameters.ValueType.Int);
            AddAvatarParam(_ctx, "CleanupReportRepeatedCalls_Float", VRCExpressionParameters.ValueType.Float, 0.5f);
            SeedLegacyAsmLiteLayers(_ctx, 2);
            SeedLegacyAsmLiteFxParams(_ctx, "CleanupReportRepeatedCalls_Int", "CleanupReportRepeatedCalls_Float");
            SeedLegacyAsmLiteExprParams(_ctx, "CleanupReportRepeatedCalls_Int", "CleanupReportRepeatedCalls_Float");
            SeedLegacyAsmLiteMenu(_ctx);

            var first = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_ctx.AvDesc);
            int firstTotalRemoved = first.FxLayersRemoved + first.FxParamsRemoved + first.ExprParamsRemoved + first.MenuControlsRemoved;
            Assert.Greater(firstTotalRemoved, 0,
                $"Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: first cleanup should remove legacy direct-injection-era state. fxLayers={first.FxLayersRemoved}, fxParams={first.FxParamsRemoved}, expr={first.ExprParamsRemoved}, menu={first.MenuControlsRemoved}.");

            var second = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_ctx.AvDesc);
            Assert.AreEqual(0, second.FxLayersRemoved,
                $"Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: second cleanup should be a deterministic no-op for FX layers. removed={second.FxLayersRemoved}.");
            Assert.AreEqual(0, second.FxParamsRemoved,
                $"Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: second cleanup should be a deterministic no-op for FX params. removed={second.FxParamsRemoved}.");
            Assert.AreEqual(0, second.ExprParamsRemoved,
                $"Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: second cleanup should be a deterministic no-op for expression params. removed={second.ExprParamsRemoved}.");
            Assert.AreEqual(0, second.MenuControlsRemoved,
                $"Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: second cleanup should be a deterministic no-op for menu controls. removed={second.MenuControlsRemoved}.");

            Assert.AreEqual(0, CountASMLiteLayers(_ctx.Ctrl), "Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: ASMLite FX layers must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountASMLiteFxParams(_ctx.Ctrl), "Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: ASMLite FX params must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountASMLiteExprParams(_ctx.AvDesc.expressionParameters), "Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: ASMLite expression params must remain absent after repeated cleanup.");
            Assert.AreEqual(0, CountSettingsManagerControls(_ctx.AvDesc.expressionsMenu), "Cleanup_Report_RepeatedCallsBecomeDeterministicNoOps: Settings Manager control must remain absent after repeated cleanup.");
        }

        [Test, Category("Integration")]
        public void ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder()
        {
            const string avatarName = "CleanupVendorizedAvatar";

            _ctx.AvatarGo.name = avatarName;
            AddAvatarParam(_ctx, "ReturnAttachedVendorized_Int", VRCExpressionParameters.ValueType.Int);
            BuildOrFail(_ctx, "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder");

            string vendorizedDir = CreateVendorizedMirrorForTest(avatarName, "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder");
            string vendorizedAvatarFolder = Path.GetDirectoryName(vendorizedDir)?.Replace('\\', '/');
            string userOwnedSiblingAsset = CreateUserOwnedSentinelAsset(vendorizedAvatarFolder, "ReturnAttachedVendorized_UserOwnedSibling.asset");

            Assert.IsTrue(AssetDatabase.IsValidFolder(vendorizedDir),
                $"ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: setup failure, expected vendorized folder '{vendorizedDir}' to exist before return-to-managed cleanup.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<AnimatorController>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController)),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: setup failure, expected mirrored FX controller asset before return-to-managed cleanup.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams)),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: setup failure, expected mirrored expression params asset before return-to-managed cleanup.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu)),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: setup failure, expected mirrored menu asset before return-to-managed cleanup.");

            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = vendorizedDir;
            _ctx.AvDesc.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            _ctx.AvDesc.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: setup failure, expected avatar FX layer before assigning mirrored controller.");

            var fxLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            fxLayer.isDefault = false;
            fxLayer.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = fxLayer;
            EditorUtility.SetDirty(_ctx.AvDesc);
            EditorUtility.SetDirty(_ctx.Comp);
            AssetDatabase.SaveAssets();

            Assert.AreEqual(1, CountMenuAssetsUnderPrefix(_ctx.AvDesc.expressionsMenu, vendorizedDir, new System.Collections.Generic.HashSet<VRCExpressionsMenu>()),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: setup failure, expected mirrored menu root reference before return-to-managed cleanup.");

            bool restored = ASMLiteWindow.TryReturnAttachedVendorizedToPackageManaged(_ctx.Comp, _ctx.AvDesc);

            Assert.IsTrue(restored,
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: attached vendorized return helper should succeed for a valid mirrored GeneratedAssets folder.");
            Assert.IsFalse(_ctx.Comp.useVendorizedGeneratedAssets,
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should clear attached vendorized mode on the component.");
            Assert.AreEqual(string.Empty, _ctx.Comp.vendorizedGeneratedAssetsPath,
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should clear the tracked vendorized folder path after cleanup.");
            Assert.IsFalse(AssetDatabase.IsValidFolder(vendorizedDir),
                $"ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should delete the vendorized GeneratedAssets folder '{vendorizedDir}'.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(vendorizedAvatarFolder),
                $"ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should preserve avatar vendorized folder '{vendorizedAvatarFolder}' while user-owned sibling assets remain.");
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(userOwnedSiblingAsset),
                $"ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should preserve user-owned sibling asset '{userOwnedSiblingAsset}' while deleting only the mirrored GeneratedAssets folder.");
            Assert.AreEqual(ASMLiteAssetPaths.ExprParams,
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/'),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should restore avatar expression parameters back to package-managed generated assets.");
            Assert.AreEqual(ASMLiteAssetPaths.Menu,
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/'),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should restore avatar expressions menu back to package-managed generated assets.");

            int restoredFxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(restoredFxIndex, 0,
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: expected avatar FX layer after return-to-managed cleanup.");
            Assert.AreEqual(ASMLiteAssetPaths.FXController,
                AssetDatabase.GetAssetPath(_ctx.AvDesc.baseAnimationLayers[restoredFxIndex].animatorController)?.Replace('\\', '/'),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: return-to-managed should restore avatar FX controller back to package-managed generated assets.");
            Assert.AreEqual(ASMLiteInstallationState.PackageManaged,
                ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp),
                "ReturnAttachedVendorizedToPackageManaged_DeletesVendorizedMirrorFolder: attached avatar should resolve to PackageManaged state after vendorized cleanup completes.");
        }

        [Test, Category("Integration")]
        public void ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState()
        {
            const string avatarName = "CleanupVendorizedRollbackAvatar";

            _ctx.AvatarGo.name = avatarName;
            AddAvatarParam(_ctx, "ReturnAttachedVendorizedRollback_Int", VRCExpressionParameters.ValueType.Int);
            BuildOrFail(_ctx, "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState");

            string vendorizedDir = CreateVendorizedMirrorForTest(avatarName, "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState");

            _ctx.Comp.useVendorizedGeneratedAssets = true;
            _ctx.Comp.vendorizedGeneratedAssetsPath = vendorizedDir;
            _ctx.AvDesc.expressionParameters = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            _ctx.AvDesc.expressionsMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));

            int fxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(fxIndex, 0,
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: setup failure, expected avatar FX layer before assigning mirrored controller.");

            var fxLayer = _ctx.AvDesc.baseAnimationLayers[fxIndex];
            fxLayer.isDefault = false;
            fxLayer.animatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            _ctx.AvDesc.baseAnimationLayers[fxIndex] = fxLayer;
            EditorUtility.SetDirty(_ctx.AvDesc);
            EditorUtility.SetDirty(_ctx.Comp);
            AssetDatabase.SaveAssets();

            Assert.IsTrue(ASMLiteFullControllerWiring.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState Setup"),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: setup should create a live FullController payload before attached-return rollback validation.");
            Assert.IsTrue(ASMLiteWindow.TryRetargetLiveFullControllerGeneratedAssetsForTesting(_ctx.Comp, vendorizedDir),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: setup should retarget live FullController references to vendorized assets before attached-return rollback validation.");
            AssertLiveFullControllerReferencesUnderPrefix(vendorizedDir, "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState setup should leave live FullController references on vendorized assets before return rollback validation.");

            using (ASMLiteGeneratedAssetMirrorService.PushFailurePointForTesting(ASMLiteGeneratedAssetMirrorTestFailurePoint.DuringVendorizedFolderDelete))
            {
                var result = ASMLiteLifecycleTransactionService.ExecuteAttachedReturnToPackageManaged(_ctx.Comp, _ctx.AvDesc);
                Assert.IsFalse(result.Success,
                    "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: attached return should fail closed when vendorized-folder delete staging is injected to fail.");
                Assert.AreEqual(ASMLiteLifecycleTransactionStage.Execute, result.FailedStage,
                    "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: delete-stage failure should surface as an execute-stage transaction failure.");
                Assert.IsTrue(result.RollbackAttempted,
                    "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: attached return should attempt rollback after delete-stage failure.");
                Assert.AreEqual(ASMLiteInstallationState.Vendorized, result.RollbackState,
                    "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: attached return rollback state should resolve back to Vendorized.");
            }

            Assert.IsTrue(_ctx.Comp.useVendorizedGeneratedAssets,
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should preserve vendorized mode on the attached component after delete-stage failure.");
            Assert.AreEqual(vendorizedDir, _ctx.Comp.vendorizedGeneratedAssetsPath,
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should preserve the tracked vendorized generated-assets path after delete-stage failure.");
            Assert.IsTrue(AssetDatabase.IsValidFolder(vendorizedDir),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should restore the vendorized generated-assets folder after delete-stage failure.");
            Assert.AreEqual(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams),
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionParameters)?.Replace('\\', '/'),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should restore avatar expression parameters back to vendorized assets after delete-stage failure.");
            Assert.AreEqual(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu),
                AssetDatabase.GetAssetPath(_ctx.AvDesc.expressionsMenu)?.Replace('\\', '/'),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should restore avatar expressions menu back to vendorized assets after delete-stage failure.");

            int rollbackFxIndex = FindFxLayerIndex(_ctx.AvDesc);
            Assert.GreaterOrEqual(rollbackFxIndex, 0,
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: expected avatar FX layer after delete-stage rollback.");
            Assert.AreEqual(vendorizedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController),
                AssetDatabase.GetAssetPath(_ctx.AvDesc.baseAnimationLayers[rollbackFxIndex].animatorController)?.Replace('\\', '/'),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should restore avatar FX controller back to vendorized assets after delete-stage failure.");
            AssertLiveFullControllerReferencesUnderPrefix(vendorizedDir,
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: rollback should restore live FullController references back to vendorized assets after delete-stage failure.");
            Assert.AreEqual(ASMLiteInstallationState.Vendorized,
                ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, _ctx.Comp),
                "ReturnAttachedVendorizedToPackageManaged_DeleteFailure_RollsBackToVendorizedState: attached avatar should resolve to Vendorized state after delete-stage rollback completes.");
        }

        [Test, Category("Integration")]
        public void DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab()
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
                    "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: setup should leave the avatar detached before recovery failure injection.");
                Assert.AreEqual(ASMLiteInstallationState.Detached,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: setup should classify the detached avatar as Detached before recovery failure injection.");

                using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachedRecoveryVerify))
                {
                    var result = ASMLiteLifecycleTransactionService.ExecuteDetachedReturnToPackageManagedRecovery(
                        _ctx.AvDesc,
                        pendingSnapshot);

                    Assert.IsFalse(result.Success,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery should fail closed when verify-stage failure is injected after reattachment completes.");
                    Assert.AreEqual(ASMLiteLifecycleTransactionStage.Verify, result.FailedStage,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: injected detached recovery failure should surface as a verify-stage transaction failure.");
                    Assert.IsTrue(result.CleanupAttempted,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery should record that cleanup ran before the reattach attempt.");
                    Assert.IsTrue(result.CleanupSucceeded,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery should report cleanup success even when later verify-stage recovery fails.");
                    Assert.IsTrue(result.ReattachAttempted,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery should record the reattach attempt before verify-stage failure.");
                    Assert.IsFalse(result.ReattachSucceeded,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery should report reattach failure after verify-stage failure destroys the partial prefab.");
                    Assert.AreEqual(ASMLiteInstallationState.NotInstalled, result.RecoveredState,
                        "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery should report the best-effort recovered state after tearing down the partial prefab.");
                }

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery verify failure should not leave a partial ASM-Lite prefab attached.");
                Assert.AreEqual(ASMLiteInstallationState.NotInstalled,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "DetachedRecovery_VerifyFailure_ReportsBestEffortStateWithoutLeavingPartialPrefab: detached recovery verify failure should clean direct-delivery runtime markers instead of leaving the avatar half-restored.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test, Category("Integration")]
        public void DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry()
        {
            const string legacyFromPath = "Settings Manager";
            const string legacyToPath = "Tools/DetachedRetry/Settings Manager";

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                _ctx.Comp.useCustomInstallPath = false;
                _ctx.Comp.customInstallPath = string.Empty;
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.RebuildForAutomation();
                var pendingSnapshot = ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot(_ctx.Comp);
                window.DetachForAutomation();

                var legacyMoveMenuRoot = new GameObject("DetachedRecoveryLegacyMoveMenu");
                legacyMoveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);
                var legacyMoveMenu = legacyMoveMenuRoot.AddComponent<VF.Model.VRCFury>();
                legacyMoveMenu.content = new VF.Model.Feature.MoveMenuItem
                {
                    fromPath = legacyFromPath,
                    toPath = legacyToPath,
                };

                Assert.AreEqual(1, CountMatchingMoveMenuHelpers(_ctx.AvDesc, legacyFromPath, legacyToPath),
                    "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: setup should leave exactly one matching legacy MoveMenu helper on the detached avatar before recovery failure injection.");
                Assert.IsNull(_ctx.AvDesc.transform.Find("ASM-Lite Install Path Routing"),
                    "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: setup should rely on the legacy MoveMenu helper rather than the package-managed routing helper.");
                Assert.AreEqual(ASMLiteInstallationState.Detached,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: setup should classify the avatar as Detached before pre-finalize recovery failure injection.");

                using (ASMLiteLifecycleTransactionService.PushFailurePointForTesting(ASMLiteLifecycleTransactionTestFailurePoint.BeforeDetachedRecoveryRoutingFinalize))
                {
                    var result = ASMLiteLifecycleTransactionService.ExecuteDetachedReturnToPackageManagedRecovery(
                        _ctx.AvDesc,
                        pendingSnapshot);

                    Assert.IsFalse(result.Success,
                        "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: detached recovery should fail closed when a pre-finalize recovery failure is injected after legacy MoveMenu adoption begins.");
                    Assert.AreEqual(ASMLiteLifecycleTransactionStage.Execute, result.FailedStage,
                        "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: the injected pre-finalize recovery failure should surface as an execute-stage transaction failure.");
                    Assert.IsTrue(result.InstallPathAdoptionAttempted,
                        "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: detached recovery should record that install-path adoption was attempted before the injected pre-finalize failure.");
                }

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: pre-finalize recovery failure should not leave a partial ASM-Lite prefab attached.");
                Assert.AreEqual(1, CountMatchingMoveMenuHelpers(_ctx.AvDesc, legacyFromPath, legacyToPath),
                    "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: detached recovery must preserve the legacy MoveMenu continuity helper when recovery fails before finalization succeeds.");
                Assert.AreEqual(ASMLiteInstallationState.NotInstalled,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "DetachedRecovery_PreFinalizeFailure_PreservesLegacyMoveMenuHelperForRetry: pre-finalize recovery failure should still report the cleaned best-effort state after tearing down the partial prefab.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test, Category("Integration")]
        public void VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName()
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
                    firstMirror?.Message ?? "VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName: expected first vendorized mirror stage to succeed.");
                Assert.IsTrue(secondMirror != null && secondMirror.Success,
                    secondMirror?.Message ?? "VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName: expected second vendorized mirror stage to succeed.");
                Assert.AreNotEqual(firstMirror.TargetPath, secondMirror.TargetPath,
                    "VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName: distinct avatars that share the same GameObject name must not collide onto the same vendorized GeneratedAssets folder.");
                Assert.AreNotEqual(
                    Path.GetDirectoryName(firstMirror.TargetPath)?.Replace('\\', '/'),
                    Path.GetDirectoryName(secondMirror.TargetPath)?.Replace('\\', '/'),
                    "VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName: distinct avatars that share the same GameObject name must receive separate vendorized avatar folders.");
                Assert.IsTrue(AssetDatabase.IsValidFolder(firstMirror.TargetPath),
                    $"VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName: expected first vendorized GeneratedAssets folder '{firstMirror.TargetPath}' to exist.");
                Assert.IsTrue(AssetDatabase.IsValidFolder(secondMirror.TargetPath),
                    $"VendorizedMirror_UsesDistinctFoldersForDistinctAvatarsWithSameName: expected second vendorized GeneratedAssets folder '{secondMirror.TargetPath}' to exist.");
            }
            finally
            {
                DeleteVendorizedAvatarFolderIfPresent(firstMirror?.TargetPath);
                DeleteVendorizedAvatarFolderIfPresent(secondMirror?.TargetPath);
                Object.DestroyImmediate(otherAvatarGo);
            }
        }
        private sealed class PackageGeneratedAssetsSnapshot
        {
            private readonly string _rootFolder;
            private readonly bool _rootFolderExisted;
            private readonly byte[] _rootMetaBytes;
            private readonly Dictionary<string, byte[]> _files;
            private readonly HashSet<string> _directories;

            private PackageGeneratedAssetsSnapshot(
                string rootFolder,
                bool rootFolderExisted,
                byte[] rootMetaBytes,
                Dictionary<string, byte[]> files,
                HashSet<string> directories)
            {
                _rootFolder = rootFolder;
                _rootFolderExisted = rootFolderExisted;
                _rootMetaBytes = rootMetaBytes;
                _files = files;
                _directories = directories;
            }

            public static PackageGeneratedAssetsSnapshot Capture(string rootFolder)
            {
                var files = new Dictionary<string, byte[]>(System.StringComparer.Ordinal);
                var directories = new HashSet<string>(System.StringComparer.Ordinal) { string.Empty };
                bool rootFolderExisted = Directory.Exists(rootFolder);
                string rootMetaPath = rootFolder + ".meta";
                byte[] rootMetaBytes = File.Exists(rootMetaPath) ? File.ReadAllBytes(rootMetaPath) : null;

                if (rootFolderExisted)
                {
                    foreach (string directory in Directory.GetDirectories(rootFolder, "*", SearchOption.AllDirectories))
                    {
                        directories.Add(ToRelativePath(rootFolder, directory));
                    }

                    foreach (string file in Directory.GetFiles(rootFolder, "*", SearchOption.AllDirectories))
                    {
                        files[ToRelativePath(rootFolder, file)] = File.ReadAllBytes(file);
                    }
                }

                return new PackageGeneratedAssetsSnapshot(rootFolder, rootFolderExisted, rootMetaBytes, files, directories);
            }

            public void Restore()
            {
                string rootMetaPath = _rootFolder + ".meta";

                if (!_rootFolderExisted)
                {
                    if (Directory.Exists(_rootFolder))
                        Directory.Delete(_rootFolder, recursive: true);
                    if (File.Exists(rootMetaPath))
                        File.Delete(rootMetaPath);
                    AssetDatabase.Refresh();
                    return;
                }

                if (!Directory.Exists(_rootFolder))
                    Directory.CreateDirectory(_rootFolder);

                if (_rootMetaBytes != null)
                    File.WriteAllBytes(rootMetaPath, _rootMetaBytes);
                else if (File.Exists(rootMetaPath))
                    File.Delete(rootMetaPath);

                foreach (string file in Directory.GetFiles(_rootFolder, "*", SearchOption.AllDirectories))
                {
                    string relativePath = ToRelativePath(_rootFolder, file);
                    if (!_files.ContainsKey(relativePath))
                        File.Delete(file);
                }

                foreach (string relativeDirectory in _directories.OrderBy(path => path.Length))
                {
                    string directoryPath = string.IsNullOrEmpty(relativeDirectory)
                        ? _rootFolder
                        : Path.Combine(_rootFolder, relativeDirectory.Replace('/', Path.DirectorySeparatorChar));
                    if (!Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);
                }

                foreach (var file in _files)
                {
                    string filePath = Path.Combine(_rootFolder, file.Key.Replace('/', Path.DirectorySeparatorChar));
                    string directoryPath = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
                        Directory.CreateDirectory(directoryPath);
                    File.WriteAllBytes(filePath, file.Value);
                }

                foreach (string directory in Directory.GetDirectories(_rootFolder, "*", SearchOption.AllDirectories)
                    .OrderByDescending(path => path.Length))
                {
                    string relativePath = ToRelativePath(_rootFolder, directory);
                    if (!_directories.Contains(relativePath) && Directory.GetFileSystemEntries(directory).Length == 0)
                        Directory.Delete(directory);
                }

                AssetDatabase.Refresh();
            }

            private static string ToRelativePath(string rootFolder, string path)
            {
                string root = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                string fullPath = Path.GetFullPath(path);
                if (fullPath.StartsWith(root, System.StringComparison.Ordinal))
                    return fullPath.Substring(root.Length).Replace('\\', '/');
                return path.Replace('\\', '/');
            }
        }


    }
}
