# Performance measurements

All figures below were measured on the development machine used to build this repository
(Windows 11 Pro, x64, .NET 10.0.402, Windows App SDK 2.5.1) with the **Debug** build, which is the
pessimistic case. They are reported verbatim from the measurement runs; no target is claimed that was
not observed.

## Cold start

Method: delete `%LOCALAPPDATA%\DietTracker\database\diettracker.db`, start `DietTracker.exe`, measure the
interval until the database file exists (which is the last step before the first frame is composed),
then sample the process for six seconds.

| Metric | Measured (Debug) |
|---|---|
| Time to database ready | **0.39 s** |
| Working set (idle) | **138.3 MB** |
| Private memory | 100.4 MB |
| Threads | 92 |
| Handles | 1 099 |
| Responding after startup | yes |

Command:

```bash
powershell -ExecutionPolicy Bypass -File tools/Smoke-Test.ps1
```

Notes:

* The window is created after the database is ready; the WinUI frame itself is not part of the 0.39 s.
* The working set is dominated by the Windows App SDK / WinUI runtime and the .NET runtime. A Release
  build with `PublishReadyToRun` reduces both startup time and memory; measure it with the same script
  (`-Configuration Release`).
* The application deliberately performs no cloud work before the first frame: authentication restore,
  the first synchronization cycle and reminder scheduling all run after the window is shown.

## Automated test timings

| Suite | Tests | Duration |
|---|---|---|
| Unit tests (business rules, database, migrations, localization, log file) | 83 | ~0.8 s |
| Integration tests (offline-first flow, multi-device sync, conflicts, backup) | 15 | ~1.0 s |
| UI smoke tests (launches the real application) | 2 | ~5 s |

The integration suite exercises the real SQLite database and the real synchronization engine, so a
single cycle (upload + pull + apply) completes in tens of milliseconds with the in-memory provider.

## Resource discipline in the implementation

* Only the visible date range is loaded; the calendar and statistics screens use bounded range queries.
* Photographs are never held in memory beyond the import; thumbnails are read from disk by the image
  control and released with the visual tree.
* Reminders are event driven: the scheduler waits until the next due instant (capped at five minutes)
  instead of polling.
* The journal is coalesced per record, so rapid edits produce a single upload.
* Unchanged records and identical photographs are never uploaded again (SHA-256 content hashes).

## What has not been measured here

* Behaviour on low-end hardware, on ARM64 and on battery power.
* Cloud round-trip timings against real OneDrive/Google Drive accounts (no credentials available in
  this environment).
* Memory usage with a long history (thousands of entries) - the bounded queries are designed for it,
  but the numbers are not measured yet.
