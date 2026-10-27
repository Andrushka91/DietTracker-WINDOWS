-- DietTracker schema v10
-- Records how often the cloud is contacted automatically: weekly (the default), monthly, or only
-- through the explicit "Synchronize now" button. Existing installations start at weekly, which is
-- the schedule the older builds approximated with a cycle after every single local edit. Additive
-- only; the journal keeps every change in the meantime, so nothing waits for the schedule twice.

ALTER TABLE AppPreferences ADD COLUMN SyncFrequency INTEGER NOT NULL DEFAULT 1;