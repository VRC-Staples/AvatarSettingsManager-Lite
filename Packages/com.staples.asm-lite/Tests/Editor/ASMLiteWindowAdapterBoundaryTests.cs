using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowAdapterBoundaryTests
    {
        [Test]
        public void ASMLiteWindow_DoesNotOwnLifecycleOrFullControllerPolicy()
        {
            string windowSource = ReadPackageSource("Editor/ASMLiteWindow.cs");
            string operationsSource = ReadPackageSource("Editor/ASMLiteWindowOperations.cs");

            Assert.That(windowSource, Does.Not.Contain("ASMLiteLifecycleTransactionService."),
                "The editor window should delegate lifecycle transaction policy through the window operation seam.");
            Assert.That(windowSource, Does.Not.Contain("ASMLitePrefabCreator.TryRefreshLiveFullControllerWiring"),
                "The editor window should not own FullController refresh policy directly.");
            Assert.That(windowSource, Does.Not.Contain("ASMLitePrefabCreator.TryRetargetLiveFullControllerGeneratedAssetsWithDiagnostics"),
                "The editor window should not own FullController retarget policy directly.");
            Assert.That(windowSource, Does.Not.Contain("ASMLitePrefabCreator."),
                "The editor window should delegate prefab creation and stale-entry detection through the window operation seam.");
            Assert.That(windowSource, Does.Not.Contain("ASMLiteMigrationContinuityService.CaptureCustomizationSnapshot"),
                "The editor window should use ASMLiteCustomizationDraft when converting attached component customization.");
            Assert.That(windowSource, Does.Not.Contain("ASMLiteMigrationContinuityService.ApplyCustomizationSnapshot"),
                "The editor window should use ASMLiteCustomizationDraft when applying customization to components.");
            Assert.That(operationsSource, Does.Contain("ASMLitePrefabCreator.CreatePrefab"),
                "The operation seam should own prefab creation service calls for the window adapter.");
            Assert.That(operationsSource, Does.Contain("ASMLitePrefabCreator.HasStalePrmsEntry"),
                "The operation seam should own stale prefab-entry detection for the window adapter.");
        }

        [Test]
        public void ASMLiteWindow_KeepsExtractedPresenterSeamsAsPrimaryPolicyOwners()
        {
            string windowSource = ReadPackageSource("Editor/ASMLiteWindow.cs");
            string operationsSource = ReadPackageSource("Editor/ASMLiteWindowOperations.cs");

            Assert.That(windowSource, Does.Contain("ASMLiteWindowOperations."),
                "Window lifecycle/build actions should go through ASMLiteWindowOperations.");
            Assert.That(windowSource, Does.Contain("AsmLiteWindowActionModel."),
                "Window action ordering/copy/visibility should remain owned by the action model.");
            Assert.That(windowSource, Does.Contain("ASMLiteCustomizationDraft"),
                "Window customization state should remain owned by the customization draft seam.");
            Assert.That(windowSource, Does.Not.Contain("ASMLiteInstallationStateService."),
                "Window state labels should remain delegated through the window operation seam instead of owning installation policy.");
            Assert.That(operationsSource, Does.Contain("ASMLiteInstallationStateService.Resolve"),
                "The operation seam should be the bridge from the window adapter to the installation-state service.");
        }

        [Test]
        public void ASMLiteWindow_RefreshesReadonlyCustomizationDraftWithoutReassignment()
        {
            string windowSource = ReadPackageSource("Editor/ASMLiteWindow.cs");

            Assert.That(windowSource, Does.Contain("private readonly ASMLiteCustomizationDraft _customizationDraft"),
                "The window should keep one owned draft instance for pending customization state.");
            Assert.That(windowSource, Does.Not.Contain("_customizationDraft = ASMLiteCustomizationDraft.CaptureFromComponent(component);"),
                "A readonly customization draft cannot be reassigned when copying component customization.");
            Assert.That(windowSource, Does.Contain("_customizationDraft.RefreshFromComponent(component);"),
                "Attached component customization should refresh the owned draft through the draft seam.");
        }

        private static string ReadPackageSource(string packageRelativePath)
        {
            string packagePath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.staples.asm-lite",
                packageRelativePath));
            return File.ReadAllText(packagePath);
        }
    }
}
