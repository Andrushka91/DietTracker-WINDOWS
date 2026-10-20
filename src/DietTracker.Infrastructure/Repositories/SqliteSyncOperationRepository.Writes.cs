using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>Write operations of <see cref="SqliteSyncOperationRepository"/>.</summary>
public sealed partial class SqliteSyncOperationRepository
{
    /// <summary>
    /// Journals an operation. Pending operations for the same entity are coalesced so that repeated
    /// edits of one meal do not grow the queue: only the newest revision is uploaded.
    /// </summary>
    public async Task EnqueueAsync(SyncOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (Microsoft.Data.Sqlite.SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var remove = connection.CreateCommand())
        {
            remove.Transaction = transaction;
            remove.CommandText =
                """
                DELETE FROM SyncOperation
                WHERE CloudAccountId = $account AND EntityType = $type AND EntityId = $entity
                  AND Status IN (0, 3);
                """;
            remove.Parameters.AddWithValue("$account", SqliteValue.ToDb(operation.CloudAccountId));
            remove.Parameters.AddWithValue("$type", (int)operation.EntityType);
            remove.Parameters.AddWithValue("$entity", SqliteValue.ToDb(operation.EntityId));
            await remove.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO SyncOperation (Id, CloudAccountId, EntityType, EntityId, OperationType, Status, EntityRevision,
                    RetryCount, LastError, NextAttemptAt, CreatedAt, CompletedAt)
                VALUES ($id, $account, $type, $entity, $operation, 0, $revision, 0, NULL, NULL, $createdAt, NULL);
                """;
            insert.Parameters.AddWithValue("$id", SqliteValue.ToDb(operation.Id));
            insert.Parameters.AddWithValue("$account", SqliteValue.ToDb(operation.CloudAccountId));
            insert.Parameters.AddWithValue("$type", (int)operation.EntityType);
            insert.Parameters.AddWithValue("$entity", SqliteValue.ToDb(operation.EntityId));
            insert.Parameters.AddWithValue("$operation", (int)operation.OperationType);
            insert.Parameters.AddWithValue("$revision", operation.EntityRevision);
            insert.Parameters.AddWithValue("$createdAt", SqliteValue.ToDb(operation.CreatedAt));
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(SyncOperation operation, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE SyncOperation SET
                Status = $status,
                RetryCount = $retryCount,
                LastError = $lastError,
                NextAttemptAt = $nextAttemptAt,
                CompletedAt = $completedAt
            WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(operation.Id));
        command.Parameters.AddWithValue("$status", (int)operation.Status);
        command.Parameters.AddWithValue("$retryCount", operation.RetryCount);
        command.Parameters.AddWithValue("$lastError", SqliteValue.NullIfEmpty(operation.LastError) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$nextAttemptAt", operation.NextAttemptAt is { } next ? SqliteValue.ToDb(next) : DBNull.Value);
        command.Parameters.AddWithValue("$completedAt", operation.CompletedAt is { } completed ? SqliteValue.ToDb(completed) : DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
