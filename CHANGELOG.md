# Changelog

All notable changes to this package. Versions follow
[Semantic Versioning](https://semver.org/).

## [0.3.0] — 2026-10-25

### Added
- A notice after submitting that says whether the report was sent, saved for
  retry, or failed. It never blocks input. Turn it off with `showSubmitFeedback`.
- `cornerRadius` and `font` settings.
- Tab moves between the form's text fields.
- PlayMode tests for the report queue and the form.

### Changed
- Redesigned form: rounded corners, panel shadow and border, focus and hover
  states, coloured priority markers, and a "Contact (optional)" section. Still no
  imported assets.
- Submitting without a title focuses the title field.

### Fixed
- Cancelling the form (button or Escape) discarded everything typed. The draft
  is now kept until it is submitted.
- `maxQueuedReports = 0` silently dropped every report. The minimum is now 1, and
  existing configs set to 0 are treated as 1.

## [0.2.0] — 2026-08-05

### Added
- **Log forwarding.** Console output, uncaught exceptions, and third-party logs
  are sent to Buildtable as structured events, with no code changes. Off by
  default.
- Uncaught exceptions are sent immediately as `crash` events, with one retry on
  next launch if the process died.
- `Buildtable.LogEvent`, `Buildtable.FlushLogs`, and
  `Buildtable.LogForwardingActive`.
- A `bug_reported` event alongside each filed bug when forwarding is on.
- Appearance settings: form copy, colours, scrim opacity, panel width, sorting
  order.
- `showStepsToRepro`, `showPriority`, and `showReporterIdentity` to hide fields.
- `Buildtable.SetCustomField` / `ClearCustomFields` and `CustomFields` on the
  draft.
- `maxSendAttempts` and exponential retry backoff, capped at an hour.
- MIT license.

### Changed
- **Breaking:** `includeRecentLogs`, `logBufferSize`, `includeLogStackTraces`,
  and `BuildtableReportDraft.IncludeLogs` are replaced by the log forwarding
  settings (`forwardLogs`, `minimumForwardedLevel`, and others).
- Log forwarding only runs in development builds unless
  `forwardLogsInReleaseBuilds` is set.
- A report's description is exactly what the tester typed; logs are no longer
  appended to it.
- The form sizes itself to its content.

### Fixed
- The action buttons overlapped the contact fields and status line.
- A report without a screenshot left an empty gap in the form.

## [0.1.0] — 2026-07-27

Initial release: in-game bug reporting.

### Added
- `BuildtableConfig` asset, with an inspector that rejects secret keys.
- `Buildtable` API: `ShowBugReporter`, `SubmitBugReport`, `SetReporter`,
  `FlushPendingReports`, `InstallId`, `PendingReportCount`.
- In-game report form with screenshot preview.
- Screenshot capture, uploaded and attached to the bug.
- Device info from `SystemInfo` and `Application`.
- Offline queue on disk, written before the first send, with retries.
- Per-install `X-Buildtable-Client` header for per-player rate limits.
- Version tagging from `StreamingAssets/buildtable-build.json` when present.
- Input System support without a package dependency.
- **Tools → Buildtable** menu: create config, open pending reports folder, reset
  install id.
