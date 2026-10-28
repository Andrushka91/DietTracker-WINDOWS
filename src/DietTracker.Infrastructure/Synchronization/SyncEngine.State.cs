using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// State reporting that never contacts the provider. The state of a fresh session starts as
/// <see cref="SyncState.NotConfigured"/>, which is only true when really no account is connected;
/// a signed-in account has to be honoured before the first cycle runs.
/// </summary>
public sealed partial class SyncEngine
{
    /// <summary>Account the current state was derived from (null when nobody is signed in).</summary>
    private Guid? _stateAccountId;

    /// <summary>Upload consent the current state was derived from.</summary>
    private bool _stateSyncEnabled;

    /// <inheritdoc />
    public async Task<SyncState> RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        // A running cycle owns the state until it finishes; overwriting it would make "Syncing"
        // disappear from the screens in the middle of an upload.
        if (_state == SyncState.Syncing)
        {
            return _state;
        }

        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);

        // The derived state depends on the account and on the upload consent; only a change of either
        // may replace a reported problem (an error, an offline cycle, an expired session), because that
        // problem is the truth until something about the configuration changes.
        var accountChanged = account?.Id != _stateAccountId
                             || (account?.SynchronizationEnabled ?? false) != _stateSyncEnabled;
        _stateAccountId = account?.Id;
        _stateSyncEnabled = account?.SynchronizationEnabled ?? false;

        // The account remembers when it last synchronized, so the in-memory field - which starts empty
        // after every restart - is seeded here. Without this a restart would claim "no synchronization
        // yet" until the next cycle finished.
        _lastSuccessfulSyncAt ??= account?.LastSynchronizedAt;

        if (!accountChanged && _state is SyncState.Error or SyncState.Offline or SyncState.AuthenticationRequired)
        {
            return _state;
        }

        SyncState state;
        if (account is null)
        {
            state = SyncState.NotConfigured;
        }
        else if (!account.SynchronizationEnabled)
        {
            // The account is connected, the user simply does not allow uploads: paused, not "no account".
            state = SyncState.Paused;
        }
        else
        {
            var pending = await _operations.CountPendingAsync(account.Id, cancellationToken).ConfigureAwait(false);
            var conflicts = await _conflicts.CountOpenAsync(cancellationToken).ConfigureAwait(false);

            // "Synced" is only claimed for data the provider has already confirmed; an account that has
            // never completed a cycle reports its outstanding work as pending instead.
            state = pending > 0 || conflicts > 0 || account.LastSynchronizedAt is null
                ? SyncState.PendingChanges
                : SyncState.Synced;
        }

        SetState(state);
        return state;
    }
}
