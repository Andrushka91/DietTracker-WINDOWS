using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="ICloudAccountRepository"/>.</summary>
public sealed partial class SqliteCloudAccountRepository : ICloudAccountRepository
{
    private const string SelectColumns =
        "Id, ProviderType, ProviderUserId, DisplayName, Email, RootFolderId, LastAuthenticatedAt, LastSynchronizedAt, " +
        "IsActive, SynchronizationEnabled, QuotaTotalBytes, QuotaRemainingBytes";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteCloudAccountRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<CloudAccount>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM CloudAccount ORDER BY ProviderType, Email;";

        var result = new List<CloudAccount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapCloudAccount(reader));
        }

        return result;
    }

    public async Task<CloudAccount?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM CloudAccount WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapCloudAccount(reader) : null;
    }

    public async Task<CloudAccount?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM CloudAccount WHERE IsActive = 1 LIMIT 1;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapCloudAccount(reader) : null;
    }

    /// <summary>Looks an account up by provider identity. Deliberately never keyed on the e-mail address.</summary>
    public async Task<CloudAccount?> FindAsync(ProviderType providerType, string providerUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM CloudAccount WHERE ProviderType = $provider AND ProviderUserId = $userId;";
        command.Parameters.AddWithValue("$provider", (int)providerType);
        command.Parameters.AddWithValue("$userId", providerUserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapCloudAccount(reader) : null;
    }
}
