using System;
using System.IO;
using NUnit.Framework;

namespace ASMLite.Tests.Editor
{
    [TestFixture]
    [Category("Smoke")]
    [Category("Headless")]
    public class ASMLiteSmokePresentationPublisherTests
    {
        [Test]
        public void Publication_UnchangedStateKeepsSuccessfulDocumentAndTimestamp()
        {
            WithStatePath(path =>
            {
                var publisher = new ASMLiteSmokePresentationPublisher();
                Assert.IsTrue(publisher.Publish(path, "running", ticks => "{\"step\":1,\"updatedUtcTicks\":" + ticks + "}"));
                string baseline = File.ReadAllText(path);
                var marker = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(path, marker);

                Assert.IsFalse(publisher.Publish(path, "running", ticks => throw new AssertionException("Unchanged state must not be serialized again.")));

                Assert.AreEqual(baseline, File.ReadAllText(path));
                Assert.AreEqual(marker, File.GetLastWriteTimeUtc(path));
                Assert.IsTrue(publisher.Publish(path, "complete", ticks => "{\"step\":2,\"updatedUtcTicks\":" + ticks + "}"));
                StringAssert.Contains("\"step\":2", File.ReadAllText(path));
            });
        }

        [Test]
        public void Publication_FailedPromotionRetainsPreviousDocumentAndRetriesSameState()
        {
            WithStatePath(path =>
            {
                var publisher = new ASMLiteSmokePresentationPublisher();
                Assert.IsTrue(publisher.Publish(path, "running", ticks => "{\"step\":1}"));
                string baseline = File.ReadAllText(path);
                using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    Assert.Throws<IOException>(() => publisher.Publish(path, "complete", ticks => "{\"step\":2}"));
                    Assert.AreEqual(baseline, File.ReadAllText(path));
                }

                Assert.IsTrue(publisher.Publish(path, "complete", ticks => "{\"step\":2}"));
                Assert.AreEqual("{\"step\":2}", File.ReadAllText(path));
                Assert.IsFalse(publisher.Publish(path, "complete", ticks => "unexpected rewrite"));
            });
        }

        [Test]
        public void Publication_FailedFirstWriteIsNotRecordedAsPublished()
        {
            WithStatePath(path =>
            {
                var publisher = new ASMLiteSmokePresentationPublisher();
                Directory.CreateDirectory(path);
                Assert.Throws<IOException>(() => publisher.Publish(path, "running", ticks => "{\"step\":1}"));
                Directory.Delete(path);

                Assert.IsTrue(publisher.Publish(path, "running", ticks => "{\"step\":1}"));
                Assert.AreEqual("{\"step\":1}", File.ReadAllText(path));
            });
        }

        private static void WithStatePath(Action<string> check)
        {
            string directory = Path.Combine(Path.GetTempPath(), "asmlite-presentation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try { check(Path.Combine(directory, "state.json")); }
            finally { Directory.Delete(directory, true); }
        }
    }
}
