# Synchronization

## Offline first

1. The user edits a meal -> the change is written to SQLite in a transaction.
2. The change is recorded in the durable outbox (`SyncOperation`).
3. The user interface updates immediately.
4. A debounced background cycle (3 s) uploads the change when an account is connected and the machine
   is online. The user never waits for the cloud.

Triggers: after sign-in, at startup, after local changes (debounced), when connectivity returns, on
"Synchronize now", and periodically (the scheduler waits at most five minutes between cycles).

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
`AuthenticationRequired`, `Error` and `Paused`. `Synced` is only reported after the provider confirmed
the upload, and the last successful synchronization time is shown next to it. That confirmation hides
itself after six seconds (it waits while the pointer rests on it, and it comes back for the next
finished cycle); offline, sign-in and error states stay on screen until they are resolved.

## Restore and recovery

* `PreviewRestoreAsync` lists what the connected account holds (counts and date range) without
  downloading anything.
* `RestoreAsync` applies the whole dataset; local data is never deleted, so a failed restore is
  harmless. Photographs are restored after their meal entry exists locally (a second cycle completes
  the restore if the meal arrived in the same pass).
* `PushAllAsync` queues every local record (used after switching provider).
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
