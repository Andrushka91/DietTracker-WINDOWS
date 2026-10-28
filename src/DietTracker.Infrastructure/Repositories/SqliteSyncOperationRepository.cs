using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of the durable synchronization journal (outbox).</summary>
public sealed partial class SqliteSyncOperationRepository : ISyncOperationRepository
{
    private const string SelectColumns =
        "Id, CloudAccountId, EntityType, EntityId, OperationType, Status, EntityRevision, RetryCount, LastError, " +
        "NextAttemptAt, CreatedAt, CompletedAt";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteSyncOperationRepository(ISqliteConnectionFactory factory) => _factory = factory;

    /// <summary>Returns the operations that are due now, honouring the exponential backoff timestamp.</summary>
    public async Task<IReadOnlyList<SyncOperation>> GetPendingAsync(Guid cloudAccountId, int maxCount, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT {SelectColumns} FROM SyncOperation
             WHERE CloudAccountId = $account
               AND Status IN (0, 3)
               AND (NextAttemptAt IS NULL OR NextAttemptAt <= $now)
             ORDER BY CreatedAt
             LIMIT $limit;
             """;
        command.Parameters.AddWithValue("$account", SqliteValue.ToDb(cloudAccountId));
        command.Parameters.AddWithValue("$now", SqliteValue.ToDb(DateTimeOffset.UtcNow));
        command.Parameters.AddWithValue("$limit", maxCount);

        var result = new List<SyncOperation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapSyncOperation(reader));
        }

        return result;
    }

    public async Task<int> CountPendingAsync(Guid cloudAccountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SyncOperation WHERE CloudAccountId = $account AND Status IN (0, 1, 3);";
        command.Parameters.AddWithValue("$account", SqliteValue.ToDb(cloudAccountId));
        return SqliteValue.GetInt(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    public async Task<bool> HasOutstandingAsync(Guid cloudAccountId, EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM SyncOperation
            WHERE CloudAccountId = $account AND EntityType = $type AND EntityId = $entity
              AND Status IN (0, 1, 3);
            """;
        command.Parameters.AddWithValue("$account", SqliteValue.ToDb(cloudAccountId));
        command.Parameters.AddWithValue("$type", (int)entityType);
        command.Parameters.AddWithValue("$entity", SqliteValue.ToDb(entityId));

        return SqliteValue.GetInt(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    public async Task<int> CompactAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SyncOperation WHERE Status = $completed AND CompletedAt < $olderThan;";
        command.Parameters.AddWithValue("$completed", (int)SyncOperationStatus.Completed);
        command.Parameters.AddWithValue("$olderThan", SqliteValue.ToDb(olderThan));
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearForAccountAsync(Guid cloudAccountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SyncOperation WHERE CloudAccountId = $account;";
        command.Parameters.AddWithValue("$account", SqliteValue.ToDb(cloudAccountId));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
