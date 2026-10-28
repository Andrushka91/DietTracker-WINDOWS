using System.Text;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Infrastructure.Cloud;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Pull side of a synchronization cycle: apply provider changes to the local database.</summary>
public sealed partial class SyncEngine
{
    /// <param name="ignoreDeviceEcho">
    /// When <see langword="true"/> a document uploaded by this very installation is applied like any
    /// other one. The regular cycle skips those documents because the local record already holds the
    /// change, but a cache that was emptied on purpose (an account change) would otherwise stay empty.
    /// </param>
    private async Task<PullOutcome> PullChangesAsync(
        ICloudStorageProvider provider,
        CloudAccount account,
        SyncMetadata metadata,
        CancellationToken cancellationToken,
        bool ignoreDeviceEcho = false)
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

        // Photograph documents are applied last: a photograph is only accepted when its meal slot is
        // already local, and the change feed never returns the same document twice, so a skipped
        // photograph would otherwise stay missing on this device for good.
        var documents = changeSet.Items
            .Where(item => item.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => CloudObjectPaths.GetEntityType(item.Path) == EntityType.MealPhoto ? 1 : 0)
            .ToArray();

        foreach (var item in documents)
        {
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

            // Echo of our own upload (while the local record still exists and the cache was not
            // rebuilt), or a payload written by a newer application version.
            if ((!ignoreDeviceEcho
                 && string.Equals(info.DeviceId, _environment.DeviceId, StringComparison.Ordinal)
                 && await HasLocalCopyAsync(info.EntityType, info.EntityId, cancellationToken).ConfigureAwait(false))
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

    /// <summary>
    /// True when the cache still holds the record a document refers to. An echo of our own upload may
    /// only be skipped while the local record exists: after the cache was emptied (an account change)
    /// the same document is missing data here and has to be applied again.
    /// </summary>
    private async Task<bool> HasLocalCopyAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken)
        => entityType switch
        {
            EntityType.MealEntry => await _entries.GetAsync(entityId, cancellationToken).ConfigureAwait(false) is not null,
            EntityType.MealPhoto => await _photos.GetAsync(entityId, cancellationToken).ConfigureAwait(false) is not null,
            EntityType.MealTemplate => await _templates.GetAsync(entityId, cancellationToken).ConfigureAwait(false) is not null,
            EntityType.ReminderSettings => await HasReminderAsync(entityId, cancellationToken).ConfigureAwait(false),
            _ => true,
        };

    /// <summary>True when the reminder of the supplied document exists in the cache.</summary>
    private async Task<bool> HasReminderAsync(Guid reminderId, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var reminders = await _reminders.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return reminders.Any(reminder => reminder.Id == reminderId);
    }

    /// <summary>Outcome counters of the pull phase.</summary>
    private sealed record PullOutcome(int Downloaded, int Deleted, int Skipped, string? Cursor);
}
