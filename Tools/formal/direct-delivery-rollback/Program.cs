using System.Text.Json;
using ASMLite.Editor;
using VRC.SDK3.Avatars.Components;

internal static class Program
{
    private static int Main()
    {
        long traces = 0, successes = 0, restored = 0, rollbackErrors = 0;
        foreach (var state in Enum.GetValues<ASMLiteInstallationState>())
        foreach (bool retarget in new[] { false, true })
        foreach (bool vendorizedContext in new[] { false, true })
        foreach (bool nullCallback in new[] { false, true })
        foreach (int injection in nullCallback ? new[] { 0 } : Enumerable.Range(0, 8))
        {
            var pending = new Stack<List<int>>();
            pending.Push([]);
            while (pending.Count > 0)
            {
                var e = new BoundaryEnvironment(pending.Pop(), pending);
                BoundaryEnvironment.Current = e;
                ASMLiteDirectDeliveryTransactionResult? result = null;
                Exception failure = null;
                var plan = new ASMLiteDirectDeliveryTransactionPlan(state, "Assets/ASM-Lite/avatar/GeneratedAssets",
                    retarget, state == ASMLiteInstallationState.Vendorized
                        ? ASMLiteLifecycleTransactionTestFailurePoint.DuringVendorizeDetachVerify
                        : ASMLiteLifecycleTransactionTestFailurePoint.DuringDetachVerify,
                    "restore", "after apply", "restore", "injected verify", "restore", "restore", vendorizedContext);
                Func<ASMLiteLifecycleTransactionTestFailurePoint, bool> callback = nullCallback ? null : point =>
                {
                    int bit = point switch
                    {
                        ASMLiteLifecycleTransactionTestFailurePoint.ThrowAfterDetachExecute => 1,
                        ASMLiteLifecycleTransactionTestFailurePoint.AfterDetachExecute => 2,
                        _ => 4
                    };
                    return (injection & bit) != 0;
                };
                try
                {
                    result = ASMLiteDirectDeliveryTransaction.Execute(new(), new VRCAvatarDescriptor(), plan, callback);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
                e.Events.Add("Caller observes exit");
                string violation = Check(e, result, failure, retarget);
                if (violation != null)
                {
                    Console.WriteLine(JsonSerializer.Serialize(new
                    {
                        status = "COUNTEREXAMPLE", property = violation, state = state.ToString(), retarget,
                        vendorizedContext, nullCallback, injection, choices = e.Choices, events = e.Events,
                        resultSuccess = result?.Success, exception = failure?.Message,
                        baseline = e.Baseline, observed = e.Payload
                    }));
                    return 1;
                }
                if (failure?.Message.StartsWith("SCOPE:", StringComparison.Ordinal) == true)
                {
                    Console.WriteLine(failure.Message);
                    return 2;
                }
                traces++;
                if (traces > 1_000_000)
                {
                    Console.WriteLine("SCOPE: more than one million terminal traces; review the checker.");
                    return 2;
                }
                if (result?.Success == true) successes++;
                if (e.RestoreSucceeded) restored++;
                if (e.RestoreFailed) rollbackErrors++;
            }
        }
        if (successes == 0 || restored == 0 || rollbackErrors == 0)
        {
            Console.WriteLine("SCOPE: success, restoration, and rollback-error exits must all be reached.");
            return 2;
        }
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "PASS", terminalTraces = traces, successes, restored, rollbackErrors,
            properties = new[] { "failure-restores", "success-requires-verification", "rollback-error-escapes", "snapshot-disposed" }
        }));
        return 0;
    }

    private static string Check(BoundaryEnvironment e, ASMLiteDirectDeliveryTransactionResult? result,
        Exception failure, bool retarget)
    {
        bool success = failure == null && result?.Success == true;
        if (e.DisposeCalls != 1 || e.Events[^2] != "Dispose")
            return "snapshot-disposed";
        if (success)
        {
            if (!e.DetachSucceeded || !e.VerifySucceeded || (retarget && !e.RetargetSucceeded)
                || e.RestoreCalls != 0 || e.Payload != new AvatarPayload(retarget ? 2 : 1, retarget ? 2 : 1, retarget ? 2 : 1))
                return "success-requires-verification";
        }
        else
        {
            if (e.RestoreCalls != 1 || (!e.RestoreFailed && (!e.RestoreSucceeded || e.Payload != e.Baseline)))
                return "failure-restores";
            if (e.RestoreFailed)
            {
                bool restoreThrew = e.DependencyFailure?.Message == "TryRestore threw";
                if (failure == null || (restoreThrew
                    ? !ReferenceEquals(failure, e.DependencyFailure)
                    : !failure.Message.StartsWith("Direct-delivery rollback failed at '", StringComparison.Ordinal)))
                    return "rollback-error-escapes";
            }
            if (e.RestoreSucceeded && e.DependencyFailure != null && !ReferenceEquals(failure, e.DependencyFailure))
                return "failure-restores";
        }
        return null;
    }
}
