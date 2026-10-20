using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>Write operations of <see cref="SqlitePhotoRepository"/>.</summary>
public sealed partial class SqlitePhotoRepository
{
    private const string UpsertSql =
        """
        INSERT INTO MealPhoto (
            Id, MealEntryId, OriginalFileName, LocalFileId, ThumbnailId, CloudObjectId, CloudRevision,
            MimeType, FileSize, ContentHash, CreatedAt, UpdatedAt, SyncStatus)
        VALUES (
            $id, $meal, $fileName, $localFile, $thumbnail, $cloudObject, $cloudRevision,
            $mime, $size, $hash, $createdAt, $updatedAt, $syncStatus)
        ON CONFLICT (Id) DO UPDATE SET
            OriginalFileName = excluded.OriginalFileName,
            LocalFileId = excluded.LocalFileId,
            ThumbnailId = excluded.ThumbnailId,
            CloudObjectId = excluded.CloudObjectId,
            CloudRevision = excluded.CloudRevision,
            MimeType = excluded.MimeType,
            FileSize = excluded.FileSize,
            ContentHash = excluded.ContentHash,
            UpdatedAt = excluded.UpdatedAt,
            SyncStatus = excluded.SyncStatus;
        """;

    /// <inheritdoc />
    public async Task AddAsync(MealPhoto photo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = UpsertSql;
        AddParameters(command, photo);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task UpdateAsync(MealPhoto photo, CancellationToken cancellationToken = default) => AddAsync(photo, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MealPhoto WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void AddParameters(SqliteCommand command, MealPhoto photo)
    {
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(photo.Id));
        command.Parameters.AddWithValue("$meal", SqliteValue.ToDb(photo.MealEntryId));
        command.Parameters.AddWithValue("$fileName", photo.OriginalFileName ?? string.Empty);
        command.Parameters.AddWithValue("$localFile", photo.LocalFileId);
        command.Parameters.AddWithValue("$thumbnail", photo.ThumbnailId ?? string.Empty);
        command.Parameters.AddWithValue("$cloudObject", SqliteValue.NullIfEmpty(photo.CloudObjectId) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$cloudRevision", SqliteValue.NullIfEmpty(photo.CloudRevision) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$mime", photo.MimeType ?? "image/jpeg");
        command.Parameters.AddWithValue("$size", photo.FileSize);
        command.Parameters.AddWithValue("$hash", photo.ContentHash ?? string.Empty);
        command.Parameters.AddWithValue("$createdAt", SqliteValue.ToDb(photo.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteValue.ToDb(photo.UpdatedAt));
        command.Parameters.AddWithValue("$syncStatus", (int)photo.SyncStatus);
    }
}
