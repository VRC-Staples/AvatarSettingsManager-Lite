using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    // Explicit native acceptance entry point. Never called by smoke-host startup.
    internal static class ASMLiteSmokeFixtureRecoveryRestartProbe
    {
        [Serializable] private sealed class Context
        {
            public int capturePid;
            public string phase, recovery, originalRoot;
        }
        [Serializable] private sealed class Baseline
        {
            public SceneSetup[] scenes;
            public string[] selection;
            public string active;
            public Stage[] trees;
        }
        [Serializable] private sealed class Stage { public string stage; }
        [Serializable] private sealed class Pair { public string original, current; }
        [Serializable] private sealed class Map { public List<Pair> pairs; }
        [Serializable] private sealed class Result
        {
            public int pid;
            public string phase, recovery, hostState, originalIdResolvedName;
            public bool passed;
            public string[] checks;
        }
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        private static string Evidence => Path.Combine(Root, ".artifacts/restart-acceptance");
        private static readonly List<string> Checks = new List<string>();

        public static void Run()
        {
            string phase = Environment.GetEnvironmentVariable("ASMLITE_FIXTURE_RECOVERY_PHASE");
            Directory.CreateDirectory(Evidence);
            try
            {
                if (phase == "mutation-gap" || phase == "restoration-gap") Capture(phase);
                else Restart(phase);
                EditorApplication.Exit(0);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Write(Path.Combine(Evidence, phase + "-failure.txt"), ex.ToString());
                EditorApplication.Exit(2);
            }
        }

        private static void Capture(string phase)
        {
            Need(ASMLiteSmokeSetupFixtureService.CheckRecoveryAdmission(out string admission), admission);
            // This entry point is restricted by the driver to its isolated test project.
            Scene a = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AsmLiteTestContext context = ASMLiteTestFixtures.CreateTestAvatar();
            context.AvatarGo.name = "RestartFixtureAvatar";
            var child = new GameObject("Duplicate");
            child.transform.SetParent(context.Comp.transform, false);
            child.SetActive(false);
            var inside = context.Comp.gameObject.AddComponent<ASMLiteSmokeFixtureReferenceProbe>();
            inside.number = 73;
            inside.text = "cold-non-default";
            inside.internalReference = child;
            inside.data = new ASMLiteSmokeFixtureReferenceProbe.Graph { value = 19, reference = child };
            inside.data.next = inside.data;
            Type vfType = AppDomain.CurrentDomain.GetAssemblies()
                .Where(assembly => assembly != typeof(VF.Model.VRCFury).Assembly)
                .Select(assembly => assembly.GetType("VF.Model.VRCFury")).First(type => type != null);
            Component vf = context.Comp.gameObject.AddComponent(vfType);
            var serialized = new SerializedObject(vf);
            serialized.FindProperty("content").managedReferenceValue = Activator.CreateInstance(vfType.Assembly.GetType("VF.Model.Feature.FullController"), true);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
            serialized.FindProperty("content.toggleParam").stringValue = "ColdCapturedToggle";
            serialized.FindProperty("content.rootObjOverride").objectReferenceValue = child;
            serialized.FindProperty("content.allowMissingAssets").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var outsideObject = new GameObject("InboundObserver");
            var outside = outsideObject.AddComponent<ASMLiteSmokeFixtureReferenceProbe>();
            outside.internalReference = child;
            inside.externalReference = outsideObject;
            AssetDatabase.SaveAssets();
            Need(EditorSceneManager.SaveScene(a, "Assets/RestartFixtureA.unity"), "saved native fixture scene");
            Scene b = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            new GameObject("UnrelatedInSecondScene");
            Need(EditorSceneManager.SaveScene(b, "Assets/RestartFixtureB.unity"), "saved additive scene");
            Scene c = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            new GameObject("UnloadedSceneState");
            Need(EditorSceneManager.SaveScene(c, "Assets/RestartFixtureC.unity"), "saved unloaded scene seed");
            EditorSceneManager.CloseScene(c, true);
            EditorSceneManager.OpenScene("Assets/RestartFixtureC.unity", OpenSceneMode.AdditiveWithoutLoading);
            SceneManager.SetActiveScene(b);
            Selection.activeObject = child;
            Selection.objects = new Object[] { child, context.Comp.gameObject, inside };
            Need(Selection.objects.Length == 3, "full selection admitted");
            var service = new ASMLiteSmokeSetupFixtureService();
            string assetsBefore = AssetsState();
            var args = new ASMLiteSmokeStepArgs { avatarName = "RestartFixtureAvatar", fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent };
            string original = GlobalObjectId.GetGlobalObjectIdSlow(context.Comp.gameObject).ToString();
            Need(service.ApplyMutation(args, a.path, "RestartFixtureAvatar", out string detail), detail);
            Need(AssetsState() == assetsBefore, "capture and in-memory mutation leave Assets bytes and metadata unchanged");
            var actionObject = new GameObject("UnrelatedActionObject");
            SceneManager.MoveGameObjectToScene(actionObject, a);
            outside.number = 29;
            Need(EditorSceneManager.SaveScene(a), "unrelated action state saved separately");
            var state = new Context { capturePid = System.Diagnostics.Process.GetCurrentProcess().Id,
                phase = phase, recovery = service.RecoveryPath, originalRoot = original };
            Write(Path.Combine(Evidence, "context.json"), JsonUtility.ToJson(state, true));
            if (phase == "restoration-gap")
            {
                service.BeforeProofPublicationForTesting = () => Prepared(state);
                Need(service.Reset(out detail), detail);
                throw new InvalidOperationException("Driver failed to interrupt proof publication.");
            }
            Prepared(state);
        }

        private static void Prepared(Context state)
        {
            Need(File.Exists(Path.Combine(state.recovery, "pending-0000.json")), "durable pending precedes interruption");
            Need(!File.Exists(Path.Combine(state.recovery, "verified.json")), "clearance has not been published");
            if (state.phase == "restoration-gap")
            {
                Need(File.Exists(Path.Combine(state.recovery, "correspondence.json")), "replacement correspondence published before proof");
                string manifest = Directory.GetFiles(state.recovery, "baseline-*.json").OrderBy(member => member, StringComparer.Ordinal).Last();
                var baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(manifest));
                Need(baseline.trees.All(tree => !File.Exists(Path.Combine(Root, tree.stage))
                    && !File.Exists(Path.Combine(Root, tree.stage + ".meta")) && !SceneManager.GetSceneByPath(tree.stage).IsValid()), "owned staging completely removed");
            }
            Write(Path.Combine(Evidence, state.phase + "-prepared.json"), JsonUtility.ToJson(new Result
            {
                pid = state.capturePid, phase = state.phase, recovery = state.recovery, passed = true, checks = Checks.ToArray()
            }, true));
            // The driver terminates only the child it created, after reading this durable barrier.
            while (true) Thread.Sleep(1000);
        }

        private static void Restart(string phase)
        {
            Context context = JsonUtility.FromJson<Context>(File.ReadAllText(Path.Combine(Evidence, "context.json")));
            Need(context.capturePid != System.Diagnostics.Process.GetCurrentProcess().Id, "distinct cold process");
            string before = EditorState();
            string recoveryBefore = RecoveryState(context.recovery);
            Need(!ASMLiteSmokeSetupFixtureService.CheckRecoveryAdmission(out string admission), "pending admission blocked: " + admission);
            Need(EditorState() == before && RecoveryState(context.recovery) == recoveryBefore, "startup admission is read-only");
            var config = new ASMLiteSmokeOverlayHostConfiguration
            {
                SessionRootPath = Path.Combine(Evidence, phase + "-host"),
                CatalogPath = Path.Combine(Root, "Tools/ci/smoke/suite-catalog.json"),
                ScenePath = "Assets/RestartFixtureA.unity", AvatarName = "RestartFixtureAvatar",
                StartupTimeoutSeconds = 120, HeartbeatSeconds = 5, ExitOnReady = false
            };
            var paths = ASMLiteSmokeArtifactPaths.FromSessionRoot(config.SessionRootPath);
            var runner = ASMLiteSmokeOverlayHost.CreateRunnerForTesting(config, ASMLiteSmokeOverlayHostUnityRuntime.Instance);
            runner.Start();
            var host = JsonUtility.FromJson<ASMLiteSmokeHostStateDocument>(File.ReadAllText(paths.HostStatePath));
            runner.StopForTesting();
            Need(host.state == ASMLiteSmokeProtocol.HostStateCrashed && host.message.Contains("SETUP_FIXTURE_RECOVERY_BLOCKED"), "native host cannot publish ready");
            Need(EditorState() == before && RecoveryState(context.recovery) == recoveryBefore, "native host startup does not restore or alter operator state");
            if (phase == "restart-corrupt" || phase == "restart-incomplete")
            {
                Need(admission.Contains(phase == "restart-corrupt" ? "Immutable scene recovery copy is corrupt" : "Incomplete fixture recovery preparation"),
                    "invalid-record diagnostic identifies the injected fault");
                Need(!ASMLiteSmokeSetupFixtureService.VerifyRepairedFixtureBaseline(out _), "invalid recovery cannot be manually cleared");
                var emptyService = new ASMLiteSmokeSetupFixtureService();
                Need(!emptyService.Reset(out _) && !emptyService.HasCleanResetProof, "invalid recovery cannot be cleared by empty reset");
                Need(EditorState() == before && RecoveryState(context.recovery) == recoveryBefore, "invalid-record checks remain read-only");
                Write(Path.Combine(Evidence, phase + "-result.json"), JsonUtility.ToJson(new Result
                {
                    pid = System.Diagnostics.Process.GetCurrentProcess().Id, phase = phase, recovery = context.recovery,
                    hostState = host.state, passed = true, checks = Checks.ToArray()
                }, true));
                return;
            }
            // Operator load/selection repair is explicit and separate from admission and proof.
            string baselineFile = Directory.GetFiles(context.recovery, "baseline-*.json").OrderBy(member => member, StringComparer.Ordinal).Last();
            var baseline = JsonUtility.FromJson<Baseline>(File.ReadAllText(baselineFile));
            EditorSceneManager.RestoreSceneManagerSetup(baseline.scenes);
            string mapPath = Path.Combine(context.recovery, "correspondence.json");
            if (!File.Exists(mapPath)) mapPath = Directory.GetFiles(context.recovery, "capture-correspondence-*.json").OrderBy(member => member, StringComparer.Ordinal).Last();
            var map = JsonUtility.FromJson<Map>(File.ReadAllText(mapPath));
            Func<string, Object> resolve = original => Resolve(map.pairs.Single(pair => pair.original == original).current);
            Selection.activeObject = null;
            Selection.objects = new Object[0];
            before = EditorState();
            recoveryBefore = RecoveryState(context.recovery);
            Need(!ASMLiteSmokeSetupFixtureService.VerifyRepairedFixtureBaseline(out string mismatch), "mismatch remains blocked: " + mismatch);
            Need(EditorState() == before && RecoveryState(context.recovery) == recoveryBefore, "failed manual proof is comparison-only");
            string resolvedName = Resolve(context.originalRoot)?.name ?? "<missing>";
            if (context.phase == "restoration-gap")
            {
                Selection.activeObject = string.IsNullOrEmpty(baseline.active) ? null : resolve(baseline.active);
                Selection.objects = baseline.selection.Select(resolve).ToArray();
                Need(Selection.objects.Length == 3, "operator restored full captured selection");
                before = EditorState();
                Need(ASMLiteSmokeSetupFixtureService.VerifyRepairedFixtureBaseline(out string verified), verified);
                Need(EditorState() == before, "successful manual proof does not load, save, restore or change selection");
                Need(ASMLiteSmokeSetupFixtureService.CheckRecoveryAdmission(out admission), admission);
                var inside = Selection.objects.OfType<ASMLiteSmokeFixtureReferenceProbe>().Single();
                Need(inside.number == 73 && inside.text == "cold-non-default", "non-default serialized data survived cold restart");
                Need(ReferenceEquals(inside.data.next, inside.data) && inside.data.value == 19 && inside.data.reference == inside.internalReference, "cyclic shared managed data and internal reference survived cold restart");
                var outside = GameObject.Find("InboundObserver").GetComponent<ASMLiteSmokeFixtureReferenceProbe>();
                Need(outside.number == 29 && outside.internalReference == inside.internalReference, "inbound reference restored; unrelated action value preserved");
                Need(GameObject.Find("UnrelatedActionObject") != null, "unrelated action object preserved");
                Component vf = inside.gameObject.GetComponents<Component>().Single(item => item.GetType().FullName == "VF.Model.VRCFury");
                var serialized = new SerializedObject(vf);
                Need(serialized.FindProperty("content.toggleParam").stringValue == "ColdCapturedToggle"
                    && serialized.FindProperty("content.rootObjOverride").objectReferenceValue == inside.internalReference,
                    "actual VRCFury managed payload and scene reference survived cold restart");
            }
            else
            {
                Need(!File.Exists(Path.Combine(context.recovery, "verified.json")), "interrupted mutation remains pending");
                Need(!ASMLiteSmokeSetupFixtureService.CheckRecoveryAdmission(out _), "empty/restart reset cannot bypass pending proof");
                Need(!new ASMLiteSmokeSetupFixtureService().Reset(out _), "new-service empty reset remains blocked");
            }
            Write(Path.Combine(Evidence, phase + "-result.json"), JsonUtility.ToJson(new Result
            {
                pid = System.Diagnostics.Process.GetCurrentProcess().Id, phase = phase, recovery = context.recovery,
                hostState = host.state, originalIdResolvedName = resolvedName, passed = true, checks = Checks.ToArray()
            }, true));
        }

        private static Object Resolve(string id) => GlobalObjectId.TryParse(id, out var parsed) ? GlobalObjectId.GlobalObjectIdentifierToObjectSlow(parsed) : null;
        private static void Need(bool value, string detail)
        {
            if (!value) throw new InvalidOperationException(detail);
            Checks.Add(detail);
        }
        private static string AssetsState() => Hash(string.Join("\n", Directory.GetFiles(Application.dataPath, "*", SearchOption.AllDirectories)
            .OrderBy(member => member, StringComparer.Ordinal).Select(member => member + "|" + FileHash(member))));
        private static string RecoveryState(string directory) => Hash(string.Join("\n", Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .OrderBy(member => member, StringComparer.Ordinal).Select(member => member + "|" + FileHash(member))));
        private static string EditorState()
        {
            var setup = EditorSceneManager.GetSceneManagerSetup();
            return AssetsState() + "|" + string.Join("|", setup.Select(item => item.path + ":" + item.isLoaded + ":" + item.isActive))
                + "|" + string.Join(",", Enumerable.Range(0, SceneManager.sceneCount).Select(index => SceneManager.GetSceneAt(index).isDirty))
                + "|" + string.Join(",", Selection.instanceIDs) + "|" + (Selection.activeObject == null ? 0 : Selection.activeObject.GetInstanceID())
                + "|" + Hash(string.Join("\n", Resources.FindObjectsOfTypeAll<Component>().Where(item => item != null && !EditorUtility.IsPersistent(item)
                    && item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded).OrderBy(item => item.GetInstanceID())
                    .Select(item => item.GetInstanceID() + "|" + EditorJsonUtility.ToJson(item))));
        }
        private static string FileHash(string path)
        {
            using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream));
        }
        private static string Hash(string text)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }
        private static void Write(string path, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None)) { file.Write(bytes, 0, bytes.Length); file.Flush(true); }
        }
    }
}
