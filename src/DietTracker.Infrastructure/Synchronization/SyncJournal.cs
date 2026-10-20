using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Durable synchronization journal (outbox) implementation. Writing a journal entry is a local
/// database write: the user interface never waits for a cloud round trip.
/// </summary>
public sealed class SyncJournal : ISyncJournal
{
    private readonly ISyncOperationRepository _operations;
    private readonly ICloudAccountRepository _accounts;
    private readonly ISyncEngine _engine;
    private readonly ILogger<SyncJournal> _logger;

    public SyncJournal(
        ISyncOperationRepository operations,
        ICloudAccountRepository accounts,
        ISyncEngine engine,
        ILogger<SyncJournal> logger)
    {
        _operations = operations;
        _accounts = accounts;
        _engine = engine;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task RecordUpsertAsync(EntityType entityType, Guid entityId, long revision, CancellationToken cancellationToken = default)
        => await RecordAsync(entityType, entityId, revision, SyncOperationType.Upsert, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task RecordDeleteAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken = default)
        => await RecordAsync(entityType, entityId, 0, SyncOperationType.Delete, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<int> CountPendingAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        return account is null ? 0 : await _operations.CountPendingAsync(account.Id, cancellationToken).ConfigureAwait(false);
    }

    private async Task RecordAsync(
        EntityType entityType,
        Guid entityId,
        long revision,
        SyncOperationType operationType,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null || !account.SynchronizationEnabled)
        {
            // Guest Mode or synchronization switched off: local data is still the source of truth,
            // there is simply nothing to upload yet.
            return;
        }

        var operation = new SyncOperation
        {
            CloudAccountId = account.Id,
            EntityType = entityType,
            EntityId = entityId,
            OperationType = operationType,
            EntityRevision = revision,
            Status = SyncOperationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            await _operations.EnqueueAsync(operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Journaling must never break the user's edit; the change is still stored locally and a
            // full reconciliation will pick it up.
            _logger.LogWarning(ex, "Could not journal {Operation} for {Entity} {EntityId}.", operationType, entityType, entityId);
            return;
        }

        // Debounced: the engine coalesces rapid edits into a single cycle.
        _engine.RequestSync(SyncTrigger.LocalChange);
    }
}
