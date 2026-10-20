using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Push side of a synchronization cycle: journal operations and deletion markers.</summary>
public sealed partial class SyncEngine
{
    private const int MaxOperationsPerCycle = 200;

    private async Task<PushOutcome> PushPendingAsync(ICloudStorageProvider provider, CloudAccount account, CancellationToken cancellationToken)
    {
        var uploaded = 0;
        var skipped = 0;
        var deleted = 0;

        var pending = await _operations.GetPendingAsync(account.Id, MaxOperationsPerCycle, cancellationToken).ConfigureAwait(false);
        foreach (var operation in pending)
        {
            try
            {
                if (await PushOneAsync(provider, operation, cancellationToken).ConfigureAwait(false))
                {
                    uploaded++;
                }
                else
                {
                    skipped++;
                }

                operation.Status = SyncOperationStatus.Completed;
                operation.CompletedAt = _clock.UtcNow;
                operation.LastError = null;
                operation.NextAttemptAt = null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (CloudAuthorizationRequiredException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Recoverable failures are retried with exponential backoff; the local change is kept.
                operation.Status = SyncOperationStatus.Failed;
                operation.RetryCount++;
                operation.LastError = ex.GetType().Name;
                operation.NextAttemptAt = _clock.UtcNow + BackoffFor(operation.RetryCount);
                _logger.LogWarning(
                    ex, "Upload of {EntityType} {EntityId} failed (attempt {Retry}).",
                    operation.EntityType, operation.EntityId, operation.RetryCount);
            }

            await _operations.UpdateAsync(operation, cancellationToken).ConfigureAwait(false);
        }

        foreach (var tombstone in await _tombstones.GetPendingAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await provider.DeleteAsync(_serializer.GetObjectPath(tombstone.EntityType, tombstone.EntityId), cancellationToken).ConfigureAwait(false);
                await _tombstones.MarkSyncedAsync(tombstone.Id, cancellationToken).ConfigureAwait(false);
                deleted++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Deletion of {EntityType} {EntityId} in the cloud failed.", tombstone.EntityType, tombstone.EntityId);
            }
        }

        return new PushOutcome(uploaded, skipped, deleted);
    }

    /// <summary>Outcome counters of the push phase.</summary>
    private sealed record PushOutcome(int Uploaded, int Skipped, int Deleted);
}
