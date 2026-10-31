using System.Text;
using System.Text.Json;
using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Serialization;
using DietTracker.Core.Utilities;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Raised when the cloud copy of a record changed after this device last synchronized it. The upload
/// is deferred instead of overwriting the other device's edit: the pull phase of the same cycle
/// resolves the two versions (newer revision wins, a genuine concurrent edit becomes a conflict).
/// </summary>
internal sealed class RemoteChangedException : Exception
{
    public RemoteChangedException(string path)
        : base($"The cloud copy of '{path}' changed on another device; the upload is deferred.")
        => Path = path;

    public string Path { get; }
}

/// <summary>Per-entity upload logic, including idempotency through content hashes.</summary>
public sealed partial class SyncEngine
{
    /// <summary>Uploads a single journaled operation. Returns false when there was nothing to upload.</summary>
    private async Task<bool> PushOneAsync(ICloudStorageProvider provider, SyncOperation operation, CancellationToken cancellationToken)
    {
        if (operation.OperationType == SyncOperationType.Delete)
        {
            await provider.DeleteAsync(_serializer.GetObjectPath(operation.EntityType, operation.EntityId), cancellationToken).ConfigureAwait(false);
            return true;
        }

        return operation.EntityType switch
        {
            EntityType.MealEntry => await PushMealEntryAsync(provider, operation, cancellationToken).ConfigureAwait(false),
            EntityType.MealPhoto => await PushPhotoAsync(provider, operation, cancellationToken).ConfigureAwait(false),
            EntityType.MealTemplate => await PushTemplateAsync(provider, operation, cancellationToken).ConfigureAwait(false),
            EntityType.ReminderSettings => await PushReminderAsync(provider, operation, cancellationToken).ConfigureAwait(false),
            EntityType.AppPreferences => await PushPreferencesAsync(provider, cancellationToken).ConfigureAwait(false),

            // Meal categories are per-device configuration whose identity is the fixed key; the
            // contract is enforced by the schema on every device, so they are not uploaded.
            _ => false,
        };
    }

    private async Task<bool> PushMealEntryAsync(ICloudStorageProvider provider, SyncOperation operation, CancellationToken cancellationToken)
    {
        var entry = await _entries.GetAsync(operation.EntityId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return false;
        }

        // Empty placeholder slots carry no user content and are deliberately not uploaded.
        if (MealContentRules.IsPlaceholder(entry))
        {
            return false;
        }

        var hash = Hashing.Sha256(JsonSerializer.Serialize(entry, DietTrackerJson.Compact));
        if (entry.SyncStatus == SyncStatus.Synced
            && !string.IsNullOrEmpty(entry.CloudRevision)
            && string.Equals(entry.ContentHash, hash, StringComparison.Ordinal))
        {
            // Already uploaded and unchanged: avoids repeated uploads of identical records.
            return false;
        }

        var path = _serializer.GetObjectPath(EntityType.MealEntry, entry.Id);

        // Compare-and-swap: an update may only overwrite the exact cloud version this device last saw.
        // A record whose CloudRevision is empty either never reached the cloud or was deliberately
        // marked "local wins" after a conflict, so it is uploaded unconditionally.
        if (!string.IsNullOrEmpty(entry.CloudRevision))
        {
            var remote = await provider.DownloadAsync(path, cancellationToken).ConfigureAwait(false);
            if (remote is not null
                && !string.IsNullOrEmpty(remote.Revision)
                && !string.Equals(remote.Revision, entry.CloudRevision, StringComparison.Ordinal))
            {
                var remoteJson = remote.Content is { Length: > 0 } content ? Encoding.UTF8.GetString(content) : null;
                var remoteHeader = remoteJson is null ? null : _serializer.ReadHeader(remoteJson);

                if (remoteHeader is { } header && header.Revision < entry.Revision)
                {
                    // This device holds the newer revision: it wins, so the stale base is dropped and
                    // the upload replaces the older cloud copy instead of being deferred forever.
                    entry.CloudRevision = null;
                    await _entries.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // The cloud holds an equal or newer version. The change feed will not report it
                    // again (the cursor already passed this document), so it is applied here; the
                    // journal entry survives and turns into a no-op on its next attempt.
                    if (remoteJson is not null && remoteHeader is { } applied)
                    {
                        await ApplyRemoteEntityAsync(provider, remote, remoteJson, applied, operation.CloudAccountId, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    throw new RemoteChangedException(path);
                }
            }
        }

        var envelope = new CloudEnvelope<MealEntry>
        {
            EntityType = EntityType.MealEntry,
            EntityId = entry.Id,
            LocalProfileId = entry.LocalProfileId,
            Revision = entry.Revision,
            UpdatedAt = entry.UpdatedAt,
            DeviceId = _environment.DeviceId,
            ContentHash = hash,
            Payload = entry,
        };

        var json = _serializer.Serialize(envelope);
        var uploaded = await provider.UploadAsync(
            new CloudObject
            {
                ObjectId = path,
                Path = path,
                Revision = entry.CloudRevision,
                ContentHash = hash,
                ModifiedAt = entry.UpdatedAt,
                SizeBytes = Encoding.UTF8.GetByteCount(json),
                Content = Encoding.UTF8.GetBytes(json),
            },
            cancellationToken).ConfigureAwait(false);

        // Only now - after the provider confirmed the upload - is the record marked as synced.
        entry.CloudRevision = uploaded.Revision;
        entry.ContentHash = hash;
        entry.SyncStatus = SyncStatus.Synced;
        await _entries.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
