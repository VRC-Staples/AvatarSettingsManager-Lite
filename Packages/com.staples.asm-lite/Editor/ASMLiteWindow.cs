using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ASMLite;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace ASMLite.Editor
{
    /// <summary>
    /// ASM-Lite main editor window. Opens via Tools → .Staples. → ASM-Lite.
    ///
    /// Provides:
    ///   • Avatar hierarchy picker
    ///   • Slot count configuration (editable before add; locked after)
    ///   • Status / diagnostics panel
    ///   • "Add ASM-Lite Prefab" button: adds prefab and immediately bakes assets
    ///   • "Rebuild ASM-Lite" button: re-bakes when prefab already present
    /// </summary>
    public class ASMLiteWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────

        private VRCAvatarDescriptor _selectedAvatar;
        private Vector2             _scrollPos;

        // Pending slot count: shown before the prefab is added, applied on add.
        private int _pendingSlotCount = 3;

        // Cached component reference: rebuilt when avatar or scene changes.
        private ASMLiteComponent _cachedComponent;

        // Cached LINQ Count() result: avoids per-repaint enumeration.
        // -1 means invalid; recomputed lazily in DrawStatus.
        private int _cachedCustomParamCount = -1;

        // Parameter count returned by the last successful build (post-VRCFury clone).
        // -1 means no build has run yet this session.
        private int _discoveredParamCount = -1;

        // Pending icon mode: shown before the prefab is added, applied on add.
        private IconMode _pendingIconMode = IconMode.MultiColor;

        // Pending gear index: shown before the prefab is added, applied on add.
        private int _pendingSelectedGearIndex = 0;

        // Pending custom icons: shown before the prefab is added, applied on add.
        private Texture2D[] _pendingCustomIcons = new Texture2D[3];

        // Pending action icon mode: shown before the prefab is added, applied on add.
        private ActionIconMode _pendingActionIconMode = ActionIconMode.Default;

        // Pending custom action icons: used when _pendingActionIconMode is Custom.
        private Texture2D _pendingCustomSaveIcon;
        private Texture2D _pendingCustomLoadIcon;
        private Texture2D _pendingCustomClearIcon;

        // Pending customization scaffold state: copied into new prefab instances
        // and refreshed from the selected avatar component when present.
        private bool _pendingUseCustomRootIcon = false;
        private Texture2D _pendingCustomRootIcon;
        private bool _pendingUseCustomRootName = false;
        private string _pendingCustomRootName = string.Empty;
        private bool _pendingUseCustomInstallPath = false;
        private string _pendingCustomInstallPath = string.Empty;
        private bool _pendingUseParameterExclusions = false;
        private string[] _pendingExcludedParameterNames = Array.Empty<string>();

        // ── Install Path Tree ─────────────────────────────────────────────────

        // Which nodes in the install-path tree are expanded (keyed by full path).
        private readonly HashSet<string> _expandedInstallPaths = new HashSet<string>(StringComparer.Ordinal);

        // Scroll position for the install-path tree view.
        private Vector2 _installPathTreeScrollPos;

        // User-draggable height of the install-path tree scroll area.
        private float _installPathTreeHeight = 240f;

        // True while the user is dragging the tree resize handle.
        private bool _isDraggingTreeResize;

        // ── Parameter Checklist ───────────────────────────────────────────────

        private Vector2 _paramChecklistScrollPos;
        private float _paramChecklistHeight = 160f;
        private bool _isDraggingParamResize;
        private string[] _cachedParamList;
        private VRCAvatarDescriptor _lastParamListAvatar;
        private ParamTreeNode _cachedParamTree;
        private readonly HashSet<string> _expandedParamMenuPaths = new HashSet<string>(StringComparer.Ordinal);

        // Cached tree; rebuilt when the selected avatar changes.
        private MenuTreeNode _cachedInstallPathTree;
        private VRCAvatarDescriptor _lastInstallPathTreeAvatar;

        // ── Wheel Preview Cache ───────────────────────────────────────────────

        // Resolved gear textures for the current settings. Rebuilt whenever
        // mode, color index, slot count, or custom icons change.
        private Texture2D[] _previewGearTextures;
        private Texture2D   _previewSaveIcon;
        private Texture2D   _previewLoadIcon;
        private Texture2D   _previewClearIcon;
        private Texture2D   _previewBackIcon;

        // Signature of the last preview build: used to detect staleness.
        private int    _previewSlotCount      = -1;
        private int    _previewIconMode       = -1;
        private int    _previewGearIndex      = -1;
        private int    _previewActionIconMode = -1;

        // Cached arrays for the main wheel. Rebuilt only when slot count or icons change.
        private Texture2D[] _mainWheelIcons;
        private string[]    _mainWheelLabels;

        // Sub-wheel arrays are invariant -- allocate once as static readonly.
        private static readonly string[] s_subWheelLabels = { "Back", "Save", "Load", "Clear" };

        // Fallback grey square drawn when a custom icon slot is unassigned.
        private Texture2D _previewFallback;

        // Bundled action icons: loaded once and held until domain reload.
        // These paths never change at runtime so there is no reason to reload
        // them on every preview cache invalidation.
        private Texture2D _cachedIconSave;
        private Texture2D _cachedIconLoad;
        private Texture2D _cachedIconClear;

        // ── Banner ────────────────────────────────────────────────────────────

        private const string BannerPath = "Packages/com.staples.asm-lite/Icons/banner.png";

        // ── Radial wheel style cache ──────────────────────────────────────────

        // Colors declared once -- Color is a struct but declaring as static readonly
        // makes the intent explicit and avoids accidental per-call reconstruction.
        private static readonly Color s_wheelColorMain   = new Color(0.14f, 0.18f, 0.20f);
        private static readonly Color s_wheelColorBorder = new Color(0.10f, 0.35f, 0.38f);
        private static readonly Color s_wheelColorInner  = new Color(0.21f, 0.24f, 0.27f);
        private static readonly Color s_separatorColor   = new Color(0.10f, 0.35f, 0.38f, 0.20f);

        // GUIStyle cached across repaints. Rebuilt lazily when null (domain reload).
        // Only fontSize is updated per call; cloning on every repaint is expensive.
        private GUIStyle _radialLabelStyle;
        // Loaded once on first draw, never reloaded mid-session.
        private Texture2D _bannerTexture;

        // ── Static GUIContent ─────────────────────────────────────────────────

        private static readonly GUIContent s_slotCountLabelActive =
            new GUIContent("Slot Count",
                "How many preset slots your avatar has. Each slot can hold a full snapshot of your settings.");

        private static readonly GUIContent s_slotCountLabelPending =
            new GUIContent("Slot Count",
                "How many preset slots to add. Each slot lets you save and load a full set of avatar settings.");

        // ── Open ──────────────────────────────────────────────────────────────

        [MenuItem("Tools/.Staples./ASM-Lite")]
        public static void Open()
        {
            var win = GetWindow<ASMLiteWindow>(title: ".Staples. ASM-Lite");
            win.minSize = new Vector2(600, 680);
            win.Show();
        }

        [MenuItem("Tools/.Staples./Dev Tools/Show ASM-Lite Package Binding")]
        public static void ShowPackageBinding()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ASMLiteComponent).Assembly);
            if (packageInfo == null)
            {
                Debug.LogWarning("[ASM-Lite] Package binding lookup failed: PackageInfo.FindForAssembly returned null.");
                return;
            }

            Debug.Log($"[ASM-Lite] Package binding: name='{packageInfo.name}', source={packageInfo.source}, version='{packageInfo.version}', assetPath='{packageInfo.assetPath}', resolvedPath='{packageInfo.resolvedPath}'.");
        }

        [MenuItem("Tools/.Staples./Dev Tools/VCC Embedded or Local Package Switcher...")]
        public static void OpenVccLocalPackageSwitcher()
        {
            ASMLiteVccSwitcherWindow.Open();
        }

        private static List<string> FindLocalPackageCandidates(string packageName, string embeddedPath)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var roots = new List<string>();
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userProfile))
            {
                roots.Add(Path.Combine(userProfile, "Documents"));
                roots.Add(Path.Combine(userProfile, "source", "repos"));
                roots.Add(Path.Combine(userProfile, "OneDrive", "Documents"));
            }

            // Also scan sibling directories around the current project root.
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (!string.IsNullOrEmpty(projectRoot))
            {
                string parent = Directory.GetParent(projectRoot)?.FullName;
                if (!string.IsNullOrEmpty(parent))
                    roots.Add(parent);
            }

            for (int i = 0; i < roots.Count; i++)
            {
                string root = roots[i];
                if (!Directory.Exists(root))
                    continue;

                foreach (var candidate in EnumeratePackageDirectories(root, packageName, maxDepth: 6, maxResults: 20))
                {
                    if (string.Equals(candidate, embeddedPath, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(candidate);
                }
            }

            return results.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IEnumerable<string> EnumeratePackageDirectories(string root, string packageName, int maxDepth, int maxResults)
        {
            var stack = new Stack<(string path, int depth)>();
            stack.Push((root, 0));
            int yielded = 0;

            while (stack.Count > 0 && yielded < maxResults)
            {
                var (current, depth) = stack.Pop();
                if (depth > maxDepth)
                    continue;

                string dirName = Path.GetFileName(current);
                string parentName = Path.GetFileName(Path.GetDirectoryName(current) ?? string.Empty);
                if (string.Equals(dirName, packageName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(parentName, "Packages", StringComparison.OrdinalIgnoreCase)
                    && File.Exists(Path.Combine(current, "package.json")))
                {
                    yielded++;
                    yield return Path.GetFullPath(current);
                    continue;
                }

                string[] children;
                try
                {
                    children = Directory.GetDirectories(current);
                }
                catch
                {
                    continue;
                }

                for (int i = 0; i < children.Length; i++)
                    stack.Push((children[i], depth + 1));
            }
        }

        private static string ResolveSelectedLocalPackagePath(string packageName, string selectedPath)
        {
            if (string.IsNullOrEmpty(selectedPath))
                return selectedPath;

            // Exact package folder selected.
            if (File.Exists(Path.Combine(selectedPath, "package.json")))
                return selectedPath;

            // Project root selected -> <root>/Packages/<packageName>
            string fromProjectRoot = Path.Combine(selectedPath, "Packages", packageName);
            if (File.Exists(Path.Combine(fromProjectRoot, "package.json")))
                return Path.GetFullPath(fromProjectRoot);

            // Packages folder selected -> <packages>/<packageName>
            string fromPackagesRoot = Path.Combine(selectedPath, packageName);
            if (File.Exists(Path.Combine(fromPackagesRoot, "package.json")))
                return Path.GetFullPath(fromPackagesRoot);

            return selectedPath;
        }

        private static bool ValidateLocalPackageDirectory(string expectedPackageName, string localPath, out string error)
        {
            error = null;
            if (!Directory.Exists(localPath))
            {
                error = $"directory does not exist: '{localPath}'.";
                return false;
            }

            string packageJsonPath = Path.Combine(localPath, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                error = $"selected folder is missing package.json: '{localPath}'.";
                return false;
            }

            string packageJson;
            try
            {
                packageJson = File.ReadAllText(packageJsonPath);
            }
            catch (Exception ex)
            {
                error = $"failed reading package.json: {ex.Message}";
                return false;
            }

            if (packageJson.IndexOf($"\"name\": \"{expectedPackageName}\"", StringComparison.OrdinalIgnoreCase) < 0
                && packageJson.IndexOf($"\"name\":\"{expectedPackageName}\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                error = $"package.json name does not match '{expectedPackageName}'.";
                return false;
            }

            return true;
        }

        private static bool TryRemoveEmbeddedPackageFolder(string projectRoot, string embeddedPath, out string note, out string error)
        {
            note = null;
            error = null;
            try
            {
                bool isLink = (File.GetAttributes(embeddedPath) & FileAttributes.ReparsePoint) != 0;
                if (isLink)
                {
                    Directory.Delete(embeddedPath, recursive: false);
                    note = "removed junction/symlink at embedded path";
                    return true;
                }

                string backupRoot = Path.Combine(projectRoot, ".asm-lite-package-backups");
                Directory.CreateDirectory(backupRoot);
                string backupName = Path.GetFileName(embeddedPath) + "__embedded_backup_" + DateTime.Now.ToString("yyyyMMddHHmmss");
                string backupPath = Path.Combine(backupRoot, backupName);
                Directory.Move(embeddedPath, backupPath);
                note = $"moved embedded folder to '{backupPath}'";
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool TrySetManifestDependency(string projectRoot, string packageName, string dependencyValue, out string error)
        {
            error = null;
            try
            {
                string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                if (!File.Exists(manifestPath))
                {
                    error = $"manifest.json not found at '{manifestPath}'.";
                    return false;
                }

                string json = File.ReadAllText(manifestPath);
                if (!RemoveTopLevelJsonObjectEntry(ref json, "dependencies", packageName))
                {
                    // no-op if entry wasn't present
                }

                if (!AddTopLevelJsonObjectEntry(ref json, "dependencies", packageName, dependencyValue, out var addError))
                {
                    error = addError;
                    return false;
                }

                string backupPath = manifestPath + ".bak";
                File.Copy(manifestPath, backupPath, overwrite: true);
                File.WriteAllText(manifestPath, json);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string ToUnityFileDependencyValue(string absolutePath)
        {
            string normalized = Path.GetFullPath(absolutePath)
                .Replace('\\', '/');

            return $"file:{normalized}";
        }

        private static bool AddTopLevelJsonObjectEntry(ref string json, string sectionName, string key, string value, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(json))
            {
                error = "json content was empty.";
                return false;
            }

            string sectionNeedle = $"\"{sectionName}\"";
            int sectionIndex = json.IndexOf(sectionNeedle, StringComparison.Ordinal);
            if (sectionIndex < 0)
            {
                error = $"section '{sectionName}' was not found.";
                return false;
            }

            int sectionBraceStart = json.IndexOf('{', sectionIndex);
            if (sectionBraceStart < 0)
            {
                error = $"section '{sectionName}' has no opening brace.";
                return false;
            }

            int sectionBraceEnd = FindMatchingBrace(json, sectionBraceStart);
            if (sectionBraceEnd < 0)
            {
                error = $"section '{sectionName}' has no matching closing brace.";
                return false;
            }

            string sectionBody = json.Substring(sectionBraceStart + 1, sectionBraceEnd - sectionBraceStart - 1);
            bool sectionEmpty = string.IsNullOrWhiteSpace(sectionBody);

            string escapedValue = value?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? string.Empty;
            string newEntry = $"\n    \"{key}\": \"{escapedValue}\"";

            if (sectionEmpty)
            {
                json = json.Insert(sectionBraceStart + 1, newEntry + "\n  ");
            }
            else
            {
                json = json.Insert(sectionBraceEnd, "," + newEntry);
            }

            return true;
        }

        private static bool TryRemoveVpmManifestPackageEntry(string projectRoot, string packageName)
        {
            try
            {
                string vpmManifestPath = Path.Combine(projectRoot, "Packages", "vpm-manifest.json");
                if (!File.Exists(vpmManifestPath))
                    return false;

                string json = File.ReadAllText(vpmManifestPath);
                bool changed = false;

                changed |= RemoveTopLevelJsonObjectEntry(ref json, "dependencies", packageName);
                changed |= RemoveTopLevelJsonObjectEntry(ref json, "locked", packageName);

                if (!changed)
                    return false;

                string backupPath = vpmManifestPath + ".bak";
                File.Copy(vpmManifestPath, backupPath, overwrite: true);
                File.WriteAllText(vpmManifestPath, json);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ASM-Lite] vpm-manifest cleanup skipped: {ex.Message}");
                return false;
            }
        }

        private static bool RemoveTopLevelJsonObjectEntry(ref string json, string sectionName, string key)
        {
            if (string.IsNullOrEmpty(json))
                return false;

            string sectionNeedle = $"\"{sectionName}\"";
            int sectionIndex = json.IndexOf(sectionNeedle, StringComparison.Ordinal);
            if (sectionIndex < 0)
                return false;

            int sectionBraceStart = json.IndexOf('{', sectionIndex);
            if (sectionBraceStart < 0)
                return false;

            int sectionBraceEnd = FindMatchingBrace(json, sectionBraceStart);
            if (sectionBraceEnd < 0)
                return false;

            string keyNeedle = $"\"{key}\"";
            int keyIndex = json.IndexOf(keyNeedle, sectionBraceStart, sectionBraceEnd - sectionBraceStart + 1, StringComparison.Ordinal);
            if (keyIndex < 0)
                return false;

            int entryStart = keyIndex;
            while (entryStart > sectionBraceStart && char.IsWhiteSpace(json[entryStart - 1])) entryStart--;
            if (entryStart > sectionBraceStart && json[entryStart - 1] == ',') entryStart--;

            int colonIndex = json.IndexOf(':', keyIndex);
            if (colonIndex < 0 || colonIndex > sectionBraceEnd)
                return false;

            int valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;

            int entryEnd = valueStart;
            if (entryEnd >= json.Length)
                return false;

            char startChar = json[entryEnd];
            if (startChar == '{')
            {
                int valueEnd = FindMatchingBrace(json, entryEnd);
                if (valueEnd < 0) return false;
                entryEnd = valueEnd + 1;
            }
            else if (startChar == '[')
            {
                int valueEnd = FindMatchingBracket(json, entryEnd);
                if (valueEnd < 0) return false;
                entryEnd = valueEnd + 1;
            }
            else
            {
                while (entryEnd < json.Length && json[entryEnd] != ',' && json[entryEnd] != '}') entryEnd++;
            }

            while (entryEnd < json.Length && char.IsWhiteSpace(json[entryEnd])) entryEnd++;
            if (entryEnd < json.Length && json[entryEnd] == ',') entryEnd++;

            json = json.Remove(entryStart, entryEnd - entryStart);
            return true;
        }

        private static int FindMatchingBrace(string text, int openIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = openIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        internal static int FindMatchingBracket(string text, int openIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = openIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (c == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (c == '"')
                        inString = false;

                    continue;
                }

                if (c == '"')
                {
                    inString = true;
                    continue;
                }

                if (c == '[') depth++;
                else if (c == ']')
                {
                    depth--;
                    if (depth == 0)
                        return i;
                }
            }

            return -1;
        }

        // ── Dev Tools apply helpers ───────────────────────────────────────────

        internal static void ApplySwitchToFileLocal(string projectRoot, string selectedLocalPath, string packageName)
        {
            bool isCurrentProject = string.Equals(
                projectRoot,
                Directory.GetParent(Application.dataPath)?.FullName,
                StringComparison.OrdinalIgnoreCase);

            string embeddedPath = Path.GetFullPath(Path.Combine(projectRoot, "Packages", packageName));
            string dependencyValue = ToUnityFileDependencyValue(selectedLocalPath);

            if (!TrySetManifestDependency(projectRoot, packageName, dependencyValue, out var manifestError))
            {
                Debug.LogError($"[ASM-Lite] Switch failed for '{Path.GetFileName(projectRoot)}': {manifestError}");
                return;
            }

            bool vpmChanged = TryRemoveVpmManifestPackageEntry(projectRoot, packageName);

            string embeddedRemovalNote = string.Empty;
            bool embeddedExists = Directory.Exists(embeddedPath)
                && !string.Equals(embeddedPath, Path.GetFullPath(selectedLocalPath), StringComparison.OrdinalIgnoreCase);

            if (embeddedExists)
            {
                if (isCurrentProject) AssetDatabase.Refresh();
                if (!TryRemoveEmbeddedPackageFolder(projectRoot, embeddedPath, out var removalNote, out var removalError))
                    Debug.LogWarning($"[ASM-Lite] Could not remove embedded folder '{embeddedPath}': {removalError}. Unity may still use the embedded copy.");
                else
                    embeddedRemovalNote = removalNote;
            }

            if (isCurrentProject)
            {
                Client.Resolve();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[ASM-Lite] [{Path.GetFileName(projectRoot)}] Switched to file dependency: {packageName} => '{dependencyValue}' ({(vpmChanged ? "removed stale vpm-manifest entry" : "no vpm-manifest change")}){(string.IsNullOrEmpty(embeddedRemovalNote) ? string.Empty : ". " + embeddedRemovalNote)}.");
        }

        internal static void ApplySwitchToEmbedded(string projectRoot, string packageName)
        {
            bool isCurrentProject = string.Equals(
                projectRoot,
                Directory.GetParent(Application.dataPath)?.FullName,
                StringComparison.OrdinalIgnoreCase);

            string embeddedPath = Path.GetFullPath(Path.Combine(projectRoot, "Packages", packageName));
            string restoredFrom = null;

            if (!Directory.Exists(embeddedPath))
            {
                string backupRoot = Path.Combine(projectRoot, ".asm-lite-package-backups");
                if (Directory.Exists(backupRoot))
                {
                    string latestBackup = Directory.GetDirectories(backupRoot, packageName + "__embedded_backup_*")
                        .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();

                    if (latestBackup != null)
                    {
                        try
                        {
                            Directory.Move(latestBackup, embeddedPath);
                            restoredFrom = latestBackup;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[ASM-Lite] Could not restore embedded backup '{latestBackup}': {ex.Message}. Unity will use VCC/registry package instead.");
                        }
                    }
                }
            }

            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(ASMLiteComponent).Assembly);
            string targetVersion = !string.IsNullOrWhiteSpace(packageInfo?.version) ? packageInfo.version : "1.0.9";

            if (!TrySetManifestDependency(projectRoot, packageName, targetVersion, out var manifestError))
            {
                Debug.LogError($"[ASM-Lite] Switch failed for '{Path.GetFileName(projectRoot)}': {manifestError}");
                return;
            }

            if (isCurrentProject)
            {
                Client.Resolve();
                AssetDatabase.Refresh();
            }

            if (restoredFrom != null)
                Debug.Log($"[ASM-Lite] [{Path.GetFileName(projectRoot)}] Switched to version dependency: {packageName} => '{targetVersion}'. Restored embedded folder from '{restoredFrom}'.");
            else
                Debug.Log($"[ASM-Lite] [{Path.GetFileName(projectRoot)}] Switched to version dependency: {packageName} => '{targetVersion}'. Embedded present={Directory.Exists(embeddedPath)}.");
        }

        // ── VCC local package discovery ───────────────────────────────────────

        internal struct VccLocalPackage
        {
            public string PackageName;
            public string DisplayName;
            public string Version;
            public string LocalPath;
        }

        internal static IEnumerable<string> FindVccProjectRoots()
            => ReadVccSettingsArray("userProjects")
                .Where(Directory.Exists)
                .Select(Path.GetFullPath);

        internal static List<VccLocalPackage> DiscoverVccLocalPackages()
        {
            var result = new List<VccLocalPackage>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in ReadVccSettingsArray("userPackageFolders"))
            {
                if (!Directory.Exists(folder)) continue;
                TryAddVccPackage(folder, result, seen);
                foreach (var sub in Directory.GetDirectories(folder))
                    TryAddVccPackage(sub, result, seen);
            }

            foreach (var path in ReadVccSettingsArray("userPackages"))
                TryAddVccPackage(path, result, seen);

            return result;
        }

        private static void TryAddVccPackage(string path, List<VccLocalPackage> result, HashSet<string> seen)
        {
            string pkgJsonPath = Path.Combine(path, "package.json");
            if (!File.Exists(pkgJsonPath)) return;
            string fullPath = Path.GetFullPath(path);
            if (!seen.Add(fullPath)) return;

            string content;
            try { content = File.ReadAllText(pkgJsonPath); }
            catch { return; }

            string name = ExtractJsonString(content, "name");
            if (string.IsNullOrEmpty(name)) return;

            result.Add(new VccLocalPackage
            {
                PackageName = name,
                DisplayName = ExtractJsonString(content, "displayName") ?? name,
                Version     = ExtractJsonString(content, "version") ?? string.Empty,
                LocalPath   = fullPath,
            });
        }

        private static IEnumerable<string> ReadVccSettingsArray(string key)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string settingsPath = Path.Combine(localAppData, "VRChatCreatorCompanion", "settings.json");
            if (!File.Exists(settingsPath)) return Enumerable.Empty<string>();

            string json;
            try { json = File.ReadAllText(settingsPath); }
            catch { return Enumerable.Empty<string>(); }

            return ExtractJsonStringArray(json, key);
        }

        private static IEnumerable<string> ExtractJsonStringArray(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (idx < 0) return Enumerable.Empty<string>();

            int bracketStart = json.IndexOf('[', idx);
            if (bracketStart < 0) return Enumerable.Empty<string>();

            int bracketEnd = FindMatchingBracket(json, bracketStart);
            if (bracketEnd < 0) return Enumerable.Empty<string>();

            return ParseJsonStringArray(json.Substring(bracketStart + 1, bracketEnd - bracketStart - 1));
        }

        private static IEnumerable<string> ParseJsonStringArray(string content)
        {
            int pos = 0;
            while (pos < content.Length)
            {
                int qStart = content.IndexOf('"', pos);
                if (qStart < 0) break;
                int qEnd = qStart + 1;
                while (qEnd < content.Length)
                {
                    if (content[qEnd] == '\\') { qEnd += 2; continue; }
                    if (content[qEnd] == '"') break;
                    qEnd++;
                }
                if (qEnd >= content.Length) break;
                yield return content.Substring(qStart + 1, qEnd - qStart - 1)
                    .Replace("\\\\", "\\").Replace("\\/", "/");
                pos = qEnd + 1;
            }
        }

        private static string ExtractJsonString(string json, string key)
        {
            int idx = json.IndexOf($"\"{key}\"", StringComparison.Ordinal);
            if (idx < 0) return null;
            int colon = json.IndexOf(':', idx);
            if (colon < 0) return null;
            int vStart = colon + 1;
            while (vStart < json.Length && char.IsWhiteSpace(json[vStart])) vStart++;
            if (vStart >= json.Length || json[vStart] != '"') return null;
            int vEnd = vStart + 1;
            while (vEnd < json.Length)
            {
                if (json[vEnd] == '\\') { vEnd += 2; continue; }
                if (json[vEnd] == '"') break;
                vEnd++;
            }
            if (vEnd >= json.Length) return null;
            return json.Substring(vStart + 1, vEnd - vStart - 1);
        }

        internal static string GetProjectPackageStatus(string projectRoot, string packageName)
        {
            // Read manifest first — a file: entry means the user explicitly set local,
            // and that takes priority in the display even if the embedded folder still
            // exists (e.g. locked by Unity while the project is open).
            string manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath))
            {
                string json;
                try { json = File.ReadAllText(manifestPath); }
                catch { json = null; }

                if (json != null)
                {
                    int idx = json.IndexOf($"\"{packageName}\"", StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        int colon = json.IndexOf(':', idx);
                        if (colon >= 0)
                        {
                            int vStart = colon + 1;
                            while (vStart < json.Length && char.IsWhiteSpace(json[vStart])) vStart++;
                            if (vStart < json.Length && json[vStart] == '"')
                            {
                                int vEnd = json.IndexOf('"', vStart + 1);
                                if (vEnd >= 0)
                                {
                                    string val = json.Substring(vStart + 1, vEnd - vStart - 1);
                                    if (val.StartsWith("file:", StringComparison.Ordinal))
                                        return "file: (local)";
                                }
                            }
                        }
                    }
                }
            }

            // No file: manifest entry — check for an embedded/linked folder.
            string embeddedPath = Path.Combine(projectRoot, "Packages", packageName);
            if (Directory.Exists(embeddedPath) && File.Exists(Path.Combine(embeddedPath, "package.json")))
            {
                try
                {
                    bool isLink = (File.GetAttributes(embeddedPath) & FileAttributes.ReparsePoint) != 0;
                    return isLink ? "linked" : "embedded";
                }
                catch { }
            }

            if (!File.Exists(manifestPath)) return "not found";

            string manifestJson;
            try { manifestJson = File.ReadAllText(manifestPath); }
            catch { return "manifest error"; }

            int midx = manifestJson.IndexOf($"\"{packageName}\"", StringComparison.Ordinal);
            if (midx < 0) return "not installed";

            int mcolon = manifestJson.IndexOf(':', midx);
            if (mcolon < 0) return "unknown";

            int mvStart = mcolon + 1;
            while (mvStart < manifestJson.Length && char.IsWhiteSpace(manifestJson[mvStart])) mvStart++;
            if (mvStart >= manifestJson.Length || manifestJson[mvStart] != '"') return "unknown";

            int mvEnd = manifestJson.IndexOf('"', mvStart + 1);
            if (mvEnd < 0) return "unknown";

            string mval = manifestJson.Substring(mvStart + 1, mvEnd - mvStart - 1);
            return $"v{mval}";
        }

        // ── GUI ───────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            // Draw the banner outside the scroll view so it sits flush with the window top.
            DrawHeader();

            _scrollPos = EditorGUILayout.BeginScrollView(
                _scrollPos,
                alwaysShowHorizontal: false,
                alwaysShowVertical: false,
                horizontalScrollbar: GUIStyle.none,
                verticalScrollbar: GUI.skin.verticalScrollbar,
                background: GUIStyle.none);

            try
            {
                DrawAvatarPicker();

                if (_selectedAvatar != null)
                {
                    EditorGUILayout.Space(8);
                    DrawSettings();
                    SectionSeparator();
                    DrawIconSettingsSection();
                    SectionSeparator();
                    DrawCustomizeSection();
                    SectionSeparator();
                    DrawStatus();
                    EditorGUILayout.Space(16);
                    DrawActionButton();
                }

                EditorGUILayout.Space(8);
            }
            catch (ExitGUIException) { throw; }
            catch (System.Exception ex)
            {
                // Swallow mid-draw exceptions so EndScrollView always runs.
                // The exception is logged so it's not silently lost.
                // Log only on Layout events to avoid flooding during Repaint passes.
                if (Event.current.type == EventType.Layout)
                    Debug.LogException(ex);
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        // ── Sections ──────────────────────────────────────────────────────────

        /// <summary>
        /// Draws a subtle 1px horizontal rule between sections.
        /// Provides visual boundary (ux-common-region-boundaries) without heavy chrome.
        /// </summary>
        private static void SectionSeparator()
        {
            EditorGUILayout.Space(6);
            Rect r = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, s_separatorColor);
            EditorGUILayout.Space(6);
        }

        private void DrawHeader()
        {
            // Load banner texture once: null after domain reload until first draw.
            if (_bannerTexture == null)
                _bannerTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(BannerPath);

            if (_bannerTexture != null)
            {
                float aspect         = (float)_bannerTexture.width / _bannerTexture.height;
                float availableWidth = EditorGUIUtility.currentViewWidth;
                float bannerHeight   = Mathf.Round(availableWidth / aspect);

                // Draw at absolute (0,0) — bypasses any layout group margin.
                // StretchToFill with the correct aspect-derived height means no letterboxing.
                GUI.DrawTexture(new Rect(0f, 0f, availableWidth, bannerHeight),
                    _bannerTexture, ScaleMode.StretchToFill, alphaBlend: true);

                // Consume the height in the layout system so content below doesn't overlap.
                GUILayout.Space(bannerHeight + 4f);
            }
            else
            {
                // Fallback when banner hasn't been imported yet.
                EditorGUILayout.Space(6);
                EditorGUILayout.LabelField(".Staples. ASM-Lite", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Avatar Settings Manager: Lite Edition", EditorStyles.miniLabel);
            }
        }

        private void DrawAvatarPicker()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Avatar", EditorStyles.boldLabel);

            var newAvatar = (VRCAvatarDescriptor)EditorGUILayout.ObjectField(
                label:             "Avatar Root",
                obj:               _selectedAvatar,
                objType:           typeof(VRCAvatarDescriptor),
                allowSceneObjects: true);

            if (newAvatar != _selectedAvatar)
            {
                _selectedAvatar = newAvatar;
                _cachedComponent = null;
                _lastRefreshFrame = -1;
                _cachedCustomParamCount = -1;
                _discoveredParamCount = -1;

                if (_selectedAvatar != null)
                    SyncPendingSlotCountFromAvatar();

                Repaint();
            }

            if (_selectedAvatar == null)
            {
                EditorGUILayout.HelpBox(
                    "Select the VRC Avatar Descriptor in your scene hierarchy to get started.",
                    MessageType.Info);
            }
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            var component = GetOrRefreshComponent();

            // Use the Unity-aware null check (operator bool): a C# != null check
            // passes for destroyed UnityEngine.Objects, which would throw on field access.
            if (component)
            {
                // Prefab is present: slot count still editable, but a rebuild
                // is needed to apply changes to the generated assets.
                int newSlot = EditorGUILayout.IntSlider(
                    s_slotCountLabelActive,
                    component.slotCount, 1, 8);

                if (newSlot != component.slotCount)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Slot Count");
                    component.slotCount = newSlot;
                    EditorUtility.SetDirty(component);
                }

                EditorGUILayout.HelpBox(
                    "Click \"Rebuild ASM-Lite\" to apply slot count changes.",
                    MessageType.None);
            }
            else
            {
                // No prefab yet: user can configure before adding.
                _pendingSlotCount = EditorGUILayout.IntSlider(
                    s_slotCountLabelPending,
                    _pendingSlotCount, 1, 8);
            }


        }

        private void DrawIconSettingsSection()
        {
            EditorGUILayout.LabelField("Icon Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical("box");
            DrawIconMode();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            EditorGUILayout.BeginVertical("box");
            DrawActionIcons();
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(8);
            DrawWheelPreview();
        }

        private static string[] ParseExcludedParameterNames(string rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return Array.Empty<string>();

            return SanitizeExcludedParameterNames(
                rawValue
                    .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => v.Trim())
                    .ToArray());
        }

        private void SetComponentBool(ASMLiteComponent component, string undoLabel, ref bool target, bool value)
        {
            if (target == value)
                return;

            Undo.RecordObject(component, undoLabel);
            target = value;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentTexture(ASMLiteComponent component, string undoLabel, ref Texture2D target, Texture2D value)
        {
            if (target == value)
                return;

            Undo.RecordObject(component, undoLabel);
            target = value;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentString(ASMLiteComponent component, string undoLabel, ref string target, string value)
        {
            string normalized = NormalizeOptionalString(value);
            if (string.Equals(target, normalized, StringComparison.Ordinal))
                return;

            Undo.RecordObject(component, undoLabel);
            target = normalized;
            EditorUtility.SetDirty(component);
        }

        private void SetComponentExcludedNames(ASMLiteComponent component, string undoLabel, string[] value)
        {
            string[] sanitized = SanitizeExcludedParameterNames(value);

            if (component.excludedParameterNames != null && component.excludedParameterNames.SequenceEqual(sanitized, StringComparer.Ordinal))
                return;

            Undo.RecordObject(component, undoLabel);
            component.excludedParameterNames = sanitized;
            EditorUtility.SetDirty(component);
        }

        private void DrawCustomizeSection()
        {
            EditorGUILayout.LabelField("Customize", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "All customization options are opt-in. Leave toggles off to keep current ASM-Lite generated output behavior.",
                MessageType.None);

            var component = GetOrRefreshComponent();

            EditorGUILayout.BeginVertical("box");

            bool useCustomRootIcon = component ? component.useCustomRootIcon : _pendingUseCustomRootIcon;
            bool newUseCustomRootIcon = EditorGUILayout.ToggleLeft("Use custom root icon", useCustomRootIcon);
            if (component)
                SetComponentBool(component, "Toggle ASM-Lite Custom Root Icon", ref component.useCustomRootIcon, newUseCustomRootIcon);
            else
                _pendingUseCustomRootIcon = newUseCustomRootIcon;

            if (newUseCustomRootIcon)
            {
                Texture2D currentRootIcon = component ? component.customRootIcon : _pendingCustomRootIcon;
                Texture2D newRootIcon = (Texture2D)EditorGUILayout.ObjectField("Root Icon", currentRootIcon, typeof(Texture2D), false);
                if (component)
                    SetComponentTexture(component, "Change ASM-Lite Root Icon", ref component.customRootIcon, newRootIcon);
                else
                    _pendingCustomRootIcon = newRootIcon;
            }

            EditorGUILayout.Space(6);

            bool useCustomRootName = component ? component.useCustomRootName : _pendingUseCustomRootName;
            bool newUseCustomRootName = EditorGUILayout.ToggleLeft("Use custom root name", useCustomRootName);
            if (component)
                SetComponentBool(component, "Toggle ASM-Lite Custom Root Name", ref component.useCustomRootName, newUseCustomRootName);
            else
                _pendingUseCustomRootName = newUseCustomRootName;

            if (newUseCustomRootName)
            {
                string currentRootName = component ? NormalizeOptionalString(component.customRootName) : NormalizeOptionalString(_pendingCustomRootName);
                string newRootName = EditorGUILayout.TextField("Root Name", currentRootName);
                if (component)
                    SetComponentString(component, "Change ASM-Lite Root Name", ref component.customRootName, newRootName);
                else
                    _pendingCustomRootName = NormalizeOptionalString(newRootName);
            }

            EditorGUILayout.Space(6);

            bool useCustomInstallPath = component ? component.useCustomInstallPath : _pendingUseCustomInstallPath;
            bool newUseCustomInstallPath = EditorGUILayout.ToggleLeft("Use custom install path", useCustomInstallPath);
            if (component)
            {
                bool installToggleChanged = component.useCustomInstallPath != newUseCustomInstallPath;
                SetComponentBool(component, "Toggle ASM-Lite Custom Install Path", ref component.useCustomInstallPath, newUseCustomInstallPath);
                if (installToggleChanged)
                    TryRefreshInstallPathPrefix(component, "Customize Toggle");
            }
            else
            {
                _pendingUseCustomInstallPath = newUseCustomInstallPath;
            }

            if (newUseCustomInstallPath)
            {
                string currentInstallPath = component ? NormalizeOptionalString(component.customInstallPath) : NormalizeOptionalString(_pendingCustomInstallPath);

                // Only apply text-field edits when the user actually changed the value.
                // Tree clicks write directly via ApplyInstallPathSelection and must not
                // be overwritten by a stale newInstallPath captured before the tree draws.
                EditorGUI.BeginChangeCheck();
                string newInstallPath = EditorGUILayout.TextField("Install Path", currentInstallPath);
                if (EditorGUI.EndChangeCheck())
                {
                    if (component)
                    {
                        string normalizedNewInstallPath = NormalizeOptionalString(newInstallPath);
                        bool installPathChanged = !string.Equals(NormalizeOptionalString(component.customInstallPath), normalizedNewInstallPath, StringComparison.Ordinal);
                        SetComponentString(component, "Change ASM-Lite Install Path", ref component.customInstallPath, newInstallPath);
                        if (installPathChanged)
                            TryRefreshInstallPathPrefix(component, "Customize Text");
                    }
                    else
                    {
                        _pendingCustomInstallPath = NormalizeOptionalString(newInstallPath);
                    }
                }

                DrawInstallPathTree(component);
            }

            EditorGUILayout.Space(6);

            bool useParameterExclusions = component ? component.useParameterExclusions : _pendingUseParameterExclusions;
            bool newUseParameterExclusions = EditorGUILayout.ToggleLeft("Customize parameter backup", useParameterExclusions);
            if (component)
                SetComponentBool(component, "Toggle ASM-Lite Parameter Backup Customization", ref component.useParameterExclusions, newUseParameterExclusions);
            else
                _pendingUseParameterExclusions = newUseParameterExclusions;

            if (newUseParameterExclusions)
                DrawParameterChecklist(component);

            EditorGUILayout.EndVertical();
        }

        private void ApplyInstallPathSelection(ASMLiteComponent component, string selectedPath)
        {
            string normalized = NormalizeOptionalString(selectedPath);
            if (component)
            {
                bool enableToggle = !string.IsNullOrEmpty(normalized) && !component.useCustomInstallPath;

                if (enableToggle)
                    SetComponentBool(component, "Toggle ASM-Lite Custom Install Path", ref component.useCustomInstallPath, true);

                SetComponentString(component, "Change ASM-Lite Install Path", ref component.customInstallPath, normalized);
                // Always attempt live prefix refresh for explicit tree selection.
                // This repairs stale VF prefix state even when the selected path text
                // matches the component's current customInstallPath value.
                TryRefreshInstallPathPrefix(component, "Customize Tree");
            }
            else
            {
                _pendingCustomInstallPath = normalized;
                if (!string.IsNullOrEmpty(normalized))
                    _pendingUseCustomInstallPath = true;
            }
        }

        private static void TryRefreshInstallPathPrefix(ASMLiteComponent component, string contextLabel)
        {
            if (component == null)
                return;

            if (!TryRefreshLiveInstallPathPrefix(component, contextLabel))
            {
                Debug.LogWarning($"[ASM-Lite] {contextLabel}: Install-path update did not refresh live FullController menu prefix immediately. Rebuild/upload will retry.");
            }
        }

        // ── Install Path Tree UI ──────────────────────────────────────────────

        private void DrawInstallPathTree(ASMLiteComponent component)
        {
            // Invalidate cached tree when avatar changes.
            if (_lastInstallPathTreeAvatar != _selectedAvatar)
            {
                _cachedInstallPathTree = null;
                _lastInstallPathTreeAvatar = _selectedAvatar;
                _expandedInstallPaths.Clear();
            }

            if (_cachedInstallPathTree == null)
                _cachedInstallPathTree = BuildInstallPathTree(_selectedAvatar);

            string currentPath = component
                ? NormalizeOptionalString(component.customInstallPath)
                : NormalizeOptionalString(_pendingCustomInstallPath);

            EditorGUILayout.Space(2f);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Install Location", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("↻", GUILayout.Width(22f), GUILayout.Height(14f)))
            {
                _cachedInstallPathTree = BuildInstallPathTree(_selectedAvatar);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();

            if (_cachedInstallPathTree == null || _cachedInstallPathTree.Children.Count == 0)
            {
                EditorGUILayout.HelpBox("No menu paths found on selected avatar.", MessageType.None);
                return;
            }

            // Root row — selects empty install path (menu root).
            bool rootSelected = string.IsNullOrEmpty(currentPath);
            var rootRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            if (rootSelected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rootRect, new Color(0.24f, 0.49f, 0.91f, 0.30f));
            if (GUI.Button(rootRect, rootSelected ? "(root)" : "(root)", rootSelected ? EditorStyles.boldLabel : EditorStyles.label))
            {
                ApplyInstallPathSelection(component, string.Empty);
                Repaint();
            }

            _installPathTreeScrollPos = EditorGUILayout.BeginScrollView(
                _installPathTreeScrollPos, GUILayout.Height(_installPathTreeHeight));

            foreach (var child in _cachedInstallPathTree.Children)
                DrawInstallPathTreeNode(child, component, currentPath, 0);

            EditorGUILayout.EndScrollView();

            // ── Resize handle ─────────────────────────────────────────────────
            // A narrow strip at the bottom-right that the user can drag up/down.
            var handleRect = EditorGUILayout.GetControlRect(false, 8f);
            handleRect = new Rect(handleRect.xMax - 24f, handleRect.y, 24f, 8f);

            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.Repaint)
            {
                // Draw three small dots as a visual grip indicator.
                var dotColor = new Color(0.55f, 0.55f, 0.55f, 0.80f);
                float cx = handleRect.x + handleRect.width / 2f;
                float cy = handleRect.y + handleRect.height / 2f;
                for (int d = -1; d <= 1; d++)
                    EditorGUI.DrawRect(new Rect(cx + d * 5f - 1f, cy - 1f, 2f, 2f), dotColor);
            }

            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
            {
                _isDraggingTreeResize = true;
                Event.current.Use();
            }

            if (_isDraggingTreeResize)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _installPathTreeHeight = Mathf.Clamp(
                        _installPathTreeHeight + Event.current.delta.y, 80f, 600f);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    _isDraggingTreeResize = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawInstallPathTreeNode(
            MenuTreeNode node,
            ASMLiteComponent component,
            string currentPath,
            int depth)
        {
            bool isSelected = string.Equals(currentPath, node.FullPath, StringComparison.Ordinal);
            bool hasChildren = node.Children.Count > 0;
            bool isExpanded = _expandedInstallPaths.Contains(node.FullPath);

            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            float indentPx = depth * 14f + 2f;
            var activeRect = new Rect(rowRect.x + indentPx, rowRect.y, rowRect.width - indentPx, rowRect.height);

            // Selection highlight.
            if (isSelected && Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(activeRect, new Color(0.24f, 0.49f, 0.91f, 0.30f));

            // Foldout arrow — toggles expand/collapse without selecting.
            if (hasChildren)
            {
                var arrowRect = new Rect(activeRect.x, activeRect.y, 14f, activeRect.height);
                bool toggled = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);
                if (toggled != isExpanded)
                {
                    if (toggled) _expandedInstallPaths.Add(node.FullPath);
                    else _expandedInstallPaths.Remove(node.FullPath);
                    isExpanded = toggled;
                    Repaint();
                }
            }

            // Label — clicking selects this path as the install location.
            var labelRect = new Rect(
                activeRect.x + 14f, activeRect.y,
                activeRect.width - 14f, activeRect.height);

            if (GUI.Button(labelRect, node.Name, isSelected ? EditorStyles.boldLabel : EditorStyles.label))
            {
                ApplyInstallPathSelection(component, node.FullPath);
                Repaint();
            }

            if (hasChildren && isExpanded)
            {
                foreach (var child in node.Children)
                    DrawInstallPathTreeNode(child, component, currentPath, depth + 1);
            }
        }

        private static MenuTreeNode BuildInstallPathTree(VRCAvatarDescriptor avatar)
        {
            var root = new MenuTreeNode { Name = string.Empty, FullPath = string.Empty };
            if (avatar == null)
                return root;

            var allPaths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in GetAvatarSubmenuPaths(avatar)) allPaths.Add(p);
            foreach (var p in GetVrcFuryMenuPrefixes(avatar)) allPaths.Add(p);

            // Apply VRCFury MoveMenuItem remaps so install-path choices reflect
            // the effective post-move menu layout (destination paths) instead of
            // exposing stale pre-move source locations.
            var moveRemaps = GetVrcFuryMoveMenuPathRemaps(avatar);
            ApplyInstallPathMoveRemaps(allPaths, moveRemaps);

            var nodeMap = new Dictionary<string, MenuTreeNode>(StringComparer.Ordinal);
            nodeMap[string.Empty] = root;

            foreach (var path in allPaths.OrderBy(p => p, StringComparer.Ordinal))
                EnsureTreeNodeExists(nodeMap, path);

            SortTreeChildren(root);
            return root;
        }

        private static MenuTreeNode EnsureTreeNodeExists(
            Dictionary<string, MenuTreeNode> nodeMap, string path)
        {
            if (nodeMap.TryGetValue(path, out var existing))
                return existing;

            int slash = path.LastIndexOf('/');
            string parentPath = slash < 0 ? string.Empty : path.Substring(0, slash);
            string name = slash < 0 ? path : path.Substring(slash + 1);

            var parent = EnsureTreeNodeExists(nodeMap, parentPath);
            var node = new MenuTreeNode { Name = name, FullPath = path };
            parent.Children.Add(node);
            nodeMap[path] = node;
            return node;
        }

        private static void SortTreeChildren(MenuTreeNode node)
        {
            node.Children.Sort((a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            foreach (var child in node.Children)
                SortTreeChildren(child);
        }

        private static Dictionary<string, string> GetVrcFuryMoveMenuPathRemaps(VRCAvatarDescriptor avatar)
        {
            var remaps = new Dictionary<string, string>(StringComparer.Ordinal);
            if (avatar == null)
                return remaps;

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                var iterator = so.GetIterator();
                if (!iterator.NextVisible(true))
                    continue;

                var seenPaths = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                        continue;

                    string managedRefType = iterator.managedReferenceFullTypename;
                    if (string.IsNullOrWhiteSpace(managedRefType)
                        || managedRefType.IndexOf("MoveMenuItem", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    string managedPath = iterator.propertyPath;
                    if (!seenPaths.Add(managedPath))
                        continue;

                    var fromProp = so.FindProperty(managedPath + ".fromPath");
                    var toProp = so.FindProperty(managedPath + ".toPath");
                    if (fromProp == null || toProp == null)
                        continue;
                    if (fromProp.propertyType != SerializedPropertyType.String
                        || toProp.propertyType != SerializedPropertyType.String)
                        continue;

                    string fromPath = NormalizeSlashPath(fromProp.stringValue);
                    string toPath = NormalizeSlashPath(toProp.stringValue);
                    if (string.IsNullOrWhiteSpace(toPath))
                        continue;

                    if (!remaps.ContainsKey(fromPath ?? string.Empty))
                        remaps[fromPath ?? string.Empty] = toPath;
                } while (iterator.NextVisible(true));
            }

            return remaps;
        }

        private static void ApplyInstallPathMoveRemaps(HashSet<string> allPaths, Dictionary<string, string> remaps)
        {
            if (allPaths == null || remaps == null || remaps.Count == 0)
                return;

            foreach (var kv in remaps)
            {
                string fromPath = kv.Key ?? string.Empty;
                string toPath = kv.Value ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(fromPath))
                {
                    allPaths.RemoveWhere(path =>
                        string.Equals(path, fromPath, StringComparison.Ordinal)
                        || path.StartsWith(fromPath + "/", StringComparison.Ordinal));
                }

                AddPathAndParents(allPaths, toPath);
            }
        }

        private static void AddPathAndParents(HashSet<string> allPaths, string fullPath)
        {
            if (allPaths == null || string.IsNullOrWhiteSpace(fullPath))
                return;

            string normalized = NormalizeSlashPath(fullPath);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            string[] segments = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return;

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < segments.Length; i++)
            {
                if (sb.Length > 0)
                    sb.Append('/');
                sb.Append(segments[i]);
                allPaths.Add(sb.ToString());
            }
        }

        // ── Parameter Backup Tree UI ──────────────────────────────────────────

        private static Dictionary<string, string> BuildHiddenAssignedByVisibleOriginalMap(IReadOnlyCollection<string> visibleParamNames)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (visibleParamNames == null || visibleParamNames.Count == 0)
                return map;

            var visibleSet = new HashSet<string>(visibleParamNames, StringComparer.Ordinal);
            var mappings = ASMLiteToggleNameBroker.GetLatestGlobalParamMappings();
            if (mappings == null || mappings.Length == 0)
                return map;

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (string.IsNullOrWhiteSpace(mapping.OriginalGlobalParam)
                    || string.IsNullOrWhiteSpace(mapping.AssignedGlobalParam))
                    continue;

                if (!visibleSet.Contains(mapping.OriginalGlobalParam))
                    continue;
                if (visibleSet.Contains(mapping.AssignedGlobalParam))
                    continue;

                if (!map.ContainsKey(mapping.OriginalGlobalParam))
                    map.Add(mapping.OriginalGlobalParam, mapping.AssignedGlobalParam);
            }

            return map;
        }

        private static HashSet<string> NormalizeExcludedSetForVisibleRows(
            IEnumerable<string> excludedRaw,
            Dictionary<string, string> hiddenAssignedByVisibleOriginal)
        {
            var normalized = new HashSet<string>(StringComparer.Ordinal);
            if (excludedRaw != null)
            {
                foreach (var name in excludedRaw)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        normalized.Add(name);
                }
            }

            if (hiddenAssignedByVisibleOriginal != null)
            {
                foreach (var kv in hiddenAssignedByVisibleOriginal)
                {
                    string original = kv.Key;
                    string assigned = kv.Value;
                    if (normalized.Contains(assigned))
                        normalized.Add(original);
                }
            }

            return normalized;
        }

        private static string[] ExpandExcludedForStorage(
            HashSet<string> visibleExcluded,
            Dictionary<string, string> hiddenAssignedByVisibleOriginal)
        {
            var expanded = new HashSet<string>(visibleExcluded ?? new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);

            if (hiddenAssignedByVisibleOriginal != null)
            {
                foreach (var kv in hiddenAssignedByVisibleOriginal)
                {
                    if (expanded.Contains(kv.Key))
                        expanded.Add(kv.Value);
                }
            }

            return expanded.OrderBy(p => p, StringComparer.Ordinal).ToArray();
        }

        private void DrawParameterChecklist(ASMLiteComponent component)
        {
            if (_lastParamListAvatar != _selectedAvatar)
            {
                _cachedParamList = null;
                _cachedParamTree = null;
                _expandedParamMenuPaths.Clear();
                _lastParamListAvatar = _selectedAvatar;
            }
            if (_cachedParamList == null)
                _cachedParamList = GetBackableParameterNames(_selectedAvatar);
            if (_cachedParamTree == null)
                _cachedParamTree = BuildParamTree(_selectedAvatar);

            EditorGUILayout.Space(2f);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Parameter Backup", EditorStyles.miniBoldLabel);
            if (GUILayout.Button("↻", GUILayout.Width(22f), GUILayout.Height(14f)))
            {
                _cachedParamList = GetBackableParameterNames(_selectedAvatar);
                _cachedParamTree = BuildParamTree(_selectedAvatar);
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("Uncheck parameters to exclude them from backup.", EditorStyles.wordWrappedMiniLabel);

            if (_cachedParamList == null || _cachedParamList.Length == 0)
            {
                EditorGUILayout.HelpBox("No parameters found on selected avatar.", MessageType.None);
                return;
            }

            // Build hidden mapping: visible original param -> hidden assigned ASM_VF_* alias.
            var hiddenAssignedByVisibleOriginal = BuildHiddenAssignedByVisibleOriginalMap(_cachedParamList);

            // Build mutable exclusion set from current component/pending state.
            string[] currentExcluded = component
                ? SanitizeExcludedParameterNames(component.excludedParameterNames)
                : SanitizeExcludedParameterNames(_pendingExcludedParameterNames);
            var excludedSet = NormalizeExcludedSetForVisibleRows(currentExcluded, hiddenAssignedByVisibleOriginal);
            var originalExcluded = new HashSet<string>(excludedSet, StringComparer.Ordinal);

            _paramChecklistScrollPos = EditorGUILayout.BeginScrollView(
                _paramChecklistScrollPos, GUILayout.Height(_paramChecklistHeight));

            foreach (var child in _cachedParamTree.Children)
                DrawParamTreeNode(child, excludedSet, 0);

            EditorGUILayout.EndScrollView();

            // Write back if anything changed.
            if (!excludedSet.SetEquals(originalExcluded))
            {
                string[] newExcluded = ExpandExcludedForStorage(excludedSet, hiddenAssignedByVisibleOriginal);
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Parameter Backup");
                    component.excludedParameterNames = newExcluded;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingExcludedParameterNames = newExcluded;
                }
                Repaint();
            }

            // ── Resize handle ─────────────────────────────────────────────────
            var handleRect = EditorGUILayout.GetControlRect(false, 8f);
            handleRect = new Rect(handleRect.xMax - 24f, handleRect.y, 24f, 8f);
            EditorGUIUtility.AddCursorRect(handleRect, MouseCursor.ResizeVertical);

            if (Event.current.type == EventType.Repaint)
            {
                var dotColor = new Color(0.55f, 0.55f, 0.55f, 0.80f);
                float cx = handleRect.x + handleRect.width / 2f;
                float cy = handleRect.y + handleRect.height / 2f;
                for (int d = -1; d <= 1; d++)
                    EditorGUI.DrawRect(new Rect(cx + d * 5f - 1f, cy - 1f, 2f, 2f), dotColor);
            }

            if (Event.current.type == EventType.MouseDown && handleRect.Contains(Event.current.mousePosition))
            {
                _isDraggingParamResize = true;
                Event.current.Use();
            }

            if (_isDraggingParamResize)
            {
                if (Event.current.type == EventType.MouseDrag)
                {
                    _paramChecklistHeight = Mathf.Clamp(
                        _paramChecklistHeight + Event.current.delta.y, 60f, 400f);
                    Event.current.Use();
                    Repaint();
                }
                else if (Event.current.type == EventType.MouseUp)
                {
                    _isDraggingParamResize = false;
                    Event.current.Use();
                }
            }
        }

        private void DrawParamTreeNode(ParamTreeNode node, HashSet<string> excludedSet, int depth)
        {
            float indentPx = depth * 14f + 2f;

            if (node.IsParam)
            {
                // Checkbox row for a parameter leaf.
                // Add extra offset so child-item checkboxes sit to the right of
                // category checkboxes/foldouts and read as subordinate rows.
                const float childItemOffset = 12f;
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var labelRect = new Rect(rowRect.x + indentPx + childItemOffset, rowRect.y, rowRect.width - indentPx - childItemOffset, rowRect.height);
                bool isIncluded = !excludedSet.Contains(node.ParamName);
                bool newIncluded = EditorGUI.ToggleLeft(labelRect, node.Name, isIncluded);
                if (newIncluded != isIncluded)
                {
                    if (newIncluded) excludedSet.Remove(node.ParamName);
                    else             excludedSet.Add(node.ParamName);
                }
            }
            else
            {
                // Folder row — foldout arrow + category checkbox + label.
                bool isExpanded = _expandedParamMenuPaths.Contains(node.MenuPath);
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var activeRect = new Rect(rowRect.x + indentPx, rowRect.y, rowRect.width - indentPx, rowRect.height);

                var arrowRect = new Rect(activeRect.x, activeRect.y, 14f, activeRect.height);
                bool toggled = EditorGUI.Foldout(arrowRect, isExpanded, GUIContent.none, true);
                if (toggled != isExpanded)
                {
                    if (toggled) _expandedParamMenuPaths.Add(node.MenuPath);
                    else         _expandedParamMenuPaths.Remove(node.MenuPath);
                    Repaint();
                }

                int totalLeafCount = CountParamLeafNodes(node);
                int includedLeafCount = CountIncludedParamLeafNodes(node, excludedSet);
                bool allIncluded = totalLeafCount > 0 && includedLeafCount == totalLeafCount;
                bool mixed = includedLeafCount > 0 && includedLeafCount < totalLeafCount;

                var toggleRect = new Rect(activeRect.x + 14f, activeRect.y, 16f, activeRect.height);
                EditorGUI.showMixedValue = mixed;
                bool newAllIncluded = EditorGUI.Toggle(toggleRect, allIncluded);
                EditorGUI.showMixedValue = false;

                if (newAllIncluded != allIncluded || (mixed && Event.current.type == EventType.MouseUp && toggleRect.Contains(Event.current.mousePosition)))
                {
                    SetFolderIncludedState(node, excludedSet, newAllIncluded);
                }

                var labelRect = new Rect(activeRect.x + 32f, activeRect.y, activeRect.width - 32f, activeRect.height);
                EditorGUI.LabelField(labelRect, node.Name, EditorStyles.boldLabel);

                if (toggled)
                    foreach (var child in node.Children)
                        DrawParamTreeNode(child, excludedSet, depth + 1);
            }
        }

        private static int CountParamLeafNodes(ParamTreeNode node)
        {
            if (node == null)
                return 0;
            if (node.IsParam)
                return 1;

            int count = 0;
            for (int i = 0; i < node.Children.Count; i++)
                count += CountParamLeafNodes(node.Children[i]);
            return count;
        }

        private static int CountIncludedParamLeafNodes(ParamTreeNode node, HashSet<string> excludedSet)
        {
            if (node == null)
                return 0;

            if (node.IsParam)
                return excludedSet.Contains(node.ParamName) ? 0 : 1;

            int count = 0;
            for (int i = 0; i < node.Children.Count; i++)
                count += CountIncludedParamLeafNodes(node.Children[i], excludedSet);
            return count;
        }

        private static void SetFolderIncludedState(ParamTreeNode node, HashSet<string> excludedSet, bool include)
        {
            if (node == null)
                return;

            if (node.IsParam)
            {
                if (include) excludedSet.Remove(node.ParamName);
                else         excludedSet.Add(node.ParamName);
                return;
            }

            for (int i = 0; i < node.Children.Count; i++)
                SetFolderIncludedState(node.Children[i], excludedSet, include);
        }

        private static ParamTreeNode BuildParamTree(VRCAvatarDescriptor avatar)
        {
            var root = new ParamTreeNode { Name = string.Empty, MenuPath = string.Empty };
            if (avatar == null) return root;

            string[] backableParams = GetBackableParameterNames(avatar);
            if (backableParams.Length == 0) return root;

            // Build VRCFury Toggle metadata so ASM_VF_* global parameters can be
            // grouped under their real menu paths with friendly labels instead of
            // falling into the "(No menu)" bucket with raw global names.
            var vrcFuryMeta = BuildVrcFuryGlobalParamMetadata(avatar);

            // Reuse the same VRCFury menu-prefix discovery strategy as the custom
            // install-path tree, then map each normalized prefix token so ASM_VF_*
            // deterministic names can be mapped back to real menu folders.
            var sanitizedPrefixToMenuPath = new Dictionary<string, string>(StringComparer.Ordinal);
            var vrcFuryMenuPrefixes = GetVrcFuryMenuPrefixes(avatar);
            for (int i = 0; i < vrcFuryMenuPrefixes.Length; i++)
            {
                string menuPrefix = vrcFuryMenuPrefixes[i];
                if (string.IsNullOrWhiteSpace(menuPrefix))
                    continue;

                string token = ASMLiteToggleNameBroker.SanitizePathToken(menuPrefix);
                if (string.IsNullOrWhiteSpace(token))
                    continue;

                if (!sanitizedPrefixToMenuPath.ContainsKey(token))
                    sanitizedPrefixToMenuPath[token] = menuPrefix;
            }

            // Map each param name → the menu folder path where it appears.
            var paramToMenuPath = new Dictionary<string, string>(StringComparer.Ordinal);
            if (avatar.expressionsMenu != null)
                ScanMenuForParamLocations(avatar.expressionsMenu, string.Empty, paramToMenuPath,
                    new HashSet<VRCExpressionsMenu>());

            // Augment with VRCFury-assigned global parameters (ASM_VF_*) when a menu
            // path hint is available. This keeps Toggle-generated parameters out of
            // the "(No menu)" catch-all when they are actually driven from a menu.
            foreach (var kvp in vrcFuryMeta)
            {
                string paramName = kvp.Key;
                var meta = kvp.Value;
                if (string.IsNullOrEmpty(meta.MenuPath))
                    continue;
                if (!paramToMenuPath.ContainsKey(paramName))
                    paramToMenuPath[paramName] = meta.MenuPath;
            }

            // Build folder nodes mirroring the menu hierarchy.
            var menuNodes = new Dictionary<string, ParamTreeNode>(StringComparer.Ordinal);
            menuNodes[string.Empty] = root;

            var unassigned = new List<string>();
            var assigned = new List<(string MenuPath, string DisplayName, string ParamName)>();
            foreach (var paramName in backableParams)
            {
                string menuPath = null;
                if (paramToMenuPath.TryGetValue(paramName, out string mappedPath) && mappedPath != null)
                    menuPath = mappedPath;

                string displayName = paramName;
                if (vrcFuryMeta.TryGetValue(paramName, out var meta) && !string.IsNullOrEmpty(meta.DisplayName))
                    displayName = meta.DisplayName;

                // For deterministic ASM_VF_* names, always infer display + parent
                // folder from the encoded menu token so rows appear as user-facing
                // toggle labels at the correct menu level.
                if (paramName.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, StringComparison.Ordinal)
                    && TryInferMenuPathAndDisplayNameFromAsmVfGlobalName(
                        paramName,
                        sanitizedPrefixToMenuPath,
                        out string inferredAsmMenuPath,
                        out string inferredAsmDisplayName))
                {
                    menuPath = inferredAsmMenuPath;
                    displayName = inferredAsmDisplayName;
                }

                // Fallback: when a parameter name itself encodes a menu-like path
                // (common in some VRCFury Toggle outputs), infer folder + label
                // directly from that path so it does not land in "(No menu)".
                if (string.IsNullOrEmpty(menuPath)
                    && TryInferMenuPathAndDisplayNameFromParamName(paramName, out string inferredMenuPath, out string inferredDisplayName))
                {
                    menuPath = inferredMenuPath;
                    if (string.IsNullOrEmpty(displayName) || string.Equals(displayName, paramName, StringComparison.Ordinal))
                        displayName = inferredDisplayName;
                }

                if (string.IsNullOrEmpty(menuPath))
                {
                    unassigned.Add(paramName);
                    continue;
                }

                assigned.Add((menuPath, string.IsNullOrWhiteSpace(displayName) ? paramName : displayName, paramName));
            }

            // Suffix duplicate display names within the same menu folder so they are
            // distinguishable (Rezz1, Rezz2, ...).
            var totalByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assigned.Count; i++)
            {
                var entry = assigned[i];
                string key = entry.MenuPath + "\u001F" + entry.DisplayName;
                totalByKey[key] = totalByKey.TryGetValue(key, out int count) ? count + 1 : 1;
            }

            var nextIndexByKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < assigned.Count; i++)
            {
                var entry = assigned[i];
                string key = entry.MenuPath + "\u001F" + entry.DisplayName;

                string finalDisplayName = entry.DisplayName;
                if (totalByKey.TryGetValue(key, out int total) && total > 1)
                {
                    int idx = nextIndexByKey.TryGetValue(key, out int next) ? next : 1;
                    nextIndexByKey[key] = idx + 1;
                    finalDisplayName = entry.DisplayName + idx;
                }

                var folderNode = EnsureParamMenuNode(menuNodes, entry.MenuPath);
                folderNode.Children.Add(new ParamTreeNode { Name = finalDisplayName, ParamName = entry.ParamName });
            }

            // Unassigned params shown in a catch-all group.
            if (unassigned.Count > 0)
            {
                const string unassignedPath = "\x01unassigned";
                var group = new ParamTreeNode { Name = "(No menu)", MenuPath = unassignedPath };
                foreach (var p in unassigned)
                {
                    string displayName = p;
                    if (vrcFuryMeta.TryGetValue(p, out var meta) && !string.IsNullOrEmpty(meta.DisplayName))
                        displayName = meta.DisplayName;

                    group.Children.Add(new ParamTreeNode { Name = displayName, ParamName = p });
                }
                root.Children.Add(group);
            }

            SortParamTreeChildren(root);
            return root;
        }

        /// <summary>
        /// Builds a map from VRCFury toggle-like managed-reference payloads to
        /// parameter metadata (menu path + friendly display name).
        ///
        /// This intentionally does not hard-require a specific managed-reference type
        /// name so it remains compatible across VRCFury schema variants.
        /// </summary>
        private static Dictionary<string, (string MenuPath, string DisplayName)> BuildVrcFuryGlobalParamMetadata(VRCAvatarDescriptor avatar)
        {
            var result = new Dictionary<string, (string MenuPath, string DisplayName)>(StringComparer.Ordinal);
            if (avatar == null)
                return result;

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var so = new SerializedObject(behaviour);
                var iterator = so.GetIterator();
                if (!iterator.NextVisible(true))
                    continue;

                var seenPaths = new HashSet<string>(StringComparer.Ordinal);
                do
                {
                    if (iterator.propertyType != SerializedPropertyType.ManagedReference)
                        continue;

                    string togglePropertyPath = iterator.propertyPath;
                    if (!seenPaths.Add(togglePropertyPath))
                        continue;

                    var useGlobalProp = so.FindProperty(togglePropertyPath + ".useGlobalParam");
                    var globalParamProp = so.FindProperty(togglePropertyPath + ".globalParam");
                    var menuPathProp = so.FindProperty(togglePropertyPath + ".menuPath");
                    var nameProp = so.FindProperty(togglePropertyPath + ".name");
                    var labelProp = so.FindProperty(togglePropertyPath + ".label");
                    var paramNameProp = so.FindProperty(togglePropertyPath + ".paramName");

                    bool hasAnyToggleFields = useGlobalProp != null
                        || globalParamProp != null
                        || menuPathProp != null
                        || nameProp != null
                        || labelProp != null
                        || paramNameProp != null;

                    if (!hasAnyToggleFields)
                        continue;

                    bool useGlobal = useGlobalProp != null
                        && useGlobalProp.propertyType == SerializedPropertyType.Boolean
                        && useGlobalProp.boolValue;

                    string globalName = globalParamProp != null && globalParamProp.propertyType == SerializedPropertyType.String
                        ? (globalParamProp.stringValue ?? string.Empty).Trim()
                        : string.Empty;

                    string rawMenuPath = menuPathProp != null && menuPathProp.propertyType == SerializedPropertyType.String
                        ? menuPathProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawName = nameProp != null && nameProp.propertyType == SerializedPropertyType.String
                        ? nameProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawLabel = labelProp != null && labelProp.propertyType == SerializedPropertyType.String
                        ? labelProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawParamName = paramNameProp != null && paramNameProp.propertyType == SerializedPropertyType.String
                        ? paramNameProp.stringValue ?? string.Empty
                        : string.Empty;

                    string rawNamePath = !string.IsNullOrWhiteSpace(rawName) && rawName.IndexOf('/') >= 0
                        ? rawName
                        : string.Empty;

                    string resolvedPathSource = !string.IsNullOrWhiteSpace(rawMenuPath)
                        ? rawMenuPath
                        : rawNamePath;

                    string normalizedFullPath = NormalizeSlashPath(resolvedPathSource);
                    string normalizedMenuPath = normalizedFullPath;
                    string displayName = string.Empty;

                    if (!string.IsNullOrWhiteSpace(rawLabel))
                    {
                        displayName = rawLabel.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(rawNamePath))
                    {
                        string[] nameParts = NormalizeSlashPath(rawNamePath).Split('/');
                        if (nameParts.Length > 0)
                        {
                            displayName = nameParts[nameParts.Length - 1];
                            if (nameParts.Length > 1)
                                normalizedMenuPath = string.Join("/", nameParts.Take(nameParts.Length - 1));
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(rawName))
                    {
                        displayName = rawName.Trim();
                    }
                    else if (!string.IsNullOrWhiteSpace(rawParamName))
                    {
                        displayName = rawParamName.Trim();
                    }

                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        if (!string.IsNullOrWhiteSpace(normalizedFullPath))
                        {
                            int lastSlash = normalizedFullPath.LastIndexOf('/');
                            if (lastSlash >= 0 && lastSlash < normalizedFullPath.Length - 1)
                            {
                                displayName = normalizedFullPath.Substring(lastSlash + 1);
                                normalizedMenuPath = normalizedFullPath.Substring(0, lastSlash);
                            }
                            else
                            {
                                displayName = normalizedFullPath;
                            }
                        }
                    }

                    // Candidate parameter names this toggle payload may emit.
                    var candidateParamNames = new List<string>(3);
                    if (useGlobal && !string.IsNullOrWhiteSpace(globalName))
                        candidateParamNames.Add(globalName);
                    if (!string.IsNullOrWhiteSpace(rawParamName))
                        candidateParamNames.Add(rawParamName.Trim());
                    if (!string.IsNullOrWhiteSpace(rawName) && rawName.IndexOf('/') < 0)
                        candidateParamNames.Add(rawName.Trim());

                    for (int c = 0; c < candidateParamNames.Count; c++)
                    {
                        string candidate = candidateParamNames[c];
                        if (string.IsNullOrWhiteSpace(candidate))
                            continue;
                        if (result.ContainsKey(candidate))
                            continue;

                        string resolvedDisplay = string.IsNullOrWhiteSpace(displayName) ? candidate : displayName;
                        if (candidate.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, StringComparison.Ordinal))
                            resolvedDisplay = candidate;

                        result[candidate] = (normalizedMenuPath, resolvedDisplay);
                    }
                } while (iterator.NextVisible(true));
            }

            // Also include deterministic names for eligible toggle candidates that
            // are not currently materialized into avatar expression parameters.
            if (avatar.gameObject != null)
            {
                var reserved = new HashSet<string>(result.Keys, StringComparer.Ordinal);
                var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(avatar.gameObject);
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    string deterministic = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                        candidate.MenuPathHint,
                        candidate.ObjectPath,
                        reserved);

                    if (string.IsNullOrWhiteSpace(deterministic) || result.ContainsKey(deterministic))
                        continue;

                    string menuPath = NormalizeSlashPath(candidate.MenuPathHint);
                    string displayName = deterministic;

                    result[deterministic] = (menuPath, displayName);
                }
            }

            return result;
        }

        private static string NormalizeSlashPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Replace('\\', '/');
            var rawSegments = normalized.Split('/');
            var cleanSegments = new List<string>(rawSegments.Length);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = NormalizeMenuPathSegment(rawSegments[i]);
                if (!string.IsNullOrEmpty(segment))
                    cleanSegments.Add(segment);
            }

            return cleanSegments.Count == 0 ? string.Empty : string.Join("/", cleanSegments);
        }

        private static bool TryInferMenuPathAndDisplayNameFromAsmVfGlobalName(
            string paramName,
            Dictionary<string, string> sanitizedPrefixToMenuPath,
            out string menuPath,
            out string displayName)
        {
            menuPath = string.Empty;
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
                return false;
            if (!paramName.StartsWith(ASMLiteToggleNameBroker.GlobalPrefix, StringComparison.Ordinal))
                return false;

            string withoutPrefix = paramName.Substring(ASMLiteToggleNameBroker.GlobalPrefix.Length);
            int split = withoutPrefix.IndexOf("__", StringComparison.Ordinal);
            if (split <= 0)
                return false;

            string menuToken = withoutPrefix.Substring(0, split);

            // First try exact token->path match from discovered VRCFury menu prefixes.
            if (sanitizedPrefixToMenuPath != null
                && sanitizedPrefixToMenuPath.TryGetValue(menuToken, out string exactPath)
                && !string.IsNullOrWhiteSpace(exactPath))
            {
                string normalized = NormalizeSlashPath(exactPath);
                if (TrySplitMenuPathForLabel(normalized, out menuPath, out displayName))
                    return true;
            }

            // Then try longest discovered prefix token match. This handles deterministic
            // names where menuToken includes the leaf label (e.g. prefix + _Bass).
            if (sanitizedPrefixToMenuPath != null && sanitizedPrefixToMenuPath.Count > 0)
            {
                string bestKey = null;
                string bestPath = null;
                foreach (var kvp in sanitizedPrefixToMenuPath)
                {
                    string key = kvp.Key;
                    if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(kvp.Value))
                        continue;

                    if (string.Equals(menuToken, key, StringComparison.Ordinal)
                        || menuToken.StartsWith(key + "_", StringComparison.Ordinal))
                    {
                        if (bestKey == null || key.Length > bestKey.Length)
                        {
                            bestKey = key;
                            bestPath = kvp.Value;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(bestKey) && !string.IsNullOrWhiteSpace(bestPath))
                {
                    string remainder = menuToken.Length > bestKey.Length
                        ? menuToken.Substring(bestKey.Length).TrimStart('_')
                        : string.Empty;

                    string normalizedParent = NormalizeSlashPath(bestPath);
                    if (!string.IsNullOrWhiteSpace(remainder))
                    {
                        string remainderLabel = DecodeAsmVfTokenToWords(remainder);
                        if (!string.IsNullOrWhiteSpace(normalizedParent)
                            && !string.IsNullOrWhiteSpace(remainderLabel))
                        {
                            menuPath = normalizedParent;
                            displayName = remainderLabel;
                            return true;
                        }
                    }

                    if (TrySplitMenuPathForLabel(normalizedParent, out menuPath, out displayName))
                        return true;
                }
            }

            // Final fallback when no discoverable prefix exists: decode token into
            // a reasonable flat folder+label shape instead of deep underscore nesting.
            string[] words = SplitAsmVfTokenWords(menuToken);
            if (words.Length < 2)
                return false;

            menuPath = string.Join(" ", words.Take(words.Length - 1));
            displayName = words[words.Length - 1];
            return !string.IsNullOrWhiteSpace(menuPath) && !string.IsNullOrWhiteSpace(displayName);
        }

        private static bool TrySplitMenuPathForLabel(string fullPath, out string menuPath, out string displayName)
        {
            menuPath = string.Empty;
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(fullPath))
                return false;

            var segments = fullPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                return false;

            displayName = segments[segments.Length - 1];
            menuPath = segments.Length > 1
                ? string.Join("/", segments.Take(segments.Length - 1))
                : segments[0];

            return !string.IsNullOrWhiteSpace(menuPath) && !string.IsNullOrWhiteSpace(displayName);
        }

        private static string[] SplitAsmVfTokenWords(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return Array.Empty<string>();

            return token
                .Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();
        }

        private static string DecodeAsmVfTokenToWords(string token)
        {
            var words = SplitAsmVfTokenWords(token);
            if (words.Length == 0)
                return string.Empty;

            return string.Join(" ", words);
        }

        private static bool TryInferMenuPathAndDisplayNameFromParamName(
            string paramName,
            out string menuPath,
            out string displayName)
        {
            menuPath = string.Empty;
            displayName = string.Empty;

            if (string.IsNullOrWhiteSpace(paramName))
                return false;

            string normalized = paramName.Replace('\\', '/').Trim();
            if (normalized.IndexOf('/') < 0)
                return false;

            var rawSegments = normalized.Split('/');
            var cleanSegments = new List<string>(rawSegments.Length);
            for (int i = 0; i < rawSegments.Length; i++)
            {
                string segment = NormalizeMenuPathSegment(rawSegments[i]);
                if (!string.IsNullOrEmpty(segment))
                    cleanSegments.Add(segment);
            }

            if (cleanSegments.Count < 2)
                return false;

            displayName = cleanSegments[cleanSegments.Count - 1];
            menuPath = string.Join("/", cleanSegments.Take(cleanSegments.Count - 1));

            return !string.IsNullOrEmpty(menuPath) && !string.IsNullOrEmpty(displayName);
        }

        private static void ScanMenuForParamLocations(
            VRCExpressionsMenu menu, string parentPath,
            Dictionary<string, string> paramToPath,
            HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || !visited.Add(menu) || menu.controls == null) return;

            foreach (var control in menu.controls)
            {
                if (control == null) continue;

                // Main control parameter.
                if (!string.IsNullOrEmpty(control.parameter?.name)
                    && !paramToPath.ContainsKey(control.parameter.name))
                    paramToPath[control.parameter.name] = parentPath;

                // Sub-parameters (radial / 2-axis / 4-axis puppets).
                if (control.subParameters != null)
                    foreach (var sub in control.subParameters)
                        if (sub != null && !string.IsNullOrEmpty(sub.name)
                            && !paramToPath.ContainsKey(sub.name))
                            paramToPath[sub.name] = parentPath;

                // Recurse into submenus.
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu
                    && control.subMenu != null)
                {
                    string seg = NormalizeMenuPathSegment(control.name);
                    string childPath = string.IsNullOrEmpty(parentPath) ? seg
                        : string.IsNullOrEmpty(seg) ? parentPath
                        : parentPath + "/" + seg;
                    ScanMenuForParamLocations(control.subMenu, childPath, paramToPath, visited);
                }
            }
        }

        private static ParamTreeNode EnsureParamMenuNode(
            Dictionary<string, ParamTreeNode> nodeMap, string path)
        {
            if (nodeMap.TryGetValue(path, out var existing)) return existing;

            int slash = path.LastIndexOf('/');
            string parentPath = slash < 0 ? string.Empty : path.Substring(0, slash);
            string name       = slash < 0 ? path          : path.Substring(slash + 1);

            var parent = EnsureParamMenuNode(nodeMap, parentPath);
            var node   = new ParamTreeNode { Name = name, MenuPath = path };
            parent.Children.Add(node);
            nodeMap[path] = node;
            return node;
        }

        private static void SortParamTreeChildren(ParamTreeNode node)
        {
            // Folder nodes first, then param leaves, each group alphabetical.
            node.Children.Sort((a, b) =>
            {
                if (a.IsParam != b.IsParam) return a.IsParam ? 1 : -1;
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (var child in node.Children)
                if (!child.IsParam) SortParamTreeChildren(child);
        }

        private static string[] GetBackableParameterNames(VRCAvatarDescriptor avatar)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);

            // 1) Existing avatar expression parameters (current runtime truth).
            if (avatar?.expressionParameters?.parameters != null)
            {
                foreach (var p in avatar.expressionParameters.parameters)
                {
                    if (p == null || string.IsNullOrEmpty(p.name))
                        continue;
                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;
                    if (p.valueType != VRCExpressionParameters.ValueType.Bool
                        && p.valueType != VRCExpressionParameters.ValueType.Int
                        && p.valueType != VRCExpressionParameters.ValueType.Float)
                        continue;

                    names.Add(p.name);
                }
            }

            // 2) VRCFury FullController referenced parameter assets (content.prms).
            //    Include these pre-bake so package/prefab-provided parameter files
            //    (for example media-control prefabs) are available in backup UI.
            if (avatar?.gameObject != null)
            {
                var referencedVfParams = GetVrcFuryReferencedParameterNames(avatar);
                for (int i = 0; i < referencedVfParams.Length; i++)
                {
                    string paramName = referencedVfParams[i];
                    if (string.IsNullOrWhiteSpace(paramName))
                        continue;
                    if (paramName.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    names.Add(paramName);
                }
            }

            // 3) VRCFury Toggle globals already assigned on serialized toggle payloads.
            //    Include them pre-bake so Parameter Backup customization can target
            //    prefab-driven toggles (e.g., nested utility prefabs) even before
            //    expressionParameters has been rebuilt.
            if (avatar?.gameObject != null)
            {
                var assignedGlobals = ASMLiteToggleNameBroker.DiscoverAssignedToggleGlobalParams(avatar.gameObject);
                for (int i = 0; i < assignedGlobals.Count; i++)
                {
                    string assigned = assignedGlobals[i];
                    if (string.IsNullOrWhiteSpace(assigned))
                        continue;
                    if (assigned.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    names.Add(assigned);
                }
            }

            // 4) VRCFury Toggle candidates that will be deterministically promoted to
            //    globals during build-request enrollment. Include them pre-bake so
            //    Parameter Backup customization can target not-yet-assigned toggles.
            if (avatar?.gameObject != null)
            {
                var reserved = new HashSet<string>(names, StringComparer.Ordinal);
                var candidates = ASMLiteToggleNameBroker.DiscoverEligibleToggleCandidates(avatar.gameObject);
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    string deterministic = ASMLiteToggleNameBroker.BuildDeterministicGlobalName(
                        candidate.MenuPathHint,
                        candidate.ObjectPath,
                        reserved);

                    if (string.IsNullOrWhiteSpace(deterministic))
                        continue;
                    if (deterministic.StartsWith("ASMLite_", StringComparison.Ordinal))
                        continue;

                    names.Add(deterministic);
                }
            }

            // If both sides of a broker mapping are present, show only the
            // original discovered parameter in the checklist and keep the
            // deterministic ASM_VF_* side hidden.
            var mappings = ASMLiteToggleNameBroker.GetLatestGlobalParamMappings();
            if (mappings != null && mappings.Length > 0)
            {
                for (int i = 0; i < mappings.Length; i++)
                {
                    var mapping = mappings[i];
                    if (string.IsNullOrWhiteSpace(mapping.OriginalGlobalParam)
                        || string.IsNullOrWhiteSpace(mapping.AssignedGlobalParam))
                        continue;

                    if (names.Contains(mapping.OriginalGlobalParam)
                        && names.Contains(mapping.AssignedGlobalParam))
                    {
                        names.Remove(mapping.AssignedGlobalParam);
                    }
                }
            }

            return names
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string[] GetAvatarSubmenuPaths(VRCAvatarDescriptor avatar)
        {
            if (avatar == null || avatar.expressionsMenu == null)
                return Array.Empty<string>();

            var allPaths = new HashSet<string>(StringComparer.Ordinal);
            var parentPaths = new HashSet<string>(StringComparer.Ordinal);
            var visitedMenus = new HashSet<VRCExpressionsMenu>();
            CollectSubmenuPathsRecursive(avatar.expressionsMenu, string.Empty, allPaths, parentPaths, visitedMenus);

            // Return every submenu path — parent folders are valid install locations too.
            return allPaths
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetVrcFuryMenuPrefixes(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return Array.Empty<string>();

            var paths = new HashSet<string>(StringComparer.Ordinal);
            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                // FullController: content.menus array with prefix + menu asset pairs.
                var menusProperty = so.FindProperty("content.menus");
                if (menusProperty != null && menusProperty.isArray)
                {
                    for (int menuIndex = 0; menuIndex < menusProperty.arraySize; menuIndex++)
                    {
                        var menuEntry = menusProperty.GetArrayElementAtIndex(menuIndex);
                        if (menuEntry == null)
                            continue;

                        var prefixProperty = menuEntry.FindPropertyRelative("prefix");
                        string normalizedPrefix = prefixProperty == null
                            ? string.Empty
                            : NormalizeMenuPathSegment(prefixProperty.stringValue);

                        if (!string.IsNullOrEmpty(normalizedPrefix))
                            paths.Add(normalizedPrefix);

                        var menuObjRef = FindVrcFuryMenuObjectReference(menuEntry);
                        var menuAsset = menuObjRef != null ? menuObjRef.objectReferenceValue as VRCExpressionsMenu : null;
                        if (menuAsset != null)
                        {
                            var visitedMenus = new HashSet<VRCExpressionsMenu>();
                            CollectVrcFuryMenuPathsRecursive(menuAsset, normalizedPrefix, paths, visitedMenus);
                        }
                    }
                }

                // Toggle / SPS / other features: iterate ALL string properties under
                // "content" whose name starts with "menu". This is robust across VRCFury
                // versions without hardcoding per-type field names like "menuPath".
                ScanVrcFuryContentForMenuPaths(so, paths);
            }

            return paths
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToArray();
        }

        private static string[] GetVrcFuryReferencedParameterNames(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return Array.Empty<string>();

            var names = new HashSet<string>(StringComparer.Ordinal);
            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                var prmsProperty = so.FindProperty("content.prms");
                if (prmsProperty == null || !prmsProperty.isArray)
                    continue;

                for (int entryIndex = 0; entryIndex < prmsProperty.arraySize; entryIndex++)
                {
                    var entry = prmsProperty.GetArrayElementAtIndex(entryIndex);
                    if (entry == null)
                        continue;

                    var parametersRefProp = FindVrcFuryParametersObjectReference(entry);
                    var parametersIdProp = entry.FindPropertyRelative("parameters.id");

                    VRCExpressionParameters referencedParams = null;
                    if (parametersRefProp != null)
                        referencedParams = parametersRefProp.objectReferenceValue as VRCExpressionParameters;

                    if (referencedParams == null && parametersIdProp != null)
                    {
                        string referencedPath = ParseVrcFuryReferencePath(parametersIdProp.stringValue);
                        if (!string.IsNullOrWhiteSpace(referencedPath))
                            referencedParams = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(referencedPath);
                    }

                    if (referencedParams?.parameters == null)
                        continue;

                    for (int paramIndex = 0; paramIndex < referencedParams.parameters.Length; paramIndex++)
                    {
                        var param = referencedParams.parameters[paramIndex];
                        if (param == null || string.IsNullOrWhiteSpace(param.name))
                            continue;

                        if (param.valueType != VRCExpressionParameters.ValueType.Bool
                            && param.valueType != VRCExpressionParameters.ValueType.Int
                            && param.valueType != VRCExpressionParameters.ValueType.Float)
                            continue;

                        names.Add(param.name);
                    }
                }
            }

            return names
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static SerializedProperty FindVrcFuryParametersObjectReference(SerializedProperty prmsEntry)
        {
            if (prmsEntry == null)
                return null;

            var direct = prmsEntry.FindPropertyRelative("parameters.objRef");
            if (direct != null)
                return direct;

            return prmsEntry.FindPropertyRelative("parameters");
        }

        private static string ParseVrcFuryReferencePath(string serializedId)
        {
            if (string.IsNullOrWhiteSpace(serializedId))
                return string.Empty;

            string trimmed = serializedId.Trim();
            int split = trimmed.IndexOf('|');
            if (split >= 0 && split < trimmed.Length - 1)
                return trimmed.Substring(split + 1).Trim();

            return trimmed;
        }

        /// <summary>
        /// Iterates all serialized string properties under a VRCFury component's "content"
        /// managed reference and collects parent path segments from any property whose name
        /// starts with "menu". Works across VRCFury versions (Toggle, SPS, etc.) without
        /// hardcoding per-type field names.
        /// </summary>
        private static void ScanVrcFuryContentForMenuPaths(SerializedObject so, HashSet<string> paths)
        {
            var contentProp = so.FindProperty("content");
            if (contentProp == null)
                return;

            var it = contentProp.Copy();

            // Enter the managed reference's first child property.
            if (!it.Next(true))
                return;

            int baseDepth = contentProp.depth;

            while (it.depth > baseDepth)
            {
                if (it.propertyType == SerializedPropertyType.String)
                {
                    string val = it.stringValue;
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        string lowerName = it.name.ToLowerInvariant();

                        // Match fields whose name starts with "menu" (e.g. FullController
                        // menuPath variants), OR known path/name carriers used by VRCFury
                        // features:
                        //   - name with slash (Toggle-style menu/item path)
                        //   - *Path fields (e.g. MoveMenuItem.toPath, legacy content.path)
                        bool isMenuField = lowerName.StartsWith("menu", StringComparison.Ordinal);
                        bool isNamePath = lowerName == "name" && val.IndexOf('/') >= 0;
                        bool isPathField = lowerName.EndsWith("path", StringComparison.Ordinal) && val.IndexOf('/') >= 0;

                        if (isMenuField || isNamePath || isPathField)
                        {
                            string[] segs = val.Split('/');
                            // For Toggle "name" fields the last segment is the item name, not a
                            // folder — stop one short so only real parent menus are offered.
                            // For menu/path destination fields (menuPath, toPath, path), include
                            // all segments because the full value is itself a folder path.
                            int segLimit = isNamePath ? segs.Length - 1 : segs.Length;
                            var sb = new System.Text.StringBuilder();
                            for (int si = 0; si < segLimit; si++)
                            {
                                string seg = NormalizeMenuPathSegment(segs[si]);
                                if (string.IsNullOrEmpty(seg)) continue;
                                if (sb.Length > 0) sb.Append('/');
                                sb.Append(seg);
                                paths.Add(sb.ToString());
                            }
                        }
                    }
                }

                // Enter children up to 4 levels deep inside content; skip deeper subtrees.
                if (!it.Next(it.depth < baseDepth + 4))
                    break;
            }
        }

        private static SerializedProperty FindVrcFuryMenuObjectReference(SerializedProperty menuEntry)
        {
            if (menuEntry == null)
                return null;

            // Most common FullController schema path.
            var direct = menuEntry.FindPropertyRelative("menu.objRef");
            if (direct != null)
                return direct;

            // Fallback for nested serialized layouts.
            var menuProperty = menuEntry.FindPropertyRelative("menu");
            if (menuProperty == null)
                return null;

            return menuProperty.FindPropertyRelative("objRef");
        }

        private static void CollectVrcFuryMenuPathsRecursive(
            VRCExpressionsMenu menu,
            string parentPath,
            HashSet<string> paths,
            HashSet<VRCExpressionsMenu> visitedMenus)
        {
            if (menu == null || !visitedMenus.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                    continue;

                string segment = NormalizeMenuPathSegment(control.name);
                string fullPath = string.IsNullOrEmpty(parentPath)
                    ? segment
                    : (string.IsNullOrEmpty(segment) ? parentPath : $"{parentPath}/{segment}");

                if (!string.IsNullOrEmpty(fullPath))
                    paths.Add(fullPath);

                CollectVrcFuryMenuPathsRecursive(control.subMenu, fullPath, paths, visitedMenus);
            }
        }

        private static void CollectSubmenuPathsRecursive(
            VRCExpressionsMenu menu,
            string parentPath,
            HashSet<string> allPaths,
            HashSet<string> parentPaths,
            HashSet<VRCExpressionsMenu> visitedMenus)
        {
            if (menu == null || !visitedMenus.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu || control.subMenu == null)
                    continue;

                string segment = NormalizeMenuPathSegment(control.name);
                string fullPath = string.IsNullOrEmpty(parentPath)
                    ? segment
                    : (string.IsNullOrEmpty(segment) ? parentPath : $"{parentPath}/{segment}");

                if (string.IsNullOrEmpty(fullPath))
                    continue;

                allPaths.Add(fullPath);
                if (HasSubmenuChildren(control.subMenu))
                    parentPaths.Add(fullPath);

                CollectSubmenuPathsRecursive(control.subMenu, fullPath, allPaths, parentPaths, visitedMenus);
            }
        }

        private static bool HasSubmenuChildren(VRCExpressionsMenu menu)
        {
            if (menu == null || menu.controls == null)
                return false;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control != null && control.type == VRCExpressionsMenu.Control.ControlType.SubMenu && control.subMenu != null)
                    return true;
            }

            return false;
        }

        private static string NormalizeMenuPathSegment(string value)
        {
            string normalized = NormalizeOptionalString(value)
                .Replace('\\', '/')
                .Trim('/');

            return normalized;
        }

        private void DrawIconMode()
        {
            var component = GetOrRefreshComponent();

            EditorGUILayout.LabelField("Slot Icons", EditorStyles.miniBoldLabel);

            // Determine current mode and slot count based on whether component exists
            int currentSlotCount = component ? component.slotCount : _pendingSlotCount;
            IconMode currentMode = component ? component.iconMode : _pendingIconMode;
            int currentGearIndex = component ? component.selectedGearIndex : _pendingSelectedGearIndex;
            Texture2D[] currentCustomIcons = component ? component.customIcons : _pendingCustomIcons;

            // Always resize customIcons to match slotCount before any indexing.
            if (currentCustomIcons == null || currentCustomIcons.Length != currentSlotCount)
            {
                var resized = new Texture2D[currentSlotCount];
                if (currentCustomIcons != null)
                {
                    int copy = Mathf.Min(currentCustomIcons.Length, currentSlotCount);
                    System.Array.Copy(currentCustomIcons, resized, copy);
                }
                currentCustomIcons = resized;

                if (component)
                {
                    component.customIcons = resized;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingCustomIcons = resized;
                }
            }

            // Mode selector.
            var newMode = (IconMode)EditorGUILayout.EnumPopup("Icon Mode", currentMode);
            if (newMode != currentMode)
            {
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Icon Mode");
                    component.iconMode = newMode;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingIconMode = newMode;
                }
            }

            // Per-mode controls.
            switch (newMode)
            {
                case IconMode.SameColor:
                {
                    var colorNames = new[] { "Blue", "Red", "Green", "Purple", "Cyan", "Orange", "Pink", "Yellow" };
                    int newIndex = EditorGUILayout.Popup("Gear Color", currentGearIndex, colorNames);
                    if (newIndex != currentGearIndex)
                    {
                        if (component)
                        {
                            Undo.RecordObject(component, "Change ASM-Lite Gear Color");
                            component.selectedGearIndex = newIndex;
                            EditorUtility.SetDirty(component);
                        }
                        else
                        {
                            _pendingSelectedGearIndex = newIndex;
                        }
                    }
                    break;
                }

                case IconMode.MultiColor:
                {
                    EditorGUILayout.HelpBox(
                        "Each slot gets a unique gear color.\nSlots 1-4: Blue, Red, Green, Purple\nSlots 5-8: Cyan, Orange, Pink, Yellow",
                        MessageType.None);
                    break;
                }

                case IconMode.Custom:
                {
                    for (int i = 0; i < currentSlotCount; i++)
                    {
                        var newTex = (Texture2D)EditorGUILayout.ObjectField(
                            $"Slot {i + 1} Icon",
                            currentCustomIcons[i],
                            typeof(Texture2D),
                            allowSceneObjects: false);
                        if (newTex != currentCustomIcons[i])
                        {
                            currentCustomIcons[i] = newTex;
                            if (component)
                            {
                                Undo.RecordObject(component, "Change ASM-Lite Custom Icon");
                                component.customIcons[i] = newTex;
                                EditorUtility.SetDirty(component);
                            }
                            else
                            {
                                _pendingCustomIcons[i] = newTex;
                            }
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Draws the Action Icons section. Allows the user to choose between the
        /// bundled Save/Load/Clear Preset icons (Default) or custom Texture2D icons
        /// (Custom). Custom icons apply globally: the same three textures are used
        /// across all slot submenus.
        /// </summary>
        private void DrawActionIcons()
        {
            var component = GetOrRefreshComponent();

            EditorGUILayout.LabelField("Action Icons", EditorStyles.miniBoldLabel);

            ActionIconMode currentMode = component ? component.actionIconMode : _pendingActionIconMode;

            var newMode = (ActionIconMode)EditorGUILayout.EnumPopup("Action Icon Mode", currentMode);
            if (newMode != currentMode)
            {
                if (component)
                {
                    Undo.RecordObject(component, "Change ASM-Lite Action Icon Mode");
                    component.actionIconMode = newMode;
                    EditorUtility.SetDirty(component);
                }
                else
                {
                    _pendingActionIconMode = newMode;
                }
            }

            if (newMode == ActionIconMode.Custom)
            {
                Texture2D currentSave  = component ? component.customSaveIcon  : _pendingCustomSaveIcon;
                Texture2D currentLoad  = component ? component.customLoadIcon  : _pendingCustomLoadIcon;
                Texture2D currentClear = component ? component.customClearIcon : _pendingCustomClearIcon;

                // Save icon
                var newSave = (Texture2D)EditorGUILayout.ObjectField(
                    "Save Icon", currentSave, typeof(Texture2D), allowSceneObjects: false);
                if (newSave != currentSave)
                {
                    if (component)
                    {
                        Undo.RecordObject(component, "Change ASM-Lite Save Icon");
                        component.customSaveIcon = newSave;
                        EditorUtility.SetDirty(component);
                    }
                    else { _pendingCustomSaveIcon = newSave; }
                }

                // Load icon
                var newLoad = (Texture2D)EditorGUILayout.ObjectField(
                    "Load Icon", currentLoad, typeof(Texture2D), allowSceneObjects: false);
                if (newLoad != currentLoad)
                {
                    if (component)
                    {
                        Undo.RecordObject(component, "Change ASM-Lite Load Icon");
                        component.customLoadIcon = newLoad;
                        EditorUtility.SetDirty(component);
                    }
                    else { _pendingCustomLoadIcon = newLoad; }
                }

                // Clear Preset icon
                var newClear = (Texture2D)EditorGUILayout.ObjectField(
                    "Clear Preset Icon", currentClear, typeof(Texture2D), allowSceneObjects: false);
                if (newClear != currentClear)
                {
                    if (component)
                    {
                        Undo.RecordObject(component, "Change ASM-Lite Clear Preset Icon");
                        component.customClearIcon = newClear;
                        EditorUtility.SetDirty(component);
                    }
                    else { _pendingCustomClearIcon = newClear; }
                }
            }
        }

        // ── Wheel Preview ─────────────────────────────────────────────────────

        /// <summary>
        /// Resolves and caches the icon textures used by the preview wheel.
        /// Exits immediately if the settings signature is unchanged.
        /// </summary>
        private void RefreshPreviewCache(
            int slotCount, IconMode iconMode, int gearIndex,
            ActionIconMode actionIconMode,
            Texture2D[] customIcons, Texture2D customSave, Texture2D customLoad, Texture2D customClear)
        {
            int modeInt       = (int)iconMode;
            int actionModeInt = (int)actionIconMode;

            bool dirty = _previewSlotCount      != slotCount
                      || _previewIconMode       != modeInt
                      || _previewGearIndex      != gearIndex
                      || _previewActionIconMode != actionModeInt;

            if (!dirty && _previewGearTextures != null
                && _previewGearTextures.Length == slotCount)
            {
                if (iconMode == IconMode.Custom && customIcons != null)
                {
                    for (int i = 0; i < slotCount; i++)
                    {
                        var expected = (i < customIcons.Length) ? customIcons[i] : null;
                        if (_previewGearTextures[i] != expected) { dirty = true; break; }
                    }
                }
                if (actionIconMode == ActionIconMode.Custom
                    && (_previewSaveIcon  != customSave
                     || _previewLoadIcon  != customLoad
                     || _previewClearIcon != customClear))
                    dirty = true;
            }
            else dirty = true;

            if (!dirty) return;

            if (_previewFallback == null)
            {
                _previewFallback = new Texture2D(1, 1);
                _previewFallback.SetPixel(0, 0, new Color(0.35f, 0.35f, 0.35f));
                _previewFallback.Apply();
            }

            _previewGearTextures = new Texture2D[slotCount];
            for (int slot = 1; slot <= slotCount; slot++)
            {
                Texture2D tex = null;
                switch (iconMode)
                {
                    case IconMode.SameColor:
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                            ASMLiteAssetPaths.GearIconPaths[gearIndex]);
                        break;
                    case IconMode.MultiColor:
                        tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                            ASMLiteAssetPaths.GearIconPaths[(slot - 1) % ASMLiteAssetPaths.GearIconPaths.Length]);
                        break;
                    case IconMode.Custom:
                        int idx = slot - 1;
                        if (customIcons != null && idx < customIcons.Length)
                            tex = customIcons[idx];
                        break;
                }
                _previewGearTextures[slot - 1] = tex != null ? tex : _previewFallback;
            }

            if (actionIconMode == ActionIconMode.Custom)
            {
                _previewSaveIcon  = customSave  != null ? customSave  : _previewFallback;
                _previewLoadIcon  = customLoad  != null ? customLoad  : _previewFallback;
                _previewClearIcon = customClear != null ? customClear : _previewFallback;
            }
            else
            {
                // Load once and hold -- these paths never change. The ??= null check
                // also handles post-domain-reload resets (Unity clears instance fields).
                _cachedIconSave  ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconSave);
                _cachedIconLoad  ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconLoad);
                _cachedIconClear ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ASMLiteAssetPaths.IconReset);
                _previewSaveIcon  = _cachedIconSave  ?? _previewFallback;
                _previewLoadIcon  = _cachedIconLoad  ?? _previewFallback;
                _previewClearIcon = _cachedIconClear ?? _previewFallback;
            }

            _previewSlotCount      = slotCount;
            _previewIconMode       = modeInt;
            _previewGearIndex      = gearIndex;
            _previewActionIconMode = actionModeInt;
        }

        /// <summary>
        /// Draws a VRC-style radial menu preview: main slot wheel and inset
        /// Save/Load/Clear sub-wheel for slot 1.
        /// </summary>
        private void DrawWheelPreview()
        {
            var component = GetOrRefreshComponent();

            int            slotCount   = component ? component.slotCount          : _pendingSlotCount;
            IconMode       iconMode    = component ? component.iconMode            : _pendingIconMode;
            int            gearIndex   = component ? component.selectedGearIndex   : _pendingSelectedGearIndex;
            ActionIconMode actionMode  = component ? component.actionIconMode      : _pendingActionIconMode;
            Texture2D[]    customIcons = component ? component.customIcons         : _pendingCustomIcons;
            Texture2D      customSave  = component ? component.customSaveIcon      : _pendingCustomSaveIcon;
            Texture2D      customLoad  = component ? component.customLoadIcon      : _pendingCustomLoadIcon;
            Texture2D      customClear = component ? component.customClearIcon     : _pendingCustomClearIcon;

            RefreshPreviewCache(slotCount, iconMode, gearIndex, actionMode,
                customIcons, customSave, customLoad, customClear);

            // Load back arrow once.
            if (_previewBackIcon == null)
                _previewBackIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(
                    ASMLiteAssetPaths.IconBackArrow) ?? _previewFallback;

            EditorGUILayout.LabelField("Expression Menu Preview", EditorStyles.miniBoldLabel);
            EditorGUILayout.LabelField(
                "Preview of generated menu icon placement.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4);

            float availWidth = EditorGUIUtility.currentViewWidth - 32f;

            // GestureManager canonical size is 300px. Keep the preview one step
            // smaller than the settings controls so it reads as confirmation,
            // not the primary focal point.
            float mainSize = Mathf.Clamp(availWidth * 0.46f, 150f, 260f);
            float subSize  = Mathf.Round(mainSize * 0.46f);

            Rect rowRect = GUILayoutUtility.GetRect(availWidth, mainSize + subSize * 0.35f + 4f);

            // Main wheel: left-center.
            Rect mainRect = new Rect(
                rowRect.x + availWidth * 0.24f - mainSize * 0.5f,
                rowRect.y,
                mainSize, mainSize);

            // Sub-wheel: offset down and right so it reads as a drill-down from the main wheel.
            Rect subRect = new Rect(
                rowRect.x + availWidth * 0.63f,
                rowRect.y + mainSize * 0.36f,
                subSize, subSize);

            if (Event.current.type != EventType.Repaint)
                return;

            // Main wheel: Back at top, then user slot icons.
            // Rebuild cached arrays only when size or content changed (handled by
            // RefreshPreviewCache above which sets _previewSlotCount when dirty).
            if (_mainWheelIcons == null || _mainWheelIcons.Length != slotCount + 1
                || _mainWheelLabels == null || _mainWheelLabels.Length != slotCount + 1)
            {
                _mainWheelIcons  = new Texture2D[slotCount + 1];
                _mainWheelLabels = new string[slotCount + 1];
                _mainWheelLabels[0] = "Back";
                for (int i = 0; i < slotCount; i++)
                    _mainWheelLabels[i + 1] = $"Slot {i + 1}";
            }
            _mainWheelIcons[0] = _previewBackIcon;
            for (int i = 0; i < slotCount; i++)
                _mainWheelIcons[i + 1] = _previewGearTextures[i];

            DrawRadialWheel(mainRect, _mainWheelIcons, _mainWheelLabels);

            // Sub-wheel: Back at top, then Save/Load/Clear.
            // Icon array must be rebuilt each repaint (action icons can change), but
            // the label array is static readonly -- no per-frame allocation.
            var subIcons = new[] { _previewBackIcon, _previewSaveIcon, _previewLoadIcon, _previewClearIcon };
            DrawRadialWheel(subRect, subIcons, s_subWheelLabels);

            // Connector line.
            var origHandles = Handles.color;
            Handles.color = new Color(s_wheelColorBorder.r, s_wheelColorBorder.g, s_wheelColorBorder.b, 0.6f);
            Handles.DrawLine(
                new Vector3(mainRect.xMax, mainRect.center.y),
                new Vector3(subRect.xMin,  subRect.center.y));
            Handles.color = origHandles;
        }

        /// <summary>
        /// Draws a VRC-style radial menu wheel using GestureManager exact dimensions and colors.
        /// Slot 0 is always at the top (12 o'clock) and goes clockwise.
        /// </summary>
        private void DrawRadialWheel(Rect rect, Texture2D[] icons, string[] labels)
        {
            int count = icons.Length;
            if (count == 0) return;

            float cx = rect.center.x;
            float cy = rect.center.y;

            // GestureManager: Size=300, radius=150. Scale proportionally.
            float scale      = rect.width / 300f;
            float outerR     = rect.width * 0.5f;         // 150px at 300
            float innerR     = rect.width / 6f;            // 50px at 300 (InnerSize/2 = 100/2)
            float iconRadius = rect.width / 3f;            // 100px at 300 (Size/3)
            float iconSize   = rect.width * 0.22f;         // ~66px at 300
            float halfIcon   = iconSize * 0.5f;

            var origColor   = GUI.color;
            var origHandles = Handles.color;

            // Background fill (full circle approximated by square: Handles clips it).
            GUI.color = s_wheelColorMain;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = origColor;

            // Outer ring.
            Handles.color = s_wheelColorBorder;
            Handles.DrawWireDisc(new Vector3(cx, cy), Vector3.forward, outerR - 1f);

            // Segment dividers: 2px lines between icons, offset by half a step.
            float angleStep = 360f / count;
            Handles.color = new Color(s_wheelColorBorder.r, s_wheelColorBorder.g, s_wheelColorBorder.b, 0.55f);
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Deg2Rad * (i * angleStep - 90f + angleStep * 0.5f);
                var p0 = new Vector3(cx + Mathf.Cos(a) * innerR,       cy + Mathf.Sin(a) * innerR,       0f);
                var p1 = new Vector3(cx + Mathf.Cos(a) * (outerR - 1f), cy + Mathf.Sin(a) * (outerR - 1f), 0f);
                Handles.DrawLine(p0, p1);
            }

            // Inner hub circle (RadialInner color, with teal border).
            Handles.color = s_wheelColorInner;
            Handles.DrawSolidDisc(new Vector3(cx, cy), Vector3.forward, innerR);
            Handles.color = s_wheelColorBorder;
            Handles.DrawWireDisc(new Vector3(cx, cy), Vector3.forward, innerR);

            // Icons and labels.
            // Reuse cached GUIStyle -- only update fontSize (depends on scale).
            if (_radialLabelStyle == null)
            {
                _radialLabelStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.UpperCenter,
                    normal    = { textColor = new Color(1f, 1f, 1f, 0.82f) }
                };
            }
            _radialLabelStyle.fontSize = Mathf.Max(7, Mathf.RoundToInt(8f * scale));
            var labelStyle = _radialLabelStyle;

            for (int i = 0; i < count; i++)
            {
                float angleRad = Mathf.Deg2Rad * (i * angleStep - 90f);
                float ix = cx + Mathf.Cos(angleRad) * iconRadius;
                float iy = cy + Mathf.Sin(angleRad) * iconRadius;

                Rect iconRect = new Rect(ix - halfIcon, iy - halfIcon, iconSize, iconSize);
                GUI.color = Color.white;
                GUI.DrawTexture(iconRect, icons[i] ?? _previewFallback, ScaleMode.ScaleToFit, alphaBlend: true);

                if (labels != null && i < labels.Length)
                {
                    float lblWidth = iconSize * 2.2f;
                    Rect lblRect = new Rect(ix - lblWidth * 0.5f, iy + halfIcon + 1f, lblWidth, 14f);
                    GUI.Label(lblRect, labels[i], labelStyle);
                }
            }

            Handles.color = origHandles;
            GUI.color     = origColor;
        }

        private void DrawStatus()
        {
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);

            var component = GetOrRefreshComponent();
            var toolState = GetAsmLiteToolState(_selectedAvatar, component);

            switch (toolState)
            {
                case AsmLiteToolState.PackageManaged:
                    EditorGUILayout.HelpBox("Tool State: Package Managed — ASM-Lite component is attached and editable.", MessageType.Info);
                    break;
                case AsmLiteToolState.Vendorized:
                    EditorGUILayout.HelpBox(
                        component != null
                            ? "Tool State: Vendorized — ASM-Lite stays attached and editable, and generated payload is mirrored under Assets/ASM-Lite."
                            : "Tool State: Vendorized — avatar references ASM-Lite assets copied under Assets/ASM-Lite and the tool object is detached.",
                        MessageType.Info);
                    break;
                case AsmLiteToolState.Detached:
                    EditorGUILayout.HelpBox("Tool State: Detached — avatar has baked ASM-Lite runtime data but no editable ASM-Lite component.", MessageType.Info);
                    break;
                default:
                    EditorGUILayout.HelpBox("Tool State: Not Installed — no ASM-Lite component or baked markers detected on this avatar.", MessageType.None);
                    break;
            }

            if (component)
            {
                EditorGUILayout.HelpBox(
                    "✓ ASM-Lite prefab is present on this avatar.",
                    MessageType.Info);

                // Guard against mid-reimport state: expressionParameters or its
                // parameters array can be transiently null while Unity is importing.
                try
                {
                    if (_discoveredParamCount >= 0)
                    {
                        // Post-build count: includes VRCFury Toggle/FullController params.
                        EditorGUILayout.HelpBox(
                            $"✓ {_discoveredParamCount} custom parameter(s) backed up across " +
                            $"{component.slotCount} slot(s).",
                            MessageType.Info);
                    }
                    else
                    {
                        var exprParams = _selectedAvatar.expressionParameters;
                        if (exprParams != null && exprParams.parameters != null)
                        {
                            if (_cachedCustomParamCount < 0)
                            {
                                _cachedCustomParamCount = exprParams.parameters
                                    .Count(p => !string.IsNullOrEmpty(p.name) && !p.name.StartsWith("ASMLite_", StringComparison.Ordinal));
                            }

                            EditorGUILayout.HelpBox(
                                $"✓ {_cachedCustomParamCount} custom parameter(s) detected: rebuild to include VRCFury parameters.",
                                MessageType.Info);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "⚠ No VRCExpressionParameters asset assigned on avatar descriptor.",
                                MessageType.Warning);
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    // Asset is mid-reimport: show a neutral message and wait for
                    // the next repaint when it will be stable again.
                    // Log unexpected exceptions (non-reimport bugs would otherwise
                    // be permanently hidden behind this UI message).
                    if (Event.current.type == EventType.Layout)
                    {
                        Debug.LogWarning($"[ASM-Lite] Expression parameters draw failed: {ex.GetType().Name}: {ex.Message}");
                        Repaint();
                    }
                    EditorGUILayout.HelpBox(
                        "⚠ Expression parameters are currently being imported. Please wait.",
                        MessageType.Warning);
                }

                DrawToggleBrokerStatus();
            }
            else
            {
                if (toolState == AsmLiteToolState.Detached || toolState == AsmLiteToolState.Vendorized)
                {
                    EditorGUILayout.HelpBox(
                        "ASM-Lite is detached on this avatar. You can return to package-managed mode below to restore editable ASM-Lite controls.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "ASM-Lite prefab has not been added to this avatar yet.\n" +
                        "Configure settings above, then click \"Add ASM-Lite Prefab\".",
                        MessageType.Warning);
                }
            }
        }

        private void DrawToggleBrokerStatus()
        {
            if (!ASMLiteToggleNameBroker.TryGetLatestEnrollmentReport(out var report))
                return;

            int totalAdjustments = report.PreflightCollisionAdjustments + report.CandidateCollisionAdjustments;
            if (totalAdjustments > 0)
            {
                EditorGUILayout.HelpBox(
                    $"[Toggle Broker] Last enrollment reserved {report.PreReservedNameCount} descriptor name(s) and adjusted deterministic assignments: preflight={report.PreflightCollisionAdjustments}, intra-candidate={report.CandidateCollisionAdjustments}.",
                    MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"[Toggle Broker] Last enrollment reserved {report.PreReservedNameCount} descriptor name(s) with no deterministic suffix adjustments needed.",
                MessageType.None);
        }

        private void DrawActionButton()
        {
            var component = GetOrRefreshComponent();
            var toolState = GetAsmLiteToolState(_selectedAvatar, component);

            if (component)
            {
                // Primary actions.
                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Rebuild ASM-Lite", GUILayout.Height(36), GUILayout.MinWidth(220)))
                {
                    var captured = component;
                    EditorApplication.delayCall += () => BakeAssets(captured);
                }

                var prevColor = GUI.color;
                GUI.color = new Color(1f, 0.45f, 0.45f);
                bool removeClicked = GUILayout.Button("Remove Prefab", GUILayout.Height(32), GUILayout.MinWidth(110));
                GUI.color = prevColor;
                if (removeClicked)
                {
                    bool confirm = EditorUtility.DisplayDialog(
                        "Remove ASM-Lite Prefab",
                        "Are you sure you want to remove the ASM-Lite prefab from this avatar?\n\n" +
                        "Any unsaved changes will be lost, but your avatar and expression parameters will not be affected.",
                        "Remove", "Cancel");

                    if (confirm)
                        EditorApplication.delayCall += () => RemovePrefab(component);
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(6f);

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Detach ASM-Lite (Runtime-safe)", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Keep your current in-game presets working, but remove the ASM-Lite tool object from this avatar. " +
                    "Great for sharing a finished avatar. You won’t be able to tweak ASM-Lite settings unless you add it again.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Detach ASM-Lite", GUILayout.Height(24)))
                {
                    var captured = component;
                    EditorApplication.delayCall += () => DetachAsmLite(captured, vendorizeToAssets: false);
                }
                EditorGUILayout.EndVertical();

                EditorGUILayout.Space(4f);

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.LabelField("Vendorize ASM-Lite Payload", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    "Keep ASM-Lite attached and editable, but mirror generated payload files into Assets/ASM-Lite/<AvatarName> " +
                    "and use those mirrored files instead of package generated assets.",
                    EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button("Vendorize (Keep Attached)", GUILayout.Height(24)))
                {
                    var captured = component;
                    EditorApplication.delayCall += () => VendorizeAsmLite(captured);
                }
                if (toolState == AsmLiteToolState.Vendorized)
                {
                    string currentVendorizedPath = NormalizeOptionalString(component.vendorizedGeneratedAssetsPath);
                    if (string.IsNullOrWhiteSpace(currentVendorizedPath))
                        currentVendorizedPath = "(path pending sync)";

                    EditorGUILayout.Space(2f);
                    EditorGUILayout.LabelField("Current vendorized folder:", EditorStyles.miniBoldLabel);
                    EditorGUILayout.SelectableLabel(currentVendorizedPath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                    EditorGUILayout.Space(2f);
                    if (GUILayout.Button("Return This Avatar to Package Managed", GUILayout.Height(22)))
                        EditorApplication.delayCall += ReturnToPackageManaged;
                }
                EditorGUILayout.EndVertical();
            }
            else
            {
                if (toolState == AsmLiteToolState.Detached || toolState == AsmLiteToolState.Vendorized)
                {
                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.LabelField("Return to Package Managed Mode", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField(
                        "Re-attach the editable ASM-Lite prefab and return this avatar to package-managed workflow. " +
                        "Keeps your current avatar content and restores normal ASM-Lite editing.",
                        EditorStyles.wordWrappedMiniLabel);
                    if (GUILayout.Button("Return to Package Managed", GUILayout.Height(28)))
                        EditorApplication.delayCall += ReturnToPackageManaged;
                    EditorGUILayout.EndVertical();
                }
                else
                {
                    if (GUILayout.Button("Add ASM-Lite Prefab", GUILayout.Height(36)))
                        EditorApplication.delayCall += AddPrefabToAvatar;
                }
            }
        }

        // ── Logic ─────────────────────────────────────────────────────────────

        // Per-frame component cache: refreshed once per OnGUI call, not once per draw section.
        private int _lastRefreshFrame = -1;

        private enum AsmLiteToolState
        {
            NotInstalled,
            PackageManaged,
            Detached,
            Vendorized,
        }

        private static AsmLiteToolState GetAsmLiteToolState(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            if (component != null)
                return component.useVendorizedGeneratedAssets ? AsmLiteToolState.Vendorized : AsmLiteToolState.PackageManaged;
            if (avatar == null)
                return AsmLiteToolState.NotInstalled;
            if (HasVendorizedAsmLiteReferences(avatar))
                return AsmLiteToolState.Vendorized;
            if (HasAsmLiteRuntimeMarkers(avatar))
                return AsmLiteToolState.Detached;
            return AsmLiteToolState.NotInstalled;
        }

        private static bool HasVendorizedAsmLiteReferences(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            const string vendorPrefix = "Assets/ASM-Lite/";

            string exprPath = avatar.expressionParameters ? AssetDatabase.GetAssetPath(avatar.expressionParameters)?.Replace('\\', '/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(exprPath) && exprPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                return true;

            string menuPath = avatar.expressionsMenu ? AssetDatabase.GetAssetPath(avatar.expressionsMenu)?.Replace('\\', '/') : string.Empty;
            if (!string.IsNullOrWhiteSpace(menuPath) && menuPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                return true;

            if (avatar.expressionsMenu != null && avatar.expressionsMenu.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control?.subMenu == null)
                        continue;

                    string subPath = AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/');
                    if (!string.IsNullOrWhiteSpace(subPath) && subPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                        return true;
                }
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var ctrl = avatar.baseAnimationLayers[i].animatorController;
                if (!ctrl)
                    continue;

                string ctrlPath = AssetDatabase.GetAssetPath(ctrl)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(ctrlPath) && ctrlPath.StartsWith(vendorPrefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool HasAsmLiteRuntimeMarkers(VRCAvatarDescriptor avatar)
        {
            if (avatar == null)
                return false;

            var expr = avatar.expressionParameters;
            if (expr?.parameters != null)
            {
                for (int i = 0; i < expr.parameters.Length; i++)
                {
                    var p = expr.parameters[i];
                    if (p == null || string.IsNullOrWhiteSpace(p.name))
                        continue;
                    if (p.name.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(p.name, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var ctrl = avatar.baseAnimationLayers[i].animatorController as UnityEditor.Animations.AnimatorController;
                if (ctrl == null)
                    continue;

                for (int j = 0; j < ctrl.layers.Length; j++)
                {
                    if (ctrl.layers[j].name.StartsWith("ASMLite_", StringComparison.Ordinal))
                        return true;
                }

                for (int j = 0; j < ctrl.parameters.Length; j++)
                {
                    string paramName = ctrl.parameters[j].name;
                    if (string.IsNullOrWhiteSpace(paramName))
                        continue;
                    if (paramName.StartsWith("ASMLite_", StringComparison.Ordinal)
                        || string.Equals(paramName, ASMLiteBuilder.CtrlParam, StringComparison.Ordinal))
                        return true;
                }
            }

            if (avatar.expressionsMenu?.controls != null)
            {
                for (int i = 0; i < avatar.expressionsMenu.controls.Count; i++)
                {
                    var control = avatar.expressionsMenu.controls[i];
                    if (control == null || control.type != VRCExpressionsMenu.Control.ControlType.SubMenu)
                        continue;

                    if (string.Equals(control.name, ASMLiteBuilder.DefaultRootControlName, StringComparison.Ordinal))
                        return true;

                    string subPath = control.subMenu ? AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/') : string.Empty;
                    if (!string.IsNullOrWhiteSpace(subPath)
                        && (subPath.IndexOf("ASMLite_", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/ASM-Lite/", StringComparison.OrdinalIgnoreCase) >= 0
                            || subPath.IndexOf("/com.staples.asm-lite/", StringComparison.OrdinalIgnoreCase) >= 0))
                        return true;
                }
            }

            return false;
        }

        private ASMLiteComponent GetOrRefreshComponent()
        {
            if (!_selectedAvatar)
            {
                _cachedComponent = null;
                return null;
            }

            // Refresh once per editor frame. Multiple Draw* calls in the same OnGUI
            // invocation reuse the cached result: avoids 3× GetComponentInChildren
            // per repaint and ensures consistent state within a single frame.
            int frame = Time.frameCount;
            if (frame != _lastRefreshFrame)
            {
                _cachedComponent   = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
                _lastRefreshFrame  = frame;
            }

            return _cachedComponent;
        }

        private static Texture2D[] CloneTextures(Texture2D[] source)
        {
            if (source == null || source.Length == 0)
                return Array.Empty<Texture2D>();

            var clone = new Texture2D[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static string NormalizeOptionalString(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private static string[] SanitizeExcludedParameterNames(string[] names)
        {
            if (names == null || names.Length == 0)
                return Array.Empty<string>();

            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static MonoBehaviour FindLiveVrcFuryComponent(ASMLiteComponent component)
        {
            if (component == null || component.gameObject == null)
                return null;

            var behaviors = component.gameObject.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviors.Length; i++)
            {
                var behavior = behaviors[i];
                if (behavior == null)
                    continue;

                var type = behavior.GetType();
                if (type == null)
                    continue;

                if (string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    return behavior;
            }

            return null;
        }

        private static bool TryRefreshLiveInstallPathPrefix(ASMLiteComponent component, string contextLabel)
        {
            if (component == null)
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Cannot refresh install-path routing because the ASM-Lite component was null.");
                return false;
            }

            if (!ASMLiteBuilder.TrySyncInstallPathRouting(component))
            {
                Debug.LogError($"[ASM-Lite] {contextLabel}: Failed to refresh install-path routing on '{component.gameObject.name}'.");
                return false;
            }

            var effectivePrefix = ASMLiteFullControllerInstallPathHelper.ResolveEffectivePrefix(component);
            if (string.IsNullOrEmpty(effectivePrefix))
                Debug.Log($"[ASM-Lite] {contextLabel}: refreshed install-path routing to root on '{component.gameObject.name}'.");
            else
                Debug.Log($"[ASM-Lite] {contextLabel}: refreshed install-path routing to '{effectivePrefix}' on '{component.gameObject.name}'.");

            return true;
        }

        private static string SanitizePathFragment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Avatar";

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (invalid.Contains(c))
                    sb.Append('_');
                else
                    sb.Append(c);
            }

            string cleaned = sb.ToString().Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? "Avatar" : cleaned;
        }

        private static string EnsureAssetFolder(string parent, string child)
        {
            string normalizedParent = parent.Replace('\\', '/').TrimEnd('/');
            string candidate = normalizedParent + "/" + child;
            if (!AssetDatabase.IsValidFolder(candidate))
                AssetDatabase.CreateFolder(normalizedParent, child);
            return candidate;
        }

        private static string EnsureVendorizeRootFolder(VRCAvatarDescriptor avatar)
        {
            string root = EnsureAssetFolder("Assets", "ASM-Lite");
            string avatarFolder = EnsureAssetFolder(root, SanitizePathFragment(avatar != null ? avatar.gameObject.name : "Avatar"));
            return EnsureAssetFolder(avatarFolder, "GeneratedAssets");
        }

        private static void CopyAssetIfPresent(string sourcePath, string destinationPath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(destinationPath))
                return;

            if (!AssetDatabase.LoadMainAssetAtPath(sourcePath))
                return;

            AssetDatabase.DeleteAsset(destinationPath);
            AssetDatabase.CopyAsset(sourcePath, destinationPath);
        }

        private static void RetargetMenuGeneratedSubmenus(VRCExpressionsMenu menu, string sourcePrefix, string destinationPrefix, HashSet<VRCExpressionsMenu> visited)
        {
            if (menu == null || visited == null || !visited.Add(menu) || menu.controls == null)
                return;

            for (int i = 0; i < menu.controls.Count; i++)
            {
                var control = menu.controls[i];
                if (control == null || control.subMenu == null)
                    continue;

                string subPath = AssetDatabase.GetAssetPath(control.subMenu)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(subPath)
                    && subPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    string fileName = Path.GetFileName(subPath);
                    string newPath = destinationPrefix + "/" + fileName;
                    var replaced = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(newPath);
                    if (replaced != null)
                    {
                        control.subMenu = replaced;
                        menu.controls[i] = control;
                        EditorUtility.SetDirty(menu);
                    }
                }

                RetargetMenuGeneratedSubmenus(control.subMenu, sourcePrefix, destinationPrefix, visited);
            }
        }

        private static bool TryVendorizeGeneratedAssetsToAvatarFolder(VRCAvatarDescriptor avatar, out string vendorizedDir)
        {
            vendorizedDir = string.Empty;
            if (avatar == null)
                return false;

            string sourcePrefix = ASMLiteAssetPaths.GeneratedDir.Replace('\\', '/').TrimEnd('/');
            string targetDir = EnsureVendorizeRootFolder(avatar);

            var generatedGuids = AssetDatabase.FindAssets(string.Empty, new[] { sourcePrefix });
            for (int i = 0; i < generatedGuids.Length; i++)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(generatedGuids[i])?.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(sourcePath) || Directory.Exists(sourcePath))
                    continue;

                string fileName = Path.GetFileName(sourcePath);
                string destinationPath = targetDir + "/" + fileName;
                CopyAssetIfPresent(sourcePath, destinationPath);
            }

            // Retarget descriptor-level generated assets.
            if (avatar.expressionParameters != null)
            {
                string exprPath = AssetDatabase.GetAssetPath(avatar.expressionParameters)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(exprPath) && exprPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionParameters>(targetDir + "/" + Path.GetFileName(exprPath));
                    if (replacement != null)
                    {
                        avatar.expressionParameters = replacement;
                        EditorUtility.SetDirty(avatar);
                    }
                }
            }

            if (avatar.expressionsMenu != null)
            {
                string menuPath = AssetDatabase.GetAssetPath(avatar.expressionsMenu)?.Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(menuPath) && menuPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                {
                    var replacement = AssetDatabase.LoadAssetAtPath<VRCExpressionsMenu>(targetDir + "/" + Path.GetFileName(menuPath));
                    if (replacement != null)
                    {
                        avatar.expressionsMenu = replacement;
                        EditorUtility.SetDirty(avatar);
                    }
                }

                RetargetMenuGeneratedSubmenus(avatar.expressionsMenu, sourcePrefix, targetDir, new HashSet<VRCExpressionsMenu>());
            }

            for (int i = 0; i < avatar.baseAnimationLayers.Length; i++)
            {
                var layer = avatar.baseAnimationLayers[i];
                var controller = layer.animatorController;
                string ctrlPath = controller ? AssetDatabase.GetAssetPath(controller)?.Replace('\\', '/') : string.Empty;
                if (string.IsNullOrWhiteSpace(ctrlPath) || !ctrlPath.StartsWith(sourcePrefix, StringComparison.Ordinal))
                    continue;

                var replacement = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(targetDir + "/" + Path.GetFileName(ctrlPath));
                if (replacement == null)
                    continue;

                layer.animatorController = replacement;
                avatar.baseAnimationLayers[i] = layer;
                EditorUtility.SetDirty(avatar);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            vendorizedDir = targetDir;
            return true;
        }

        private static bool TryRetargetLiveFullControllerGeneratedAssets(ASMLiteComponent component, string generatedDir)
        {
            if (component == null || string.IsNullOrWhiteSpace(generatedDir))
                return false;

            var vfComponent = FindLiveVrcFuryComponent(component);
            if (vfComponent == null)
                return false;

            var fxController = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(generatedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.FXController));
            var menu = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(generatedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.Menu));
            var parameters = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(generatedDir + "/" + Path.GetFileName(ASMLiteAssetPaths.ExprParams));
            if (fxController == null || menu == null || parameters == null)
                return false;

            var so = new SerializedObject(vfComponent);
            so.Update();

            bool applied = false;
            applied |= SetObjectReferenceIfPresent(so, "content.controllers.Array.data[0].controller.objRef", fxController);
            applied |= SetObjectReferenceIfPresent(so, "content.menus.Array.data[0].menu.objRef", menu);
            applied |= SetObjectReferenceIfPresent(so, "content.prms.Array.data[0].parameters.objRef", parameters);
            applied |= SetObjectReferenceIfPresent(so, "content.prms.Array.data[0].parameter.objRef", parameters);
            applied |= SetObjectReferenceIfPresent(so, "content.prms.Array.data[0].objRef", parameters);
            applied |= SetObjectReferenceIfPresent(so, "content.controller.objRef", fxController);
            applied |= SetObjectReferenceIfPresent(so, "content.menu.objRef", menu);
            applied |= SetObjectReferenceIfPresent(so, "content.parameters.objRef", parameters);

            if (!applied)
                return false;

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(vfComponent);
            return true;
        }

        private static bool SetObjectReferenceIfPresent(SerializedObject so, string path, UnityEngine.Object value)
        {
            var prop = so.FindProperty(path);
            if (prop == null)
                return false;

            prop.objectReferenceValue = value;
            return true;
        }

        private void VendorizeAsmLite(ASMLiteComponent component)
        {
            if (component == null)
                return;

            const string modeLabel = "Vendorize ASM-Lite (Keep Attached)";
            bool confirm = EditorUtility.DisplayDialog(
                modeLabel,
                "This will keep ASM-Lite attached and editable, mirror generated payload files into Assets/ASM-Lite/<AvatarName>/GeneratedAssets, and switch this avatar to those mirrored files. Continue?",
                "Continue",
                "Cancel");
            if (!confirm)
                return;

            var avatar = component.GetComponentInParent<VRCAvatarDescriptor>();
            if (avatar == null)
            {
                EditorUtility.DisplayDialog(modeLabel, "No VRCAvatarDescriptor found for this ASM-Lite component.", "OK");
                return;
            }

            if (!TryRefreshLiveInstallPathPrefix(component, "Vendorize"))
            {
                EditorUtility.DisplayDialog(modeLabel, "Failed to refresh FullController install prefix before vendorizing.", "OK");
                return;
            }

            int count = ASMLiteBuilder.Build(component);
            if (count >= 0)
                _discoveredParamCount = count;

            if (!TryVendorizeGeneratedAssetsToAvatarFolder(avatar, out string vendorizedDir))
            {
                EditorUtility.DisplayDialog(modeLabel, "Failed to mirror generated assets to Assets/ASM-Lite.", "OK");
                return;
            }

            if (!TryRetargetLiveFullControllerGeneratedAssets(component, vendorizedDir))
            {
                EditorUtility.DisplayDialog(modeLabel, "Mirrored assets were created, but live FullController references could not be retargeted.", "OK");
                return;
            }

            Undo.RecordObject(component, "Enable ASM-Lite Vendorized Assets");
            component.useVendorizedGeneratedAssets = true;
            component.vendorizedGeneratedAssetsPath = vendorizedDir;
            EditorUtility.SetDirty(component);

            _cachedCustomParamCount = -1;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ASM-Lite] Vendorized generated payload for '{avatar.gameObject.name}' to '{vendorizedDir}' and kept ASM-Lite attached.");
            EditorUtility.DisplayDialog(modeLabel + " Complete", $"Vendorized assets folder:\n{vendorizedDir}\n\nASM-Lite remains attached and editable on this avatar.", "OK");
            Repaint();
        }

        private void DetachAsmLite(ASMLiteComponent component, bool vendorizeToAssets)
        {
            if (component == null)
                return;

            string modeLabel = vendorizeToAssets ? "Vendorize + Detach" : "Detach ASM-Lite";
            bool confirm = EditorUtility.DisplayDialog(
                modeLabel,
                vendorizeToAssets
                    ? "This will bake ASM-Lite directly into avatar assets, copy generated assets into Assets/ASM-Lite/<AvatarName>/GeneratedAssets, then remove the ASM-Lite prefab object. Continue?"
                    : "This will bake ASM-Lite directly into avatar assets, then remove the ASM-Lite prefab object. Continue?",
                "Continue",
                "Cancel");

            if (!confirm)
                return;

            if (!ASMLiteBuilder.TryDetachToDirectDelivery(component, out string detail))
            {
                Debug.LogError(detail);
                EditorUtility.DisplayDialog(modeLabel, detail, "OK");
                return;
            }

            var avatar = component.GetComponentInParent<VRCAvatarDescriptor>();
            string vendorizedDir = string.Empty;
            if (vendorizeToAssets && avatar != null)
            {
                if (!TryVendorizeGeneratedAssetsToAvatarFolder(avatar, out vendorizedDir))
                {
                    Debug.LogWarning("[ASM-Lite] Vendorize requested, but generated assets could not be copied/rebound. Detached payload still applied.");
                }
            }

            Undo.SetCurrentGroupName(modeLabel);
            int group = Undo.GetCurrentGroup();
            Undo.DestroyObjectImmediate(component.gameObject);
            Undo.CollapseUndoOperations(group);

            _cachedComponent = null;
            _lastRefreshFrame = -1;
            _discoveredParamCount = -1;
            _cachedCustomParamCount = -1;

            string completion;
            if (vendorizeToAssets && !string.IsNullOrWhiteSpace(vendorizedDir))
            {
                completion = $"{detail}\n\nVendorized assets folder:\n{vendorizedDir}";
                Debug.Log($"[ASM-Lite] {detail} Vendorized generated assets to '{vendorizedDir}'.");
            }
            else
            {
                completion = detail;
                Debug.Log($"[ASM-Lite] {detail}");
            }

            EditorUtility.DisplayDialog(modeLabel + " Complete", completion, "OK");
            Repaint();
        }

        private void ReturnToPackageManaged()
        {
            if (_selectedAvatar == null)
                return;

            var existing = GetOrRefreshComponent();
            if (existing != null)
            {
                if (!existing.useVendorizedGeneratedAssets)
                {
                    EditorUtility.DisplayDialog(
                        "Already Package Managed",
                        "This avatar already has an ASM-Lite component attached and editable.",
                        "OK");
                    return;
                }

                Undo.RecordObject(existing, "Disable ASM-Lite Vendorized Assets");
                existing.useVendorizedGeneratedAssets = false;
                existing.vendorizedGeneratedAssetsPath = string.Empty;
                EditorUtility.SetDirty(existing);

                if (!ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(existing.gameObject, existing, "Return To Package Managed"))
                {
                    EditorUtility.DisplayDialog(
                        "Return to Package Managed",
                        "Failed to restore package-managed FullController wiring on the attached ASM-Lite component.",
                        "OK");
                    return;
                }

                _discoveredParamCount = -1;
                _cachedCustomParamCount = -1;
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                EditorUtility.DisplayDialog(
                    "Package Managed Restored",
                    "ASM-Lite remains attached and editable. This avatar now uses package-managed generated payload references again.",
                    "OK");
                Repaint();
                return;
            }

            ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_selectedAvatar);

            _cachedComponent = null;
            _lastRefreshFrame = -1;
            _discoveredParamCount = -1;
            _cachedCustomParamCount = -1;

            AddPrefabToAvatar();

            EditorUtility.DisplayDialog(
                "Package Managed Restored",
                "ASM-Lite has been re-attached in package-managed mode for this avatar.\n\nYou can now edit settings and rebuild normally.",
                "OK");
        }

        private void CopyPendingCustomizationToComponent(ASMLiteComponent component)
        {
            if (component == null)
                return;

            component.slotCount = _pendingSlotCount;
            component.iconMode = _pendingIconMode;
            component.selectedGearIndex = _pendingSelectedGearIndex;
            component.actionIconMode = _pendingActionIconMode;
            component.customSaveIcon = _pendingCustomSaveIcon;
            component.customLoadIcon = _pendingCustomLoadIcon;
            component.customClearIcon = _pendingCustomClearIcon;
            component.customIcons = CloneTextures(_pendingCustomIcons);

            component.useCustomRootIcon = _pendingUseCustomRootIcon;
            component.customRootIcon = _pendingCustomRootIcon;
            component.useCustomRootName = _pendingUseCustomRootName;
            component.customRootName = NormalizeOptionalString(_pendingCustomRootName);
            component.useCustomInstallPath = _pendingUseCustomInstallPath;
            component.customInstallPath = NormalizeOptionalString(_pendingCustomInstallPath);
            component.useParameterExclusions = _pendingUseParameterExclusions;
            component.excludedParameterNames = SanitizeExcludedParameterNames(_pendingExcludedParameterNames);
        }

        private void CopyComponentCustomizationToPending(ASMLiteComponent component)
        {
            _pendingSlotCount = component.slotCount;
            _pendingIconMode = component.iconMode;
            _pendingSelectedGearIndex = component.selectedGearIndex;
            _pendingActionIconMode = component.actionIconMode;
            _pendingCustomSaveIcon = component.customSaveIcon;
            _pendingCustomLoadIcon = component.customLoadIcon;
            _pendingCustomClearIcon = component.customClearIcon;
            _pendingCustomIcons = CloneTextures(component.customIcons);

            _pendingUseCustomRootIcon = component.useCustomRootIcon;
            _pendingCustomRootIcon = component.customRootIcon;
            _pendingUseCustomRootName = component.useCustomRootName;
            _pendingCustomRootName = NormalizeOptionalString(component.customRootName);
            _pendingUseCustomInstallPath = component.useCustomInstallPath;
            _pendingCustomInstallPath = NormalizeOptionalString(component.customInstallPath);
            _pendingUseParameterExclusions = component.useParameterExclusions;
            _pendingExcludedParameterNames = SanitizeExcludedParameterNames(component.excludedParameterNames);
        }

        private static bool TryResolveInstallPrefixFromMovedRootPath(string rootControlName, string movedDestinationPath, out string installPrefix)
        {
            installPrefix = string.Empty;

            string root = NormalizeOptionalString(rootControlName);
            string destination = NormalizeSlashPath(movedDestinationPath);
            if (string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(destination))
                return false;

            if (string.Equals(destination, root, StringComparison.Ordinal))
            {
                installPrefix = string.Empty;
                return true;
            }

            string suffix = "/" + root;
            if (destination.EndsWith(suffix, StringComparison.Ordinal))
            {
                installPrefix = destination.Substring(0, destination.Length - suffix.Length);
                return true;
            }

            // Fallback: treat destination as direct prefix if it does not include
            // the root segment explicitly.
            installPrefix = destination;
            return true;
        }

        private static bool TryAdoptInstallPathFromMoveMenu(
            ASMLiteComponent component,
            VRCAvatarDescriptor avatar,
            out string adoptedInstallPrefix,
            out int removedMoveComponents)
        {
            adoptedInstallPrefix = string.Empty;
            removedMoveComponents = 0;

            if (component == null || avatar == null)
                return false;

            string effectiveRootName = ASMLiteBuilder.ResolveEffectiveRootControlName(component);
            if (string.IsNullOrWhiteSpace(effectiveRootName))
                return false;

            var remaps = GetVrcFuryMoveMenuPathRemaps(avatar);
            if (remaps.Count == 0)
                return false;

            string normalizedRoot = NormalizeSlashPath(effectiveRootName);
            string matchedDestination = null;
            foreach (var kv in remaps)
            {
                string fromPath = NormalizeSlashPath(kv.Key);
                if (!string.Equals(fromPath, normalizedRoot, StringComparison.Ordinal))
                    continue;

                matchedDestination = kv.Value;
                break;
            }

            if (string.IsNullOrWhiteSpace(matchedDestination))
                return false;

            if (!TryResolveInstallPrefixFromMovedRootPath(effectiveRootName, matchedDestination, out string resolvedPrefix))
                return false;

            resolvedPrefix = NormalizeOptionalString(resolvedPrefix);

            bool changedComponent = !component.useCustomInstallPath
                || !string.Equals(NormalizeOptionalString(component.customInstallPath), resolvedPrefix, StringComparison.Ordinal);

            if (changedComponent)
            {
                Undo.RecordObject(component, "Adopt ASM-Lite Install Path From Move Menu");
                component.useCustomInstallPath = true;
                component.customInstallPath = resolvedPrefix;
                EditorUtility.SetDirty(component);
            }

            var behaviours = avatar.GetComponentsInChildren<MonoBehaviour>(includeInactive: true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                var type = behaviour.GetType();
                if (type == null || !string.Equals(type.FullName, "VF.Model.VRCFury", StringComparison.Ordinal))
                    continue;

                // Preserve ASM-Lite managed install-path routing helper.
                if (string.Equals(behaviour.gameObject.name, "ASM-Lite Install Path Routing", StringComparison.Ordinal))
                    continue;

                var so = new SerializedObject(behaviour);
                so.Update();

                var content = so.FindProperty("content");
                if (content == null || content.propertyType != SerializedPropertyType.ManagedReference)
                    continue;

                string managedRefType = content.managedReferenceFullTypename;
                if (string.IsNullOrWhiteSpace(managedRefType)
                    || managedRefType.IndexOf("MoveMenuItem", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                var fromProp = so.FindProperty("content.fromPath");
                if (fromProp == null || fromProp.propertyType != SerializedPropertyType.String)
                    continue;

                string fromPath = NormalizeSlashPath(fromProp.stringValue);
                if (!string.Equals(fromPath, normalizedRoot, StringComparison.Ordinal))
                    continue;

                Undo.DestroyObjectImmediate(behaviour);
                removedMoveComponents++;
            }

            adoptedInstallPrefix = resolvedPrefix;
            return changedComponent || removedMoveComponents > 0;
        }

        private void AddPrefabToAvatar()
        {
            if (_selectedAvatar == null)
                return;

            var existing = _selectedAvatar.GetComponentInChildren<ASMLiteComponent>(includeInactive: true);
            if (existing != null)
            {
                bool replace = EditorUtility.DisplayDialog(
                    "ASM-Lite Already Present",
                    "An ASM-Lite component is already on this avatar.\n\n" +
                    "Do you want to add another instance?",
                    "Add Anyway", "Cancel");
                if (!replace)
                    return;
            }

            ASMLitePrefabCreator.CreatePrefab();

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);

            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog(
                    "ASM-Lite: Error",
                    $"Could not load prefab at {ASMLiteAssetPaths.Prefab}.\nCheck the Console for details.",
                    "OK");
                return;
            }

            Undo.SetCurrentGroupName("Add ASM-Lite Prefab");
            int group = Undo.GetCurrentGroup();

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                prefabAsset, _selectedAvatar.transform);

            var component = instance.GetComponent<ASMLiteComponent>();
            if (component != null)
            {
                CopyPendingCustomizationToComponent(component);
                if (!ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(instance, component, "Add Prefab"))
                {
                    Debug.LogError("[ASM-Lite] Failed to refresh live FullController wiring on newly added prefab instance.");
                    Undo.DestroyObjectImmediate(instance);
                    return;
                }
            }

            Undo.RegisterCreatedObjectUndo(instance, "Add ASM-Lite Prefab");
            Undo.CollapseUndoOperations(group);

            _cachedComponent  = null;
            _lastRefreshFrame = -1;   // force cache refresh on next draw
            Selection.activeGameObject = instance;
            EditorGUIUtility.PingObject(instance);

            Debug.Log($"[ASM-Lite] Prefab added to '{_selectedAvatar.gameObject.name}' with {_pendingSlotCount} slot(s). Baking assets...");

            // Immediately bake so assets are populated before the user hits Play.
            // Invalidate and re-fetch through the cache so the component reference
            // is consistent with subsequent GetOrRefreshComponent() calls.
            _cachedComponent  = null;
            _lastRefreshFrame = -1;
            component = GetOrRefreshComponent();
            if (component != null)
                BakeAssets(component);

            Repaint();
        }

        private void BakeAssets(ASMLiteComponent component)
        {
            if (component == null)
                return;

            // Check for stale prms entry from pre-1.0.5 prefab instances. If present,
            // destroy the old instance and re-add a fresh prefab so the double-path
            // that produces 2 extra synced parameters is removed before baking.
            if (ASMLitePrefabCreator.HasStalePrmsEntry(component.gameObject))
            {
                Debug.Log("[ASM-Lite] Stale prms entry detected on prefab instance (pre-1.0.5). Replacing with current prefab to remove the double-registration path.");

                // Capture settings before destroying the instance.
                int savedSlotCount = component.slotCount;
                IconMode savedIconMode = component.iconMode;
                int savedGearIndex = component.selectedGearIndex;
                ActionIconMode savedActionIconMode = component.actionIconMode;
                Texture2D savedCustomSave = component.customSaveIcon;
                Texture2D savedCustomLoad = component.customLoadIcon;
                Texture2D savedCustomClear = component.customClearIcon;
                Texture2D[] savedCustomIcons = CloneTextures(component.customIcons);
                bool savedUseCustomRootIcon = component.useCustomRootIcon;
                Texture2D savedCustomRootIcon = component.customRootIcon;
                bool savedUseCustomRootName = component.useCustomRootName;
                string savedCustomRootName = NormalizeOptionalString(component.customRootName);
                bool savedUseCustomInstallPath = component.useCustomInstallPath;
                string savedCustomInstallPath = NormalizeOptionalString(component.customInstallPath);
                bool savedUseParameterExclusions = component.useParameterExclusions;
                string[] savedExcludedParameterNames = SanitizeExcludedParameterNames(component.excludedParameterNames);
                bool savedUseVendorizedGeneratedAssets = component.useVendorizedGeneratedAssets;
                string savedVendorizedGeneratedAssetsPath = NormalizeOptionalString(component.vendorizedGeneratedAssetsPath);
                Transform savedParent = component.gameObject.transform.parent;

                Undo.SetCurrentGroupName("Rebuild ASM-Lite (migration)");
                int group = Undo.GetCurrentGroup();

                Undo.DestroyObjectImmediate(component.gameObject);

                ASMLitePrefabCreator.CreatePrefab();
                var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ASMLiteAssetPaths.Prefab);
                if (prefabAsset == null)
                {
                    Debug.LogError("[ASM-Lite] Could not load refreshed prefab after migration. Aborting rebuild.");
                    return;
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, savedParent);
                var newComponent = instance.GetComponent<ASMLiteComponent>();
                if (newComponent != null)
                {
                    newComponent.slotCount = savedSlotCount;
                    newComponent.iconMode = savedIconMode;
                    newComponent.selectedGearIndex = savedGearIndex;
                    newComponent.actionIconMode = savedActionIconMode;
                    newComponent.customSaveIcon = savedCustomSave;
                    newComponent.customLoadIcon = savedCustomLoad;
                    newComponent.customClearIcon = savedCustomClear;
                    newComponent.customIcons = savedCustomIcons;
                    newComponent.useCustomRootIcon = savedUseCustomRootIcon;
                    newComponent.customRootIcon = savedCustomRootIcon;
                    newComponent.useCustomRootName = savedUseCustomRootName;
                    newComponent.customRootName = savedCustomRootName;
                    newComponent.useCustomInstallPath = savedUseCustomInstallPath;
                    newComponent.customInstallPath = savedCustomInstallPath;
                    newComponent.useParameterExclusions = savedUseParameterExclusions;
                    newComponent.excludedParameterNames = savedExcludedParameterNames;
                    newComponent.useVendorizedGeneratedAssets = savedUseVendorizedGeneratedAssets;
                    newComponent.vendorizedGeneratedAssetsPath = savedVendorizedGeneratedAssetsPath;

                    if (!ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring(instance, newComponent, "Bake Migration"))
                    {
                        Debug.LogError("[ASM-Lite] Migration rebuild failed to refresh live FullController wiring. Aborting rebuild.");
                        return;
                    }
                }

                Undo.RegisterCreatedObjectUndo(instance, "Rebuild ASM-Lite (migration)");
                Undo.CollapseUndoOperations(group);

                _cachedComponent  = null;
                _lastRefreshFrame = -1;
                component = GetOrRefreshComponent();

                if (component == null)
                {
                    Debug.LogError("[ASM-Lite] Could not find component after migration. Aborting rebuild.");
                    return;
                }

                Debug.Log("[ASM-Lite] Migration complete. Continuing with bake.");
            }

            try
            {
                // Rebuild-prep contract for the reverted VF delivery path:
                // 1) collapse duplicate stale VF.Model.VRCFury components, preserving one;
                // 2) strip only direct-injection-era descriptor remnants (ASMLite_ namespace)
                //    so rebuild input reflects generated assets + VF wiring only.
                var migrationReport = ASMLiteBuilder.PrepareRevertedDeliveryRebuild(component);

                if (!TryRefreshLiveInstallPathPrefix(component, "Bake"))
                {
                    Debug.LogError("[ASM-Lite] Bake aborted before asset rebuild because live FullController menu prefix refresh failed.");
                    return;
                }

                int count = ASMLiteBuilder.Build(component);
                if (count >= 0)
                    _discoveredParamCount = count;

                if (component.useVendorizedGeneratedAssets)
                {
                    string preferredDir = NormalizeOptionalString(component.vendorizedGeneratedAssetsPath);
                    if (!TryVendorizeGeneratedAssetsToAvatarFolder(_selectedAvatar, out string syncedDir))
                    {
                        Debug.LogWarning("[ASM-Lite] Vendorized mode enabled but generated asset mirror sync failed. Keeping existing references.");
                    }
                    else
                    {
                        string effectiveDir = string.IsNullOrWhiteSpace(preferredDir) ? syncedDir : preferredDir;
                        if (!string.Equals(effectiveDir, syncedDir, StringComparison.Ordinal))
                            effectiveDir = syncedDir;

                        if (TryRetargetLiveFullControllerGeneratedAssets(component, effectiveDir))
                        {
                            component.vendorizedGeneratedAssetsPath = effectiveDir;
                            EditorUtility.SetDirty(component);
                            Debug.Log($"[ASM-Lite] Vendorized payload sync complete at '{effectiveDir}'.");
                        }
                        else
                        {
                            Debug.LogWarning("[ASM-Lite] Vendorized payload sync copied assets, but live FullController references were not retargeted.");
                        }
                    }
                }

                AssetDatabase.Refresh();
                Debug.Log($"[ASM-Lite] Assets baked for '{component.gameObject.name}' via generated assets + VRCFury FullController wiring. migrationRemoved={migrationReport.StaleVrcFuryRemoved}, cleanupFxLayers={migrationReport.Cleanup.FxLayersRemoved}, cleanupFxParams={migrationReport.Cleanup.FxParamsRemoved}, cleanupExprParams={migrationReport.Cleanup.ExprParamsRemoved}, cleanupMenuControls={migrationReport.Cleanup.MenuControlsRemoved}.");
            }
            catch (System.Exception ex)
            {
                EditorUtility.DisplayDialog(
                    "ASM-Lite: Build Error",
                    $"An error occurred while baking assets:\n\n{ex.Message}\n\nCheck the Console for details.",
                    "OK");
                Debug.LogException(ex);
            }
        }

        private void RemovePrefab(ASMLiteComponent component)
        {
            if (component == null || component.gameObject == null)
                return;

            // Remove-path cleanup strips only direct-injection-era descriptor remnants.
            ASMLiteBuilder.CleanupReport removeCleanupReport = default;
            if (_selectedAvatar != null)
                removeCleanupReport = ASMLiteBuilder.CleanUpAvatarAssetsWithReport(_selectedAvatar);

            Undo.SetCurrentGroupName("Remove ASM-Lite Prefab");
            int group = Undo.GetCurrentGroup();

            var prefabRoot = component.gameObject;
            Undo.DestroyObjectImmediate(prefabRoot);

            Undo.CollapseUndoOperations(group);

            _cachedComponent  = null;
            _lastRefreshFrame = -1;

            Debug.Log($"[ASM-Lite] Prefab removed from avatar. cleanupFxLayers={removeCleanupReport.FxLayersRemoved}, cleanupFxParams={removeCleanupReport.FxParamsRemoved}, cleanupExprParams={removeCleanupReport.ExprParamsRemoved}, cleanupMenuControls={removeCleanupReport.MenuControlsRemoved}.");
            Repaint();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        /// <summary>
        /// Reads the slot count and icon settings from the existing ASMLiteComponent on
        /// <see cref="_selectedAvatar"/> and stores them in the pending fields.
        /// Routes through the per-frame cache rather than calling GetComponentInChildren directly.
        /// </summary>
        private void SyncPendingSlotCountFromAvatar()
        {
            var existing = GetOrRefreshComponent();
            if (existing != null)
            {
                if (TryAdoptInstallPathFromMoveMenu(existing, _selectedAvatar, out string adoptedPrefix, out int removedMoveComponents))
                {
                    string readablePrefix = string.IsNullOrEmpty(adoptedPrefix) ? "<root>" : adoptedPrefix;
                    Debug.Log($"[ASM-Lite] Adopted install path from VRCFury Move Menu for '{existing.gameObject.name}': prefix='{readablePrefix}', removedMoveComponents={removedMoveComponents}.");
                }

                CopyComponentCustomizationToPending(existing);
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject == null)
                return;

            var descriptor = Selection.activeGameObject
                .GetComponentInParent<VRCAvatarDescriptor>(includeInactive: true)
                ?? Selection.activeGameObject.GetComponent<VRCAvatarDescriptor>();

            if (descriptor != null && descriptor != _selectedAvatar)
            {
                _selectedAvatar  = descriptor;
                _cachedComponent = null;
                _lastRefreshFrame = -1;
                _cachedCustomParamCount = -1;
                _discoveredParamCount = -1;
                SyncPendingSlotCountFromAvatar();

                Repaint();
            }
        }

        // ── Nested types ──────────────────────────────────────────────────────

        /// <summary>Node in the install-path menu tree.</summary>
        private class MenuTreeNode
        {
            public string Name;
            public string FullPath;
            public readonly List<MenuTreeNode> Children = new List<MenuTreeNode>();
        }

        /// <summary>
        /// Node in the parameter-backup tree. Leaf nodes (IsParam == true) represent
        /// individual parameters with a checkbox. Interior nodes are menu folders.
        /// </summary>
        private class ParamTreeNode
        {
            public string Name;
            public string MenuPath;   // non-null / set for folder nodes
            public string ParamName;  // non-null for leaf param nodes
            public bool IsParam => ParamName != null;
            public readonly List<ParamTreeNode> Children = new List<ParamTreeNode>();
        }
    }

    internal class ASMLiteVccSwitcherWindow : EditorWindow
    {
        private sealed class ProjectRow
        {
            public string ProjectName;
            public string ProjectRoot;
            public string Status;
            public bool Selected;
        }

        private List<ASMLiteWindow.VccLocalPackage> _packages;
        private int _selectedPkg = -1;
        private List<ProjectRow> _rows;
        private Vector2 _pkgScroll;
        private Vector2 _projScroll;
        private bool _initialized;

        internal static void Open()
        {
            var win = GetWindow<ASMLiteVccSwitcherWindow>(false, "VCC Embedded or Local Package Switcher");
            win.minSize = new Vector2(580, 440);
            win.Scan();
            win.Show();
        }

        private void Scan()
        {
            _packages = ASMLiteWindow.DiscoverVccLocalPackages();
            _selectedPkg = _packages.Count == 1 ? 0 : -1;
            BuildRows();
            _initialized = true;
            Repaint();
        }

        private void BuildRows()
        {
            if (_selectedPkg < 0 || _packages == null || _selectedPkg >= _packages.Count)
            {
                _rows = new List<ProjectRow>();
                return;
            }

            var pkg = _packages[_selectedPkg];
            string currentRoot = Directory.GetParent(Application.dataPath)?.FullName;
            var roots = ASMLiteWindow.FindVccProjectRoots().ToList();
            if (!string.IsNullOrEmpty(currentRoot)
                && !roots.Any(r => string.Equals(r, currentRoot, StringComparison.OrdinalIgnoreCase)))
                roots.Insert(0, currentRoot);

            _rows = roots.Select(root => new ProjectRow
            {
                ProjectName = Path.GetFileName(root)
                    + (string.Equals(root, currentRoot, StringComparison.OrdinalIgnoreCase) ? " (current)" : string.Empty),
                ProjectRoot = root,
                Status      = ASMLiteWindow.GetProjectPackageStatus(root, pkg.PackageName),
                Selected    = false,
            }).ToList();
        }

        private void OnGUI()
        {
            if (!_initialized) { EditorGUILayout.LabelField("Scanning..."); return; }

            // ── Local packages ────────────────────────────────────────────────
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Local packages (VCC user package folders)", EditorStyles.boldLabel);

            _pkgScroll = EditorGUILayout.BeginScrollView(_pkgScroll, GUILayout.MaxHeight(140));
            if (_packages == null || _packages.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No local packages found. Add package folders in VCC → Settings → Packages.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < _packages.Count; i++)
                {
                    var pkg = _packages[i];
                    EditorGUILayout.BeginHorizontal();
                    bool newSel = EditorGUILayout.Toggle(i == _selectedPkg, GUILayout.Width(18));
                    EditorGUILayout.BeginVertical();
                    EditorGUILayout.LabelField($"{pkg.DisplayName}  v{pkg.Version}", EditorStyles.boldLabel);
                    EditorGUILayout.LabelField($"{pkg.PackageName}  ·  {pkg.LocalPath}", EditorStyles.miniLabel);
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.EndHorizontal();
                    if (newSel && i != _selectedPkg) { _selectedPkg = i; BuildRows(); }
                }
            }
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(6);

            // ── Projects ──────────────────────────────────────────────────────
            bool pkgPicked = _selectedPkg >= 0 && _packages != null && _selectedPkg < _packages.Count;
            using (new EditorGUI.DisabledScope(!pkgPicked))
            {
                EditorGUILayout.LabelField("VCC projects", EditorStyles.boldLabel);
                _projScroll = EditorGUILayout.BeginScrollView(_projScroll, GUILayout.ExpandHeight(true));
                if (_rows != null)
                    foreach (var row in _rows)
                    {
                        EditorGUILayout.BeginHorizontal();
                        row.Selected = EditorGUILayout.Toggle(row.Selected, GUILayout.Width(18));
                        EditorGUILayout.LabelField(row.ProjectName, GUILayout.ExpandWidth(true));
                        EditorGUILayout.LabelField(row.Status, GUILayout.Width(110));
                        EditorGUILayout.EndHorizontal();
                    }
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space(2);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("All") && _rows != null)  foreach (var r in _rows) r.Selected = true;
                if (GUILayout.Button("None") && _rows != null) foreach (var r in _rows) r.Selected = false;
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Scan")) Scan();
            bool hasSelection = pkgPicked && _rows != null && _rows.Any(r => r.Selected);
            using (new EditorGUI.DisabledScope(!hasSelection))
            {
                if (GUILayout.Button("Set to Local"))    Apply(toLocal: true);
                if (GUILayout.Button("Set to Embedded")) Apply(toLocal: false);
            }
            if (GUILayout.Button("Close")) Close();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(4);
        }

        private void Apply(bool toLocal)
        {
            if (_packages == null || _rows == null || _selectedPkg < 0) return;
            var pkg = _packages[_selectedPkg];
            foreach (var row in _rows.Where(r => r.Selected).ToList())
            {
                if (toLocal)
                    ASMLiteWindow.ApplySwitchToFileLocal(row.ProjectRoot, pkg.LocalPath, pkg.PackageName);
                else
                    ASMLiteWindow.ApplySwitchToEmbedded(row.ProjectRoot, pkg.PackageName);
            }
            BuildRows();
            Repaint();
        }
    }
}
