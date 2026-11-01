using System.Text;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Infrastructure.Cloud;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Pull side of a synchronization cycle: apply provider changes to the local database.</summary>
public sealed partial class SyncEngine
{
    /// <summary>Documents the pull phase keeps in flight while it applies the previous one.</summary>
    private const int PullConcurrency = 8;

    /// <summary>Photograph binaries held in memory ahead of the apply cursor.</summary>
    private const int PhotoPrefetchLimit = 2;

    /// <param name="ignoreDeviceEcho">
    /// When <see langword="true"/> a document uploaded by this very installation is applied like any
    /// other one. The regular cycle skips those documents because the local record already holds the
    /// change, but a cache that was emptied on purpose (an account change) would otherwise stay empty.
    /// </param>
    /// <param name="progress">
    /// Reports "0 of N" and then one step per document, so a blocking bar can show real progress
    /// instead of a spinner. The background cycles never pass one.
    /// </param>
    private async Task<PullOutcome> PullChangesAsync(
        ICloudStorageProvider provider,
        CloudAccount account,
        SyncMetadata metadata,
        IProgress<RestoreProgress>? progress,
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
        var conflicted = 0;
        DateTimeOffset? retryFrom = null;

        // Photograph documents are applied last: a photograph is only accepted when its meal slot is
        // already local. A document that cannot be applied yet does not advance the cursor past it -
        // retryFrom keeps the next change query behind such a document so it is fetched again.
        var documents = changeSet.Items
            .Where(item => item.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => CloudObjectPaths.GetEntityType(item.Path) == EntityType.MealPhoto ? 1 : 0)
            .ToArray();

        // The bar starts empty and moves one step per document ("12 of 40 documents received from
        // the cloud"), which is what a sign-in barrier needs.
        progress?.Report(new RestoreProgress { Stage = "download", Processed = 0, Total = documents.Length });

        // The downloads of the next documents travel while the loop applies the current one: the
        // pull is limited by the network again instead of by one round trip per document. A
        // photograph's binary starts as soon as its metadata arrived, so the picture is already in
        // memory (or on its way) when the apply step needs it.
        var binaries = new PhotoBytePrefetcher(PhotoPrefetchLimit);
        await using var downloads = new OrderedPrefetcher<CloudObject, string?>(
            documents,
            PullConcurrency,
            (item, _, token) => FetchDocumentAsync(provider, item, binaries, token),
            cancellationToken);

        foreach (var item in documents)
        {
            var json = await downloads.NextAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(new RestoreProgress
            {
                Stage = "download",
                Processed = downloads.Consumed,
                Total = documents.Length,
            });

            if (json is null)
            {
                // Listed but not downloadable: retried, never silently counted as synchronized.
                skipped++;
                retryFrom = Earliest(retryFrom, item.ModifiedAt);
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
                else
                {
                    // A deletion marker for a record this device never had: nothing to remove.
                    _logger.LogDebug(
                        "Deletion marker for {EntityType} {EntityId} ignored: no such record locally.",
                        info.EntityType, info.EntityId);
                }

                continue;
            }

            var outcome = await ApplyRemoteEntityAsync(provider, item, json, info, account.Id, binaries, cancellationToken).ConfigureAwait(false);
            switch (outcome)
            {
                case ApplyOutcome.Applied:
                    downloaded++;
                    break;

                case ApplyOutcome.Conflicted:
                    conflicted++;
                    break;

                case ApplyOutcome.Retry:
                    skipped++;
                    retryFrom = Earliest(retryFrom, item.ModifiedAt);
                    break;

                default:
                    skipped++;
                    break;
            }
        }

        // The stored cursor may not pass a document that still has to be retried: clamping it one
        // second before the oldest such document makes the next cycle pick it up again.
        var next = changeSet.NextCursor;
        if (retryFrom is not null)
        {
            var clamped = retryFrom.Value.AddSeconds(-1);
            var value = next?.Watermark is { } watermark && watermark <= clamped
                ? next.Value
                : clamped.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);

            next = new SyncCursor
            {
                ProviderType = account.ProviderType,
                ProviderUserId = account.ProviderUserId,
                Watermark = next?.Watermark is { } existing && existing <= clamped ? existing : clamped,
                Value = value,
            };
        }

        return new PullOutcome(downloaded, deleted, skipped, conflicted, next?.Value);
    }

    /// <summary>
    /// Downloads one document (or reuses the payload the provider already returned) and starts the
    /// download of a photograph's binary as soon as its metadata has arrived.
    /// </summary>
    private async Task<string?> FetchDocumentAsync(
        ICloudStorageProvider provider,
        CloudObject item,
        PhotoBytePrefetcher binaries,
        CancellationToken cancellationToken)
    {
        var json = item.Content is { Length: > 0 }
            ? Encoding.UTF8.GetString(item.Content)
            : await DownloadTextAsync(provider, item.ObjectId, cancellationToken).ConfigureAwait(false);

        if (json is not null
            && CloudObjectPaths.GetEntityType(item.Path) == EntityType.MealPhoto
            && Guid.TryParse(Path.GetFileNameWithoutExtension(item.Path), out var photoId))
        {
            var objectPath = _serializer.GetPhotoObjectPath(photoId);
            binaries.Prefetch(photoId, () => ReadBytesAsync(provider, objectPath, cancellationToken));
        }

        return json;
    }

    /// <summary>The complete content of one provider object, or null when it does not exist.</summary>
    private static async Task<byte[]?> ReadBytesAsync(
        ICloudStorageProvider provider,
        string objectPath,
        CancellationToken cancellationToken)
    {
        await using var stream = await provider.OpenReadAsync(objectPath, cancellationToken).ConfigureAwait(false);
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>The older of two optional timestamps.</summary>
    private static DateTimeOffset Earliest(DateTimeOffset? current, DateTimeOffset candidate)
        => current is { } existing && existing <= candidate ? existing : candidate;

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
    private sealed record PullOutcome(int Downloaded, int Deleted, int Skipped, int Conflicted, string? Cursor);
}
