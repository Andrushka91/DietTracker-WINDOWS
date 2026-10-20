using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IReminderRepository"/> plus the fired-reminder ledger.</summary>
public sealed partial class SqliteReminderRepository : IReminderRepository
{
    private const string SelectColumns =
        "Id, LocalProfileId, MealCategoryId, MealCategoryKey, Enabled, Time, Weekdays, SnoozeMinutes, UpdatedAt, SyncStatus";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteReminderRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<ReminderSettings>> GetAllAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ReminderSettings WHERE LocalProfileId = $profile ORDER BY MealCategoryKey;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));

        var result = new List<ReminderSettings>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public async Task<ReminderSettings?> GetForCategoryAsync(Guid profileId, MealCategoryKey key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ReminderSettings WHERE LocalProfileId = $profile AND MealCategoryKey = $key;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));
        command.Parameters.AddWithValue("$key", (int)key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    private static ReminderSettings Map(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        LocalProfileId = SqliteValue.GetGuid(reader.GetValue(1)),
        MealCategoryId = SqliteValue.GetGuid(reader.GetValue(2)),
        MealCategoryKey = SqliteValue.GetEnum<MealCategoryKey>(reader.GetValue(3)),
        Enabled = SqliteValue.GetBool(reader.GetValue(4)),
        Time = SqliteValue.GetTimeOnly(reader.GetValue(5)),
        Weekdays = SqliteValue.GetInt(reader.GetValue(6)),
        SnoozeMinutes = reader.IsDBNull(7) ? null : SqliteValue.GetInt(reader.GetValue(7)),
        UpdatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(8)),
        SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(9)),
    };
}
