using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteWindowRiskAffordanceTests
    {
        [Test]
        public void MaintenanceActionClassification_IdentifiesOnlyMaintenanceActions()
        {
            var allActions = Enum.GetValues(typeof(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction))
                .Cast<ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction>()
                .ToArray();

            var expectedMaintenanceActions = new[]
            {
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize,
                ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged,
            };

            foreach (var action in expectedMaintenanceActions)
            {
                Assert.IsTrue(
                    ASMLite.Editor.ASMLiteWindow.IsMaintenanceAction(action),
                    $"Action '{action}' should be classified as a maintenance action.");
            }

            foreach (var action in allActions.Except(expectedMaintenanceActions))
            {
                Assert.IsFalse(
                    ASMLite.Editor.ASMLiteWindow.IsMaintenanceAction(action),
                    $"Action '{action}' should not be classified as a maintenance action.");
            }
        }

        [Test]
        public void AttachedAvatar_MaintenanceActions_AreAdvancedOnlyWhilePrimaryFlowStaysRecommended()
        {
            var ctx = ASMLiteTestFixtures.CreateTestAvatar();
            var window = ScriptableObject.CreateInstance<ASMLite.Editor.ASMLiteWindow>();
            try
            {
                window.SelectAvatarForAutomation(ctx.AvDesc);

                var hierarchy = window.GetActionHierarchyContract();

                Assert.IsFalse(hierarchy.AdvancedDisclosureExpanded,
                    "Fresh attached-avatar windows should keep Advanced Actions collapsed by default.");
                Assert.IsTrue(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Rebuild),
                    "Attached package-managed avatars should keep Rebuild in the primary recommended workflow.");

                Assert.IsFalse(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab),
                    "Remove Prefab should stay out of the primary action group for attached avatars.");
                Assert.IsFalse(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach),
                    "Detach should stay out of the primary action group for attached avatars.");
                Assert.IsFalse(hierarchy.HasPrimaryAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize),
                    "Vendorize should stay out of the primary action group for attached avatars.");

                Assert.IsTrue(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.RemovePrefab),
                    "Remove Prefab should remain available under Advanced Actions for attached avatars.");
                Assert.IsTrue(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Detach),
                    "Detach should remain available under Advanced Actions for attached avatars.");
                Assert.IsTrue(hierarchy.HasAdvancedAction(ASMLite.Editor.ASMLiteWindow.AsmLiteWindowAction.Vendorize),
                    "Vendorize should remain available under Advanced Actions for attached avatars.");
            }
            finally
            {
                Object.DestroyImmediate(window);
                ASMLiteTestFixtures.TearDownTestAvatar(ctx.AvatarGo);
            }
        }
    }
}
