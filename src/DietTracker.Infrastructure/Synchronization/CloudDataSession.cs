using DietTracker.Core.BusinessRules;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Cloud-only data lifecycle: the local SQLite database is a cache of exactly one connected account.
/// </summary>
/// <remarks>
/// <para>
/// The engine stays offline-first <em>inside</em> a session - a meal is written locally, shown
/// immediately and uploaded in the background. This service decides the boundaries of that session:
/// the pending changes are uploaded before an account is replaced, the cache is emptied afterwards and
/// the dataset of the connected account is downloaded at once, so switching accounts shows the other
/// account's data immediately instead of mixing both.
/// </para>
/// <para>
/// Two situations are deliberately excluded from deletion. A cache that has never been uploaded is kept
/// (a first sign-in adopts it instead of throwing it away), and a sign-out is refused while changes are
/// still waiting for the provider: the cloud copy is meant to be the only copy, so nothing is deleted
/// before it exists there. The caller can override the second rule explicitly.
/// </para>
/// </remarks>
public sealed class CloudDataSession : ICloudDataSession
{
    private static readonly DateOnly HistoryStart = new(2000, 1, 1);

    private static readonly DateOnly HistoryEnd = new(2100, 1, 1);

    private readonly ICloudAccountRepository _accounts;
    private readonly ISyncEngine _engine;
    private readonly IPrivacyService _privacy;
    private readonly IProfileService _profiles;
    private readonly IMealEntryRepository _entries;
    private readonly IPhotoRepository _photos;
    private readonly IMealTemplateRepository _templates;
    private readonly ISyncOperationRepository _operations;
    private readonly IReminderScheduler _reminders;
    private readonly IAppDataPaths _paths;
    private readonly ILogger<CloudDataSession> _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public CloudDataSession(
        ICloudAccountRepository accounts,
        ISyncEngine engine,
        IPrivacyService privacy,
        IProfileService profiles,
        IMealEntryRepository entries,
        IPhotoRepository photos,
        IMealTemplateRepository templates,
        ISyncOperationRepository operations,
        IReminderScheduler reminders,
        IAppDataPaths paths,
        ILogger<CloudDataSession> logger)
    {
        _accounts = accounts;
        _engine = engine;
        _privacy = privacy;
        _profiles = profiles;
        _entries = entries;
        _photos = photos;
        _templates = templates;
        _operations = operations;
        _reminders = reminders;
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccountTransitionResult> PrepareForSignInAsync(
        IProgress<AccountTransitionStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (account is not null)
            {
                // The credentials are about to be replaced by the interactive sign-in, so this is the
                // last moment at which the previous account can still receive its pending changes.
                progress?.Report(AccountTransitionStage.Uploading);
                var upload = await UploadPendingAsync(cancellationToken).ConfigureAwait(false);
                var pending = await CountPendingChangesAsync(account, cancellationToken).ConfigureAwait(false);

                if (pending > 0)
                {
                    _logger.LogWarning(
                        "{Pending} change(s) of the connected account could not be uploaded; the sign-in is refused.",
                        pending);

                    progress?.Report(AccountTransitionStage.Completed);

                    return new AccountTransitionResult
                    {
                        Transition = AccountTransition.SignIn,
                        Uploaded = upload?.Uploaded ?? 0,
                        PendingChanges = pending,
                        FailureReason = AccountTransitionResult.PendingChangesReason,
                    };
                }

                progress?.Report(AccountTransitionStage.Completed);

                // Nothing is deleted here on purpose: the cache of the current account must survive a
                // cancelled sign-in. It is emptied once the new account is really connected.
                return new AccountTransitionResult
                {
                    Transition = AccountTransition.SignIn,
                    Uploaded = upload?.Uploaded ?? 0,
                };
            }

            // No account is connected. Records that no cloud account has seen exist only until the first
            // sign-in (or survived a crash): they are adopted (uploaded) by the account that is about to
            // sign in instead of being deleted.
            var usable = await HasLocalUserDataAsync(cancellationToken).ConfigureAwait(false);
            var everConnected = (await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false)).Count > 0;

            if (usable && (!everConnected || await HasUnuploadedChangesAsync(cancellationToken).ConfigureAwait(false)))
            {
                _logger.LogInformation("Local records are adopted by the account that is about to sign in.");
                progress?.Report(AccountTransitionStage.Completed);

                return new AccountTransitionResult
                {
                    Transition = AccountTransition.SignIn,
                    LocalDataAdopted = true,
                };
            }

            progress?.Report(AccountTransitionStage.Completed);
            return new AccountTransitionResult { Transition = AccountTransition.SignIn };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccountTransitionResult> CompleteSignInAsync(
        bool adoptLocalData,
        IProgress<AccountTransitionStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                return new AccountTransitionResult
                {
                    Transition = AccountTransition.SignIn,
                    FailureReason = AccountTransitionResult.NotConnectedReason,
                };
            }

            if (adoptLocalData && await HasLocalUserDataAsync(cancellationToken).ConfigureAwait(false))
            {
                // Every local record is queued and uploaded, so the account starts with the data the
                // user created before signing in. Nothing is deleted on this path.
                progress?.Report(AccountTransitionStage.Uploading);
                var push = await _engine.PushAllAsync(null, cancellationToken).ConfigureAwait(false);
                progress?.Report(AccountTransitionStage.Completed);

                _logger.LogInformation(
                    "The local records were adopted by the connected account ({Uploaded} uploaded).",
                    push.Uploaded);

                return new AccountTransitionResult
                {
                    Transition = AccountTransition.SignIn,
                    LocalDataAdopted = true,
                    CloudDataLoaded = push.Succeeded,
                    Uploaded = push.Uploaded,
                    Downloaded = push.Downloaded,
                };
            }

            // The sign-in succeeded, so the cache of the previous account can go now: records,
            // photographs, journal, cursor and conflicts. What follows is the new account only.
            progress?.Report(AccountTransitionStage.ClearingLocalData);
            var files = await _privacy.PurgeLocalCacheAsync(includePhotographs: true, cancellationToken).ConfigureAwait(false);

            // ... and its dataset is downloaded immediately, so the user never has to press a restore
            // button and never sees records of the account that was connected before.
            progress?.Report(AccountTransitionStage.Downloading);
            var rebuild = await _engine.RebuildFromCloudAsync(null, cancellationToken).ConfigureAwait(false);
            await RefreshRemindersAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(AccountTransitionStage.Completed);

            _logger.LogInformation(
                "Signed in. The previous cache was emptied ({Files} photograph file(s)) and {Downloaded} record(s) of the connected account were applied.",
                files, rebuild.Downloaded);

            return new AccountTransitionResult
            {
                Transition = AccountTransition.SignIn,
                LocalDataCleared = true,
                CloudDataLoaded = rebuild.Succeeded,
                Downloaded = rebuild.Downloaded,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccountTransitionResult> SignOutAsync(
        bool discardPendingChanges = false,
        IProgress<AccountTransitionStage>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            var uploaded = 0;
            var pending = 0;

            if (account is not null)
            {
                // Upload first, delete afterwards: the local cache is only given up once the cloud
                // copy exists. The sign-out button therefore never needs a separate "synchronize now".
                progress?.Report(AccountTransitionStage.Uploading);
                var upload = await UploadPendingAsync(cancellationToken).ConfigureAwait(false);
                uploaded = upload?.Uploaded ?? 0;
                pending = await CountPendingChangesAsync(account, cancellationToken).ConfigureAwait(false);

                if (pending > 0 && !discardPendingChanges)
                {
                    _logger.LogWarning(
                        "Sign-out was refused: {Pending} change(s) are still waiting for the cloud.",
                        pending);

                    progress?.Report(AccountTransitionStage.Completed);

                    return new AccountTransitionResult
                    {
                        Transition = AccountTransition.SignOut,
                        Uploaded = uploaded,
                        PendingChanges = pending,
                        FailureReason = AccountTransitionResult.PendingChangesReason,
                    };
                }

                // Credentials are removed before the records: the tokens must never outlive the session.
                progress?.Report(AccountTransitionStage.ClearingLocalData);
                await _privacy.SignOutAsync(cancellationToken).ConfigureAwait(false);
            }

            var files = await _privacy.PurgeLocalCacheAsync(includePhotographs: true, cancellationToken).ConfigureAwait(false);
            await RefreshRemindersAsync(cancellationToken).ConfigureAwait(false);
            await _engine.RefreshStateAsync(cancellationToken).ConfigureAwait(false);
            progress?.Report(AccountTransitionStage.Completed);

            _logger.LogInformation(
                "Signed out. The local cache was emptied ({Files} photograph file(s); {Pending} change(s) discarded).",
                files, pending);

            return new AccountTransitionResult
            {
                Transition = AccountTransition.SignOut,
                LocalDataCleared = true,
                Uploaded = uploaded,
                PendingChanges = pending,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<AccountTransitionResult?> EnforceOnStartupAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A connected account owns the cache; the session continues where it stopped.
            if (await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                return null;
            }

            // An installation that has never been connected keeps its offline records: they are the only
            // copy that exists. From the first account on, nothing may survive a session.
            if ((await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false)).Count == 0)
            {
                return null;
            }

            if (!await HasLocalUserDataAsync(cancellationToken).ConfigureAwait(false) && !HasPhotoFiles())
            {
                return null;
            }

            // Changes that never reached the cloud cannot be repaired here - no account is connected, so
            // nothing can receive them. They are kept (and adopted by the next sign-in) instead of being
            // thrown away.
            if (await HasUnuploadedChangesAsync(cancellationToken).ConfigureAwait(false))
            {
                _logger.LogWarning(
                    "A cache without a connected account still holds changes that were never uploaded; it is kept for the next sign-in.");
                return null;
            }

            var files = await _privacy.PurgeLocalCacheAsync(includePhotographs: true, cancellationToken).ConfigureAwait(false);
            await RefreshRemindersAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "A cache that survived without a connected account was removed at start-up ({Files} photograph file(s)).",
                files);

            return new AccountTransitionResult
            {
                Transition = AccountTransition.Startup,
                LocalDataCleared = true,
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs a cycle that is never deferred by the configured frequency. A provider failure is logged and
    /// reported through <see langword="null"/>; the caller decides whether it may delete anything.
    /// </summary>
    private async Task<SyncResult?> UploadPendingAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _engine.SynchronizeAsync(SyncTrigger.Manual, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Uploading the pending changes failed; the local cache is kept.");
            return null;
        }
    }

    /// <summary>
    /// Number of changes the cloud does not have yet. Uploads that were switched off count as pending
    /// whenever records exist locally, because those records have never reached the cloud.
    /// </summary>
    private async Task<int> CountPendingChangesAsync(CloudAccount account, CancellationToken cancellationToken)
    {
        var pending = await _operations.CountPendingAsync(account.Id, cancellationToken).ConfigureAwait(false);

        if (!account.SynchronizationEnabled && await HasLocalUserDataAsync(cancellationToken).ConfigureAwait(false))
        {
            return Math.Max(pending, 1);
        }

        return pending;
    }

    /// <summary>
    /// True when the journal of any remembered account still holds a change. Those changes exist only
    /// on this device, so they must survive until an account can upload them.
    /// </summary>
    private async Task<bool> HasUnuploadedChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var account in await _accounts.GetAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (await _operations.CountPendingAsync(account.Id, cancellationToken).ConfigureAwait(false) > 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when the local database holds anything the user created or confirmed.</summary>
    private async Task<bool> HasLocalUserDataAsync(CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);

        var range = await _entries.GetForRangeAsync(profile.Id, HistoryStart, HistoryEnd, cancellationToken).ConfigureAwait(false);
        if (range.Values.SelectMany(entries => entries).Any(MealContentRules.HasUserContent))
        {
            return true;
        }

        if ((await _templates.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false)).Count > 0)
        {
            return true;
        }

        // Photograph rows that belong to no meal still mean that user data is present.
        return (await _photos.GetAllAsync(cancellationToken).ConfigureAwait(false)).Count > 0;
    }

    /// <summary>True when photograph files exist even though the metadata rows are already gone.</summary>
    private bool HasPhotoFiles()
    {
        try
        {
            return Directory.Exists(_paths.PhotoDirectory)
                   && Directory.EnumerateFiles(_paths.PhotoDirectory, "*", SearchOption.AllDirectories).Any();
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Drops the schedules of the records that were just removed, so no notification of a signed-out
    /// account can reach the user.
    /// </summary>
    private async Task RefreshRemindersAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _reminders.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The reminder schedule could not be refreshed after the cache changed.");
        }
    }
}
