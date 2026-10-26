using System;
using System.IO;
using NUnit.Framework;

namespace BuildtableSdk.Tests
{
    public class ReportQueueTests
    {
        private string _directory;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "buildtable-tests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
        }

        private static BuildtableQueuedReport Report(string id, int ageSeconds = 0) => new BuildtableQueuedReport
        {
            id = id,
            createdAtUtcTicks = DateTime.UtcNow.AddSeconds(-ageSeconds).Ticks,
            title = "Title " + id,
        };

        [Test]
        public void ZeroCapStillKeepsOneReport()
        {
            var queue = new BuildtableReportQueue(0, _directory);

            Assert.IsTrue(queue.Save(Report("a")));
            Assert.AreEqual(1, queue.Count);
        }

        [Test]
        public void DropsOldestPastCap()
        {
            var queue = new BuildtableReportQueue(2, _directory);

            queue.Save(Report("oldest", 30));
            queue.Save(Report("middle", 20));
            queue.Save(Report("newest", 10));

            var remaining = queue.LoadAll();
            Assert.AreEqual(2, remaining.Count);
            Assert.AreEqual("middle", remaining[0].id);
            Assert.AreEqual("newest", remaining[1].id);
        }

        [Test]
        public void LoadsOldestFirst()
        {
            var queue = new BuildtableReportQueue(10, _directory);

            queue.Save(Report("new", 1));
            queue.Save(Report("old", 100));

            var reports = queue.LoadAll();
            Assert.AreEqual("old", reports[0].id);
            Assert.AreEqual("new", reports[1].id);
        }

        [Test]
        public void ScreenshotRoundTrips()
        {
            var queue = new BuildtableReportQueue(10, _directory);
            var bytes = new byte[] { 1, 2, 3, 4 };

            queue.Save(Report("a"), bytes);

            var report = queue.LoadAll()[0];
            Assert.IsTrue(report.hasScreenshot);
            CollectionAssert.AreEqual(bytes, queue.LoadScreenshot(report));
        }

        [Test]
        public void DiscardScreenshotKeepsReport()
        {
            var queue = new BuildtableReportQueue(10, _directory);
            queue.Save(Report("a"), new byte[] { 1 });
            var report = queue.LoadAll()[0];

            queue.DiscardScreenshot(report);

            var reloaded = queue.LoadAll();
            Assert.AreEqual(1, reloaded.Count);
            Assert.IsFalse(reloaded[0].hasScreenshot);
            Assert.IsNull(queue.LoadScreenshot(reloaded[0]));
        }

        [Test]
        public void RemoveDeletesRecordAndScreenshot()
        {
            var queue = new BuildtableReportQueue(10, _directory);
            queue.Save(Report("a"), new byte[] { 1 });

            queue.Remove(queue.LoadAll()[0]);

            Assert.AreEqual(0, queue.Count);
            Assert.AreEqual(0, Directory.GetFiles(_directory).Length);
        }

        [Test]
        public void UnreadableRecordIsDiscarded()
        {
            var queue = new BuildtableReportQueue(10, _directory);
            queue.Save(Report("good"));
            File.WriteAllText(Path.Combine(_directory, "broken.json"), "{}");

            var reports = queue.LoadAll();

            Assert.AreEqual(1, reports.Count);
            Assert.AreEqual("good", reports[0].id);
            Assert.IsFalse(File.Exists(Path.Combine(_directory, "broken.json")));
        }

        [Test]
        public void MissingDirectoryReadsAsEmpty()
        {
            var queue = new BuildtableReportQueue(10, _directory);

            Assert.AreEqual(0, queue.Count);
            Assert.IsEmpty(queue.LoadAll());
        }

        [Test]
        public void RetryBackoffDoublesPerAttempt()
        {
            var report = Report("a");
            Assert.IsTrue(report.IsDueForRetry(60), "never attempted");

            report.attempts = 1;
            report.lastAttemptUtcTicks = DateTime.UtcNow.AddSeconds(-30).Ticks;
            Assert.IsFalse(report.IsDueForRetry(60), "first retry waits one interval");

            report.lastAttemptUtcTicks = DateTime.UtcNow.AddSeconds(-61).Ticks;
            Assert.IsTrue(report.IsDueForRetry(60));

            report.attempts = 3;
            report.lastAttemptUtcTicks = DateTime.UtcNow.AddSeconds(-200).Ticks;
            Assert.IsFalse(report.IsDueForRetry(60), "third retry waits four intervals");
        }

        [Test]
        public void RetryBackoffCapsAtAnHour()
        {
            var report = Report("a");
            report.attempts = 50;
            report.lastAttemptUtcTicks = DateTime.UtcNow.AddSeconds(-3601).Ticks;

            Assert.IsTrue(report.IsDueForRetry(600));
        }
    }
}
