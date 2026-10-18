using System;
using static ASMLite.Editor.ASMLiteWindow;

namespace ASMLite.Editor
{
    // Optional test-assembly callbacks only; the product window owns no smoke state or I/O.
    internal sealed class ASMLiteWindowSmokeCallbacks
    {
        internal Action<string, string> ConfigureExternalOverlay;
        internal Action<string, string, int, int, VisibleAutomationOverlayState, bool, string[]> SetOverlayStatus;
        internal Action ClearOverlay;
        internal Action<string, string> ShowCompletionReview;
        internal Action AcknowledgeCompletionReview;
        internal Func<bool> IsCompletionReviewVisible;
        internal Func<bool> WasCompletionReviewAcknowledged;
        internal Func<VisibleAutomationOverlayHostSnapshot> GetHostSnapshot;
        internal Func<VisibleAutomationExternalOverlayStateDocument> ReadExternalState;
        internal Func<bool> ConsumeExternalAcknowledgement;
        internal Action Refresh;
        internal Action Dispose;
    }
}
