using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Headless")]
    public class ASMLiteWindowRiskAffordanceTests
    {
        [Test]
        public void AttachedAvatar_MaintenanceActionsStayAdvanced()
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
