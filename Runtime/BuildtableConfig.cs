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

        [Header("Log forwarding")]
        [Tooltip("Stream console output to Buildtable. Nothing in your game has to call anything — every " +
                 "Debug.Log/LogWarning/LogError and every uncaught exception is captured automatically. " +
                 "Off by default: logs can contain player names, file paths, and anything else your game " +
                 "logs, so it should be a decision rather than a default.")]
        public bool forwardLogs = false;

        [Tooltip("Also forward from non-development builds. Leave OFF: with it off, a shipped release build " +
                 "cannot send logs even if 'Forward logs' is left on in this asset.")]
        public bool forwardLogsInReleaseBuilds = false;

        [Tooltip("Lowest severity worth forwarding. 'Warning' catches problems without the noise of every " +
                 "Debug.Log; 'Log' forwards everything; 'Error' forwards only failures.")]
        public BuildtableLogLevel minimumForwardedLevel = BuildtableLogLevel.Warning;

        [Tooltip("Seconds between log batches. Uncaught exceptions ignore this and send immediately.")]
        [Range(5, 300)]
        public int logFlushIntervalSeconds = 30;

        [Tooltip("Most lines packed into a single batch.")]
        [Range(10, 200)]
        public int maxLinesPerBatch = 200;

        [Tooltip("Ceiling on lines held in memory between flushes. Past it the oldest are dropped, so a " +
                 "runaway error loop costs a fixed amount of memory.")]
        [Range(50, 2000)]
        public int maxBufferedLines = 500;

        [Tooltip("Include stack traces. They only attach to errors, assertions, and exceptions regardless.")]
        public bool forwardStackTraces = true;

        [Header("Delivery")]
        [Tooltip("Reports that fail to send are written to disk and retried. This caps how many are kept.")]
        [Range(0, 200)]
        public int maxQueuedReports = 25;

        [Tooltip("Seconds between retry sweeps of the offline queue.")]
        [Range(10, 600)]
        public int retryIntervalSeconds = 60;

        [Tooltip("Give up on a report after this many failed attempts. Without a cap, a report the server " +
                 "keeps rate-limiting would be retried for the life of the install.")]
        [Range(1, 50)]
        public int maxSendAttempts = 10;

        [Header("Appearance")]
        [Tooltip("Heading shown at the top of the report form.")]
        public string formTitle = "Report a bug";

        [Tooltip("Smaller line under the heading. Leave empty to hide it.")]
        public string formSubtitle = "Sent to your team's Buildtable board.";

        [Tooltip("Label on the submit button.")]
        public string submitButtonLabel = "Send report";

        [Tooltip("Width of the form panel in reference pixels (the canvas scales from 1920x1080).")]
        [Range(420, 1400)]
        public float panelWidth = 760f;

        [Tooltip("Buttons, checkmarks, and the selected priority.")]
        public Color accentColor = new Color(0.36f, 0.45f, 0.95f, 1f);

        [Tooltip("Background of the form panel.")]
        public Color panelColor = new Color(0.09f, 0.09f, 0.11f, 0.98f);

        [Tooltip("Background of text fields and unselected buttons.")]
        public Color fieldColor = new Color(0.16f, 0.16f, 0.19f, 1f);

        [Tooltip("Primary text.")]
        public Color textColor = new Color(0.92f, 0.92f, 0.94f, 1f);

        [Tooltip("Labels, placeholders, and status text.")]
        public Color mutedTextColor = new Color(0.62f, 0.62f, 0.68f, 1f);

        [Tooltip("How much the form dims the game behind it. 0 leaves the game fully visible.")]
        [Range(0f, 1f)]
        public float scrimOpacity = 0.55f;

        [Tooltip("Canvas sorting order. Raise it if the form ends up behind your own UI.")]
        public int sortingOrder = 30000;

        [Header("Form fields")]
        [Tooltip("Show the 'Steps to reproduce' box.")]
        public bool showStepsToRepro = true;

        [Tooltip("Show the priority selector. Hidden means every report is filed as medium.")]
        public bool showPriority = true;

        [Tooltip("Show the name and email boxes. Hide these if you already call Buildtable.SetReporter() " +
                 "from your own account system.")]
        public bool showReporterIdentity = true;

        /// <summary>True when the config has enough to talk to the API at all.</summary>
        public bool IsUsable =>
            !string.IsNullOrEmpty(apiBaseUrl) && !string.IsNullOrEmpty(ingestApiKey);

        public string ApiRoot => (apiBaseUrl ?? "").TrimEnd('/');
    }
}
