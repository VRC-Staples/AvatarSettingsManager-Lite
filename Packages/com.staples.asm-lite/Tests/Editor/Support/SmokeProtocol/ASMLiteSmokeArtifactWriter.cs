using System;
using System.Collections.Generic;
using System.IO;

namespace ASMLite.Tests.Editor
{
    internal sealed class ASMLiteSmokeArtifactWriter
    {
        private readonly ASMLiteSmokeSessionPaths _paths;
        private readonly Func<string, string> _makeSessionRelativePath;

        internal ASMLiteSmokeArtifactWriter(ASMLiteSmokeSessionPaths paths, Func<string, string> makeSessionRelativePath)
        {
            _paths = paths ?? throw new InvalidOperationException("Smoke session paths are required.");
            _makeSessionRelativePath = makeSessionRelativePath ?? throw new InvalidOperationException("Session-relative path resolver is required.");
        }

        internal ASMLiteSmokeArtifactReferences PrepareRunArtifacts(
            int runOrdinal,
            string suiteId,
            IEnumerable<ASMLiteSmokeProtocolEvent> runEvents,
            string nunitXml,
            bool writeFailureArtifact)
        {
            string runDirectoryPath = _paths.GetRunDirectoryPath(runOrdinal, suiteId);
            Directory.CreateDirectory(runDirectoryPath);

            string resultPath = _paths.GetResultPath(runOrdinal, suiteId);
            string failurePath = _paths.GetFailurePath(runOrdinal, suiteId);
            string eventsSlicePath = _paths.GetEventsSlicePath(runOrdinal, suiteId);
            string nunitPath = _paths.GetNUnitPath(runOrdinal, suiteId);

            string sliceNdjson = ASMLiteSmokeProtocol.ToNdjson(runEvents ?? Array.Empty<ASMLiteSmokeProtocolEvent>());
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(eventsSlicePath, sliceNdjson);
            ASMLiteSmokeAtomicFileIo.WriteJsonAtomically(nunitPath, nunitXml ?? string.Empty);

            return new ASMLiteSmokeArtifactReferences
            {
                resultPath = _makeSessionRelativePath(resultPath),
                failurePath = writeFailureArtifact ? _makeSessionRelativePath(failurePath) : string.Empty,
                eventsSlicePath = _makeSessionRelativePath(eventsSlicePath),
                nunitPath = _makeSessionRelativePath(nunitPath),
                debugSummaryPath = string.Empty
            };
        }
    }
}
