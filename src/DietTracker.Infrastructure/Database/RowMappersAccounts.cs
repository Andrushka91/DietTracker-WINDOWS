using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Database;

/// <summary>Mappers for preferences, cloud accounts and journal rows.</summary>
public static partial class RowMappers
{
    /// <summary>Maps an <c>AppPreferences</c> row.</summary>
    public static AppPreferences MapAppPreferences(SqliteDataReader reader) => new()
    {
        LocalProfileId = SqliteValue.GetGuid(reader.GetValue(0)),
        Language = SqliteValue.GetString(reader.GetValue(1)),
        TimeZone = SqliteValue.GetString(reader.GetValue(2)),
        ShowSnackCards = SqliteValue.GetBool(reader.GetValue(3)),
        StartWithWindows = SqliteValue.GetBool(reader.GetValue(4)),
        MinimizeToTray = SqliteValue.GetBool(reader.GetValue(5)),
        RemindersEnabled = SqliteValue.GetBool(reader.GetValue(6)),
        Use12HourClock = SqliteValue.GetBool(reader.GetValue(7)),
        SynchronizationEnabled = SqliteValue.GetBool(reader.GetValue(8)),
        PreferredProvider = SqliteValue.GetEnum<ProviderType>(reader.GetValue(9)),
        CloudUploadConsentGranted = SqliteValue.GetBool(reader.GetValue(10)),
        UpdatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(11)),
        SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(12)),
        OnboardingCompleted = reader.FieldCount > 13 && SqliteValue.GetBool(reader.GetValue(13)),
        SyncFrequency = reader.FieldCount > 14
            ? SqliteValue.GetEnum<SyncFrequency>(reader.GetValue(14))
            : SyncFrequency.Weekly,
    };

    /// <summary>Maps a <c>CloudAccount</c> row.</summary>
    public static CloudAccount MapCloudAccount(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        ProviderType = SqliteValue.GetEnum<ProviderType>(reader.GetValue(1)),
        ProviderUserId = SqliteValue.GetString(reader.GetValue(2)),
        DisplayName = SqliteValue.GetString(reader.GetValue(3)),
        Email = SqliteValue.GetString(reader.GetValue(4)),
        RootFolderId = SqliteValue.GetStringOrNull(reader.GetValue(5)),
        LastAuthenticatedAt = SqliteValue.GetDateTimeOffsetOrNull(reader.GetValue(6)),
        LastSynchronizedAt = SqliteValue.GetDateTimeOffsetOrNull(reader.GetValue(7)),
        IsActive = SqliteValue.GetBool(reader.GetValue(8)),
        SynchronizationEnabled = SqliteValue.GetBool(reader.GetValue(9)),
        QuotaTotalBytes = SqliteValue.GetLongOrNull(reader.GetValue(10)),
        QuotaRemainingBytes = SqliteValue.GetLongOrNull(reader.GetValue(11)),
    };

    /// <summary>Maps a <c>SyncOperation</c> row.</summary>
    public static SyncOperation MapSyncOperation(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        CloudAccountId = SqliteValue.GetGuid(reader.GetValue(1)),
        EntityType = SqliteValue.GetEnum<EntityType>(reader.GetValue(2)),
        EntityId = SqliteValue.GetGuid(reader.GetValue(3)),
        OperationType = SqliteValue.GetEnum<SyncOperationType>(reader.GetValue(4)),
        Status = SqliteValue.GetEnum<SyncOperationStatus>(reader.GetValue(5)),
        EntityRevision = SqliteValue.GetLong(reader.GetValue(6)),
        RetryCount = SqliteValue.GetInt(reader.GetValue(7)),
        LastError = SqliteValue.GetStringOrNull(reader.GetValue(8)),
        NextAttemptAt = SqliteValue.GetDateTimeOffsetOrNull(reader.GetValue(9)),
        CreatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(10)),
        CompletedAt = SqliteValue.GetDateTimeOffsetOrNull(reader.GetValue(11)),
    };

    /// <summary>Maps a <c>SyncMetadata</c> row.</summary>
    public static SyncMetadata MapSyncMetadata(SqliteDataReader reader) => new()
    {
        CloudAccountId = SqliteValue.GetGuid(reader.GetValue(0)),
        ProviderType = SqliteValue.GetEnum<ProviderType>(reader.GetValue(1)),
        LastSyncCursor = SqliteValue.GetStringOrNull(reader.GetValue(2)),
        LastSyncTimestamp = SqliteValue.GetDateTimeOffsetOrNull(reader.GetValue(3)),
        SchemaVersion = SqliteValue.GetInt(reader.GetValue(4)),
        DeviceId = SqliteValue.GetString(reader.GetValue(5)),
        ManifestRevision = reader.FieldCount > 6 ? SqliteValue.GetStringOrNull(reader.GetValue(6)) : null,
    };

    /// <summary>Maps a <c>ConflictRecord</c> row.</summary>
    public static ConflictRecord MapConflictRecord(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        EntityType = SqliteValue.GetEnum<EntityType>(reader.GetValue(1)),
        EntityId = SqliteValue.GetGuid(reader.GetValue(2)),
        LocalPayload = SqliteValue.GetString(reader.GetValue(3)),
        RemotePayload = SqliteValue.GetString(reader.GetValue(4)),
        LocalModifiedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(5)),
        RemoteModifiedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(6)),
        RemoteRevision = SqliteValue.GetStringOrNull(reader.GetValue(7)),
        DeviceId = SqliteValue.GetStringOrNull(reader.GetValue(8)),
        DetectedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(9)),
        IsResolved = SqliteValue.GetBool(reader.GetValue(10)),
    };

    /// <summary>Maps a <c>DeletionTombstone</c> row.</summary>
    public static DeletionTombstone MapTombstone(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        EntityType = SqliteValue.GetEnum<EntityType>(reader.GetValue(1)),
        EntityId = SqliteValue.GetGuid(reader.GetValue(2)),
        DeletedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(3)),
        SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(4)),
    };
}
