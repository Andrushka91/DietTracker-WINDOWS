using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Destructive data operations of <see cref="PrivacyService"/>.</summary>
public sealed partial class PrivacyService
{
    /// <inheritdoc />
    public async Task DeleteLocalDataAsync(bool includePhotographs, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var sql in new[]
                 {
                     "DELETE FROM MealEntry WHERE LocalProfileId = $profile;",
                     "DELETE FROM MealTemplate WHERE LocalProfileId = $profile;",
                     "DELETE FROM ReminderSettings WHERE LocalProfileId = $profile;",
                     "DELETE FROM SyncOperation;",
                     "DELETE FROM SyncMetadata;",
                     "DELETE FROM ConflictRecord;",
                     "DELETE FROM DeletionTombstone;",
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profile.Id));
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        if (includePhotographs)
        {
            await DeletePhotoFilesAsync(cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Local data was deleted (photographs: {IncludePhotos}). Cloud data is untouched.", includePhotographs);
    }

    /// <inheritdoc />
    public async Task<int> PurgeLocalCacheAsync(bool includePhotographs, CancellationToken cancellationToken = default)
    {
        // The active profile survives (it owns the preferences and carries the identity of the account
        // that is connected right now); every record is removed regardless of the profile that owns it,
        // so a cache written by an earlier account can never be shown to the next one.
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        await using var connection = await _connectionFactory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        // Child rows first: the schema cascades, but the order keeps the purge valid even if foreign key
        // enforcement is ever switched off for a connection.
        foreach (var sql in new[]
                 {
                     "DELETE FROM ReminderFired;",
                     "DELETE FROM MealPhoto;",
                     "DELETE FROM MealEntry;",
                     "DELETE FROM MealTemplate;",
                     "DELETE FROM ReminderSettings;",
                     "DELETE FROM SyncOperation;",
                     "DELETE FROM SyncMetadata;",
                     "DELETE FROM ConflictRecord;",
                     "DELETE FROM DeletionTombstone;",
                     "DELETE FROM AppPreferences WHERE LocalProfileId <> $profile;",
                     "DELETE FROM UserProfile WHERE Id <> $profile;",
                 })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            if (sql.Contains("$profile", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profile.Id));
            }

            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

        var files = includePhotographs ? await DeletePhotoFilesAsync(cancellationToken).ConfigureAwait(false) : 0;

        _logger.LogInformation(
            "The local cache was emptied ({Files} photograph file(s) removed); photographs: {IncludePhotos}. Cloud data is untouched.",
            files, includePhotographs);

        return files;
    }

    /// <inheritdoc />
    public async Task<int> DeleteCloudDataAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("No cloud account is connected.");

        var provider = _providers.Create(account.ProviderType);
        await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var objects = await provider.ListAsync(string.Empty, cancellationToken).ConfigureAwait(false);
        var deleted = 0;

        foreach (var item in objects)
        {
            await provider.DeleteAsync(item.ObjectId, cancellationToken).ConfigureAwait(false);
            deleted++;
        }

        _logger.LogInformation("{Count} object(s) were deleted from the connected cloud account.", deleted);
        return deleted;
    }

    /// <inheritdoc />
    public Task<int> DeleteAllPhotographsAsync(CancellationToken cancellationToken = default)
        => DeletePhotoFilesAsync(cancellationToken);

    private async Task<int> DeletePhotoFilesAsync(CancellationToken cancellationToken)
    {
        var count = 0;

        foreach (var directory in new[] { _paths.PhotoDirectory, _paths.ThumbnailDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.Delete(file);
                    count++;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Photograph file '{Path}' could not be deleted.", file);
                }
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return count;
    }
}
