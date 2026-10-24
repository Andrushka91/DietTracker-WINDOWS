# DietTracker

A native Windows 11 desktop application (WinUI 3, .NET 10, C#) for planning five daily meals,
tracking what was actually eaten, attaching photographs, receiving native Windows reminders and
synchronizing the data with the user's own Google Drive account.

The application is **offline first**: SQLite is the authoritative working database and every feature
works without an account. Signing in is optional and only adds secure cloud synchronization and
data recovery.

## Highlights

* **Five permanent meal cards** per day (Breakfast, Morning Snack, Lunch, Afternoon Snack, Dinner)
  that can be customized but never deleted; empty cards stay visible. The cards are laid out in a
  responsive grid that keeps the meal order at any window size, and all cards share the same height -
  nothing scrolls inside a card.
* **Inline editing** inside each card, with save/cancel and immediate local persistence.
* **DONE / missed / pending** tracking with completion timestamps and idempotent completion.
  Pending is the initial state of the database and of the business logic: a day the user has not
  touched stays pending (gray), the green check confirms a meal (done, green) and the red cross
  marks it as missed (red). The three states are always set by the user, never inferred from the
  clock - a skipped day keeps its pending meals - and the date decides what is allowed: today and
  past days can be edited in both directions (so a forgotten day can still be corrected), while
  future days are locked in pending no matter how often the buttons are pressed. The dashboard
  shows the three counts as coloured squares, the calendar colours each day accordingly, and the
  statistics split every period into done, missed and pending shares.
* **Photographs** (JPEG/PNG/WEBP, max 3 per meal, max 10 MB each) with automatic thumbnails stored as
  files in the private application data folder - never as database BLOBs.
* **Native Windows notifications** with snooze, "mark as DONE" and deep links back into the app.
* **Google account** sign-in (OAuth 2.0 with PKCE in the system browser, Google Drive
  `appDataFolder`) with silent session restoration.
* **Three interface languages**: Romanian (default), English and Spanish.
* **Light and dark appearance that follows Windows**: the whole interface and the window title bar
  switch together with the system setting for apps, including while the application is running.
* **MSIX packaging** ready for Microsoft Store submission.

## Repository layout

```text
DietTracker.sln
src/DietTracker.Core             domain models, business rules, validation, analytics (net10.0)
src/DietTracker.Infrastructure   SQLite, repositories, migrations, auth, cloud, sync, notifications
src/DietTracker.App              WinUI 3 application (MVVM, XAML, MSIX packaging)
tests/DietTracker.UnitTests      business rules, database, localization parity
tests/DietTracker.IntegrationTests  offline-first flow, multi-device sync, conflicts, backup
  tests/DietTracker.UITests      launches the real application (category: UI)
tools/Run-App.ps1                builds the unpackaged application and starts it (with diagnostics)
tools/Smoke-Test.ps1             start-up/performance smoke test (window, memory, responsiveness)
tools/Generate-StoreAssets.ps1   Store and tile artwork
docs/                            architecture, authentication, synchronization, privacy, Store
```

## Prerequisites

| Requirement | Version |
|---|---|
| Windows | 11 (build 22621 or later) |
| .NET SDK | 10.0 (10.0.402 or later) |
| Windows App SDK runtime | 2.5 (installed with the app, or the runtime redistributable) |
| NuGet packages | restored automatically |
| Windows Developer Mode | only needed to sideload/register the unsigned MSIX test package |

Visual Studio is **not** required: the whole solution (application + tests) builds with the .NET SDK
alone (verified with `dotnet build DietTracker.sln -c Debug -p:Platform=x64`).

## Run the app

### Quick reference - the easy way (npm-style shortcuts)

`package.json` in the repository root provides short commands. There is **no** Node.js code in this
repository and **no** `npm install` needed - npm is only used as a friendly command launcher.
There is no "rebuild" step to remember: **`npm start` always builds whatever changed and runs it.**

| Command | What it does | Where the built .exe goes |
|---|---|---|
| **`npm start`** | **build + run - the one command after every change** | `dist/exec/DietTracker.exe` - and it is started for you |
| `npm run build` | build only, no start | `dist/exec/DietTracker.exe` |
| `npm run start:debug` | build + run a debuggable build (only when debugging) | `dist/debug/DietTracker.exe` |
| `npm run build:debug` | debuggable build only | `dist/debug/DietTracker.exe` |
| `npm test` | run the unit tests | (no app .exe) |
| `npm run test:all` | unit + integration tests | (no app .exe) |
| `npm run test:ui` | UI smoke tests (start the real app) | uses the `dist/exec` .exe |

**Where the exe file goes after `npm run build`:**

```text
dist/exec/DietTracker.exe
```

`dist/` is the conventional folder name for the built application - the same convention the npm/webpack
world uses - so it is always obvious where the executable is, and `exec` means "executable": the
optimized application you actually run. That folder is the complete application: besides
`DietTracker.exe` it contains the freshly built `DietTracker.Core.dll`,
`DietTracker.Infrastructure.dll` and all XAML resources - so always start the exe **from its folder**.

**What about `Debug`?** A `Debug` build is unoptimized and carries debug symbols - it exists only for
debugging sessions with Visual Studio or a debugger, never for normal use. That is why the commands
used every day build the optimized application into `dist/exec/` (MSBuild calls this the `Release`
configuration). A debuggable build goes to its own `dist/debug/` folder, and only when explicitly
asked for with `npm run build:debug`. The two never mix, so a debug build can never be mistaken for
the real application.

Without npm, the same build is (forward slashes on purpose: they work in PowerShell, CMD and Git Bash
alike, while backslashes are eaten by bash-style shells and the build fails with `MSB1009`):

```powershell
dotnet build src/DietTracker.App/DietTracker.App.csproj -c Release -p:Platform=x64 -m:1 -p:WindowsPackageType=None -p:EnableMsixTooling=false
```

### After every change - the workflow

1. Edit the sources (which project? see "Which sources do I edit?" below).
2. Run **`npm start`**. That is the whole workflow: it compiles everything that changed and starts the
   app. There is no separate "rebuild" step - the build is incremental and always produces a fresh
   executable.
3. Close the app window when you are done. (`npm start` closes a leftover running instance for you,
   because a running app locks `DietTracker.exe`.)

> **The app still behaves like the old version?** Then a stale .exe was started by hand instead of
> running `npm start`. `dist/exec` and `dist/debug` are separate folders - building one does **not** update the
> other.

`npm start` is a thin wrapper around `tools/Run-App.ps1`, which you can also call directly (it builds,
stops a running instance, starts the app and diagnoses the packaged/unpackaged mix-up):

```powershell
powershell -ExecutionPolicy Bypass -File tools/Run-App.ps1                        # optimized app (default)
powershell -ExecutionPolicy Bypass -File tools/Run-App.ps1 -Configuration Debug   # debuggable build
```

### Which sources do I edit? (and why one build command is enough)

| Project | What lives there |
|---|---|
| `src/DietTracker.Core` | business logic: models, rules, statistics/progress calculations, validation |
| `src/DietTracker.Infrastructure` | SQLite database, repositories, synchronization, notifications |
| `src/DietTracker.App` | the user interface: XAML pages, meal cards, view models |

You only edit the project that owns what you are changing: a statistics calculation lives in
`DietTracker.Core`, a database query in `DietTracker.Infrastructure`, a card layout in
`DietTracker.App`. There is never a reason to touch the other two.

**You never build these projects separately.** `DietTracker.App` references `DietTracker.Core` and
`DietTracker.Infrastructure`, so the one build command (`npm start` or `npm run build`) compiles all
three automatically and copies the fresh `DietTracker.Core.dll` and `DietTracker.Infrastructure.dll`
next to `DietTracker.exe`. The build is *incremental*: after editing a `.Core` source it notices the
change and recompiles just that project before producing the executable - so "edit the source, run
`npm start`" is the complete loop, with no manual rebuild-from-scratch step anywhere.

### Details and troubleshooting

* Start it from its own folder: that is where its DLLs, XAML resources and assets live.
* `-p:WindowsPackageType=None` is what makes the .exe startable by hand. The project default is `MSIX`, and
  an MSIX-typed `DietTracker.exe` refuses to run outside its package - it exits at once with
  `Class not registered (0x80040154)`. For the package and the Store see
  [docs/microsoft-store/submission.md](docs/microsoft-store/submission.md).
* **Nothing happens when you start it?** Then you started the executable of a *packaged* layout: it closes
  immediately, without any message, because it needs the package identity that only an installed MSIX
  provides. Look for an `AppxManifest.xml` next to the .exe; if it is there, rebuild the unpackaged flavour
  with the command above (`tools\Run-App.ps1` detects this case and says so). The reason is also recorded in
  Event Viewer > Windows Logs > Application (source `.NET Runtime`) and in the log file below.
* First run creates an empty database at `%LOCALAPPDATA%\DietTracker\database\diettracker.db`; delete
  `%LOCALAPPDATA%\DietTracker` to start over.
* Build everything (application + tests) with one command -
  `dotnet build DietTracker.sln -c Debug -p:Platform=x64 -m:1` - then run the application build above
  afterwards, because the solution build leaves the packaged flavour.

### Packaged build (optional - the Store package)

```powershell
# Developer Mode must be enabled (Settings > System > For developers): the test package is unsigned.
dotnet build src/DietTracker.App/DietTracker.App.csproj -c Release -p:Platform=x64 -m:1 -p:GenerateAppxPackageOnBuild=true
Add-AppxPackage -Path artifacts/msix/DietTracker.App_1.0.0.0_Test/DietTracker.App_1.0.0.0_x64.msix
```

The package (`.msix` plus `.msixbundle`, unsigned, ~99 MB) is written to
`artifacts\msix\DietTracker.App_1.0.0.0_Test\`. Installing and running the packaged application
(notifications, the `startupTask` extension) was **not** verified in this environment -
see [docs/known-limitations.md](docs/known-limitations.md) item 4.

### Where the data lives

| Build | Data folder |
|---|---|
| Unpackaged | `%LOCALAPPDATA%\DietTracker` (`database\`, `photos\`, `backups\`, `logs\`, `secure\`) |
| Packaged (MSIX) | the package's private `LocalState` folder |

## Test the app (and read its logs)

### 1. Start-up test with measurements

```powershell
powershell -ExecutionPolicy Bypass -File tools\Smoke-Test.ps1
```

The script starts the built application, waits for its main window, reports what it measured and closes it
again. Output measured in this repository (Debug build):

```text
Starting ...\dist\exec\DietTracker.exe

StartupSeconds  : 0.62
WorkingSetMB    : 174.9
PrivateMemoryMB : 117.2
Threads         : 100
Handles         : 1348
Responding      : True
MainWindowTitle : DietTracker
```

A window title and `Responding : True` mean the application really came up (0.6-1.7 s here). The script
fails loudly, with the exit code, if the process dies during start-up or shows no window within 60 seconds.
Add `-Configuration Release` to check the Release build.

### 2. Read the log file

```powershell
# use the application for a while, then:
Get-Content "$env:LOCALAPPDATA\DietTracker\logs\diettracker.log" -Tail 40
```

Every message of level *information* and above is written to
`%LOCALAPPDATA%\DietTracker\logs\diettracker.log` (packaged build: the package's `LocalState` folder). The
file is capped at 1 MB and rotated once to `diettracker.log.1`; each message is written with a short lived
file handle, so you can read the log while the application is running. A first start looks like this:

```text
2026-10-19 15:18:50.422 +02:00 [INF] DietTracker.Infrastructure.Database.DatabaseInitializer: Applying database migration DietTracker.Infrastructure.Database.Migrations.0001_initial.sql.
2026-10-19 15:18:50.436 +02:00 [INF] DietTracker.Infrastructure.Database.DatabaseInitializer: Applying database migration DietTracker.Infrastructure.Database.Migrations.0002_sync_manifest_revision.sql.
2026-10-19 15:18:50.437 +02:00 [INF] DietTracker.Infrastructure.Database.DatabaseInitializer: Applying database migration DietTracker.Infrastructure.Database.Migrations.0003_onboarding_flag.sql.
2026-10-19 15:18:50.439 +02:00 [INF] DietTracker.Infrastructure.Database.DatabaseInitializer: Database ready at '%LOCALAPPDATA%\DietTracker\database\diettracker.db' (schema 0 -> 3, 3 migration(s) applied).
```

* Level codes: `TRC` `DBG` `INF` `WRN` `ERR` `CRT`. The same messages also go to the debugger output (the
  Debug provider), and they never contain credentials, tokens or personal data.
* If the process dies before it can log anything (a missing runtime, for example), Windows has the reason:
  Event Viewer > Windows Logs > Application (sources `.NET Runtime` and `Application Error`).

### 3. Automated tests

```bash
# Build the application first (see "Run the app"): the UI tests start that exact executable.
dotnet test tests/DietTracker.UnitTests          -c Debug -p:Platform=x64   # 83 tests, ~0.8 s
dotnet test tests/DietTracker.IntegrationTests   -c Debug -p:Platform=x64   # 15 tests, ~0.7 s
dotnet test tests/DietTracker.UITests            -c Debug -p:Platform=x64   # 2 tests, ~5 s, starts the real app
```

Each test prints its result to the console (pass/fail plus the exception and stack trace of failures). The
integration assembly is named `DietTracker.Sync.Tests` and is written to
`artifacts\tests\integration\<Configuration>\net10.0-windows10.0.26100.0\win-x64`. The UI tests delete
`%LOCALAPPDATA%\DietTracker\database\diettracker.db` before starting the application, so do not run them
while you are using the unpackaged build yourself.

## Configuration of the identity provider

DietTracker contains no secret in its source. The Google client identifier and client secret are read
from an optional `%LOCALAPPDATA%\DietTracker\appsettings.json`; the built-in default is a placeholder
that must be replaced with your own registration before a real sign-in can succeed. See
[docs/authentication/setup.md](docs/authentication/setup.md).

## Status and honesty statement

* Core, infrastructure, the WinUI application, the tests and the packaging build cleanly in this
  environment, and the application launches and creates its database (measured: 0.39 s to database
  ready, ~138 MB working set in a Debug build).
* The cloud provider is implemented against the documented Google Drive v3 REST API but could **not**
  be exercised against a real account here: no Google client registration existed in this
  environment, and interactive consent requires a human. The provider code paths are covered by tests
  with an in-memory provider that mimics revisions and change tracking.
* Microsoft Store publication has **not** happened: it requires a Partner Center account, a reserved
  name and Microsoft certification. All artifacts and instructions are prepared in
  [docs/microsoft-store/submission.md](docs/microsoft-store/submission.md).
* See [docs/known-limitations.md](docs/known-limitations.md) for the complete, candid list.
