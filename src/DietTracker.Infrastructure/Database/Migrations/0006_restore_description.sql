-- DietTracker schema v6
-- Brings the free-form food description back after it was briefly removed in v5. Existing rows get
-- an empty description; descriptions inside template JSON were scrubbed by v5 and are rewritten the
-- next time a template is saved.

ALTER TABLE MealEntry ADD COLUMN Ingredients TEXT NOT NULL DEFAULT '';