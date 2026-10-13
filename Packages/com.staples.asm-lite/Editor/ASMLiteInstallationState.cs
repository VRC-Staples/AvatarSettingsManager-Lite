using ASMLite;
using VRC.SDK3.Avatars.Components;

namespace ASMLite.Editor
{
    internal enum ASMLiteInstallationState
    {
        NotInstalled,
        PackageManaged,
        Detached,
        Vendorized,
    }

    internal static class ASMLiteInstallationStateService
    {
        internal static ASMLiteInstallationState Resolve(VRCAvatarDescriptor avatar, ASMLiteComponent component)
        {
            if (component != null)
                return component.useVendorizedGeneratedAssets ? ASMLiteInstallationState.Vendorized : ASMLiteInstallationState.PackageManaged;
            if (avatar == null)
                return ASMLiteInstallationState.NotInstalled;
            if (ASMLiteGeneratedOwnershipPolicy.HasVendorizedReferences(avatar))
                return ASMLiteInstallationState.Vendorized;
            if (HasAsmLiteRuntimeMarkers(avatar))
                return ASMLiteInstallationState.Detached;
            return ASMLiteInstallationState.NotInstalled;
        }

        internal static bool HasAsmLiteRuntimeMarkers(VRCAvatarDescriptor avatar)
        {
            // Runtime delivery evidence remains separate from vendorized path ownership.
            return ASMLiteGeneratedOwnershipPolicy.HasRuntimeMarkers(avatar);
        }
    }
}
