using UnityEngine;
using VRC.SDKBase;
#if UNITY_EDITOR
using System.Reflection;
using System.Text;
#endif

namespace ASMLite
{
    /// <summary>
    /// Controls which icon set is displayed in the ASM-Lite radial menu.
    /// SameColor : all gear icons tinted to match the avatar's active colour.
    /// MultiColor: each gear slot uses its own distinct colour icon.
    /// Custom    : user-supplied Texture2D icons from the customIcons array.
    /// </summary>
    public enum IconMode
    {
        MultiColor = 0,
        SameColor  = 1,
        Custom     = 2,
    }

    /// <summary>
    /// Controls which icons are used for the Save, Load, and Clear Preset action buttons
    /// inside each slot submenu.
    /// Default: use the bundled Save.png, Load.png, Reset.png icons.
    /// Custom : user-supplied Texture2D icons for each action type.
    /// </summary>
    public enum ActionIconMode
    {
        Default = 0,
        Custom  = 1,
    }

    /// <summary>
    /// ASM-Lite component. Add this to an avatar root (or any child GameObject) to
    /// enable the ASM-Lite slot system. Implements IEditorOnly so the VRChat SDK
    /// strips the component from the build, and IPreprocessCallbackBehaviour so the
    /// VRChat SDK invokes the builder during the avatar build pipeline.
    /// </summary>
    [AddComponentMenu("ASM-Lite/ASM-Lite Component")]
    [DisallowMultipleComponent]
    public class ASMLiteComponent : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        // ─── Public fields ────────────────────────────────────────────────────

        /// <summary>
        /// Number of avatar expression parameter slots that ASM-Lite manages.
        /// Informational only in S01; used by the builder in S02+.
        /// </summary>
        [SerializeField]
        public int slotCount = 3;

        /// <summary>
        /// Which icon set mode to use for the radial menu gear icons.
        /// </summary>
        [SerializeField]
        public IconMode iconMode = IconMode.MultiColor;

        /// <summary>
        /// Index of the gear icon (0-7) used when iconMode is SameColor or MultiColor.
        /// Corresponds to the colour order in ASMLiteAssetPaths.GearIconPaths.
        /// </summary>
        [SerializeField]
        public int selectedGearIndex = 0;

        /// <summary>
        /// Enables per-slot custom icon overrides for expression menu slot entries.
        /// When disabled, slot entries use iconMode fallback only.
        /// </summary>
        [SerializeField]
        public bool useCustomSlotIcons = false;

        /// <summary>
        /// User-supplied icon textures for per-slot overrides.
        /// Each non-null entry overrides iconMode for that slot only.
        /// </summary>
        [SerializeField]
        public Texture2D[] customIcons = new Texture2D[0];

        /// <summary>
        /// Which icon set to use for the Save, Load, and Clear Preset action buttons.
        /// Default uses the bundled icons; Custom allows user-supplied textures.
        /// </summary>
        [SerializeField]
        public ActionIconMode actionIconMode = ActionIconMode.Default;

        /// <summary>
        /// User-supplied icon for the Save action button. Used when actionIconMode is Custom.
        /// </summary>
        [SerializeField]
        public Texture2D customSaveIcon;

        /// <summary>
        /// User-supplied icon for the Load action button. Used when actionIconMode is Custom.
        /// </summary>
        [SerializeField]
        public Texture2D customLoadIcon;

        /// <summary>
        /// User-supplied icon for the Clear Preset action button. Used when actionIconMode is Custom.
        /// </summary>
        [SerializeField]
        public Texture2D customClearIcon;

        /// <summary>
        /// Legacy dedicated root-icon toggle retained for backward compatibility.
        /// Root icon customization now applies whenever custom icons are enabled.
        /// </summary>
        [SerializeField]
        public bool useCustomRootIcon = false;

        /// <summary>
        /// Optional custom root menu icon used when custom icons are enabled.
        /// </summary>
        [SerializeField]
        public Texture2D customRootIcon;

        /// <summary>
        /// Enables custom root menu name behavior for the generated ASM-Lite root control.
        /// </summary>
        [SerializeField]
        public bool useCustomRootName = false;

        /// <summary>
        /// Optional custom root menu name used when <see cref="useCustomRootName"/> is enabled.
        /// </summary>
        [SerializeField]
        public string customRootName = "";

        /// <summary>
        /// Optional legacy custom slot label format used when <see cref="useCustomRootName"/> is enabled.
        /// Supports {slot} token replacement (1-based). Example: "Outfit {slot}".
        /// Kept for backward compatibility with existing serialized data.
        /// </summary>
        [SerializeField]
        public string customPresetNameFormat = "";

        /// <summary>
        /// Optional per-slot custom preset labels used when <see cref="useCustomRootName"/> is enabled.
        /// Slot index maps to array index + 1.
        /// </summary>
        [SerializeField]
        public string[] customPresetNames = new string[0];

        /// <summary>
        /// Optional custom Save label used when <see cref="useCustomRootName"/> is enabled.
        /// </summary>
        [SerializeField]
        public string customSaveLabel = "";

        /// <summary>
        /// Optional custom Load label used when <see cref="useCustomRootName"/> is enabled.
        /// </summary>
        [SerializeField]
        public string customLoadLabel = "";

        /// <summary>
        /// Optional custom Clear Preset label used when <see cref="useCustomRootName"/> is enabled.
        /// </summary>
        [SerializeField]
        public string customClearPresetLabel = "";

        /// <summary>
        /// Optional custom Confirm label used when <see cref="useCustomRootName"/> is enabled.
        /// </summary>
        [SerializeField]
        public string customConfirmLabel = "";

        /// <summary>
        /// Enables custom install path behavior for generated ASM-Lite menu/assets.
        /// </summary>
        [SerializeField]
        public bool useCustomInstallPath = false;

        /// <summary>
        /// Optional custom install path used when <see cref="useCustomInstallPath"/> is enabled.
        /// </summary>
        [SerializeField]
        public string customInstallPath = "";

        /// <summary>
        /// Enables parameter exclusion filtering when building ASM-Lite backup/default parameter lists.
        /// </summary>
        [SerializeField]
        public bool useParameterExclusions = false;

        /// <summary>
        /// Parameter names excluded from ASM-Lite backup/default parameter generation.
        /// </summary>
        [SerializeField]
        public string[] excludedParameterNames = new string[0];

        /// <summary>
        /// When true, ASM-Lite keeps a vendorized mirror of generated payload assets
        /// under the project Assets folder and rewires the live FullController payload
        /// to those mirrored assets after each bake.
        /// </summary>
        [SerializeField]
        public bool useVendorizedGeneratedAssets = false;

        /// <summary>
        /// Project-relative folder (e.g. Assets/ASM-Lite/&lt;Avatar&gt;/GeneratedAssets)
        /// where vendorized generated assets are mirrored when
        /// <see cref="useVendorizedGeneratedAssets"/> is enabled.
        /// </summary>
        [SerializeField]
        public string vendorizedGeneratedAssetsPath = "";

#if UNITY_EDITOR
        // ─── Reflection cache ─────────────────────────────────────────────────

        /// <summary>
        /// Cached reference to ASMLiteBuilder.Build to avoid repeated reflection
        /// lookups on every preprocess call.
        /// </summary>
        private static MethodInfo s_buildMethod;
        private static MethodInfo s_getLatestBuildDiagnosticMethod;

        private static System.Type GetBuilderType()
        {
            return System.Type.GetType("ASMLite.Editor.ASMLiteBuilder, ASMLite.Editor");
        }

        /// <summary>
        /// Resolves and caches a reference to the ASMLiteBuilder.Build static method.
        /// Returns null if the editor assembly is not loaded or the method is not found.
        /// </summary>
        private static MethodInfo GetBuildMethod()
        {
            if (s_buildMethod != null)
                return s_buildMethod;

            var type = GetBuilderType();
            if (type == null)
                return null;

            s_buildMethod = type.GetMethod(
                "Build",
                BindingFlags.Public | BindingFlags.Static);

            return s_buildMethod;
        }

        private static MethodInfo GetLatestBuildDiagnosticMethod()
        {
            if (s_getLatestBuildDiagnosticMethod != null)
                return s_getLatestBuildDiagnosticMethod;

            var type = GetBuilderType();
            if (type == null)
                return null;

            s_getLatestBuildDiagnosticMethod = type.GetMethod(
                "GetLatestBuildDiagnosticResult",
                BindingFlags.NonPublic | BindingFlags.Static);

            return s_getLatestBuildDiagnosticMethod;
        }

        private static bool ReadBoolProperty(object instance, string name, bool fallback)
        {
            if (instance == null)
                return fallback;

            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (property == null)
                return fallback;

            if (!(property.GetValue(instance) is bool value))
                return fallback;

            return value;
        }

        private static string ReadStringProperty(object instance, string name)
        {
            if (instance == null)
                return string.Empty;

            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (property == null)
                return string.Empty;

            return property.GetValue(instance) as string ?? string.Empty;
        }

        private static object ReadObjectProperty(object instance, string name)
        {
            if (instance == null)
                return null;

            var property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return property?.GetValue(instance);
        }

        private static void AppendDiagnosticLog(StringBuilder builder, object diagnosticObject, bool includeBuildDiagnosticPrefix)
        {
            if (builder == null || diagnosticObject == null)
                return;

            if (ReadBoolProperty(diagnosticObject, "Success", fallback: true))
                return;

            string code = ReadStringProperty(diagnosticObject, "Code");
            string message = ReadStringProperty(diagnosticObject, "Message");
            string contextPath = ReadStringProperty(diagnosticObject, "ContextPath");
            string remediation = ReadStringProperty(diagnosticObject, "Remediation");

            if (includeBuildDiagnosticPrefix)
            {
                builder.Append("[ASM-Lite] Build diagnostic ");
                builder.Append(code);
                builder.Append(": ");
                builder.Append(message);
            }
            else
            {
                builder.Append(code);
                builder.Append(": ");
                builder.Append(message);
            }

            if (!string.IsNullOrWhiteSpace(contextPath))
                builder.Append($" Context: '{contextPath}'.");
            if (!string.IsNullOrWhiteSpace(remediation))
                builder.Append($" Remediation: {remediation}");

            var innerDiagnostic = ReadObjectProperty(diagnosticObject, "InnerDiagnostic");
            if (innerDiagnostic != null && !ReadBoolProperty(innerDiagnostic, "Success", fallback: true))
            {
                string innerCode = ReadStringProperty(innerDiagnostic, "Code");
                string innerContextPath = ReadStringProperty(innerDiagnostic, "ContextPath");
                string innerRemediation = ReadStringProperty(innerDiagnostic, "Remediation");

                builder.Append(" Inner: ");
                builder.Append(innerCode);
                if (!string.IsNullOrWhiteSpace(innerContextPath))
                    builder.Append($" ({innerContextPath})");
                builder.Append('.');
                if (!string.IsNullOrWhiteSpace(innerRemediation))
                    builder.Append($" Inner Remediation: {innerRemediation}");
            }
        }

        private static void LogBuildFailureDiagnosticFromBuilder()
        {
            var diagnosticMethod = GetLatestBuildDiagnosticMethod();
            if (diagnosticMethod == null)
                return;

            object diagnosticObject;
            try
            {
                diagnosticObject = diagnosticMethod.Invoke(null, null);
            }
            catch
            {
                return;
            }

            if (diagnosticObject == null)
                return;

            if (ReadBoolProperty(diagnosticObject, "Success", fallback: true))
                return;

            var diagnosticLog = new StringBuilder();
            AppendDiagnosticLog(diagnosticLog, diagnosticObject, includeBuildDiagnosticPrefix: true);
            Debug.LogError(diagnosticLog.ToString());
        }
#endif

        // ─── IPreprocessCallbackBehaviour ─────────────────────────────────────

        /// <summary>
        /// PreprocessOrder controls ordering relative to other IPreprocessCallbackBehaviour
        /// implementors on the avatar. The entire IPreprocessCallbackBehaviour phase runs
        /// via PreprocessCallbackBehaviours (callbackOrder=-2048), which is after VRCFury's
        /// main build (VrcfAvatarPreprocessor, callbackOrder=int.MinValue). Lower values
        /// run earlier within the IPreprocessCallbackBehaviour phase.
        ///
        /// ASM-Lite uses this callback to regenerate its stub assets so the prefab's
        /// VRCFury FullController payload can consume those assets as the delivery path.
        /// </summary>
        public int PreprocessOrder => -10;

        /// <summary>
        /// Called by the VRChat SDK before avatar upload. Delegates to ASMLiteBuilder
        /// to regenerate generated assets used by ASM-Lite's VRCFury FullController
        /// delivery wiring.
        ///
        /// Note: deterministic VRCFury Toggle global-name enrollment runs earlier in the
        /// build-request callback phase (ASMLiteToggleBuildRequestedCallback) and restores
        /// source scene values via delayed broker cleanup after the request path completes.
        /// </summary>
        public bool OnPreprocess()
        {
#if UNITY_EDITOR
            // Call ASMLiteBuilder.Build via cached reflection to avoid a hard
            // compile-time dependency from the runtime assembly onto the editor assembly.
            var buildMethod = GetBuildMethod();
            if (buildMethod == null)
            {
                Debug.LogError("[ASM-Lite] ASMLiteBuilder.Build method not found. Is the Editor assembly loaded?");
                return false;
            }

            try
            {
                object result = buildMethod.Invoke(null, new object[] { this });
                int buildCount = result is int intResult ? intResult : -1;
                if (buildCount < 0)
                {
                    LogBuildFailureDiagnosticFromBuilder();
                    return false;
                }
            }
            catch (TargetInvocationException ex)
            {
                // Emit a single linked console entry so stack trace and message
                // appear together and double-click-to-source works correctly.
                Debug.LogException(ex.InnerException ?? ex);
                return false;
            }
#endif
            return true;
        }
    }
}
