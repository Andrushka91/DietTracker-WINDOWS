using System.Text;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Pull side of a synchronization cycle: apply provider changes to the local database.</summary>
public sealed partial class SyncEngine
{
    private async Task<PullOutcome> PullChangesAsync(
        ICloudStorageProvider provider,
        CloudAccount account,
        SyncMetadata metadata,
        CancellationToken cancellationToken)
    {
        var cursor = new SyncCursor
        {
            ProviderType = account.ProviderType,
            ProviderUserId = account.ProviderUserId,
            Value = metadata.LastSyncCursor,
        };

        var changeSet = await provider.GetChangesAsync(cursor, cancellationToken).ConfigureAwait(false);

        var downloaded = 0;
        var deleted = 0;
        var skipped = 0;

        foreach (var item in changeSet.Items)
        {
            if (!item.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var json = item.Content is { Length: > 0 }
                ? Encoding.UTF8.GetString(item.Content)
                : await DownloadTextAsync(provider, item.ObjectId, cancellationToken).ConfigureAwait(false);

            if (json is null)
            {
                skipped++;
                continue;
            }

            var header = _serializer.ReadHeader(json);
            if (header is null)
            {
                skipped++;
                continue;
            }

            var info = header.Value;

            // Echo of our own upload, or a payload written by a newer application version.
            if (string.Equals(info.DeviceId, _environment.DeviceId, StringComparison.Ordinal)
                || info.SchemaVersion > CloudSchema.CurrentVersion)
            {
                skipped++;
                continue;
            }

            if (info.IsDeleted)
            {
                if (await ApplyRemoteDeletionAsync(info.EntityType, info.EntityId, cancellationToken).ConfigureAwait(false))
                {
                    deleted++;
                }

                continue;
            }

            if (await ApplyRemoteEntityAsync(provider, item, json, info, account.Id, cancellationToken).ConfigureAwait(false))
            {
                downloaded++;
            }
            else
            {
                skipped++;
            }
        }

        return new PullOutcome(downloaded, deleted, skipped, changeSet.NextCursor?.Value);
    }

    private async Task<string?> DownloadTextAsync(ICloudStorageProvider provider, string objectId, CancellationToken cancellationToken)
    {
        await using var stream = await provider.OpenReadAsync(objectId, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies a deletion marker that arrived from another device.</summary>
    private async Task<bool> ApplyRemoteDeletionAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        switch (entityType)
        {
            case EntityType.MealPhoto:
                var photo = await _photos.GetAsync(entityId, cancellationToken).ConfigureAwait(false);
                if (photo is null)
                {
                    return false;
                }

                await _photos.DeleteAsync(entityId, cancellationToken).ConfigureAwait(false);
                await _photoStore.DeleteFilesAsync(photo, cancellationToken).ConfigureAwait(false);
                return true;

            case EntityType.MealTemplate:
                await _templates.DeleteAsync(entityId, cancellationToken).ConfigureAwait(false);
                return true;

            case EntityType.MealEntry:
                // Meal slots are permanent: a deletion coming from another device clears the content
                // of the slot instead of removing the row, so the five cards always stay visible.
                var entry = await _entries.GetAsync(entityId, cancellationToken).ConfigureAwait(false);
                if (entry is null)
                {
                    return false;
                }

                entry.Title = string.Empty;
                entry.Ingredients = string.Empty;
                entry.PlannedTime = null;
                entry.Status = MealStatus.Pending;
                entry.CompletedAt = null;
                entry.SyncStatus = SyncStatus.Synced;
                await _entries.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
                return true;

            default:
                return false;
        }
    }

    /// <summary>Outcome counters of the pull phase.</summary>
    private sealed record PullOutcome(int Downloaded, int Deleted, int Skipped, string? Cursor);
}
