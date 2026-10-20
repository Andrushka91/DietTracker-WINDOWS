using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IMealEntryRepository"/>. Every statement is parameterized and
/// range queries are bounded so a long history is never loaded into memory at once.
/// </summary>
public sealed partial class SqliteMealEntryRepository : IMealEntryRepository
{
    internal const string SelectColumns =
        "Id, LocalProfileId, Date, MealCategoryId, MealCategoryKey, Title, Ingredients, Quantities, Notes, " +
        "PlannedTime, Status, CompletedAt, CreatedAt, UpdatedAt, Revision, SyncStatus, CloudRevision, ContentHash";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteMealEntryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<MealEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealEntry WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapMealEntry(reader) : null;
    }

    /// <inheritdoc />
    public async Task<MealEntry?> GetBySlotAsync(Guid profileId, DateOnly date, MealCategoryKey key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM MealEntry WHERE LocalProfileId = $profile AND Date = $date AND MealCategoryKey = $key;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));
        command.Parameters.AddWithValue("$date", SqliteValue.ToDb(date));
        command.Parameters.AddWithValue("$key", (int)key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapMealEntry(reader) : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealEntry>> GetForDateAsync(Guid profileId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM MealEntry WHERE LocalProfileId = $profile AND Date = $date ORDER BY MealCategoryKey;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));
        command.Parameters.AddWithValue("$date", SqliteValue.ToDb(date));

        var result = new List<MealEntry>(5);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapMealEntry(reader));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<MealEntry>>> GetForRangeAsync(
        Guid profileId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT {SelectColumns} FROM MealEntry WHERE LocalProfileId = $profile AND Date >= $from AND Date <= $to ORDER BY Date, MealCategoryKey;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));
        command.Parameters.AddWithValue("$from", SqliteValue.ToDb(from));
        command.Parameters.AddWithValue("$to", SqliteValue.ToDb(to));

        var result = new Dictionary<DateOnly, IReadOnlyList<MealEntry>>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var entry = RowMappers.MapMealEntry(reader);
            if (!result.TryGetValue(entry.Date, out var list))
            {
                list = new List<MealEntry>();
                result[entry.Date] = list;
            }

            ((List<MealEntry>)list).Add(entry);
        }

        return result;
    }
}
