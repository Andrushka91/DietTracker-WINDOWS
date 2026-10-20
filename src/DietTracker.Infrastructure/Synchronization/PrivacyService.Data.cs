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
