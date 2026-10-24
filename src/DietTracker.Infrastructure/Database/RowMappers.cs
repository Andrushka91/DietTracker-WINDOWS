using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Database;

/// <summary>
/// Maps SQLite rows to domain models. Centralized so the column order used by the repositories and
/// the mapping stay in sync in exactly one place.
/// </summary>
public static partial class RowMappers
{
    /// <summary>Maps a <c>MealEntry</c> row (order matches <c>SqliteMealEntryRepository.SelectColumns</c>).</summary>
    public static MealEntry MapMealEntry(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        LocalProfileId = SqliteValue.GetGuid(reader.GetValue(1)),
        Date = SqliteValue.GetDateOnly(reader.GetValue(2)),
        MealCategoryId = SqliteValue.GetGuid(reader.GetValue(3)),
        MealCategoryKey = SqliteValue.GetEnum<MealCategoryKey>(reader.GetValue(4)),
        Title = SqliteValue.GetString(reader.GetValue(5)),
        Ingredients = SqliteValue.GetString(reader.GetValue(6)),
        PlannedTime = SqliteValue.GetTimeOnlyOrNull(reader.GetValue(7)),
        Status = NormalizeMealStatus(SqliteValue.GetEnum<MealStatus>(reader.GetValue(8))),
        CompletedAt = SqliteValue.GetDateTimeOffsetOrNull(reader.GetValue(9)),
        CreatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(10)),
        UpdatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(11)),
        Revision = SqliteValue.GetLong(reader.GetValue(12)),
        SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(13)),
        CloudRevision = SqliteValue.GetStringOrNull(reader.GetValue(14)),
        ContentHash = SqliteValue.GetStringOrNull(reader.GetValue(15)),
    };

    /// <summary>
    /// A meal has exactly two states (PENDING and DONE). A value written by an older build - the
    /// removed MISSED state - is mapped back to PENDING, so rows restored from an old backup or
    /// pulled from the cloud can never reintroduce a third state.
    /// </summary>
    private static MealStatus NormalizeMealStatus(MealStatus status)
        => Enum.IsDefined(status) ? status : MealStatus.Pending;

    /// <summary>Maps a <c>MealPhoto</c> row.</summary>
    public static MealPhoto MapMealPhoto(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        MealEntryId = SqliteValue.GetGuid(reader.GetValue(1)),
        OriginalFileName = SqliteValue.GetString(reader.GetValue(2)),
        LocalFileId = SqliteValue.GetString(reader.GetValue(3)),
        ThumbnailId = SqliteValue.GetString(reader.GetValue(4)),
        CloudObjectId = SqliteValue.GetStringOrNull(reader.GetValue(5)),
        CloudRevision = SqliteValue.GetStringOrNull(reader.GetValue(6)),
        MimeType = SqliteValue.GetString(reader.GetValue(7)),
        FileSize = SqliteValue.GetLong(reader.GetValue(8)),
        ContentHash = SqliteValue.GetString(reader.GetValue(9)),
        CreatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(10)),
        UpdatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(11)),
        SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(12)),
    };

    /// <summary>Maps a <c>UserProfile</c> row.</summary>
    public static UserProfile MapUserProfile(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        ProviderType = SqliteValue.GetEnum<ProviderType>(reader.GetValue(1)),
        ProviderUserId = SqliteValue.GetString(reader.GetValue(2)),
        DisplayName = SqliteValue.GetString(reader.GetValue(3)),
        Email = SqliteValue.GetString(reader.GetValue(4)),
        Language = SqliteValue.GetString(reader.GetValue(5)),
        TimeZone = SqliteValue.GetString(reader.GetValue(6)),
        CreatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(7)),
        UpdatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(8)),
        SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(9)),
    };
}
