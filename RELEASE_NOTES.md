# Release notes — Diet Tracker

## 1.0.0

_First release of Diet Tracker for Windows: a meal planner for Windows 11 that keeps the plan in the
user's own Google Drive account and works offline from a local working copy._

### Highlights

* **Five permanent daily meal cards** (breakfast, morning snack, lunch, afternoon snack, dinner)
  with inline editing, immediate local persistence and a responsive two-column grid that keeps the
  meal order and equal card heights at any window size.
* **Completion tracking** with DONE / pending states set explicitly by the user, coloured counters
  on the dashboard, day colours in the calendar and proportional state shares in the statistics.
* **Photographs** per meal (JPEG/PNG/WEBP, up to three per meal) with locally generated thumbnails.
* **Native Windows reminders** with snooze, "mark as DONE" actions and deep links back into the app,
  plus notification-area presence with restore/exit.
* **Single instance**: a second launch activates the window that is already open (restoring it from
  the notification area when it was closed there) and ends immediately, so two copies can never write
  to the same database. A crashed instance does not block the next start (the mutex is taken over).
* **Offline capable**: edits are written to local SQLite first, so the interface never waits for the
  network. That database is the working copy of the account that is signed in - the cloud copy is the
  one that survives. Every feature works without an account; records created before the first sign-in
  are adopted by that account.

### Cloud synchronization (Google)

* **Sign-in with Google** through the official OAuth 2.0 authorization code flow with PKCE in the
  system browser; tokens are stored DPAPI-protected and sessions are restored silently on the next
  start.
* **Google Drive `appDataFolder` storage** — a hidden, application-private folder; Diet Tracker never
  sees other files in the user's Drive.
* **Change tracking and conflict handling** with content hashes, provider revisions and an explicit
  conflict resolution; uploads are idempotent.
* **Visible feedback**: the status bar reports the synchronization state, the settings page shows the
  last successful synchronization, and a tray notification reports how many records were uploaded and
  downloaded.
* **Sign in / sign out states** are reflected in the interface, including the connected account name
  and e-mail address. The e-mail is the headline of the account card in **Settings**, with the
  sign-in / sign-out button directly below it, the remaining cloud space under it and, instead of an
  upload switch, the two captions that state exactly what a signed-in account stores. The separate
  "Sign in" section and the "Account and sync" page are gone: every account and synchronization action
  lives on the settings page.
* **One account at a time, nothing kept behind**: connecting an account downloads its dataset
  immediately, so the meals of that account are visible at once and the records of a previously
  connected account are gone. Signing out uploads everything that is still pending and then deletes
  the meals, photographs, templates and synchronization journal from the device. If a change cannot be
  uploaded (offline, uploads switched off) the sign-out is refused rather than deleting the only copy.
* **Uploads leave the device as soon as they are saved** - a meal or a photograph is on its way within
  seconds. The schedule in Settings ("Automatic synchronization") decides how often the app also
  checks the cloud for changes made on other devices; "only with the button below" still keeps every
  automatic cycle off.
* **Photographs attached while signed out are uploaded after signing in**: the push phase reconciles
  local photographs that never reached the journal, and identifies each binary by its logical path
  (`photos/<guid>.bin`) instead of a provider revision token.
* **Truthful state reporting**: "no account connected" is only shown when there really is none; a
  connected account with uploads switched off reports "synchronization is turned off", and the last
  successful synchronization time is restored from the account after a restart.

### Settings page

* **No Save button, nothing to forget**: every switch (Start with Windows, Minimize to the
  notification area, 12 hour clock) and the automatic synchronization interval are written the moment
  they change, and the card confirms the write. Filling the page from the database never writes back.
* **The upload consent switch is gone**: signing in is the consent. Once an account is connected the
  meals and photographs are stored in it automatically, and the account card explains exactly what is
  uploaded instead of asking again.
* **Privacy and backup are one card**: the storage facts, the export, the restore and the integrity
  check share a single section. The destructive buttons (delete photographs, delete local data, delete
  cloud data, disable synchronization) were removed - the operations still exist in the service layer
  (the sign-out flow uses them) but they are no longer offered in the interface.
* **Export data writes a PDF**: the save dialog offers *Diet Tracker backup* (`.dtbackup`, restorable)
  and *PDF report* (`.pdf`, printable). The report is written by a small built-in PDF writer - no third
  party library, no embedded font, an A4 page per part of the history, a day heading per date, one row
  per meal with time, slot, state, title and description, a summary of done/missed/pending and
  `Page x of y` in the footer.
* **The cards are aligned**: the page is a two column, three row grid instead of two independent
  columns, so the two cards of a row always share their top and bottom edge; the row grows with the
  taller card, and the About card spans the full width at the bottom.

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

* **The interface-language switch was removed**: Diet Tracker ships in English only. The old
  `Language` combo box wrote a preference that could only take effect on the next start (MRT resolves
  the resource language once), so choosing a different language looked broken. The translated resource
  files stay in the repository but are no longer selectable.
* **Microsoft account sign-in and OneDrive synchronization** were removed together with the guarded
  provider switching; Diet Tracker synchronizes with Google Drive only in this version. The MSAL
  packages are no longer referenced, and the provider value layout keeps existing databases
  migration-free.

### Known limitations

* Real Google sign-in and synchronization require the per-installation Google Cloud registration;
  the flow has been verified interactively, but large-folder edge cases have not been exercised.
* Store publication has not happened yet; the packaged (MSIX) installation path is not verified in
  this environment. See `docs/known-limitations.md` for the complete list.
