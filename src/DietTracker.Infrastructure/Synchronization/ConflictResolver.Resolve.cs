using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Applies the user's decision to a previously recorded conflict.</summary>
public sealed partial class ConflictResolver
{
    /// <inheritdoc />
    public async Task ResolveAsync(Guid conflictId, ConflictResolutionChoice choice, CancellationToken cancellationToken = default)
    {
        var conflict = (await _conflicts.GetOpenAsync(cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.Id == conflictId);

        if (conflict is null)
        {
            return;
        }

        switch (choice)
        {
            case ConflictResolutionChoice.KeepLocal:
                await KeepLocalAsync(conflict, cancellationToken).ConfigureAwait(false);
                break;

            case ConflictResolutionChoice.KeepRemote:
                await KeepRemoteAsync(conflict, cancellationToken).ConfigureAwait(false);
                break;

            default:
                await KeepBothAsync(conflict, cancellationToken).ConfigureAwait(false);
                break;
        }

        conflict.IsResolved = true;
        await _conflicts.UpdateAsync(conflict, cancellationToken).ConfigureAwait(false);
    }

    private async Task KeepLocalAsync(ConflictRecord conflict, CancellationToken cancellationToken)
    {
        if (conflict.EntityType == EntityType.MealEntry)
        {
            var entry = await _entries.GetAsync(conflict.EntityId, cancellationToken).ConfigureAwait(false);
            if (entry is not null)
            {
                entry.SyncStatus = SyncStatus.PendingUpdate;
                entry.CloudRevision = null;
                await _entries.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
            }
        }

        await JournalUpsertAsync(conflict.EntityType, conflict.EntityId, cancellationToken).ConfigureAwait(false);
    }

    private async Task KeepRemoteAsync(ConflictRecord conflict, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealEntry>(conflict.RemotePayload);
        if (envelope?.Payload is null)
        {
            return;
        }

        var remote = envelope.Payload;
        var local = await _entries.GetAsync(conflict.EntityId, cancellationToken).ConfigureAwait(false);
        if (local is null)
        {
            return;
        }

        // Only content is taken over; the local identifiers stay stable so references remain valid.
        local.Title = remote.Title;
        local.Ingredients = remote.Ingredients;
        local.Quantities = remote.Quantities;
        local.Notes = remote.Notes;
        local.PlannedTime = remote.PlannedTime;
        local.Status = remote.Status;
        local.CompletedAt = remote.CompletedAt;
        local.Revision = Math.Max(local.Revision, remote.Revision);
        local.SyncStatus = SyncStatus.Synced;
        local.CloudRevision = conflict.RemoteRevision;
        local.UpdatedAt = _clock.UtcNow;

        await _entries.UpsertAsync(local, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Keeps the remote version as the live record and preserves the local version as a reusable
    /// template named "Conflict copy", so the user's text is never lost.
    /// </summary>
    private async Task KeepBothAsync(ConflictRecord conflict, CancellationToken cancellationToken)
    {
        var localEnvelope = _serializer.Deserialize<MealEntry>(conflict.LocalPayload);
        if (localEnvelope?.Payload is { } localPayload)
        {
            var template = Core.BusinessRules.MealCopyRules.CreateTemplate(
                localPayload,
                $"Conflict copy {localPayload.Date:yyyy-MM-dd} {localPayload.MealCategoryKey}",
                _clock.UtcNow);

            await _templates.UpsertAsync(template, cancellationToken).ConfigureAwait(false);
            await JournalUpsertAsync(EntityType.MealTemplate, template.Id, cancellationToken).ConfigureAwait(false);
        }

        await KeepRemoteAsync(conflict, cancellationToken).ConfigureAwait(false);
    }
}
