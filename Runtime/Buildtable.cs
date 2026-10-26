using System.Collections.Generic;
using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// The SDK's public surface. Everything a game needs goes through here:
    ///
    /// <code>
    /// Buildtable.ShowBugReporter();                 // open the in-game form
    /// Buildtable.SubmitBugReport(new BuildtableReportDraft { Title = "…" });
    /// </code>
    ///
    /// With <see cref="BuildtableConfig.autoStart"/> on (the default) nothing
    /// needs to be wired into a scene — the reporter spawns itself before the
    /// first scene loads.
    /// </summary>
    public static class Buildtable
    {
        public const string SdkVersion = "0.3.0";

        private static BuildtableConfig _config;
        private static bool _configLoadAttempted;

        /// <summary>
        /// The config asset from <c>Resources/BuildtableConfig</c>, or null when
        /// none exists. Assign this before <see cref="Initialize"/> to configure
        /// the SDK from code instead (useful for per-environment keys in CI).
        /// </summary>
        public static BuildtableConfig Config
        {
            get
            {
                if (_config == null && !_configLoadAttempted)
                {
                    _configLoadAttempted = true;
                    _config = Resources.Load<BuildtableConfig>(BuildtableConfig.ResourceName);
                }
                return _config;
            }
            set
            {
                _config = value;
                _configLoadAttempted = true;
            }
        }

        /// <summary>Anonymous per-install id sent as <c>X-Buildtable-Client</c>.</summary>
        public static string InstallId => BuildtableInstall.InstallId;

        public static bool IsInitialized => BuildtableBugReporter.Instance != null;

        /// <summary>Reports written to disk that haven't reached the server yet.</summary>
        public static int PendingReportCount => BuildtableBugReporter.Instance?.PendingReportCount ?? 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoStart()
        {
            var config = Config;
            if (config == null || !config.autoStart) return;
            Initialize();
        }

        /// <summary>
        /// Creates the runtime host if it doesn't exist yet. Call this manually
        /// when <see cref="BuildtableConfig.autoStart"/> is off, or after
        /// assigning a config from code.
        /// </summary>
        public static BuildtableBugReporter Initialize()
        {
            if (BuildtableBugReporter.Instance != null) return BuildtableBugReporter.Instance;

            if (Config == null)
            {
                Debug.LogError(
                    "[Buildtable] Cannot initialize: no BuildtableConfig. Create one via " +
                    "Assets > Create > Buildtable > Config, save it to Assets/Resources, " +
                    "or assign Buildtable.Config from code before calling Initialize().");
                return null;
            }

            var host = new GameObject("Buildtable");
            return host.AddComponent<BuildtableBugReporter>();
        }

        /// <summary>Opens the in-game report form, capturing the current frame first.</summary>
        public static void ShowBugReporter() => Initialize()?.Show();

        public static void HideBugReporter() => BuildtableBugReporter.Instance?.Hide();

        /// <summary>
        /// Files a report without showing any UI. The report is written to disk
        /// before the first network call, so it survives a crash or a quit.
        /// </summary>
        public static void SubmitBugReport(BuildtableReportDraft draft) => Initialize()?.Submit(draft);

        /// <summary>Retries everything still queued on disk.</summary>
        public static void FlushPendingReports() => BuildtableBugReporter.Instance?.FlushQueue();

        /// <summary>
        /// True when console output is actually being forwarded — that is,
        /// <see cref="BuildtableConfig.forwardLogs"/> is on <em>and</em> this is a
        /// development build (or the release escape hatch is set).
        /// </summary>
        public static bool LogForwardingActive =>
            BuildtableBugReporter.Instance?.Telemetry?.LogForwardingActive ?? false;

        /// <summary>
        /// Records a custom gameplay event on the project's event stream.
        ///
        /// <code>
        /// Buildtable.LogEvent("level_completed", new Dictionary&lt;string, string&gt; {
        ///     { "level", "3" }, { "deaths", "2" }
        /// });
        /// </code>
        ///
        /// Works even with log forwarding off: calling this is an explicit
        /// decision, unlike ambient console capture.
        /// </summary>
        public static void LogEvent(string name, Dictionary<string, string> fields = null) =>
            Initialize()?.Telemetry?.LogEvent(name, fields);

        /// <summary>
        /// Sends buffered log lines and events immediately instead of waiting for
        /// the flush timer. Worth calling before a scripted quit or a level load
        /// you expect to be slow.
        /// </summary>
        public static void FlushLogs() => BuildtableBugReporter.Instance?.Telemetry?.Flush();

        /// <summary>
        /// Pre-fills the reporter fields, e.g. from your own account system.
        /// Persisted across sessions.
        /// </summary>
        public static void SetReporter(string name, string email)
        {
            BuildtableInstall.ReporterName = name;
            BuildtableInstall.ReporterEmail = email;
        }

        /// <summary>
        /// Values attached to every subsequent report — the build's branch, the
        /// player's save slot, whatever your board defines. Keys must match a
        /// custom field on the project's bug board; anything else is dropped
        /// server-side.
        /// </summary>
        internal static readonly Dictionary<string, string> DefaultCustomFields = new Dictionary<string, string>();

        /// <summary>Sets a custom field value for every later report. A null value clears it.</summary>
        public static void SetCustomField(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (value == null) DefaultCustomFields.Remove(key);
            else DefaultCustomFields[key] = value;
        }

        public static void ClearCustomFields() => DefaultCustomFields.Clear();
    }
}
