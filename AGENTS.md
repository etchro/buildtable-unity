# Working in this repo

A Unity Package Manager package (`app.buildtable.sdk`), not a Unity project.
Consumers install it from a git URL pinned to a tag. The server it talks to lives
in a separate repo (`buildtable`), under `/api/v1`.

## Layout

| Path | Contents |
|---|---|
| `Runtime/Buildtable.cs` | Public static API. Start here |
| `Runtime/BuildtableConfig.cs` | Every setting, as a ScriptableObject loaded from `Resources/` |
| `Runtime/BugReporting/` | Reporter host (`BuildtableBugReporter`), the overlay UI, and the disk queue |
| `Runtime/Telemetry/` | Log forwarding and custom events |
| `Runtime/Core/` | HTTP, JSON, screenshot, device info, install id, input shim |
| `Editor/` | Config inspector and **Tools → Buildtable** menu |
| `Tests/Runtime/` | PlayMode tests, plus a fake API server |
| `Samples~/` | Importable sample; ignored by Unity until imported |

## Constraints

- **Unity 2021.3+.** Don't use APIs newer than that without a version guard.
- **No dependencies beyond uGUI.** Input System support uses reflection
  (`BuildtableInputSystemBridge`); keep it that way.
- **No assets.** The overlay is built in code, including its rounded-corner
  sprites, so the package works without importing anything.
- **Key polling goes through `BuildtableInput`**, never `Input` directly; `Input`
  throws in Input System-only projects.
- **Disk and network failures must not throw** into the game. Catch, log a
  `[Buildtable]` warning, and continue.
- **Every file needs a committed `.meta`.** A git package is read-only to the
  consumer, so a missing one causes import warnings and unstable GUIDs. Let Unity
  generate it (opening the dev project below does this) and commit it.
- Test hooks are `internal` and exposed through `Runtime/AssemblyInfo.cs`. Don't
  make them public.

## Tests

Tests only compile in a Unity project that lists the package as testable. Set
one up next to this repo (e.g. `../buildtable-unity-dev`) with this in
`Packages/manifest.json`:

```json
"dependencies": {
  "app.buildtable.sdk": "file:../../buildtable-unity",
  "com.unity.test-framework": "1.8.0"
},
"testables": ["app.buildtable.sdk"]
```

and set **Player → Allow downloads over HTTP** to *Always allowed* (the fake
server is plain HTTP on `127.0.0.1`). Then:

```sh
Unity -batchmode -projectPath ../buildtable-unity-dev -runTests -testPlatform PlayMode -testResults results.xml
```

Screenshot capture is skipped in tests because `WaitForEndOfFrame` never fires in
batch mode. Visual changes to the overlay still need a look in the Editor.

## Releasing

1. Move `## [Unreleased]` in `CHANGELOG.md` to `## [x.y.z] — YYYY-MM-DD`.
2. Set the version in `package.json` and `Buildtable.SdkVersion`.
3. Update the `#vx.y.z` install URLs in `README.md`.
4. Commit, tag `vx.y.z`, push the commit and the tag.

Pushing to `main` alone doesn't reach users; their install URL is pinned to a tag.
