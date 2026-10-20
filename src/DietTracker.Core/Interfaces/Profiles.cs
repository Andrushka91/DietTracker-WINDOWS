using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>
/// Owns the active local profile and its preferences. Guest Mode is a first class profile
/// (<c>ProviderType.None</c>) so that signing in later never requires migrating data.
/// </summary>
public interface IProfileService
{
    /// <summary>Returns the active profile, creating the Guest profile on first launch.</summary>
    Task<UserProfile> GetOrCreateActiveProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the preferences of the active profile, creating defaults when missing.</summary>
    Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default);

    Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default);

    /// <summary>Binds the active profile to a provider identity after a successful sign-in.</summary>
    Task<UserProfile> BindProviderAsync(
        Enums.ProviderType providerType,
        string providerUserId,
        string displayName,
        string email,
        CancellationToken cancellationToken = default);

    /// <summary>Returns to Guest Mode without deleting any data.</summary>
    Task<UserProfile> DetachProviderAsync(CancellationToken cancellationToken = default);
}
