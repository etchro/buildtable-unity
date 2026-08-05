using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Threading;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Batches console output and custom events and ships them to
    /// <c>POST /api/v1/events</c>. Owned by <see cref="BuildtableBugReporter"/>,
    /// which drives <see cref="Tick"/> from its own Update.
    ///
    /// Two things distinguish this from the bug queue next door:
    ///
    /// <list type="bullet">
    /// <item>Nothing here is durable. A batch that fails to send is dropped, not
    /// written to disk. Bug reports are precious; logs are cheap and the server
    /// deletes them after 7 days. Persisting them is how a dead server ends up
    /// filling a player's disk with telemetry. The single exception is a crash
    /// event, which gets one slot on disk because the process is often about to
    /// die and it's the one log that matters.</item>
    /// <item>It is gated on <see cref="Debug.isDebugBuild"/>. A shipped release
    /// build cannot forward logs unless a second, separately named flag is also
    /// set — see <see cref="BuildtableConfig.forwardLogsInReleaseBuilds"/>.</item>
    /// </list>
    /// </summary>
    public sealed class BuildtableTelemetry : IDisposable
    {
        /// <summary>Contract cap: the server rejects a request with more than 100 events.</summary>
        private const int MaxEventsPerRequest = 5;

        /// <summary>Contract target: stay well inside whatever byte cap the server adopts.</summary>
        private const int MaxBytesPerEvent = 128 * 1024;

        /// <summary>
        /// Ceiling on serialized events waiting to go out. Reached only when the
        /// server is unreachable, and the oldest are dropped — logs are not worth
        /// unbounded memory.
        /// </summary>
        private const int MaxPendingEvents = 20;

        /// <summary>Backpressure ceiling: an offline game retries at most every 5 minutes.</summary>
        private const float MaxFlushIntervalSeconds = 300f;

        /// <summary>Floor between buffer-pressure flushes, matching the config's minimum interval.</summary>
        private const float MinPressureFlushGapSeconds = 5f;

        private readonly BuildtableConfig _config;
        private readonly MonoBehaviour _host;
        private readonly BuildtableLogBuffer _buffer;
        private readonly List<BuildtableLogEntry> _drained = new List<BuildtableLogEntry>();
        private readonly List<PendingEvent> _pending = new List<PendingEvent>();

        private float _baseInterval;
        private float _currentInterval;
        private float _nextFlushTime;
        private float _nextPressureFlushTime;
        private bool _sending;
        private bool _disabled;
        private int _flushRequested;

        private struct PendingEvent
        {
            public string Json;
            /// <summary>Crash events are the only ones worth surviving a restart.</summary>
            public bool IsCrash;
        }

        /// <summary>
        /// True when console output is actually being captured — i.e. the config
        /// asked for it <em>and</em> the debug-build gate is open.
        /// </summary>
        public bool LogForwardingActive { get; private set; }

        public BuildtableTelemetry(BuildtableConfig config, MonoBehaviour host)
        {
            _config = config;
            _host = host;

            _baseInterval = Mathf.Clamp(config.logFlushIntervalSeconds, 5, 300);
            _currentInterval = _baseInterval;
            _nextFlushTime = Time.unscaledTime + _baseInterval;

            // Evaluated once. If the gate is closed we never subscribe to the log
            // callback at all — no buffer, no allocation, no per-line cost.
            LogForwardingActive = config.forwardLogs && (Debug.isDebugBuild || config.forwardLogsInReleaseBuilds);

            if (LogForwardingActive)
            {
                _buffer = new BuildtableLogBuffer(
                    Mathf.Max(10, config.maxBufferedLines),
                    config.forwardStackTraces,
                    config.minimumForwardedLevel);
                // Fires off the main thread; all it may do is set the flag.
                _buffer.ExceptionLogged = () => Interlocked.Exchange(ref _flushRequested, 1);
                _buffer.Start();
            }

            RecoverPersistedCrash();
        }

        public void Dispose()
        {
            _buffer?.Dispose();
        }

        /// <summary>Console lines captured but not yet sent. Zero when forwarding is off.</summary>
        public int BufferedLineCount => _buffer?.Count ?? 0;

        /// <summary>
        /// Records a custom gameplay event. Deliberately <em>not</em> gated on the
        /// debug-build check: a developer calling this explicitly has made a
        /// decision, which is not the same as ambient log capture.
        /// </summary>
        public void LogEvent(string name, Dictionary<string, string> fields)
        {
            if (_disabled || string.IsNullOrEmpty(name)) return;

            var json = BuildtableJson.Object();
            json.String("type", "custom");
            // Server cap; truncating beats a 400 that discards the whole batch.
            json.String("name", name.Length > 200 ? name.Substring(0, 200) : name);
            json.String("occurredAt", Iso8601(DateTime.UtcNow));
            json.Raw("deviceInfo", BuildtableDeviceInfo.BuildCompactFragment());

            json.BeginObject("payload");
            if (fields != null)
            {
                foreach (var pair in fields)
                {
                    if (string.IsNullOrEmpty(pair.Key)) continue;
                    json.String(pair.Key, pair.Value);
                }
            }
            json.EndObject();

            Enqueue(new PendingEvent { Json = json.EndObject().ToString() });
            Interlocked.Exchange(ref _flushRequested, 1);
        }

        /// <summary>Drains the buffer and sends everything now, if anything is waiting.</summary>
        public void Flush()
        {
            Interlocked.Exchange(ref _flushRequested, 1);
            Pump();
        }

        /// <summary>Called every frame by the host. Cheap when there's nothing to do.</summary>
        public void Tick()
        {
            if (_disabled || _sending) return;

            var forced = Interlocked.Exchange(ref _flushRequested, 0) == 1;
            var timerDue = Time.unscaledTime >= _nextFlushTime;

            // Buffer pressure: a batch's worth has piled up, so send it rather
            // than let the ring start dropping lines while the timer runs down.
            // Rate-limited on its own floor — a game logging faster than the
            // network can carry must drop lines, not fire a request per frame.
            var full = _buffer != null
                && _buffer.Count >= Mathf.Clamp(_config.maxLinesPerBatch, 10, 200)
                && Time.unscaledTime >= _nextPressureFlushTime;

            if (!forced && !timerDue && !full) return;

            var now = Time.unscaledTime;
            _nextFlushTime = now + _currentInterval;
            _nextPressureFlushTime = now + MinPressureFlushGapSeconds;
            Pump();
        }

        /// <summary>
        /// Last chance on mobile, where the process may never be resumed. Drains
        /// into the pending list and persists a crash if one is waiting, then
        /// tries a send that may or may not get to finish.
        /// </summary>
        public void FlushBeforeSuspend()
        {
            if (_disabled) return;

            DrainIntoPending();
            PersistCrashIfAny();

            // On quit the coroutine will usually be killed mid-request. Starting
            // it is still worth it: on pause, and on desktop quit, it often lands.
            if (_host != null && _host.isActiveAndEnabled) Pump();
        }

        // ── Draining ──────────────────────────────────────────────────────────

        private void Pump()
        {
            if (_disabled || _sending) return;

            DrainIntoPending();
            if (_pending.Count == 0) return;
            if (_config == null || !_config.IsUsable) return;
            if (_host == null || !_host.isActiveAndEnabled) return;

            _host.StartCoroutine(SendRoutine());
        }

        /// <summary>
        /// Turns buffered lines into serialized events. Exceptions become their
        /// own <c>crash</c> event so the viewer can surface them without parsing
        /// a batch; everything else is chunked into <c>log_batch</c> events
        /// bounded by both line count and bytes.
        /// </summary>
        private void DrainIntoPending()
        {
            if (_buffer == null) return;

            var dropped = _buffer.DroppedSinceDrain;
            _drained.Clear();
            if (_buffer.Drain(_drained) == 0) return;

            var maxLines = Mathf.Clamp(_config.maxLinesPerBatch, 10, 200);
            var batch = new List<BuildtableLogEntry>(maxLines);
            var bytes = 0;

            foreach (var entry in _drained)
            {
                if (entry.Type == LogType.Exception)
                {
                    // Flush whatever preceded it first, so the ordering the
                    // viewer sees matches the ordering the game produced.
                    if (batch.Count > 0)
                    {
                        Enqueue(BuildLogBatch(batch, ref dropped));
                        batch.Clear();
                        bytes = 0;
                    }

                    Enqueue(BuildCrash(entry));
                    continue;
                }

                var size = EstimateSize(entry);
                if (batch.Count > 0 && (batch.Count >= maxLines || bytes + size > MaxBytesPerEvent))
                {
                    Enqueue(BuildLogBatch(batch, ref dropped));
                    batch.Clear();
                    bytes = 0;
                }

                batch.Add(entry);
                bytes += size;
            }

            if (batch.Count > 0) Enqueue(BuildLogBatch(batch, ref dropped));
            _drained.Clear();
        }

        private PendingEvent BuildLogBatch(List<BuildtableLogEntry> lines, ref int dropped)
        {
            var json = BuildtableJson.Object();
            json.String("type", "log_batch");
            // The batch is stamped with its newest line, not "now" — a batch sent
            // after a 5-minute backoff still describes when it happened.
            json.String("occurredAt", Iso8601(lines[lines.Count - 1].TimeUtc));
            json.Raw("deviceInfo", BuildtableDeviceInfo.BuildCompactFragment());

            json.BeginObject("payload");
            if (dropped > 0)
            {
                // Say so rather than let a gap look like quiet. Reported once,
                // on the first batch of the drain that observed it.
                json.Number("droppedLines", dropped);
                dropped = 0;
            }
            json.BeginArray("lines");
            foreach (var line in lines) WriteLine(json, line);
            json.EndArray();
            json.EndObject();

            return new PendingEvent { Json = json.EndObject().ToString() };
        }

        private PendingEvent BuildCrash(BuildtableLogEntry entry)
        {
            var json = BuildtableJson.Object();
            json.String("type", "crash");
            json.String("occurredAt", Iso8601(entry.TimeUtc));
            json.Raw("deviceInfo", BuildtableDeviceInfo.BuildCompactFragment());

            json.BeginObject("payload");
            json.BeginArray("lines");
            WriteLine(json, entry);
            json.EndArray();
            json.EndObject();

            return new PendingEvent { Json = json.EndObject().ToString(), IsCrash = true };
        }

        private static void WriteLine(BuildtableJson json, BuildtableLogEntry entry)
        {
            json.BeginObject();
            json.String("t", Iso8601(entry.TimeUtc));
            // Unity's LogType names verbatim — Log, Warning, Error, Assert, Exception.
            json.String("level", entry.Type.ToString());
            json.String("message", entry.Message ?? "");
            json.StringIfSet("stack", entry.StackTrace);
            json.EndObject();
        }

        private static int EstimateSize(BuildtableLogEntry entry)
        {
            // Rough: enough to keep a batch inside the byte ceiling without
            // serializing twice to find out.
            var size = 80;
            if (entry.Message != null) size += entry.Message.Length;
            if (entry.StackTrace != null) size += entry.StackTrace.Length;
            return size;
        }

        private void Enqueue(PendingEvent evt)
        {
            _pending.Add(evt);
            while (_pending.Count > MaxPendingEvents)
            {
                // Drop the oldest non-crash event. A crash outlives ordinary
                // lines; if the list is somehow all crashes, the oldest goes.
                var victim = _pending.FindIndex(e => !e.IsCrash);
                _pending.RemoveAt(victim >= 0 ? victim : 0);
            }
        }

        // ── Sending ───────────────────────────────────────────────────────────

        private IEnumerator SendRoutine()
        {
            _sending = true;
            try
            {
                while (_pending.Count > 0)
                {
                    var take = Mathf.Min(MaxEventsPerRequest, _pending.Count);
                    var batch = _pending.GetRange(0, take);
                    var hadCrash = false;

                    var body = new BuildtableJson();
                    body.BeginObject();
                    body.BeginArray("events");
                    foreach (var evt in batch)
                    {
                        body.RawElement(evt.Json);
                        if (evt.IsCrash) hadCrash = true;
                    }
                    body.EndArray();
                    body.EndObject();

                    if (hadCrash) PersistCrashIfAny();

                    BuildtableApiResult result = default;
                    yield return BuildtableApi.PostEvents(_config, body.ToString(), (r, _) => result = r);

                    // Sent or rejected, the batch is gone either way: logs are not
                    // retried. The only question is what happens next.
                    _pending.RemoveRange(0, take);

                    if (result.Success)
                    {
                        _currentInterval = _baseInterval;
                        if (hadCrash) ClearPersistedCrash();
                        continue;
                    }

                    if (!result.Retryable && (result.StatusCode == 401 || result.StatusCode == 403))
                    {
                        // A key the server won't accept fails identically forever.
                        // Keep quiet for the rest of the session rather than spend
                        // a request every 30 seconds proving it.
                        _disabled = true;
                        _buffer?.Stop();
                        _pending.Clear();
                        Debug.LogWarning($"[Buildtable] Event forwarding disabled for this session ({result}).");
                        yield break;
                    }

                    // Back off and stop for now — the rest of the queue would fail
                    // the same way, and a game in an error loop with no network
                    // must not spend its frame budget on failing requests.
                    _currentInterval = Mathf.Min(_currentInterval * 2f, MaxFlushIntervalSeconds);
                    _nextFlushTime = Time.unscaledTime + _currentInterval;
                    yield break;
                }
            }
            finally
            {
                _sending = false;
            }
        }

        // ── The one durable slot ──────────────────────────────────────────────

        private static string CrashSlotPath =>
            Path.Combine(Application.persistentDataPath, "buildtable", "crash.json");

        /// <summary>
        /// Writes the newest pending crash to a single reusable slot. One slot,
        /// overwritten — a crash loop must not be able to grow a file per crash.
        /// </summary>
        private void PersistCrashIfAny()
        {
            try
            {
                string newest = null;
                for (var i = _pending.Count - 1; i >= 0; i--)
                {
                    if (!_pending[i].IsCrash) continue;
                    newest = _pending[i].Json;
                    break;
                }
                if (newest == null) return;

                var path = CrashSlotPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, newest);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Buildtable] Could not persist crash event: {e.Message}");
            }
        }

        private void RecoverPersistedCrash()
        {
            try
            {
                var path = CrashSlotPath;
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                // Delete before enqueuing: a crash that can't be delivered gets
                // exactly one extra attempt, never a permanent retry on boot.
                File.Delete(path);

                if (!string.IsNullOrEmpty(json) && json.Length <= MaxBytesPerEvent)
                {
                    Enqueue(new PendingEvent { Json = json, IsCrash = true });
                    Interlocked.Exchange(ref _flushRequested, 1);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Buildtable] Could not recover crash event: {e.Message}");
            }
        }

        private void ClearPersistedCrash()
        {
            try
            {
                var path = CrashSlotPath;
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // A stale slot costs one duplicate crash event next launch.
            }
        }

        private static string Iso8601(DateTime utc) =>
            utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
    }
}
