using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    public class ASMLiteSmokeArtifactPathsTests
    {
        [Test]
        public void SessionLayout_paths_follow_canonical_contract()
        {
            string sessionRoot = Path.Combine(Path.GetTempPath(), "asmlite-smoke-artifacts", Guid.NewGuid().ToString("N"));
            var sessionPaths = ASMLiteSmokeArtifactPaths.FromSessionRoot(sessionRoot);

            sessionPaths.EnsureSessionLayout();

            Assert.That(Directory.Exists(sessionPaths.CommandsDirectoryPath), Is.True);
            Assert.That(Directory.Exists(sessionPaths.EventsDirectoryPath), Is.True);
            Assert.That(Directory.Exists(sessionPaths.RunsDirectoryPath), Is.True);
            Assert.That(Path.GetFileName(sessionPaths.SessionMetadataPath), Is.EqualTo("session.json"));
            Assert.That(Path.GetFileName(sessionPaths.CatalogSnapshotPath), Is.EqualTo("suite-catalog.snapshot.json"));
            Assert.That(Path.GetFileName(sessionPaths.EventsLogPath), Is.EqualTo("events.ndjson"));
            Assert.That(Path.GetFileName(sessionPaths.HostStatePath), Is.EqualTo("host-state.json"));
        }

        [Test]
        public void Naming_is_lexically_sortable_ascii_and_windows_safe()
        {
            string sessionRoot = Path.Combine(Path.GetTempPath(), "asmlite-smoke-artifacts", Guid.NewGuid().ToString("N"));
            var sessionPaths = ASMLiteSmokeArtifactPaths.FromSessionRoot(sessionRoot);

            string commandA = sessionPaths.BuildCommandFileName(2, "run-suite", "cmd_000002_run-suite");
            string commandB = sessionPaths.BuildCommandFileName(11, "run-suite", "cmd_000011_run-suite");
            Assert.That(string.CompareOrdinal(commandA, commandB), Is.LessThan(0));

            string runDirA = sessionPaths.BuildRunDirectoryName(2, "lifecycle-roundtrip");
            string runDirB = sessionPaths.BuildRunDirectoryName(11, "playmode-runtime-validation");
            Assert.That(string.CompareOrdinal(runDirA, runDirB), Is.LessThan(0));

            ASMLiteSmokeSessionPaths.ValidatePortablePathSegment(commandA, nameof(commandA));
            ASMLiteSmokeSessionPaths.ValidatePortablePathSegment(commandB, nameof(commandB));
            ASMLiteSmokeSessionPaths.ValidatePortablePathSegment(runDirA, nameof(runDirA));
            ASMLiteSmokeSessionPaths.ValidatePortablePathSegment(runDirB, nameof(runDirB));
        }

        [Test]
        public void Result_and_failure_fixtures_keep_relative_artifacts_under_session_root()
        {
            var resultDocument = ASMLiteSmokeArtifactPaths.LoadResultFixture("result.sample.json");
            var failureDocument = ASMLiteSmokeArtifactPaths.LoadFailureFixture("failure.sample.json");
            var eventsSlice = ASMLiteSmokeProtocol.LoadEventFixture("events.slice.sample.ndjson");

            Assert.That(eventsSlice.Length, Is.GreaterThan(0));
            Assert.That(eventsSlice.First().eventSeq, Is.EqualTo(12));

            string sessionRoot = Path.Combine(Path.GetTempPath(), "asmlite-smoke-artifacts", Guid.NewGuid().ToString("N"));
            var sessionPaths = ASMLiteSmokeArtifactPaths.FromSessionRoot(sessionRoot);
            sessionPaths.EnsureSessionLayout();

            string resultSlicePath = sessionPaths.ResolveSessionRelativePath(resultDocument.artifactPaths.eventsSlicePath, "result.artifactPaths.eventsSlicePath");
            string failureSlicePath = sessionPaths.ResolveSessionRelativePath(failureDocument.artifactPaths.eventsSlicePath, "failure.artifactPaths.eventsSlicePath");

            string rootWithSeparator = sessionPaths.SessionRootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? sessionPaths.SessionRootPath
                : sessionPaths.SessionRootPath + Path.DirectorySeparatorChar;
            Assert.That(resultSlicePath.StartsWith(rootWithSeparator, StringComparison.Ordinal), Is.True);
            Assert.That(failureSlicePath.StartsWith(rootWithSeparator, StringComparison.Ordinal), Is.True);
        }
    }
}
