# Architecture overview

## Layers

```text
DietTracker.App (WinUI 3, net10.0-windows)
    Views / Controls / ViewModels / Converters / Styles / Resources
        |  depends on abstractions only (Core interfaces)
DietTracker.Infrastructure (net10.0-windows)
    Database | Repositories | Migrations | FileStorage | Authentication | Cloud | Synchronization
    Notifications | Security | Shell | Services
        |
DietTracker.Core (net10.0, no Windows dependency)
    Models | Enums | Interfaces | BusinessRules | Validation | Analytics | Services | Serialization
```

* `DietTracker.Core` has no dependency on Windows, XAML, SQLite or any cloud SDK. It contains the
  domain model, the business rules (the five permanent cards, the completion lifecycle, copying,
  photographs), validation, the analytics and the application service `MealTrackingService`.
* `DietTracker.Infrastructure` implements every Core interface: SQLite persistence with versioned
  migrations, the photo store, DPAPI protected token storage, the two identity providers, the two
  cloud storage providers, the shared synchronization engine, the Windows notification/reminder
  services and the shell integration (startup task, notification area).
* `DietTracker.App` is the WinUI 3 presentation layer. ViewModels use `CommunityToolkit.Mvvm`, depend
  only on Core interfaces (plus a few UI service interfaces) and never touch SQLite, the file system or
  the network directly.

## Composition

`ServiceCollectionExtensions.AddDietTrackerInfrastructure` registers the whole object graph
(repositories, providers, engine, notification services) with `Microsoft.Extensions.DependencyInjection`.
`App.BuildServices` adds the UI services (localization, dialogs, file pickers, tray icon, view models) and
the logging pipeline. `AddDietTrackerLogging` writes to the debugger output (the built-in Debug provider)
and, because that output is invisible outside a debug session, to a plain text file in the private
application data folder (`Diagnostics.FileLoggerProvider`, capped and rotated) - this is the log a tester
reads after a run. The dependency graph is intentionally acyclic:

```text
MealTrackingService -> repositories + ISyncJournal -> ISyncEngine
SyncEngine          -> repositories + ICloudStorageProviderFactory + IConflictResolver
ConflictResolver    -> repositories + outbox (no dependency on the journal service)
```

## Key design decisions

| Decision | Rationale |
|---|---|
| SQLite via `Microsoft.Data.Sqlite` with hand written, parameterized ADO.NET | No ORM overhead, full control over the transactional behaviour and the exact SQL, easy to test. |
| Versioned SQL migrations embedded as resources, applied transactionally with `PRAGMA user_version` | Store updates must never lose data; a pre-migration safety copy is produced with `VACUUM INTO`. |
| Five permanent categories modelled separately from the daily entries | The cards can be customized but never deleted; a unique index enforces one slot per profile, date and category. |
| Placeholder slots carry no content (`PlannedTime = null`) | An untouched card produces no cloud traffic and no journal noise. |
| Photographs as files, metadata in SQLite | Keeps the database small and fast; thumbnails are generated with the Windows Imaging Component. |
| Content hashes (SHA-256) per record and per photograph | Idempotent uploads, duplicate detection and integrity verification. |
| Provider agnostic synchronization engine | Google Drive has its own cursor, revision and folder semantics; the engine only sees `ICloudStorageProvider`. |
| Cloud payload = per-record JSON documents in the application folder | Incremental transfers, no whole-dataset rewrites, no SQLite file synchronization. |
| DPAPI (current user) + file based token store | Windows-backed protection without extra services; a copied file is useless on another account or machine. |
| Notification area icon via `Shell_NotifyIcon` | The Windows App SDK has no tray API; the documented Win32 API is used behind `ITrayIconService`. |

## Documented deviations from the specification

1. **Google Drive is called through its REST API** (`HttpClient`) instead of the `Google.Apis.Drive.v3`
   client library. This keeps the dependency footprint small, gives precise control over app-folder
   addressing, resumable uploads and optimistic concurrency, and avoids blocking APIs in the desktop
   process.
2. **Google OAuth uses a loopback redirect** (`http://127.0.0.1:{port}`) which is the flow Google
   requires for desktop applications; the authorization page is always shown by the external browser.
3. **`IUiContext`/localization lookups** are provided by the application layer so that view models stay
   free of XAML types while all user visible strings still come from the `.resw` resources.
4. **`docs/` also contains `performance/`** in addition to the folders suggested by the specification,
   because measured performance data is part of the deliverables.

## Error handling and resilience

* Every user action is a local, transactional database write first; the journal is written afterwards.
* A failing cloud provider can never corrupt or block local data: the cycle is caught, the state becomes
  `Error`, and the operations stay in the outbox with exponential backoff (5 s, 15 s, 45 s ... capped at
  30 minutes).
* Authentication failures from background work are surfaced as `AuthenticationRequired` instead of an
  interactive prompt.
* The integrity checker (`IDataIntegrityChecker`) validates the five categories, one slot per date and
  category, orphaned photographs and inconsistent completion state; it is exposed in Settings.
