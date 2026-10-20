using DietTracker.Core.Enums;

namespace DietTracker.Core.Interfaces;

/// <summary>
/// Records local changes in the durable synchronization journal (outbox) and asks the
/// synchronization engine for a debounced cycle. Writing to the journal never waits for the cloud.
/// </summary>
public interface ISyncJournal
{
    /// <summary>Journals an upsert of an entity revision.</summary>
    Task RecordUpsertAsync(EntityType entityType, Guid entityId, long revision, CancellationToken cancellationToken = default);

    /// <summary>Journals a deletion (tombstone) so that the removal propagates to other devices.</summary>
    Task RecordDeleteAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Number of operations waiting to be uploaded.</summary>
    Task<int> CountPendingAsync(CancellationToken cancellationToken = default);
}
