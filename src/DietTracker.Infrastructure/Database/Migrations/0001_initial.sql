-- DietTracker schema v1
-- Conventions:
--   * Identifiers are GUID strings (TEXT) so they stay stable across devices.
--   * Dates are 'yyyy-MM-dd', times are 'HH:mm:ss', instants are ISO-8601 UTC with offset.
--   * Structured collections are JSON TEXT (never BLOBs for images).
--   * Nothing is hard deleted by normal application operations: the five meal categories and the
--     daily meal slots are permanent, deletions travel as tombstones.

CREATE TABLE UserProfile (
    Id                TEXT    NOT NULL PRIMARY KEY,
    ProviderType      INTEGER NOT NULL DEFAULT 0,
    ProviderUserId    TEXT    NOT NULL DEFAULT '',
    DisplayName       TEXT    NOT NULL DEFAULT '',
    Email             TEXT    NOT NULL DEFAULT '',
    Language          TEXT    NOT NULL DEFAULT 'ro-RO',
    TimeZone          TEXT    NOT NULL DEFAULT 'Europe/Bucharest',
    IsActive          INTEGER NOT NULL DEFAULT 0,
    CreatedAt         TEXT    NOT NULL,
    UpdatedAt         TEXT    NOT NULL,
    SyncStatus        INTEGER NOT NULL DEFAULT 0,
    CloudRevision     TEXT    NULL,
    CHECK (ProviderType IN (0, 1, 2))
);

CREATE INDEX IX_UserProfile_Active ON UserProfile (IsActive);

-- A given provider identity maps to exactly one local profile (never keyed on the e-mail address).
CREATE UNIQUE INDEX UX_UserProfile_Identity
    ON UserProfile (ProviderType, ProviderUserId)
    WHERE ProviderUserId <> '';

CREATE TABLE MealCategory (
    Id                TEXT    NOT NULL PRIMARY KEY,
    FixedCategoryKey  INTEGER NOT NULL UNIQUE,
    Name              TEXT    NOT NULL DEFAULT '',
    Color             TEXT    NOT NULL DEFAULT '#FF107C10',
    Icon              TEXT    NOT NULL DEFAULT '',
    DefaultTime       TEXT    NOT NULL,
    DisplayOrder      INTEGER NOT NULL,
    CHECK (FixedCategoryKey BETWEEN 1 AND 5)
);

CREATE TABLE MealEntry (
    Id                TEXT    NOT NULL PRIMARY KEY,
    LocalProfileId    TEXT    NOT NULL REFERENCES UserProfile (Id) ON DELETE CASCADE,
    Date              TEXT    NOT NULL,
    MealCategoryId    TEXT    NOT NULL REFERENCES MealCategory (Id),
    MealCategoryKey   INTEGER NOT NULL,
    Title             TEXT    NOT NULL DEFAULT '',
    Ingredients       TEXT    NOT NULL DEFAULT '',
    Quantities        TEXT    NOT NULL DEFAULT '[]',
    Notes             TEXT    NOT NULL DEFAULT '',
    PlannedTime       TEXT    NULL,
    Status            INTEGER NOT NULL DEFAULT 0,
    CompletedAt       TEXT    NULL,
    CreatedAt         TEXT    NOT NULL,
    UpdatedAt         TEXT    NOT NULL,
    Revision          INTEGER NOT NULL DEFAULT 0,
    SyncStatus        INTEGER NOT NULL DEFAULT 0,
    CloudRevision     TEXT    NULL,
    ContentHash       TEXT    NULL,
    CHECK (Status IN (0, 1, 2)),
    CHECK (MealCategoryKey BETWEEN 1 AND 5),
    CHECK (Status <> 1 OR CompletedAt IS NOT NULL)
);

-- Enforce one meal slot per profile, date and fixed meal category.
CREATE UNIQUE INDEX UX_MealEntry_Slot
    ON MealEntry (LocalProfileId, Date, MealCategoryKey);

CREATE INDEX IX_MealEntry_Profile_Date ON MealEntry (LocalProfileId, Date);
CREATE INDEX IX_MealEntry_SyncStatus ON MealEntry (SyncStatus);
CREATE INDEX IX_MealEntry_Status ON MealEntry (Status);

CREATE TABLE MealPhoto (
    Id                TEXT    NOT NULL PRIMARY KEY,
    MealEntryId       TEXT    NOT NULL REFERENCES MealEntry (Id) ON DELETE CASCADE,
    OriginalFileName  TEXT    NOT NULL DEFAULT '',
    LocalFileId       TEXT    NOT NULL,
    ThumbnailId       TEXT    NOT NULL DEFAULT '',
    CloudObjectId     TEXT    NULL,
    CloudRevision     TEXT    NULL,
    MimeType          TEXT    NOT NULL DEFAULT 'image/jpeg',
    FileSize          INTEGER NOT NULL DEFAULT 0,
    ContentHash       TEXT    NOT NULL DEFAULT '',
    CreatedAt         TEXT    NOT NULL,
    UpdatedAt         TEXT    NOT NULL,
    SyncStatus        INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IX_MealPhoto_Meal ON MealPhoto (MealEntryId);
CREATE INDEX IX_MealPhoto_Hash ON MealPhoto (ContentHash);
CREATE INDEX IX_MealPhoto_SyncStatus ON MealPhoto (SyncStatus);

CREATE TABLE MealTemplate (
    Id                TEXT    NOT NULL PRIMARY KEY,
    LocalProfileId    TEXT    NOT NULL REFERENCES UserProfile (Id) ON DELETE CASCADE,
    Name              TEXT    NOT NULL,
    IsFullDay         INTEGER NOT NULL DEFAULT 0,
    TemplateData      TEXT    NOT NULL DEFAULT '{}',
    RecurrenceRules   TEXT    NULL,
    CreatedAt         TEXT    NOT NULL,
    UpdatedAt         TEXT    NOT NULL,
    SyncStatus        INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IX_MealTemplate_Profile ON MealTemplate (LocalProfileId);

CREATE TABLE ReminderSettings (
    Id                TEXT    NOT NULL PRIMARY KEY,
    LocalProfileId    TEXT    NOT NULL REFERENCES UserProfile (Id) ON DELETE CASCADE,
    MealCategoryId    TEXT    NOT NULL REFERENCES MealCategory (Id),
    MealCategoryKey   INTEGER NOT NULL,
    Enabled           INTEGER NOT NULL DEFAULT 0,
    Time              TEXT    NOT NULL,
    Weekdays          INTEGER NOT NULL DEFAULT 127,
    SnoozeMinutes     INTEGER NULL,
    UpdatedAt         TEXT    NOT NULL,
    SyncStatus        INTEGER NOT NULL DEFAULT 0,
    CHECK (Weekdays BETWEEN 1 AND 127)
);

CREATE UNIQUE INDEX UX_ReminderSettings_Category
    ON ReminderSettings (LocalProfileId, MealCategoryKey);

-- Ledger that guarantees a reminder is never raised twice for the same meal slot and date,
-- even after an application restart or a resume from sleep.
CREATE TABLE ReminderFired (
    Id                TEXT    NOT NULL PRIMARY KEY,
    ReminderId        TEXT    NOT NULL REFERENCES ReminderSettings (Id) ON DELETE CASCADE,
    Date              TEXT    NOT NULL,
    FiredAt           TEXT    NOT NULL,
    MealEntryId       TEXT    NULL
);

CREATE UNIQUE INDEX UX_ReminderFired_Reminder_Date ON ReminderFired (ReminderId, Date);

CREATE TABLE AppPreferences (
    LocalProfileId        TEXT    NOT NULL PRIMARY KEY REFERENCES UserProfile (Id) ON DELETE CASCADE,
    Language              TEXT    NOT NULL DEFAULT '',
    TimeZone              TEXT    NOT NULL DEFAULT '',
    ShowSnackCards        INTEGER NOT NULL DEFAULT 1,
    StartWithWindows      INTEGER NOT NULL DEFAULT 0,
    MinimizeToTray        INTEGER NOT NULL DEFAULT 0,
    RemindersEnabled      INTEGER NOT NULL DEFAULT 1,
    Use12HourClock        INTEGER NOT NULL DEFAULT 0,
    SynchronizationEnabled INTEGER NOT NULL DEFAULT 0,
    PreferredProvider     INTEGER NOT NULL DEFAULT 0,
    CloudUploadConsentGranted INTEGER NOT NULL DEFAULT 0,
    UpdatedAt             TEXT    NOT NULL,
    SyncStatus            INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE CloudAccount (
    Id                    TEXT    NOT NULL PRIMARY KEY,
    ProviderType          INTEGER NOT NULL,
    ProviderUserId        TEXT    NOT NULL DEFAULT '',
    DisplayName           TEXT    NOT NULL DEFAULT '',
    Email                 TEXT    NOT NULL DEFAULT '',
    RootFolderId          TEXT    NULL,
    LastAuthenticatedAt   TEXT    NULL,
    LastSynchronizedAt    TEXT    NULL,
    IsActive              INTEGER NOT NULL DEFAULT 0,
    SynchronizationEnabled INTEGER NOT NULL DEFAULT 1,
    QuotaTotalBytes       INTEGER NULL,
    QuotaRemainingBytes   INTEGER NULL,
    CHECK (ProviderType IN (1, 2))
);

CREATE UNIQUE INDEX UX_CloudAccount_Identity ON CloudAccount (ProviderType, ProviderUserId);

-- Exactly one active cloud account: enforced through a partial unique index.
CREATE UNIQUE INDEX UX_CloudAccount_Active ON CloudAccount (IsActive) WHERE IsActive = 1;

CREATE TABLE SyncOperation (
    Id                TEXT    NOT NULL PRIMARY KEY,
    CloudAccountId    TEXT    NOT NULL REFERENCES CloudAccount (Id) ON DELETE CASCADE,
    EntityType        INTEGER NOT NULL,
    EntityId          TEXT    NOT NULL,
    OperationType     INTEGER NOT NULL,
    Status            INTEGER NOT NULL DEFAULT 0,
    EntityRevision    INTEGER NOT NULL DEFAULT 0,
    RetryCount        INTEGER NOT NULL DEFAULT 0,
    LastError         TEXT    NULL,
    NextAttemptAt     TEXT    NULL,
    CreatedAt         TEXT    NOT NULL,
    CompletedAt       TEXT    NULL
);

CREATE INDEX IX_SyncOperation_Pending ON SyncOperation (CloudAccountId, Status, CreatedAt);
CREATE INDEX IX_SyncOperation_Entity ON SyncOperation (EntityType, EntityId);

CREATE TABLE SyncMetadata (
    CloudAccountId    TEXT    NOT NULL PRIMARY KEY REFERENCES CloudAccount (Id) ON DELETE CASCADE,
    ProviderType      INTEGER NOT NULL,
    LastSyncCursor    TEXT    NULL,
    LastSyncTimestamp TEXT    NULL,
    SchemaVersion     INTEGER NOT NULL DEFAULT 1,
    DeviceId          TEXT    NOT NULL DEFAULT ''
);

CREATE TABLE ConflictRecord (
    Id                TEXT    NOT NULL PRIMARY KEY,
    EntityType        INTEGER NOT NULL,
    EntityId          TEXT    NOT NULL,
    LocalPayload      TEXT    NOT NULL DEFAULT '',
    RemotePayload     TEXT    NOT NULL DEFAULT '',
    LocalModifiedAt   TEXT    NOT NULL,
    RemoteModifiedAt  TEXT    NOT NULL,
    RemoteRevision    TEXT    NULL,
    DeviceId          TEXT    NULL,
    DetectedAt        TEXT    NOT NULL,
    IsResolved        INTEGER NOT NULL DEFAULT 0
);

CREATE INDEX IX_ConflictRecord_Open ON ConflictRecord (IsResolved, DetectedAt);

CREATE TABLE DeletionTombstone (
    Id                TEXT    NOT NULL PRIMARY KEY,
    EntityType        INTEGER NOT NULL,
    EntityId          TEXT    NOT NULL,
    DeletedAt         TEXT    NOT NULL,
    SyncStatus        INTEGER NOT NULL DEFAULT 3
);

CREATE UNIQUE INDEX UX_DeletionTombstone_Entity ON DeletionTombstone (EntityType, EntityId);
