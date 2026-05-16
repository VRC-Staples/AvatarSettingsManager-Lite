using System;
using System.Reflection;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// VF delivery pipeline assertions that intentionally avoid direct descriptor-injection checks.
    /// These tests prove Build() writes generated assets for VRCFury pickup while leaving fixture
    /// descriptor surfaces untouched.
    /// </summary>
    [TestFixture]
    [Category("Headless")]
    [Category("Integration")]
    public class ASMLiteVRCFuryPipelineTests
    {
        private const string SuiteName = nameof(ASMLiteVRCFuryPipelineTests);
        private static ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot s_classGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.GeneratedAssetsSnapshot _testGeneratedAssetsBaseline;
        private ASMLiteGeneratedAssetTestIsolation.SourceAssetsSnapshot _sourceVRCFuryAssetsBaseline;
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
            ASMLiteToggleNameBroker.ResetLatestEnrollmentStateForTests();
            ASMLiteToggleNameBroker.ClearPendingRestoreState();
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _testGeneratedAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureGeneratedAssets(SuiteName);
            _sourceVRCFuryAssetsBaseline = ASMLiteGeneratedAssetTestIsolation.CaptureSourceAssets(
                SuiteName,
                SourceVRCFuryFixturePaths());
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "fixture creation returned null context.");
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                ASMLiteToggleNameBroker.ClearPendingRestoreState();
                ASMLiteToggleNameBroker.ResetLatestEnrollmentStateForTests();
                ASMLiteTestFixtures.TearDownTestAvatar(_ctx?.AvatarGo);
                _sourceVRCFuryAssetsBaseline?.AssertUnchanged(SuiteName);
            }
            finally
            {
                (_testGeneratedAssetsBaseline ?? s_classGeneratedAssetsBaseline)?.Restore();
                ASMLiteGeneratedAssetTestIsolation.DeleteTempFolder();
                _testGeneratedAssetsBaseline = null;
                _sourceVRCFuryAssetsBaseline = null;
                _ctx = null;
            }
        }

        private static string[] SourceVRCFuryFixturePaths()
            => new[]
            {
                ASMLiteAssetPaths.Prefab,
            };

        private static object AddVrcFuryToggle(
            GameObject owner,
            bool useGlobalParam,
            string globalParam,
            string menuPath,
            string name,
            bool defaultOn = false,
            bool saved = true)
        {
            var vrcFuryType = FindRequiredType("VF.Model.VRCFury");
            var toggleType = FindRequiredType("VF.Model.Feature.Toggle");
            var vrcFury = owner.AddComponent(vrcFuryType);
            var toggle = Activator.CreateInstance(toggleType);

            SetField(toggle, "useGlobalParam", useGlobalParam);
            SetField(toggle, "globalParam", globalParam ?? string.Empty);
            if (TrySetField(toggle, "menuPath", menuPath ?? string.Empty))
            {
                SetField(toggle, "name", name ?? string.Empty);
            }
            else
            {
                SetField(toggle, "name", CombineVrcFuryMenuPath(menuPath, name));
            }
            SetField(toggle, "defaultOn", defaultOn);
            SetField(toggle, "saved", saved);
            SetField(vrcFury, "content", toggle);
            return toggle;
        }

        private static object AddVrcFuryFullController(GameObject owner, VRCExpressionParameters parameters, params string[] globalParams)
        {
            return AddVrcFuryFullController(owner, parameters, allNonsyncedAreGlobal: false, globalParams: globalParams);
        }

        private static object AddVrcFuryFullController(
            GameObject owner,
            VRCExpressionParameters parameters,
            bool allNonsyncedAreGlobal,
            params string[] globalParams)
        {
            var vrcFuryType = FindRequiredType("VF.Model.VRCFury");
            var fullControllerType = FindRequiredType("VF.Model.Feature.FullController");
            var vrcFury = owner.AddComponent(vrcFuryType);
            var fullController = Activator.CreateInstance(fullControllerType);

            var prmsField = fullControllerType.GetField("prms", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Type paramsEntryType = null;
            if (prmsField != null)
            {
                var args = prmsField.FieldType.GetGenericArguments();
                if (args.Length > 0)
                    paramsEntryType = args[0];
                else if (prmsField.FieldType.IsArray)
                    paramsEntryType = prmsField.FieldType.GetElementType();
            }

            if (paramsEntryType != null)
            {
                var parametersField = paramsEntryType.GetField("parameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(parametersField, "Expected VRCFury FullController.ParamsEntry.parameters field.");
                var paramsEntry = Activator.CreateInstance(paramsEntryType);
                var guidParams = Activator.CreateInstance(parametersField.FieldType);
                SetField(guidParams, "objRef", parameters);
                parametersField.SetValue(paramsEntry, guidParams);

                var prms = prmsField.GetValue(fullController);
                if (prmsField.FieldType.IsArray)
                {
                    var array = Array.CreateInstance(paramsEntryType, 1);
                    array.SetValue(paramsEntry, 0);
                    prmsField.SetValue(fullController, array);
                }
                else
                {
                    ((System.Collections.IList)prms).Add(paramsEntry);
                }
            }
            else
            {
                var legacyParametersField = fullControllerType.GetField("parameters", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(legacyParametersField, "Expected VRCFury FullController parameters field.");
                var guidParams = Activator.CreateInstance(legacyParametersField.FieldType);
                SetField(guidParams, "objRef", parameters);
                legacyParametersField.SetValue(fullController, guidParams);
            }

            var globalsField = fullControllerType.GetField("globalParams", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(globalsField, "Expected VRCFury FullController.globalParams field.");

            var allNonsyncedField = fullControllerType.GetField("allNonsyncedAreGlobal", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (allNonsyncedField != null)
                allNonsyncedField.SetValue(fullController, allNonsyncedAreGlobal);

            if (globalParams != null)
            {
                if (globalsField.FieldType.IsArray)
                {
                    var array = Array.CreateInstance(typeof(string), globalParams.Length);
                    for (int i = 0; i < globalParams.Length; i++)
                        array.SetValue(globalParams[i], i);
                    globalsField.SetValue(fullController, array);
                }
                else
                {
                    var globals = (System.Collections.IList)globalsField.GetValue(fullController);
                    for (int i = 0; i < globalParams.Length; i++)
                        globals.Add(globalParams[i]);
                }
            }

            SetField(vrcFury, "content", fullController);
            return fullController;
        }

        private static Type FindRequiredType(string fullName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                    return type;
            }

            Assert.Ignore($"VRCFury test fixture type '{fullName}' was not found in this Unity project.");
            return null;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected field '{fieldName}' on '{target.GetType().FullName}'.");
            field.SetValue(target, value);
        }

        private static bool TrySetField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return false;
            field.SetValue(target, value);
            return true;
        }

        private static string CombineVrcFuryMenuPath(string menuPath, string name)
        {
            string cleanedMenuPath = (menuPath ?? string.Empty).Trim('/');
            string cleanedName = (name ?? string.Empty).Trim('/');
            if (string.IsNullOrEmpty(cleanedMenuPath))
                return cleanedName;
            if (string.IsNullOrEmpty(cleanedName))
                return cleanedMenuPath;
            return $"{cleanedMenuPath}/{cleanedName}";
        }

        private static object GetField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected field '{fieldName}' on '{target.GetType().FullName}'.");
            return field.GetValue(target);
        }

        private static bool FullControllerGlobalParamsContains(object fullController, string parameterName)
        {
            var globals = GetField(fullController, "globalParams") as System.Collections.IEnumerable;
            Assert.IsNotNull(globals, "Expected VRCFury FullController.globalParams to be enumerable.");

            foreach (var item in globals)
            {
                if (string.Equals(item as string, parameterName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool ReadBoolField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected field '{fieldName}' on '{target.GetType().FullName}'.");
            return (bool)field.GetValue(target);
        }

        private static string ReadStringField(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Expected field '{fieldName}' on '{target.GetType().FullName}'.");
            return (string)field.GetValue(target);
        }

        private static AnimatorController LoadGeneratedController(string aid)
        {
            var generatedCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(generatedCtrl, $"{aid}: generated FX controller must exist.");
            return generatedCtrl;
        }

        private static VRCExpressionParameters LoadGeneratedParams(string aid)
        {
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedExpr, $"{aid}: generated expression params must exist.");
            Assert.IsNotNull(generatedExpr.parameters, $"{aid}: generated expression params list must not be null.");
            return generatedExpr;
        }

        private static VRC_AvatarParameterDriver LoadSlotDriver(string aid, AnimatorController ctrl, string layerName, string stateName)
        {
            var layer = ctrl.layers.FirstOrDefault(l => l.name == layerName);
            Assert.IsNotNull(layer.stateMachine, $"{aid}: expected layer '{layerName}' in generated controller.");

            var state = layer.stateMachine.states.FirstOrDefault(s => s.state.name == stateName).state;
            Assert.IsNotNull(state, $"{aid}: expected state '{stateName}' in layer '{layerName}'.");

            var driver = state.behaviours.OfType<VRC_AvatarParameterDriver>().SingleOrDefault();
            Assert.IsNotNull(driver, $"{aid}: expected one VRCAvatarParameterDriver on state '{stateName}'.");
            return driver;
        }

        [Test, Category("Integration")]
        public void Build_WritesGeneratedAssets_ButDoesNotMutateFixtureDescriptorSurfaces()
        {
            _ctx.Comp.slotCount = 2;
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "FixtureUserParam", VRCExpressionParameters.ValueType.Int);

            int liveFxAsmLayersBefore = _ctx.Ctrl.layers.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxLayer);
            int liveExprAsmBefore = (_ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter);
            int liveMenuSettingsBefore = (_ctx.AvDesc.expressionsMenu.controls ?? new System.Collections.Generic.List<VRCExpressionsMenu.Control>())
                .Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl);

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                $"Build should discover exactly one user parameter. got {buildResult}.");

            int liveFxAsmLayersAfter = _ctx.Ctrl.layers.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxLayer);
            int liveExprAsmAfter = (_ctx.AvDesc.expressionParameters.parameters ?? new VRCExpressionParameters.Parameter[0])
                .Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter);
            int liveMenuSettingsAfter = (_ctx.AvDesc.expressionsMenu.controls ?? new System.Collections.Generic.List<VRCExpressionsMenu.Control>())
                .Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl);

            Assert.AreEqual(liveFxAsmLayersBefore, liveFxAsmLayersAfter,
                $"Build should not inject ASMLite layers into fixture descriptor FX controller. before={liveFxAsmLayersBefore}, after={liveFxAsmLayersAfter}.");
            Assert.AreEqual(liveExprAsmBefore, liveExprAsmAfter,
                $"Build should not inject ASMLite expression params into fixture descriptor asset. before={liveExprAsmBefore}, after={liveExprAsmAfter}.");
            Assert.AreEqual(liveMenuSettingsBefore, liveMenuSettingsAfter,
                $"Build should not inject Settings Manager into fixture descriptor root menu. before={liveMenuSettingsBefore}, after={liveMenuSettingsAfter}.");

            var generatedCtrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            var generatedMenu = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(ASMLiteAssetPaths.Menu);

            Assert.IsNotNull(generatedCtrl, "generated FX controller must exist.");
            Assert.IsNotNull(generatedExpr, "generated expression params must exist.");
            Assert.IsNotNull(generatedMenu, "generated menu must exist.");

            Assert.AreEqual(2, generatedCtrl.layers.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedFxLayer),
                "generated FX controller should carry one ASMLite layer per configured slot.");
            Assert.AreEqual(4,
                generatedExpr.parameters.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedExpressionParameter),
                "generated expression params should contain ASMLite_Ctrl + one Clear-default key + one backup per slot.");
            Assert.AreEqual(1,
                generatedMenu.controls.Count(ASMLiteGeneratedOwnershipPolicy.IsGeneratedRootMenuControl),
                "generated root menu should contain one Settings Manager wrapper.");
        }

        [Test, Category("Integration")]
        public void Regression_StaleFirstUploadSchemaLag_FirstRebuildUsesCurrentDescriptorParamSet()
        {
            _ctx.Comp.slotCount = 1;

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "OldSchemaParam",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                });
            int firstBuild = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, firstBuild,
                $"setup failure, first build should discover one stale param. got {firstBuild}.");

            var beforeCtrl = LoadGeneratedController("stale schema rebuild");
            Assert.IsTrue(beforeCtrl.parameters.Any(p => p.name == "OldSchemaParam"),
                "setup failure, expected stale schema marker in generated FX controller before rebuild.");

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "Brokered_Clothing/Rezz",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            int rebuildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, rebuildResult,
                $"rebuild should discover the current descriptor schema in one pass. got {rebuildResult}.");

            var rebuiltCtrl = LoadGeneratedController("stale schema rebuild");
            var rebuiltExpr = LoadGeneratedParams("stale schema rebuild");

            Assert.IsTrue(rebuiltCtrl.parameters.Any(p => p.name == "Brokered_Clothing/Rezz"),
                "regression guard: first rebuild must include the current brokered parameter in generated FX controller.");
            Assert.IsFalse(rebuiltCtrl.parameters.Any(p => p.name == "OldSchemaParam"),
                "regression guard: first rebuild must not require a second upload cycle to evict stale FX schema names.");
            Assert.IsTrue(rebuiltExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_Brokered_Clothing/Rezz"),
                "regression guard: first rebuild must emit backup key for the current brokered parameter.");
            Assert.IsTrue(rebuiltExpr.parameters.Any(p => p != null && p.name == "ASMLite_Def_Brokered_Clothing/Rezz"),
                "regression guard: first rebuild must emit Clear Preset default key for the current brokered parameter.");
        }

        [Test, Category("Integration")]
        public void Regression_VFPickupDrift_OpaqueVFNamesRemainUnrenamedInGeneratedAssets()
        {
            _ctx.Comp.slotCount = 2;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "Brokered_Clothing/Rezz",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.25f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "Brokered_Menu/Hood",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, buildResult,
                $"setup failure, expected two discovered brokered params. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("opaque source preservation");
            var generatedExpr = LoadGeneratedParams("opaque source preservation");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "Brokered_Clothing/Rezz"),
                "regression guard: generated FX controller must preserve brokered source names exactly.");
            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "Brokered_Menu/Hood"),
                "regression guard: generated FX controller must preserve brokered source names exactly.");
            Assert.IsFalse(generatedCtrl.parameters.Any(p => p.name == "Rezz" || p.name == "Hood"),
                "regression guard: generated FX controller must not rewrite opaque brokered source names.");

            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_Brokered_Clothing/Rezz"),
                "regression guard: generated expression params must preserve brokered backup key naming for slot 1.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S2_Brokered_Menu/Hood"),
                "regression guard: generated expression params must preserve brokered backup key naming for slot 2.");
        }

        [Test, Category("Integration")]
        public void Regression_DuplicateDescriptorParams_DoNotDuplicateGeneratedKeysOrBreakBuild()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "Brokered_Mode/Outfit",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "Brokered_Mode/Outfit",
                    valueType = VRCExpressionParameters.ValueType.Int,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "Brokered_Mode/Accessory",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(3, buildResult,
                $"discovery should still observe all descriptor entries before generation dedupe. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("duplicate descriptor dedupe");
            var generatedExpr = LoadGeneratedParams("duplicate descriptor dedupe");

            int sourceDuplicates = generatedCtrl.parameters.Count(p => p.name == "Brokered_Mode/Outfit");
            int backupDuplicates = generatedExpr.parameters.Count(p => p != null && p.name == "ASMLite_Bak_S1_Brokered_Mode/Outfit");

            Assert.AreEqual(1, sourceDuplicates,
                "regression guard: duplicate descriptor names must be deduped in generated FX source parameter declarations.");
            Assert.AreEqual(1, backupDuplicates,
                "regression guard: duplicate descriptor names must be deduped in generated expression backup keys.");
            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "Brokered_Mode/Accessory"),
                "regression guard: dedupe path must preserve distinct sibling parameters.");
        }

        [Test, Category("Integration")]
        public void Regression_BrokerDeterministicNames_AreConsumedAsOpaqueSourceParams()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = "ASM_VF_Outfit_Hood__Avatar_ASM_Lite",
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                },
                new VRCExpressionParameters.Parameter
                {
                    name = "ASM_VF_Outfit_Hat__Avatar_ASM_Lite",
                    valueType = VRCExpressionParameters.ValueType.Float,
                    defaultValue = 0.5f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(2, buildResult,
                $"build should discover broker-assigned deterministic names as regular source params. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("broker deterministic names");
            var generatedExpr = LoadGeneratedParams("broker deterministic names");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "ASM_VF_Outfit_Hood__Avatar_ASM_Lite"),
                "regression guard: generated FX controller must preserve broker deterministic source names exactly.");
            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == "ASM_VF_Outfit_Hat__Avatar_ASM_Lite"),
                "regression guard: generated FX controller must preserve broker deterministic source names exactly.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_ASM_VF_Outfit_Hood__Avatar_ASM_Lite"),
                "regression guard: generated backup keys must include broker deterministic source names without rewriting.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Bak_S1_ASM_VF_Outfit_Hat__Avatar_ASM_Lite"),
                "regression guard: generated backup keys must include broker deterministic source names without rewriting.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Def_ASM_VF_Outfit_Hood__Avatar_ASM_Lite"),
                "regression guard: generated Clear Preset default keys must include broker deterministic source names without rewriting.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == "ASMLite_Def_ASM_VF_Outfit_Hat__Avatar_ASM_Lite"),
                "regression guard: generated Clear Preset default keys must include broker deterministic source names without rewriting.");
        }

        [Test, Category("Integration")]
        public void Regression_AssignedVrcFuryToggleGlobal_IsBackedUpBeforeVrcFuryBake()
        {
            const string source = "Clothing/Rezz";
            const string backup = "ASMLite_Bak_S1_Clothing/Rezz";
            const string defaultKey = "ASMLite_Def_Clothing/Rezz";

            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx);

            var toggleGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "RezzToggle");
            AddVrcFuryToggle(
                toggleGo,
                useGlobalParam: true,
                globalParam: source,
                menuPath: "Clothing",
                name: "Rezz",
                defaultOn: false,
                saved: true);

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                $"VRCFury assigned toggle globals should be included even before VRCFury writes descriptor expression params. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("assigned VF toggle global");
            var generatedExpr = LoadGeneratedParams("assigned VF toggle global");
            var saveDriver = LoadSlotDriver("assigned VF toggle global", generatedCtrl, "ASMLite_Slot1", "SaveSlot1");
            var loadDriver = LoadSlotDriver("assigned VF toggle global", generatedCtrl, "ASMLite_Slot1", "LoadSlot1");
            var resetDriver = LoadSlotDriver("assigned VF toggle global", generatedCtrl, "ASMLite_Slot1", "ResetSlot1");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == source),
                "regression guard: generated FX controller must declare the VRCFury toggle source parameter.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == backup),
                "regression guard: generated expression params must include a slot backup for the VRCFury toggle source.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == defaultKey),
                "regression guard: generated expression params must include the Clear Preset default key for the VRCFury toggle source.");

            Assert.IsTrue(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == source && p.name == backup),
                "save must copy VRCFury toggle source into the slot backup.");
            Assert.IsTrue(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == backup && p.name == source),
                "load must copy the slot backup back into the VRCFury toggle source.");
            Assert.IsTrue(resetDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == defaultKey && p.name == source),
                "clear preset must restore the VRCFury toggle source default.");
        }

        [Test, Category("Integration")]
        public void Regression_SurfaceVrcFuryToggle_PlannedGlobal_IsGeneratedAndPreprocessEnrolled()
        {
            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx);

            var toggleGo = ASMLiteTestFixtures.CreateChild(_ctx.AvatarGo, "RezzToggle");
            var toggle = AddVrcFuryToggle(
                toggleGo,
                useGlobalParam: false,
                globalParam: string.Empty,
                menuPath: "Clothing",
                name: "Rezz",
                defaultOn: false,
                saved: true);

            string plannedSource = ASMLiteToggleNameBroker
                .DiscoverPlannedToggleExpressionParameters(_ctx.AvatarGo, _ctx.AvDesc)
                .Single()
                .name;
            string backup = $"ASMLite_Bak_S1_{plannedSource}";
            string defaultKey = $"ASMLite_Def_{plannedSource}";

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                $"surface VRCFury toggles should be planned into generated assets before VRCFury bake. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("surface VF toggle global");
            var generatedExpr = LoadGeneratedParams("surface VF toggle global");
            var saveDriver = LoadSlotDriver("surface VF toggle global", generatedCtrl, "ASMLite_Slot1", "SaveSlot1");
            var loadDriver = LoadSlotDriver("surface VF toggle global", generatedCtrl, "ASMLite_Slot1", "LoadSlot1");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == plannedSource),
                "regression guard: generated FX controller must declare the planned VRCFury surface-toggle parameter.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == backup),
                "regression guard: generated expression params must include a slot backup for the planned VRCFury surface-toggle parameter.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == defaultKey),
                "regression guard: generated expression params must include the Clear Preset default key for the planned VRCFury surface-toggle parameter.");
            Assert.IsTrue(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == plannedSource && p.name == backup),
                "save must copy planned VRCFury surface-toggle source into the slot backup.");
            Assert.IsTrue(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == backup && p.name == plannedSource),
                "load must copy the slot backup back into the planned VRCFury surface-toggle source.");

            var callback = new ASMLiteTogglePreprocessAvatarCallback();
            Assert.IsTrue(callback.OnPreprocessAvatar(_ctx.AvatarGo),
                "preprocess enrollment callback should not block VRCFury/SDK preprocessing.");
            Assert.IsTrue(ReadBoolField(toggle, "useGlobalParam"),
                "preprocess enrollment should make the surface toggle consume the planned global parameter before VRCFury bakes.");
            Assert.AreEqual(plannedSource, ReadStringField(toggle, "globalParam"),
                "preprocess enrollment should use the same planned source name that generated assets backed up.");

            var restore = ASMLiteToggleNameBroker.RestorePendingMutations(warnOnNoData: false);
            Assert.AreEqual(1, restore.RestoredCount,
                "preprocess enrollment should leave a restore record for source-avatar cleanup.");
            Assert.IsFalse(ReadBoolField(toggle, "useGlobalParam"),
                "restore should return surface toggle useGlobalParam to its original false value.");
            Assert.AreEqual(string.Empty, ReadStringField(toggle, "globalParam"),
                "restore should return surface toggle globalParam to its original empty value.");
        }

        [Test, Category("Integration")]
        public void Regression_FullControllerGlobalParam_IsBackedUpBeforeVrcFuryBake()
        {
            const string source = "Props/Lollipop";
            const string backupSlot1 = "ASMLite_Bak_S1_Props/Lollipop";
            const string backupSlot2 = "ASMLite_Bak_S2_Props/Lollipop";
            const string defaultKey = "ASMLite_Def_Props/Lollipop";

            _ctx.Comp.slotCount = 2;
            ASMLiteTestFixtures.SetExpressionParams(_ctx);

            var vrcFuryParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            vrcFuryParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = source,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                }
            };

            try
            {
                AddVrcFuryFullController(_ctx.AvatarGo, vrcFuryParams, "*");

                int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
                Assert.AreEqual(1, buildResult,
                    $"VRCFury FullController global expression params should be included before VRCFury writes descriptor expression params. got {buildResult}.");

                var generatedCtrl = LoadGeneratedController("full controller VF global param");
                var generatedExpr = LoadGeneratedParams("full controller VF global param");
                var saveDriver = LoadSlotDriver("full controller VF global param", generatedCtrl, "ASMLite_Slot1", "SaveSlot1");
                var loadSlot2Driver = LoadSlotDriver("full controller VF global param", generatedCtrl, "ASMLite_Slot2", "LoadSlot2");

                Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == source),
                    "regression guard: generated FX controller must declare the VRCFury FullController global source parameter.");
                Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == backupSlot1),
                    "regression guard: generated expression params must include a slot 1 backup for the VRCFury FullController source.");
                Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == backupSlot2),
                    "repro guard: generated expression params must include a slot 2 backup so loading untouched slot 2 resets the lollipop toggle.");
                Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == defaultKey),
                    "regression guard: generated expression params must include the Clear Preset default key for the VRCFury FullController source.");

                Assert.IsTrue(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == source && p.name == backupSlot1),
                    "save must copy VRCFury FullController source into the slot 1 backup.");
                Assert.IsTrue(loadSlot2Driver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == backupSlot2 && p.name == source),
                    "repro guard: loading untouched slot 2 must copy slot 2 backup/default back into the VRCFury FullController source.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vrcFuryParams);
            }
        }

        [Test, Category("Integration")]
        public void Regression_FullControllerAllNonsyncedGlobalParam_DoesNotBackUpListedExpressionParams()
        {
            const string source = "SPS/Lollipop";

            _ctx.Comp.slotCount = 2;
            ASMLiteTestFixtures.SetExpressionParams(_ctx);

            var vrcFuryParams = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            vrcFuryParams.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = source,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = false,
                }
            };

            try
            {
                AddVrcFuryFullController(
                    _ctx.AvatarGo,
                    vrcFuryParams,
                    allNonsyncedAreGlobal: true,
                    globalParams: Array.Empty<string>());

                int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
                Assert.AreEqual(0, buildResult,
                    "VRCFury allNonsyncedAreGlobal does not make parameters listed in FullController.prms global; VRCFury only leaves params absent from prms unrenamed.");

                var generatedCtrl = LoadGeneratedController("full controller nonsynced listed param");
                Assert.IsFalse(generatedCtrl.parameters.Any(p => p.name == source),
                    "regression guard: do not back up a FullController.prms parameter solely because its VRChat networkSynced flag is false.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(vrcFuryParams);
            }
        }

        [Test, Category("Integration")]
        public void Regression_BoolCopyDrivers_PreClearDestinationBeforeCopy()
        {
            const string source = "Menu/BoolToggle";
            const string backup = "ASMLite_Bak_S1_Menu/BoolToggle";
            const string defaultKey = "ASMLite_Def_Menu/BoolToggle";

            _ctx.Comp.slotCount = 1;
            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = source,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                $"setup failure, expected one bool parameter. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("bool copy driver");
            var saveDriver = LoadSlotDriver("bool copy driver", generatedCtrl, "ASMLite_Slot1", "SaveSlot1");
            var loadDriver = LoadSlotDriver("bool copy driver", generatedCtrl, "ASMLite_Slot1", "LoadSlot1");
            var resetDriver = LoadSlotDriver("bool copy driver", generatedCtrl, "ASMLite_Slot1", "ResetSlot1");

            AssertHasPreClearBeforeCopy("bool copy save", saveDriver, source, backup);
            AssertHasPreClearBeforeCopy("bool copy load", loadDriver, backup, source);
            AssertHasPreClearBeforeCopy("bool copy reset backup", resetDriver, defaultKey, backup);
            AssertHasPreClearBeforeCopy("bool copy reset source", resetDriver, defaultKey, source);
        }

        private static void AssertHasPreClearBeforeCopy(string aid, VRC_AvatarParameterDriver driver, string source, string destination)
        {
            int copyIndex = driver.parameters.FindIndex(parameter =>
                parameter.type == VRC_AvatarParameterDriver.ChangeType.Copy
                && parameter.source == source
                && parameter.name == destination);
            Assert.GreaterOrEqual(copyIndex, 0,
                $"{aid}: expected Copy {source} -> {destination}.");

            int preClearIndex = driver.parameters.FindIndex(parameter =>
                parameter.type == VRC_AvatarParameterDriver.ChangeType.Set
                && parameter.name == destination
                && parameter.value == 0f);
            Assert.GreaterOrEqual(preClearIndex, 0,
                $"{aid}: expected Set false before Copy into bool destination '{destination}'.");
            Assert.Less(preClearIndex, copyIndex,
                $"{aid}: Set false must run before Copy into bool destination '{destination}'.");
        }

        [Test, Category("Integration")]
        public void Regression_DeterministicRebuild_PreservesMappedLegacyBackupAliasesForLoadContinuity()
        {
            const string legacySource = "LegacyBrokered_Menu/Hat";
            const string legacyBackup = "ASMLite_Bak_S1_LegacyBrokered_Menu/Hat";

            var stubAsset = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(stubAsset, "deterministic rebuild setup: generated expression parameters asset must exist.");
            stubAsset.parameters = new[]
            {
                new VRCExpressionParameters.Parameter
                {
                    name = legacyBackup,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 1f,
                    saved = true,
                    networkSynced = true,
                }
            };
            EditorUtility.SetDirty(stubAsset);
            AssetDatabase.SaveAssets();

            _ctx.Comp.slotCount = 1;

            var toggle = AddVrcFuryToggle(
                _ctx.AvatarGo,
                useGlobalParam: false,
                globalParam: legacySource,
                menuPath: "Menu/Hat",
                name: "Hat");

            var enrollment = ASMLiteToggleNameBroker.EnrollForBuildRequest();
            Assert.GreaterOrEqual(enrollment.EnrolledCount, 1,
                "deterministic rebuild setup: expected deterministic broker enrollment before continuity build.");

            string deterministicSource = ReadStringField(toggle, "globalParam");
            Assert.IsFalse(string.IsNullOrWhiteSpace(deterministicSource),
                "deterministic rebuild setup: enrollment should assign deterministic global parameter name.");
            Assert.AreNotEqual(legacySource, deterministicSource,
                "deterministic rebuild setup: deterministic source should not keep legacy brokered global name verbatim.");
            string deterministicBackup = $"ASMLite_Bak_S1_{deterministicSource}";

            ASMLiteTestFixtures.SetExpressionParams(_ctx,
                new VRCExpressionParameters.Parameter
                {
                    name = deterministicSource,
                    valueType = VRCExpressionParameters.ValueType.Bool,
                    defaultValue = 0f,
                    saved = true,
                    networkSynced = true,
                });

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(1, buildResult,
                $"deterministic rebuild should discover one live deterministic source param. got {buildResult}.");

            var generatedCtrl = LoadGeneratedController("deterministic rebuild continuity");
            var generatedExpr = LoadGeneratedParams("deterministic rebuild continuity");
            var saveDriver = LoadSlotDriver("deterministic rebuild continuity", generatedCtrl, "ASMLite_Slot1", "SaveSlot1");
            var loadDriver = LoadSlotDriver("deterministic rebuild continuity", generatedCtrl, "ASMLite_Slot1", "LoadSlot1");
            var resetDriver = LoadSlotDriver("deterministic rebuild continuity", generatedCtrl, "ASMLite_Slot1", "ResetSlot1");

            Assert.IsTrue(generatedCtrl.parameters.Any(p => p.name == deterministicSource),
                "regression guard: generated FX controller must consume deterministic source param on rebuild.");
            Assert.IsFalse(generatedCtrl.parameters.Any(p => p.name == legacySource),
                "regression guard: generated FX controller must not regress to legacy brokered source declaration after deterministic enrollment.");

            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == deterministicBackup),
                "regression guard: deterministic backup key must be generated from deterministic source.");
            Assert.IsTrue(generatedExpr.parameters.Any(p => p != null && p.name == legacyBackup),
                "regression guard: mapped legacy backup alias must remain preserved for preset continuity.");

            Assert.IsTrue(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == deterministicSource && p.name == deterministicBackup),
                "regression guard: save path must keep deterministic backup copy.");
            Assert.IsTrue(saveDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == deterministicSource && p.name == legacyBackup),
                "regression guard: save path must mirror into mapped legacy backup alias.");

            Assert.IsTrue(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == deterministicBackup && p.name == deterministicSource),
                "regression guard: load path must keep deterministic backup source.");
            Assert.IsTrue(loadDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == legacyBackup && p.name == deterministicSource),
                "regression guard: load path must remain compatible with legacy mapped backup alias.");

            string deterministicDefault = $"ASMLite_Def_{deterministicSource}";
            Assert.IsTrue(resetDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == deterministicDefault && p.name == deterministicBackup),
                "regression guard: clear path must keep deterministic backup reset wiring.");
            Assert.IsTrue(resetDriver.parameters.Any(p => p.type == VRC_AvatarParameterDriver.ChangeType.Copy && p.source == deterministicDefault && p.name == legacyBackup),
                "regression guard: clear path must mirror mapped legacy alias reset wiring.");

            var report = ASMLiteBuilder.GetLatestLegacyAliasContinuityReport();
            Assert.GreaterOrEqual(report.MappedCount, 1,
                "regression guard: continuity diagnostics must count mapped legacy aliases.");
            Assert.GreaterOrEqual(report.MirroredCount, 1,
                "regression guard: continuity diagnostics must count mirrored legacy aliases.");
            Assert.AreEqual(0, report.UnmatchedCount,
                "regression guard: mapped scenario should not report unmatched aliases.");
        }
    }
}
