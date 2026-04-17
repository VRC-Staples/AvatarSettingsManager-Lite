using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.TestTools.TestRunner.Api;
using NUnit.Framework;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using ASMLite;
using ASMLite.Editor;

namespace ASMLite.Tests.Editor
{
    /// <summary>
    /// Shared test fixtures for ASMLite integration tests.
    /// Call CreateTestAvatar() to get a fully-wired avatar, TearDownTestAvatar() to clean up.
    /// </summary>
    public static class ASMLiteTestFixtures
    {
        private const string TempDir = "Assets/ASMLiteTests_Temp";

        public static AsmLiteTestContext CreateTestAvatar()
        {
            // Create temp directory (guard against already existing)
            if (!AssetDatabase.IsValidFolder(TempDir))
                AssetDatabase.CreateFolder("Assets", "ASMLiteTests_Temp");

            // Create AnimatorController
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(
                TempDir + "/TestFX.controller");

            // Create VRCExpressionParameters
            var paramsAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            paramsAsset.parameters = new VRCExpressionParameters.Parameter[0];
            AssetDatabase.CreateAsset(paramsAsset, TempDir + "/TestParams.asset");

            // Create VRCExpressionsMenu
            var menuAsset = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            menuAsset.controls = new System.Collections.Generic.List<VRCExpressionsMenu.Control>();
            AssetDatabase.CreateAsset(menuAsset, TempDir + "/TestMenu.asset");

            AssetDatabase.SaveAssets();

            // Create avatar GameObject
            var avatarGo = new GameObject("TestAvatar");
            var avDesc = avatarGo.AddComponent<VRCAvatarDescriptor>();

            // Wire FX layer -- resize to at least 5 slots rather than indexing blindly
            var layers = avDesc.baseAnimationLayers;
            if (layers == null || layers.Length < 5)
            {
                var newLayers = new VRCAvatarDescriptor.CustomAnimLayer[5];
                if (layers != null)
                    for (int i = 0; i < layers.Length; i++)
                        newLayers[i] = layers[i];
                // Slot 4 is FX
                newLayers[4] = new VRCAvatarDescriptor.CustomAnimLayer
                {
                    type = VRCAvatarDescriptor.AnimLayerType.FX,
                    isDefault = false,
                    isEnabled = true,
                    animatorController = ctrl
                };
                avDesc.baseAnimationLayers = newLayers;
            }
            else
            {
                layers[4].animatorController = ctrl;
                layers[4].isDefault = false;
                layers[4].isEnabled = true;
                avDesc.baseAnimationLayers = layers;
            }

            avDesc.expressionParameters = paramsAsset;
            avDesc.expressionsMenu = menuAsset;

            // Add ASMLiteComponent as child
            var compGo = new GameObject("ASMLite");
            compGo.transform.SetParent(avatarGo.transform);
            var comp = compGo.AddComponent<ASMLiteComponent>();

            return new AsmLiteTestContext
            {
                AvatarGo = avatarGo,
                AvDesc = avDesc,
                Comp = comp,
                Ctrl = ctrl,
                ParamsAsset = paramsAsset,
                MenuAsset = menuAsset
            };
        }

        public static void AddExpressionParam(
            AsmLiteTestContext ctx,
            string name,
            VRCExpressionParameters.ValueType valueType,
            float defaultValue = 0f,
            bool saved = true,
            bool networkSynced = true)
        {
            var existing = ctx.ParamsAsset.parameters ?? new VRCExpressionParameters.Parameter[0];
            var updated = new VRCExpressionParameters.Parameter[existing.Length + 1];
            existing.CopyTo(updated, 0);
            updated[existing.Length] = new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = valueType,
                defaultValue = defaultValue,
                saved = saved,
                networkSynced = networkSynced,
            };

            ctx.ParamsAsset.parameters = updated;
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        public static void SetExpressionParams(AsmLiteTestContext ctx, params VRCExpressionParameters.Parameter[] parameters)
        {
            Assert.IsNotNull(ctx, "SetExpressionParams requires a valid test context.");

            if (ctx.ParamsAsset == null && ctx.AvDesc != null && ctx.AvDesc.expressionParameters != null)
                ctx.ParamsAsset = ctx.AvDesc.expressionParameters;

            if (ctx.ParamsAsset == null)
            {
                var fallbackParamsAsset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
                fallbackParamsAsset.parameters = new VRCExpressionParameters.Parameter[0];
                ctx.ParamsAsset = fallbackParamsAsset;

                if (ctx.AvDesc != null)
                {
                    ctx.AvDesc.expressionParameters = fallbackParamsAsset;
                    EditorUtility.SetDirty(ctx.AvDesc);
                }
            }

            Assert.IsTrue(ctx.ParamsAsset != null,
                "SetExpressionParams requires a live ParamsAsset reference. Ensure CreateTestAvatar() completed and fixture assets were not torn down before this helper call.");

            ctx.ParamsAsset.parameters = parameters ?? new VRCExpressionParameters.Parameter[0];
            EditorUtility.SetDirty(ctx.ParamsAsset);
            AssetDatabase.SaveAssets();
        }

        public static GameObject CreateChild(GameObject parent, string name)
        {
            var child = new GameObject(name ?? "Child");
            if (parent != null)
                child.transform.SetParent(parent.transform);
            return child;
        }

        internal static VF.Model.VRCFury EnsureLiveFullControllerPayload(ASMLiteComponent component)
        {
            if (component == null)
                return null;

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

        internal static string ReadSerializedMenuPrefix(VF.Model.VRCFury vf)
        {
            var serializedVf = new SerializedObject(vf);
            serializedVf.Update();

            var prefixProperty = serializedVf.FindProperty("content.menus.Array.data[0].prefix");
            Assert.IsNotNull(prefixProperty,
                "Expected serialized FullController menu prefix field at content.menus.Array.data[0].prefix.");

            return prefixProperty.stringValue;
        }

        public static void TearDownTestAvatar(GameObject avatarGo)
        {
            AssetDatabase.DeleteAsset(TempDir);
            AssetDatabase.Refresh();
            if (avatarGo != null)
                UnityEngine.Object.DestroyImmediate(avatarGo);
        }

        public static void ResetGeneratedExprParams()
        {
            var generatedExpr = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(ASMLiteAssetPaths.ExprParams);
            if (generatedExpr == null)
                return;
            generatedExpr.parameters = new VRCExpressionParameters.Parameter[0];
            EditorUtility.SetDirty(generatedExpr);
            AssetDatabase.SaveAssets();
        }
    }

    /// <summary>
    /// Holds all objects created by CreateTestAvatar().
    /// </summary>
    public class AsmLiteTestContext
    {
        public GameObject AvatarGo;
        public VRCAvatarDescriptor AvDesc;
        public ASMLiteComponent Comp;
        public AnimatorController Ctrl;
        public VRCExpressionParameters ParamsAsset;
        public VRCExpressionsMenu MenuAsset;
    }

    [Serializable]
    internal sealed class AsmLiteBatchFilterDefinition
    {
        public string[] testNames = Array.Empty<string>();
        public string[] groupNames = Array.Empty<string>();
        public string[] categoryNames = Array.Empty<string>();
        public string[] assemblyNames = Array.Empty<string>();
    }

    [Serializable]
    internal sealed class AsmLiteBatchRunConfiguration
    {
        public AsmLiteBatchRunDefinition[] runs = Array.Empty<AsmLiteBatchRunDefinition>();
    }

    [Serializable]
    internal sealed class AsmLiteBatchRunDefinition
    {
        public string name;
        public string resultFile;
        public string[] testNames = Array.Empty<string>();
        public string[] groupNames = Array.Empty<string>();
        public string[] categoryNames = Array.Empty<string>();
        public string[] assemblyNames = Array.Empty<string>();
        public AsmLiteBatchFilterDefinition[] filters = Array.Empty<AsmLiteBatchFilterDefinition>();
        public bool runSynchronously;
        public bool allowEmptySelection;
    }

    public static class ASMLiteBatchTestRunner
    {
        private const string RunsJsonEnv = "ASMLITE_BATCH_RUNS_JSON";
        private const string ResultsDirEnv = "ASMLITE_BATCH_RESULTS_DIR";
        private const string CanonicalResultsFileName = "editmode-results.xml";

        private static readonly Queue<AsmLiteBatchRunDefinition> s_pendingRuns = new Queue<AsmLiteBatchRunDefinition>();
        private static readonly List<CompletedRunResult> s_completedRunResults = new List<CompletedRunResult>();

        private static TestRunnerApi s_testRunnerApi;
        private static CallbackForwarder s_callbackForwarder;
        private static string s_resultsDirectory;
        private static int s_completedRunCount;
        private static int s_expectedRunCount;
        private static int s_exitCode;
        private static AsmLiteBatchRunDefinition s_activeRun;

        public static void RunFromCommandLine()
        {
            try
            {
                ResetStaticState();

                string rawJson = Environment.GetEnvironmentVariable(RunsJsonEnv);
                if (string.IsNullOrWhiteSpace(rawJson))
                    throw new InvalidOperationException($"Missing required environment variable '{RunsJsonEnv}'.");

                string rawResultsDir = Environment.GetEnvironmentVariable(ResultsDirEnv);
                s_resultsDirectory = string.IsNullOrWhiteSpace(rawResultsDir)
                    ? Directory.GetCurrentDirectory()
                    : rawResultsDir;
                Directory.CreateDirectory(s_resultsDirectory);

                var configuration = JsonUtility.FromJson<AsmLiteBatchRunConfiguration>(rawJson);
                if (configuration == null || configuration.runs == null || configuration.runs.Length == 0)
                    throw new InvalidOperationException("Batch test runner requires at least one run definition.");

                string canonicalResultPath = Path.GetFullPath(ResolveResultPath(CanonicalResultsFileName));
                var writtenResultPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    canonicalResultPath,
                };
                for (int i = 0; i < configuration.runs.Length; i++)
                {
                    var normalized = NormalizeRun(configuration.runs[i], i);
                    string resolvedResultPath = Path.GetFullPath(ResolveResultPath(normalized.resultFile));
                    if (string.Equals(resolvedResultPath, canonicalResultPath, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"Batch test runner reserves canonical result path '{canonicalResultPath}'. Pick a different resultFile.");
                    if (!writtenResultPaths.Add(resolvedResultPath))
                        throw new InvalidOperationException($"Batch test runner requires unique resultFile values. Duplicate path: '{resolvedResultPath}'.");
                    if (!normalized.allowEmptySelection && !RunHasAnySelection(normalized))
                        throw new InvalidOperationException($"Batch test runner run '{normalized.name}' must declare at least one selector unless allowEmptySelection is true.");

                    s_pendingRuns.Enqueue(normalized);
                }

                s_expectedRunCount = s_pendingRuns.Count;

                s_testRunnerApi = ScriptableObject.CreateInstance<TestRunnerApi>();
                s_callbackForwarder = new CallbackForwarder();
                s_testRunnerApi.RegisterCallbacks(s_callbackForwarder);

                EditorApplication.delayCall += StartNextRun;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorApplication.Exit(1);
            }
        }

        private static void ResetStaticState()
        {
            s_pendingRuns.Clear();
            s_completedRunCount = 0;
            s_expectedRunCount = 0;
            s_exitCode = 0;
            s_activeRun = null;
            s_completedRunResults.Clear();

            if (s_testRunnerApi != null && s_callbackForwarder != null)
                s_testRunnerApi.UnregisterCallbacks(s_callbackForwarder);

            if (s_testRunnerApi != null)
                UnityEngine.Object.DestroyImmediate(s_testRunnerApi);

            s_testRunnerApi = null;
            s_callbackForwarder = null;
            s_resultsDirectory = null;
        }

        internal static AsmLiteBatchRunDefinition NormalizeRun(AsmLiteBatchRunDefinition run, int index)
        {
            var normalized = run ?? new AsmLiteBatchRunDefinition();
            normalized.name = string.IsNullOrWhiteSpace(normalized.name)
                ? $"run-{index + 1:D2}"
                : normalized.name.Trim();
            normalized.resultFile = string.IsNullOrWhiteSpace(normalized.resultFile)
                ? $"{normalized.name}.xml"
                : normalized.resultFile.Trim();
            normalized.testNames = NormalizeStringArray(normalized.testNames);
            normalized.groupNames = NormalizeStringArray(normalized.groupNames);
            normalized.categoryNames = NormalizeStringArray(normalized.categoryNames);
            normalized.assemblyNames = NormalizeStringArray(normalized.assemblyNames);
            normalized.filters = NormalizeFilters(normalized.filters);

            var legacyFilter = CreateFilterDefinition(
                normalized.testNames,
                normalized.groupNames,
                normalized.categoryNames,
                normalized.assemblyNames);

            if (normalized.filters.Length == 0)
            {
                if (HasAnySelection(legacyFilter))
                    normalized.filters = new[] { legacyFilter };
            }
            else if (HasAnySelection(legacyFilter))
            {
                var filtersWithLegacy = new AsmLiteBatchFilterDefinition[normalized.filters.Length + 1];
                normalized.filters.CopyTo(filtersWithLegacy, 0);
                filtersWithLegacy[normalized.filters.Length] = legacyFilter;
                normalized.filters = filtersWithLegacy;
            }

            return normalized;
        }

        internal static bool RunHasAnySelection(AsmLiteBatchRunDefinition run)
        {
            return run?.filters != null && run.filters.Any(HasAnySelection);
        }

        internal static Filter[] BuildExecutionFilters(AsmLiteBatchRunDefinition run)
        {
            if (run?.filters == null || run.filters.Length == 0)
            {
                return new[]
                {
                    new Filter
                    {
                        testMode = TestMode.EditMode,
                        testNames = Array.Empty<string>(),
                        groupNames = Array.Empty<string>(),
                        categoryNames = Array.Empty<string>(),
                        assemblyNames = Array.Empty<string>(),
                    }
                };
            }

            var filters = new Filter[run.filters.Length];
            for (int i = 0; i < run.filters.Length; i++)
            {
                var definition = run.filters[i] ?? new AsmLiteBatchFilterDefinition();
                filters[i] = new Filter
                {
                    testMode = TestMode.EditMode,
                    testNames = NormalizeStringArray(definition.testNames),
                    groupNames = NormalizeStringArray(definition.groupNames),
                    categoryNames = NormalizeStringArray(definition.categoryNames),
                    assemblyNames = NormalizeStringArray(definition.assemblyNames),
                };
            }

            return filters;
        }

        private static AsmLiteBatchFilterDefinition[] NormalizeFilters(AsmLiteBatchFilterDefinition[] filters)
        {
            if (filters == null || filters.Length == 0)
                return Array.Empty<AsmLiteBatchFilterDefinition>();

            return filters
                .Where(filter => filter != null)
                .Select(filter => CreateFilterDefinition(
                    filter.testNames,
                    filter.groupNames,
                    filter.categoryNames,
                    filter.assemblyNames))
                .ToArray();
        }

        private static AsmLiteBatchFilterDefinition CreateFilterDefinition(
            string[] testNames,
            string[] groupNames,
            string[] categoryNames,
            string[] assemblyNames)
        {
            return new AsmLiteBatchFilterDefinition
            {
                testNames = NormalizeStringArray(testNames),
                groupNames = NormalizeStringArray(groupNames),
                categoryNames = NormalizeStringArray(categoryNames),
                assemblyNames = NormalizeStringArray(assemblyNames),
            };
        }

        private static bool HasAnySelection(AsmLiteBatchFilterDefinition filter)
        {
            return filter != null
                && (filter.testNames.Length > 0
                    || filter.groupNames.Length > 0
                    || filter.categoryNames.Length > 0
                    || filter.assemblyNames.Length > 0);
        }

        private static string[] NormalizeStringArray(string[] values)
        {
            return values == null
                ? Array.Empty<string>()
                : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();
        }

        private static void StartNextRun()
        {
            EditorApplication.delayCall -= StartNextRun;

            try
            {
                if (s_pendingRuns.Count == 0)
                {
                    CompleteAndExit();
                    return;
                }

                s_activeRun = s_pendingRuns.Dequeue();
                var filters = BuildExecutionFilters(s_activeRun);
                var settings = new ExecutionSettings(filters)
                {
                    runSynchronously = s_activeRun.runSynchronously,
                };

                Debug.Log($"[ASM-Lite] Starting batch test run '{s_activeRun.name}' ({s_completedRunCount + 1} of {s_completedRunCount + s_pendingRuns.Count + 1}) with {filters.Length} filter selection(s).");
                s_testRunnerApi.Execute(settings);
            }
            catch (Exception ex)
            {
                s_exitCode = 1;
                Debug.LogException(ex);
                CompleteAndExit();
            }
        }

        private static void HandleRunFinished(ITestResultAdaptor result)
        {
            try
            {
                if (s_activeRun == null)
                    throw new InvalidOperationException("Batch test runner received RunFinished without an active run.");

                string resultPath = ResolveResultPath(s_activeRun.resultFile);
                XmlDocument resultDocument = ConvertResultToXmlDocument(result);
                WriteXmlDocument(resultDocument, resultPath);
                s_completedRunResults.Add(new CompletedRunResult
                {
                    name = s_activeRun.name,
                    document = resultDocument,
                });
                s_completedRunCount++;

                if (result.FailCount > 0 || result.TestStatus == TestStatus.Failed || (result.ResultState?.StartsWith("Failed", StringComparison.Ordinal) ?? false))
                    s_exitCode = 1;

                Debug.Log($"[ASM-Lite] Finished batch test run '{s_activeRun.name}' -> {resultPath}");
            }
            catch (Exception ex)
            {
                s_exitCode = 1;
                Debug.LogException(ex);
            }
            finally
            {
                s_activeRun = null;
                EditorApplication.delayCall += StartNextRun;
            }
        }

        private static string ResolveResultPath(string resultFile)
        {
            if (Path.IsPathRooted(resultFile))
                return resultFile;

            return Path.Combine(s_resultsDirectory, resultFile);
        }

        private static XmlDocument ConvertResultToXmlDocument(ITestResultAdaptor result)
        {
            var toXmlMethod = result.GetType().GetMethod("ToXml", Type.EmptyTypes);
            if (toXmlMethod == null)
                throw new MissingMethodException(result.GetType().FullName, "ToXml");

            object xmlObject = toXmlMethod.Invoke(result, null);
            var document = new XmlDocument();
            switch (xmlObject)
            {
                case XmlDocument xmlDocument:
                    document.LoadXml(xmlDocument.OuterXml);
                    break;
                case XmlNode xmlNode:
                    document.LoadXml(xmlNode.OuterXml);
                    break;
                default:
                    string xmlText = xmlObject?.ToString();
                    if (string.IsNullOrWhiteSpace(xmlText))
                        throw new InvalidOperationException($"Test result adapter '{result.GetType().FullName}' returned an empty XML payload.");

                    document.LoadXml(xmlText);
                    break;
            }

            return document;
        }

        private static void WriteXmlDocument(XmlDocument document, string resultPath)
        {
            string directory = Path.GetDirectoryName(resultPath) ?? s_resultsDirectory;
            Directory.CreateDirectory(directory);

            string tempPath = Path.Combine(directory, Path.GetFileName(resultPath) + ".tmp");
            if (File.Exists(tempPath))
                File.Delete(tempPath);

            document.Save(tempPath);
            if (File.Exists(resultPath))
                File.Delete(resultPath);

            File.Move(tempPath, resultPath);
        }

        internal static XmlDocument BuildCanonicalResultsDocument(IReadOnlyList<XmlDocument> runDocuments)
        {
            if (runDocuments == null || runDocuments.Count == 0)
                throw new InvalidOperationException("Batch test runner requires at least one completed result document to build canonical NUnit output.");

            if (runDocuments.Count == 1)
            {
                var singleRunDocument = new XmlDocument();
                singleRunDocument.LoadXml(runDocuments[0].OuterXml);
                return singleRunDocument;
            }

            var aggregateDocument = new XmlDocument();
            aggregateDocument.AppendChild(aggregateDocument.CreateXmlDeclaration("1.0", "utf-8", null));

            XmlElement aggregateRoot = aggregateDocument.CreateElement("test-run");
            aggregateDocument.AppendChild(aggregateRoot);

            XmlElement firstRoot = runDocuments[0].DocumentElement
                ?? throw new InvalidOperationException("Batch test runner cannot aggregate a result document without a root element.");

            CopyAttributeIfPresent(firstRoot, aggregateRoot, "id");
            CopyAttributeIfPresent(firstRoot, aggregateRoot, "engine-version");
            CopyAttributeIfPresent(firstRoot, aggregateRoot, "clr-version");

            int testcaseCount = 0;
            int total = 0;
            int passed = 0;
            int failed = 0;
            int inconclusive = 0;
            int skipped = 0;
            int asserts = 0;
            double duration = 0d;
            DateTimeOffset? earliestStart = null;
            DateTimeOffset? latestEnd = null;

            foreach (XmlDocument runDocument in runDocuments)
            {
                XmlElement runRoot = runDocument.DocumentElement
                    ?? throw new InvalidOperationException("Batch test runner cannot aggregate a result document without a root element.");
                if (!string.Equals(runRoot.Name, "test-run", StringComparison.Ordinal))
                    throw new InvalidOperationException($"Batch test runner expected NUnit root element 'test-run' but received '{runRoot.Name}'.");

                testcaseCount += ReadIntAttribute(runRoot, "testcasecount");
                total += ReadIntAttribute(runRoot, "total");
                passed += ReadIntAttribute(runRoot, "passed");
                failed += ReadIntAttribute(runRoot, "failed");
                inconclusive += ReadIntAttribute(runRoot, "inconclusive");
                skipped += ReadIntAttribute(runRoot, "skipped");
                asserts += ReadIntAttribute(runRoot, "asserts");
                duration += ReadDoubleAttribute(runRoot, "duration");

                DateTimeOffset? start = ReadDateTimeOffsetAttribute(runRoot, "start-time");
                if (!earliestStart.HasValue || (start.HasValue && start.Value < earliestStart.Value))
                    earliestStart = start;

                DateTimeOffset? end = ReadDateTimeOffsetAttribute(runRoot, "end-time");
                if (!latestEnd.HasValue || (end.HasValue && end.Value > latestEnd.Value))
                    latestEnd = end;

                foreach (XmlNode childNode in runRoot.ChildNodes)
                {
                    if (childNode.NodeType != XmlNodeType.Element)
                        continue;

                    aggregateRoot.AppendChild(aggregateDocument.ImportNode(childNode, true));
                }
            }

            aggregateRoot.SetAttribute("testcasecount", testcaseCount.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("total", total.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("passed", passed.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("failed", failed.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("inconclusive", inconclusive.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("skipped", skipped.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("asserts", asserts.ToString(CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("duration", duration.ToString("0.#######", CultureInfo.InvariantCulture));
            aggregateRoot.SetAttribute("result", failed > 0 ? "Failed" : inconclusive > 0 ? "Inconclusive" : "Passed");

            if (earliestStart.HasValue)
                aggregateRoot.SetAttribute("start-time", earliestStart.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture));
            if (latestEnd.HasValue)
                aggregateRoot.SetAttribute("end-time", latestEnd.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ssZ", CultureInfo.InvariantCulture));

            return aggregateDocument;
        }

        private static void CopyAttributeIfPresent(XmlElement source, XmlElement destination, string attributeName)
        {
            string value = source.GetAttribute(attributeName);
            if (!string.IsNullOrWhiteSpace(value))
                destination.SetAttribute(attributeName, value);
        }

        private static int ReadIntAttribute(XmlElement element, string attributeName)
        {
            return int.TryParse(element.GetAttribute(attributeName), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : 0;
        }

        private static double ReadDoubleAttribute(XmlElement element, string attributeName)
        {
            return double.TryParse(element.GetAttribute(attributeName), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
                ? value
                : 0d;
        }

        private static DateTimeOffset? ReadDateTimeOffsetAttribute(XmlElement element, string attributeName)
        {
            return DateTimeOffset.TryParse(
                element.GetAttribute(attributeName),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out DateTimeOffset value)
                ? value
                : (DateTimeOffset?)null;
        }

        private static void WriteCanonicalResultsIfComplete()
        {
            if (s_expectedRunCount <= 0)
                return;

            if (s_completedRunResults.Count != s_expectedRunCount)
            {
                Debug.LogWarning($"[ASM-Lite] Skipping canonical NUnit result output because only {s_completedRunResults.Count} of {s_expectedRunCount} batch runs completed.");
                return;
            }

            var canonicalDocument = BuildCanonicalResultsDocument(s_completedRunResults.Select(run => run.document).ToArray());
            WriteXmlDocument(canonicalDocument, ResolveResultPath(CanonicalResultsFileName));
        }

        private static void CompleteAndExit()
        {
            try
            {
                WriteCanonicalResultsIfComplete();

                if (s_testRunnerApi != null && s_callbackForwarder != null)
                    s_testRunnerApi.UnregisterCallbacks(s_callbackForwarder);
            }
            catch (Exception ex)
            {
                s_exitCode = 1;
                Debug.LogException(ex);
            }
            finally
            {
                if (s_testRunnerApi != null)
                    UnityEngine.Object.DestroyImmediate(s_testRunnerApi);

                s_testRunnerApi = null;
                s_callbackForwarder = null;
                EditorApplication.Exit(s_exitCode);
            }
        }

        private sealed class CompletedRunResult
        {
            public string name;
            public XmlDocument document;
        }

        private sealed class CallbackForwarder : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                HandleRunFinished(result);
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
            }
        }
    }

    [TestFixture]
    internal sealed class ASMLiteBatchTestRunnerTests
    {
        [Test]
        public void NormalizeRun_UsesExplicitFilters_AndAppendsLegacySelectors()
        {
            var run = new AsmLiteBatchRunDefinition
            {
                name = "  mixed-run  ",
                resultFile = "  custom-results.xml  ",
                filters = new[]
                {
                    new AsmLiteBatchFilterDefinition
                    {
                        groupNames = new[] { "  ^ASMLite\\.Tests\\.Editor\\.ASMLiteBuilderTests(?:\\.|$)  " }
                    }
                },
                testNames = new[] { "  ASMLite.Tests.Editor.ASMLitePrefabWiringTests.W02_HasStalePrmsEntry_DetectsLegacyPrmsNames_AndIgnoresOtherNames  " },
            };

            var normalized = ASMLiteBatchTestRunner.NormalizeRun(run, 0);

            Assert.AreEqual("mixed-run", normalized.name);
            Assert.AreEqual("custom-results.xml", normalized.resultFile);
            Assert.AreEqual(2, normalized.filters.Length,
                "Legacy selector fields should be appended as an extra OR filter when explicit filters are present.");
            Assert.AreEqual("^ASMLite\\.Tests\\.Editor\\.ASMLiteBuilderTests(?:\\.|$)", normalized.filters[0].groupNames[0]);
            Assert.AreEqual(
                "ASMLite.Tests.Editor.ASMLitePrefabWiringTests.W02_HasStalePrmsEntry_DetectsLegacyPrmsNames_AndIgnoresOtherNames",
                normalized.filters[1].testNames[0]);
        }

        [Test]
        public void NormalizeRun_WhenNoSelectorsProvided_LeavesFiltersEmpty_AndReportsNoSelection()
        {
            var run = ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition(), 1);

            Assert.IsEmpty(run.filters,
                "Runs without explicit selectors should stay empty after normalization so accidental full-suite execution is rejected by default.");
            Assert.IsFalse(ASMLiteBatchTestRunner.RunHasAnySelection(run),
                "Runs without selectors should report no configured selection after normalization.");
        }

        [Test]
        public void BuildExecutionFilters_WhenNoSelectorsProvided_ReturnsSingleDefaultEditModeFilter()
        {
            var run = ASMLiteBatchTestRunner.NormalizeRun(new AsmLiteBatchRunDefinition
            {
                allowEmptySelection = true,
            }, 1);

            var filters = ASMLiteBatchTestRunner.BuildExecutionFilters(run);

            Assert.AreEqual(1, filters.Length);
            Assert.AreEqual(TestMode.EditMode, filters[0].testMode);
            Assert.IsEmpty(filters[0].testNames);
            Assert.IsEmpty(filters[0].groupNames);
            Assert.IsEmpty(filters[0].categoryNames);
            Assert.IsEmpty(filters[0].assemblyNames);
        }

        [Test]
        public void BuildCanonicalResultsDocument_MergesCompletedRuns_IntoCanonicalNUnitOutput()
        {
            var first = new XmlDocument();
            first.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""1"" testcasecount=""2"" result=""Passed"" total=""2"" passed=""2"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" engine-version=""3.5.0.0"" clr-version=""4.0.30319.42000"" start-time=""2026-04-17 01:00:00Z"" end-time=""2026-04-17 01:00:10Z"" duration=""10.5"">
  <test-suite type=""TestSuite"" id=""11"" name=""core"" result=""Passed"" total=""2"" passed=""2"" failed=""0"" inconclusive=""0"" skipped=""0"" asserts=""1"" />
</test-run>");

            var second = new XmlDocument();
            second.LoadXml(@"<?xml version=""1.0"" encoding=""utf-8""?>
<test-run id=""2"" testcasecount=""1"" result=""Failed"" total=""1"" passed=""0"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"" engine-version=""3.5.0.0"" clr-version=""4.0.30319.42000"" start-time=""2026-04-17 01:00:05Z"" end-time=""2026-04-17 01:00:20Z"" duration=""2.25"">
  <test-suite type=""TestSuite"" id=""12"" name=""integration"" result=""Failed"" total=""1"" passed=""0"" failed=""1"" inconclusive=""0"" skipped=""0"" asserts=""2"" />
</test-run>");

            XmlDocument aggregate = ASMLiteBatchTestRunner.BuildCanonicalResultsDocument(new[] { first, second });
            XmlElement root = aggregate.DocumentElement;

            Assert.IsNotNull(root);
            Assert.AreEqual("test-run", root.Name);
            Assert.AreEqual("3", root.GetAttribute("testcasecount"));
            Assert.AreEqual("3", root.GetAttribute("total"));
            Assert.AreEqual("2", root.GetAttribute("passed"));
            Assert.AreEqual("1", root.GetAttribute("failed"));
            Assert.AreEqual("3", root.GetAttribute("asserts"));
            Assert.AreEqual("Failed", root.GetAttribute("result"));
            Assert.AreEqual("2026-04-17 01:00:00Z", root.GetAttribute("start-time"));
            Assert.AreEqual("2026-04-17 01:00:20Z", root.GetAttribute("end-time"));
            Assert.AreEqual(2, root.SelectNodes("test-suite")?.Count ?? 0,
                "Canonical NUnit output should preserve each completed run as a top-level imported suite.");
        }
    }
}
