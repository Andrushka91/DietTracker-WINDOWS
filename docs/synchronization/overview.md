# Synchronization

## Cloud-only data lifecycle

The cloud account is the copy that survives; the local SQLite database is a **cache of exactly one
account**. `ICloudDataSession` (`CloudDataSession`) owns the boundaries of that cache:

| Moment | What happens |
|---|---|
| Start-up | A connected account keeps its cache. Without an account, a cache that survived (a crash between the two steps below) is deleted - unless the journal still holds changes that never reached the cloud, or the installation was never connected: those records are the only copy and survive until a sign-in can upload them. |
| Before a sign-in | Everything the current account still has pending is uploaded. A change that cannot be uploaded refuses the sign-in; the cache is **not** touched yet, so a cancelled sign-in costs nothing. Records the cloud has never seen (a first sign-in, or a cache that survived a crash) are adopted by the account instead of being deleted. |
| After a sign-in | The cache is emptied (records, photographs, journal, cursor, conflicts and tombstones) and the dataset of the connected account is downloaded at once (`RebuildFromCloudAsync`), so the screens show that account only. |
| Sign-out | Pending changes are uploaded, the credentials are removed, then the whole local cache is deleted. If changes cannot be uploaded (offline, uploads switched off) the operation is refused, because deleting the cache would delete the only copy; the user can explicitly discard them. |

Two details make this work reliably:

* **Nothing is deleted before the provider confirmed the upload.** `CountPendingAsync` on the durable
  journal decides whether a sign-out may continue; the refusal carries
  `AccountTransitionResult.PendingChangesReason` and the number of changes.
* **A rebuilt cache must accept its own documents.** The regular pull skips documents whose `deviceId`
  is this installation (they are echoes of a local change that is still in SQLite) - but only while
  the local record still exists. After a purge the same document is missing data and is applied, and
  `RebuildFromCloudAsync` applies every document of the account regardless, so an interrupted rebuild
  is repaired by the next cycle.

The dataset is still scoped per account in the cloud (each provider account has its own application
folder), and every row carries the local profile that owns it. The purge deletes records of every
profile, so a cache written by an earlier account can never be shown to the next one.

## Offline first inside a session

1. The user edits a meal -> the change is written to SQLite in a transaction.
2. The change is recorded in the durable outbox (`SyncOperation`).
3. The user interface updates immediately.
4. A debounced background cycle (3 s) uploads the change when an account is connected, uploads are
   allowed and the machine is online. The user never waits for the cloud.

Triggers: after sign-in, at startup, after local changes (debounced), when connectivity returns, on
"Synchronize now", and periodically (the scheduler knocks once an hour).

The schedule chosen in Settings ("Automatic synchronization": weekly, monthly, or only with the
button) decides how often the engine also *checks the cloud on its own* - it never delays an upload
that was just journaled. A meal or a photograph is on its way within seconds, which is what makes the
cloud copy trustworthy; only "only with the button below" keeps every automatic cycle switched off.

## Cloud layout (both providers)

```text
<application folder>/
    entities/MealEntry/<guid>.json        one document per record
    entities/MealPhoto/<guid>.json        photograph metadata
    entities/MealTemplate/<guid>.json
    entities/ReminderSettings/<guid>.json
    entities/AppPreferences/<guid>.json
    photos/<guid>.bin                     photograph bytes
```

* OneDrive: the application specific folder `/me/drive/special/approot` (permission
  `Files.ReadWrite.AppFolder`). Uploads use simple PUT up to 4 MB and a resumable upload session above
  that; change tracking uses the Graph **delta** feed rooted at the app folder.
* Google Drive: the hidden `appDataFolder`; files are stored with flat names
  (`entities__MealEntry__<guid>.json`) and mapped back to logical paths. Google's change feed cannot be
  scoped to `appDataFolder`, so Diet Tracker uses a **modified-time watermark** (one minute in the past
to absorb clock skew); every apply operation is idempotent, so examining an object twice is harmless.

## Payload

```json
{
  "schemaVersion": 1,
  "entityType": "MealEntry",
  "entityId": "<guid>",
  "localProfileId": "<guid>",
  "revision": 7,
  "updatedAt": "2026-03-10T13:31:02+00:00",
  "deviceId": "<installation id>",
  "isDeleted": false,
  "contentHash": "<sha-256 of the payload>",
  "payload": { }
}
```

## Conflict policy

The decision prefers provider revisions and content hashes over local wall-clock timestamps:

1. identical `contentHash` -> no change;
2. `deviceId` equals this installation -> echo of our own upload, accept it;
3. the cloud revision still equals the revision the local record was based on -> only the local side
   changed, keep the local version and re-upload;
4. the remote revision is higher -> the other device is ahead, accept the remote version;
5. otherwise (same revision, different content, no usable revision token) -> **both versions are
   preserved** in `ConflictRecord` and the user is asked which one to keep. "Keep both" stores the local
   version as a template named *Conflict copy* so nothing is lost.

Deletions travel as tombstones. A meal slot deleted on another device is *cleared* rather than removed,
because the five cards are permanent.

## Status reporting

The engine reports `NotConfigured`, `Synced`, `Syncing`, `PendingChanges`, `Offline`,
`AuthenticationRequired`, `Error` and `Paused`. `NotConfigured` means *no account is connected* - a
signed-in account is never reported that way: with uploads switched off it is `Paused`, and before the
first cycle it is `PendingChanges`. The state is derived from the stored account at start-up
(`ISyncEngine.RefreshStateAsync`), so the screens do not depend on a cycle having run, and the stored
"last successful synchronization" timestamp survives a restart. `Synced` is only reported after the
provider confirmed the upload, and the last successful synchronization time is shown next to it. That confirmation hides
itself after six seconds (it waits while the pointer rests on it, and it comes back for the next
finished cycle); offline, sign-in and error states stay on screen until they are resolved.

## Progress reporting for long running work

Signing in, signing out, "Synchronize now", "Restore from the cloud" and "Delete cloud data" run
behind a window wide overlay: a dark translucent scrim, a spinner and a sentence that names the
current stage ("Uploading your changes...", "Signing out and deleting the local data...",
"Downloading your account's data..."). The overlay swallows pointer and keyboard input, so a second
action cannot be started by accident. While a **sign-out or a synchronization** runs, the window
cannot be closed either - not with the caption bar button and not from the notification area - because
stopping in the middle would leave the local cache half deleted; a two minute safety limit releases
the block so a provider that never answers cannot trap the user. The sign-out button performs the
upload itself, so "Synchronize now" is never a prerequisite for leaving the account.

## Restore and recovery

* `PreviewRestoreAsync` lists what the connected account holds (counts and date range) without
  downloading anything.
* `RestoreAsync` merges the whole dataset into the cache (it never deletes anything by itself, so a
  failed restore is harmless). Photographs are restored after their meal entry exists locally (a second
  cycle completes the restore if the meal arrived in the same pass).
* `RebuildFromCloudAsync` is the variant used by the cloud-only lifecycle: the cache is empty at that
  point, so every document - including this installation's own uploads - is applied.
* `PushAllAsync` queues every local record (used when a first sign-in adopts the records that were
  created without an account, and after switching provider).
* A local export/backup (`.dtbackup` = ZIP with `backup.json` plus the photographs) is available
  independently of any cloud provider, and a safety copy is written before provider switches and before
  local data deletion.

## Known provider differences handled by the adapters

| Aspect | OneDrive | Google Drive |
|---|---|---|
| Addressing | logical paths (app folder) | flat file names in `appDataFolder`, mapped to logical paths |
| Revisions | `eTag` (used for `If-Match`) | file `version` |
| Change tracking | delta link (opaque cursor) | modified-time watermark |
| Large uploads | resumable session | multipart upload |
| Quota | `/me/drive?$select=quota` | `about?fields=storageQuota` |
