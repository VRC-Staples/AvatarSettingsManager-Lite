using System;
using System.Collections.Generic;

namespace ASMLite.Editor
{
    internal enum AsmLiteWindowActionGroup
    {
        Primary,
        Advanced,
    }

    internal enum AsmLiteWindowActionExecution
    {
        None,
        AddPrefab,
        Rebuild,
        ReturnToPackageManaged,
        RemovePrefab,
        Detach,
        Vendorize,
    }

    internal readonly struct AsmLiteWindowActionConfirmationMetadata
    {
        public AsmLiteWindowActionConfirmationMetadata(
            bool required,
            string title,
            string message,
            string confirmLabel,
            string cancelLabel)
        {
            Required = required;
            Title = title ?? string.Empty;
            Message = message ?? string.Empty;
            ConfirmLabel = confirmLabel ?? string.Empty;
            CancelLabel = cancelLabel ?? string.Empty;
        }

        public bool Required { get; }
        public string Title { get; }
        public string Message { get; }
        public string ConfirmLabel { get; }
        public string CancelLabel { get; }

        public static AsmLiteWindowActionConfirmationMetadata None { get; } =
            new AsmLiteWindowActionConfirmationMetadata(false, string.Empty, string.Empty, string.Empty, string.Empty);
    }

    internal readonly struct AsmLiteWindowActionDescriptor
    {
        public AsmLiteWindowActionDescriptor(
            ASMLiteWindow.AsmLiteWindowAction action,
            AsmLiteWindowActionGroup group,
            string label,
            string heading,
            string description,
            bool isMaintenance,
            bool isDestructive,
            bool isVisible,
            bool isEnabled,
            AsmLiteWindowActionExecution execution,
            bool supportsVisibleAutomation,
            AsmLiteWindowActionConfirmationMetadata confirmation)
        {
            Action = action;
            Group = group;
            Label = label ?? string.Empty;
            Heading = heading ?? string.Empty;
            Description = description ?? string.Empty;
            IsMaintenance = isMaintenance;
            IsDestructive = isDestructive;
            IsVisible = isVisible;
            IsEnabled = isEnabled;
            Execution = execution;
            SupportsVisibleAutomation = supportsVisibleAutomation;
            Confirmation = confirmation;
        }

        public ASMLiteWindow.AsmLiteWindowAction Action { get; }
        public AsmLiteWindowActionGroup Group { get; }
        public string Label { get; }
        public string Heading { get; }
        public string Description { get; }
        public bool IsMaintenance { get; }
        public bool IsDestructive { get; }
        public bool IsVisible { get; }
        public bool IsEnabled { get; }
        public AsmLiteWindowActionExecution Execution { get; }
        public bool SupportsVisibleAutomation { get; }
        public AsmLiteWindowActionConfirmationMetadata Confirmation { get; }
    }

    internal static class AsmLiteWindowActionModel
    {
        internal const string DetachDescriptionText =
            "Keep your current in-game preset data working, but remove the ASM-Lite tool object from this avatar. Great for sharing a finished avatar. You won’t be able to tweak ASM-Lite settings unless you add it again.";

        private const string VendorizeDescriptionText =
            "Keep ASM-Lite attached and editable, but mirror generated payload files into Assets/ASM-Lite/<AvatarName> and use those mirrored files instead of package generated assets.";

        private static readonly AsmLiteWindowActionConfirmationMetadata RemovePrefabConfirmation =
            new AsmLiteWindowActionConfirmationMetadata(
                required: true,
                title: "Remove ASM-Lite Prefab",
                message: "Are you sure you want to remove the ASM-Lite prefab from this avatar?\n\n" +
                         "Any unsaved changes will be lost, but your avatar and expression parameters will not be affected.",
                confirmLabel: "Remove",
                cancelLabel: "Cancel");

        public static ASMLiteWindow.AsmLiteActionHierarchy Build(
            ASMLiteInstallationState toolState,
            bool hasComponent,
            bool advancedDisclosureExpanded)
        {
            var descriptors = new List<AsmLiteWindowActionDescriptor>();

            if (hasComponent)
            {
                descriptors.Add(CreateDescriptor(
                    ASMLiteWindow.AsmLiteWindowAction.Rebuild,
                    AsmLiteWindowActionGroup.Primary,
                    advancedDisclosureExpanded));
                descriptors.Add(CreateDescriptor(
                    ASMLiteWindow.AsmLiteWindowAction.RemovePrefab,
                    AsmLiteWindowActionGroup.Advanced,
                    advancedDisclosureExpanded));
                descriptors.Add(CreateDescriptor(
                    ASMLiteWindow.AsmLiteWindowAction.Detach,
                    AsmLiteWindowActionGroup.Advanced,
                    advancedDisclosureExpanded));
                descriptors.Add(CreateDescriptor(
                    ASMLiteWindow.AsmLiteWindowAction.Vendorize,
                    AsmLiteWindowActionGroup.Advanced,
                    advancedDisclosureExpanded));

                if (toolState == ASMLiteInstallationState.Vendorized)
                {
                    descriptors.Add(CreateDescriptor(
                        ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged,
                        AsmLiteWindowActionGroup.Advanced,
                        advancedDisclosureExpanded));
                }

                return new ASMLiteWindow.AsmLiteActionHierarchy(descriptors.ToArray(), advancedDisclosureExpanded);
            }

            if (toolState == ASMLiteInstallationState.Detached || toolState == ASMLiteInstallationState.Vendorized)
            {
                descriptors.Add(CreateDescriptor(
                    ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged,
                    AsmLiteWindowActionGroup.Primary,
                    advancedDisclosureExpanded));
                return new ASMLiteWindow.AsmLiteActionHierarchy(descriptors.ToArray(), advancedDisclosureExpanded);
            }

            descriptors.Add(CreateDescriptor(
                ASMLiteWindow.AsmLiteWindowAction.AddPrefab,
                AsmLiteWindowActionGroup.Primary,
                advancedDisclosureExpanded));
            return new ASMLiteWindow.AsmLiteActionHierarchy(descriptors.ToArray(), advancedDisclosureExpanded);
        }

        public static AsmLiteWindowActionDescriptor CreateDescriptor(
            ASMLiteWindow.AsmLiteWindowAction action,
            AsmLiteWindowActionGroup group,
            bool advancedDisclosureExpanded)
        {
            var definition = GetDefinition(action);
            bool isVisible = group == AsmLiteWindowActionGroup.Primary || advancedDisclosureExpanded;
            return new AsmLiteWindowActionDescriptor(
                action,
                group,
                definition.Label,
                definition.Heading,
                definition.Description,
                definition.IsMaintenance,
                definition.IsDestructive,
                isVisible,
                isEnabled: true,
                definition.Execution,
                definition.SupportsVisibleAutomation,
                definition.Confirmation);
        }

        public static bool IsMaintenanceAction(ASMLiteWindow.AsmLiteWindowAction action)
        {
            return GetDefinition(action).IsMaintenance;
        }

        private static ActionDefinition GetDefinition(ASMLiteWindow.AsmLiteWindowAction action)
        {
            switch (action)
            {
                case ASMLiteWindow.AsmLiteWindowAction.AddPrefab:
                    return new ActionDefinition(
                        label: "Add ASM-Lite Prefab",
                        heading: string.Empty,
                        description: string.Empty,
                        isMaintenance: false,
                        isDestructive: false,
                        execution: AsmLiteWindowActionExecution.AddPrefab,
                        supportsVisibleAutomation: true,
                        confirmation: AsmLiteWindowActionConfirmationMetadata.None);
                case ASMLiteWindow.AsmLiteWindowAction.Rebuild:
                    return new ActionDefinition(
                        label: "Rebuild ASM-Lite",
                        heading: string.Empty,
                        description: string.Empty,
                        isMaintenance: false,
                        isDestructive: false,
                        execution: AsmLiteWindowActionExecution.Rebuild,
                        supportsVisibleAutomation: true,
                        confirmation: AsmLiteWindowActionConfirmationMetadata.None);
                case ASMLiteWindow.AsmLiteWindowAction.ReturnToPackageManaged:
                    return new ActionDefinition(
                        label: "Return to Package Managed",
                        heading: "Return to Package Managed Mode",
                        description: "Re-attach the editable ASM-Lite prefab and return this avatar to package-managed workflow. Keeps your current avatar content and restores normal ASM-Lite editing.",
                        isMaintenance: false,
                        isDestructive: false,
                        execution: AsmLiteWindowActionExecution.ReturnToPackageManaged,
                        supportsVisibleAutomation: true,
                        confirmation: AsmLiteWindowActionConfirmationMetadata.None);
                case ASMLiteWindow.AsmLiteWindowAction.RemovePrefab:
                    return new ActionDefinition(
                        label: "Remove Prefab",
                        heading: string.Empty,
                        description: string.Empty,
                        isMaintenance: true,
                        isDestructive: true,
                        execution: AsmLiteWindowActionExecution.RemovePrefab,
                        supportsVisibleAutomation: false,
                        confirmation: RemovePrefabConfirmation);
                case ASMLiteWindow.AsmLiteWindowAction.Detach:
                    return new ActionDefinition(
                        label: "Detach ASM-Lite",
                        heading: "Detach ASM-Lite (Runtime-safe)",
                        description: DetachDescriptionText,
                        isMaintenance: true,
                        isDestructive: false,
                        execution: AsmLiteWindowActionExecution.Detach,
                        supportsVisibleAutomation: true,
                        confirmation: AsmLiteWindowActionConfirmationMetadata.None);
                case ASMLiteWindow.AsmLiteWindowAction.Vendorize:
                    return new ActionDefinition(
                        label: "Vendorize (Keep Attached)",
                        heading: "Vendorize ASM-Lite Payload",
                        description: VendorizeDescriptionText,
                        isMaintenance: true,
                        isDestructive: false,
                        execution: AsmLiteWindowActionExecution.Vendorize,
                        supportsVisibleAutomation: true,
                        confirmation: AsmLiteWindowActionConfirmationMetadata.None);
                case ASMLiteWindow.AsmLiteWindowAction.ReturnAttachedVendorizedToPackageManaged:
                    return new ActionDefinition(
                        label: "Return This Avatar to Package Managed",
                        heading: "Return This Avatar to Package Managed",
                        description: "Stop using the vendorized payload folder for this attached ASM-Lite component and return to package-managed generated assets.",
                        isMaintenance: true,
                        isDestructive: false,
                        execution: AsmLiteWindowActionExecution.ReturnToPackageManaged,
                        supportsVisibleAutomation: true,
                        confirmation: AsmLiteWindowActionConfirmationMetadata.None);
                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown ASM-Lite window action.");
            }
        }

        private readonly struct ActionDefinition
        {
            public ActionDefinition(
                string label,
                string heading,
                string description,
                bool isMaintenance,
                bool isDestructive,
                AsmLiteWindowActionExecution execution,
                bool supportsVisibleAutomation,
                AsmLiteWindowActionConfirmationMetadata confirmation)
            {
                Label = label;
                Heading = heading;
                Description = description;
                IsMaintenance = isMaintenance;
                IsDestructive = isDestructive;
                Execution = execution;
                SupportsVisibleAutomation = supportsVisibleAutomation;
                Confirmation = confirmation;
            }

            public string Label { get; }
            public string Heading { get; }
            public string Description { get; }
            public bool IsMaintenance { get; }
            public bool IsDestructive { get; }
            public AsmLiteWindowActionExecution Execution { get; }
            public bool SupportsVisibleAutomation { get; }
            public AsmLiteWindowActionConfirmationMetadata Confirmation { get; }
        }
    }
}
