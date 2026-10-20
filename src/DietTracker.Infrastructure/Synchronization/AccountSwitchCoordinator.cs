using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Guards provider and account switching.
/// </summary>
/// <remarks>
/// A switch is never performed implicitly: the user is shown which account currently holds the
/// synchronized data, how many local records exist and which strategy will be applied. Accounts are
/// identified by provider + provider user id, never by e-mail address, so a Microsoft and a Google
/// account are never treated as the same identity. A safety copy is written before anything is uploaded.
/// </remarks>
public sealed class AccountSwitchCoordinator : IAccountSwitchCoordinator
{
    private readonly IAuthenticationCoordinator _authentication;
    private readonly ICloudAccountRepository _accounts;
    private readonly IMealEntryRepository _entries;
    private readonly IProfileService _profiles;
    private readonly ISyncEngine _engine;
    private readonly ISyncJournal _journal;
    private readonly IBackupService _backup;
    private readonly ILogger<AccountSwitchCoordinator> _logger;

    public AccountSwitchCoordinator(
        IAuthenticationCoordinator authentication,
        ICloudAccountRepository accounts,
        IMealEntryRepository entries,
        IProfileService profiles,
        ISyncEngine engine,
        ISyncJournal journal,
        IBackupService backup,
        ILogger<AccountSwitchCoordinator> logger)
    {
        _authentication = authentication;
        _accounts = accounts;
        _entries = entries;
        _profiles = profiles;
        _engine = engine;
        _journal = journal;
        _backup = backup;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccountSwitchPreview> PreviewAsync(ProviderType targetProvider, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var current = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);

        return new AccountSwitchPreview
        {
            CurrentProvider = current?.ProviderType ?? ProviderType.None,
            CurrentAccountEmail = current?.Email ?? string.Empty,
            TargetProvider = targetProvider,
            LocalRecords = await _entries.CountAsync(profile.Id, cancellationToken).ConfigureAwait(false),
            PendingChanges = await _journal.CountPendingAsync(cancellationToken).ConfigureAwait(false),
        };
    }

    /// <inheritdoc />
    public async Task<AccountSwitchResult> ExecuteAsync(
        ProviderType targetProvider,
        AccountSwitchStrategy strategy,
        IProgress<RestoreProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (strategy == AccountSwitchStrategy.Cancel)
        {
            return new AccountSwitchResult { Succeeded = false, Strategy = strategy, ReasonCode = "switch.cancelled" };
        }

        // Safety net: a complete local backup is written before any operation that can upload data.
        var safetyCopy = await _backup.CreateSafetyCopyAsync($"switch-{targetProvider}", cancellationToken).ConfigureAwait(false);
        if (safetyCopy is not null)
        {
            _logger.LogInformation("A safety copy was created at '{Path}' before switching providers.", safetyCopy);
        }

        var signIn = await _authentication
            .SignInAsync(targetProvider, grantUploadConsent: true, cancellationToken)
            .ConfigureAwait(false);

        if (!signIn.Succeeded)
        {
            return new AccountSwitchResult
            {
                Succeeded = false,
                Strategy = strategy,
                ReasonCode = signIn.IsCancelled ? "switch.cancelled" : signIn.ReasonCode ?? "switch.signin-failed",
                Diagnostic = signIn.Diagnostic,
            };
        }

        switch (strategy)
        {
            case AccountSwitchStrategy.UploadLocalToNewAccount:
            {
                var push = await _engine.PushAllAsync(progress, cancellationToken).ConfigureAwait(false);
                return new AccountSwitchResult
                {
                    Succeeded = push.Succeeded,
                    Strategy = strategy,
                    RecordsUploaded = push.Uploaded,
                    Conflicts = push.Conflicts,
                    ReasonCode = push.ReasonCode,
                };
            }

            case AccountSwitchStrategy.RestoreFromNewAccount:
            {
                var restore = await _engine.RestoreAsync(progress, cancellationToken).ConfigureAwait(false);
                return new AccountSwitchResult
                {
                    Succeeded = restore.Succeeded,
                    Strategy = strategy,
                    RecordsDownloaded = restore.Downloaded,
                    Conflicts = restore.Conflicts,
                    ReasonCode = restore.ReasonCode,
                };
            }

            default:
            {
                // Merge: the remote dataset is pulled first, then the local records are uploaded. Any
                // record that changed on both sides is preserved as a conflict for the user to settle.
                var restore = await _engine.RestoreAsync(progress, cancellationToken).ConfigureAwait(false);
                var push = await _engine.PushAllAsync(progress, cancellationToken).ConfigureAwait(false);

                return new AccountSwitchResult
                {
                    Succeeded = restore.Succeeded && push.Succeeded,
                    Strategy = strategy,
                    RecordsUploaded = push.Uploaded,
                    RecordsDownloaded = restore.Downloaded,
                    Conflicts = Math.Max(restore.Conflicts, push.Conflicts),
                    ReasonCode = restore.ReasonCode ?? push.ReasonCode,
                };
            }
        }
    }
}
