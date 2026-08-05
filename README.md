# Buildtable SDK for Unity

In-game bug reporting and log forwarding for [Buildtable](https://buildtable.app).
A tester presses a key, describes what went wrong, and the report — with a
screenshot and full device details — lands on your project's bug board. Turn on
log forwarding and the console output around it gets there too, without a line of
code in your game.

Requires Unity **2021.3 LTS** or newer.

> **Status:** Milestone A (bug reporting) and C (log/event forwarding). Playtest
> forms and the build-registration hook are planned — see [Roadmap](#roadmap).

---

## Install

In Unity: **Window → Package Manager → + → Add package from git URL…**, paste
this, and hit **Add**:

```
https://github.com/etchro/buildtable-unity.git
```

That's it — no download, no unzipping, no `.unitypackage`. Unity fetches it and
it shows up under *Packages* in your Project window. Updating later is
**Package Manager → Buildtable SDK → Update**.

<details>
<summary>Prefer editing the manifest, or pinning a version?</summary>

Add it to `Packages/manifest.json` yourself:

```json
{
  "dependencies": {
    "app.buildtable.sdk": "https://github.com/etchro/buildtable-unity.git"
  }
}
```

Append `#v0.2.0` (or any tag) to pin a specific release instead of tracking
`main`. Recommended once you're past prototyping — an unpinned git dependency can
change under you mid-playtest.
</details>

**Requires git on your machine.** Unity shells out to it for git-URL packages. If
Package Manager says *"No 'git' executable was found"*, install
[Git](https://git-scm.com/downloads) and restart Unity.

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

### Your key lives in your game's repo

`BuildtableConfig.asset` is a normal Unity asset, so it gets committed with your
project. If your **game** repo is public, treat that ingest key as public too —
which is the same assumption you should already be making, since the asset ships
inside every build.

That's survivable by design (an ingest key can only file bugs and upload
attachments) but it isn't free: anyone holding it can post junk to your board. So:

- **Give each collaborator their own ingest key.** Then a leaked or noisy one gets
  revoked on its own, without breaking everyone else's builds.
- **Rotate the key** if a build gets wider distribution than you planned.
- Don't reuse one key across unrelated projects.

If you'd rather keep the key out of source control entirely, turn **`autoStart`
off** in the asset, leave `ingestApiKey` empty, and start the SDK yourself once
you've supplied the key from wherever you do keep it:

```csharp
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
static void ConfigureBuildtable()
{
    Buildtable.Config.ingestApiKey = MyBuildSecrets.BuildtableKey;
    Buildtable.Initialize();
}
```

`autoStart` must be off for this: with it on, the SDK initialises from its own
`BeforeSceneLoad` hook and the order between the two is undefined. Note this
writes to the config asset in memory — harmless in a player, but in the Editor
it will dirty the asset, so guard it with `#if !UNITY_EDITOR` if that bothers you.

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
| Device info | `SystemInfo` + `Application`: OS, CPU, GPU, memory, resolution, quality level, scene, playtime |
| Custom fields | Whatever you set with `Buildtable.SetCustomField` |
| Version | From the build stamp, when one exists (see Roadmap → Milestone D) |

The description is exactly what the tester typed. Logs are **not** stapled onto
it — they travel separately as structured events and are correlated in the viewer
by time, install id, and version.

## Log forwarding

Tick **Forward logs** in the config. That's the whole integration:

```
Forward logs                    ✔
Minimum forwarded level         Warning
```

You don't wrap `Debug.Log`, you don't route anything through a Buildtable
logger, and you don't touch your game code. The SDK subscribes to
`Application.logMessageReceivedThreaded`, which is Unity's process-wide log sink —
so it already sees:

- every `Debug.Log`, `Debug.LogWarning`, `Debug.LogError`, `Debug.LogException`
- every **uncaught exception**, with its stack trace
- failed `Debug.Assert` calls
- logs from **third-party packages and plugins**, which never knew Buildtable existed
- logs from background threads and jobs, not just the main thread

If your game already logs its errors, it is already instrumented.

```csharp
// Nothing Buildtable-specific — this is forwarded as-is
Debug.LogError($"Elevator {id} never opened after cutscene");
```

### What gets sent, and when

Lines are batched in memory and posted to `/api/v1/events` every 30 seconds
(`logFlushIntervalSeconds`). Uncaught exceptions don't wait — they're sent
immediately as a `crash` event, because the process is often about to die. A
batch is also sent when the buffer fills, when a bug report is filed, and when
the app is paused or quit.

`minimumForwardedLevel` decides how much noise you take. `Warning` (the default)
catches problems without shipping every frame's `Debug.Log`; `Log` forwards
everything; `Error` forwards only failures. Stack traces attach to errors,
assertions, and exceptions only — never to ordinary log lines.

### Release builds can't leak logs

Forwarding is active only when:

```csharp
forwardLogs && (Debug.isDebugBuild || forwardLogsInReleaseBuilds)
```

`Debug.isDebugBuild` is true in the Editor and in any player built with
**Development Build** ticked. So a shipped release build **cannot** forward logs
even if `forwardLogs` is left on in the asset — you'd have to deliberately set a
second, separately named flag. When the gate is closed the SDK never subscribes
to the log callback at all: no buffer, no allocation, no per-line cost.

### Privacy and cost

Logs are whatever your game logs, so check that isn't player names or absolute
file paths before you turn this on. Buildtable **deletes forwarded logs after 7
days** — they're diagnostic, not a record.

Nothing is written to disk. A batch that can't be delivered is dropped and the
flush interval doubles (up to 5 minutes) until the network comes back. Bug
reports are precious and survive a crash; logs are cheap and don't. The one
exception is a `crash` event, which gets a single reusable slot on disk and one
retry on next launch.

Memory is bounded by `maxBufferedLines` (default 500). A game in an error loop
drops its oldest lines and reports `droppedLines` in the batch rather than
growing without limit.

### Custom events

For gameplay milestones rather than console output:

```csharp
Buildtable.LogEvent("level_completed", new Dictionary<string, string> {
    { "level", "3" },
    { "deaths", "2" },
});

Buildtable.FlushLogs();               // send now, e.g. before a scripted quit
bool on = Buildtable.LogForwardingActive;  // did the debug-build gate open?
```

`LogEvent` works even with `forwardLogs` off — calling it is an explicit decision,
unlike ambient console capture.

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
| `forwardLogs` | **off** | Opt-in; see [Log forwarding](#log-forwarding) |
| `forwardLogsInReleaseBuilds` | **off** | Leave off unless you mean it |
| `minimumForwardedLevel` | `Warning` | `Log` \| `Warning` \| `Error` \| `Exception` |
| `logFlushIntervalSeconds` | 30 | 5–300. Exceptions ignore it and send at once |
| `maxLinesPerBatch` | 200 | Contract cap |
| `maxBufferedLines` | 500 | Oldest dropped past the cap |
| `forwardStackTraces` | on | Errors, assertions, and exceptions only regardless |
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
- **C — auto-capture** — done: `log_batch`, `crash`, and `custom` events via `/api/v1/events`. Sessions (`sessionId`) land with Milestone B
- **D — build hook** — `IPreprocessBuildWithReport` registers the build via `/api/v1/builds` with a secret key and stamps the id into StreamingAssets, so every payload is version-tagged. The runtime reader for that stamp already ships
- **E — Editor bug browser** — read the board from `GET /api/v1/bugs`

## Notes for contributors

`.meta` files are committed so the package imports cleanly from a git URL. If you
add a file, let Unity generate its `.meta` and commit that too — a missing one
makes Unity re-mint GUIDs per consumer.
