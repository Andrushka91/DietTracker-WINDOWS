-- DietTracker schema v2
-- Adds the manifest revision used by the synchronization engine to detect that the cloud dataset
-- was rewritten by another device without walking every object (see docs/synchronization/overview.md).
-- The migration is additive only: existing rows and history are preserved untouched.

ALTER TABLE SyncMetadata ADD COLUMN ManifestRevision TEXT NULL;

-- Records the last successful full upload per account; used by the restore preview and by the
-- "pending changes" badge without scanning the whole journal.
ALTER TABLE SyncMetadata ADD COLUMN LastFullUploadAt TEXT NULL;
