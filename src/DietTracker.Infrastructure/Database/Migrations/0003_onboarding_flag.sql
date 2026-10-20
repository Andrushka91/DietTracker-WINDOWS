-- DietTracker schema v3
-- Records that the first-launch welcome screen was completed, so that the three account choices
-- (Microsoft, Google, continue without an account) are shown exactly once. Additive only.

ALTER TABLE AppPreferences ADD COLUMN OnboardingCompleted INTEGER NOT NULL DEFAULT 0;
