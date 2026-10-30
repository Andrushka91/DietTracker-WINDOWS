-- DietTracker schema v11
-- Disables the 12-hour clock: the shipped default is the 24-hour clock (Use12HourClock = 0) and an
-- installation that still carries the old on-by-default value is aligned with it when the next
-- start applies this migration. The corrected preferences are also queued as one pending journal
-- operation for the active account, because preferences are synchronized: without that queue the
-- cloud copy would still hold Use12HourClock = 1 and a later rebuild of the local cache would take
-- it over again. Both statements are conditioned on the old value, so an installation that already
-- runs the 24-hour clock changes nothing and never queues an upload.

INSERT INTO SyncOperation
    (Id, CloudAccountId, EntityType, EntityId, OperationType, Status, EntityRevision, RetryCount,
     LastError, NextAttemptAt, CreatedAt, CompletedAt)
SELECT
    lower(substr(hex(randomblob(4)), 1, 8) || '-' ||
          substr(hex(randomblob(2)), 1, 4) || '-' ||
          substr(hex(randomblob(2)), 1, 4) || '-' ||
          substr(hex(randomblob(2)), 1, 4) || '-' ||
          hex(randomblob(6))),
    account.Id,
    5, -- EntityType.AppPreferences
    preferences.LocalProfileId,
    0, -- SyncOperationType.Upsert
    0, -- SyncOperationStatus.Pending
    1,
    0,
    NULL,
    NULL,
    strftime('%Y-%m-%dT%H:%M:%f+00:00', 'now'),
    NULL
FROM AppPreferences preferences
JOIN CloudAccount account
  ON account.IsActive = 1
 AND account.SynchronizationEnabled = 1
WHERE preferences.Use12HourClock = 1
  AND NOT EXISTS (
      SELECT 1 FROM SyncOperation queued
      WHERE queued.CloudAccountId = account.Id
        AND queued.EntityType = 5
        AND queued.EntityId = preferences.LocalProfileId
        AND queued.Status IN (0, 1, 3));

UPDATE AppPreferences
SET Use12HourClock = 0,
    SyncStatus      = 2 -- SyncStatus.PendingUpdate: the corrected row is what the queue above uploads.
WHERE Use12HourClock = 1;