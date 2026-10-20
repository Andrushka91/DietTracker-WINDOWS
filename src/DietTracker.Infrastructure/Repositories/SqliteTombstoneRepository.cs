using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="ITombstoneRepository"/> (deletion markers).</summary>
public sealed class SqliteTombstoneRepository : ITombstoneRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteTombstoneRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(DeletionTombstone tombstone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tombstone);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO DeletionTombstone (Id, EntityType, EntityId, DeletedAt, SyncStatus)
            VALUES ($id, $type, $entity, $deletedAt, 3)
            ON CONFLICT (EntityType, EntityId) DO UPDATE SET DeletedAt = excluded.DeletedAt, SyncStatus = 3;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(tombstone.Id));
        command.Parameters.AddWithValue("$type", (int)tombstone.EntityType);
        command.Parameters.AddWithValue("$entity", SqliteValue.ToDb(tombstone.EntityId));
        command.Parameters.AddWithValue("$deletedAt", SqliteValue.ToDb(tombstone.DeletedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DeletionTombstone>> GetPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT Id, EntityType, EntityId, DeletedAt, SyncStatus FROM DeletionTombstone WHERE SyncStatus = 3 ORDER BY DeletedAt;";

        var result = new List<DeletionTombstone>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapTombstone(reader));
        }

        return result;
    }

    public async Task MarkSyncedAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE DeletionTombstone SET SyncStatus = 1 WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
