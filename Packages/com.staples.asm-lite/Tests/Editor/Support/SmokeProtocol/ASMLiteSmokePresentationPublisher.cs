using System;

namespace ASMLite.Tests.Editor
{
    // Timestamps describe successful publications, not animation ticks. Host heartbeat is separate.
    internal sealed class ASMLiteSmokePresentationPublisher
    {
        private string _lastPublishedSemanticJson;

        internal bool Publish(string path, string semanticJson, Func<long, string> buildPublication)
        {
            if (string.Equals(semanticJson, _lastPublishedSemanticJson, StringComparison.Ordinal))
                return false;

            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(path, buildPublication(DateTime.UtcNow.Ticks));
            _lastPublishedSemanticJson = semanticJson;
            return true;
        }
    }
}
