using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IProfileRepository"/>.</summary>
public sealed class SqliteProfileRepository : IProfileRepository
{
    private const string SelectColumns =
        "Id, ProviderType, ProviderUserId, DisplayName, Email, Language, TimeZone, CreatedAt, UpdatedAt, SyncStatus";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteProfileRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<UserProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM UserProfile WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapUserProfile(reader) : null;
    }

    public async Task<UserProfile?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM UserProfile WHERE IsActive = 1 LIMIT 1;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapUserProfile(reader) : null;
    }

    public async Task<IReadOnlyList<UserProfile>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM UserProfile ORDER BY CreatedAt;";

        var result = new List<UserProfile>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapUserProfile(reader));
        }

        return result;
    }

    public async Task UpsertAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO UserProfile (Id, ProviderType, ProviderUserId, DisplayName, Email, Language, TimeZone, IsActive, CreatedAt, UpdatedAt, SyncStatus, CloudRevision)
            VALUES ($id, $provider, $providerUserId, $displayName, $email, $language, $timeZone, 1, $createdAt, $updatedAt, $syncStatus, NULL)
            ON CONFLICT (Id) DO UPDATE SET
                ProviderType = excluded.ProviderType,
                ProviderUserId = excluded.ProviderUserId,
                DisplayName = excluded.DisplayName,
                Email = excluded.Email,
                Language = excluded.Language,
                TimeZone = excluded.TimeZone,
                UpdatedAt = excluded.UpdatedAt,
                SyncStatus = excluded.SyncStatus;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(profile.Id));
        command.Parameters.AddWithValue("$provider", (int)profile.ProviderType);
        command.Parameters.AddWithValue("$providerUserId", profile.ProviderUserId ?? string.Empty);
        command.Parameters.AddWithValue("$displayName", profile.DisplayName ?? string.Empty);
        command.Parameters.AddWithValue("$email", profile.Email ?? string.Empty);
        command.Parameters.AddWithValue("$language", profile.Language ?? "en-US");
        command.Parameters.AddWithValue("$timeZone", profile.TimeZone ?? "Europe/Bucharest");
        command.Parameters.AddWithValue("$createdAt", SqliteValue.ToDb(profile.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteValue.ToDb(profile.UpdatedAt));
        command.Parameters.AddWithValue("$syncStatus", (int)profile.SyncStatus);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task SetActiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "UPDATE UserProfile SET IsActive = 0 WHERE IsActive = 1;";
            await clear.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var activate = connection.CreateCommand())
        {
            activate.Transaction = transaction;
            activate.CommandText = "UPDATE UserProfile SET IsActive = 1 WHERE Id = $id;";
            activate.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));
            await activate.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
