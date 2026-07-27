using System;
using System.Text;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Fixed-size ring of the most recent console lines, so a bug report can
    /// carry the log tail that led up to it. Subscribes to
    /// <see cref="Application.logMessageReceived"/>.
    ///
    /// Threading: the callback can fire off the main thread (jobs, background
    /// threads), so writes are locked. Capacity is bounded, so a runaway
    /// error loop costs a fixed amount of memory.
    /// </summary>
    public sealed class BuildtableLogBuffer : IDisposable
    {
        private readonly object _gate = new object();
        private readonly Entry[] _entries;
        private readonly bool _includeStackTraces;
        private int _next;
        private int _count;
        private bool _subscribed;

        private struct Entry
        {
            public LogType Type;
            public string Message;
            public string StackTrace;
        }

        public BuildtableLogBuffer(int capacity, bool includeStackTraces)
        {
            _entries = new Entry[Mathf.Max(1, capacity)];
            _includeStackTraces = includeStackTraces;
        }

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

        public void Dispose() => Stop();

        private void OnLog(string message, string stackTrace, LogType type)
        {
            var isFailure = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
            lock (_gate)
            {
                _entries[_next] = new Entry
                {
                    Type = type,
                    Message = message,
                    // Stack traces are large and only interesting on failures.
                    StackTrace = _includeStackTraces && isFailure ? stackTrace : null,
                };
                _next = (_next + 1) % _entries.Length;
                if (_count < _entries.Length) _count++;
            }
        }

        /// <summary>
        /// Oldest-first plain-text dump, capped so a pathological log tail can't
        /// blow past the server's 10,000-character description limit on its own.
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
            }
        }
    }
}
