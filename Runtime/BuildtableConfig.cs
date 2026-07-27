using UnityEngine;

namespace BuildtableSdk
{
    /// <summary>
    /// Project-wide SDK settings. Create one via
    /// <c>Assets &gt; Create &gt; Buildtable &gt; Config</c>; it must live at
    /// <c>Assets/Resources/BuildtableConfig.asset</c> so the runtime can load it
    /// without any scene wiring.
    ///
    /// This asset is serialized into the player, so it holds the *ingest* key
    /// only. Ingest keys are write-only by design and assumed extractable from a
    /// build. Secret keys (Editor/CI: build registration, bug listing) must never
    /// be stored here — they live in EditorPrefs or CI environment variables.
    /// </summary>
    [CreateAssetMenu(fileName = "BuildtableConfig", menuName = "Buildtable/Config")]
    public class BuildtableConfig : ScriptableObject
    {
        public const string ResourceName = "BuildtableConfig";

        [Header("Connection")]
        [Tooltip("Base URL of your Buildtable deployment. No trailing slash.")]
        public string apiBaseUrl = "https://buildtable.app";

        [Tooltip("Project ingest key (bt_ingest_...). Write-only scope; safe to ship in a build.")]
        public string ingestApiKey = "";

        [Tooltip("Seconds before an API request is abandoned and re-queued.")]
        [Range(5, 120)]
        public int requestTimeoutSeconds = 20;

        [Header("Bug reporter")]
        [Tooltip("Spawn the reporter automatically on game start. Turn this off to instantiate BuildtableBugReporter yourself.")]
        public bool autoStart = true;

        [Tooltip("Listen for the toggle key. Turn off to open the reporter only from your own UI via Buildtable.ShowBugReporter().")]
        public bool hotkeyEnabled = true;

        [Tooltip("Key that opens and closes the in-game reporter.")]
        public KeyCode toggleKey = KeyCode.F8;

        [Tooltip("Freeze the game (Time.timeScale = 0) while the overlay is open.")]
        public bool pauseWhileOpen = true;

        [Tooltip("Unlock and show the mouse cursor while the overlay is open, then restore it on close.")]
        public bool releaseCursorWhileOpen = true;

        [Header("Capture")]
        [Tooltip("Grab the frame behind the overlay and attach it to the report.")]
        public bool captureScreenshot = true;

        [Tooltip("Longest screenshot edge, in pixels. Larger captures are downscaled before encoding so they stay well under the 5 MB server cap.")]
        [Range(480, 3840)]
        public int maxScreenshotDimension = 1920;

        [Tooltip("JPEG quality for screenshots.")]
        [Range(30, 100)]
        public int screenshotQuality = 85;

        [Tooltip("Attach SystemInfo details (OS, GPU, memory, resolution) to every report.")]
        public bool includeDeviceInfo = true;

        [Tooltip("Attach the most recent console lines to every report.")]
        public bool includeRecentLogs = true;

        [Tooltip("How many log lines to keep in the ring buffer.")]
        [Range(0, 500)]
        public int logBufferSize = 100;

        [Tooltip("Also record stack traces for errors and exceptions. Off keeps payloads small.")]
        public bool includeLogStackTraces = true;

        [Header("Delivery")]
        [Tooltip("Reports that fail to send are written to disk and retried. This caps how many are kept.")]
        [Range(0, 200)]
        public int maxQueuedReports = 25;

        [Tooltip("Seconds between retry sweeps of the offline queue.")]
        [Range(10, 600)]
        public int retryIntervalSeconds = 60;

        /// <summary>True when the config has enough to talk to the API at all.</summary>
        public bool IsUsable =>
            !string.IsNullOrEmpty(apiBaseUrl) && !string.IsNullOrEmpty(ingestApiKey);

        public string ApiRoot => (apiBaseUrl ?? "").TrimEnd('/');
    }
}
