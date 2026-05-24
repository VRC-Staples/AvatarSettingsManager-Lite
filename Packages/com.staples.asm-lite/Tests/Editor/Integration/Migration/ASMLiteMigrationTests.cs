using NUnit.Framework;
using System.Linq;
using UnityEngine;
using ASMLite.Editor;

namespace VF.Model
{
    // Test-local stub that intentionally matches migration predicate full name.
    // Shared across tests that need VF.Model.VRCFury without hard dependency on VRCFury internals.
    internal class VRCFury : MonoBehaviour
    {
        [SerializeReference] public object content;
        [SerializeReference] public object[] features;
        public string untouchedMarker = "keep";
    }
}

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Migration integration invariants for stale VRCFury component cleanup.
    /// These tests exercise MigrateStaleVRCFuryComponents() end-to-end against real
    /// ASMLite fixture graphs and exact VF.Model.VRCFury full-name matching semantics,
    /// verifying duplicate collapse while preserving one delivery component.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteMigrationTests
    {
        private const string SuiteName = nameof(ASMLiteMigrationTests);
        private static ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _testGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.SourceAssetsSnapshot _sourceMigrationAssetsBaseline;
        private AsmLiteTestContext _ctx;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            s_classGeneratedAssetsBaseline = null;
        }

        [SetUp]
        public void SetUp()
        {
            s_classGeneratedAssetsBaseline?.Restore();
            ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _sourceMigrationAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(
                SuiteName,
                SourceMigrationFixturePaths());
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "fixture creation returned null context.");
            Assert.IsNotNull(_ctx.Comp, "fixture did not create ASMLiteComponent.");
            Assert.IsNotNull(_ctx.AvDesc, "fixture did not create VRCAvatarDescriptor.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
                _sourceMigrationAssetsBaseline?.Restore();
                _sourceMigrationAssetsBaseline?.AssertUnchanged(SuiteName);
            }
            finally
            {
                _sourceMigrationAssetsBaseline?.Restore();
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
                _testGeneratedAssetsBaseline = null;
                _sourceMigrationAssetsBaseline = null;
                _ctx = null;
            }
        }

        private static string[] SourceMigrationFixturePaths()
            => new[]
            {
                ASMLiteAssetPaths.Prefab,
            };

        private static string FullNameOrNull(Component c)
            => c == null ? "<null>" : c.GetType().FullName ?? "<null>";

        private static void AddAvatarParam(AsmLiteTestContext ctx, string name, VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType type, float defaultValue = 0f)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter[0];
            var updated = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = type,
                defaultValue = defaultValue,
                saved = true,
                networkSynced = true,
            };
            ctx.ParamsAsset.parameters = updated;
            UnityEditor.EditorUtility.SetDirty(ctx.ParamsAsset);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        private static void AddUserOwnedArtifacts(AsmLiteTestContext ctx)
        {
            ctx.Ctrl.AddLayer(new UnityEditor.Animations.AnimatorControllerLayer
            {
                name = "User_CustomLayer",
                defaultWeight = 1f,
                stateMachine = new UnityEditor.Animations.AnimatorStateMachine { name = "UserStateMachine" }
            });
            ctx.Ctrl.AddParameter("User_CustomParam", UnityEngine.AnimatorControllerParameterType.Float);

            var existingExpr = ctx.AvDesc.expressionParameters.parameters ?? new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter[0];
            var mergedExpr = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter[existingExpr.Length + 1];
            existingExpr.CopyTo(mergedExpr, 0);
            mergedExpr[existingExpr.Length] = new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
            {
                name = "UserExprParam",
                valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float,
                defaultValue = 0.5f,
                saved = true,
                networkSynced = true,
            };
            ctx.AvDesc.expressionParameters.parameters = mergedExpr;
            ctx.AvDesc.expressionsMenu.controls.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control
            {
                name = "User Control",
                type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Button,
            });

            UnityEditor.EditorUtility.SetDirty(ctx.Ctrl);
            UnityEditor.EditorUtility.SetDirty(ctx.AvDesc.expressionParameters);
            UnityEditor.EditorUtility.SetDirty(ctx.AvDesc.expressionsMenu);
            UnityEditor.AssetDatabase.SaveAssets();
        }

        // ── Migration duplicate cleanup

        [Test, Category("Integration")]
        public void Migration_CollapsesDuplicateVRCFuryTypeNameMatches()
        {
            var first = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var second = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            Assert.IsNotNull(first, "setup failure, failed adding first VF.Model.VRCFury stub component.");
            Assert.IsNotNull(second, "setup failure, failed adding second VF.Model.VRCFury stub component.");

            string fullName = first.GetType().FullName;
            Assert.AreEqual("VF.Model.VRCFury", fullName,
                $"malformed test setup, expected exact full name VF.Model.VRCFury but got '{fullName ?? "<null>"}'.");

            int beforeTargets = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c != null && c.GetType().FullName == "VF.Model.VRCFury")
                    beforeTargets++;
            }
            Assert.Greater(beforeTargets, 1,
                $"setup failure, expected at least two stale VRCFury components before migration. beforeTargets={beforeTargets}.");

            ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp);

            int afterTargets = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c != null && c.GetType().FullName == "VF.Model.VRCFury")
                    afterTargets++;
            }

            Assert.AreEqual(1, afterTargets,
                $"migration must collapse duplicate components and preserve one VF delivery component. beforeTargets={beforeTargets}, afterTargets={afterTargets}.");
        }

        // ── Migration non-target preservation

        [Test, Category("Integration")]
        public void Migration_PreservesNonTargetComponentsInMixedSet()
        {
            var staleA = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var staleB = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var preserved = _ctx.Comp.gameObject.AddComponent<BoxCollider>();

            Assert.IsNotNull(staleA, "setup failure, failed adding first VF.Model.VRCFury stub component.");
            Assert.IsNotNull(staleB, "setup failure, failed adding second VF.Model.VRCFury stub component.");
            Assert.IsNotNull(preserved, "setup failure, failed adding non-target BoxCollider component.");
            Assert.AreEqual("VF.Model.VRCFury", staleA.GetType().FullName,
                $"malformed test setup, expected exact stale full name VF.Model.VRCFury but got '{FullNameOrNull(staleA)}'.");
            Assert.AreEqual(typeof(BoxCollider).FullName, preserved.GetType().FullName,
                $"malformed test setup, expected BoxCollider full name '{typeof(BoxCollider).FullName}' but got '{FullNameOrNull(preserved)}'.");

            ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp);

            int staleCount = 0;
            int boxCount = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c == null) continue;
                string name = c.GetType().FullName;
                if (name == "VF.Model.VRCFury") staleCount++;
                if (name == typeof(BoxCollider).FullName) boxCount++;
            }

            Assert.AreEqual(1, staleCount,
                $"migration must collapse stale VF.Model.VRCFury duplicates down to one preserved component. staleCount={staleCount}.");
            Assert.GreaterOrEqual(boxCount, 1,
                $"migration must preserve non-target components. expected BoxCollider survivor, boxCount={boxCount}.");
        }

        // ── Null component no-op

        [Test, Category("Integration")]
        public void Migration_NullComponent_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => ASMLiteBuilder.MigrateStaleVRCFuryComponents(null),
                "migration must no-op when ASMLiteComponent is null.");
        }

        // ── Idempotent migration cleanup

        [Test, Category("Integration")]
        public void Migration_RepeatedCalls_AreIdempotentAndDoNotOverRemove()
        {
            var staleA = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var staleB = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var preserved = _ctx.Comp.gameObject.AddComponent<BoxCollider>();

            Assert.IsNotNull(staleA, "setup failure, failed adding first VF.Model.VRCFury stub component.");
            Assert.IsNotNull(staleB, "setup failure, failed adding second VF.Model.VRCFury stub component.");
            Assert.IsNotNull(preserved, "setup failure, failed adding non-target BoxCollider component.");
            Assert.AreEqual("VF.Model.VRCFury", staleA.GetType().FullName,
                $"malformed test setup, expected exact stale full name VF.Model.VRCFury but got '{FullNameOrNull(staleA)}'.");

            Assert.DoesNotThrow(() => { ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp); },
                "first migration invocation must not throw.");
            Assert.DoesNotThrow(() => { ASMLiteBuilder.MigrateStaleVRCFuryComponents(_ctx.Comp); },
                "second migration invocation must not throw (idempotency).");

            int staleCount = 0;
            int boxCount = 0;
            foreach (var c in _ctx.Comp.gameObject.GetComponents<Component>())
            {
                if (c == null) continue;
                string name = c.GetType().FullName;
                if (name == "VF.Model.VRCFury") staleCount++;
                if (name == typeof(BoxCollider).FullName) boxCount++;
            }

            Assert.AreEqual(1, staleCount,
                $"idempotent migration must preserve exactly one VF.Model.VRCFury delivery component after repeated calls. staleCount={staleCount}.");
            Assert.GreaterOrEqual(boxCount, 1,
                $"repeated migration calls must not remove non-target components. boxCount={boxCount}.");
        }

        // ── Rebuild prep selective cleanup

        [Test, Category("Integration")]
        public void RebuildPrep_MixedLegacyState_RemovesOnlyObsoleteArtifacts()
        {
            var staleA = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var staleB = _ctx.Comp.gameObject.AddComponent<VF.Model.VRCFury>();
            var preservedCollider = _ctx.Comp.gameObject.AddComponent<BoxCollider>();
            Assert.IsNotNull(staleA, "setup failure, failed adding first VF.Model.VRCFury stub component.");
            Assert.IsNotNull(staleB, "setup failure, failed adding second VF.Model.VRCFury stub component.");
            Assert.IsNotNull(preservedCollider, "setup failure, failed adding preserved BoxCollider component.");

            _ctx.Ctrl.AddLayer(new UnityEditor.Animations.AnimatorControllerLayer
            {
                name = "ASMLite_LegacyInjected",
                defaultWeight = 1f,
                stateMachine = new UnityEditor.Animations.AnimatorStateMachine { name = "LegacySM" }
            });
            _ctx.Ctrl.AddLayer(new UnityEditor.Animations.AnimatorControllerLayer
            {
                name = "User_CustomLayer",
                defaultWeight = 1f,
                stateMachine = new UnityEditor.Animations.AnimatorStateMachine { name = "UserSM" }
            });
            _ctx.Ctrl.AddParameter("ASMLite_Ctrl", UnityEngine.AnimatorControllerParameterType.Int);
            _ctx.Ctrl.AddParameter("ASMLite_Bak_S1_Legacy", UnityEngine.AnimatorControllerParameterType.Float);
            _ctx.Ctrl.AddParameter("User_CustomParam", UnityEngine.AnimatorControllerParameterType.Float);

            _ctx.AvDesc.expressionParameters.parameters = new[]
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = "ASMLite_Ctrl",
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Int,
                    saved = false,
                    networkSynced = false,
                },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = "ASMLite_Bak_S1_Legacy",
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float,
                    saved = true,
                    networkSynced = false,
                },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter
                {
                    name = "UserExprParam",
                    valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.5f,
                    saved = true,
                    networkSynced = true,
                }
            };

            _ctx.AvDesc.expressionsMenu.controls.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control
            {
                name = "Settings Manager",
                type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.SubMenu,
            });
            _ctx.AvDesc.expressionsMenu.controls.Add(new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control
            {
                name = "User Control",
                type = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionsMenu.Control.ControlType.Button,
            });

            var first = ASMLiteBuilder.PrepareRevertedDeliveryRebuild(_ctx.Comp);
            Assert.IsTrue(first.AvatarDescriptorFound, "rebuild prep should find avatar descriptor for fixture avatar.");
            Assert.AreEqual(1, first.StaleVrcFuryRemoved,
                $"rebuild prep should collapse stale VRCFury duplicates down to one removal. removed={first.StaleVrcFuryRemoved}.");
            Assert.Greater(first.Cleanup.FxLayersRemoved, 0,
                $"rebuild prep should remove legacy ASMLite FX layers. removed={first.Cleanup.FxLayersRemoved}.");
            Assert.Greater(first.Cleanup.FxParamsRemoved, 0,
                $"rebuild prep should remove legacy ASMLite FX params. removed={first.Cleanup.FxParamsRemoved}.");
            Assert.Greater(first.Cleanup.ExprParamsRemoved, 0,
                $"rebuild prep should remove legacy ASMLite expression params. removed={first.Cleanup.ExprParamsRemoved}.");
            Assert.Greater(first.Cleanup.MenuControlsRemoved, 0,
                $"rebuild prep should remove legacy Settings Manager controls. removed={first.Cleanup.MenuControlsRemoved}.");

            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "rebuild prep must preserve user-owned FX layers.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "rebuild prep must preserve user-owned FX params.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "rebuild prep must preserve user-owned expression params.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "rebuild prep must preserve user-owned menu controls.");

            var second = ASMLiteBuilder.PrepareRevertedDeliveryRebuild(_ctx.Comp);
            Assert.AreEqual(0, second.StaleVrcFuryRemoved,
                $"repeated rebuild prep should be a no-op for stale VRCFury removals. removed={second.StaleVrcFuryRemoved}.");
            Assert.AreEqual(0, second.Cleanup.FxLayersRemoved,
                $"repeated rebuild prep should be a no-op for FX layer cleanup. removed={second.Cleanup.FxLayersRemoved}.");
            Assert.AreEqual(0, second.Cleanup.FxParamsRemoved,
                $"repeated rebuild prep should be a no-op for FX parameter cleanup. removed={second.Cleanup.FxParamsRemoved}.");
            Assert.AreEqual(0, second.Cleanup.ExprParamsRemoved,
                $"repeated rebuild prep should be a no-op for expression parameter cleanup. removed={second.Cleanup.ExprParamsRemoved}.");
            Assert.AreEqual(0, second.Cleanup.MenuControlsRemoved,
                $"repeated rebuild prep should be a no-op for menu cleanup. removed={second.Cleanup.MenuControlsRemoved}.");

            var outcome = ASMLiteMigrationContinuityService.CreateOutcomeReport(
                default,
                first,
                default);
            StringAssert.Contains("cleaned ASM-Lite-owned state", outcome.ToCompactSummary(),
                "compact migration outcome reporting should summarize selective cleanup counts in one transport object.");
            StringAssert.Contains("staleVrcFury=1", outcome.ToCompactSummary(),
                "compact migration outcome reporting should include collapsed stale VRCFury counts.");
        }

        [Test, Category("Integration")]
        public void DetachRecoverCycle_PreservesLegacyContinuityAndSelectiveCleanup()
        {
            const string deterministicSource = "ASM_VF_Menu_Cape__TestAvatar_ASMLite";
            const string unmatchedLegacy = "ASMLite_Bak_S1_LegacyBrokered_Menu/Cape";

            AddAvatarParam(_ctx, deterministicSource, VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float, 0.5f);
            AddUserOwnedArtifacts(_ctx);

            var generatedExprParams = UnityEditor.AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedExprParams, "generated expression-parameters asset must exist for detach/recover continuity setup.");
            generatedExprParams.parameters = new[]
            {
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = unmatchedLegacy, valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float, defaultValue = 0.1f, saved = true, networkSynced = true },
                new VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.Parameter { name = "ASMLite_Bak_S_", valueType = VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters.ValueType.Float, defaultValue = 0.2f, saved = true, networkSynced = true },
            };
            UnityEditor.EditorUtility.SetDirty(generatedExprParams);
            UnityEditor.AssetDatabase.SaveAssets();

            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(_ctx.AvDesc);
                window.DetachForAutomation();

                Assert.IsNull(_ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true),
                    "setup should leave the avatar detached before recovery.");
                Assert.AreEqual(ASMLiteInstallationState.Detached,
                    ASMLiteWindow.GetAsmLiteToolState(_ctx.AvDesc, null),
                    "setup should classify the avatar as Detached before recovery.");

                window.ReturnToPackageManagedForAutomation();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(window);
            }

            var recovered = _ctx.AvDesc.GetComponentInChildren<ASMLiteComponent>(true);
            Assert.IsNotNull(recovered,
                "detached recovery should reattach ASM-Lite in package-managed mode.");
            Assert.IsTrue(_ctx.Ctrl.layers.Any(l => l.name == "User_CustomLayer"),
                "detached recovery cleanup should preserve user-owned FX layers.");
            Assert.IsTrue(_ctx.Ctrl.parameters.Any(p => p.name == "User_CustomParam"),
                "detached recovery cleanup should preserve user-owned FX parameters.");
            Assert.IsTrue(_ctx.AvDesc.expressionParameters.parameters.Any(p => p != null && p.name == "UserExprParam"),
                "detached recovery cleanup should preserve user-owned expression parameters.");
            Assert.IsTrue(_ctx.AvDesc.expressionsMenu.controls.Any(c => c != null && c.name == "User Control"),
                "detached recovery cleanup should preserve user-owned menu controls.");

            int buildResult = ASMLiteBuilder.Build(recovered);
            Assert.GreaterOrEqual(buildResult, 0,
                $"build should succeed after detach/recover continuity validation. result={buildResult}.");

            generatedExprParams = UnityEditor.AssetDatabase.LoadAssetAtPath<VRC.SDK3.Avatars.ScriptableObjects.VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedExprParams, "generated expression-parameters asset must exist after detach/recover rebuild.");
            Assert.IsTrue(generatedExprParams.parameters.Any(p => p != null && p.name == unmatchedLegacy),
                "unmatched but well-formed legacy backup aliases must remain preserved after detach/recover rebuild.");
            Assert.IsFalse(generatedExprParams.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S_"),
                "malformed legacy backup aliases must remain excluded after detach/recover rebuild.");

            var report = ASMLiteBuilder.GetLatestLegacyAliasContinuityReport();
            Assert.GreaterOrEqual(report.UnmatchedCount, 1,
                "detach/recover rebuild should preserve unmatched legacy continuity reporting.");
        }
    }
}
