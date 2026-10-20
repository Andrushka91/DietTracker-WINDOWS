using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IConflictRepository"/>. Both versions are kept.</summary>
public sealed class SqliteConflictRepository : IConflictRepository
{
    private const string SelectColumns =
        "Id, EntityType, EntityId, LocalPayload, RemotePayload, LocalModifiedAt, RemoteModifiedAt, RemoteRevision, DeviceId, DetectedAt, IsResolved";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteConflictRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task AddAsync(ConflictRecord conflict, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ConflictRecord (Id, EntityType, EntityId, LocalPayload, RemotePayload, LocalModifiedAt,
                RemoteModifiedAt, RemoteRevision, DeviceId, DetectedAt, IsResolved)
            VALUES ($id, $type, $entity, $local, $remote, $localAt, $remoteAt, $revision, $device, $detectedAt, 0)
            ON CONFLICT (Id) DO UPDATE SET
                LocalPayload = excluded.LocalPayload,
                RemotePayload = excluded.RemotePayload,
                RemoteModifiedAt = excluded.RemoteModifiedAt,
                RemoteRevision = excluded.RemoteRevision,
                DetectedAt = excluded.DetectedAt;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(conflict.Id));
        command.Parameters.AddWithValue("$type", (int)conflict.EntityType);
        command.Parameters.AddWithValue("$entity", SqliteValue.ToDb(conflict.EntityId));
        command.Parameters.AddWithValue("$local", conflict.LocalPayload ?? string.Empty);
        command.Parameters.AddWithValue("$remote", conflict.RemotePayload ?? string.Empty);
        command.Parameters.AddWithValue("$localAt", SqliteValue.ToDb(conflict.LocalModifiedAt));
        command.Parameters.AddWithValue("$remoteAt", SqliteValue.ToDb(conflict.RemoteModifiedAt));
        command.Parameters.AddWithValue("$revision", SqliteValue.NullIfEmpty(conflict.RemoteRevision) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$device", SqliteValue.NullIfEmpty(conflict.DeviceId) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$detectedAt", SqliteValue.ToDb(conflict.DetectedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ConflictRecord>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM ConflictRecord WHERE IsResolved = 0 ORDER BY DetectedAt DESC;";

        var result = new List<ConflictRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapConflictRecord(reader));
        }

        return result;
    }

    public async Task UpdateAsync(ConflictRecord conflict, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(conflict);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "UPDATE ConflictRecord SET IsResolved = $resolved WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(conflict.Id));
        command.Parameters.AddWithValue("$resolved", conflict.IsResolved ? 1 : 0);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<int> CountOpenAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ConflictRecord WHERE IsResolved = 0;";
        return SqliteValue.GetInt(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }
}
