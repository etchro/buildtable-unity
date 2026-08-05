using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// The runtime host: owns the log buffer, the offline queue, the overlay,
    /// and the send loop. Created automatically at startup when
    /// <see cref="BuildtableConfig.autoStart"/> is on; otherwise add it to a
    /// scene yourself or call <see cref="Buildtable.Initialize"/>.
    ///
    /// Survives scene loads via DontDestroyOnLoad — a report filed in level 7
    /// must still be retried after the player returns to the menu.
    /// </summary>
    [DisallowMultipleComponent]
    public class BuildtableBugReporter : MonoBehaviour
    {
        public static BuildtableBugReporter Instance { get; private set; }

        private BuildtableConfig _config;
        private BuildtableLogBuffer _logBuffer;
        private BuildtableReportQueue _queue;
        private BuildtableBugReportOverlay _overlay;

        private bool _sending;
        private bool _capturing;
        private float _nextRetryTime;

        /// <summary>Raised after a report reaches the server. Argument is the new bug id.</summary>
        public event Action<string> ReportSubmitted;

        /// <summary>Raised when a report could not be delivered. Argument is the failure reason.</summary>
        public event Action<string> ReportFailed;

        public BuildtableConfig Config => _config;
        public bool IsOverlayOpen => _overlay != null && _overlay.IsOpen;
        public int PendingReportCount => _queue?.Count ?? 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            _config = Buildtable.Config;
            if (_config == null)
            {
                Debug.LogError(
                    "[Buildtable] No BuildtableConfig found. Create one via " +
                    "Assets > Create > Buildtable > Config and save it at " +
                    "Assets/Resources/BuildtableConfig.asset.");
                enabled = false;
                return;
            }

            if (!_config.IsUsable)
            {
                Debug.LogWarning(
                    "[Buildtable] BuildtableConfig has no ingest key or base URL. " +
                    "Reports will be queued on disk but cannot be delivered.");
            }

            _queue = new BuildtableReportQueue(_config.maxQueuedReports);

            if (_config.includeRecentLogs && _config.logBufferSize > 0)
            {
                _logBuffer = new BuildtableLogBuffer(_config.logBufferSize, _config.includeLogStackTraces);
                _logBuffer.Start();
            }
        }

        private void Start()
        {
            StartCoroutine(BuildtableBuildStamp.Load());
            // Drain anything left over from a previous session before the player
            // has done anything worth capturing.
            _nextRetryTime = Time.unscaledTime + 2f;
        }

        private void OnDestroy()
        {
            _logBuffer?.Dispose();
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_config == null) return;

            if (_config.hotkeyEnabled && BuildtableInput.GetKeyDown(_config.toggleKey))
            {
                if (IsOverlayOpen) Hide();
                else Show();
            }

            if (Time.unscaledTime >= _nextRetryTime)
            {
                _nextRetryTime = Time.unscaledTime + Mathf.Max(10, _config.retryIntervalSeconds);
                FlushQueue();
            }
        }

        /// <summary>Captures the frame behind the UI, then opens the report overlay.</summary>
        public void Show()
        {
            if (IsOverlayOpen || _capturing) return;
            StartCoroutine(ShowRoutine());
        }

        private IEnumerator ShowRoutine()
        {
            _capturing = true;
            byte[] screenshot = null;

            if (_config.captureScreenshot)
            {
                // Must happen before any overlay geometry exists, or the tester
                // sends a picture of the report form.
                yield return BuildtableScreenshot.Capture(
                    _config.maxScreenshotDimension,
                    _config.screenshotQuality,
                    bytes => screenshot = bytes);
            }

            _capturing = false;

            if (_overlay == null) _overlay = BuildtableBugReportOverlay.Create(this);
            _overlay.Open(screenshot);
        }

        public void Hide() => _overlay?.Close();

        /// <summary>
        /// Queues a report and starts delivering it. Safe to call from game code
        /// without ever showing the overlay — e.g. from your own pause menu.
        /// </summary>
        public void Submit(BuildtableReportDraft draft)
        {
            if (draft == null || string.IsNullOrWhiteSpace(draft.Title))
            {
                ReportFailed?.Invoke("A title is required.");
                return;
            }

            var description = draft.Description ?? "";
            if (draft.IncludeLogs && _logBuffer != null)
            {
                var logs = _logBuffer.Snapshot();
                if (!string.IsNullOrEmpty(logs))
                {
                    // Stopgap: the tail rides along in the description where a
                    // developer will actually see it. POST /api/v1/events already
                    // accepts structured log_batch payloads server-side — moving
                    // logs there is Milestone C's job, not this path's.
                    description = string.IsNullOrEmpty(description)
                        ? "Recent logs:\n" + logs
                        : description + "\n\n--- Recent logs ---\n" + logs;
                }
            }

            var report = new BuildtableQueuedReport
            {
                id = Guid.NewGuid().ToString("N"),
                createdAtUtcTicks = DateTime.UtcNow.Ticks,
                title = draft.Title.Trim(),
                description = description.Trim(),
                stepsToRepro = draft.StepsToRepro?.Trim(),
                priority = draft.Priority,
                reporterName = draft.ReporterName?.Trim(),
                reporterEmail = draft.ReporterEmail?.Trim(),
                versionId = BuildtableBuildStamp.Current?.buildId,
                deviceInfoJson = _config.includeDeviceInfo ? BuildtableDeviceInfo.BuildFragment() : null,
                customFields = BuildCustomFields(draft.CustomFields),
            };

            // Persist before the first attempt, not after the first failure.
            _queue.Save(report, draft.Screenshot);

            if (!string.IsNullOrEmpty(draft.ReporterName)) BuildtableInstall.ReporterName = draft.ReporterName;
            if (!string.IsNullOrEmpty(draft.ReporterEmail)) BuildtableInstall.ReporterEmail = draft.ReporterEmail;

            FlushQueue();
        }

        /// <summary>
        /// Merges the draft's fields over the defaults set with
        /// <see cref="Buildtable.SetCustomField"/>, into the flat list the queue
        /// record can serialize.
        /// </summary>
        private static List<BuildtableCustomField> BuildCustomFields(Dictionary<string, string> fromDraft)
        {
            var merged = new Dictionary<string, string>(Buildtable.DefaultCustomFields);
            if (fromDraft != null)
            {
                foreach (var pair in fromDraft) merged[pair.Key] = pair.Value;
            }

            if (merged.Count == 0) return null;

            var list = new List<BuildtableCustomField>(merged.Count);
            foreach (var pair in merged)
            {
                list.Add(new BuildtableCustomField { key = pair.Key, value = pair.Value });
            }
            return list;
        }

        /// <summary>Retries every queued report. No-op while a send is already in flight.</summary>
        public void FlushQueue()
        {
            if (_sending || _config == null || !_config.IsUsable) return;
            if (_queue.Count == 0) return;
            StartCoroutine(FlushRoutine());
        }

        private IEnumerator FlushRoutine()
        {
            _sending = true;
            try
            {
                foreach (var report in _queue.LoadAll())
                {
                    // A report that has already failed is on its own backoff
                    // schedule; the sweep interval only decides how often we look.
                    if (!report.IsDueForRetry(_config.retryIntervalSeconds)) continue;

                    var keepGoing = true;
                    yield return SendRoutine(report, transient => keepGoing = !transient);
                    // A transient failure means the network is down; the rest of
                    // the backlog would fail identically. Wait for the next sweep.
                    if (!keepGoing) break;
                }
            }
            finally
            {
                _sending = false;
            }
        }

        private IEnumerator SendRoutine(BuildtableQueuedReport report, Action<bool> onTransientFailure)
        {
            report.attempts++;
            report.lastAttemptUtcTicks = DateTime.UtcNow.Ticks;

            // Step 1 — upload the screenshot, if this report still has one and
            // hasn't already uploaded it on an earlier attempt.
            if (report.hasScreenshot && string.IsNullOrEmpty(report.attachmentPath))
            {
                var screenshot = _queue.LoadScreenshot(report);
                if (screenshot == null || screenshot.Length == 0)
                {
                    _queue.DiscardScreenshot(report);
                }
                else
                {
                    BuildtableApiResult uploadResult = default;
                    string path = null;
                    yield return BuildtableApi.UploadScreenshot(_config, screenshot, (r, p) =>
                    {
                        uploadResult = r;
                        path = p;
                    });

                    if (uploadResult.Success)
                    {
                        report.attachmentPath = path;
                        // Save immediately: if the bug POST below fails, the next
                        // attempt must reuse this path instead of re-uploading.
                        _queue.Save(report);
                    }
                    else if (uploadResult.Retryable)
                    {
                        RetryLater(report, $"Screenshot upload failed ({uploadResult})", uploadResult.ErrorMessage);
                        onTransientFailure?.Invoke(true);
                        yield break;
                    }
                    else
                    {
                        // Rejected outright (too large, wrong type, bad key
                        // scope). The written report is still worth delivering.
                        Debug.LogWarning($"[Buildtable] Screenshot rejected ({uploadResult}); sending report without it.");
                        _queue.DiscardScreenshot(report);
                    }
                }
            }

            // Step 2 — create the bug.
            BuildtableApiResult bugResult = default;
            string bugId = null;
            yield return BuildtableApi.PostBug(_config, report.ToRequestBody(), (r, id) =>
            {
                bugResult = r;
                bugId = id;
            });

            if (bugResult.Success)
            {
                _queue.Remove(report);
                ReportSubmitted?.Invoke(bugId);
                onTransientFailure?.Invoke(false);
                yield break;
            }

            if (bugResult.Retryable)
            {
                RetryLater(report, $"Report delivery failed ({bugResult})", bugResult.ErrorMessage);
                onTransientFailure?.Invoke(true);
                yield break;
            }

            // Permanent rejection — an invalid key or a payload the server will
            // never accept. Retrying forever would just burn requests.
            Debug.LogError($"[Buildtable] Report rejected and discarded ({bugResult}).");
            _queue.Remove(report);
            ReportFailed?.Invoke(bugResult.ErrorMessage);
            onTransientFailure?.Invoke(false);
        }

        /// <summary>
        /// Records a transient failure: persist the attempt count so the backoff
        /// grows, or give up once the report has burned through its attempts.
        /// Without the cap a permanently rate-limited report retries forever.
        /// </summary>
        private void RetryLater(BuildtableQueuedReport report, string context, string reason)
        {
            if (report.attempts >= Mathf.Max(1, _config.maxSendAttempts))
            {
                Debug.LogError(
                    $"[Buildtable] {context}. Giving up after {report.attempts} attempts; report discarded.");
                _queue.Remove(report);
                ReportFailed?.Invoke(reason);
                return;
            }

            _queue.Save(report);
            Debug.LogWarning($"[Buildtable] {context}; queued for retry (attempt {report.attempts}).");
            ReportFailed?.Invoke(reason);
        }
    }
}
