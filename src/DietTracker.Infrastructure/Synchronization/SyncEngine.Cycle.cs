using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>One synchronization cycle: push the journal, then pull provider changes.</summary>
public sealed partial class SyncEngine
{
    /// <summary>
    /// Minimum distance between two automatic downloads of an open window. The foreground loop asks
    /// more often than this; the extra calls collapse into "not due" before any request leaves.
    /// </summary>
    private static readonly TimeSpan AutoPullInterval = TimeSpan.FromSeconds(10);

    private async Task<SyncResult> RunCycleAsync(SyncTrigger trigger, CancellationToken cancellationToken, bool pushOnly = false, bool pullOnly = false)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            SetState(SyncState.NotConfigured, trigger: trigger);
            return SyncResult.Failure("sync.not-configured", state: SyncState.NotConfigured);
        }

        if (!account.SynchronizationEnabled && trigger is not SyncTrigger.AccountTransition)
        {
            // An account is connected but uploads are not allowed. That is a pause, and reporting it as
            // "no account connected" would be plainly wrong - the account card shows the e-mail.
            // An account transition is the exception: the confirmed dialog promises that everything
            // still pending is uploaded before the only copy of the data is deleted, so holding the
            // cycle back here would block signing out forever (or push the user into discarding).
            SetState(SyncState.Paused, trigger: trigger);
            return SyncResult.Failure("sync.paused", state: SyncState.Paused);
        }

        // Cadence gate: the user decided how often the cloud is contacted on its own. Manual actions
        // (the "Synchronize now" button), a restore, a full upload, the retry of a dropped connection,
        // a local edit and a sign-in always run; the remaining automatic triggers - start-up and the
        // hourly check - wait until the chosen frequency has elapsed since the last successful cycle.
        // The timestamp lives on the account, so a weekly schedule survives restarts. A deferred cycle
        // raises no state change at all - it must be evaluated before the offline check too, so that a
        // scheduled knock while the machine is offline does not paint a warning strip either.
        if (!await IsDueAsync(trigger, account, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("Cycle triggered by {Trigger} deferred: the configured frequency has not elapsed.", trigger);
            return SyncResult.NotDue();
        }

        // The passive download of an open window is throttled on its own: the foreground loop asks
        // every few seconds, but only one small change-feed request per interval actually leaves.
        if (pullOnly)
        {
            var sinceLastPull = _lastAutoPullAt is { } lastPull ? _clock.UtcNow - lastPull : (TimeSpan?)null;
            if (sinceLastPull is { } age && age < AutoPullInterval)
            {
                return SyncResult.NotDue();
            }

            _lastAutoPullAt = _clock.UtcNow;
        }

        if (!_connectivity.IsOnline)
        {
            // When the machine is offline the state is reported as Offline; the journal keeps every
            // change so the cycle can be repeated as soon as connectivity returns.
            SetState(SyncState.Offline, trigger: trigger);
            return SyncResult.Failure("sync.offline", state: SyncState.Offline);
        }

        // The passive download of an open window keeps its state quiet: announcing "Synchronizing"
        // every few seconds would make the status area blink for work the user did not start. A
        // download that actually brought records still publishes its new state (and refreshes the
        // visible day through it), other cycles publish as they always did.
        var quiet = trigger is SyncTrigger.AutoPull && _state is SyncState.Synced;
        if (!quiet)
        {
            SetState(SyncState.Syncing, trigger: trigger);
        }

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

            // The push decides the outcome of an account transition on its own: a sign-out needs the
            // journal drained, not a download of the dataset that is about to be deleted locally -
            // and the download is what made every attempt take half a minute. The transition also
            // uploads operations that wait for their retry: leaving them out would keep them counted
            // as pending while the sign-out waits for exactly them.
            // A pull-only cycle is the opposite case: nothing is journaled, so there is nothing to
            // upload and the journal of another account must not be touched.
            var push = pullOnly
                ? new PushOutcome(0, 0, 0)
                : await PushPendingAsync(provider, account, trigger is SyncTrigger.AccountTransition, cancellationToken).ConfigureAwait(false);

            // The pull result stays empty for a push-only cycle, so the cursor bookkeeping below
            // keeps the stored cursor untouched.
            var pull = pushOnly
                ? new PullOutcome(0, 0, 0, 0, null)
                : await PullChangesAsync(provider, account, metadata, cancellationToken).ConfigureAwait(false);

            metadata.LastSyncTimestamp = _clock.UtcNow;
            metadata.LastSyncCursor = pull.Cursor ?? metadata.LastSyncCursor;
            await _metadata.UpsertAsync(metadata, cancellationToken).ConfigureAwait(false);

            account.LastSynchronizedAt = _clock.UtcNow;
            account.LastAuthenticatedAt ??= _clock.UtcNow;
            var quota = await provider.GetQuotaAsync(cancellationToken).ConfigureAwait(false);
            account.QuotaTotalBytes = quota?.TotalBytes;
            account.QuotaRemainingBytes = quota?.RemainingBytes;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);

            var pending = await _operations.CountPendingAsync(account.Id, cancellationToken).ConfigureAwait(false);
            var openConflicts = await _conflicts.CountOpenAsync(cancellationToken).ConfigureAwait(false);
            var retryable = pending > 0 || openConflicts > 0 || push.Skipped > 0 || pull.Skipped > 0 || pull.Conflicted > 0;

            // "Last successful synchronization" is only claimed when every listed document was applied:
            // a skipped download or a deferred photograph used to look like success.
            if (!retryable)
            {
                _lastSuccessfulSyncAt = _clock.UtcNow;
            }

            var result = SyncResult.Ok(push.Uploaded, pull.Downloaded, pull.Deleted, openConflicts, push.Skipped + pull.Skipped);

            // A quiet pull that found nothing keeps the state it had; one that brought records
            // publishes the result, which is also what refreshes the visible day.
            if (!quiet || retryable || pull.Downloaded + pull.Deleted > 0)
            {
                SetState(retryable ? SyncState.PendingChanges : SyncState.Synced, result, trigger);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            SetState(SyncState.PendingChanges, trigger: trigger);
            throw;
        }
        catch (CloudAuthorizationRequiredException ex)
        {
            _logger.LogInformation("The provider requires interactive reauthentication ({Reason}).", ex.ReasonCode);
            var result = SyncResult.Failure("sync.authentication-required", ex.ReasonCode, requiresReauth: true, state: SyncState.AuthenticationRequired);
            SetState(SyncState.AuthenticationRequired, result, trigger);
            return result;
        }
        catch (CloudQuotaExceededException ex)
        {
            var result = SyncResult.Failure("sync.quota-exceeded", ex.Message, state: SyncState.Error);
            SetState(SyncState.Error, result, trigger);
            return result;
        }
        catch (Exception ex)
        {
            // Local data is untouched: the cycle failed and will be retried with backoff.
            _logger.LogWarning(ex, "Synchronization cycle triggered by {Trigger} failed.", trigger);
            var result = SyncResult.Failure("sync.failed", ex.GetType().Name);
            SetState(SyncState.Error, result, trigger);
            return result;
        }
    }

    /// <summary>
    /// Publishes the new state. <paramref name="trigger"/> travels with it so the shell can tell a
    /// cycle the user started by hand from the automatic ones: only the manual cycle opens the
    /// status strip and the notification area; everything else synchronizes in silence.
    /// </summary>
    private void SetState(SyncState state, SyncResult? result = null, SyncTrigger? trigger = null)
    {
        _state = state;
        StateChanged?.Invoke(this, new SyncStateChangedEventArgs
        {
            State = state,
            LastResult = result,
            LastSuccessfulSyncAt = _lastSuccessfulSyncAt,
            Trigger = trigger,
        });
    }

    /// <summary>
    /// Decides whether an automatic cycle may run right now, according to the synchronization
    /// frequency the user chose in Settings (weekly by default, monthly, or "only with the button").
    /// </summary>
    private async Task<bool> IsDueAsync(SyncTrigger trigger, CloudAccount account, CancellationToken cancellationToken)
    {
        // Explicit actions and consistency repairs always run: the user asked for them. The upload
        // before an account transition belongs here too - it belongs to the confirmed sign-in or
        // sign-out, not to the schedule.
        if (trigger is SyncTrigger.Manual or SyncTrigger.PostRestore or SyncTrigger.ConnectivityRestored
            or SyncTrigger.AccountTransition)
        {
            return true;
        }

        var frequency = await ReadSyncFrequencyAsync(cancellationToken).ConfigureAwait(false);

        // The passive download of an open window is not part of the schedule: it exists so that the
        // other device's changes appear within a minute instead of within a week. "Only with the
        // button below" is still respected - the user asked for no automatic cloud traffic at all.
        if (trigger is SyncTrigger.AutoPull)
        {
            return frequency != SyncFrequency.Manual;
        }

        // A local change - a meal, a photograph - is uploaded as soon as it is journaled: the outbox
        // exists to get it out, and holding it back until the weekly (or monthly) schedule is due would
        // leave the cloud stale for days while the user believes the data was synchronized. The
        // schedule still decides how often the app checks the cloud on its own, and "only with the
        // button below" keeps every automatic upload switched off.
        if (trigger is SyncTrigger.LocalChange or SyncTrigger.PostSignIn)
        {
            return frequency != SyncFrequency.Manual;
        }

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
