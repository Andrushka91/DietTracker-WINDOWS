using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Rebuilds an emptied local cache from the connected account (used by the cloud-only data lifecycle
/// after an account change or after the cache was purged).
/// </summary>
public sealed partial class SyncEngine
{
    /// <inheritdoc />
    public async Task<SyncResult> RebuildFromCloudAsync(
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
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

            // A null cursor enumerates the whole dataset. The device echo check is switched off on
            // purpose: the local cache does not exist any more, so a document this installation uploaded
            // earlier is not an echo - it is data that is missing here and has to be applied again.
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
                cancellationToken,
                ignoreDeviceEcho: true).ConfigureAwait(false);

            var metadata = await _metadata.GetAsync(account.Id, cancellationToken).ConfigureAwait(false) ?? new SyncMetadata
            {
                CloudAccountId = account.Id,
                ProviderType = account.ProviderType,
                DeviceId = _environment.DeviceId,
            };

            metadata.DeviceId = _environment.DeviceId;
            metadata.LastSyncCursor = pull.Cursor ?? metadata.LastSyncCursor;
            metadata.LastSyncTimestamp = _clock.UtcNow;
            await _metadata.UpsertAsync(metadata, cancellationToken).ConfigureAwait(false);

            account.LastSynchronizedAt = _clock.UtcNow;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);

            progress?.Report(new RestoreProgress { Stage = "complete", Processed = pull.Downloaded, Total = pull.Downloaded });

            _lastSuccessfulSyncAt = _clock.UtcNow;
            var result = SyncResult.Ok(0, pull.Downloaded, pull.Deleted, 0, pull.Skipped);
            SetState(SyncState.Synced, result);

            _logger.LogInformation(
                "The local cache was rebuilt from the connected account: {Downloaded} record(s) applied, {Skipped} skipped.",
                pull.Downloaded, pull.Skipped);

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
            _logger.LogWarning(ex, "Rebuilding the local cache from the cloud failed.");
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
