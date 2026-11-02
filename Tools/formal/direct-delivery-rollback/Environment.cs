using ASMLite.Editor;

namespace VRC.SDK3.Avatars.Components
{
    internal sealed class VRCAvatarDescriptor
    {
        internal sealed class GameObject { internal string name = "avatar"; }
        internal GameObject gameObject = new();
    }
}

namespace ASMLite.Editor
{
    internal sealed class ASMLiteComponent { }
    internal enum ASMLiteInstallationState { PackageManaged, Vendorized }
    internal enum ASMLiteLifecycleTransactionStage { Execute, Verify }
    internal enum ASMLiteLifecycleTransactionTestFailurePoint
    {
        ThrowAfterDetachExecute, AfterDetachExecute, DuringDetachVerify, DuringVendorizeDetachVerify
    }

    internal sealed record AvatarPayload(int Menu, int Parameters, int Fx)
    {
        internal AvatarPayload Mutate(int mask, int value) => new(
            (mask & 1) != 0 ? value : Menu,
            (mask & 2) != 0 ? value : Parameters,
            (mask & 4) != 0 ? value : Fx);
    }

    internal sealed class DependencyException(string message) : Exception(message);

    internal sealed class BoundaryEnvironment
    {
        internal static BoundaryEnvironment Current;
        internal readonly AvatarPayload Baseline = new(101, 202, 303);
        internal AvatarPayload Payload = new(101, 202, 303);
        internal readonly List<string> Events = [];
        internal readonly List<int> Prefix;
        internal readonly List<int> Choices = [];
        internal readonly Stack<List<int>> Pending;
        internal bool DetachSucceeded;
        internal bool RetargetSucceeded;
        internal bool VerifySucceeded;
        internal bool RestoreSucceeded;
        internal bool RestoreFailed;
        internal int RestoreCalls;
        internal int DisposeCalls;
        internal Exception DependencyFailure;

        internal BoundaryEnvironment(List<int> prefix, Stack<List<int>> pending)
        {
            Prefix = prefix;
            Pending = pending;
        }

        internal int Choose(string operation, int size)
        {
            if (Choices.Count >= 16)
                throw new InvalidOperationException("SCOPE: more than 16 dependency choices; review the checker.");
            int selected = Choices.Count < Prefix.Count ? Prefix[Choices.Count] : 0;
            if (selected < 0 || selected >= size)
                throw new InvalidOperationException("SCOPE: a replay choice is outside its domain.");
            if (Choices.Count >= Prefix.Count)
            {
                for (int alternative = size - 1; alternative > 0; alternative--)
                    Pending.Push([.. Choices, alternative]);
            }
            Choices.Add(selected);
            Events.Add(operation + "=" + selected);
            return selected;
        }

        internal bool Apply(string operation, int value)
        {
            int choice = Choose(operation, 17);
            if (choice == 0)
            {
                Payload = Payload.Mutate(7, value);
                return true;
            }
            Payload = Payload.Mutate((choice - 1) % 8, value);
            if (choice >= 9)
                Throw(operation);
            return false;
        }

        internal void Throw(string operation)
        {
            DependencyFailure = new DependencyException(operation + " threw");
            throw DependencyFailure;
        }
    }

    internal static class ASMLiteBuilder
    {
        internal static bool TryDetachToDirectDelivery(ASMLiteComponent component, out string detail)
        {
            detail = "direct delivery";
            var e = BoundaryEnvironment.Current;
            e.DetachSucceeded = e.Apply("TryDetachToDirectDelivery", 1);
            return e.DetachSucceeded;
        }
    }

    internal sealed class ASMLiteGeneratedAssetMirrorResult
    {
        internal bool Success;
        internal string Message = "retarget failed";
        internal string ContextPath = "avatar";
        internal string Remediation = "restore";
    }

    internal static class ASMLiteGeneratedAssetMirrorService
    {
        internal static ASMLiteGeneratedAssetMirrorResult RetargetAvatarGeneratedAssetsToVendorized(
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar, string path)
        {
            var e = BoundaryEnvironment.Current;
            e.RetargetSucceeded = e.Apply("RetargetAvatarGeneratedAssetsToVendorized", 2);
            return new() { Success = e.RetargetSucceeded };
        }
    }

    internal static class ASMLiteLifecycleVerification
    {
        internal static bool VerifyDirectDeliveryState(ASMLiteComponent component,
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar,
            ASMLiteInstallationState state, string path, out string message, out string context)
        {
            message = "verification failed";
            context = "avatar";
            var e = BoundaryEnvironment.Current;
            int choice = e.Choose("VerifyDirectDeliveryState", 3);
            if (choice == 2)
                e.Throw("VerifyDirectDeliveryState");
            e.VerifySucceeded = choice == 0;
            return e.VerifySucceeded;
        }
    }

    internal sealed class ASMLiteDirectDeliveryRollbackSnapshot : IDisposable
    {
        private readonly AvatarPayload captured;
        private ASMLiteDirectDeliveryRollbackSnapshot()
        {
            captured = BoundaryEnvironment.Current.Payload;
            BoundaryEnvironment.Current.Events.Add("Capture");
        }

        internal static ASMLiteDirectDeliveryRollbackSnapshot Capture(
            VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar) => new();

        internal bool TryRestore(VRC.SDK3.Avatars.Components.VRCAvatarDescriptor avatar,
            out string context, out string remediation)
        {
            var e = BoundaryEnvironment.Current;
            e.RestoreCalls++;
            context = "avatar";
            remediation = "restore retained assets";
            int choice = e.Choose("TryRestore", 17);
            if (choice == 0)
            {
                e.Payload = captured;
                e.RestoreSucceeded = true;
                return true;
            }
            e.RestoreFailed = true;
            e.Payload = new AvatarPayload(
                ((choice - 1) % 8 & 1) != 0 ? captured.Menu : e.Payload.Menu,
                ((choice - 1) % 8 & 2) != 0 ? captured.Parameters : e.Payload.Parameters,
                ((choice - 1) % 8 & 4) != 0 ? captured.Fx : e.Payload.Fx);
            if (choice >= 9)
                e.Throw("TryRestore");
            return false;
        }

        public void Dispose()
        {
            var e = BoundaryEnvironment.Current;
            e.DisposeCalls++;
            e.Events.Add("Dispose");
        }
    }
}
