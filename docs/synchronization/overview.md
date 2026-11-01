# Synchronization

## Cloud-only data lifecycle

The cloud account is the copy that survives; the local SQLite database is a **cache of exactly one
account**. `ICloudDataSession` (`CloudDataSession`) owns the boundaries of that cache:

| Moment | What happens |
|---|---|
| Start-up | A connected account keeps its cache. Without an account, a cache that survived (a crash between the two steps below) is deleted - unless the journal still holds changes that never reached the cloud, or the installation was never connected: those records are the only copy and survive until a sign-in can upload them. |
| Before a sign-in | Everything the current account still has pending is uploaded (a push-only cycle that ignores the retry backoff, exactly like before a sign-out). A change that cannot be uploaded refuses the sign-in; the cache is **not** touched yet, so a cancelled sign-in costs nothing. Records the cloud has never seen (a first sign-in, or a cache that survived a crash) are adopted by the account instead of being deleted. |
| After a sign-in | The cache is emptied (records, photographs, journal, cursor, conflicts and tombstones) and the dataset of the connected account is downloaded at once (`RebuildFromCloudAsync`), so the screens show that account only. |
| Sign-out | The outgoing journal is uploaded - a push-only cycle that ignores the retry backoff and runs even while automatic uploads are paused - then the credentials are removed and the whole local cache is deleted. If changes still cannot be uploaded (offline, provider error) the operation is refused, because deleting the cache would delete the only copy; the app reports that in the status line and keeps everything. |

Two details make this work reliably:

* **Nothing is deleted before the provider confirmed the upload.** `CountPendingAsync` on the durable
  journal decides whether a sign-out may continue; the refusal carries
  `AccountTransitionResult.PendingChangesReason` and the number of changes. The upload of a
  transition is push-only (the change feed is not read for data that is about to be deleted or
  replaced), runs even while automatic uploads are paused, uploads operations that wait for their
  retry backoff instead of silently counting them, and is repeated a few times - so a flaky
  connection waits instead of failing, and the interface reports a persistent refusal in the status
  line without ever offering to discard the data.
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
"Synchronize now", periodically (the scheduler knocks once an hour) and by the **live pull** of an
open window.

The schedule chosen in Settings ("Automatic synchronization": weekly, monthly, or only with the
button) decides how often the engine also *checks the cloud on its own* - it never delays an upload
that was just journaled. A meal or a photograph is on its way within seconds, which is what makes the
cloud copy trustworthy; only "only with the button below" keeps every automatic cycle switched off.

### Live pull of an open window

A receiving device must not wait a week to see the other device's meal, so an open window asks for a
silent, **pull-only** cycle (`SyncTrigger.AutoPull`): every 20 s while it has the focus, every 60 s
while it is open but unfocused. A window hidden in the notification area asks nothing at all. The
engine throttles the request (10 s minimum between two passive downloads) and never uploads in this
mode - a local edit runs its own cycle - so an idle window costs one small change-feed call per
interval and nothing else.

* Two consecutive triggers coalesce, but a request that arrives **while a cycle is running** is
  remembered and runs one more pass: an edit committed after the journal was read is uploaded at
  once instead of waiting for the next scheduled knock.
* A passive pull that brought no record keeps the previous state, so the status strip does not blink
  every few seconds; one that materialized records publishes the result, which is what refreshes the
  visible day. The dashboard reload skips the reload while one of its cards has an open editor, so a
  background import never discards typing in progress.
* Windows already reports "unchanged" for a document whose payload it holds, and the mobile port now
  does the same: the provider re-lists objects inside its clock-skew window, and re-applying them is
  idempotent, but counting them as downloads would flash the status strip and reload the day.

The same rules exist on Android with the intervals above; the poll pauses while the app is in the
background and pulls immediately when it comes back to the foreground.

### Parallel downloads (why a first synchronization is fast)

A pull is **latency bound**: every document costs one round trip, while applying it costs a few
milliseconds of SQLite. The pull therefore downloads through a bounded ordered window
(`OrderedPrefetcher`, 8 documents) and applies the results **in the order of the change feed**:

* up to eight documents are travelling while the current one is applied, so the connection never
  idles between two documents - the same "transfers" idea every desktop sync client uses;
* the window is small on purpose: an unbounded `Task.WhenAll` over a few hundred photographs would
  open one connection per record, exhaust the provider's per-user quota and keep every payload in
  memory;
* the order is preserved because the apply side owns the invariants - a photograph is only written
  after its meal slot, and the cursor may not pass a document that still has to be retried;
* a photograph's **binary** starts downloading as soon as its metadata document arrived
  (`PhotoBytePrefetcher`, two pictures in memory), so the apply step awaits bytes that are already on
  their way instead of asking for them;
* the mobile app pulls with the same design (`src/cloud/parallel.ts`: window of 8 documents, two
  prefetched pictures, plus the listing of the push phase reused by the pull phase of the same cycle
  - one folder listing per cycle instead of two).

The savings are proportional to the number of documents: twenty changes used to cost twenty
sequential round trips, they now cost three "waves" of eight.

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

**A count replaces the spinner whenever the work can be counted.** An account transition and a manual
synchronization report one frame per settled document (`AccountTransitionProgress` /
`RestoreProgress`), so the same overlay paints the determinate bar the mobile app has always had:

| Phase | Bar | Line under it |
|---|---|---|
| sign-in / sign-out upload | "n of N" of the journal | "n of N change(s) synced with the cloud" |
| sign-in download (rebuild) | "n of N" of the account's documents | "n of N document(s) received from the cloud" |
| "Synchronize now" | same two phases, in order | same lines |

A phase that cannot be counted (the browser round trip, emptying the cache) keeps the spinner: an
empty bar would claim progress that is not measurable yet. The bar is the *visible* half of the
parallel pull above - a first sign-in of a full account now moves in steps of eight documents instead
of crawling one document at a time.

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
