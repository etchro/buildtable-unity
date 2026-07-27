using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Durable store for reports that haven't reached the server yet.
    ///
    /// Every report is written here *before* the first send attempt, not only
    /// after a failure — a tester who alt-F4s the instant they hit submit still
    /// gets their report delivered on the next launch. Each report is a pair of
    /// files under <c>persistentDataPath/buildtable/pending</c>:
    /// <c>&lt;id&gt;.json</c> (the record) and optionally <c>&lt;id&gt;.jpg</c>
    /// (the screenshot, kept out of the JSON so a queued report doesn't cost
    /// megabytes of base64).
    ///
    /// All disk access is defensive: a full disk or a locked file must degrade
    /// to "this report isn't durable", never to an exception in the middle of a
    /// tester's session.
    /// </summary>
    public class BuildtableReportQueue
    {
        private readonly string _directory;
        private readonly int _maxReports;

        public BuildtableReportQueue(int maxReports)
        {
            _maxReports = Mathf.Max(0, maxReports);
            _directory = Path.Combine(Application.persistentDataPath, "buildtable", "pending");
        }

        public string Directory => _directory;

        private string RecordPath(string id) => Path.Combine(_directory, id + ".json");
        private string ScreenshotPath(string id) => Path.Combine(_directory, id + ".jpg");

        /// <summary>Persists a report and its screenshot. Returns false if nothing could be written.</summary>
        public bool Save(BuildtableQueuedReport report, byte[] screenshot = null)
        {
            if (_maxReports == 0) return false;

            try
            {
                System.IO.Directory.CreateDirectory(_directory);

                if (screenshot != null && screenshot.Length > 0)
                {
                    File.WriteAllBytes(ScreenshotPath(report.id), screenshot);
                    report.hasScreenshot = true;
                }

                // Written last so a half-written pair is never picked up: the
                // record is what makes a report visible to the queue.
                File.WriteAllText(RecordPath(report.id), JsonUtility.ToJson(report));
                Trim();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Buildtable] Could not queue report to disk: {e.Message}");
                return false;
            }
        }

        /// <summary>Oldest first, so a backlog drains in the order it was filed.</summary>
        public List<BuildtableQueuedReport> LoadAll()
        {
            var reports = new List<BuildtableQueuedReport>();
            try
            {
                if (!System.IO.Directory.Exists(_directory)) return reports;

                foreach (var file in System.IO.Directory.GetFiles(_directory, "*.json"))
                {
                    BuildtableQueuedReport report = null;
                    try
                    {
                        report = JsonUtility.FromJson<BuildtableQueuedReport>(File.ReadAllText(file));
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Buildtable] Discarding unreadable queued report {Path.GetFileName(file)}: {e.Message}");
                    }

                    if (report == null || string.IsNullOrEmpty(report.id))
                    {
                        TryDeleteFile(file);
                        continue;
                    }
                    reports.Add(report);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Buildtable] Could not read the report queue: {e.Message}");
            }

            reports.Sort((a, b) => a.createdAtUtcTicks.CompareTo(b.createdAtUtcTicks));
            return reports;
        }

        public byte[] LoadScreenshot(BuildtableQueuedReport report)
        {
            if (!report.hasScreenshot) return null;
            try
            {
                var path = ScreenshotPath(report.id);
                return File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Buildtable] Could not read queued screenshot: {e.Message}");
                return null;
            }
        }

        /// <summary>Drops the screenshot but keeps the report — used when an upload fails permanently.</summary>
        public void DiscardScreenshot(BuildtableQueuedReport report)
        {
            report.hasScreenshot = false;
            TryDeleteFile(ScreenshotPath(report.id));
            Save(report);
        }

        public void Remove(BuildtableQueuedReport report)
        {
            TryDeleteFile(RecordPath(report.id));
            TryDeleteFile(ScreenshotPath(report.id));
        }

        public int Count
        {
            get
            {
                try
                {
                    return System.IO.Directory.Exists(_directory)
                        ? System.IO.Directory.GetFiles(_directory, "*.json").Length
                        : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>
        /// Enforces the cap by dropping the oldest reports. A bounded queue is
        /// the point: a build pointed at a dead server must not fill a player's
        /// disk with screenshots.
        /// </summary>
        private void Trim()
        {
            var reports = LoadAll();
            for (var i = 0; i < reports.Count - _maxReports; i++)
            {
                Debug.LogWarning($"[Buildtable] Report queue full ({_maxReports}); dropping the oldest report.");
                Remove(reports[i]);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) { Debug.LogWarning($"[Buildtable] Could not delete {path}: {e.Message}"); }
        }
    }
}
