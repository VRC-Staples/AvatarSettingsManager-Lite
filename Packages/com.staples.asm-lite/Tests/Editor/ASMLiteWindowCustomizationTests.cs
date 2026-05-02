using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace VF.Model.Feature
{
    [System.Serializable]
    internal class FullControllerLikeParamsRef
    {
        public VRCExpressionParameters objRef;
        public string id;
    }

    [System.Serializable]
    internal class FullControllerLikePrmsEntry
    {
        public FullControllerLikeParamsRef parameters = new FullControllerLikeParamsRef();
    }

    [System.Serializable]
    internal class FullControllerLike
    {
        public FullControllerLikePrmsEntry[] prms;
    }

    [System.Serializable]
    internal class MoveMenuItem
    {
        public string fromPath;
        public string toPath;
    }
}

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowCustomizationTests
    {
        private AsmLiteTestContext _ctx;
        private AsmLiteTestContext _ctxAlt;

        [SetUp]
        public void SetUp()
        {
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            _ctxAlt = ASMLiteTestFixtures.CreateTestAvatar();
            _ctxAlt.AvatarGo.name = "TestAvatarAlt";
            Selection.activeGameObject = null;
            ASMLiteTestFixtures.ResetGeneratedExprParams();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeGameObject = null;
            ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
            ASMLiteTestFixtures.TearDownTestAvatar(_ctxAlt?.AvatarGo);
            _ctx = null;
            _ctxAlt = null;
        }

        [Test]
        public void SelectingAvatar_LoadsPersistedCustomizationFromComponent()
        {
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Reopen Root  ";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "  ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { " HatVisible ", "", "HatVisible", "Mood" };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.AreSame(_ctx.AvDesc, snapshot.SelectedAvatar);
                Assert.IsTrue(snapshot.UseCustomRootIcon);
                Assert.IsTrue(snapshot.UseCustomRootName);
                Assert.IsTrue(snapshot.UseCustomInstallPath);
                Assert.IsTrue(snapshot.UseParameterExclusions);
                Assert.AreEqual("Reopen Root", snapshot.CustomRootName);
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath);
                CollectionAssert.AreEqual(new[] { "HatVisible", "Mood" }, snapshot.ExcludedParameterNames);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingDifferentAvatar_RefreshesCustomizationSnapshot()
        {
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "Primary";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "Packages/Primary";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { "PrimaryParam" };

            _ctxAlt.Comp.useCustomRootIcon = false;
            _ctxAlt.Comp.useCustomRootName = true;
            _ctxAlt.Comp.customRootName = "  Alternate Root  ";
            _ctxAlt.Comp.useCustomInstallPath = true;
            _ctxAlt.Comp.customInstallPath = "   ";
            _ctxAlt.Comp.useParameterExclusions = true;
            _ctxAlt.Comp.excludedParameterNames = new[] { "Alt", "Alt", " Mood " };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.SelectAvatarForAutomation(_ctxAlt.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.AreSame(_ctxAlt.AvDesc, snapshot.SelectedAvatar);
                Assert.IsFalse(snapshot.UseCustomRootIcon);
                Assert.IsTrue(snapshot.UseCustomRootName);
                Assert.AreEqual("Alternate Root", snapshot.CustomRootName);
                Assert.IsTrue(snapshot.UseCustomInstallPath);
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath);
                CollectionAssert.AreEqual(new[] { "Alt", "Mood" }, snapshot.ExcludedParameterNames);
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [TestCase(1)]
        [TestCase(5)]
        [TestCase(8)]
        public void SetSlotCountForAutomation_UpdatesPendingCustomizationSnapshot(int slotCount)
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                window.SetSlotCountForAutomation(slotCount);

                var snapshot = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.AreEqual(slotCount, snapshot.SlotCount,
                    "The automation slot-count seam should immediately surface min, max, and non-default middle values through the pending customization snapshot.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void PendingCustomizationSnapshotForAutomation_ReportsSelectedAvatarSlotCount()
        {
            _ctx.Comp.slotCount = 7;

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                var snapshot = window.GetPendingCustomizationSnapshotForAutomation();
                Assert.AreEqual(7, snapshot.SlotCount,
                    "Selecting an attached avatar should copy the component slot count into the pending automation snapshot contract.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void VisibleParameterBackupOptions_IncludeAssignedPrefabToggleGlobals_PreBake()
        {
            var limbRoot = new GameObject("AvatarLimbScaling");
            limbRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var arms = new GameObject("Arms");
            arms.transform.SetParent(limbRoot.transform, false);

            var vf = arms.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = true,
                globalParam = "AvatarLimbScaling_Arms",
                name = "Avatar Limb Scaling/Arms",
                menuPath = "",
            };

            string[] backable = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.Contains(backable, "AvatarLimbScaling_Arms",
                "Assigned VRCFury globals under nested prefab-style hierarchy should remain visible in the parameter backup checklist before bake.");
        }

        [Test]
        public void VisibleParameterBackupOptions_PreferDeterministicToggleAlias_OverLegacySourceName()
        {
            const string legacySource = "VF300_Clothing/Rezz";

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = legacySource,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            var vf = _ctx.AvatarGo.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.Toggle
            {
                useGlobalParam = false,
                globalParam = legacySource,
                menuPath = "Clothing/Rezz",
                name = "Rezz",
            };

            string[] backable = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

            string deterministic = ASMLite.Editor.ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                "Clothing/Rezz",
                _ctx.AvatarGo.name,
                new HashSet<string>(StringComparer.Ordinal));

            CollectionAssert.DoesNotContain(backable, legacySource,
                "The parameter backup checklist should hide stale legacy toggle names when ASM-Lite will rebind that toggle to a deterministic alias.");
            CollectionAssert.Contains(backable, deterministic,
                "The parameter backup checklist should show the deterministic alias that ASM-Lite will actually back up after enrollment.");
        }

        [Test]
        public void VisibleParameterBackupOptions_IncludeVrcFuryReferencedParameterAssets_PreBake()
        {
            var mediaRoot = new GameObject("Media");
            mediaRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = mediaRoot.AddComponent<VF.Model.VRCFury>();
            var referencedParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            referencedParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = "VRCOSC/Media/Play",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "VRCOSC/Media/Volume",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = false,
                    networkSynced = false,
                },
            };

            try
            {
                vf.content = new VF.Model.Feature.FullControllerLike
                {
                    prms = new[]
                    {
                        new VF.Model.Feature.FullControllerLikePrmsEntry
                        {
                            parameters = new VF.Model.Feature.FullControllerLikeParamsRef
                            {
                                objRef = referencedParams,
                                id = string.Empty,
                            },
                        },
                    },
                };

                string[] backable = ASMLite.Editor.ASMLiteWindow.GetVisibleParameterBackupOptionsForTesting(_ctx.AvDesc);

                CollectionAssert.Contains(backable, "VRCOSC/Media/Play",
                    "Referenced parameter assets should contribute visible parameter backup options before bake.");
                CollectionAssert.Contains(backable, "VRCOSC/Media/Volume",
                    "Supported parameter types from referenced parameter assets should remain visible before bake.");
            }
            finally
            {
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void VisibleInstallPathOptions_ReflectMoveMenuDestinations()
        {
            var rootMenu = CreateTempMenuAsset("MoveMenuRoot");
            var userSubmenu = CreateTempMenuAsset("MoveMenuUserSubmenu");
            _ctx.AvDesc.expressionsMenu = rootMenu;
            rootMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Unrelated",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = userSubmenu,
                },
            };
            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Source Bucket",
                toPath = "Destination/Source Bucket",
            };

            string[] paths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.DoesNotContain(paths, "Source Bucket",
                "The install-path picker should not offer stale move-menu source paths.");
            CollectionAssert.DoesNotContain(paths, "Source Bucket/Submenu",
                "The install-path picker should not offer stale descendants under a moved source path.");
            CollectionAssert.Contains(paths, "Destination",
                "The install-path picker should offer the destination parent created by a move-menu remap.");
            CollectionAssert.Contains(paths, "Destination/Source Bucket",
                "The install-path picker should offer the remapped destination path.");
            CollectionAssert.Contains(paths, "Unrelated",
                "The install-path picker should keep unrelated user submenu paths visible.");
        }

        [Test]
        public void VisibleInstallPathOptions_IncludeMoveMenuDestinationHierarchy()
        {
            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            const string destinationPath = "Root Bucket/Feature Node/Leaf Group";
            var expectedPrefixes = new[]
            {
                "Root Bucket",
                "Root Bucket/Feature Node",
                "Root Bucket/Feature Node/Leaf Group",
            };

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Legacy Bucket/Feature Node/Leaf Group",
                toPath = destinationPath,
            };

            string[] paths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            foreach (var expected in expectedPrefixes)
            {
                CollectionAssert.Contains(paths, expected,
                    "The install-path picker should expose each parent segment of a move-menu destination hierarchy.");
            }
        }

        [Test]
        public void VisibleInstallPathOptions_ExcludeAsmLitePresetsBranchAcrossRootNameReloads()
        {
            var rootMenu = CreateTempMenuAsset("RootMenu");
            _ctx.AvDesc.expressionsMenu = rootMenu;

            var userSubmenu = CreateTempMenuAsset("UserSubmenu");
            var nestedSubmenu = CreateTempMenuAsset("NestedSubmenu");
            Assert.IsNotNull(userSubmenu,
                "Regression setup requires a persisted user submenu asset.");
            Assert.IsNotNull(nestedSubmenu,
                "Regression setup requires a persisted nested submenu asset.");
            userSubmenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Hats",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = nestedSubmenu,
                },
            };
            nestedSubmenu.controls = new List<VRCExpressionsMenu.Control>();
            EditorUtility.SetDirty(userSubmenu);
            EditorUtility.SetDirty(nestedSubmenu);
            AssetDatabase.SaveAssets();

            var asmLitePresetsMenu = LoadAsmLitePresetsMenu();
            rootMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Creator Settings",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = asmLitePresetsMenu,
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Accessories",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = userSubmenu,
                },
            };
            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            string[] firstPaths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.DoesNotContain(firstPaths, "Creator Settings",
                "ASM-Lite's injected presets branch must not be offered as a custom install destination when the root menu has a custom name.");
            CollectionAssert.Contains(firstPaths, "Accessories",
                "Non-ASM-Lite submenu roots should remain available as install destinations.");
            CollectionAssert.Contains(firstPaths, "Accessories/Hats",
                "Non-ASM-Lite submenu descendants should remain available as install destinations.");

            rootMenu.controls[0] = new VRCExpressionsMenu.Control
            {
                name = "Settings Manager",
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = asmLitePresetsMenu,
            };
            EditorUtility.SetDirty(rootMenu);
            AssetDatabase.SaveAssets();

            string[] reloadedPaths = ASMLite.Editor.ASMLiteWindow.GetVisibleInstallPathOptionsForTesting(_ctx.AvDesc);

            CollectionAssert.DoesNotContain(reloadedPaths, "Creator Settings",
                "Reloaded install-path options must not retain stale custom ASM-Lite root names after the root name is reset.");
            CollectionAssert.DoesNotContain(reloadedPaths, "Settings Manager",
                "Reloaded install-path options must also exclude the default ASM-Lite root branch itself.");
            CollectionAssert.AreEquivalent(new[] { "Accessories", "Accessories/Hats" }, reloadedPaths,
                "Reloaded install-path options should contain only real user menu paths after ASM-Lite branches are filtered out.");
        }

        [Test]
        public void SelectingAvatar_AdoptsMoveMenuInstallPathAndRemovesMoveComponent()
        {
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = string.Empty;
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = string.Empty;

            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Settings Manager",
                toPath = "Tools/Settings Manager",
            };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                Assert.IsTrue(_ctx.Comp.useCustomInstallPath,
                    "Move-menu migration should enable custom install path on the ASM-Lite component.");
                Assert.AreEqual("Tools", _ctx.Comp.customInstallPath,
                    "Move-menu migration should adopt destination parent as install prefix.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsTrue(snapshot.UseCustomInstallPath,
                    "Visible customization state should mirror adopted install-path enablement.");
                Assert.AreEqual("Tools", snapshot.CustomInstallPath,
                    "Visible customization state should mirror the adopted install-path prefix.");

                int remainingMoveComponents = _ctx.AvatarGo
                    .GetComponentsInChildren<VF.Model.VRCFury>(true)
                    .Count(c => c != null
                        && c.content is VF.Model.Feature.MoveMenuItem move
                        && string.Equals(move.fromPath, "Settings Manager", StringComparison.Ordinal)
                        && string.Equals(move.toPath, "Tools/Settings Manager", StringComparison.Ordinal));

                Assert.AreEqual(0, remainingMoveComponents,
                    "Move-menu migration should remove the consumed MoveMenuItem component to avoid duplicate routing.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void SelectingAvatar_DoesNotAdoptMalformedMoveMenuDestinationOrRemoveHelper()
        {
            _ctx.Comp.useCustomInstallPath = false;
            _ctx.Comp.customInstallPath = string.Empty;
            _ctx.Comp.useCustomRootName = false;
            _ctx.Comp.customRootName = string.Empty;

            var moveMenuRoot = new GameObject("MalformedMoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Settings Manager",
                toPath = "Tools/BrokenDestination",
            };

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);

                Assert.IsFalse(_ctx.Comp.useCustomInstallPath,
                    "Malformed move-menu destinations must fail closed without enabling custom install path on the ASM-Lite component.");
                Assert.AreEqual(string.Empty, _ctx.Comp.customInstallPath,
                    "Malformed move-menu destinations must fail closed without writing an adopted install prefix.");

                var snapshot = window.GetPendingCustomizationSnapshotForTesting();
                Assert.IsFalse(snapshot.UseCustomInstallPath,
                    "Visible customization state should remain unchanged when move-menu adoption rejects a malformed destination.");
                Assert.AreEqual(string.Empty, snapshot.CustomInstallPath,
                    "Visible customization state should not surface any adopted prefix when move-menu adoption rejects a malformed destination.");
                Assert.IsTrue(moveMenuRoot.TryGetComponent<VF.Model.VRCFury>(out _),
                    "Malformed move-menu destinations must not remove the legacy helper when install-prefix resolution fails.");
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        private static VRCExpressionsMenu CreateTempMenuAsset(string name)
        {
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"Assets/ASMLiteTests_Temp/{name}.asset");
            var menu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menu.controls = new List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menu, assetPath);
            AssetDatabase.SaveAssets();

            var persistedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(assetPath);
            Assert.IsNotNull(persistedMenu,
                $"Expected temporary VRCExpressionsMenu asset at '{assetPath}'.");
            return persistedMenu;
        }

        private static VRCExpressionsMenu LoadAsmLitePresetsMenu()
        {
            string presetsMenuPath = $"{ASMLite.Editor.ASMLiteAssetPaths.GeneratedDir}/ASMLite_Presets_Menu.asset";
            var menu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(presetsMenuPath);
            Assert.IsNotNull(menu,
                $"Expected generated ASM-Lite presets menu at '{presetsMenuPath}'.");
            return menu;
        }
    }
}
