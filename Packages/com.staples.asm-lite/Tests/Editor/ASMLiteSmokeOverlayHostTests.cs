using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeOverlayHostTests
    {
        [Test]
        public void CommandLine_ParsesRequiredSessionAndCatalogPaths()
        {
            string sessionRoot = Path.Combine(Path.GetTempPath(), "asmlite-smoke-host-session", "..", "asmlite-smoke-host-session-normalized");
            string catalogPath = Path.Combine(Path.GetTempPath(), "asmlite-smoke-host-catalog", "..", "asmlite-smoke-host-catalog", "catalog.json");

            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeSessionRoot", sessionRoot,
                "-asmliteSmokeCatalogPath", catalogPath,
            };

            ASMLiteSmokeOverlayHostConfiguration configuration = ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args);

            Assert.AreEqual(Path.GetFullPath(sessionRoot), configuration.SessionRootPath);
            Assert.AreEqual(Path.GetFullPath(catalogPath), configuration.CatalogPath);
        }

        [Test]
        public void CommandLine_DefaultsToClickMeSceneAndOct25DressAvatar()
        {
            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeSessionRoot", Path.Combine(Path.GetTempPath(), "asmlite-smoke-defaults-session"),
                "-asmliteSmokeCatalogPath", Path.Combine(Path.GetTempPath(), "asmlite-smoke-defaults-session", "catalog.json"),
            };

            ASMLiteSmokeOverlayHostConfiguration configuration = ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args);

            Assert.AreEqual("Assets/Click ME.unity", configuration.ScenePath);
            Assert.AreEqual("Oct25_Dress", configuration.AvatarName);
            Assert.AreEqual(120, configuration.StartupTimeoutSeconds);
            Assert.AreEqual(5, configuration.HeartbeatSeconds);
            Assert.That(configuration.ExitOnReady, Is.False);
        }

        [Test]
        public void CommandLine_RejectsMissingSessionRoot()
        {
            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeCatalogPath", Path.Combine(Path.GetTempPath(), "asmlite-smoke-missing-session", "catalog.json"),
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args));

            StringAssert.Contains("-asmliteSmokeSessionRoot", exception.Message);
        }

        [Test]
        public void CommandLine_RejectsInvalidTimeouts()
        {
            string[] args =
            {
                "Unity",
                "-batchmode",
                "-asmliteSmokeSessionRoot", Path.Combine(Path.GetTempPath(), "asmlite-smoke-invalid-timeout"),
                "-asmliteSmokeCatalogPath", Path.Combine(Path.GetTempPath(), "asmlite-smoke-invalid-timeout", "catalog.json"),
                "-asmliteSmokeStartupTimeoutSeconds", "0",
            };

            var exception = Assert.Throws<InvalidOperationException>(() =>
                ASMLiteSmokeOverlayHostCommandLine.ParseConfiguration(args));

            StringAssert.Contains("-asmliteSmokeStartupTimeoutSeconds", exception.Message);
        }

        [Test]
        public void UnityRuntime_NormalizesPlayModeCloneAvatarNames()
        {
            Assert.AreEqual("Oct25_Dress", ASMLiteSmokeOverlayHostUnityRuntime.NormalizeUnityRuntimeName("Oct25_Dress (Clone)"));
            Assert.AreEqual("Oct25_Dress", ASMLiteSmokeOverlayHostUnityRuntime.NormalizeUnityRuntimeName(" Oct25_Dress (Clone) (Clone) "));
        }

        [Test]
        public void UnityRuntime_AssertsPackageResourcesAndCanonicalCatalogLoad()
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-package-resource-present",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out string packageDetail,
                    out string packageStackTrace),
                Is.True);
            StringAssert.Contains("ASM-Lite.prefab", packageDetail);
            Assert.That(packageStackTrace, Is.Empty);

            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-catalog-loads",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out string catalogDetail,
                    out string catalogStackTrace),
                Is.True);
            StringAssert.Contains("canonical smoke catalog", catalogDetail);
            Assert.That(catalogStackTrace, Is.Empty);
        }

        [Test]
        public void UnityRuntime_AssertsPackageResourceMissingWithStableDiagnosticCode()
        {
            Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-package-resource-present",
                    new ASMLiteSmokeStepArgs { objectName = "Packages/com.staples.asm-lite/Missing.prefab" },
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out string detail,
                    out string stackTrace),
                Is.False);
            StringAssert.Contains("SETUP_PACKAGE_RESOURCE_MISSING", detail);
            StringAssert.Contains("prefab source was not found", detail);
            Assert.That(stackTrace, Is.Empty);
        }

        [Test]
        public void UnityRuntime_AssertsExpectedPrimaryActionFromStepArgs()
        {
            var avatarObject = new GameObject("Phase06_AddPrefabAvatar");
            avatarObject.AddComponent<VRCAvatarDescriptor>();

            try
            {
                Selection.activeObject = avatarObject;
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));
                LogAssert.Expect(LogType.Error, new Regex("^No graphic device is available"));

                bool success = ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-primary-action",
                    new ASMLiteSmokeStepArgs { expectedPrimaryAction = "Add Prefab" },
                    string.Empty,
                    avatarObject.name,
                    out string detail,
                    out string stackTrace);

                Assert.That(success, Is.True, detail + "\n" + stackTrace);
                Assert.That(detail, Is.EqualTo("Primary action is Add Prefab."));
            }
            finally
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    string.Empty,
                    avatarObject.name,
                    out _,
                    out _);
                UnityEngine.Object.DestroyImmediate(avatarObject);
                Selection.activeObject = null;
            }
        }

        [Test]
        public void UnityRuntime_AssertsGeneratedReferencesRemainPackageManagedByDefault()
        {
            var avatarObject = new GameObject("Phase07B_PackageManagedAvatar");
            avatarObject.AddComponent<VRCAvatarDescriptor>();
            var componentObject = new GameObject("ASM-Lite Component");
            componentObject.transform.SetParent(avatarObject.transform);
            var component = componentObject.AddComponent<ASMLite.ASMLiteComponent>();
            component.useVendorizedGeneratedAssets = false;
            component.vendorizedGeneratedAssetsPath = string.Empty;

            try
            {
                bool success = ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-generated-references-package-managed",
                    new ASMLiteSmokeStepArgs(),
                    string.Empty,
                    avatarObject.name,
                    out string detail,
                    out string stackTrace);

                Assert.That(success, Is.True, detail + "\n" + stackTrace);
                Assert.That(detail, Is.EqualTo("Generated references are package-managed by default."));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_Phase1ActionsSetSlotAndInstallPathAndAssertNoComponent()
        {
            GameObject avatarWithoutComponent = null;
            GameObject avatarWithComponent = null;
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                avatarWithoutComponent = new GameObject("Phase1_NoComponentAvatar");
                avatarWithoutComponent.AddComponent<VRCAvatarDescriptor>();

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "assert-no-component",
                        new ASMLiteSmokeStepArgs(),
                        string.Empty,
                        avatarWithoutComponent.name,
                        out string noComponentDetail,
                        out string noComponentStackTrace),
                    Is.True);
                StringAssert.Contains("No ASM-Lite component", noComponentDetail);
                Assert.That(noComponentStackTrace, Is.Empty);

                avatarWithComponent = new GameObject("Phase1_ComponentAvatar");
                avatarWithComponent.AddComponent<VRCAvatarDescriptor>();
                var component = avatarWithComponent.AddComponent<ASMLite.ASMLiteComponent>();

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "set-slot-count",
                        new ASMLiteSmokeStepArgs { slotCount = 5 },
                        string.Empty,
                        avatarWithComponent.name,
                        out string slotDetail,
                        out string slotStackTrace),
                    Is.True);
                Assert.AreEqual(5, component.slotCount);
                StringAssert.Contains("slotCount", slotDetail);
                Assert.That(slotStackTrace, Is.Empty);

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "set-install-path-state",
                        new ASMLiteSmokeStepArgs { installPathPresetId = "nested" },
                        string.Empty,
                        avatarWithComponent.name,
                        out string pathDetail,
                        out string pathStackTrace),
                    Is.True);
                Assert.That(component.useCustomInstallPath, Is.True);
                Assert.AreEqual("Avatars/ASM-Lite", component.customInstallPath);
                StringAssert.Contains("installPathPresetId 'nested'", pathDetail);
                Assert.That(pathStackTrace, Is.Empty);
            }
            finally
            {
                if (avatarWithoutComponent != null)
                    UnityEngine.Object.DestroyImmediate(avatarWithoutComponent);
                if (avatarWithComponent != null)
                    UnityEngine.Object.DestroyImmediate(avatarWithComponent);
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void UnityRuntime_AttachedCustomizationSnapshotReportsFieldSpecificDiffs()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("Phase1_SnapshotAvatar");
                avatarObject.AddComponent<VRCAvatarDescriptor>();
                var component = avatarObject.AddComponent<ASMLite.ASMLiteComponent>();
                component.slotCount = 3;
                component.useCustomInstallPath = true;
                component.customInstallPath = "ASM-Lite";

                bool success = ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-attached-customization-snapshot",
                    new ASMLiteSmokeStepArgs
                    {
                        slotCount = 4,
                        expectedInstallPathEnabled = true,
                        expectedNormalizedEffectivePath = "Wardrobe/ASM-Lite",
                        expectedComponentPresent = true,
                        expectedPrimaryAction = "Rebuild",
                    },
                    string.Empty,
                    avatarObject.name,
                    out string detail,
                    out string stackTrace);

                Assert.That(success, Is.False);
                StringAssert.Contains("slotCount expected <4> but was <3>", detail);
                StringAssert.Contains("normalizedEffectivePath expected <Wardrobe/ASM-Lite> but was <ASM-Lite>", detail);
                Assert.That(stackTrace, Is.Empty);
            }
            finally
            {
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_PendingCustomizationSnapshotMatchesWindowAutomationState()
        {
            GameObject avatarObject = null;
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                avatarObject = new GameObject("Phase1_PendingSnapshotAvatar");
                avatarObject.AddComponent<VRCAvatarDescriptor>();
                Selection.activeObject = avatarObject;

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "open-window",
                        new ASMLiteSmokeStepArgs(),
                        string.Empty,
                        avatarObject.name,
                        out _,
                        out _),
                    Is.True);
                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "select-avatar",
                        new ASMLiteSmokeStepArgs(),
                        string.Empty,
                        avatarObject.name,
                        out _,
                        out _),
                    Is.True);
                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "set-slot-count",
                        new ASMLiteSmokeStepArgs { slotCount = 4 },
                        string.Empty,
                        avatarObject.name,
                        out _,
                        out _),
                    Is.True);
                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "set-install-path-state",
                        new ASMLiteSmokeStepArgs { installPathPresetId = "root" },
                        string.Empty,
                        avatarObject.name,
                        out _,
                        out _),
                    Is.True);

                bool success = ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "assert-pending-customization-snapshot",
                    new ASMLiteSmokeStepArgs
                    {
                        slotCount = 4,
                        installPathPresetId = "root",
                        expectedInstallPathEnabled = true,
                        expectedNormalizedEffectivePath = string.Empty,
                        expectedComponentPresent = false,
                        expectedPrimaryAction = "Add Prefab",
                    },
                    string.Empty,
                    avatarObject.name,
                    out string detail,
                    out string stackTrace);

                Assert.That(success, Is.True, detail + "\n" + stackTrace);
                StringAssert.Contains("Pending customization snapshot matched", detail);
                Assert.That(stackTrace, Is.Empty);
            }
            finally
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    string.Empty,
                    avatarObject == null ? string.Empty : avatarObject.name,
                    out _,
                    out _);
                Selection.activeObject = null;
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void UnityRuntime_ClosesOpensAndFocusesAutomationWindow()
        {
            bool previousIgnoreFailingMessages = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out _,
                    out _);

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "open-window",
                        new ASMLiteSmokeStepArgs(),
                        "Assets/Click ME.unity",
                        "Oct25_Dress",
                        out string openDetail,
                        out string openStackTrace),
                    Is.True);
                StringAssert.Contains("opened", openDetail);
                Assert.That(openStackTrace, Is.Empty);

                Assert.That(ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                        "assert-window-focused",
                        new ASMLiteSmokeStepArgs(),
                        "Assets/Click ME.unity",
                        "Oct25_Dress",
                        out string focusDetail,
                        out string focusStackTrace),
                    Is.True);
                StringAssert.Contains("automation", focusDetail);
                Assert.That(focusStackTrace, Is.Empty);
            }
            finally
            {
                ASMLiteSmokeOverlayHostUnityRuntime.Instance.ExecuteCatalogStep(
                    "close-window",
                    new ASMLiteSmokeStepArgs(),
                    "Assets/Click ME.unity",
                    "Oct25_Dress",
                    out _,
                    out _);
                LogAssert.ignoreFailingMessages = previousIgnoreFailingMessages;
            }
        }

        [Test]
        public void UnityRuntime_FindsSceneAvatarWithRuntimeComponent_WhenDuplicateNameExists()
        {
            GameObject avatarWithoutComponentObject = null;
            GameObject avatarWithComponentObject = null;
            try
            {
                avatarWithoutComponentObject = new GameObject("Oct25_Dress");
                avatarWithoutComponentObject.AddComponent<VRCAvatarDescriptor>();

                avatarWithComponentObject = new GameObject("Oct25_Dress (Clone)");
                VRCAvatarDescriptor avatarWithComponent = avatarWithComponentObject.AddComponent<VRCAvatarDescriptor>();
                var componentChild = new GameObject("ASM-Lite Component");
                componentChild.transform.SetParent(avatarWithComponentObject.transform);
                componentChild.AddComponent<ASMLite.ASMLiteComponent>();

                VRCAvatarDescriptor resolved = ASMLiteSmokeOverlayHostUnityRuntime.Instance.FindAvatarByName("Oct25_Dress");

                Assert.That(resolved, Is.SameAs(avatarWithComponent));
            }
            finally
            {
                if (avatarWithoutComponentObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarWithoutComponentObject);
                if (avatarWithComponentObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarWithComponentObject);
            }
        }

        [Test]
        public void UnityRuntime_FindAvatarByName_PrefersMatchingSelectedAvatarOverComponentBiasedDuplicateLookup()
        {
            GameObject selectedAvatarObject = null;
            GameObject duplicateWithComponentObject = null;
            try
            {
                selectedAvatarObject = new GameObject("Oct25_Dress");
                VRCAvatarDescriptor selectedAvatar = selectedAvatarObject.AddComponent<VRCAvatarDescriptor>();

                duplicateWithComponentObject = new GameObject("Oct25_Dress (Clone)");
                duplicateWithComponentObject.AddComponent<VRCAvatarDescriptor>();
                var componentChild = new GameObject("ASM-Lite Component");
                componentChild.transform.SetParent(duplicateWithComponentObject.transform);
                componentChild.AddComponent<ASMLite.ASMLiteComponent>();

                Selection.activeObject = selectedAvatarObject;

                VRCAvatarDescriptor resolved = ASMLiteSmokeOverlayHostUnityRuntime.Instance.FindAvatarByName("Oct25_Dress");

                Assert.That(resolved, Is.SameAs(selectedAvatar));
            }
            finally
            {
                Selection.activeObject = null;
                if (selectedAvatarObject != null)
                    UnityEngine.Object.DestroyImmediate(selectedAvatarObject);
                if (duplicateWithComponentObject != null)
                    UnityEngine.Object.DestroyImmediate(duplicateWithComponentObject);
            }
        }

        [Test]
        public void UnityRuntime_AcceptsEditorOnlyComponentStrippedDuringPlaymode()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("Oct25_Dress");
                VRCAvatarDescriptor avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();

                bool valid = ASMLiteSmokeOverlayHostUnityRuntime.ValidateRuntimeComponentState(
                    "Oct25_Dress",
                    avatar,
                    isPlaying: true,
                    out string detail);

                Assert.That(valid, Is.True);
                StringAssert.Contains("stripped for playmode", detail);
            }
            finally
            {
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_RejectsMissingComponentOutsidePlaymode()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("Oct25_Dress");
                VRCAvatarDescriptor avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();

                bool valid = ASMLiteSmokeOverlayHostUnityRuntime.ValidateRuntimeComponentState(
                    "Oct25_Dress",
                    avatar,
                    isPlaying: false,
                    out string detail);

                Assert.That(valid, Is.False);
                StringAssert.Contains("component was not found", detail);
            }
            finally
            {
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_UsesSelectedSceneAvatarBeforeNameLookup()
        {
            GameObject canonical = null;
            GameObject alternate = null;
            try
            {
                canonical = new GameObject("FixtureAvatar");
                canonical.AddComponent<VRCAvatarDescriptor>();
                alternate = new GameObject("AlternateAvatar");
                VRCAvatarDescriptor alternateDescriptor = alternate.AddComponent<VRCAvatarDescriptor>();
                Selection.activeObject = alternate;

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.True, detail);
                Assert.That(avatar, Is.SameAs(alternateDescriptor));
                StringAssert.Contains("selected avatar 'AlternateAvatar'", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (canonical != null)
                    UnityEngine.Object.DestroyImmediate(canonical);
                if (alternate != null)
                    UnityEngine.Object.DestroyImmediate(alternate);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_FindsSingleActiveAvatarByFixtureNameWhenNothingSelected()
        {
            GameObject avatarObject = null;
            try
            {
                Selection.activeObject = null;
                avatarObject = new GameObject("FixtureAvatar");
                VRCAvatarDescriptor descriptor = avatarObject.AddComponent<VRCAvatarDescriptor>();

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.True, detail);
                Assert.That(avatar, Is.SameAs(descriptor));
                StringAssert.Contains("found by fixture name", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_ReportsWrongSelectedObjectDiagnostic()
        {
            GameObject wrongObject = null;
            try
            {
                wrongObject = new GameObject("FixtureAvatar");
                Selection.activeObject = wrongObject;

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_SELECTED_OBJECT_NOT_AVATAR", detail);
                StringAssert.Contains("selected object is not a valid avatar", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (wrongObject != null)
                    UnityEngine.Object.DestroyImmediate(wrongObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_ReportsAmbiguousDuplicateNamesWithoutSelectedDisambiguator()
        {
            GameObject first = null;
            GameObject second = null;
            try
            {
                Selection.activeObject = null;
                first = new GameObject("FixtureAvatar");
                first.AddComponent<VRCAvatarDescriptor>();
                second = new GameObject("FixtureAvatar");
                second.AddComponent<VRCAvatarDescriptor>();

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_AMBIGUOUS", detail);
                StringAssert.Contains("Multiple avatar descriptors", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (first != null)
                    UnityEngine.Object.DestroyImmediate(first);
                if (second != null)
                    UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_AllowsSelectedInactiveAvatarButDoesNotFindUnselectedInactiveAvatar()
        {
            GameObject avatarObject = null;
            try
            {
                avatarObject = new GameObject("FixtureAvatar");
                VRCAvatarDescriptor descriptor = avatarObject.AddComponent<VRCAvatarDescriptor>();
                avatarObject.SetActive(false);
                Selection.activeObject = null;

                bool unselectedResolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor unselectedAvatar,
                    out string unselectedDetail);

                Assert.That(unselectedResolved, Is.False);
                Assert.That(unselectedAvatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_NOT_FOUND", unselectedDetail);

                Selection.activeObject = avatarObject;
                bool selectedResolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor selectedAvatar,
                    out string selectedDetail);

                Assert.That(selectedResolved, Is.True, selectedDetail);
                Assert.That(selectedAvatar, Is.SameAs(descriptor));
            }
            finally
            {
                Selection.activeObject = null;
                if (avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(avatarObject);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_IgnoresSameNameNonAvatarObjectsDuringNameLookup()
        {
            GameObject nonAvatar = null;
            try
            {
                Selection.activeObject = null;
                nonAvatar = new GameObject("FixtureAvatar");

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_NOT_FOUND", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (nonAvatar != null)
                    UnityEngine.Object.DestroyImmediate(nonAvatar);
            }
        }

        [Test]
        public void UnityRuntime_AvatarSelection_ReportsPrefabAssetSelectionDiagnostic()
        {
            const string folderPath = "Assets/ASMLiteAvatarSelectionTests_Temp";
            const string prefabPath = folderPath + "/FixturePrefabAvatar.prefab";
            GameObject source = null;
            try
            {
                if (!AssetDatabase.IsValidFolder(folderPath))
                    AssetDatabase.CreateFolder("Assets", "ASMLiteAvatarSelectionTests_Temp");
                source = new GameObject("FixtureAvatar");
                source.AddComponent<VRCAvatarDescriptor>();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
                UnityEngine.Object.DestroyImmediate(source);
                source = null;
                Selection.activeObject = prefab;

                bool resolved = ASMLiteSmokeOverlayHostUnityRuntime.TryResolveAvatarForSelection(
                    "FixtureAvatar",
                    out VRCAvatarDescriptor avatar,
                    out string detail);

                Assert.That(resolved, Is.False);
                Assert.That(avatar, Is.Null);
                StringAssert.Contains("SETUP_AVATAR_PREFAB_ASSET", detail);
                StringAssert.Contains("prefab asset", detail);
            }
            finally
            {
                Selection.activeObject = null;
                if (source != null)
                    UnityEngine.Object.DestroyImmediate(source);
                AssetDatabase.DeleteAsset(prefabPath);
                if (AssetDatabase.IsValidFolder(folderPath))
                    AssetDatabase.DeleteAsset(folderPath);
            }
        }

        [Test]
        public void Runner_RemainsRegisteredAfterReady_WhenExitOnReadyIsFalse()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                Assert.That(context.Runner.IsRunningForTesting, Is.True);
                Assert.That(context.Runtime.RegisterUpdateCount, Is.EqualTo(1));
                Assert.That(context.Runtime.UnregisterUpdateCount, Is.EqualTo(0));
                Assert.That(context.Runtime.OpenedScenes, Is.Empty);
                Assert.That(context.Runtime.SelectedAvatars, Is.Empty);
                Assert.That(context.Runtime.CloseAutomationWindowIfOpenCount, Is.EqualTo(1));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, hostState.state);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .ToArray();
                Assert.That(events.Select(item => item.eventType), Is.EquivalentTo(new[] { "session-started", "unity-ready" }));
                Assert.That(events.Select(item => item.eventSeq).ToArray(), Is.EqualTo(new[] { 1, 2 }));
                StringAssert.DoesNotContain("loaded Assets/Click ME.unity", events[1].message);
                StringAssert.Contains("ready for suite commands", events[1].message);
            }
        }

        [Test]
        public void SetupSuite_OpensSceneSelectsAvatarAndAddsPrefabOnlyWhenRunAsSuiteStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                Assert.That(context.Runtime.OpenedScenes, Is.Empty);
                Assert.That(context.Runtime.SelectedAvatars, Is.Empty);

                WriteCommand(context.Paths, BuildRunSuiteCommand(1, "cmd_000001_run-suite", "setup-scene-avatar"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.OpenedScenes, Is.EqualTo(new[] { "Assets/Click ME.unity" }));
                Assert.That(context.Runtime.SelectedAvatars, Is.EqualTo(new[] { "Oct25_Dress" }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "open-scene", "open-window", "select-avatar", "add-prefab", "assert-primary-action" }));
            }
        }

        [Test]
        public void SetupSuite_AppliesFixtureMutationAndStepTargetOverridesBeforeAction()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildRunSuiteCommand(11, "cmd_000011_run-suite", "fixture-mutation-host"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[] { ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection }));
                Assert.That(context.Runtime.OpenedScenes, Is.EqualTo(new[] { "Assets/FixtureOverride.unity" }));
                Assert.That(context.Runtime.LastMutationObjectName, Is.EqualTo("Wrong Target"));
            }
        }

        [Test]
        public void SetupGeneratedAssetRecoverySignalsSuite_AppliesFixtureMutationsAndRecoveryAssertions()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000013_run-suite";

                WriteCommand(context.Paths, BuildRunSuiteCommand(13, commandId, "generated-asset-recovery-signals"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent,
                    ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent,
                    ASMLiteSmokeSetupFixtureMutationIds.MissingGeneratedFolder,
                    ASMLiteSmokeSetupFixtureMutationIds.StaleGeneratedFolder,
                    ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent,
                }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[]
                {
                    "add-prefab",
                    "add-prefab",
                    "assert-primary-action",
                    "assert-primary-action",
                    "assert-primary-action",
                    "assert-primary-action",
                }));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void SetupGeneratedReferenceOwnershipSuite_AppliesPackageManagedAssertion()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000013b_run-suite";

                WriteCommand(context.Paths, BuildRunSuiteCommand(13, commandId, "generated-reference-ownership"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent,
                }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[]
                {
                    "add-prefab",
                    "assert-generated-references-package-managed",
                }));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void SetupPhase1SlotMatrixSuite_InvokesPrebuildSlotActions()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000017_run-suite";

                WriteCommand(context.Paths, BuildRunSuiteCommand(17, commandId, "prebuild-slots-matrix"));
                AdvanceUntilIdleAfterRun(context, maxTicks: 128);

                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "prelude-recover-context", StringComparison.Ordinal)), Is.EqualTo(8));
                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "set-slot-count", StringComparison.Ordinal)), Is.EqualTo(8));
                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "assert-pending-customization-snapshot", StringComparison.Ordinal)), Is.EqualTo(8));
                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "assert-attached-customization-snapshot", StringComparison.Ordinal)), Is.EqualTo(8));
                CollectionAssert.Contains(context.Runtime.ExecutedActions, "assert-no-component");

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void SetupPhase1PathMatrixSuite_InvokesPrebuildPathActions()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000018_run-suite";

                WriteCommand(context.Paths, BuildRunSuiteCommand(18, commandId, "prebuild-path-matrix"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "prelude-recover-context", StringComparison.Ordinal)), Is.EqualTo(4));
                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "set-install-path-state", StringComparison.Ordinal)), Is.EqualTo(4));
                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "assert-pending-customization-snapshot", StringComparison.Ordinal)), Is.EqualTo(4));
                Assert.That(context.Runtime.ExecutedActions.Count(action => string.Equals(action, "assert-attached-customization-snapshot", StringComparison.Ordinal)), Is.EqualTo(4));
                CollectionAssert.Contains(context.Runtime.ExecutedActions, "assert-no-component");

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void SetupDestructiveRecoverySuite_ResetsAfterEveryDestructiveCase()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000014_run-suite";

                WriteCommand(context.Paths, BuildRunSuiteCommand(14, commandId, "destructive-recovery-reset"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.ControlledCorruptGeneratedAsset,
                    ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline,
                    ASMLiteSmokeSetupFixtureMutationIds.MissingGeneratedFolder,
                    ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent,
                    ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline,
                }));
                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(5));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Count(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && item.message.Contains("Clean reset passed")), Is.EqualTo(5));
            }
        }

        [Test]
        public void SetupDestructiveRecoverySuite_StopsWhenCleanResetFails()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000015_run-suite";
                context.Runtime.FailNextSetupFixtureReset("simulated cleanup ledger failure");

                WriteCommand(context.Paths, BuildRunSuiteCommand(15, commandId, "destructive-recovery-reset"));
                AdvanceUntilReviewRequired(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.ControlledCorruptGeneratedAsset,
                }));
                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(1));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)
                    && item.message.Contains("SETUP_DESTRUCTIVE_RESET_FAILED")
                    && item.message.Contains("simulated cleanup ledger failure")), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "review-required", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void SetupSuite_TreatsExpectedDiagnosticStepFailureAsPassed()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000012_run-suite";
                context.Runtime.FailAction("open-scene", "SETUP_SCENE_MISSING: configured scene could not be found at Assets/Missing.unity");

                WriteCommand(context.Paths, BuildRunSuiteCommand(12, commandId, "expected-diagnostic-host"));
                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && item.message.Contains("Expected diagnostic 'SETUP_SCENE_MISSING' matched")), Is.True);
            }
        }

        [Test]
        public void SetupSuite_ResetsFixtureStateAfterExpectedDiagnosticCaseBeforeContinuing()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000016_run-suite";
                context.Runtime.FailAction(
                    "select-avatar",
                    "SETUP_AVATAR_NOT_FOUND: avatar could not be found for fixture name 'Oct25_Dress'.");

                WriteCommand(context.Paths, BuildRunSuiteCommand(16, commandId, "expected-diagnostic-fixture-reset-host"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.UnselectedInactiveAvatar,
                }));
                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(1));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && item.message.Contains("Expected diagnostic 'SETUP_AVATAR_NOT_FOUND' matched")), Is.True);
            }
        }

        [Test]
        public void SetupSuite_DoesNotResetTwiceAfterRequiredCleanResetWithinCase()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000017_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(17, commandId, "clean-reset-then-finish-host"));
                AdvanceUntilIdleAfterRun(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection,
                }));
                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(1),
                    "Required clean reset should clear fixture-mutation state before the case ends.");

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-passed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Count(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && item.message.Contains("Clean reset passed")), Is.EqualTo(1));
                Assert.That(events.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && item.message.Contains("Fixture state reset after case completion.")), Is.False,
                    "No extra case-end fixture reset should run after a required clean reset already restored the baseline.");
            }
        }

        [Test]
        public void SetupSuite_DelaysCaseFixtureResetUntilAfterSettleForFinalMutationStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000018_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(18, commandId, "settle-then-reset-host"));

                context.Runtime.AdvanceSeconds(0.1d);
                var earlyEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(0),
                    "Fixture reset should wait until the post-add-prefab settle window completes.");
                Assert.That(earlyEvents.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && string.Equals(item.stepId, "add-prefab-final-mutation-settle", StringComparison.Ordinal)), Is.False,
                    "Final mutation step should not report passed until deferred cleanup succeeds.");

                AdvanceUntilIdleAfterRun(context);

                var completedEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(1),
                    "Fixture reset should run once the settle window completes.");
                Assert.That(completedEvents.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && string.Equals(item.stepId, "add-prefab-final-mutation-settle", StringComparison.Ordinal)), Is.True);
                Assert.That(completedEvents.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)
                    && string.Equals(item.stepId, "add-prefab-final-mutation-settle", StringComparison.Ordinal)), Is.False);
            }
        }

        [Test]
        public void SetupSuite_FailingDeferredResetDoesNotEmitContradictoryStepPass()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000019_run-suite";
                context.Runtime.FailNextSetupFixtureReset("simulated cleanup ledger failure");

                WriteCommand(context.Paths, BuildRunSuiteCommand(19, commandId, "settle-then-reset-host"));
                AdvanceUntilReviewRequired(context);

                Assert.That(context.Runtime.AppliedFixtureMutations, Is.EqualTo(new[]
                {
                    ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent,
                }));
                Assert.That(context.Runtime.ResetSetupFixtureCount, Is.EqualTo(1));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-passed", StringComparison.Ordinal)
                    && string.Equals(item.stepId, "add-prefab-final-mutation-settle", StringComparison.Ordinal)), Is.False,
                    "Deferred cleanup failure should not leave a contradictory step-passed event behind.");
                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)
                    && string.Equals(item.stepId, "add-prefab-final-mutation-settle", StringComparison.Ordinal)
                    && item.message.Contains("simulated cleanup ledger failure")), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "review-required", StringComparison.Ordinal)), Is.True);
            }
        }

        [Test]
        public void Runner_DoesNotRegisterAfterReady_WhenExitOnReadyIsTrue()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: true))
            {
                Assert.That(context.Runner.IsRunningForTesting, Is.False);
                Assert.That(context.Runtime.RegisterUpdateCount, Is.EqualTo(0));
                Assert.That(context.Runtime.ExitCodes, Is.EquivalentTo(new[] { 0 }));
            }
        }

        [Test]
        public void Heartbeat_RewritesHostStateWithoutChangingReadyState()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                var before = ReadHostState(context.Paths.HostStatePath);
                context.Runtime.AdvanceSeconds(6);
                var after = ReadHostState(context.Paths.HostStatePath);

                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReady, after.state);
                Assert.AreEqual(before.lastEventSeq, after.lastEventSeq);
                Assert.AreEqual(before.lastCommandSeq, after.lastCommandSeq);
                Assert.AreNotEqual(before.heartbeatUtc, after.heartbeatUtc);
            }
        }

        [Test]
        public void CommandPolling_ProcessesCommandFilesInSequenceOrder()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildRunSuiteCommand(2, "cmd_000002_run-suite"));
                WriteCommand(context.Paths, BuildReviewDecisionCommand(1, "cmd_000001_review-decision"));

                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Length, Is.EqualTo(1));
                Assert.AreEqual("cmd_000001_review-decision", events[0].commandId);
            }
        }

        [Test]
        public void CommandPolling_DoesNotReprocessCommandIdsRecoveredFromEvents()
        {
            using (var context = RunnerTestContext.CreateWithoutStart(exitOnReady: false))
            {
                ASMLiteSmokeProtocol.AppendEventLine(context.Paths.EventsLogPath, new ASMLiteSmokeProtocolEvent
                {
                    protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                    sessionId = "seed-session",
                    eventId = "evt_000001_seed",
                    eventSeq = 1,
                    eventType = "session-started",
                    timestampUtc = "2026-04-23T00:00:00Z",
                    commandId = "cmd_000010_run-suite",
                    runId = string.Empty,
                    groupId = string.Empty,
                    suiteId = string.Empty,
                    caseId = string.Empty,
                    stepId = string.Empty,
                    effectiveResetPolicy = string.Empty,
                    hostState = ASMLiteSmokeProtocol.HostStateIdle,
                    message = "seed",
                    reviewDecisionOptions = Array.Empty<string>(),
                    supportedCapabilities = new[] { "suiteCatalogV1" },
                });

                context.StartRunner();
                WriteCommand(context.Paths, BuildRunSuiteCommand(10, "cmd_000010_run-suite"));
                AdvanceUntilIdleAfterRun(context);

                var rejectedEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal))
                    .ToArray();

                Assert.That(rejectedEvents.Any(item => item.commandId == "cmd_000010_run-suite"), Is.False);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_ExecutesLifecycleRoundtripInOrderWithoutRejecting()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000003_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(3, commandId));
                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Select(item => item.eventType), Is.EqualTo(new[]
                {
                    "suite-started",
                    "case-started",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "step-started",
                    "step-passed",
                    "suite-passed",
                    "session-idle",
                }));

                ASMLiteSmokeProtocolEvent sessionIdleEvent = events.Single(item => string.Equals(item.eventType, "session-idle", StringComparison.Ordinal));
                Assert.That(sessionIdleEvent.hostState, Is.EqualTo(ASMLiteSmokeProtocol.HostStateIdle));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateIdle, hostState.state);
                StringAssert.Contains("returned to suite selection", hostState.message);

                Assert.That(events.Select(item => item.eventSeq), Is.Ordered.Ascending);
                Assert.That(events.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[]
                    {
                        "rebuild",
                        "hygiene-cleanup-after-rebuild",
                        "vendorize",
                        "hygiene-cleanup-after-vendorize",
                        "detach",
                        "hygiene-cleanup-after-detach",
                        "return-to-package-managed",
                    }));
                Assert.That(
                    events.Any(item => !string.IsNullOrWhiteSpace(item.effectiveResetPolicy)),
                    Is.True,
                    "expected at least one lifecycle event to include effectiveResetPolicy");
                Assert.That(
                    events
                        .Where(item => !string.IsNullOrWhiteSpace(item.effectiveResetPolicy))
                        .All(item => string.Equals(item.effectiveResetPolicy, "FullPackageRebuild", StringComparison.Ordinal)),
                    Is.True);

                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[]
                {
                    "rebuild",
                    "lifecycle-hygiene-cleanup",
                    "vendorize",
                    "lifecycle-hygiene-cleanup",
                    "detach",
                    "lifecycle-hygiene-cleanup",
                    "return-to-package-managed",
                }));

                string resultPath = context.Paths.GetResultPath(1, "lifecycle-roundtrip");
                string eventsSlicePath = context.Paths.GetEventsSlicePath(1, "lifecycle-roundtrip");
                string nunitPath = context.Paths.GetNUnitPath(1, "lifecycle-roundtrip");
                string failurePath = context.Paths.GetFailurePath(1, "lifecycle-roundtrip");

                Assert.That(File.Exists(resultPath), Is.True);
                Assert.That(File.Exists(eventsSlicePath), Is.True);
                Assert.That(File.Exists(nunitPath), Is.True);
                Assert.That(File.Exists(failurePath), Is.False);

                var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFromJson(File.ReadAllText(resultPath));
                Assert.AreEqual("passed", resultDocument.result);
                Assert.AreEqual("FullPackageRebuild", resultDocument.effectiveResetPolicy);
                Assert.That(resultDocument.firstEventSeq, Is.GreaterThan(0));
                Assert.That(resultDocument.lastEventSeq, Is.GreaterThanOrEqualTo(resultDocument.firstEventSeq));
                Assert.That(resultDocument.artifactPaths.resultPath, Does.StartWith("runs/"));
                Assert.That(resultDocument.artifactPaths.eventsSlicePath, Does.StartWith("runs/"));
                Assert.That(resultDocument.artifactPaths.nunitPath, Does.StartWith("runs/"));
                Assert.That(resultDocument.artifactPaths.failurePath, Is.EqualTo(string.Empty));
            }
        }

        [Test]
        public void CommandPolling_RunSuite_ExecutesPlaymodeSuiteInSingleSession()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000008_run-suite";
                var command = BuildRunSuiteCommand(8, commandId);
                command.runSuite.suiteId = "playmode-runtime-validation";
                command.runSuite.requestedResetDefault = "SceneReload";
                command.runSuite.reason = "playmode-suite-test";

                WriteCommand(context.Paths, command);
                AdvanceUntilIdleAfterRun(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Last().eventType, Is.EqualTo("session-idle"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("enter-playmode"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("assert-runtime-component-valid"));
                Assert.That(context.Runtime.ExecutedActions, Does.Contain("exit-playmode"));
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_StreamsEventsAcrossTicksBeforeArtifactsExist()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000070_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(70, commandId));

                context.Runtime.AdvanceSeconds(1);
                var afterDispatchState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateRunning, afterDispatchState.state);
                Assert.AreEqual("run-0070-lifecycle-roundtrip", afterDispatchState.activeRunId);
                var afterDispatchEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(afterDispatchEvents, Is.Empty);

                context.Runtime.AdvanceSeconds(1);
                var streamedEvents = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(streamedEvents.Select(item => item.eventType), Is.EqualTo(new[] { "suite-started" }));
                Assert.That(File.Exists(context.Paths.GetResultPath(1, "lifecycle-roundtrip")), Is.False);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_StepSleepSecondsDelaysStepExecution()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000071_run-suite";
                var command = BuildRunSuiteCommand(71, commandId);
                command.runSuite.stepSleepSeconds = 1.5d;
                WriteCommand(context.Paths, command);

                context.Runtime.AdvanceSeconds(1);
                context.Runtime.AdvanceSeconds(1);
                context.Runtime.AdvanceSeconds(1);
                context.Runtime.AdvanceSeconds(1);
                Assert.That(context.Runtime.ExecutedActions, Is.Empty);

                context.Runtime.AdvanceSeconds(1);
                Assert.That(context.Runtime.ExecutedActions, Is.Empty);

                context.Runtime.AdvanceSeconds(0.5d);
                Assert.That(context.Runtime.ExecutedActions, Is.Not.Empty);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_WaitsBrieflyAfterLifecycleMutationBeforeNextStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string commandId = "cmd_000072_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(72, commandId));

                context.Runtime.AdvanceSeconds(1); // command accepted
                context.Runtime.AdvanceSeconds(1); // suite-started
                context.Runtime.AdvanceSeconds(1); // case-started
                context.Runtime.AdvanceSeconds(1); // rebuild step-started
                context.Runtime.AdvanceSeconds(1); // rebuild executes and passes
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild" }));

                context.Runtime.AdvanceSeconds(0.1d);
                var eventsBeforeSettle = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(eventsBeforeSettle.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild" }));

                context.Runtime.AdvanceSeconds(0.15d);
                context.Runtime.AdvanceSeconds(0d);
                var eventsAfterSettle = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(eventsAfterSettle.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild", "hygiene-cleanup-after-rebuild" }));
            }
        }

        [Test]
        public void CommandPolling_AbortRun_WritesAbortedArtifactsAndReviewRequiredState()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000080_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(80, runCommandId));
                context.Runtime.AdvanceSeconds(1);

                var runningState = ReadHostState(context.Paths.HostStatePath);
                const string abortCommandId = "cmd_000081_abort-run";
                WriteCommand(context.Paths, BuildAbortRunCommand(81, abortCommandId, runningState.activeRunId, "lifecycle-roundtrip"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, abortCommandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(events.Select(item => item.eventType), Is.EqualTo(new[]
                {
                    "abort-requested",
                    "run-aborted",
                    "review-required",
                }));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
                Assert.AreEqual(runningState.activeRunId, hostState.activeRunId);

                string resultPath = context.Paths.GetResultPath(1, "lifecycle-roundtrip");
                Assert.That(File.Exists(resultPath), Is.True);
                var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFromJson(File.ReadAllText(resultPath));
                Assert.AreEqual("aborted", resultDocument.result);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_TreatsConsoleErrorsAsStepFailures()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runtime.EmitConsoleErrorDuringAction("vendorize", "Injected Unity console error.");

                const string commandId = "cmd_000012_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(12, commandId));
                AdvanceUntilReviewRequired(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild", "lifecycle-hygiene-cleanup", "vendorize" }));

                string failurePath = context.Paths.GetFailurePath(1, "lifecycle-roundtrip");
                Assert.That(File.Exists(failurePath), Is.True);
                var failureDocument = ASMLiteSmokeArtifactPaths.LoadFailureFromJson(File.ReadAllText(failurePath));
                Assert.AreEqual("vendorize", failureDocument.stepId);
                Assert.That(failureDocument.failureMessage, Does.Contain("Unity console error"));
                Assert.That(failureDocument.failureMessage, Does.Contain("Injected Unity console error"));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
            }
        }

        [Test]
        public void CommandPolling_RunSuite_FailsFastAfterFirstFailedStep()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");

                const string commandId = "cmd_000011_run-suite";
                WriteCommand(context.Paths, BuildRunSuiteCommand(11, commandId));
                AdvanceUntilReviewRequired(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Where(item => string.Equals(item.commandId, commandId, StringComparison.Ordinal))
                    .ToArray();

                Assert.That(events.Any(item => string.Equals(item.eventType, "step-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Any(item => string.Equals(item.eventType, "suite-failed", StringComparison.Ordinal)), Is.True);
                Assert.That(events.Where(item => string.Equals(item.eventType, "step-started", StringComparison.Ordinal))
                    .Select(item => item.stepId), Is.EqualTo(new[] { "rebuild", "hygiene-cleanup-after-rebuild", "vendorize" }));
                Assert.That(context.Runtime.ExecutedActions, Is.EqualTo(new[] { "rebuild", "lifecycle-hygiene-cleanup", "vendorize" }));

                string resultPath = context.Paths.GetResultPath(1, "lifecycle-roundtrip");
                string failurePath = context.Paths.GetFailurePath(1, "lifecycle-roundtrip");
                string eventsSlicePath = context.Paths.GetEventsSlicePath(1, "lifecycle-roundtrip");
                Assert.That(File.Exists(resultPath), Is.True);
                Assert.That(File.Exists(failurePath), Is.True);
                Assert.That(File.Exists(eventsSlicePath), Is.True);

                var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFromJson(File.ReadAllText(resultPath));
                Assert.AreEqual("failed", resultDocument.result);
                Assert.AreEqual("FullPackageRebuild", resultDocument.effectiveResetPolicy);

                var failureDocument = ASMLiteSmokeArtifactPaths.LoadFailureFromJson(File.ReadAllText(failurePath));
                Assert.AreEqual("vendorize", failureDocument.stepId);
                Assert.That(failureDocument.failureMessage, Does.Contain("Injected vendorize failure"));
                Assert.AreEqual("FullPackageRebuild", failureDocument.effectiveResetPolicy);
                Assert.That(failureDocument.artifactPaths.failurePath, Does.StartWith("runs/"));
                Assert.That(failureDocument.eventSeqRange.first, Is.GreaterThan(0));
                Assert.That(failureDocument.eventSeqRange.last, Is.GreaterThanOrEqualTo(failureDocument.eventSeqRange.first));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
                StringAssert.Contains("waiting for operator review", hostState.message);

                ASMLiteSmokeProtocolEvent reviewRequiredEvent = events.Single(item => string.Equals(item.eventType, "review-required", StringComparison.Ordinal));
                Assert.That(reviewRequiredEvent.reviewDecisionOptions, Is.EqualTo(new[] { "return-to-suite-list", "rerun-suite", "exit" }));
            }
        }

        [Test]
        public void CommandPolling_RunSuiteIsRejectedWhileReviewDecisionIsPending()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(20, "cmd_000020_run-suite"));
                AdvanceUntilReviewRequired(context);

                int executedActionCount = context.Runtime.ExecutedActions.Count;
                WriteCommand(context.Paths, BuildRunSuiteCommand(21, "cmd_000021_run-suite"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, "cmd_000021_run-suite", StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.True);
                Assert.That(context.Runtime.ExecutedActions.Count, Is.EqualTo(executedActionCount));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_ReturnToSuiteList_TransitionsToIdleWithoutRejecting()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000030_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(30, runCommandId));
                AdvanceUntilReviewRequired(context);

                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000031_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        31,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "return-to-suite-list"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "session-idle", StringComparison.Ordinal)), Is.True);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateIdle, hostState.state);
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_RerunSuite_ReexecutesWithoutRestart()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000040_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(40, runCommandId));
                AdvanceUntilReviewRequired(context);

                int initialActionCount = context.Runtime.ExecutedActions.Count;
                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000041_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        41,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "rerun-suite"));
                AdvanceUntilReviewRequired(context);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);

                ASMLiteSmokeProtocolEvent[] rerunEvents = events
                    .Where(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal))
                    .ToArray();
                Assert.That(rerunEvents.Any(item => string.Equals(item.eventType, "suite-started", StringComparison.Ordinal)), Is.True);
                Assert.That(rerunEvents.Any(item => string.Equals(item.eventType, "review-required", StringComparison.Ordinal)), Is.True);
                Assert.That(context.Runtime.ExecutedActions.Count, Is.GreaterThan(initialActionCount));
                Assert.That(context.Runtime.ExitCodes, Is.Empty);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_Exit_ShutsDownSession()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000050_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(50, runCommandId));
                AdvanceUntilReviewRequired(context);

                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000051_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        51,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "exit"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.False);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "session-exiting", StringComparison.Ordinal)), Is.True);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateExiting, hostState.state);
                Assert.That(context.Runtime.ExitWithoutSavingCodes, Is.EquivalentTo(new[] { 0 }));
            }
        }

        [Test]
        public void CommandPolling_ReviewDecision_InvalidDecision_IsRejectedAndKeepsReviewRequiredState()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                const string runCommandId = "cmd_000060_run-suite";
                context.Runtime.FailAction("vendorize", "Injected vendorize failure.");
                WriteCommand(context.Paths, BuildRunSuiteCommand(60, runCommandId));
                AdvanceUntilReviewRequired(context);

                ASMLiteSmokeProtocolEvent reviewRequired = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                    .Single(item => string.Equals(item.commandId, runCommandId, StringComparison.Ordinal)
                        && string.Equals(item.eventType, "review-required", StringComparison.Ordinal));

                const string reviewCommandId = "cmd_000061_review-decision";
                WriteCommand(
                    context.Paths,
                    BuildReviewDecisionCommand(
                        61,
                        reviewCommandId,
                        reviewRequired.runId,
                        reviewRequired.suiteId,
                        "continue"));
                context.Runtime.AdvanceSeconds(1);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.commandId, reviewCommandId, StringComparison.Ordinal)
                    && string.Equals(item.eventType, "command-rejected", StringComparison.Ordinal)), Is.True);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateReviewRequired, hostState.state);
                Assert.That(context.Runtime.ExitCodes, Is.Empty);
            }
        }

        [TestCase("SceneReload", "Inherit", "SceneReload")]
        [TestCase("SceneReload", "FullPackageRebuild", "FullPackageRebuild")]
        [TestCase("FullPackageRebuild", "Inherit", "FullPackageRebuild")]
        [TestCase("FullPackageRebuild", "SceneReload", "SceneReload")]
        public void ResetService_ResolvesEffectiveResetPolicyDeterministically(string globalDefault, string suiteOverride, string expected)
        {
            string resolved = ASMLiteSmokeResetService.ResolveEffectivePolicy(globalDefault, suiteOverride);
            Assert.AreEqual(expected, resolved);
        }

        [Test]
        public void StallSignal_WritesStalledHostStateAndEvent()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runner.PublishStalledForTesting("Host heartbeat timeout.");

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateStalled, hostState.state);
                Assert.AreEqual("Host heartbeat timeout.", hostState.message);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.AreEqual("host-stalled", events.Last().eventType);
            }
        }

        [Test]
        public void CrashSignal_WritesCrashedHostStateAndEvent()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                context.Runner.PublishCrashedForTesting(new InvalidOperationException("boom"));

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateCrashed, hostState.state);
                StringAssert.Contains("InvalidOperationException", hostState.message);

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.AreEqual("host-crashed", events.Last().eventType);
            }
        }

        [Test]
        public void ShutdownSession_WritesExitingStateAndUnregistersRunner()
        {
            using (var context = RunnerTestContext.Create(exitOnReady: false))
            {
                WriteCommand(context.Paths, BuildShutdownCommand(4, "cmd_000004_shutdown-session"));
                context.Runtime.AdvanceSeconds(1);

                var hostState = ReadHostState(context.Paths.HostStatePath);
                Assert.AreEqual(ASMLiteSmokeProtocol.HostStateExiting, hostState.state);
                Assert.That(context.Runner.IsRunningForTesting, Is.False);
                Assert.That(context.Runtime.UnregisterUpdateCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(context.Runtime.ExitCodes, Is.EquivalentTo(new[] { 0 }));

                var events = ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath);
                Assert.That(events.Any(item => string.Equals(item.eventType, "session-exiting", StringComparison.Ordinal)), Is.True);
            }
        }

        private static ASMLiteSmokeHostStateDocument ReadHostState(string hostStatePath)
        {
            string raw = File.ReadAllText(hostStatePath);
            return ASMLiteSmokeProtocol.LoadHostStateFromJson(raw);
        }

        private static void WriteCommand(ASMLiteSmokeSessionPaths paths, ASMLiteSmokeProtocolCommand command)
        {
            string filePath = paths.GetCommandPath(command.commandSeq, command.commandType, command.commandId);
            string json = ASMLiteSmokeProtocol.ToJson(command, prettyPrint: true);
            File.WriteAllText(filePath, json);
        }

        private static void AdvanceUntilReviewRequired(RunnerTestContext context, int maxTicks = 64)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                context.Runtime.AdvanceSeconds(1);
                var hostState = ReadHostState(context.Paths.HostStatePath);
                if (string.Equals(hostState.state, ASMLiteSmokeProtocol.HostStateReviewRequired, StringComparison.Ordinal))
                    return;
            }

            Assert.Fail("Runner did not reach review-required within the expected tick budget.");
        }

        private static void AdvanceUntilIdleAfterRun(RunnerTestContext context, int maxTicks = 64)
        {
            ASMLiteSmokeHostStateDocument lastState = null;
            for (int i = 0; i < maxTicks; i++)
            {
                context.Runtime.AdvanceSeconds(1);
                lastState = ReadHostState(context.Paths.HostStatePath);
                if (string.Equals(lastState.state, ASMLiteSmokeProtocol.HostStateIdle, StringComparison.Ordinal)
                    && lastState.lastEventSeq > 2)
                    return;
            }

            string eventSummary = string.Join(", ", ASMLiteSmokeProtocol.LoadEventsFromNdjsonFileTolerant(context.Paths.EventsLogPath)
                .Select(item => $"{item.eventSeq}:{item.eventType}:{item.message}"));
            string stateSummary = lastState == null
                ? "<no host state>"
                : $"state={lastState.state}; lastEventSeq={lastState.lastEventSeq}; lastCommandSeq={lastState.lastCommandSeq}; message={lastState.message}";
            Assert.Fail($"Runner did not return to idle within the expected tick budget. {stateSummary}. Events: {eventSummary}");
        }

        private static ASMLiteSmokeProtocolCommand BuildRunSuiteCommand(int commandSeq, string commandId, string suiteId = "lifecycle-roundtrip")
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "run-suite",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = new ASMLiteSmokeRunSuitePayload
                {
                    suiteId = suiteId,
                    requestedBy = "operator",
                    requestedResetDefault = "SceneReload",
                    reason = "sequence-order-test",
                    stepSleepSeconds = 0d,
                },
                reviewDecision = null,
                abortRun = null,
            };
        }

        private static ASMLiteSmokeProtocolCommand BuildReviewDecisionCommand(
            int commandSeq,
            string commandId,
            string runId = "run-0001-lifecycle-roundtrip",
            string suiteId = "lifecycle-roundtrip",
            string decision = "continue",
            string notes = "sequence-order-test")
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "review-decision",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = null,
                reviewDecision = new ASMLiteSmokeReviewDecisionPayload
                {
                    runId = runId,
                    suiteId = suiteId,
                    decision = decision,
                    requestedBy = "operator",
                    notes = notes,
                },
                abortRun = null,
            };
        }

        private static ASMLiteSmokeProtocolCommand BuildAbortRunCommand(
            int commandSeq,
            string commandId,
            string runId,
            string suiteId,
            string reason = "operator-abort")
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "abort-run",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = null,
                reviewDecision = null,
                abortRun = new ASMLiteSmokeAbortRunPayload
                {
                    runId = runId,
                    suiteId = suiteId,
                    requestedBy = "operator",
                    reason = reason,
                },
            };
        }

        private static string BuildTestCatalogJson()
        {
            var catalog = new ASMLiteSmokeCatalogDocument
            {
                catalogVersion = 1,
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                fixture = new ASMLiteSmokeFixtureDefinition
                {
                    scenePath = "Assets/Click ME.unity",
                    avatarName = "Oct25_Dress",
                },
                groups = new[]
                {
                    new ASMLiteSmokeGroupDefinition
                    {
                        groupId = "phase08-host-tests",
                        label = "Phase 08 Host Tests",
                        description = "Local catalog used by ASMLiteSmokeOverlayHostTests.",
                        suites = new[]
                        {
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "setup-scene-avatar",
                                label = "Setup Scene / Avatar / Prefab",
                                description = "Loads the canonical scene, selects the target avatar, and adds the prefab scaffold on request.",
                                resetOverride = "Inherit",
                                speed = "quick",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "The scene is open, Oct25_Dress is selected, and the prefab scaffold is ready.",
                                debugHint = "Check scene/avatar fixture names, prefab prerequisites, and window focus if setup stalls.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "setup-scene-avatar",
                                        label = "Scene, avatar, and prefab setup",
                                        description = "Run setup and prefab scaffold steps only when explicitly requested.",
                                        expectedOutcome = "Scene/avatar/window/prefab setup completes.",
                                        debugHint = "Inspect host event stream for setup step boundaries.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition { stepId = "open-scene", label = "Scene is open", description = "Open canonical smoke scene.", actionType = "open-scene", expectedOutcome = "Scene opens.", debugHint = "Inspect scene path." },
                                            new ASMLiteSmokeStepDefinition { stepId = "open-window", label = "ASM-Lite window is open", description = "Open ASM-Lite window.", actionType = "open-window", expectedOutcome = "Window opens.", debugHint = "Inspect window state." },
                                            new ASMLiteSmokeStepDefinition { stepId = "select-avatar", label = "Avatar is selected", description = "Select Oct25_Dress.", actionType = "select-avatar", expectedOutcome = "Avatar selected.", debugHint = "Inspect avatar lookup." },
                                            new ASMLiteSmokeStepDefinition { stepId = "add-prefab", label = "ASM-Lite scaffold is added", description = "Add the ASM-Lite prefab scaffold.", actionType = "add-prefab", expectedOutcome = "Prefab scaffold is attached.", debugHint = "Inspect avatar prerequisites." },
                                            new ASMLiteSmokeStepDefinition { stepId = "assert-primary-action", label = "Primary action is shown", description = "Confirm rebuild action is visible.", actionType = "assert-primary-action", expectedOutcome = "Primary action is visible.", debugHint = "Inspect window refresh state." },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "lifecycle-roundtrip",
                                label = "Lifecycle Roundtrip",
                                description = "Validates rebuild/vendorize/detach/return lifecycle.",
                                resetOverride = "FullPackageRebuild",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-lifecycle" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "All lifecycle steps pass in order.",
                                debugHint = "Check lifecycle step ordering and action mapping.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "lifecycle-case-01",
                                        label = "Lifecycle Case",
                                        description = "Run lifecycle sequence from package-managed state.",
                                        expectedOutcome = "Rebuild/vendorize/detach/return all pass.",
                                        debugHint = "Inspect host event stream for lifecycle step boundaries.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition { stepId = "rebuild", label = "ASM-Lite assets are rebuilt", description = "Rebuild package state.", actionType = "rebuild", expectedOutcome = "Rebuild succeeds.", debugHint = "Inspect rebuild logs." },
                                            new ASMLiteSmokeStepDefinition { stepId = "hygiene-cleanup-after-rebuild", label = "Post-step cleanup is applied", description = "Reset known lifecycle drift after rebuild.", actionType = "lifecycle-hygiene-cleanup", expectedOutcome = "Package-managed baseline restored.", debugHint = "Inspect cleanup after rebuild." },
                                            new ASMLiteSmokeStepDefinition { stepId = "vendorize", label = "Generated assets are vendorized", description = "Vendorize generated assets.", actionType = "vendorize", expectedOutcome = "Vendorize succeeds.", debugHint = "Inspect vendorized assets." },
                                            new ASMLiteSmokeStepDefinition { stepId = "hygiene-cleanup-after-vendorize", label = "Post-step cleanup is applied", description = "Reset known lifecycle drift after vendorize.", actionType = "lifecycle-hygiene-cleanup", expectedOutcome = "Package-managed baseline restored.", debugHint = "Inspect cleanup after vendorize." },
                                            new ASMLiteSmokeStepDefinition { stepId = "detach", label = "ASM-Lite component is detached", description = "Detach from package-managed state.", actionType = "detach", expectedOutcome = "Detach succeeds.", debugHint = "Confirm detached workspace state." },
                                            new ASMLiteSmokeStepDefinition { stepId = "hygiene-cleanup-after-detach", label = "Post-step cleanup is applied", description = "Reset known lifecycle drift after detach.", actionType = "lifecycle-hygiene-cleanup", expectedOutcome = "Package-managed baseline restored.", debugHint = "Inspect cleanup after detach." },
                                            new ASMLiteSmokeStepDefinition { stepId = "return-to-package-managed", label = "Package-managed state is restored", description = "Return to package-managed baseline.", actionType = "return-to-package-managed", expectedOutcome = "Return succeeds.", debugHint = "Confirm package-managed state restored." },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "fixture-mutation-host",
                                label = "Fixture Mutation Host",
                                description = "Validates fixture mutation dispatch and step target overrides.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Fixture mutation runs before the catalog action.",
                                debugHint = "Inspect fixture mutation dispatch and step args.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "fixture-mutation-host-case",
                                        label = "Fixture mutation host case",
                                        description = "Apply a wrong-object fixture mutation, then run a step with overridden target paths.",
                                        expectedOutcome = "Mutation and target override both execute.",
                                        debugHint = "Inspect fake runtime mutation and opened scene lists.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "mutate-then-open-scene",
                                                label = "Fixture change is applied and scene is open",
                                                description = "Apply wrong-object mutation and open override scene.",
                                                actionType = "open-scene",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection,
                                                    scenePath = "Assets/FixtureOverride.unity",
                                                    avatarName = "FixtureOverrideAvatar",
                                                    objectName = "Wrong Target",
                                                },
                                                expectedOutcome = "Mutation runs and override scene opens.",
                                                debugHint = "Inspect fixture mutation args.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "generated-asset-recovery-signals",
                                label = "Generated Asset Recovery Signals",
                                description = "Validates generated asset recovery signal fixture dispatch for clean-add, rebuild, and orphaned generated-folder states.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Generated recovery signal actions pass through the host runner.",
                                debugHint = "Inspect generated recovery fixture mutations and action ordering.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "generated-recovery-host-case",
                                        label = "Generated recovery host case",
                                        description = "Exercise clean add, rebuild assertion, missing/stale generated folder assertions, and orphaned generated folder recovery assertion.",
                                        expectedOutcome = "All generated recovery fixture hooks and assertions execute.",
                                        debugHint = "Inspect fake runtime mutation and action lists.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "add-prefab-clean-readiness",
                                                label = "Clean baseline scaffold is added",
                                                description = "Apply remove-component and add the ASM-Lite prefab scaffold.",
                                                actionType = "add-prefab",
                                                args = new ASMLiteSmokeStepArgs { fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent },
                                                expectedOutcome = "Prefab scaffold is attached from a clean baseline.",
                                                debugHint = "Inspect remove-component fixture dispatch.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "add-prefab-for-readiness-rebuild",
                                                label = "Scaffold is added before rebuild check",
                                                description = "Apply remove-component and add the ASM-Lite prefab scaffold.",
                                                actionType = "add-prefab",
                                                args = new ASMLiteSmokeStepArgs { fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent },
                                                expectedOutcome = "Prefab scaffold is attached before the rebuild assertion.",
                                                debugHint = "Inspect remove-component fixture dispatch.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-primary-action-after-readiness-add",
                                                label = "Rebuild action is shown",
                                                description = "Assert Rebuild is the primary action after Add Prefab.",
                                                actionType = "assert-primary-action",
                                                args = new ASMLiteSmokeStepArgs { expectedPrimaryAction = "Rebuild" },
                                                expectedOutcome = "Rebuild is the primary action.",
                                                debugHint = "Inspect action hierarchy output.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-primary-action-missing-generated-folder",
                                                label = "Rebuild action is shown for missing generated folder",
                                                description = "Apply missing-generated-folder and assert Rebuild is the primary action.",
                                                actionType = "assert-primary-action",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.MissingGeneratedFolder,
                                                    expectedPrimaryAction = "Rebuild",
                                                },
                                                expectedOutcome = "Rebuild is the primary action.",
                                                debugHint = "Inspect missing-generated-folder fixture dispatch.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-primary-action-stale-generated-folder",
                                                label = "Rebuild action is shown for stale generated folder",
                                                description = "Apply stale-generated-folder and assert Rebuild is the primary action.",
                                                actionType = "assert-primary-action",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.StaleGeneratedFolder,
                                                    expectedPrimaryAction = "Rebuild",
                                                },
                                                expectedOutcome = "Rebuild is the primary action.",
                                                debugHint = "Inspect stale-generated-folder fixture dispatch.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-primary-action-generated-folder-without-component",
                                                label = "Add Prefab action is shown for orphaned generated folder",
                                                description = "Apply generated-folder-without-component and assert Add Prefab is the primary action.",
                                                actionType = "assert-primary-action",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent,
                                                    expectedPrimaryAction = "Add Prefab",
                                                },
                                                expectedOutcome = "Add Prefab is the primary action.",
                                                debugHint = "Inspect orphaned generated-folder fixture dispatch.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "generated-reference-ownership",
                                label = "Generated Reference Ownership",
                                description = "Validates generated reference ownership assertions after the default setup add flow.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Generated reference ownership assertion passes through the host runner.",
                                debugHint = "Inspect generated reference assertion action ordering.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "generated-reference-ownership-host-case",
                                        label = "Generated reference ownership host case",
                                        description = "Exercise the clean add and package-managed generated reference ownership assertion.",
                                        expectedOutcome = "Generated reference ownership fixture hooks and assertions execute.",
                                        debugHint = "Inspect fake runtime mutation and action lists.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "add-prefab-for-package-managed-references",
                                                label = "Scaffold is added before package-managed check",
                                                description = "Apply remove-component and add the ASM-Lite prefab scaffold before the package-managed assertion.",
                                                actionType = "add-prefab",
                                                args = new ASMLiteSmokeStepArgs { fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent },
                                                expectedOutcome = "Prefab scaffold is attached before generated reference assertion.",
                                                debugHint = "Inspect remove-component fixture dispatch.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-generated-references-package-managed",
                                                label = "Generated references are package-managed",
                                                description = "Assert generated references are package-managed by default after Add Prefab.",
                                                actionType = "assert-generated-references-package-managed",
                                                expectedOutcome = "Generated references are package-managed by default.",
                                                debugHint = "Inspect generated reference assertion action dispatch.",
                                            },
                                        },
                                    },
                                },
                            },
                            BuildPhase1SlotMatrixSuite(),
                            BuildPhase1PathMatrixSuite(),
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "destructive-recovery-reset",
                                label = "Destructive Recovery Reset",
                                description = "Exercises destructive setup fixture states and proves clean reset after every case.",
                                resetOverride = "FullPackageRebuild",
                                speed = "destructive",
                                risk = "destructive",
                                presetGroups = new[] { "all-setup", "destructive-drills" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Every destructive fixture state is cleaned before the next case runs.",
                                debugHint = "Inspect clean reset proof and destructive fixture evidence before rerunning.",
                                cases = new[]
                                {
                                    BuildDestructiveResetCase(
                                        "controlled-corrupt-generated-asset",
                                        "Controlled corrupt generated asset",
                                        ASMLiteSmokeSetupFixtureMutationIds.ControlledCorruptGeneratedAsset,
                                        "Assert Rebuild is still recoverable after a controlled generated asset corruption."),
                                    BuildDestructiveResetCase(
                                        "stale-vendorized-references",
                                        "Stale vendorized references",
                                        ASMLiteSmokeSetupFixtureMutationIds.VendorizedStateBaseline,
                                        "Assert package-managed return is available from stale vendorized references.",
                                        "ReturnToPackageManaged"),
                                    BuildDestructiveResetCase(
                                        "removed-generated-assets-after-component",
                                        "Removed generated assets after component exists",
                                        ASMLiteSmokeSetupFixtureMutationIds.MissingGeneratedFolder,
                                        "Assert Rebuild is available when generated assets are missing after component setup."),
                                    BuildDestructiveResetCase(
                                        "removed-component-after-generated-assets",
                                        "Removed component after generated assets exist",
                                        ASMLiteSmokeSetupFixtureMutationIds.GeneratedFolderWithoutComponent,
                                        "Assert Add Prefab is available when generated assets exist without the component.",
                                        "AddPrefab"),
                                    BuildDestructiveResetCase(
                                        "interrupted-detached-state",
                                        "Interrupted detached state",
                                        ASMLiteSmokeSetupFixtureMutationIds.DetachedStateBaseline,
                                        "Assert Add Prefab is available from a staged detached baseline.",
                                        "AddPrefab"),
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "expected-diagnostic-host",
                                label = "Expected Diagnostic Host",
                                description = "Validates active run expected diagnostic handling.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Expected diagnostic failures are reported as passing steps.",
                                debugHint = "Inspect expected diagnostic matching in the active run path.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "expected-diagnostic-host-case",
                                        label = "Expected diagnostic host case",
                                        description = "Run one expected-failure step through active run execution.",
                                        expectedOutcome = "The expected diagnostic is treated as a passed step.",
                                        debugHint = "Inspect expected diagnostic args.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "open-missing-scene",
                                                label = "Missing scene is reported",
                                                description = "Attempt to open an intentionally missing scene.",
                                                actionType = "open-scene",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    scenePath = "Assets/Missing.unity",
                                                    expectStepFailure = true,
                                                    expectedDiagnosticCode = "SETUP_SCENE_MISSING",
                                                    expectedDiagnosticContains = "scene could not be found",
                                                },
                                                expectedOutcome = "Expected scene diagnostic matches.",
                                                debugHint = "Inspect expected diagnostic matching.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "expected-diagnostic-fixture-reset-host",
                                label = "Expected Diagnostic Fixture Reset Host",
                                description = "Validates expected diagnostic cases reset fixture state before the next case runs.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Expected diagnostic fixture cases pass and reset their fixture state before continuing.",
                                debugHint = "Inspect expected diagnostic matching and fixture reset behavior between cases.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "expected-diagnostic-fixture-reset-case",
                                        label = "Expected diagnostic fixture reset case",
                                        description = "Run an expected-failure avatar selection case that mutates fixture state.",
                                        expectedOutcome = "The expected diagnostic matches and the fixture state is reset before the next case.",
                                        debugHint = "Inspect fixture mutation cleanup between cases.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "unselected-inactive-avatar",
                                                label = "Inactive avatar failure is reported",
                                                description = "Make the avatar inactive and verify the expected missing-avatar diagnostic.",
                                                actionType = "select-avatar",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.UnselectedInactiveAvatar,
                                                    expectStepFailure = true,
                                                    expectedDiagnosticCode = "SETUP_AVATAR_NOT_FOUND",
                                                    expectedDiagnosticContains = "avatar could not be found",
                                                },
                                                expectedOutcome = "Expected avatar-not-found diagnostic matches.",
                                                debugHint = "Inspect inactive-avatar fixture cleanup after the case passes.",
                                            },
                                        },
                                    },
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "follow-up-ready-case",
                                        label = "Follow-up ready case",
                                        description = "Confirm the suite continues after the fixture reset.",
                                        expectedOutcome = "The follow-up case runs after fixture cleanup.",
                                        debugHint = "Inspect case progression after the expected diagnostic case.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-host-ready",
                                                label = "Host is ready",
                                                description = "Run a simple follow-up step after the expected diagnostic case.",
                                                actionType = "assert-host-ready",
                                                expectedOutcome = "Host-ready assertion passes.",
                                                debugHint = "Inspect whether fixture cleanup let the suite continue cleanly.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "clean-reset-then-finish-host",
                                label = "Clean Reset Then Finish Host",
                                description = "Validates required clean reset clears fixture-mutation tracking before a later step ends the case.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "A required clean reset runs once and the case finishes without an extra case-end reset.",
                                debugHint = "Inspect fixture-mutation tracking after required clean reset succeeds mid-case.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "clean-reset-clears-mutation-state",
                                        label = "Required clean reset clears mutation state",
                                        description = "Apply a fixture mutation that requires clean reset, then finish the same case with a ready assertion.",
                                        expectedOutcome = "Only the required clean reset runs and the final step does not trigger duplicate cleanup.",
                                        debugHint = "Inspect case-end cleanup after required clean reset succeeded earlier in the case.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "mutate-then-clean-reset",
                                                label = "Mutation requires clean reset",
                                                description = "Apply wrong-object selection and force a clean reset before continuing the case.",
                                                actionType = "assert-host-ready",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.WrongObjectSelection,
                                                    requireCleanReset = true,
                                                },
                                                expectedOutcome = "The required clean reset restores the fixture baseline.",
                                                debugHint = "Inspect whether mutation tracking clears after the required reset.",
                                            },
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "finish-after-clean-reset",
                                                label = "Case finishes after clean reset",
                                                description = "Run a ready assertion after the required clean reset already restored the baseline.",
                                                actionType = "assert-host-ready",
                                                expectedOutcome = "The case finishes without an extra case-end fixture reset.",
                                                debugHint = "Inspect whether duplicate case-end cleanup is skipped.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "settle-then-reset-host",
                                label = "Settle Then Reset Host",
                                description = "Validates final settle-requiring steps delay fixture reset until the settle window completes.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-setup" },
                                requiresPlayMode = false,
                                stopOnFirstFailure = true,
                                expectedOutcome = "The fixture reset waits until the add-prefab settle phase completes before the next case runs.",
                                debugHint = "Inspect add-prefab settle timing versus case-end fixture reset.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "final-mutation-settle-case",
                                        label = "Final mutation settle case",
                                        description = "Apply remove-component on a final add-prefab step so the case needs settle before cleanup.",
                                        expectedOutcome = "The add-prefab pass settles before fixture reset runs.",
                                        debugHint = "Inspect whether fixture reset waits for add-prefab settle.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "add-prefab-final-mutation-settle",
                                                label = "Final add-prefab mutation settles before reset",
                                                description = "Apply remove-component and add the ASM-Lite prefab scaffold as the last step in the case.",
                                                actionType = "add-prefab",
                                                args = new ASMLiteSmokeStepArgs
                                                {
                                                    fixtureMutation = ASMLiteSmokeSetupFixtureMutationIds.RemoveComponent,
                                                },
                                                expectedOutcome = "The step enters settle before case cleanup runs.",
                                                debugHint = "Inspect settle ordering for the final mutation step.",
                                            },
                                        },
                                    },
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "post-settle-follow-up-case",
                                        label = "Post-settle follow-up case",
                                        description = "Confirm the suite continues after the settled cleanup finishes.",
                                        expectedOutcome = "The follow-up case runs after the delayed fixture reset.",
                                        debugHint = "Inspect whether the next case waits for the delayed reset.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition
                                            {
                                                stepId = "assert-host-ready-after-settle-reset",
                                                label = "Host is ready after settle reset",
                                                description = "Run a simple follow-up step after the settled cleanup.",
                                                actionType = "assert-host-ready",
                                                expectedOutcome = "Host-ready assertion passes after delayed cleanup.",
                                                debugHint = "Inspect post-settle case progression.",
                                            },
                                        },
                                    },
                                },
                            },
                            new ASMLiteSmokeSuiteDefinition
                            {
                                suiteId = "playmode-runtime-validation",
                                label = "Playmode Runtime Validation",
                                description = "Validates playmode enter/assert/exit flow.",
                                resetOverride = "Inherit",
                                speed = "standard",
                                risk = "safe",
                                presetGroups = new[] { "all-playmode" },
                                requiresPlayMode = true,
                                stopOnFirstFailure = true,
                                expectedOutcome = "Playmode runtime checks complete successfully.",
                                debugHint = "Inspect playmode transitions and runtime assertion output.",
                                cases = new[]
                                {
                                    new ASMLiteSmokeCaseDefinition
                                    {
                                        caseId = "playmode-case-01",
                                        label = "Playmode Case",
                                        description = "Enter playmode, assert runtime component validity, exit playmode.",
                                        expectedOutcome = "All playmode steps pass.",
                                        debugHint = "Check runtime assertion details and playmode transitions.",
                                        steps = new[]
                                        {
                                            new ASMLiteSmokeStepDefinition { stepId = "enter-playmode", label = "Play mode is entered", description = "Enter playmode session.", actionType = "enter-playmode", expectedOutcome = "Entered playmode.", debugHint = "Inspect playmode enter diagnostics." },
                                            new ASMLiteSmokeStepDefinition { stepId = "assert-runtime-component-valid", label = "Runtime component is valid", description = "Assert runtime component validity.", actionType = "assert-runtime-component-valid", expectedOutcome = "Runtime component is valid.", debugHint = "Check runtime assertion metadata." },
                                            new ASMLiteSmokeStepDefinition { stepId = "exit-playmode", label = "Play mode is exited", description = "Exit playmode session.", actionType = "exit-playmode", expectedOutcome = "Exited playmode.", debugHint = "Inspect playmode exit diagnostics." },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
            };

            return JsonUtility.ToJson(catalog, prettyPrint: true);
        }

        private static ASMLiteSmokeSuiteDefinition BuildPhase1SlotMatrixSuite()
        {
            return new ASMLiteSmokeSuiteDefinition
            {
                suiteId = "prebuild-slots-matrix",
                label = "Prebuild Slot Matrix",
                description = "Local host test catalog suite for Phase 1 slot-count prebuild coverage.",
                resetOverride = "Inherit",
                speed = "standard",
                risk = "safe",
                presetGroups = new[] { "all-setup" },
                requiresPlayMode = false,
                stopOnFirstFailure = true,
                expectedOutcome = "Every slot-count prebuild case executes.",
                debugHint = "Inspect Phase 1 slot-count action ordering.",
                cases = Enumerable.Range(1, 8)
                    .Select(slotCount => BuildPhase1SlotCase(slotCount))
                    .ToArray(),
            };
        }

        private static ASMLiteSmokeSuiteDefinition BuildPhase1PathMatrixSuite()
        {
            return new ASMLiteSmokeSuiteDefinition
            {
                suiteId = "prebuild-path-matrix",
                label = "Prebuild Install Path Matrix",
                description = "Local host test catalog suite for Phase 1 install-path prebuild coverage.",
                resetOverride = "Inherit",
                speed = "standard",
                risk = "safe",
                presetGroups = new[] { "all-setup" },
                requiresPlayMode = false,
                stopOnFirstFailure = true,
                expectedOutcome = "Every install-path prebuild case executes.",
                debugHint = "Inspect Phase 1 install-path action ordering.",
                cases = new[]
                {
                    BuildPhase1PathCase("disabled", false, string.Empty, 1),
                    BuildPhase1PathCase("root", true, string.Empty, 2),
                    BuildPhase1PathCase("simple", true, "ASM-Lite", 3),
                    BuildPhase1PathCase("nested", true, "Avatars/ASM-Lite", 4),
                },
            };
        }

        private static ASMLiteSmokeCaseDefinition BuildPhase1SlotCase(int slotCount)
        {
            return new ASMLiteSmokeCaseDefinition
            {
                caseId = $"S{slotCount:00}-slot-count-{slotCount}",
                label = $"Slot count {slotCount}",
                description = $"Prebuild customization flow with slot count {slotCount}.",
                expectedOutcome = "Pending and attached customization snapshots match slot-count expectations.",
                debugHint = "Inspect slot-count customization snapshot details.",
                steps = BuildPhase1CustomizationSteps(
                    "slot",
                    $"{slotCount}",
                    new ASMLiteSmokeStepDefinition
                    {
                        stepId = $"S{slotCount:00}-set-slot-count-{slotCount}",
                        label = $"Set slot count {slotCount}",
                        description = $"Set automation slot count to {slotCount}.",
                        actionType = "set-slot-count",
                        args = new ASMLiteSmokeStepArgs { slotCount = slotCount },
                        expectedOutcome = "Slot count is staged for setup.",
                        debugHint = "Inspect slot-count automation state.",
                    },
                    new ASMLiteSmokeStepArgs
                    {
                        slotCount = slotCount,
                        installPathPresetId = "disabled",
                        expectedInstallPathEnabled = false,
                        expectedNormalizedEffectivePath = string.Empty,
                        expectedComponentPresent = true,
                        expectedPrimaryAction = "Rebuild",
                    }),
            };
        }

        private static ASMLiteSmokeCaseDefinition BuildPhase1PathCase(string presetId, bool enabled, string normalizedPath, int slotCount)
        {
            return new ASMLiteSmokeCaseDefinition
            {
                caseId = $"P{slotCount:00}-install-path-{presetId}",
                label = $"Install path {presetId}",
                description = $"Prebuild customization flow with install-path preset '{presetId}'.",
                expectedOutcome = "Pending and attached customization snapshots match install-path expectations.",
                debugHint = "Inspect install-path customization snapshot details.",
                steps = BuildPhase1CustomizationSteps(
                    "path",
                    presetId,
                    new ASMLiteSmokeStepDefinition
                    {
                        stepId = $"P{slotCount:00}-set-install-path-{presetId}",
                        label = $"Set install path {presetId}",
                        description = $"Set automation install-path preset '{presetId}'.",
                        actionType = "set-install-path-state",
                        args = new ASMLiteSmokeStepArgs { installPathPresetId = presetId },
                        expectedOutcome = "Install-path state is staged for setup.",
                        debugHint = "Inspect install-path automation state.",
                    },
                    new ASMLiteSmokeStepArgs
                    {
                        slotCount = slotCount,
                        installPathPresetId = presetId,
                        expectedInstallPathEnabled = enabled,
                        expectedNormalizedEffectivePath = normalizedPath,
                        expectedComponentPresent = true,
                        expectedPrimaryAction = "Rebuild",
                    }),
            };
        }

        private static ASMLiteSmokeStepDefinition[] BuildPhase1CustomizationSteps(
            string axis,
            string suffix,
            ASMLiteSmokeStepDefinition customizationStep,
            ASMLiteSmokeStepArgs expectedArgs)
        {
            return new[]
            {
                new ASMLiteSmokeStepDefinition { stepId = $"{axis}-{suffix}-recover-context", label = "Recover setup context", description = "Recover setup context before the prebuild assertion.", actionType = "prelude-recover-context", expectedOutcome = "Setup context is recovered.", debugHint = "Inspect prelude recovery output." },
                new ASMLiteSmokeStepDefinition { stepId = $"{axis}-{suffix}-assert-no-component", label = "No component before setup", description = "Assert ASM-Lite component is not attached before setup.", actionType = "assert-no-component", expectedOutcome = "No ASM-Lite component is attached.", debugHint = "Inspect avatar hierarchy before setup." },
                customizationStep,
                new ASMLiteSmokeStepDefinition { stepId = $"{axis}-{suffix}-assert-pending-snapshot", label = "Pending customization matches", description = "Assert staged customization snapshot before Add Prefab.", actionType = "assert-pending-customization-snapshot", args = expectedArgs, expectedOutcome = "Pending customization snapshot matches.", debugHint = "Inspect pending customization snapshot." },
                new ASMLiteSmokeStepDefinition { stepId = $"{axis}-{suffix}-add-prefab", label = "Add prefab", description = "Attach ASM-Lite prefab with the staged customization.", actionType = "add-prefab", expectedOutcome = "ASM-Lite component is attached.", debugHint = "Inspect Add Prefab result." },
                new ASMLiteSmokeStepDefinition { stepId = $"{axis}-{suffix}-assert-attached-snapshot", label = "Attached customization matches", description = "Assert attached component snapshot after Add Prefab.", actionType = "assert-attached-customization-snapshot", args = expectedArgs, expectedOutcome = "Attached customization snapshot matches.", debugHint = "Inspect attached component snapshot." },
            };
        }

        private static ASMLiteSmokeCaseDefinition BuildDestructiveResetCase(
            string caseId,
            string label,
            string fixtureMutation,
            string description,
            string expectedPrimaryAction = "Rebuild")
        {
            return new ASMLiteSmokeCaseDefinition
            {
                caseId = caseId,
                label = label,
                description = description,
                expectedOutcome = "The destructive fixture state is exercised and then reset to a clean baseline.",
                debugHint = "Inspect destructive fixture mutation details and clean reset proof.",
                steps = new[]
                {
                    new ASMLiteSmokeStepDefinition
                    {
                        stepId = caseId,
                        label = label,
                        description = description,
                        actionType = "assert-primary-action",
                        args = new ASMLiteSmokeStepArgs
                        {
                            fixtureMutation = fixtureMutation,
                            expectedPrimaryAction = expectedPrimaryAction,
                            preserveFailureEvidence = true,
                            requireCleanReset = true,
                        },
                        expectedOutcome = "The destructive state is recoverable and clean reset proof is recorded.",
                        debugHint = "Inspect primary action contract and setup fixture reset detail.",
                    },
                },
            };
        }

        private static ASMLiteSmokeProtocolCommand BuildShutdownCommand(int commandSeq, string commandId)
        {
            return new ASMLiteSmokeProtocolCommand
            {
                protocolVersion = ASMLiteSmokeProtocol.SupportedProtocolVersion,
                sessionId = "phase06",
                commandId = commandId,
                commandSeq = commandSeq,
                commandType = "shutdown-session",
                createdAtUtc = "2026-04-23T00:00:00Z",
                launchSession = null,
                runSuite = null,
                reviewDecision = null,
                abortRun = null,
            };
        }

        private sealed class RunnerTestContext : IDisposable
        {
            private readonly GameObject _avatarObject;
            private bool _disposed;

            private RunnerTestContext(
                string sessionRoot,
                string catalogPath,
                ASMLiteSmokeSessionPaths paths,
                FakeRuntime runtime,
                ASMLiteSmokeOverlayHostConfiguration configuration,
                ASMLiteSmokeOverlayHostRunner runner,
                GameObject avatarObject)
            {
                SessionRoot = sessionRoot;
                CatalogPath = catalogPath;
                Paths = paths;
                Runtime = runtime;
                Configuration = configuration;
                Runner = runner;
                _avatarObject = avatarObject;
            }

            internal string SessionRoot { get; }
            internal string CatalogPath { get; }
            internal ASMLiteSmokeSessionPaths Paths { get; }
            internal FakeRuntime Runtime { get; }
            internal ASMLiteSmokeOverlayHostConfiguration Configuration { get; }
            internal ASMLiteSmokeOverlayHostRunner Runner { get; private set; }

            internal static RunnerTestContext Create(bool exitOnReady)
            {
                RunnerTestContext context = CreateWithoutStart(exitOnReady);
                context.StartRunner();
                return context;
            }

            internal static RunnerTestContext CreateWithoutStart(bool exitOnReady)
            {
                string sessionRoot = Path.Combine(Path.GetTempPath(), $"asmlite-smoke-host-{Guid.NewGuid():N}");
                Directory.CreateDirectory(sessionRoot);

                string catalogPath = Path.Combine(sessionRoot, "catalog.json");
                File.WriteAllText(catalogPath, BuildTestCatalogJson());

                var paths = ASMLiteSmokeArtifactPaths.FromSessionRoot(sessionRoot);
                paths.EnsureSessionLayout();

                var runtime = new FakeRuntime();
                var avatarObject = new GameObject("Oct25_Dress");
                var avatar = avatarObject.AddComponent<VRCAvatarDescriptor>();
                runtime.RegisterAvatar(avatar);

                var configuration = new ASMLiteSmokeOverlayHostConfiguration
                {
                    SessionRootPath = sessionRoot,
                    CatalogPath = catalogPath,
                    ScenePath = "Assets/Click ME.unity",
                    AvatarName = "Oct25_Dress",
                    StartupTimeoutSeconds = 120,
                    HeartbeatSeconds = 5,
                    ExitOnReady = exitOnReady,
                };

                var runner = ASMLiteSmokeOverlayHost.CreateRunnerForTesting(configuration, runtime);
                return new RunnerTestContext(sessionRoot, catalogPath, paths, runtime, configuration, runner, avatarObject);
            }

            internal void StartRunner()
            {
                Runner.Start();
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                try
                {
                    Runner?.StopForTesting();
                }
                catch
                {
                    // Ignore teardown errors.
                }

                if (_avatarObject != null)
                    UnityEngine.Object.DestroyImmediate(_avatarObject);

                if (Directory.Exists(SessionRoot))
                    Directory.Delete(SessionRoot, recursive: true);
            }
        }

        private sealed class FakeRuntime : IASMLiteSmokeOverlayHostRuntime
        {
            private readonly Dictionary<string, VRCAvatarDescriptor> _avatars = new Dictionary<string, VRCAvatarDescriptor>(StringComparer.Ordinal);
            private UnityEditor.EditorApplication.CallbackFunction _tick;
            private readonly DateTime _baseUtc = new DateTime(2026, 4, 23, 0, 0, 0, DateTimeKind.Utc);
            private string _activeScenePath = "Assets/Untitled.unity";

            internal int RegisterUpdateCount { get; private set; }
            internal int UnregisterUpdateCount { get; private set; }
            internal int CloseAutomationWindowIfOpenCount { get; private set; }
            internal List<int> ExitWithoutSavingCodes { get; } = new List<int>();
            internal List<int> ExitCodes => ExitWithoutSavingCodes;
            internal List<string> ExecutedActions { get; } = new List<string>();
            internal List<string> OpenedScenes { get; } = new List<string>();
            internal List<string> SelectedAvatars { get; } = new List<string>();
            internal List<string> AppliedFixtureMutations { get; } = new List<string>();
            internal string LastMutationObjectName { get; private set; } = string.Empty;
            internal int ResetSetupFixtureCount { get; private set; }

            private readonly Dictionary<string, string> _forcedFailuresByAction = new Dictionary<string, string>(StringComparer.Ordinal);
            private readonly List<Tuple<string, string>> _consoleErrors = new List<Tuple<string, string>>();
            private readonly Dictionary<string, string> _consoleErrorsByAction = new Dictionary<string, string>(StringComparer.Ordinal);
            private bool _consoleErrorCaptureActive;
            private string _nextSetupFixtureResetFailure = string.Empty;

            internal double CurrentTimeSeconds { get; private set; }

            public string GetActiveScenePath()
            {
                return _activeScenePath;
            }

            public void OpenScene(string scenePath)
            {
                OpenedScenes.Add(scenePath);
                _activeScenePath = scenePath;
            }

            public VRCAvatarDescriptor FindAvatarByName(string avatarName)
            {
                _avatars.TryGetValue(avatarName ?? string.Empty, out VRCAvatarDescriptor avatar);
                return avatar;
            }

            public void SelectAvatarForAutomation(VRCAvatarDescriptor avatar)
            {
                SelectedAvatars.Add(avatar == null || avatar.gameObject == null ? string.Empty : avatar.gameObject.name);
            }

            public void CloseAutomationWindowIfOpen()
            {
                CloseAutomationWindowIfOpenCount++;
            }

            public double GetTimeSinceStartup()
            {
                return CurrentTimeSeconds;
            }

            public string GetUtcNowIso()
            {
                return _baseUtc.AddSeconds(CurrentTimeSeconds).ToString("O", CultureInfo.InvariantCulture);
            }

            public string GetUnityVersion()
            {
                return "2022.3.22f1";
            }

            public void RegisterUpdate(UnityEditor.EditorApplication.CallbackFunction tick)
            {
                RegisterUpdateCount++;
                _tick = tick;
            }

            public void UnregisterUpdate(UnityEditor.EditorApplication.CallbackFunction tick)
            {
                UnregisterUpdateCount++;
                if (_tick == tick)
                    _tick = null;
            }

            public string[] EnumerateCommandFiles(string commandsDirectoryPath)
            {
                if (!Directory.Exists(commandsDirectoryPath))
                    return Array.Empty<string>();

                return Directory.GetFiles(commandsDirectoryPath, "*.json", SearchOption.TopDirectoryOnly);
            }

            public string ReadAllText(string path)
            {
                return File.ReadAllText(path);
            }

            public bool ApplySetupFixtureMutation(
                ASMLiteSmokeStepArgs args,
                string defaultScenePath,
                string defaultAvatarName,
                string evidenceRootPath,
                out string detail,
                out string stackTrace)
            {
                string mutation = args == null || string.IsNullOrWhiteSpace(args.fixtureMutation)
                    ? string.Empty
                    : args.fixtureMutation.Trim();
                AppliedFixtureMutations.Add(mutation);
                LastMutationObjectName = args == null || string.IsNullOrWhiteSpace(args.objectName)
                    ? string.Empty
                    : args.objectName.Trim();
                detail = $"{mutation} fixture mutation applied.";
                stackTrace = string.Empty;
                return true;
            }

            public bool ResetSetupFixture(out string detail, out string stackTrace)
            {
                ResetSetupFixtureCount++;
                stackTrace = string.Empty;
                if (!string.IsNullOrWhiteSpace(_nextSetupFixtureResetFailure))
                {
                    detail = _nextSetupFixtureResetFailure;
                    _nextSetupFixtureResetFailure = string.Empty;
                    return false;
                }

                detail = "Setup fixture reset completed with clean baseline proof.";
                return true;
            }

            public bool ExecuteCatalogStep(
                string actionType,
                ASMLiteSmokeStepArgs args,
                string scenePath,
                string avatarName,
                out string detail,
                out string stackTrace)
            {
                string normalizedAction = string.IsNullOrWhiteSpace(actionType) ? string.Empty : actionType.Trim();
                ExecutedActions.Add(normalizedAction);

                if (_forcedFailuresByAction.TryGetValue(normalizedAction, out string failureMessage))
                {
                    detail = failureMessage;
                    stackTrace = string.Empty;
                    return false;
                }

                if (string.Equals(normalizedAction, "open-scene", StringComparison.Ordinal))
                    OpenScene(scenePath);
                else if (string.Equals(normalizedAction, "select-avatar", StringComparison.Ordinal))
                    SelectAvatarForAutomation(FindAvatarByName(avatarName));

                if (_consoleErrorCaptureActive && _consoleErrorsByAction.TryGetValue(normalizedAction, out string consoleErrorMessage))
                    _consoleErrors.Add(Tuple.Create(consoleErrorMessage, "Injected Unity console stack trace."));

                detail = $"{normalizedAction} completed.";
                stackTrace = string.Empty;
                return true;
            }

            public void StartConsoleErrorCapture()
            {
                _consoleErrors.Clear();
                _consoleErrorCaptureActive = true;
            }

            public void StopConsoleErrorCapture()
            {
                _consoleErrorCaptureActive = false;
            }

            public int GetConsoleErrorCheckpoint()
            {
                return _consoleErrors.Count;
            }

            public bool TryGetConsoleErrorsSince(int checkpoint, out string detail, out string stackTrace)
            {
                int startIndex = Math.Max(0, Math.Min(checkpoint, _consoleErrors.Count));
                var errors = _consoleErrors.Skip(startIndex).ToArray();
                if (errors.Length == 0)
                {
                    detail = string.Empty;
                    stackTrace = string.Empty;
                    return false;
                }

                detail = string.Join("\n", errors.Select(item => $"Unity console error: {item.Item1}"));
                stackTrace = string.Join("\n\n", errors.Select(item => item.Item2));
                return true;
            }

            public void ExitEditorWithoutSaving(int exitCode)
            {
                ExitWithoutSavingCodes.Add(exitCode);
            }

            internal void RegisterAvatar(VRCAvatarDescriptor avatar)
            {
                if (avatar == null || avatar.gameObject == null)
                    throw new InvalidOperationException("A live avatar descriptor is required.");

                _avatars[avatar.gameObject.name] = avatar;
            }

            internal void FailAction(string actionType, string failureMessage)
            {
                if (string.IsNullOrWhiteSpace(actionType))
                    throw new InvalidOperationException("actionType is required.");

                _forcedFailuresByAction[actionType.Trim()] = string.IsNullOrWhiteSpace(failureMessage)
                    ? "Injected action failure."
                    : failureMessage.Trim();
            }

            internal void FailNextSetupFixtureReset(string failureMessage)
            {
                _nextSetupFixtureResetFailure = string.IsNullOrWhiteSpace(failureMessage)
                    ? "Injected setup fixture reset failure."
                    : failureMessage.Trim();
            }

            internal void EmitConsoleErrorDuringAction(string actionType, string message)
            {
                if (string.IsNullOrWhiteSpace(actionType))
                    throw new InvalidOperationException("actionType is required.");

                _consoleErrorsByAction[actionType.Trim()] = string.IsNullOrWhiteSpace(message)
                    ? "Injected Unity console error."
                    : message.Trim();
            }

            internal void AdvanceSeconds(double seconds)
            {
                CurrentTimeSeconds += seconds;
                _tick?.Invoke();
            }
        }
    }
}
