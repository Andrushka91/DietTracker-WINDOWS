using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>Write operations of <see cref="SqliteCloudAccountRepository"/>.</summary>
public sealed partial class SqliteCloudAccountRepository
{
    /// <summary>
    /// Inserts or updates the account and makes it the only active one. The partial unique index on
    /// <c>IsActive</c> guarantees two accounts can never be active simultaneously.
    /// </summary>
    public async Task UpsertActiveAsync(CloudAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "UPDATE CloudAccount SET IsActive = 0 WHERE IsActive = 1;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText =
                """
                INSERT INTO CloudAccount (Id, ProviderType, ProviderUserId, DisplayName, Email, RootFolderId,
                    LastAuthenticatedAt, LastSynchronizedAt, IsActive, SynchronizationEnabled, QuotaTotalBytes, QuotaRemainingBytes)
                VALUES ($id, $provider, $userId, $displayName, $email, $rootFolder, $authenticatedAt, $synchronizedAt,
                    1, $syncEnabled, $quotaTotal, $quotaRemaining)
                ON CONFLICT (ProviderType, ProviderUserId) DO UPDATE SET
                    DisplayName = excluded.DisplayName,
                    Email = excluded.Email,
                    RootFolderId = COALESCE(excluded.RootFolderId, CloudAccount.RootFolderId),
                    LastAuthenticatedAt = excluded.LastAuthenticatedAt,
                    LastSynchronizedAt = COALESCE(excluded.LastSynchronizedAt, CloudAccount.LastSynchronizedAt),
                    IsActive = 1,
                    SynchronizationEnabled = excluded.SynchronizationEnabled;
                """;
            AddAccountParameters(upsert, account);
            await upsert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CloudAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE CloudAccount SET
                DisplayName = $displayName,
                Email = $email,
                RootFolderId = $rootFolder,
                LastAuthenticatedAt = $authenticatedAt,
                LastSynchronizedAt = $synchronizedAt,
                SynchronizationEnabled = $syncEnabled,
                QuotaTotalBytes = $quotaTotal,
                QuotaRemainingBytes = $quotaRemaining
            WHERE Id = $id;
            """;
        AddAccountParameters(command, account);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ClearActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE CloudAccount SET IsActive = 0 WHERE IsActive = 1;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM CloudAccount WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddAccountParameters(SqliteCommand command, CloudAccount account)
    {
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(account.Id));
        command.Parameters.AddWithValue("$provider", (int)account.ProviderType);
        command.Parameters.AddWithValue("$userId", account.ProviderUserId ?? string.Empty);
        command.Parameters.AddWithValue("$displayName", account.DisplayName ?? string.Empty);
        command.Parameters.AddWithValue("$email", account.Email ?? string.Empty);
        command.Parameters.AddWithValue("$rootFolder", SqliteValue.NullIfEmpty(account.RootFolderId) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$authenticatedAt", account.LastAuthenticatedAt is { } auth ? SqliteValue.ToDb(auth) : DBNull.Value);
        command.Parameters.AddWithValue("$synchronizedAt", account.LastSynchronizedAt is { } sync ? SqliteValue.ToDb(sync) : DBNull.Value);
        command.Parameters.AddWithValue("$syncEnabled", account.SynchronizationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$quotaTotal", account.QuotaTotalBytes is { } total ? total : (object)DBNull.Value);
        command.Parameters.AddWithValue("$quotaRemaining", account.QuotaRemainingBytes is { } remaining ? remaining : (object)DBNull.Value);
    }
}
