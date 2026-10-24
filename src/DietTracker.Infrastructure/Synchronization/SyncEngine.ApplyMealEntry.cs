using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Applies a remote meal entry, resolving conflicts without losing either version.</summary>
public sealed partial class SyncEngine
{
    private async Task<bool> ApplyMealEntryAsync(
        CloudObject item,
        string json,
        (int SchemaVersion, EntityType EntityType, Guid EntityId, long Revision, DateTimeOffset UpdatedAt, string DeviceId, bool IsDeleted, string ContentHash) header,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealEntry>(json);
        if (envelope?.Payload is null)
        {
            return false;
        }

        var remote = envelope.Payload;
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        // The record may be unknown locally, or the same slot may already exist under a different id
        // (for example when the same meal was created independently on two devices).
        var local = await _entries.GetAsync(remote.Id, cancellationToken).ConfigureAwait(false)
                    ?? await _entries.GetBySlotAsync(profile.Id, remote.Date, remote.MealCategoryKey, cancellationToken).ConfigureAwait(false);

        var decision = _conflictResolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = remote.Id,
            LocalRevision = local?.Revision ?? 0,
            RemoteRevision = envelope.Revision,
            LocalContentHash = local?.ContentHash,
            RemoteContentHash = envelope.ContentHash,
            LocalUpdatedAt = local?.UpdatedAt ?? DateTimeOffset.MinValue,
            RemoteUpdatedAt = envelope.UpdatedAt,
            LocalBaseRevision = local?.CloudRevision,
            RemoteRevisionToken = item.Revision,
            RemoteDeviceId = envelope.DeviceId,
            LocalDeviceId = _environment.DeviceId,
        });

        switch (decision)
        {
            case ConflictDecision.NoChange:
                if (local is not null)
                {
                    local.CloudRevision = item.Revision;
                    local.ContentHash = envelope.ContentHash;
                    local.SyncStatus = SyncStatus.Synced;
                    await _entries.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
                }

                return false;

            case ConflictDecision.KeepLocal:
                if (local is not null)
                {
                    local.SyncStatus = SyncStatus.PendingUpdate;
                    await _entries.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
                    await JournalUpsertAsync(accountId, EntityType.MealEntry, local.Id, local.Revision, cancellationToken).ConfigureAwait(false);
                }

                return false;

            case ConflictDecision.RequiresUserDecision:
                // Both versions are preserved until the user chooses; nothing is discarded.
                await _conflictResolver.RecordAsync(
                    EntityType.MealEntry,
                    remote.Id,
                    local is null ? "{}" : _serializer.Serialize(new CloudEnvelope<MealEntry>
                    {
                        EntityType = EntityType.MealEntry,
                        EntityId = local.Id,
                        Revision = local.Revision,
                        Payload = local,
                    }),
                    json,
                    local?.UpdatedAt ?? DateTimeOffset.MinValue,
                    envelope.UpdatedAt,
                    item.Revision,
                    envelope.DeviceId,
                    cancellationToken).ConfigureAwait(false);
                return false;

            default:
                var target = local ?? new MealEntry
                {
                    Id = remote.Id,
                    LocalProfileId = profile.Id,
                    Date = remote.Date,
                    MealCategoryKey = remote.MealCategoryKey,
                    CreatedAt = remote.CreatedAt,
                };

                if (local is null)
                {
                    var category = await _categories.GetByKeyAsync(remote.MealCategoryKey, cancellationToken).ConfigureAwait(false);
                    target.MealCategoryId = category?.Id ?? Guid.Empty;
                }

                target.Title = remote.Title;
                target.Ingredients = remote.Ingredients;
                target.PlannedTime = remote.PlannedTime;
                target.Status = remote.Status;
                target.CompletedAt = remote.CompletedAt;
                target.Revision = Math.Max(target.Revision, envelope.Revision);
                target.UpdatedAt = envelope.UpdatedAt;
                target.CloudRevision = item.Revision;
                target.ContentHash = envelope.ContentHash;
                target.SyncStatus = SyncStatus.Synced;

                await _entries.UpsertAsync(target, cancellationToken).ConfigureAwait(false);
                return true;
        }
    }
}
