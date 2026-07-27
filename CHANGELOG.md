# Changelog

All notable changes to this package are documented here. This project follows
[Semantic Versioning](https://semver.org/).

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
