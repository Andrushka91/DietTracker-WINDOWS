-- DietTracker schema v12
-- Removes the interface language completely: Diet Tracker ships in English only (the application
-- forces en-US at start-up, the package declares exactly that language and the translated resource
-- files are gone), so neither stored column has a reader any more. The values were never
-- synchronized (the preferences sync deliberately skips the language), so dropping them loses
-- nothing in the cloud either. The first migration is left untouched (it must stay applicable to
-- brand new databases, which then run this one right afterwards).

ALTER TABLE AppPreferences DROP COLUMN Language;

ALTER TABLE UserProfile DROP COLUMN Language;