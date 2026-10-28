using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Push side of a synchronization cycle: journal operations and deletion markers.</summary>
public sealed partial class SyncEngine
{
    private const int MaxOperationsPerCycle = 200;

    /// <summary>Upper bound of photographs journaled per cycle by the reconciliation step.</summary>
    private const int MaxReconciledPhotosPerCycle = 50;

    private async Task<PushOutcome> PushPendingAsync(ICloudStorageProvider provider, CloudAccount account, CancellationToken cancellationToken)
    {
        var uploaded = 0;
        var skipped = 0;
        var deleted = 0;

        await JournalUnjournaledPhotosAsync(account, cancellationToken).ConfigureAwait(false);

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

    /// <summary>
    /// Journals photographs that were attached while no upload could happen - Guest Mode, uploads
    /// switched off, or a journal that was cleared - and therefore never reached the outbox. Without
    /// this step such a photograph stays on this device forever while the user believes it is
    /// synchronized. Records that are already queued (or waiting for a retry) are left alone.
    /// </summary>
    private async Task JournalUnjournaledPhotosAsync(CloudAccount account, CancellationToken cancellationToken)
    {
        var queued = 0;

        foreach (var photo in await _photos.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (queued >= MaxReconciledPhotosPerCycle)
            {
                return;
            }

            var cloudPath = _serializer.GetPhotoObjectPath(photo.Id);

            // Earlier builds stored the provider's revision token (for example "3") in CloudObjectId
            // instead of the object path; the identifier is normalized to the path that addresses the
            // binary, so a photograph is never looked up under a revision.
            if (photo.SyncStatus == SyncStatus.Synced && photo.CloudObjectId != cloudPath)
            {
                photo.CloudObjectId = cloudPath;
                await _photos.UpdateAsync(photo, cancellationToken).ConfigureAwait(false);
                continue;
            }

            // Only photographs that have never left this device are reconciled; a failed or pending
            // update already owns its journal entry.
            if (photo.SyncStatus != SyncStatus.PendingUpload || !string.IsNullOrEmpty(photo.CloudObjectId))
            {
                continue;
            }

            if (await _operations.HasOutstandingAsync(account.Id, EntityType.MealPhoto, photo.Id, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await _operations.EnqueueAsync(
                new SyncOperation
                {
                    CloudAccountId = account.Id,
                    EntityType = EntityType.MealPhoto,
                    EntityId = photo.Id,
                    OperationType = SyncOperationType.Upsert,
                    EntityRevision = 1,
                    Status = SyncOperationStatus.Pending,
                    CreatedAt = _clock.UtcNow,
                },
                cancellationToken).ConfigureAwait(false);

            queued++;
        }

        if (queued > 0)
        {
            _logger.LogInformation("Queued {Count} photograph(s) that had never been journaled.", queued);
        }
    }

    /// <summary>Outcome counters of the push phase.</summary>
    private sealed record PushOutcome(int Uploaded, int Skipped, int Deleted);
}
