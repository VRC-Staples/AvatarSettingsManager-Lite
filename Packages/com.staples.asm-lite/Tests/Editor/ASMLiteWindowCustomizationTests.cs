using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
        public void SyncPendingSlotCountFromAvatar_NoPrefab_PreservesPendingCustomization()
        {
            if (_ctx.Comp != null)
                Object.DestroyImmediate(_ctx.Comp.gameObject);

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);
                SetPrivateField(window, "_pendingUseCustomRootIcon", true);
                SetPrivateField(window, "_pendingUseCustomRootName", true);
                SetPrivateField(window, "_pendingCustomRootName", "Keep Me");
                SetPrivateField(window, "_pendingUseCustomInstallPath", true);
                SetPrivateField(window, "_pendingCustomInstallPath", "Packages/Keep");
                SetPrivateField(window, "_pendingUseParameterExclusions", true);
                SetPrivateField(window, "_pendingExcludedParameterNames", new[] { "A", "B" });

                InvokePrivate(window, "SyncPendingSlotCountFromAvatar");

                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomRootIcon"));
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomRootName"));
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomInstallPath"));
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseParameterExclusions"));
                Assert.AreEqual("Keep Me", GetPrivateField<string>(window, "_pendingCustomRootName"));
                Assert.AreEqual("Packages/Keep", GetPrivateField<string>(window, "_pendingCustomInstallPath"));
                CollectionAssert.AreEqual(new[] { "A", "B" }, GetPrivateField<string[]>(window, "_pendingExcludedParameterNames"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ReopenWindow_SameAvatar_ReloadsPersistedCustomizationFromComponent()
        {
            _ctx.Comp.useCustomRootIcon = true;
            _ctx.Comp.customRootIcon = null;
            _ctx.Comp.useCustomRootName = true;
            _ctx.Comp.customRootName = "  Reopen Root  ";
            _ctx.Comp.useCustomInstallPath = true;
            _ctx.Comp.customInstallPath = "  ";
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = new[] { " HatVisible ", "", "HatVisible", "Mood" };

            var firstWindow = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(firstWindow, "_selectedAvatar", _ctx.AvDesc);
                InvokePrivate(firstWindow, "SyncPendingSlotCountFromAvatar");
            }
            finally
            {
                Object.DestroyImmediate(firstWindow);
            }

            var reopenedWindow = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(reopenedWindow, "_selectedAvatar", _ctx.AvDesc);
                InvokePrivate(reopenedWindow, "SyncPendingSlotCountFromAvatar");

                Assert.IsTrue(GetPrivateField<bool>(reopenedWindow, "_pendingUseCustomRootIcon"));
                Assert.IsTrue(GetPrivateField<bool>(reopenedWindow, "_pendingUseCustomRootName"));
                Assert.IsTrue(GetPrivateField<bool>(reopenedWindow, "_pendingUseCustomInstallPath"));
                Assert.IsTrue(GetPrivateField<bool>(reopenedWindow, "_pendingUseParameterExclusions"));
                Assert.AreEqual("Reopen Root", GetPrivateField<string>(reopenedWindow, "_pendingCustomRootName"));
                Assert.AreEqual(string.Empty, GetPrivateField<string>(reopenedWindow, "_pendingCustomInstallPath"));
                CollectionAssert.AreEqual(new[] { "HatVisible", "Mood" }, GetPrivateField<string[]>(reopenedWindow, "_pendingExcludedParameterNames"));
            }
            finally
            {
                Object.DestroyImmediate(reopenedWindow);
            }
        }

        [Test]
        public void OnSelectionChange_SwitchesAvatarAndRefreshesPendingCustomization()
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
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);
                InvokePrivate(window, "SyncPendingSlotCountFromAvatar");

                Selection.activeGameObject = _ctxAlt.AvatarGo;
                InvokePrivate(window, "OnSelectionChange");

                Assert.AreSame(_ctxAlt.AvDesc, GetPrivateField<VRC.SDK3.Avatars.Components.VRCAvatarDescriptor>(window, "_selectedAvatar"));
                Assert.IsFalse(GetPrivateField<bool>(window, "_pendingUseCustomRootIcon"));
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomRootName"));
                Assert.AreEqual("Alternate Root", GetPrivateField<string>(window, "_pendingCustomRootName"));
                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomInstallPath"));
                Assert.AreEqual(string.Empty, GetPrivateField<string>(window, "_pendingCustomInstallPath"));
                CollectionAssert.AreEqual(new[] { "Alt", "Mood" }, GetPrivateField<string[]>(window, "_pendingExcludedParameterNames"));
            }
            finally
            {
                Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void GetBackableParameterNames_IncludesAssignedPrefabToggleGlobals_PreBake()
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

            string[] backable = InvokePrivateStatic<string[]>(
                typeof(ASMLite.Editor.ASMLiteWindow),
                "GetBackableParameterNames",
                _ctx.AvDesc);

            CollectionAssert.Contains(backable, "AvatarLimbScaling_Arms",
                "Assigned VRCFury globals under nested prefab-style hierarchy should be backable pre-bake.");
        }

        [Test]
        public void GetBackableParameterNames_IncludesVrcFuryReferencedParameterAssets_PreBake()
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

                string[] backable = InvokePrivateStatic<string[]>(
                    typeof(ASMLite.Editor.ASMLiteWindow),
                    "GetBackableParameterNames",
                    _ctx.AvDesc);

                CollectionAssert.Contains(backable, "VRCOSC/Media/Play",
                    "VRCFury FullController prms references should be included in backable names pre-bake.");
                CollectionAssert.Contains(backable, "VRCOSC/Media/Volume",
                    "All supported value types from referenced parameter assets should be included pre-bake.");
            }
            finally
            {
                Object.DestroyImmediate(referencedParams);
            }
        }

        [Test]
        public void GetVrcFuryMoveMenuPathRemaps_ExtractsFromAndToPaths()
        {
            var moveMenuRoot = new GameObject("MoveMenuFeature");
            moveMenuRoot.transform.SetParent(_ctx.AvatarGo.transform, false);

            var vf = moveMenuRoot.AddComponent<VF.Model.VRCFury>();
            vf.content = new VF.Model.Feature.MoveMenuItem
            {
                fromPath = "Source Bucket",
                toPath = "Destination/Source Bucket",
            };

            var remaps = InvokePrivateStatic<Dictionary<string, string>>(
                typeof(ASMLite.Editor.ASMLiteWindow),
                "GetVrcFuryMoveMenuPathRemaps",
                _ctx.AvDesc);

            Assert.IsTrue(remaps.TryGetValue("Source Bucket", out var destination),
                "Move-menu remaps should include source path from serialized fromPath.");
            Assert.AreEqual("Destination/Source Bucket", destination,
                "Move-menu remaps should preserve destination path from serialized toPath.");
        }

        [Test]
        public void ApplyInstallPathMoveRemaps_RemovesSourceAndAddsDestinationHierarchy()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal)
            {
                "Source Bucket",
                "Source Bucket/Submenu",
                "Unrelated",
            };

            var remaps = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Source Bucket"] = "Destination/Source Bucket",
            };

            InvokePrivateStatic<object>(
                typeof(ASMLite.Editor.ASMLiteWindow),
                "ApplyInstallPathMoveRemaps",
                paths,
                remaps);

            CollectionAssert.DoesNotContain(paths, "Source Bucket",
                "Source install path should be suppressed when move remap exists.");
            CollectionAssert.DoesNotContain(paths, "Source Bucket/Submenu",
                "Source subtree should be suppressed when move remap exists.");
            CollectionAssert.Contains(paths, "Destination",
                "Destination parent should be available for install path selection.");
            CollectionAssert.Contains(paths, "Destination/Source Bucket",
                "Destination path should be available for install path selection.");
            CollectionAssert.Contains(paths, "Unrelated",
                "Non-moved paths should remain available.");
        }

        [Test]
        public void GetVrcFuryMenuPrefixes_IncludesMoveMenuToPathDestinations()
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

            string[] prefixes = InvokePrivateStatic<string[]>(
                typeof(ASMLite.Editor.ASMLiteWindow),
                "GetVrcFuryMenuPrefixes",
                _ctx.AvDesc);

            foreach (var expected in expectedPrefixes)
            {
                CollectionAssert.Contains(prefixes, expected,
                    "Move-menu destination path should contribute each parent segment and full path to install path options.");
            }
        }

        [Test]
        public void SyncPendingSlotCountFromAvatar_AdoptsMoveMenuInstallPathAndRemovesMoveComponent()
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
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);
                InvokePrivate(window, "SyncPendingSlotCountFromAvatar");

                Assert.IsTrue(_ctx.Comp.useCustomInstallPath,
                    "Move-menu migration should enable custom install path on the ASM-Lite component.");
                Assert.AreEqual("Tools", _ctx.Comp.customInstallPath,
                    "Move-menu migration should adopt destination parent as install prefix.");

                Assert.IsTrue(GetPrivateField<bool>(window, "_pendingUseCustomInstallPath"),
                    "Pending state should mirror adopted install-path enablement.");
                Assert.AreEqual("Tools", GetPrivateField<string>(window, "_pendingCustomInstallPath"),
                    "Pending state should mirror adopted install-path prefix.");

                int remainingMoveComponents = _ctx.AvatarGo
                    .GetComponentsInChildren<VF.Model.VRCFury>(true)
                    .Count(c => c != null
                        && c.content is VF.Model.Feature.MoveMenuItem move
                        && string.Equals(move.fromPath, "Settings Manager", StringComparison.Ordinal)
                        && string.Equals(move.toPath, "Tools/Settings Manager", StringComparison.Ordinal));

                Assert.AreEqual(0, remainingMoveComponents,
                    "Move-menu migration should remove consumed MoveMenuItem component to avoid duplicate routing.");
            }
            finally
            {
                Object.DestroyImmediate(window);
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

        private static T InvokePrivateStatic<T>(System.Type targetType, string methodName, params object[] args)
        {
            var method = targetType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Missing private static method '{methodName}' on {targetType.Name}.");
            return (T)method.Invoke(null, args);
        }
    }
}
