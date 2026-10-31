using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Applies a remote meal entry, resolving conflicts without losing either version.</summary>
public sealed partial class SyncEngine
{
    private async Task<ApplyOutcome> ApplyMealEntryAsync(
        CloudObject item,
        string json,
        (int SchemaVersion, EntityType EntityType, Guid EntityId, long Revision, DateTimeOffset UpdatedAt, string DeviceId, bool IsDeleted, string ContentHash) header,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealEntry>(json);
        if (envelope?.Payload is null)
        {
            _logger.LogWarning("The meal entry document {ObjectId} could not be read and was ignored.", item.ObjectId);
            return ApplyOutcome.Unchanged;
        }

        var remote = envelope.Payload;
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        // The record may be unknown locally, or the same slot may already exist under a different id
        // (the empty placeholder this installation created when the day was first shown, or a record
        // created independently on the other device).
        var localById = await _entries.GetAsync(remote.Id, cancellationToken).ConfigureAwait(false);
        var slotOccupant = localById ?? await _entries.GetBySlotAsync(profile.Id, remote.Date, remote.MealCategoryKey, cancellationToken).ConfigureAwait(false);

        // A record this cache has never seen cannot be a conflict: there is no local edit to preserve.
        // This is what makes a newly published record - even at revision zero - adoptable instead of
        // being parked in ConflictRecord with no local payload at all.
        if (slotOccupant is null)
        {
            return await CreateFromRemoteAsync(remote, profile, envelope, item, cancellationToken).ConfigureAwait(false);
        }

        if (slotOccupant.Id != remote.Id)
        {
            // Two different records compete for one permanent slot. A slot that only holds the empty
            // placeholder adopts the cloud identity (its photographs follow); a slot with real content
            // keeps BOTH versions, because the revision comparison of two unrelated records would
            // otherwise discard one of them.
            if (MealContentRules.IsPlaceholder(slotOccupant))
            {
                return await CreateFromRemoteAsync(remote, profile, envelope, item, cancellationToken).ConfigureAwait(false);
            }

            if (MealContentRules.IsPlaceholder(remote))
            {
                // The incoming record holds no user content: it cannot displace anything, so it is
                // not worth a conflict row.
                return ApplyOutcome.Unchanged;
            }

            return await RecordMealConflictAsync(slotOccupant, json, envelope, item, cancellationToken).ConfigureAwait(false);
        }

        var local = slotOccupant;
        var decision = _conflictResolver.Decide(new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = remote.Id,
            LocalRevision = local.Revision,
            RemoteRevision = envelope.Revision,
            LocalContentHash = local.ContentHash,
            RemoteContentHash = envelope.ContentHash,
            LocalUpdatedAt = local.UpdatedAt,
            RemoteUpdatedAt = envelope.UpdatedAt,
            LocalBaseRevision = local.CloudRevision,
            RemoteRevisionToken = item.Revision,
            RemoteDeviceId = envelope.DeviceId,
            LocalDeviceId = _environment.DeviceId,
        });

        switch (decision)
        {
            case ConflictDecision.NoChange:
                local.CloudRevision = item.Revision;
                local.ContentHash = envelope.ContentHash;
                local.SyncStatus = SyncStatus.Synced;
                await _entries.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
                return ApplyOutcome.Unchanged;

            case ConflictDecision.KeepLocal:
                local.SyncStatus = SyncStatus.PendingUpdate;
                // The local row no longer corresponds to a known cloud version: clearing the token lets
                // the upload replace the remote copy instead of being deferred by the compare-and-swap.
                local.CloudRevision = null;
                await _entries.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
                await JournalUpsertAsync(accountId, EntityType.MealEntry, local.Id, local.Revision, cancellationToken).ConfigureAwait(false);
                return ApplyOutcome.Unchanged;

            case ConflictDecision.RequiresUserDecision:
                // Both versions are preserved until the user chooses; nothing is discarded.
                return await RecordMealConflictAsync(local, json, envelope, item, cancellationToken).ConfigureAwait(false);

            default:
                ApplyFields(local, remote, envelope, item);
                await _entries.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
                return ApplyOutcome.Applied;
        }
    }

    /// <summary>Creates the row of a record this cache does not hold yet (or only as a placeholder).</summary>
    private async Task<ApplyOutcome> CreateFromRemoteAsync(
        MealEntry remote,
        UserProfile profile,
        CloudEnvelope<MealEntry> envelope,
        CloudObject item,
        CancellationToken cancellationToken)
    {
        var category = await _categories.GetByKeyAsync(remote.MealCategoryKey, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            // The five permanent categories are configuration; without them the row cannot reference
            // a category, so the document is retried instead of being written with an empty key.
            return ApplyOutcome.Retry;
        }

        var target = new MealEntry
        {
            Id = remote.Id,
            LocalProfileId = profile.Id,
            Date = remote.Date,
            MealCategoryId = category.Id,
            MealCategoryKey = remote.MealCategoryKey,
            CreatedAt = remote.CreatedAt,
            Revision = remote.Revision,
        };

        ApplyFields(target, remote, envelope, item);

        var occupant = await _entries.GetBySlotAsync(profile.Id, remote.Date, remote.MealCategoryKey, cancellationToken).ConfigureAwait(false);
        if (occupant is not null && occupant.Id != remote.Id)
        {
            // Replace the placeholder of this slot in one transaction, keeping its photographs.
            await _entries.AdoptCloudIdentityAsync(target, occupant.Id, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _entries.UpsertAsync(target, cancellationToken).ConfigureAwait(false);
        }

        return ApplyOutcome.Applied;
    }

    /// <summary>Copies the changeable fields of a remote record onto a local row.</summary>
    private static void ApplyFields(MealEntry target, MealEntry remote, CloudEnvelope<MealEntry> envelope, CloudObject item)
    {
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
    }

    /// <summary>
    /// Stores both versions of a conflicting record. Returns the outcome for the pull: a revision the
    /// user already decided about is never re-opened, and an identical pending conflict is not stacked.
    /// </summary>
    private async Task<ApplyOutcome> RecordMealConflictAsync(
        MealEntry local,
        string json,
        CloudEnvelope<MealEntry> envelope,
        CloudObject item,
        CancellationToken cancellationToken)
    {
        var existing = await _conflicts.GetForEntityAsync(EntityType.MealEntry, local.Id, cancellationToken).ConfigureAwait(false);
        var sameRevision = existing.FirstOrDefault(conflict =>
            string.Equals(conflict.RemoteRevision, item.Revision, StringComparison.Ordinal));

        if (sameRevision is not null)
        {
            // The user already chose a version for this exact cloud revision (or still has to):
            // nothing new is decided and nothing is duplicated.
            return sameRevision.IsResolved ? ApplyOutcome.Unchanged : ApplyOutcome.Conflicted;
        }

        await _conflictResolver.RecordAsync(
            EntityType.MealEntry,
            local.Id,
            _serializer.Serialize(new CloudEnvelope<MealEntry>
            {
                EntityType = EntityType.MealEntry,
                EntityId = local.Id,
                Revision = local.Revision,
                Payload = local,
            }),
            json,
            local.UpdatedAt,
            envelope.UpdatedAt,
            item.Revision,
            envelope.DeviceId,
            cancellationToken).ConfigureAwait(false);

        return ApplyOutcome.Conflicted;
    }
}

