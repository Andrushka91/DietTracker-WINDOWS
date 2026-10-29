using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IPhotoRepository"/> (metadata only, never image bytes).</summary>
public sealed partial class SqlitePhotoRepository : IPhotoRepository
{
    private const string SelectColumns =
        "Id, MealEntryId, OriginalFileName, LocalFileId, ThumbnailId, CloudObjectId, CloudRevision, " +
        "MimeType, FileSize, ContentHash, CreatedAt, UpdatedAt, SyncStatus";

    private readonly ISqliteConnectionFactory _factory;

    public SqlitePhotoRepository(ISqliteConnectionFactory factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealPhoto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealPhoto ORDER BY CreatedAt;";

        var result = new List<MealPhoto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapMealPhoto(reader));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealPhoto>> GetForMealAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealPhoto WHERE MealEntryId = $meal ORDER BY CreatedAt;";
        command.Parameters.AddWithValue("$meal", SqliteValue.ToDb(mealEntryId));

        var result = new List<MealPhoto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapMealPhoto(reader));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealPhoto>> GetForDateRangeAsync(
        Guid profileId,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT p.Id, p.MealEntryId, p.OriginalFileName, p.LocalFileId, p.ThumbnailId, " +
            "p.CloudObjectId, p.CloudRevision, p.MimeType, p.FileSize, p.ContentHash, p.CreatedAt, " +
            "p.UpdatedAt, p.SyncStatus FROM MealPhoto p " +
            "INNER JOIN MealEntry e ON e.Id = p.MealEntryId " +
            "WHERE e.LocalProfileId = $profile AND e.Date >= $from AND e.Date <= $to " +
            "ORDER BY e.Date, e.MealCategoryKey, p.CreatedAt;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));
        command.Parameters.AddWithValue("$from", SqliteValue.ToDb(from));
        command.Parameters.AddWithValue("$to", SqliteValue.ToDb(to));

        var result = new List<MealPhoto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(RowMappers.MapMealPhoto(reader));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<MealPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealPhoto WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapMealPhoto(reader) : null;
    }

    /// <inheritdoc />
    public async Task<int> CountForMealAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM MealPhoto WHERE MealEntryId = $meal;";
        command.Parameters.AddWithValue("$meal", SqliteValue.ToDb(mealEntryId));
        return SqliteValue.GetInt(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<MealPhoto?> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealPhoto WHERE ContentHash = $hash LIMIT 1;";
        command.Parameters.AddWithValue("$hash", contentHash);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapMealPhoto(reader) : null;
    }
}
