# Release notes — Diet Tracker

## 1.0.0

_First release of Diet Tracker for Windows: an offline-first meal planner for Windows 11 with
optional, private cloud synchronization through the user's own Google Drive account._

### Highlights

* **Five permanent daily meal cards** (breakfast, morning snack, lunch, afternoon snack, dinner)
  with inline editing, immediate local persistence and a responsive two-column grid that keeps the
  meal order and equal card heights at any window size.
* **Completion tracking** with DONE / pending states set explicitly by the user, coloured counters
  on the dashboard, day colours in the calendar and proportional state shares in the statistics.
* **Photographs** per meal (JPEG/PNG/WEBP, up to three per meal) with locally generated thumbnails.
* **Native Windows reminders** with snooze, "mark as DONE" actions and deep links back into the app,
  plus notification-area presence with restore/exit.
* **Offline first**: local SQLite is the authoritative database; every feature works without an
  account (Guest Mode).

### Cloud synchronization (Google)

* **Sign-in with Google** through the official OAuth 2.0 authorization code flow with PKCE in the
  system browser; tokens are stored DPAPI-protected and sessions are restored silently on the next
  start.
* **Google Drive `appDataFolder` storage** — a hidden, application-private folder; Diet Tracker never
  sees other files in the user's Drive.
* **Change tracking and conflict handling** with content hashes, provider revisions and an explicit
  conflict resolution; uploads are idempotent.
* **Visible feedback**: the status bar reports the synchronization state, the account page shows the
  last successful synchronization, and a tray notification reports how many records were uploaded and
  downloaded.
* **Sign in / sign out states** are reflected in the interface, including the connected account name
  and e-mail address.

### Changed

* The identity-provider configuration moved to a per-user settings file
  (`%LOCALAPPDATA%\DietTracker\appsettings.json`, case-insensitive keys, `//` comments allowed) that
  carries the Google client identifier and client secret of the installation. A missing or
  misconfigured registration fails fast with a clear, localized message instead of opening the
  browser into a Google error page.
* The page the browser lands on after the authorization redirect is now a branded Diet Tracker card
  that follows the system light/dark preference and closes itself when the browser allows it.
* Sign-in failures surface localized, user-readable messages in English, Romanian and Spanish.
* Database schema updated to **v9** (migrations 0004–0009); a safety copy is written before every
  migration.

### Removed

* **Microsoft account sign-in and OneDrive synchronization** were removed together with the guarded
  provider switching; Diet Tracker synchronizes with Google Drive only in this version. The MSAL
  packages are no longer referenced, and the provider value layout keeps existing databases
  migration-free.

### Known limitations

* Real Google sign-in and synchronization require the per-installation Google Cloud registration;
  the flow has been verified interactively, but large-folder edge cases have not been exercised.
* Store publication has not happened yet; the packaged (MSIX) installation path is not verified in
  this environment. See `docs/known-limitations.md` for the complete list.
