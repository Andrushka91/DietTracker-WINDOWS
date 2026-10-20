using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Authentication;

/// <summary>
/// Application level authentication coordination: interactive sign-in, silent session restore,
/// account bookkeeping, upload consent and local sign-out. Signing out only removes credentials.
/// </summary>
public sealed class AuthenticationCoordinator : IAuthenticationCoordinator
{
    private readonly IAuthenticationProviderFactory _providers;
    private readonly ICloudAccountRepository _accounts;
    private readonly IProfileService _profiles;
    private readonly IAppPreferencesRepository _preferences;
    private readonly IClock _clock;
    private readonly ILogger<AuthenticationCoordinator> _logger;

    public AuthenticationCoordinator(
        IAuthenticationProviderFactory providers,
        ICloudAccountRepository accounts,
        IProfileService profiles,
        IAppPreferencesRepository preferences,
        IClock clock,
        ILogger<AuthenticationCoordinator> logger)
    {
        _providers = providers;
        _accounts = accounts;
        _profiles = profiles;
        _preferences = preferences;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<ProviderType>? ActiveAccountChanged;

    /// <inheritdoc />
    public Task<CloudAccount?> GetActiveAccountAsync(CancellationToken cancellationToken = default)
        => _accounts.GetActiveAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AuthenticationResult> SignInAsync(
        ProviderType providerType,
        bool grantUploadConsent,
        CancellationToken cancellationToken = default)
    {
        var result = await _providers.Get(providerType).SignInInteractiveAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            return result;
        }

        await RegisterAccountAsync(providerType, result, grantUploadConsent, cancellationToken).ConfigureAwait(false);
        ActiveAccountChanged?.Invoke(this, providerType);
        return result;
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult?> TryRestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return null;
        }

        var result = await _providers.Get(account.ProviderType).AcquireSilentAsync(cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            account.LastAuthenticatedAt = _clock.UtcNow;
            await _accounts.UpdateAsync(account, cancellationToken).ConfigureAwait(false);
            ActiveAccountChanged?.Invoke(this, account.ProviderType);
        }
        else
        {
            _logger.LogInformation(
                "The {Provider} session could not be restored silently ({Reason}); interactive sign-in is required.",
                account.ProviderType, result.ReasonCode);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is not null)
        {
            await _providers.Get(account.ProviderType).SignOutAsync(cancellationToken).ConfigureAwait(false);
        }

        // Only the session is removed: local data stays untouched.
        await _accounts.ClearActiveAsync(cancellationToken).ConfigureAwait(false);

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (preferences is not null)
        {
            preferences.SynchronizationEnabled = false;
            preferences.PreferredProvider = ProviderType.None;
            await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
        }

        await _profiles.DetachProviderAsync(cancellationToken).ConfigureAwait(false);
        ActiveAccountChanged?.Invoke(this, ProviderType.None);
    }

    private async Task RegisterAccountAsync(
        ProviderType providerType,
        AuthenticationResult result,
        bool grantUploadConsent,
        CancellationToken cancellationToken)
    {
        var account = await _accounts.FindAsync(providerType, result.ProviderUserId, cancellationToken).ConfigureAwait(false)
                      ?? new CloudAccount { ProviderType = providerType, ProviderUserId = result.ProviderUserId };

        account.DisplayName = result.DisplayName;
        account.Email = result.Email;
        account.IsActive = true;
        account.SynchronizationEnabled = grantUploadConsent;
        account.LastAuthenticatedAt = _clock.UtcNow;

        await _accounts.UpsertActiveAsync(account, cancellationToken).ConfigureAwait(false);
        await _profiles.BindProviderAsync(providerType, result.ProviderUserId, result.DisplayName, result.Email, cancellationToken).ConfigureAwait(false);

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (preferences is not null)
        {
            preferences.PreferredProvider = providerType;
            preferences.SynchronizationEnabled = grantUploadConsent;
            preferences.CloudUploadConsentGranted = grantUploadConsent || preferences.CloudUploadConsentGranted;
            preferences.UpdatedAt = _clock.UtcNow;
            await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
        }
    }
}
