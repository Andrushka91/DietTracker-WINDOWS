using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>One synchronization cycle: push the journal, then pull provider changes.</summary>
public sealed partial class SyncEngine
{
    private async Task<SyncResult> RunCycleAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null || !account.SynchronizationEnabled)
        {
            SetState(SyncState.NotConfigured);
            return SyncResult.Failure("sync.not-configured", state: SyncState.NotConfigured);
        }

        // Cadence gate: the user decided how often the cloud is contacted. Manual actions (the
        // "Synchronize now" button), a restore, a full upload and the retry of a dropped connection
        // always run; every other trigger - start-up, sign-in, a local edit, the hourly check - waits
        // until the chosen frequency has elapsed since the last successful cycle. The timestamp lives
        // on the account, so a weekly schedule survives restarts. A deferred cycle raises no state
        // change at all - it must be evaluated before the offline check too, so that a scheduled
        // knock while the machine is offline does not paint a warning strip either.
        if (!await IsDueAsync(trigger, account, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("Cycle triggered by {Trigger} deferred: the configured frequency has not elapsed.", trigger);
            return SyncResult.NotDue();
        }

        if (!_connectivity.IsOnline)
        {
            // When the machine is offline the state is reported as Offline; the journal keeps every
            // change so the cycle can be repeated as soon as connectivity returns.
            SetState(SyncState.Offline);
            return SyncResult.Failure("sync.offline", state: SyncState.Offline);
        }

        SetState(SyncState.Syncing);

        try
        {
            var provider = _providers.Create(account.ProviderType);
            await provider.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var metadata = await _metadata.GetAsync(account.Id, cancellationToken).ConfigureAwait(false) ?? new SyncMetadata
            {
                CloudAccountId = account.Id,
                ProviderType = account.ProviderType,
                DeviceId = _environment.DeviceId,
            };

            metadata.DeviceId = _environment.DeviceId;

            var push = await PushPendingAsync(provider, account, cancellationToken).ConfigureAwait(false);
            var pull = await PullChangesAsync(provider, account, metadata, cancellationToken).ConfigureAwait(false);

            metadata.LastSyncTimestamp = _clock.UtcNow;
            metadata.LastSyncCursor = pull.Cursor ?? metadata.LastSyncCursor;
            await _metadata.UpsertAsync(metadata, cancellationToken).ConfigureAwait(false);

            account.LastSynchronizedAt = _clock.UtcNow;
            account.LastAuthenticatedAt ??= _clock.UtcNow;
            var quota = await provider.GetQuotaAsync(cancellationToken).ConfigureAwait(false);
            account.QuotaTotalBytes = quota?.TotalBytes;
            account.QuotaRemainingBytes = quota?.RemainingBytes;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);

            _lastSuccessfulSyncAt = _clock.UtcNow;

            var pending = await _operations.CountPendingAsync(account.Id, cancellationToken).ConfigureAwait(false);
            var openConflicts = await _conflicts.CountOpenAsync(cancellationToken).ConfigureAwait(false);

            var result = SyncResult.Ok(push.Uploaded, pull.Downloaded, pull.Deleted, openConflicts, push.Skipped + pull.Skipped);
            SetState(pending > 0 || openConflicts > 0 ? SyncState.PendingChanges : SyncState.Synced, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            SetState(SyncState.PendingChanges);
            throw;
        }
        catch (CloudAuthorizationRequiredException ex)
        {
            _logger.LogInformation("The provider requires interactive reauthentication ({Reason}).", ex.ReasonCode);
            var result = SyncResult.Failure("sync.authentication-required", ex.ReasonCode, requiresReauth: true, state: SyncState.AuthenticationRequired);
            SetState(SyncState.AuthenticationRequired, result);
            return result;
        }
        catch (CloudQuotaExceededException ex)
        {
            var result = SyncResult.Failure("sync.quota-exceeded", ex.Message, state: SyncState.Error);
            SetState(SyncState.Error, result);
            return result;
        }
        catch (Exception ex)
        {
            // Local data is untouched: the cycle failed and will be retried with backoff.
            _logger.LogWarning(ex, "Synchronization cycle triggered by {Trigger} failed.", trigger);
            var result = SyncResult.Failure("sync.failed", ex.GetType().Name);
            SetState(SyncState.Error, result);
            return result;
        }
    }

    private void SetState(SyncState state, SyncResult? result = null)
    {
        _state = state;
        StateChanged?.Invoke(this, new SyncStateChangedEventArgs
        {
            State = state,
            LastResult = result,
            LastSuccessfulSyncAt = _lastSuccessfulSyncAt,
        });
    }

    /// <summary>
    /// Decides whether an automatic cycle may run right now, according to the synchronization
    /// frequency the user chose in Settings (weekly by default, monthly, or "only with the button").
    /// </summary>
    private async Task<bool> IsDueAsync(SyncTrigger trigger, CloudAccount account, CancellationToken cancellationToken)
    {
        // Explicit actions and consistency repairs always run: the user asked for them.
        if (trigger is SyncTrigger.Manual or SyncTrigger.PostRestore or SyncTrigger.ConnectivityRestored)
        {
            return true;
        }

        var frequency = await ReadSyncFrequencyAsync(cancellationToken).ConfigureAwait(false);
        var age = account.LastSynchronizedAt is { } last ? _clock.UtcNow - last : (TimeSpan?)null;
        return SyncFrequencyRules.IsDue(frequency, age);
    }

    /// <summary>
    /// Reads the schedule from the preferences. A read problem must never break a cycle, so it falls
    /// back to the default frequency.
    /// </summary>
    private async Task<SyncFrequency> ReadSyncFrequencyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var preferences = await _profiles.GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
            return preferences.SyncFrequency;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not read the synchronization frequency; assuming weekly.");
            return SyncFrequency.Weekly;
        }
    }

    /// <summary>Backoff for a failed operation: 5s, 15s, 45s, ... capped at 30 minutes.</summary>
    private static TimeSpan BackoffFor(int retryCount)
        => TimeSpan.FromSeconds(Math.Min(1800, 5 * Math.Pow(3, Math.Min(retryCount, 6))));
}
