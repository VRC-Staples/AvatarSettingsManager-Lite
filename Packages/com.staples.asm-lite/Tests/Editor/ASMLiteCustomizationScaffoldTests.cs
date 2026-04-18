using System;
using System.Reflection;
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
        public void CommitInstallPathDraftIfBlurred_NoComponent_DoesNotThrowAndPreservesPendingPath()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_pendingCustomInstallPath", "Tools/Custom");

                Assert.DoesNotThrow(() => InvokePrivate(window, "CommitInstallPathDraftIfBlurred", null),
                    "Install-path draft commits should fail closed when no live ASMLite component exists, so enabling custom install path before prefab enrollment cannot throw.");
                Assert.AreEqual("Tools/Custom", GetPrivateField<string>(window, "_pendingCustomInstallPath"),
                    "Pending install-path text should remain intact when no live component exists yet.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ResolveVisibleInstallPathDraft_UsesLiveComponentValue_WhenFieldIsNotFocused()
        {
            _ctx.Comp.customInstallPath = "Tools/Live";

            var resolved = InvokePrivateStatic<string>(typeof(ASMLite.Editor.ASMLiteWindow),
                "ResolveVisibleInstallPathDraft", _ctx.Comp, "Tools/StaleDraft", false);

            Assert.AreEqual("Tools/Live", resolved,
                "When the install-path field is not actively being edited, the visible draft should resync from the live component so tree picks and external updates show up in the text box immediately.");
        }

        [Test]
        public void ResolveVisibleInstallPathDraft_PreservesPendingDraft_WhenFieldIsFocused()
        {
            _ctx.Comp.customInstallPath = "Tools/Live";

            var resolved = InvokePrivateStatic<string>(typeof(ASMLite.Editor.ASMLiteWindow),
                "ResolveVisibleInstallPathDraft", _ctx.Comp, "Tools/InProgressDraft", true);

            Assert.AreEqual("Tools/InProgressDraft", resolved,
                "While the install-path text field is focused, the visible draft should stay on the in-progress user edit instead of snapping back to the last serialized component value.");
        }

        [Test]
        public void RetargetLiveFullControllerGeneratedAssets_RestoresWildcardGlobalParams()
        {
            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.GreaterOrEqual(buildResult, 0,
                $"Build should succeed before retargeting live FullController assets. result={buildResult}.");

            Assert.IsTrue(ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(_ctx.Comp.gameObject, _ctx.Comp, "Scaffold Retarget Setup"),
                "Setup should create a live VF.Model.VRCFury FullController payload before retarget validation.");

            var vf = _ctx.Comp.GetComponent<VF.Model.VRCFury>();
            Assert.IsNotNull(vf,
                "Setup should leave a VF.Model.VRCFury component on the ASM-Lite object.");

            var beforeSo = new SerializedObject(vf);
            beforeSo.Update();
            var beforeGlobalParams = beforeSo.FindProperty("content.globalParams");
            Assert.IsNotNull(beforeGlobalParams,
                "Expected serialized FullController globalParams array at content.globalParams before retarget validation.");
            beforeGlobalParams.arraySize = 0;
            beforeSo.ApplyModifiedPropertiesWithoutUndo();

            bool retargeted = InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                "TryRetargetLiveFullControllerGeneratedAssets", _ctx.Comp, ASMLiteAssetPaths.GeneratedDir);
            Assert.IsTrue(retargeted,
                "Retargeting should succeed for package-managed generated assets when the live FullController payload exists.");

            var afterSo = new SerializedObject(vf);
            afterSo.Update();
            var afterGlobalParams = afterSo.FindProperty("content.globalParams");
            Assert.IsNotNull(afterGlobalParams,
                "Expected serialized FullController globalParams array at content.globalParams after retarget validation.");
            Assert.AreEqual(1, afterGlobalParams.arraySize,
                "Retargeting should restore wildcard global-parameter enrollment so menu button triggers continue resolving against the generated FX controller.");
            Assert.AreEqual("*", afterSo.FindProperty("content.globalParams.Array.data[0]")?.stringValue,
                "Retargeting should restore the wildcard global-parameter registration consumed by VRCFury FullController merges.");
        }

        [Test]
        public void ApplyInstallPathSelection_WithComponent_SyncsPendingDraftAndSerializedValue()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_pendingCustomInstallPath", "Tools/StaleDraft");
                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = string.Empty;

                InvokePrivate(window, "ApplyInstallPathSelection", _ctx.Comp, "Tools/Selected");

                Assert.AreEqual("Tools/Selected", _ctx.Comp.customInstallPath,
                    "Tree selection should still commit the chosen install path onto the live component when ASM-Lite is already attached.");
                Assert.AreEqual("Tools/Selected", GetPrivateField<string>(window, "_pendingCustomInstallPath"),
                    "Tree selection should also sync the pending text draft so the install-path field immediately reflects the clicked menu path.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
        }

        [Test]
        public void ShouldRenderWheelPreview_RepaintOnly()
        {
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldRenderWheelPreview", EventType.Repaint),
                "Wheel preview rendering should run during repaint events so the UI can draw normally.");
            Assert.IsFalse(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldRenderWheelPreview", EventType.Layout),
                "Wheel preview should skip heavy cache/icon work during layout events to avoid typing lag from repeated editor redraws.");
            Assert.IsFalse(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldRenderWheelPreview", EventType.KeyDown),
                "Wheel preview should also skip heavy redraw work on key events while the user is typing in text fields.");
        }

        [Test]
        public void ShouldDelayTextFieldCommit_CustomNamingControls_Only()
        {
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDelayTextFieldCommit", "asm_name_root"),
                "Root name edits should use delayed commit so the preview and scaffold state do not churn on every keystroke.");
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDelayTextFieldCommit", "asm_name_preset_1"),
                "Preset name edits should use delayed commit so slot preview labels stay stable until the field blurs.");
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDelayTextFieldCommit", "asm_name_preset_pending_3"),
                "Pending preset name edits should also use delayed commit before a live component exists.");
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDelayTextFieldCommit", "asm_name_save_pending"),
                "Action label edits should use delayed commit so the preview does not update on every character.");
            Assert.IsFalse(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDelayTextFieldCommit", "asm_install_path"),
                "Install path edits should keep immediate updates because the delayed-commit optimization is scoped only to custom naming fields.");
            Assert.IsFalse(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDelayTextFieldCommit", string.Empty),
                "Blank control names should fail closed and never opt into delayed commit behavior.");
        }

        [Test]
        public void ShouldDeferImmediateInstallPathRefresh_CustomizeContextsOnly()
        {
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDeferImmediateInstallPathRefresh", "Customize Toggle"),
                "Customize toggle updates should defer live FullController refresh so VRCFury editor debug does not rebuild against incomplete anim-object state.");
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDeferImmediateInstallPathRefresh", "Customize Text"),
                "Customize text commits should defer live FullController refresh until Bake/Build.");
            Assert.IsTrue(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDeferImmediateInstallPathRefresh", "Customize Tree"),
                "Install-path tree picks should also defer live FullController refresh in the editor window.");
            Assert.IsFalse(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDeferImmediateInstallPathRefresh", "Bake"),
                "Bake-time refresh must remain enabled because generated assets and FullController wiring are committed there.");
            Assert.IsFalse(InvokePrivateStatic<bool>(typeof(ASMLite.Editor.ASMLiteWindow),
                    "ShouldDeferImmediateInstallPathRefresh", string.Empty),
                "Blank context labels should fail closed and not suppress refresh behavior outside known customize flows.");
        }

        [Test]
        public void RemovePrefab_RemovesAsmLiteVrcFuryArtifactsFromAvatar()
        {
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);

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

                Assert.DoesNotThrow(() => InvokePrivate(window, "RemovePrefab", _ctx.Comp),
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
        public void PreviewActionIconMode_UsesComponentGateInsteadOfStalePendingState_WhenComponentPresent()
        {
            _ctx.Comp.useCustomSlotIcons = false;
            _ctx.Comp.actionIconMode = ActionIconMode.Custom;

            var resolved = InvokePrivateStatic<ActionIconMode>(typeof(ASMLite.Editor.ASMLiteWindow),
                "ResolvePreviewActionIconMode", _ctx.Comp, true, ActionIconMode.Custom);

            Assert.AreEqual(ActionIconMode.Default, resolved,
                "Preview action icon mode should fail closed to Default when the live component has custom icons disabled, even if pending scaffold state is stale Custom.");
        }

        [Test]
        public void PreviewActionIconMode_UsesComponentActionMode_WhenComponentCustomIconsAreEnabled()
        {
            _ctx.Comp.useCustomSlotIcons = true;
            _ctx.Comp.actionIconMode = ActionIconMode.Custom;

            var resolved = InvokePrivateStatic<ActionIconMode>(typeof(ASMLite.Editor.ASMLiteWindow),
                "ResolvePreviewActionIconMode", _ctx.Comp, false, ActionIconMode.Default);

            Assert.AreEqual(ActionIconMode.Custom, resolved,
                "Preview action icon mode should honor the live component action icon mode when custom icons are enabled, regardless of stale pending scaffold state.");
        }

        [Test]
        public void PreviewActionIconMode_UsesPendingState_WhenNoComponentIsPresent()
        {
            var resolved = InvokePrivateStatic<ActionIconMode>(typeof(ASMLite.Editor.ASMLiteWindow),
                "ResolvePreviewActionIconMode", null, true, ActionIconMode.Custom);

            Assert.AreEqual(ActionIconMode.Custom, resolved,
                "Preview action icon mode should continue using pending scaffold state before a live component exists.");
        }

        [Test]
        public void PreviewActionIconMode_FailsClosedToDefault_WhenPendingCustomIconsAreDisabled()
        {
            var resolved = InvokePrivateStatic<ActionIconMode>(typeof(ASMLite.Editor.ASMLiteWindow),
                "ResolvePreviewActionIconMode", null, false, ActionIconMode.Custom);

            Assert.AreEqual(ActionIconMode.Default, resolved,
                "Preview action icon mode should fail closed to Default before a live component exists when pending custom icons are disabled, even if pending action mode is stale Custom.");
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

                var wiredVf = component.GetComponent<VF.Model.VRCFury>();
                Assert.IsNotNull(wiredVf, "Add prefab should keep a live VF.Model.VRCFury component for bake wiring.");
                Assert.AreEqual(string.Empty, ReadSerializedMenuPrefix(wiredVf),
                    "Add prefab + immediate bake should normalize null install path to legacy empty FullController prefix.");

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

                var rebuiltVf = rebuilt.GetComponent<VF.Model.VRCFury>();
                Assert.IsNotNull(rebuiltVf, "Migration rebuild should preserve VF.Model.VRCFury delivery component.");
                Assert.AreEqual(string.Empty, ReadSerializedMenuPrefix(rebuiltVf),
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
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);

                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "  Avatars/Primary  ";
                InvokePrivate(window, "BakeAssets", _ctx.Comp);
                Assert.AreEqual("Avatars/Primary", ReadSerializedMenuPrefix(vf),
                    "Enabled custom install path should serialize trimmed FullController prefix on live bake.");

                _ctx.Comp.useCustomInstallPath = false;
                _ctx.Comp.customInstallPath = "Avatars/ShouldNotPersist";
                InvokePrivate(window, "BakeAssets", _ctx.Comp);
                Assert.AreEqual(string.Empty, ReadSerializedMenuPrefix(vf),
                    "Disabled custom install path should reset FullController prefix to legacy empty value.");

                _ctx.Comp.useCustomInstallPath = true;
                _ctx.Comp.customInstallPath = "   ";
                InvokePrivate(window, "BakeAssets", _ctx.Comp);
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
            var staleVf = _ctx.Comp.GetComponent<VF.Model.VRCFury>();
            if (staleVf != null)
                UnityEngine.Object.DestroyImmediate(staleVf);

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                SetPrivateField(window, "_selectedAvatar", _ctx.AvDesc);

                LogAssert.Expect(LogType.Warning,
                    $"[ASM-Lite] Bake: VF.Model.VRCFury component was missing on '{_ctx.Comp.gameObject.name}'. Live FullController wiring was repaired automatically.");

                InvokePrivate(window, "BakeAssets", _ctx.Comp);

                var repairedVf = _ctx.Comp.GetComponent<VF.Model.VRCFury>();
                Assert.IsNotNull(repairedVf,
                    "Bake should auto-heal missing VF.Model.VRCFury before refreshing install-path routing.");
                Assert.AreEqual("Avatars/MissingVf", ReadSerializedMenuPrefix(repairedVf),
                    "Bake auto-heal should still apply normalized install-path prefix deterministically.");
                Assert.GreaterOrEqual(GetPrivateField<int>(window, "_discoveredParamCount"), 0,
                    "Bake should continue into rebuild after auto-healing missing VF.Model.VRCFury.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }
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

            object[] invocationArgs = args ?? new object[] { null };
            method.Invoke(target, invocationArgs);
        }

        private static T InvokePrivateStatic<T>(Type targetType, string methodName, params object[] args)
        {
            var method = targetType.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, $"Missing private static method '{methodName}' on {targetType.Name}.");
            return (T)method.Invoke(null, args);
        }
    }
}
