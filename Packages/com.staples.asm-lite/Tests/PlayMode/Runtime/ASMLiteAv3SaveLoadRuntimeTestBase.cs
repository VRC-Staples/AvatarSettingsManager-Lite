using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using ASMLite;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite.Editor;
using ASMLite.Tests.Editor;

namespace ASMLite.Tests.PlayMode
{
    public readonly struct ASMLiteAv3SaveLoadSeedCase
    {
        public ASMLiteAv3SaveLoadSeedCase(string name, uint seed)
        {
            Name = name ?? string.Empty;
            Seed = seed;
        }

        public string Name { get; }
        public uint Seed { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    public readonly struct ASMLiteAv3SaveLoadFuzzCase
    {
        public ASMLiteAv3SaveLoadFuzzCase(string name, bool isEnabled, uint seed, int iterations, string diagnostic)
        {
            Name = name ?? string.Empty;
            IsEnabled = isEnabled;
            Seed = seed;
            Iterations = iterations;
            Diagnostic = diagnostic ?? string.Empty;
        }

        public string Name { get; }
        public bool IsEnabled { get; }
        public uint Seed { get; }
        public int Iterations { get; }
        public string Diagnostic { get; }

        public override string ToString()
        {
            return Name;
        }
    }

    public abstract class ASMLiteAv3SaveLoadRuntimeTestBase
    {
        protected const string TestAvatarName = "ASMLite_AV3_SaveLoad_Runtime_Avatar";
        protected const string MergedParamsPath = "Assets/ASMLiteTests_Temp/ASMLiteAv3SaveLoadRuntimeMergedParams.asset";
        protected const string RealUatAvatarName = "ASMLite_AV3_SaveLoad_RealUAT_Avatar";
        protected const string RealUatMergedParamsPath = "Assets/ASMLiteTests_Temp/ASMLiteAv3SaveLoadRealUatMergedParams.asset";
        protected const string VrcFuryPlayModeEditorPref = "com.vrcfury.playMode";
        protected const string RealUatAvatarAssetPathEnvVar = "ASMLITE_AV3_UAT_AVATAR_ASSET_PATH";
        protected const string RealUatAvatarNameEnvVar = "ASMLITE_AV3_UAT_AVATAR_NAME";
        protected const string RealUatMaxSavedParametersEnvVar = "ASMLITE_AV3_UAT_MAX_SAVED_PARAMETERS";
        protected const string RealUatMaxUnsavedParametersEnvVar = "ASMLITE_AV3_UAT_MAX_UNSAVED_PARAMETERS";
        protected const string FuzzSeedEnvVar = "ASMLITE_AV3_SAVE_LOAD_FUZZ_SEED";
        protected const string FuzzIterationsEnvVar = "ASMLITE_AV3_SAVE_LOAD_FUZZ_ITERATIONS";
        protected const string RealUatAvatarAssetPathArg = "-asmliteAv3UatAvatarAssetPath";
        protected const string RealUatAvatarNameArg = "-asmliteAv3UatAvatarName";
        protected const string RealUatMaxSavedParametersArg = "-asmliteAv3UatMaxSavedParameters";
        protected const string RealUatMaxUnsavedParametersArg = "-asmliteAv3UatMaxUnsavedParameters";
        protected const string FuzzSeedArg = "-asmliteAv3SaveLoadFuzzSeed";
        protected const string FuzzIterationsArg = "-asmliteAv3SaveLoadFuzzIterations";
        protected const uint DefaultFuzzSeed = 0xA5A5F005u;
        protected const int DefaultRealUatMaxSavedParameters = 6;
        protected const int DefaultRealUatMaxUnsavedParameters = 6;

        protected static readonly ASMLiteAv3SaveLoadSeedCase[] SaveLoadSeedCases =
        {
            new ASMLiteAv3SaveLoadSeedCase("Seed_0xA5A50001", 0xA5A50001u),
            new ASMLiteAv3SaveLoadSeedCase("Seed_0xA5A50002", 0xA5A50002u),
            new ASMLiteAv3SaveLoadSeedCase("Seed_0xA5A50003", 0xA5A50003u),
        };

        protected static IEnumerable<ASMLiteAv3SaveLoadFuzzCase> FuzzCases
        {
            get
            {
                yield return ASMLiteAv3FuzzReplayConfig.Read().ToFuzzCase();
            }
        }

        protected static readonly string[] SavedParameterNames =
        {
            "ASMTest_BoolSaved_A",
            "ASMTest_BoolSaved_B",
            "ASMTest_IntSaved_A",
            "ASMTest_IntSaved_B",
            "ASMTest_FloatSaved_A",
            "ASMTest_FloatSaved_B",
        };

        protected static readonly string[] UnsavedParameterNames =
        {
            "ASMTest_BoolUnsaved_A",
            "ASMTest_BoolUnsaved_B",
            "ASMTest_IntUnsaved_A",
            "ASMTest_IntUnsaved_B",
            "ASMTest_FloatUnsaved_A",
            "ASMTest_FloatUnsaved_B",
        };

        protected static readonly ASMLiteAv3ParameterDescriptor[] SavedParameterDescriptors =
        {
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_BoolSaved_A", VRCExpressionParameters.ValueType.Bool),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_BoolSaved_B", VRCExpressionParameters.ValueType.Bool),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_IntSaved_A", VRCExpressionParameters.ValueType.Int),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_IntSaved_B", VRCExpressionParameters.ValueType.Int),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_FloatSaved_A", VRCExpressionParameters.ValueType.Float),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_FloatSaved_B", VRCExpressionParameters.ValueType.Float),
        };

        protected static readonly ASMLiteAv3ParameterDescriptor[] UnsavedParameterDescriptors =
        {
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_BoolUnsaved_A", VRCExpressionParameters.ValueType.Bool),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_BoolUnsaved_B", VRCExpressionParameters.ValueType.Bool),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_IntUnsaved_A", VRCExpressionParameters.ValueType.Int),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_IntUnsaved_B", VRCExpressionParameters.ValueType.Int),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_FloatUnsaved_A", VRCExpressionParameters.ValueType.Float),
            ASMLiteAv3SaveLoadHarness.Descriptor("ASMTest_FloatUnsaved_B", VRCExpressionParameters.ValueType.Float),
        };

        private AsmLiteTestContext _ctx;
        private GameObject _realUatRootInstance;
        private bool _hadVrcFuryPlayModePref;
        private bool _previousVrcFuryPlayMode;
        private bool _disabledVrcFuryPlayMode;

        [TearDown]
        public void TearDown()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                DestroyTestAvatar();
        }

        [UnityTearDown]
        public IEnumerator UnityTearDown()
        {
            if (EditorApplication.isPlaying)
                yield return new ExitPlayMode();

            DestroyTestAvatar();
        }
        protected void BuildAndWireAvatarFixture()
        {
            ASMLiteTestFixtures.ResetGeneratedExprParams();
            _ctx = ASMLiteTestFixtures.CreateTestAvatar();
            Assert.IsNotNull(_ctx, "Runtime: fixture creation returned null context.");
            _ctx.AvatarGo.name = TestAvatarName;
            _ctx.Comp.slotCount = 1;
            _ctx.Comp.useParameterExclusions = true;
            _ctx.Comp.excludedParameterNames = UnsavedParameterNames.ToArray();

            AddVisibilityParameters();

            int buildResult = ASMLiteBuilder.Build(_ctx.Comp);
            Assert.AreEqual(SavedParameterNames.Length, buildResult,
                $"Runtime: Build should discover only saved/non-excluded parameters. got {buildResult}.");

            var generatedParams = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedParams, $"Runtime: generated expression params missing at {ASMLiteAssetPaths.ExprParams}.");
            Assert.IsTrue((generatedParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                    .Any(parameter => parameter != null && parameter.name == ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName),
                "Runtime: generated expression params must include ASMLite_Ctrl before AV3 visibility check.");

            var generatedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(generatedController, $"Runtime: generated FX controller missing at {ASMLiteAssetPaths.FXController}.");

            var mergedParams = BuildMergedParameters(_ctx.ParamsAsset, generatedParams);
            AssetDatabase.DeleteAsset(MergedParamsPath);
            AssetDatabase.CreateAsset(mergedParams, MergedParamsPath);
            AssetDatabase.SaveAssets();

            _ctx.AvDesc.expressionParameters = mergedParams;
            WireFxController(_ctx.AvDesc, generatedController);
            DisableVrcFuryPlayModeProcessing();
            EditorUtility.SetDirty(_ctx.AvDesc);
        }

        protected RealUatAvatarSetup BuildAndWireRealUatAvatarFixture(RealUatSelection selection)
        {
            Assert.IsTrue(selection.IsConfigured, "External UAT: real UAT setup requires explicit operator selection.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(selection.AssetPath), "External UAT: configured real UAT asset path was empty.");

            ASMLiteTestFixtures.ResetGeneratedExprParams();
            EnsureTestTempFolder();

            var sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(selection.AssetPath);
            Assert.IsNotNull(sourcePrefab,
                $"External UAT: operator-selected real UAT prefab was not found at '{selection.AssetPath}'. "
                + $"Use a project-relative prefab path via {RealUatAvatarAssetPathEnvVar} or {RealUatAvatarAssetPathArg}; the test intentionally does not scan the project for avatars.");

            _realUatRootInstance = PrefabUtility.InstantiatePrefab(sourcePrefab) as GameObject;
            if (_realUatRootInstance == null)
                _realUatRootInstance = UnityEngine.Object.Instantiate(sourcePrefab);
            Assert.IsNotNull(_realUatRootInstance, $"External UAT: failed to instantiate real UAT prefab '{selection.AssetPath}'.");

            var descriptor = SelectRealUatDescriptor(_realUatRootInstance, selection);
            descriptor.gameObject.name = RealUatAvatarName;
            Assert.IsNotNull(descriptor.expressionParameters,
                $"External UAT: selected real UAT avatar '{descriptor.gameObject.name}' from '{selection.AssetPath}' has no VRCExpressionParameters asset.");

            var savedParameters = SelectRealUatParameters(
                descriptor.expressionParameters,
                saved: true,
                maxCount: selection.MaxSavedParameters);
            if (savedParameters.Length == 0)
            {
                Assert.Inconclusive(
                    $"External UAT: selected real UAT avatar '{descriptor.gameObject.name}' from '{selection.AssetPath}' has no supported saved Bool/Int/Float parameters. "
                    + "Choose an avatar with saved expression parameters or lower exclusions before running this manual test.");
            }

            var unsavedParameters = SelectRealUatParameters(
                descriptor.expressionParameters,
                saved: false,
                maxCount: selection.MaxUnsavedParameters);

            var selectedSavedNames = new HashSet<string>(savedParameters.Select(parameter => parameter.name), StringComparer.Ordinal);
            var excludedNames = (descriptor.expressionParameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(IsSupportedRealUatParameter)
                .Select(parameter => parameter.name)
                .Where(name => !selectedSavedNames.Contains(name))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            var componentRoot = new GameObject("ASMLite_RealUAT_ManualCloneOnly");
            componentRoot.transform.SetParent(descriptor.transform, worldPositionStays: false);
            var component = componentRoot.AddComponent<ASMLiteComponent>();
            component.slotCount = 1;
            component.useParameterExclusions = true;
            component.excludedParameterNames = excludedNames;

            int buildResult = ASMLiteBuilder.Build(component);
            Assert.AreEqual(savedParameters.Length, buildResult,
                $"External UAT: Build should back up only the operator-selected safe saved subset for real UAT. "
                + $"asset='{selection.AssetPath}' avatar='{descriptor.gameObject.name}' "
                + $"selectedSaved=[{string.Join(", ", selectedSavedNames.OrderBy(name => name, StringComparer.Ordinal))}] "
                + $"excludedCount={excludedNames.Length} got={buildResult}.");

            var generatedParams = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            Assert.IsNotNull(generatedParams, $"External UAT: generated expression params missing at {ASMLiteAssetPaths.ExprParams}.");
            Assert.IsTrue((generatedParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                    .Any(parameter => parameter != null && parameter.name == ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName),
                "External UAT: generated expression params must include ASMLite_Ctrl before real UAT runtime check.");

            var generatedController = AssetDatabase.LoadAssetAtPath<AnimatorController>(ASMLiteAssetPaths.FXController);
            Assert.IsNotNull(generatedController, $"External UAT: generated FX controller missing at {ASMLiteAssetPaths.FXController}.");

            var mergedParams = BuildMergedParameters(descriptor.expressionParameters, generatedParams);
            AssetDatabase.DeleteAsset(RealUatMergedParamsPath);
            AssetDatabase.CreateAsset(mergedParams, RealUatMergedParamsPath);
            AssetDatabase.SaveAssets();

            descriptor.expressionParameters = mergedParams;
            WireFxController(descriptor, generatedController);
            DisableVrcFuryPlayModeProcessing();
            EditorUtility.SetDirty(descriptor);

            Debug.Log(
                $"External UAT: Real UAT manual avatar clone prepared. asset='{selection.AssetPath}' avatar='{descriptor.gameObject.name}' "
                + $"savedSubset=[{string.Join(", ", savedParameters.Select(parameter => parameter.name))}] "
                + $"unsavedMetadataSubset=[{string.Join(", ", unsavedParameters.Select(parameter => parameter.name))}] "
                + $"excludedMetadataCount={excludedNames.Length}. Source prefab asset was not modified.");

            return new RealUatAvatarSetup(
                savedParameters.Select(ToDescriptor).ToArray(),
                unsavedParameters.Select(ToDescriptor).ToArray());
        }

        private static void EnsureTestTempFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/ASMLiteTests_Temp"))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");
        }

        private static VRCAvatarDescriptor SelectRealUatDescriptor(GameObject root, RealUatSelection selection)
        {
            Assert.IsNotNull(root, "External UAT: cannot select a real UAT descriptor from a null prefab instance.");

            var descriptors = root.GetComponentsInChildren<VRCAvatarDescriptor>(includeInactive: true);
            Assert.IsTrue(descriptors.Length > 0,
                $"External UAT: operator-selected real UAT prefab '{selection.AssetPath}' contains no VRCAvatarDescriptor. "
                + "Choose the avatar prefab root or provide a prefab containing the descriptor.");

            if (!string.IsNullOrWhiteSpace(selection.AvatarName))
            {
                var selected = descriptors.FirstOrDefault(descriptor =>
                    descriptor != null && string.Equals(descriptor.gameObject.name, selection.AvatarName, StringComparison.Ordinal));
                Assert.IsNotNull(selected,
                    $"External UAT: real UAT prefab '{selection.AssetPath}' did not contain avatar descriptor named '{selection.AvatarName}'. "
                    + $"Available=[{string.Join(", ", descriptors.Where(descriptor => descriptor != null).Select(descriptor => descriptor.gameObject.name))}].");
                return selected;
            }

            if (descriptors.Length == 1)
                return descriptors[0];

            Assert.Inconclusive(
                $"External UAT: real UAT prefab '{selection.AssetPath}' contains multiple avatar descriptors. "
                + $"Set {RealUatAvatarNameEnvVar} or {RealUatAvatarNameArg} to one of: "
                + $"[{string.Join(", ", descriptors.Where(descriptor => descriptor != null).Select(descriptor => descriptor.gameObject.name))}].");
            return null;
        }

        private static VRCExpressionParameters.Parameter[] SelectRealUatParameters(
            VRCExpressionParameters expressionParameters,
            bool saved,
            int maxCount)
        {
            maxCount = Math.Max(0, maxCount);
            if (expressionParameters == null || maxCount == 0)
                return Array.Empty<VRCExpressionParameters.Parameter>();

            var candidates = (expressionParameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => IsSupportedRealUatParameter(parameter) && parameter.saved == saved)
                .GroupBy(parameter => parameter.name, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(parameter => parameter.valueType)
                .ThenBy(parameter => parameter.name, StringComparer.Ordinal)
                .ToArray();

            var selected = new List<VRCExpressionParameters.Parameter>(maxCount);
            var typeOrder = new[]
            {
                VRCExpressionParameters.ValueType.Bool,
                VRCExpressionParameters.ValueType.Int,
                VRCExpressionParameters.ValueType.Float,
            };

            while (selected.Count < maxCount)
            {
                bool addedAny = false;
                foreach (var type in typeOrder)
                {
                    if (selected.Count >= maxCount)
                        break;

                    var next = PickBalancedRealUatCandidate(candidates, selected, type);
                    if (next == null)
                        continue;

                    selected.Add(next);
                    addedAny = true;
                }

                if (!addedAny)
                    break;
            }

            return selected.ToArray();
        }

        private static VRCExpressionParameters.Parameter PickBalancedRealUatCandidate(
            VRCExpressionParameters.Parameter[] candidates,
            List<VRCExpressionParameters.Parameter> selected,
            VRCExpressionParameters.ValueType type)
        {
            var selectedNames = new HashSet<string>(
                selected.Select(parameter => parameter.name),
                StringComparer.Ordinal);
            var typedCandidates = (candidates ?? Array.Empty<VRCExpressionParameters.Parameter>())
                .Where(parameter => parameter != null && parameter.valueType == type && !selectedNames.Contains(parameter.name))
                .OrderBy(parameter => parameter.name, StringComparer.Ordinal)
                .ToArray();
            if (typedCandidates.Length == 0)
                return null;

            int selectedOfType = selected.Count(parameter => parameter != null && parameter.valueType == type);
            int index = selectedOfType % 2 == 0
                ? selectedOfType / 2
                : typedCandidates.Length - 1 - (selectedOfType / 2);
            if (index < 0)
                index = 0;
            if (index >= typedCandidates.Length)
                index = typedCandidates.Length - 1;
            return typedCandidates[index];
        }

        private static bool IsSupportedRealUatParameter(VRCExpressionParameters.Parameter parameter)
        {
            return parameter != null
                && !string.IsNullOrWhiteSpace(parameter.name)
                && !parameter.name.StartsWith("ASMLite_", StringComparison.Ordinal)
                && !string.Equals(parameter.name, ASMLiteAv3RuntimeBridge.ASMLiteControlParameterName, StringComparison.Ordinal)
                && (parameter.valueType == VRCExpressionParameters.ValueType.Bool
                    || parameter.valueType == VRCExpressionParameters.ValueType.Int
                    || parameter.valueType == VRCExpressionParameters.ValueType.Float);
        }

        private static ASMLiteAv3ParameterDescriptor ToDescriptor(VRCExpressionParameters.Parameter parameter)
        {
            return ASMLiteAv3SaveLoadHarness.Descriptor(parameter.name, parameter.valueType);
        }

        protected static uint DeriveFuzzIterationSeed(uint seed, int iterationIndex)
        {
            unchecked
            {
                uint mixed = seed ^ ((uint)Math.Max(0, iterationIndex) * 0x9E3779B9u) ^ 0xC2B2AE35u;
                mixed ^= mixed >> 16;
                mixed *= 0x7FEB352Du;
                mixed ^= mixed >> 15;
                mixed *= 0x846CA68Bu;
                mixed ^= mixed >> 16;
                return mixed == 0u ? 0xA5A5F005u : mixed;
            }
        }

        protected static string FormatSeed(uint seed)
        {
            return ASMLiteAv3SaveLoadRunContext.FormatSeed(seed);
        }

        private void AddVisibilityParameters()
        {
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolSaved_A", VRCExpressionParameters.ValueType.Bool, 1f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolSaved_B", VRCExpressionParameters.ValueType.Bool, 0f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntSaved_A", VRCExpressionParameters.ValueType.Int, 1f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntSaved_B", VRCExpressionParameters.ValueType.Int, 2f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatSaved_A", VRCExpressionParameters.ValueType.Float, 0.25f, saved: true);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatSaved_B", VRCExpressionParameters.ValueType.Float, 0.75f, saved: true);

            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolUnsaved_A", VRCExpressionParameters.ValueType.Bool, 1f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_BoolUnsaved_B", VRCExpressionParameters.ValueType.Bool, 0f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntUnsaved_A", VRCExpressionParameters.ValueType.Int, 3f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_IntUnsaved_B", VRCExpressionParameters.ValueType.Int, 4f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatUnsaved_A", VRCExpressionParameters.ValueType.Float, 0.33f, saved: false);
            ASMLiteTestFixtures.AddExpressionParam(_ctx, "ASMTest_FloatUnsaved_B", VRCExpressionParameters.ValueType.Float, 0.66f, saved: false);
        }

        private static VRCExpressionParameters BuildMergedParameters(
            VRCExpressionParameters avatarParams,
            VRCExpressionParameters generatedParams)
        {
            var merged = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            var byName = new Dictionary<string, VRCExpressionParameters.Parameter>(StringComparer.Ordinal);

            foreach (var parameter in avatarParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
            {
                if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                    byName[parameter.name] = parameter;
            }

            foreach (var parameter in generatedParams.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>())
            {
                if (parameter != null && !string.IsNullOrEmpty(parameter.name))
                    byName[parameter.name] = parameter;
            }

            merged.parameters = byName.Values.ToArray();
            return merged;
        }

        private static void WireFxController(VRCAvatarDescriptor descriptor, AnimatorController controller)
        {
            var layers = descriptor.baseAnimationLayers;
            if (layers == null || layers.Length < 5)
                Array.Resize(ref layers, 5);

            layers[0] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Base);
            layers[1] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Additive);
            layers[2] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Gesture);
            layers[3] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.Action);
            layers[4] = CreateLayer(VRCAvatarDescriptor.AnimLayerType.FX, controller);

            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = layers;
            descriptor.specialAnimationLayers = descriptor.specialAnimationLayers ?? Array.Empty<VRCAvatarDescriptor.CustomAnimLayer>();
        }

        private static VRCAvatarDescriptor.CustomAnimLayer CreateLayer(
            VRCAvatarDescriptor.AnimLayerType type,
            AnimatorController controller = null)
        {
            return new VRCAvatarDescriptor.CustomAnimLayer
            {
                type = type,
                isDefault = controller == null,
                isEnabled = true,
                animatorController = controller,
            };
        }

        private void DisableVrcFuryPlayModeProcessing()
        {
            if (_disabledVrcFuryPlayMode)
                return;

            _hadVrcFuryPlayModePref = EditorPrefs.HasKey(VrcFuryPlayModeEditorPref);
            _previousVrcFuryPlayMode = EditorPrefs.GetBool(VrcFuryPlayModeEditorPref, true);
            EditorPrefs.SetBool(VrcFuryPlayModeEditorPref, false);
            _disabledVrcFuryPlayMode = true;
        }

        private void RestoreVrcFuryPlayModeProcessing()
        {
            if (!_disabledVrcFuryPlayMode)
                return;

            if (_hadVrcFuryPlayModePref)
                EditorPrefs.SetBool(VrcFuryPlayModeEditorPref, _previousVrcFuryPlayMode);
            else
                EditorPrefs.DeleteKey(VrcFuryPlayModeEditorPref);

            _disabledVrcFuryPlayMode = false;
        }

        protected static RealUatSelection ReadRealUatSelection()
        {
            return ReadRealUatSelection(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable);
        }

        protected static RealUatSelection ReadRealUatSelection(string[] commandLineArgs, Func<string, string> readEnvironmentVariable)
        {
            commandLineArgs = commandLineArgs ?? Array.Empty<string>();
            readEnvironmentVariable = readEnvironmentVariable ?? (_ => null);

            string rawAssetPath = FirstNonEmpty(
                ReadCommandLineValue(commandLineArgs, RealUatAvatarAssetPathArg),
                readEnvironmentVariable(RealUatAvatarAssetPathEnvVar));
            string avatarName = FirstNonEmpty(
                ReadCommandLineValue(commandLineArgs, RealUatAvatarNameArg),
                readEnvironmentVariable(RealUatAvatarNameEnvVar));

            int maxSavedParameters = ParsePositiveInt(
                FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, RealUatMaxSavedParametersArg),
                    readEnvironmentVariable(RealUatMaxSavedParametersEnvVar)),
                DefaultRealUatMaxSavedParameters);

            int maxUnsavedParameters = ParsePositiveInt(
                FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, RealUatMaxUnsavedParametersArg),
                    readEnvironmentVariable(RealUatMaxUnsavedParametersEnvVar)),
                DefaultRealUatMaxUnsavedParameters);

            if (string.IsNullOrWhiteSpace(rawAssetPath))
            {
                return RealUatSelection.Unconfigured(
                    "External UAT: External real-avatar UAT is manual/non-default and did not run because no operator-selected avatar prefab was provided. "
                    + $"Set {RealUatAvatarAssetPathEnvVar}=Assets/Path/Avatar.prefab or pass {RealUatAvatarAssetPathArg} Assets/Path/Avatar.prefab. "
                    + $"Optional selector: {RealUatAvatarNameEnvVar} / {RealUatAvatarNameArg}. "
                    + "The test intentionally avoids broad project scans and never mutates the source prefab asset.");
            }

            string assetPath = NormalizeAssetPath(rawAssetPath);
            return RealUatSelection.Configured(assetPath, avatarName, maxSavedParameters, maxUnsavedParameters);
        }

        private static string NormalizeAssetPath(string rawAssetPath)
        {
            rawAssetPath = (rawAssetPath ?? string.Empty).Trim().Trim('\"');
            if (string.IsNullOrWhiteSpace(rawAssetPath))
                return string.Empty;

            string guidPath = AssetDatabase.GUIDToAssetPath(rawAssetPath);
            if (!string.IsNullOrWhiteSpace(guidPath))
                return guidPath;

            rawAssetPath = rawAssetPath.Replace('\\', '/');
            if (rawAssetPath.StartsWith("Assets/", StringComparison.Ordinal) || string.Equals(rawAssetPath, "Assets", StringComparison.Ordinal))
                return rawAssetPath;

            try
            {
                string fullPath = Path.GetFullPath(rawAssetPath).Replace('\\', '/');
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
                if (fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
                    return fullPath.Substring(projectRoot.Length + 1);
            }
            catch
            {
                // Fall through to the raw value so the load assertion reports the exact input.
            }

            return rawAssetPath;
        }

        private static string ReadCommandLineValue(string[] args, string key)
        {
            if (args == null || string.IsNullOrWhiteSpace(key))
                return string.Empty;

            for (int i = 0; i < args.Length; i++)
            {
                if (!string.Equals(args[i], key, StringComparison.Ordinal))
                    continue;

                if (i + 1 < args.Length)
                    return args[i + 1];
                return string.Empty;
            }

            string prefix = key + "=";
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != null && args[i].StartsWith(prefix, StringComparison.Ordinal))
                    return args[i].Substring(prefix.Length);
            }

            return string.Empty;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }

        private static int ParsePositiveInt(string text, int fallback)
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0)
                return value;
            return fallback;
        }

        private void DestroyTestAvatar()
        {
            RestoreVrcFuryPlayModeProcessing();
            var avatar = _ctx?.AvatarGo != null ? _ctx.AvatarGo : GameObject.Find(TestAvatarName);
            ASMLiteTestFixtures.TearDownTestAvatar(avatar);
            AssetDatabase.DeleteAsset(MergedParamsPath);
            AssetDatabase.DeleteAsset(RealUatMergedParamsPath);
            if (_realUatRootInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_realUatRootInstance);
                _realUatRootInstance = null;
            }
            _ctx = null;
        }

        private readonly struct ASMLiteAv3FuzzReplayConfig
        {
            private ASMLiteAv3FuzzReplayConfig(bool isEnabled, uint seed, int iterations, string diagnostic)
            {
                IsEnabled = isEnabled;
                Seed = seed;
                Iterations = isEnabled && iterations > 0 ? iterations : 0;
                Diagnostic = diagnostic ?? string.Empty;
            }

            internal bool IsEnabled { get; }
            internal uint Seed { get; }
            internal int Iterations { get; }
            internal string Diagnostic { get; }

            internal static ASMLiteAv3FuzzReplayConfig Read()
            {
                return Read(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable);
            }

            internal static ASMLiteAv3FuzzReplayConfig Read(string[] commandLineArgs, Func<string, string> readEnvironmentVariable)
            {
                commandLineArgs = commandLineArgs ?? Array.Empty<string>();
                readEnvironmentVariable = readEnvironmentVariable ?? (_ => null);

                string rawIterations = FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, FuzzIterationsArg),
                    readEnvironmentVariable(FuzzIterationsEnvVar));
                if (!int.TryParse(rawIterations, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iterations) || iterations <= 0)
                {
                    return Disabled(
                        "Fuzz replay: Replayable AV3 save/load fuzz/scale coverage is local-only opt-in and disabled by default. "
                        + $"Set {FuzzIterationsEnvVar}=N (or pass {FuzzIterationsArg} N) to enable; "
                        + $"optionally set {FuzzSeedEnvVar}=0xSEED (or pass {FuzzSeedArg} 0xSEED). "
                        + $"Default replay seed when omitted is {FormatSeed(DefaultFuzzSeed)}.");
                }

                string rawSeed = FirstNonEmpty(
                    ReadCommandLineValue(commandLineArgs, FuzzSeedArg),
                    readEnvironmentVariable(FuzzSeedEnvVar));
                uint seed = DefaultFuzzSeed;
                if (!string.IsNullOrWhiteSpace(rawSeed) && !TryParseSeed(rawSeed, out seed))
                {
                    return Disabled(
                        $"Fuzz replay: Invalid AV3 save/load fuzz seed '{rawSeed}'. "
                        + $"Use decimal uint or hex 0xSEED via {FuzzSeedEnvVar} / {FuzzSeedArg}. "
                        + $"Iterations requested={iterations.ToString(CultureInfo.InvariantCulture)}; fuzz did not run.");
                }

                return new ASMLiteAv3FuzzReplayConfig(
                    true,
                    seed,
                    iterations,
                    $"Fuzz replay: Replayable AV3 save/load fuzz/scale coverage enabled. seed={FormatSeed(seed)} iterations={iterations.ToString(CultureInfo.InvariantCulture)}.");
            }

            internal ASMLiteAv3SaveLoadFuzzCase ToFuzzCase()
            {
                string name = IsEnabled
                    ? $"Seed_{FormatSeed(Seed)}_iterations={Iterations.ToString(CultureInfo.InvariantCulture)}"
                    : "Disabled_LocalOptIn";
                return new ASMLiteAv3SaveLoadFuzzCase(name, IsEnabled, Seed, Iterations, Diagnostic);
            }

            public override string ToString()
            {
                return IsEnabled
                    ? $"seed={FormatSeed(Seed)} iterations={Iterations.ToString(CultureInfo.InvariantCulture)}"
                    : Diagnostic;
            }

            private static ASMLiteAv3FuzzReplayConfig Disabled(string diagnostic)
            {
                return new ASMLiteAv3FuzzReplayConfig(false, DefaultFuzzSeed, 0, diagnostic);
            }

            private static bool TryParseSeed(string text, out uint seed)
            {
                seed = 0u;
                text = (text ?? string.Empty).Trim().Trim('\"');
                if (string.IsNullOrWhiteSpace(text))
                    return false;

                if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                {
                    return uint.TryParse(
                        text.Substring(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out seed);
                }

                return uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
            }
        }

        protected readonly struct RealUatSelection
        {
            private RealUatSelection(bool isConfigured, string assetPath, string avatarName, int maxSavedParameters, int maxUnsavedParameters, string diagnostic)
            {
                IsConfigured = isConfigured;
                AssetPath = assetPath ?? string.Empty;
                AvatarName = avatarName ?? string.Empty;
                MaxSavedParameters = maxSavedParameters > 0 ? maxSavedParameters : DefaultRealUatMaxSavedParameters;
                MaxUnsavedParameters = maxUnsavedParameters > 0 ? maxUnsavedParameters : DefaultRealUatMaxUnsavedParameters;
                Diagnostic = diagnostic ?? string.Empty;
            }

            internal bool IsConfigured { get; }
            internal string AssetPath { get; }
            internal string AvatarName { get; }
            internal int MaxSavedParameters { get; }
            internal int MaxUnsavedParameters { get; }
            internal string Diagnostic { get; }

            internal static RealUatSelection Configured(string assetPath, string avatarName, int maxSavedParameters, int maxUnsavedParameters)
            {
                return new RealUatSelection(true, assetPath, avatarName, maxSavedParameters, maxUnsavedParameters, string.Empty);
            }

            internal static RealUatSelection Unconfigured(string diagnostic)
            {
                return new RealUatSelection(false, string.Empty, string.Empty, DefaultRealUatMaxSavedParameters, DefaultRealUatMaxUnsavedParameters, diagnostic);
            }
        }

        protected readonly struct RealUatAvatarSetup
        {
            internal RealUatAvatarSetup(
                ASMLiteAv3ParameterDescriptor[] savedDescriptors,
                ASMLiteAv3ParameterDescriptor[] unsavedDescriptors)
            {
                SavedDescriptors = savedDescriptors ?? Array.Empty<ASMLiteAv3ParameterDescriptor>();
                UnsavedDescriptors = unsavedDescriptors ?? Array.Empty<ASMLiteAv3ParameterDescriptor>();
            }

            internal ASMLiteAv3ParameterDescriptor[] SavedDescriptors { get; }
            internal ASMLiteAv3ParameterDescriptor[] UnsavedDescriptors { get; }
        }
    }
}
