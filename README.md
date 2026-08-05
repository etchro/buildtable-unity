# Buildtable SDK for Unity

In-game bug reporting for [Buildtable](https://buildtable.app). A tester presses a
key, describes what went wrong, and the report — with a screenshot, the recent
log tail, and full device details — lands on your project's bug board.

Requires Unity **2021.3 LTS** or newer.

> **Status:** Milestone A (bug reporting). Playtest forms, telemetry/events, and
> the build-registration hook are planned — see [Roadmap](#roadmap).

---

## Install

Package Manager → **Add package from git URL…**:

```
https://github.com/etchro/buildtable-unity.git
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "app.buildtable.sdk": "https://github.com/etchro/buildtable-unity.git"
  }
}
```

Pin a release by appending `#v0.1.0`.

## Setup

1. In Buildtable, open **your project → Settings → API keys** and create an
   **ingest** key. Copy it — the plaintext is shown once.
2. In Unity, run **Tools → Buildtable → Create or Select Config**. This creates
   `Assets/Resources/BuildtableConfig.asset`.
3. Paste the key into **Ingest API key**. Leave **API base URL** as
   `https://buildtable.app` unless you self-host.
4. Press Play and hit **F8**.

That's the whole setup. With `Auto start` on (the default) the SDK spawns itself
before the first scene loads — nothing to add to a scene, no prefab to drag.

### Ingest keys vs secret keys

Only ever put an **ingest** key (`bt_ingest_…`) in the config asset. That asset
is serialized into your build, so anything in it should be assumed extractable.
Ingest keys are write-only by design: they can file bugs and upload attachments,
and nothing else.

**Secret** keys (`bt_secret_…`) can read your bug list and register builds. They
belong in EditorPrefs or a CI environment variable, never in a shipped asset.
The config inspector flags a secret key as an error if one gets pasted in.

## Usage

```csharp
using BuildtableSdk;

// Open the report form (captures the frame behind it first)
Buildtable.ShowBugReporter();

// Or file one with no UI — from a crash handler, a pause menu, an automated test
Buildtable.SubmitBugReport(new BuildtableReportDraft
{
    Title = "Elevator softlock on floor 3",
    Description = "Doors never opened after the cutscene.",
    StepsToRepro = "1. Take the elevator\n2. Skip the cutscene",
    Priority = "high",          // critical | high | medium | low
});

// Pre-fill the reporter fields from your own account system
Buildtable.SetReporter("Ana", "ana@studio.example");

// How many reports are still waiting to reach the server
Debug.Log(Buildtable.PendingReportCount);
```

Subscribe to delivery outcomes if you want your own confirmation UI:

```csharp
Buildtable.Initialize().ReportSubmitted += bugId => Debug.Log($"Filed {bugId}");
Buildtable.Initialize().ReportFailed    += reason => Debug.LogWarning(reason);
```

## What ends up in a report

| Field | Source |
|---|---|
| Title, description, steps, priority | The tester |
| Reporter name / email | Remembered between sessions |
| Screenshot | Frame captured *before* the form is drawn, downscaled and JPEG-encoded |
| Recent logs | Ring buffer of `Application.logMessageReceivedThreaded`, appended to the description — **off by default** |
| Device info | `SystemInfo` + `Application`: OS, CPU, GPU, memory, resolution, quality level, scene, playtime |
| Custom fields | Whatever you set with `Buildtable.SetCustomField` |
| Version | From the build stamp, when one exists (see Roadmap → Milestone D) |

### Log forwarding is opt-in

`includeRecentLogs` defaults to **off**. A log tail can contain player names,
absolute file paths, and anything else your game happens to log, so it is only
attached once you turn it on in the config.

When it is on, the tail rides along in the description. That is a stopgap: the
API's `/api/v1/events` endpoint accepts `log_batch` and `crash` events with a
structured payload, and moving logs there — batched, out of the description, and
attributable to a session — is the next thing worth building.

## Customising the form

The overlay is built in code, but its look and contents come from the config
asset rather than the source, so a project can restyle it without forking:

| Setting | Purpose |
|---|---|
| `formTitle` / `formSubtitle` / `submitButtonLabel` | Copy. An empty subtitle hides the line entirely |
| `accentColor` | Submit button, checkmark, selected priority |
| `panelColor` / `fieldColor` / `textColor` / `mutedTextColor` | The rest of the palette |
| `scrimOpacity` | How much the game behind is dimmed. 0 leaves it fully visible |
| `panelWidth` | Form width in 1920×1080 reference pixels |
| `sortingOrder` | Raise it if the form lands behind your own UI |
| `showStepsToRepro` / `showPriority` / `showReporterIdentity` | Drop fields you don't want. The form resizes to fit whatever is left |

Attach your own metadata to every report:

```csharp
Buildtable.SetCustomField("branch", "feature/elevators");
Buildtable.SetCustomField("saveSlot", saveSlot.ToString());
```

Keys must match a custom field defined on your project's bug board — the server
drops values for keys it doesn't recognise. Per-report values can also be passed
on the draft via `CustomFields`, and those win over the defaults above.

If you need a form this doesn't cover, turn `hotkeyEnabled` off and build your
own UI on top of `Buildtable.SubmitBugReport(draft)` — the queue, retry, upload,
and device-info paths are all independent of the overlay.

## Offline behaviour

Every report is written to
`Application.persistentDataPath/buildtable/pending/` **before** the first network
call — not after the first failure. A tester who alt-F4s the moment they hit
submit still gets their report delivered on the next launch.

The queue retries on a timer (default 60s) and on startup. Transient failures
(offline, timeout, 429, 5xx) are retried; permanent rejections (bad key, invalid
payload) are logged and dropped rather than retried forever. The queue is capped
(default 25 reports) so a build pointed at a dead server can't fill a disk.

Each retry backs off exponentially from the sweep interval, capped at an hour,
and a report is discarded after `maxSendAttempts` (default 10). A server that
keeps returning 429 therefore costs a bounded number of requests rather than one
per minute forever.

## Rate limits

Every request carries `X-Buildtable-Client`, a random per-install id stored in
`PlayerPrefs`. This is what gives each player their own rate-limit budget — an
ingest key ships inside every copy of the game, so the server can't treat one key
as one client. Without the header, everyone behind one office NAT would share a
single bucket.

The id is random, not a device fingerprint, and carries no personal data. Clear
it with **Tools → Buildtable → Reset Install Id**.

## Configuration reference

| Setting | Default | Notes |
|---|---|---|
| `apiBaseUrl` | `https://buildtable.app` | No trailing slash |
| `ingestApiKey` | — | `bt_ingest_…` only |
| `autoStart` | on | Spawns the reporter before the first scene |
| `hotkeyEnabled` / `toggleKey` | on / F8 | Turn off to drive it from your own UI |
| `pauseWhileOpen` | on | `Time.timeScale = 0` while the form is up |
| `releaseCursorWhileOpen` | on | Unlocks the cursor, then restores it |
| `captureScreenshot` | on | |
| `maxScreenshotDimension` | 1920 | Longest edge; keeps files well under the 5 MB server cap |
| `screenshotQuality` | 85 | JPEG quality |
| `includeDeviceInfo` | on | |
| `includeRecentLogs` / `logBufferSize` | **off** / 100 | Opt-in; see above |
| `maxQueuedReports` | 25 | Oldest dropped past the cap |
| `retryIntervalSeconds` | 60 | Sweep interval; failures back off exponentially from it |
| `maxSendAttempts` | 10 | Report discarded after this many failures |

Appearance and form-field settings are listed under
[Customising the form](#customising-the-form).

## Input System support

Works with the legacy Input Manager, the Input System package, or both. In
Input System-only projects the configured `KeyCode` is mapped onto the new API by
name via reflection, so the SDK never takes a hard dependency on a package your
project may not have installed. If a key has no equivalent, open the form from
your own input handling instead.

## Roadmap

- **A — bug reporter** — shipped in 0.1.0
- **B — playtest forms** — fetch a session's schema from `/api/v1/playtests/active`, render it natively, submit at quit or checkpoint
- **C — auto-capture** — log/crash batches and custom events via `/api/v1/events`. The endpoint is already live server-side (`log_batch`, `crash`, `custom`, batches of up to 100); only the Unity side is missing. Opt-in stays the default — the capability exists for when it's wanted, not to stream everything by default
- **D — build hook** — `IPreprocessBuildWithReport` registers the build via `/api/v1/builds` with a secret key and stamps the id into StreamingAssets, so every payload is version-tagged. The runtime reader for that stamp already ships
- **E — Editor bug browser** — read the board from `GET /api/v1/bugs`

## Notes for contributors

`.meta` files are committed so the package imports cleanly from a git URL. If you
add a file, let Unity generate its `.meta` and commit that too — a missing one
makes Unity re-mint GUIDs per consumer.
