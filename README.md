# Buildtable SDK for Unity

In-game bug reporting and log forwarding for [Buildtable](https://buildtable.app).
A tester presses a key, describes the problem, and the report lands on your
project's bug board with a screenshot and device details. With log forwarding on,
the console output around it gets there too.

Requires Unity **2021.3 LTS** or newer.

## Install

In Unity: **Window → Package Manager → + → Add package from git URL…**, then paste:

```
https://github.com/etchro/buildtable-unity.git#v0.3.0
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "app.buildtable.sdk": "https://github.com/etchro/buildtable-unity.git#v0.3.0"
  }
}
```

To update, change the tag. Releases are listed in the [changelog](CHANGELOG.md).
Unity needs [Git](https://git-scm.com/downloads) installed to fetch git-URL
packages.

## Setup

1. In Buildtable, open **your project → Settings → API keys** and create an
   **ingest** key. The plaintext is shown once.
2. In Unity, run **Tools → Buildtable → Create or Select Config**. This creates
   `Assets/Resources/BuildtableConfig.asset`.
3. Paste the key into **Ingest API key**. Leave **API base URL** as
   `https://buildtable.app` unless you self-host.
4. Press Play and hit **F8**.

With `autoStart` on (the default) the SDK starts itself before the first scene
loads. Nothing needs to be added to a scene.

### Keys

Only put an **ingest** key (`bt_ingest_…`) in the config asset. The asset ships
inside every build, so treat the key as public: it can file bugs and upload
attachments, nothing else. **Secret** keys (`bt_secret_…`) can read your board
and must never go in a shipped asset; the config inspector flags one as an error.

Since anyone holding an ingest key can post to your board, give each collaborator
their own key, rotate it if a build spreads further than planned, and don't share
one key across projects.

To keep the key out of source control, turn `autoStart` off, leave
`ingestApiKey` empty, and supply it yourself:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void ConfigureBuildtable()
{
    Buildtable.Config.ingestApiKey = MyBuildSecrets.BuildtableKey;
    Buildtable.Initialize();
}
```

`autoStart` must be off, or the SDK's own startup hook may run first. In the
Editor this dirties the config asset; wrap it in `#if !UNITY_EDITOR` if that
matters.

## Usage

```csharp
using BuildtableSdk;

// Open the report form (captures the frame behind it first)
Buildtable.ShowBugReporter();

// Or file a report with no UI
Buildtable.SubmitBugReport(new BuildtableReportDraft
{
    Title = "Elevator softlock on floor 3",
    Description = "Doors never opened after the cutscene.",
    StepsToRepro = "1. Take the elevator\n2. Skip the cutscene",
    Priority = "high",          // critical | high | medium | low
});

// Pre-fill the reporter fields from your own account system
Buildtable.SetReporter("Ana", "ana@studio.example");

// Reports still waiting to reach the server
Debug.Log(Buildtable.PendingReportCount);

// Delivery outcomes, for your own confirmation UI
Buildtable.Initialize().ReportSubmitted += bugId => Debug.Log($"Filed {bugId}");
Buildtable.Initialize().ReportFailed    += reason => Debug.LogWarning(reason);
```

### What a report contains

| Field | Source |
|---|---|
| Title, description, steps, priority | The tester |
| Reporter name / email | Remembered between sessions |
| Screenshot | Frame captured before the form is drawn, downscaled, JPEG |
| Device info | OS, CPU, GPU, memory, resolution, quality level, scene, playtime |
| Custom fields | Set with `Buildtable.SetCustomField` |
| Version | From `StreamingAssets/buildtable-build.json`, when present |

Logs are not appended to the description. They travel as separate events and
are matched to the report by time, install id, and version.

### Custom fields

```csharp
Buildtable.SetCustomField("branch", "feature/elevators");
Buildtable.SetCustomField("saveSlot", saveSlot.ToString());
```

Keys must match a custom field on your project's bug board; the server drops
unknown keys. Values set on a draft's `CustomFields` override these defaults.

## Log forwarding

Tick **Forward logs** in the config. The SDK listens to
`Application.logMessageReceivedThreaded`, so it picks up every `Debug.Log*`
call, uncaught exceptions, failed asserts, and logs from third-party packages
and background threads, with no changes to your code.

- **Batching.** Lines are posted to `/api/v1/events` every 30 seconds
  (`logFlushIntervalSeconds`), when the buffer fills, when a bug is filed, and
  on pause or quit. Uncaught exceptions are sent immediately as a `crash` event.
- **Level.** `minimumForwardedLevel` defaults to `Warning`. Stack traces are
  attached to errors, asserts, and exceptions only.
- **Release builds.** Forwarding runs only when
  `forwardLogs && (Debug.isDebugBuild || forwardLogsInReleaseBuilds)`. A release
  build doesn't forward unless you set the second flag, and when off the SDK
  doesn't subscribe to the log callback at all.
- **Offline.** Logs aren't written to disk. A batch that fails is dropped and
  the flush interval backs off (up to 5 minutes). The exception is a `crash`
  event, which gets one slot on disk and one retry on next launch.
- **Memory.** Capped at `maxBufferedLines` (500); the oldest lines are dropped
  and counted in `droppedLines`.
- **Privacy.** Logs contain whatever your game logs, so check for player names
  and file paths first. Buildtable deletes forwarded logs after 7 days.

Custom events work even with `forwardLogs` off:

```csharp
Buildtable.LogEvent("level_completed", new Dictionary<string, string> {
    { "level", "3" },
    { "deaths", "2" },
});

Buildtable.FlushLogs();                     // send now, e.g. before a scripted quit
bool on = Buildtable.LogForwardingActive;   // is forwarding running in this build?
```

## Customising the form

The form is built in code with uGUI. Its look and fields come from the config
asset:

| Setting | Purpose |
|---|---|
| `formTitle` / `formSubtitle` / `submitButtonLabel` | Copy. An empty subtitle hides the line |
| `accentColor` | Submit button, checkmark, selected priority |
| `panelColor` / `fieldColor` / `textColor` / `mutedTextColor` | The rest of the palette |
| `scrimOpacity` | How much the game behind is dimmed |
| `panelWidth` | Form width in 1920×1080 reference pixels |
| `cornerRadius` | Rounding of fields and buttons (the panel uses double). 0 is square |
| `font` | A `.ttf`/`.otf` Font asset. Empty uses Unity's built-in font |
| `showSubmitFeedback` | After submit, a notice says whether the report was sent or saved for retry |
| `sortingOrder` | Raise it if the form appears behind your own UI |
| `showStepsToRepro` / `showPriority` / `showReporterIdentity` | Hide fields. The form resizes to fit |

A cancelled form keeps its draft until the report is submitted. Tab moves between
fields.

For a form this doesn't cover, turn `hotkeyEnabled` off and build your own UI on
`Buildtable.SubmitBugReport(draft)`. Queueing, retries, and uploads don't depend
on the overlay.

## Delivery

Every report is written to `Application.persistentDataPath/buildtable/pending/`
before the first network call, so a report survives a crash or quit right after
submit and is sent on next launch.

The queue retries every `retryIntervalSeconds` (60) and on startup, backing off
exponentially up to an hour per report. Offline, timeout, 429, and 5xx responses
are retried; other 4xx responses are logged and dropped. A report is discarded
after `maxSendAttempts` (10), and the queue holds at most `maxQueuedReports` (25),
dropping the oldest.

Every request carries `X-Buildtable-Client`, a random per-install id kept in
`PlayerPrefs`, so each player gets their own rate-limit budget instead of sharing
one per IP. It is not a device fingerprint. Reset it with **Tools → Buildtable →
Reset Install Id**.

## Configuration reference

| Setting | Default | Notes |
|---|---|---|
| `apiBaseUrl` | `https://buildtable.app` | |
| `ingestApiKey` | — | `bt_ingest_…` only |
| `requestTimeoutSeconds` | 20 | |
| `autoStart` | on | Starts the SDK before the first scene |
| `hotkeyEnabled` / `toggleKey` | on / F8 | |
| `pauseWhileOpen` | on | `Time.timeScale = 0` while the form is open |
| `releaseCursorWhileOpen` | on | Unlocks the cursor, then restores it |
| `captureScreenshot` | on | |
| `maxScreenshotDimension` | 1920 | Longest edge |
| `screenshotQuality` | 85 | JPEG quality |
| `includeDeviceInfo` | on | |
| `forwardLogs` | **off** | See [Log forwarding](#log-forwarding) |
| `forwardLogsInReleaseBuilds` | **off** | |
| `minimumForwardedLevel` | `Warning` | `Log` \| `Warning` \| `Error` \| `Exception` |
| `logFlushIntervalSeconds` | 30 | 5–300 |
| `maxLinesPerBatch` | 200 | |
| `maxBufferedLines` | 500 | |
| `forwardStackTraces` | on | Errors, asserts, and exceptions only |
| `maxQueuedReports` | 25 | Minimum 1 |
| `retryIntervalSeconds` | 60 | |
| `maxSendAttempts` | 10 | |

Appearance settings are under [Customising the form](#customising-the-form).

## Input System

Works with the legacy Input Manager, the Input System package, or both. In Input
System-only projects the `KeyCode` is mapped to the new API by reflection, so the
package has no hard dependency on Input System.

## Roadmap

- Playtest forms: fetch a session's questions from Buildtable and show them in game
- Build registration: an Editor build hook that registers each build and writes
  the version stamp
- An Editor window for browsing the bug board

## Contributing

See [AGENTS.md](AGENTS.md) for the repo layout, conventions, and how to run the
tests.
