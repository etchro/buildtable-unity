# Changelog

All notable changes to this package are documented here. This project follows
[Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- A delivery notice after submitting: "Sending report…", then sent, saved for
  retry, or failed. It sits on its own raycaster-less canvas, so it never blocks
  input. Toggle with `showSubmitFeedback`.
- `cornerRadius` and `font` appearance settings.
- Tab moves between the form's text fields.
- PlayMode tests for the report queue and the overlay (draft handling, tab
  order, delivery notices against a local fake server). See "Running the tests"
  in the README.

### Changed
- Visual polish on the overlay: rounded corners, a soft panel shadow and border,
  hover and focus rings on fields and buttons, a drawn checkmark, coloured
  priority dots, dividers, and a "Contact (optional)" label over the name and
  email fields. All sprites are generated at runtime; the package still ships no
  art assets. A failed submit (no title) now focuses the title field.

### Fixed
- `maxQueuedReports = 0` silently dropped every report: reports are only sent
  from the disk queue, and a cap of 0 meant nothing was ever written to it. The
  minimum is now 1, and an existing asset set to 0 is treated as 1.
- Cancelling the form (button or Escape) threw away everything typed. The draft
  now stays until it is submitted, including the field that had focus when
  Escape was pressed.

## [0.2.0] — 2026-08-05

### Added
- **Log forwarding.** Console output streams to `POST /api/v1/events` as
  structured `log_batch` events instead of riding along in a bug description.
  Zero integration: subscribing to `Application.logMessageReceivedThreaded`
  captures every `Debug.Log*`, every uncaught exception, and anything logged by
  third-party packages, without a line of game code.
- Uncaught exceptions are sent immediately as a `crash` event rather than waiting
  for the flush timer, and get one durable slot on disk so a crash that killed the
  process is retried on next launch.
- `Buildtable.LogEvent(name, fields)` for custom gameplay events,
  `Buildtable.FlushLogs()`, and `Buildtable.LogForwardingActive`.
- A `bug_reported` event is emitted alongside a filed bug when forwarding is on,
  so the viewer can line a bug up against the logs around it.
- `BuildtableApi.PostEvents`, `BuildtableJson.RawElement`, and
  `BuildtableDeviceInfo.BuildCompactFragment` (a short fragment for events, since
  the full hardware dump would dwarf the lines it describes).
- Appearance settings on `BuildtableConfig`: form copy, accent/panel/field/text
  colours, scrim opacity, panel width, and canvas sorting order.
- Form-field toggles (`showStepsToRepro`, `showPriority`, `showReporterIdentity`).
  The overlay now measures itself from whatever rows remain.
- `Buildtable.SetCustomField` / `ClearCustomFields`, plus `CustomFields` on the
  draft, wired to the `customFields` object `POST /api/v1/bugs` already accepts.
- `maxSendAttempts` config, exponential retry backoff (capped at an hour), and
  `lastAttemptUtcTicks` on the queue record.
- MIT `LICENSE.md`, plus `license`/`licensesUrl`/`changelogUrl` in `package.json`.

### Changed
- **Breaking:** the log config is replaced. `includeRecentLogs`,
  `logBufferSize`, `includeLogStackTraces`, and `BuildtableReportDraft.IncludeLogs`
  are gone; `forwardLogs`, `forwardLogsInReleaseBuilds`, `minimumForwardedLevel`,
  `logFlushIntervalSeconds`, `maxLinesPerBatch`, `maxBufferedLines`, and
  `forwardStackTraces` replace them. 0.1.0 was never released, so there is no
  migration shim.
- **Forwarding is opt-in and gated on `Debug.isDebugBuild`.** A shipped release
  build cannot send logs even with `forwardLogs` left on — that needs a second,
  separately named flag. With the gate closed the SDK never subscribes to the log
  callback, so it costs nothing.
- A bug report's description is now exactly what the tester typed. Logs are
  correlated in the viewer by time, install id, and version instead.
- `BuildtableLogBuffer` keeps structured entries (timestamp, level, message,
  stack) and gained `Drain()`. `Snapshot()` remains for games that want to show a
  log tail in their own debug UI.
- Failed log batches are dropped rather than queued, and the flush interval backs
  off to 5 minutes. Logs deliberately do not use the durable report queue — that
  is what stops a dead server filling a player's disk with telemetry.
- The overlay's layout is computed rather than hand-tuned, and the panel is sized
  to its content.

### Fixed
- The action bar overlapped the identity fields and status line: content stacked
  to 654px inside a 652px panel, with the buttons pinned over the last two rows.
- A report with no screenshot left the preview row's 128px of dead space behind.

## [0.1.0] — 2026-07-27

Initial release: Milestone A, in-game bug reporting.

### Added
- `BuildtableConfig` ScriptableObject (base URL, ingest key, capture and delivery
  options) loaded from `Resources`, with an inspector that rejects secret keys.
- `Buildtable` facade: `ShowBugReporter`, `SubmitBugReport`, `SetReporter`,
  `FlushPendingReports`, `InstallId`, `PendingReportCount`.
- In-game report overlay built in code with uGUI — title, description, steps,
  priority, reporter identity, screenshot preview and opt-out.
- Screenshot capture at end of frame, downscaled and JPEG-encoded, uploaded via
  `POST /api/v1/uploads` and attached to `POST /api/v1/bugs`.
- Recent-log ring buffer over `Application.logMessageReceived`, appended to the
  report description.
- Device payload from `SystemInfo` / `Application`.
- Durable offline queue in `persistentDataPath`, written before the first send
  attempt, with retry on a timer and permanent-failure discard.
- Per-install `X-Buildtable-Client` header so each player gets their own
  rate-limit budget.
- Build-stamp reader for `StreamingAssets/buildtable-build.json`, so reports are
  version-tagged as soon as the Milestone D build hook lands.
- Input System support by reflection, with no hard package dependency.
- Editor menu items: create/select config, open the pending report folder, reset
  the install id.
