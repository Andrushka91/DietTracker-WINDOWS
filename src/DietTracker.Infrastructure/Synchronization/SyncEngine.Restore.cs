using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Restore preview and full restore (used after reinstalling or on a second computer).</summary>
public sealed partial class SyncEngine
{
    /// <inheritdoc />
    public async Task<RestorePreview?> PreviewRestoreAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var provider = _providers.Create(account.ProviderType);
        await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var items = await provider.ListAsync("entities/", cancellationToken).ConfigureAwait(false);
        if (items.Count == 0)
        {
            return new RestorePreview
            {
                ProviderType = account.ProviderType,
                AccountEmail = account.Email,
                SchemaVersion = CloudSchema.CurrentVersion,
            };
        }

        return new RestorePreview
        {
            ProviderType = account.ProviderType,
            AccountEmail = account.Email,
            MealEntries = items.Count(i => i.Path.Contains("/MealEntry/", StringComparison.OrdinalIgnoreCase)),
            Photos = items.Count(i => i.Path.Contains("/MealPhoto/", StringComparison.OrdinalIgnoreCase)),
            Templates = items.Count(i => i.Path.Contains("/MealTemplate/", StringComparison.OrdinalIgnoreCase)),
            OldestRecord = items.Min(i => i.ModifiedAt),
            NewestRecord = items.Max(i => i.ModifiedAt),
            SchemaVersion = CloudSchema.CurrentVersion,
        };
    }

    /// <inheritdoc />
    public async Task<SyncResult> RestoreAsync(IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return SyncResult.Failure("sync.not-configured", state: SyncState.NotConfigured);
        }

        if (!_connectivity.IsOnline)
        {
            return SyncResult.Failure("sync.offline", state: SyncState.Offline);
        }

        await _cycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SetState(SyncState.Syncing);
            var provider = _providers.Create(account.ProviderType);
            await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);

            progress?.Report(new RestoreProgress { Stage = "discover" });

            var stored = await _metadata.GetAsync(account.Id, cancellationToken).ConfigureAwait(false);
            var metadata = stored ?? new SyncMetadata
            {
                CloudAccountId = account.Id,
                ProviderType = account.ProviderType,
                DeviceId = _environment.DeviceId,
            };

            // A null cursor means "enumerate the whole dataset". Local data is never deleted, so a
            // restore can only add or update records - which is what makes a failed restore harmless.
            var pull = await PullChangesAsync(
                provider,
                account,
                new SyncMetadata
                {
                    CloudAccountId = account.Id,
                    ProviderType = account.ProviderType,
                    DeviceId = _environment.DeviceId,
                    LastSyncCursor = null,
                },
                cancellationToken).ConfigureAwait(false);

            metadata.LastSyncCursor = pull.Cursor ?? metadata.LastSyncCursor;
            metadata.LastSyncTimestamp = _clock.UtcNow;
            await _metadata.UpsertAsync(metadata, cancellationToken).ConfigureAwait(false);

            account.LastSynchronizedAt = _clock.UtcNow;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);

            progress?.Report(new RestoreProgress { Stage = "complete", Processed = pull.Downloaded, Total = pull.Downloaded });

            _lastSuccessfulSyncAt = _clock.UtcNow;
            var result = SyncResult.Ok(0, pull.Downloaded, pull.Deleted, 0, pull.Skipped);
            SetState(SyncState.Synced, result);
            return result;
        }
        catch (CloudAuthorizationRequiredException ex)
        {
            var result = SyncResult.Failure("sync.authentication-required", ex.ReasonCode, requiresReauth: true, state: SyncState.AuthenticationRequired);
            SetState(SyncState.AuthenticationRequired, result);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Restore failed.");
            var result = SyncResult.Failure("sync.restore-failed", ex.GetType().Name);
            SetState(SyncState.Error, result);
            return result;
        }
        finally
        {
            _cycleGate.Release();
        }
    }
}
