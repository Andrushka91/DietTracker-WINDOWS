using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>Read/write access to remembered cloud accounts.</summary>
public interface ICloudAccountRepository
{
    Task<IReadOnlyList<CloudAccount>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<CloudAccount?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CloudAccount?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Finds a remembered account by provider and provider user id (never by email alone).</summary>
    Task<CloudAccount?> FindAsync(Enums.ProviderType providerType, string providerUserId, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the account and makes it the single active account.</summary>
    Task UpsertActiveAsync(CloudAccount account, CancellationToken cancellationToken = default);

    Task UpdateAsync(CloudAccount account, CancellationToken cancellationToken = default);

    /// <summary>Signs the account out locally; meal data is untouched.</summary>
    Task ClearActiveAsync(CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Durable synchronization journal (outbox) with retry bookkeeping.</summary>
public interface ISyncOperationRepository
{
    Task EnqueueAsync(SyncOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the operations that are due now, honouring the exponential backoff timestamp. With
    /// <paramref name="includeDeferred"/> the operations that still wait for a retry are returned as
    /// well: an account transition must be able to drain the journal immediately instead of being
    /// refused while a backoff that nobody is waiting for counts down.
    /// </summary>
    Task<IReadOnlyList<SyncOperation>> GetPendingAsync(Guid cloudAccountId, int maxCount, bool includeDeferred = false, CancellationToken cancellationToken = default);

    Task UpdateAsync(SyncOperation operation, CancellationToken cancellationToken = default);

    Task<int> CountPendingAsync(Guid cloudAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the journal still holds an operation for the record - pending, running or waiting for a
    /// retry. Used to decide whether a local record that has never been uploaded needs a new journal
    /// entry, without re-queueing a record that is already (or still) on its way.
    /// </summary>
    Task<bool> HasOutstandingAsync(Guid cloudAccountId, Enums.EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Removes completed operations older than the supplied instant (journal compaction).</summary>
    Task<int> CompactAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    /// <summary>Drops the journal of an account after a confirmed full re-upload.</summary>
    Task ClearForAccountAsync(Guid cloudAccountId, CancellationToken cancellationToken = default);
}

/// <summary>Per account/provider synchronization bookkeeping (cursors, timestamps, schema version).</summary>
public interface ISyncMetadataRepository
{
    Task<SyncMetadata?> GetAsync(Guid cloudAccountId, CancellationToken cancellationToken = default);

    Task UpsertAsync(SyncMetadata metadata, CancellationToken cancellationToken = default);

    Task<DateTimeOffset?> GetLastSyncTimestampAsync(CancellationToken cancellationToken = default);
}

/// <summary>Conflicts that require a user decision.</summary>
public interface IConflictRepository
{
    Task AddAsync(ConflictRecord conflict, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ConflictRecord>> GetOpenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Every conflict ever recorded for one record (open and resolved). Used to avoid re-opening a
    /// conflict the user already decided about when the same cloud revision is seen again.
    /// </summary>
    Task<IReadOnlyList<ConflictRecord>> GetForEntityAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default);

    Task UpdateAsync(ConflictRecord conflict, CancellationToken cancellationToken = default);

    Task<int> CountOpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Deletion markers propagated to the cloud.</summary>
public interface ITombstoneRepository
{
    Task AddAsync(DeletionTombstone tombstone, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<DeletionTombstone>> GetPendingAsync(CancellationToken cancellationToken = default);

    Task MarkSyncedAsync(Guid id, CancellationToken cancellationToken = default);
}
