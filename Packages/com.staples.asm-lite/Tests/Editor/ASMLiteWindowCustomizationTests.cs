using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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
