using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="ISyncMetadataRepository"/>.</summary>
public sealed class SqliteSyncMetadataRepository : ISyncMetadataRepository
{
    private readonly ISqliteConnectionFactory _factory;

    public SqliteSyncMetadataRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<SyncMetadata?> GetAsync(Guid cloudAccountId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT CloudAccountId, ProviderType, LastSyncCursor, LastSyncTimestamp, SchemaVersion, DeviceId, ManifestRevision " +
            "FROM SyncMetadata WHERE CloudAccountId = $account;";
        command.Parameters.AddWithValue("$account", SqliteValue.ToDb(cloudAccountId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapSyncMetadata(reader) : null;
    }

    public async Task UpsertAsync(SyncMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SyncMetadata (CloudAccountId, ProviderType, LastSyncCursor, LastSyncTimestamp, SchemaVersion, DeviceId, ManifestRevision)
            VALUES ($account, $provider, $cursor, $timestamp, $schema, $device, $manifest)
            ON CONFLICT (CloudAccountId) DO UPDATE SET
                ProviderType = excluded.ProviderType,
                LastSyncCursor = excluded.LastSyncCursor,
                LastSyncTimestamp = excluded.LastSyncTimestamp,
                SchemaVersion = excluded.SchemaVersion,
                DeviceId = excluded.DeviceId,
                ManifestRevision = excluded.ManifestRevision;
            """;
        command.Parameters.AddWithValue("$account", SqliteValue.ToDb(metadata.CloudAccountId));
        command.Parameters.AddWithValue("$provider", (int)metadata.ProviderType);
        command.Parameters.AddWithValue("$cursor", SqliteValue.NullIfEmpty(metadata.LastSyncCursor) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$timestamp", metadata.LastSyncTimestamp is { } ts ? SqliteValue.ToDb(ts) : DBNull.Value);
        command.Parameters.AddWithValue("$schema", metadata.SchemaVersion);
        command.Parameters.AddWithValue("$device", metadata.DeviceId ?? string.Empty);
        command.Parameters.AddWithValue("$manifest", SqliteValue.NullIfEmpty(metadata.ManifestRevision) ?? (object)DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<DateTimeOffset?> GetLastSyncTimestampAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(LastSyncTimestamp) FROM SyncMetadata;";
        return SqliteValue.GetDateTimeOffsetOrNull(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }
}
