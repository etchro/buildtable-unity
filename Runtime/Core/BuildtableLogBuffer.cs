using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Severity ladder for <see cref="BuildtableConfig.minimumForwardedLevel"/>.
    ///
    /// Unity's own <see cref="LogType"/> is declared in an order that has nothing
    /// to do with severity (Error is 0, Log is 3), so it can't be compared with
    /// <c>&gt;=</c> and reads as nonsense in an inspector dropdown labelled
    /// "minimum". This enum is declared in severity order so both work.
    /// <c>Assert</c> maps onto <see cref="Error"/>.
    /// </summary>
    public enum BuildtableLogLevel
    {
        /// <summary>Everything, including ordinary Debug.Log calls.</summary>
        Log = 0,
        Warning = 1,
        /// <summary>Debug.LogError and Debug.Assert failures.</summary>
        Error = 2,
        /// <summary>Uncaught exceptions only.</summary>
        Exception = 3,
    }

    /// <summary>One captured console line, kept structured because it ends up as JSON.</summary>
    public struct BuildtableLogEntry
    {
        public DateTime TimeUtc;
        public LogType Type;
        public string Message;
        public string StackTrace;
    }

    /// <summary>
    /// Fixed-size ring of recent console lines, fed by
    /// <see cref="Application.logMessageReceivedThreaded"/>. Nothing in the game
    /// has to call anything: subscribing to that callback captures every
    /// <c>Debug.Log*</c> and every uncaught exception in the process, including
    /// ones raised inside third-party packages.
    ///
    /// Threading: the callback fires off the main thread (jobs, background
    /// threads, the loading thread), so writes are locked and messages are
    /// truncated at capture time. Capacity is bounded, so a runaway error loop
    /// costs a fixed amount of memory — the oldest lines are dropped, and
    /// <see cref="DroppedSinceDrain"/> records how many.
    /// </summary>
    public sealed class BuildtableLogBuffer : IDisposable
    {
        /// <summary>A single line is never worth more than this; stack traces get their own budget.</summary>
        private const int MaxMessageCharacters = 4000;
        private const int MaxStackCharacters = 4000;

        private readonly object _gate = new object();
        private readonly BuildtableLogEntry[] _entries;
        private readonly bool _includeStackTraces;
        private readonly BuildtableLogLevel _minimumLevel;
        private int _next;
        private int _count;
        private int _dropped;
        private bool _subscribed;

        /// <summary>
        /// Raised on whatever thread logged, immediately after an uncaught
        /// exception is recorded. Handlers must do nothing but set a flag: the
        /// main thread owns every Unity API worth calling in response.
        /// </summary>
        public Action ExceptionLogged;

        public BuildtableLogBuffer(int capacity, bool includeStackTraces, BuildtableLogLevel minimumLevel = BuildtableLogLevel.Log)
        {
            _entries = new BuildtableLogEntry[Mathf.Max(1, capacity)];
            _includeStackTraces = includeStackTraces;
            _minimumLevel = minimumLevel;
        }

        /// <summary>Lines waiting to be drained.</summary>
        public int Count { get { lock (_gate) return _count; } }

        /// <summary>Lines overwritten since the last drain because the ring was full.</summary>
        public int DroppedSinceDrain { get { lock (_gate) return _dropped; } }

        public void Start()
        {
            if (_subscribed) return;
            Application.logMessageReceivedThreaded += OnLog;
            _subscribed = true;
        }

        public void Stop()
        {
            if (!_subscribed) return;
            Application.logMessageReceivedThreaded -= OnLog;
            _subscribed = false;
        }

        public void Dispose()
        {
            Stop();
            ExceptionLogged = null;
        }

        /// <summary>Where a <see cref="LogType"/> sits on the severity ladder.</summary>
        public static BuildtableLogLevel SeverityOf(LogType type)
        {
            switch (type)
            {
                case LogType.Exception: return BuildtableLogLevel.Exception;
                case LogType.Error:
                case LogType.Assert: return BuildtableLogLevel.Error;
                case LogType.Warning: return BuildtableLogLevel.Warning;
                default: return BuildtableLogLevel.Log;
            }
        }

        private void OnLog(string message, string stackTrace, LogType type)
        {
            // The SDK's own diagnostics must never enter the buffer. A failing
            // flush logs a warning; if that warning were captured it would refill
            // the buffer, trigger the next flush, and fail again — an error loop
            // the SDK created and then reported on.
            if (message != null && message.StartsWith("[Buildtable]", StringComparison.Ordinal)) return;

            var severity = SeverityOf(type);
            if (severity < _minimumLevel) return;

            var isFailure = severity >= BuildtableLogLevel.Error;
            var entry = new BuildtableLogEntry
            {
                TimeUtc = DateTime.UtcNow,
                Type = type,
                Message = Truncate(message, MaxMessageCharacters),
                // Stack traces are large and only interesting on failures.
                StackTrace = _includeStackTraces && isFailure ? Truncate(stackTrace, MaxStackCharacters) : null,
            };

            lock (_gate)
            {
                if (_count == _entries.Length) _dropped++;
                _entries[_next] = entry;
                _next = (_next + 1) % _entries.Length;
                if (_count < _entries.Length) _count++;
            }

            if (type == LogType.Exception)
            {
                var handler = ExceptionLogged;
                if (handler != null) handler();
            }
        }

        /// <summary>
        /// Moves everything buffered into <paramref name="into"/>, oldest first,
        /// and empties the ring. Returns the number of lines appended.
        /// </summary>
        public int Drain(List<BuildtableLogEntry> into)
        {
            if (into == null) return 0;
            lock (_gate)
            {
                var start = _count == _entries.Length ? _next : 0;
                for (var i = 0; i < _count; i++)
                {
                    into.Add(_entries[(start + i) % _entries.Length]);
                }

                var moved = _count;
                Array.Clear(_entries, 0, _entries.Length);
                _next = 0;
                _count = 0;
                _dropped = 0;
                return moved;
            }
        }

        /// <summary>
        /// Oldest-first plain-text dump that does <em>not</em> consume the buffer.
        /// The SDK no longer sends this anywhere — it's here for games that want
        /// to show a log tail in their own debug UI.
        /// </summary>
        public string Snapshot(int maxCharacters = 8000)
        {
            var sb = new StringBuilder();
            lock (_gate)
            {
                var start = _count == _entries.Length ? _next : 0;
                for (var i = 0; i < _count; i++)
                {
                    var entry = _entries[(start + i) % _entries.Length];
                    sb.Append('[').Append(entry.Type).Append("] ").Append(entry.Message).Append('\n');
                    if (!string.IsNullOrEmpty(entry.StackTrace)) sb.Append(entry.StackTrace).Append('\n');
                }
            }

            if (sb.Length <= maxCharacters) return sb.ToString();

            // Keep the tail — the lines nearest the failure are the useful ones.
            var text = sb.ToString();
            return "…(truncated)…\n" + text.Substring(text.Length - maxCharacters);
        }

        public void Clear()
        {
            lock (_gate)
            {
                Array.Clear(_entries, 0, _entries.Length);
                _next = 0;
                _count = 0;
                _dropped = 0;
            }
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max) + "…(truncated)";
        }
    }
}
