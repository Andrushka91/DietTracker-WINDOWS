using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>Write operations of <see cref="SqliteMealEntryRepository"/>.</summary>
public sealed partial class SqliteMealEntryRepository
{
    private const string UpsertSql =
        """
        INSERT INTO MealEntry (
            Id, LocalProfileId, Date, MealCategoryId, MealCategoryKey, Title, Ingredients, Quantities, Notes,
            PlannedTime, Status, CompletedAt, CreatedAt, UpdatedAt, Revision, SyncStatus, CloudRevision, ContentHash)
        VALUES (
            $id, $profile, $date, $categoryId, $categoryKey, $title, $ingredients, $quantities, $notes,
            $plannedTime, $status, $completedAt, $createdAt, $updatedAt, $revision, $syncStatus, $cloudRevision, $contentHash)
        ON CONFLICT (LocalProfileId, Date, MealCategoryKey) DO UPDATE SET
            Title = excluded.Title,
            Ingredients = excluded.Ingredients,
            Quantities = excluded.Quantities,
            Notes = excluded.Notes,
            PlannedTime = excluded.PlannedTime,
            Status = excluded.Status,
            CompletedAt = excluded.CompletedAt,
            UpdatedAt = excluded.UpdatedAt,
            Revision = excluded.Revision,
            SyncStatus = excluded.SyncStatus,
            CloudRevision = excluded.CloudRevision,
            ContentHash = excluded.ContentHash;
        """;

    /// <inheritdoc />
    public async Task UpsertAsync(MealEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        AddParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertRangeAsync(IEnumerable<MealEntry> entries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var entry in entries)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = UpsertSql;
            AddParameters(command, entry);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MealEntry WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MealEntry WHERE LocalProfileId = $profile;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));
        return SqliteValue.GetInt(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    private static void AddParameters(SqliteCommand command, MealEntry entry)
    {
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(entry.Id));
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(entry.LocalProfileId));
        command.Parameters.AddWithValue("$date", SqliteValue.ToDb(entry.Date));
        command.Parameters.AddWithValue("$categoryId", SqliteValue.ToDb(entry.MealCategoryId));
        command.Parameters.AddWithValue("$categoryKey", (int)entry.MealCategoryKey);
        command.Parameters.AddWithValue("$title", entry.Title ?? string.Empty);
        command.Parameters.AddWithValue("$ingredients", entry.Ingredients ?? string.Empty);
        command.Parameters.AddWithValue("$quantities", SqliteValue.ToJson(entry.Quantities));
        command.Parameters.AddWithValue("$notes", entry.Notes ?? string.Empty);
        command.Parameters.AddWithValue("$plannedTime", entry.PlannedTime is { } time ? SqliteValue.ToDb(time) : DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)entry.Status);
        command.Parameters.AddWithValue("$completedAt", entry.CompletedAt is { } completed ? SqliteValue.ToDb(completed) : DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", SqliteValue.ToDb(entry.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteValue.ToDb(entry.UpdatedAt));
        command.Parameters.AddWithValue("$revision", entry.Revision);
        command.Parameters.AddWithValue("$syncStatus", (int)entry.SyncStatus);
        command.Parameters.AddWithValue("$cloudRevision", SqliteValue.NullIfEmpty(entry.CloudRevision) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$contentHash", SqliteValue.NullIfEmpty(entry.ContentHash) ?? (object)DBNull.Value);
    }
}
