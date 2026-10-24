-- DietTracker schema v8
-- Removes the third meal state: a meal is either PENDING or DONE. Rows still carrying the old
-- MISSED value (2) become PENDING again - a missed meal was never confirmed, so it has no
-- completion timestamp and stays consistent with the original CHECK constraints.
-- NOTE: the legacy CHECK (Status IN (0, 1, 2)) of schema v1 is left in place because SQLite can
-- only replace it by rebuilding the table (which MealPhoto references); the application never
-- writes 2 anymore and every read maps an unknown value back to PENDING.

UPDATE MealEntry
SET Status = 0,
    CompletedAt = NULL
WHERE Status = 2;