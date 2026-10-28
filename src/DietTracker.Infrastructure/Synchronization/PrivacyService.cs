using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Explicit, user driven data lifecycle operations.
/// </summary>
/// <remarks>
/// Signing out, disabling synchronization, deleting local data and deleting cloud data are four
/// distinct operations and are deliberately implemented as four distinct methods. Nothing here is ever
/// triggered implicitly.
/// </remarks>
public sealed partial class PrivacyService : IPrivacyService
{
    private readonly IAuthenticationCoordinator _authentication;
    private readonly ICloudAccountRepository _accounts;
    private readonly IProfileService _profiles;
    private readonly IPhotoStore _photoStore;
    private readonly ICloudStorageProviderFactory _providers;
    private readonly IAppDataPaths _paths;
    private readonly IAppPreferencesRepository _preferences;
    private readonly ISqliteConnectionFactory _connectionFactory;
    private readonly ILogger<PrivacyService> _logger;

    public PrivacyService(
        IAuthenticationCoordinator authentication,
        ICloudAccountRepository accounts,
        IProfileService profiles,
        IPhotoStore photoStore,
        ICloudStorageProviderFactory providers,
        IAppDataPaths paths,
        IAppPreferencesRepository preferences,
        ISqliteConnectionFactory connectionFactory,
        ILogger<PrivacyService> logger)
    {
        _authentication = authentication;
        _accounts = accounts;
        _profiles = profiles;
        _photoStore = photoStore;
        _providers = providers;
        _paths = paths;
        _preferences = preferences;
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SignOutAsync(CancellationToken cancellationToken = default)
        => _authentication.SignOutAsync(cancellationToken);

    /// <inheritdoc />
    public async Task DisableSynchronizationAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            account.SynchronizationEnabled = false;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (preferences is not null)
        {
            preferences.SynchronizationEnabled = false;
            await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Synchronization was disabled. Previously uploaded cloud files are not removed.");
    }

    /// <inheritdoc />
    public async Task SetUploadConsentAsync(bool granted, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            account.SynchronizationEnabled = granted;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (preferences is not null)
        {
            preferences.SynchronizationEnabled = granted;

            // The consent itself is remembered even when it is withdrawn again: it records that the
            // user has been asked, not that uploads are currently allowed.
            preferences.CloudUploadConsentGranted = preferences.CloudUploadConsentGranted || granted;
            preferences.UpdatedAt = DateTimeOffset.UtcNow;
            await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Cloud uploads were {State}. Nothing is deleted either way.", granted ? "allowed" : "refused");
    }

    /// <inheritdoc />
    public async Task DisconnectAccountAsync(Guid cloudAccountId, CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetAsync(cloudAccountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return;
        }

        // Forgetting the authorization removes the tokens but never touches cloud files.
        await _authentication.SignOutAsync(cancellationToken).ConfigureAwait(false);
        await _accounts.DeleteAsync(cloudAccountId, cancellationToken).ConfigureAwait(false);
    }
}
