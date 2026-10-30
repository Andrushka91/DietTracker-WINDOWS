using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Services;

/// <summary>
/// Owns the active local profile and its preferences. The first launch creates a Guest profile
/// (<see cref="ProviderType.None"/>), so signing in later never requires migrating any meal data.
/// </summary>
public sealed class ProfileService : IProfileService
{
    private readonly IProfileRepository _profiles;
    private readonly IAppPreferencesRepository _preferences;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ProfileService(IProfileRepository profiles, IAppPreferencesRepository preferences, IClock clock)
    {
        _profiles = profiles;
        _preferences = preferences;
        _clock = clock;
    }

    public async Task<UserProfile> GetOrCreateActiveProfileAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var active = await _profiles.GetActiveAsync(cancellationToken).ConfigureAwait(false);
            if (active is not null)
            {
                return active;
            }

            // Reuse an existing Guest profile if one is already stored but not marked active.
            var existing = (await _profiles.GetAllAsync(cancellationToken).ConfigureAwait(false))
                .FirstOrDefault(p => p.ProviderType == ProviderType.None);

            var profile = existing ?? new UserProfile
            {
                ProviderType = ProviderType.None,
                DisplayName = string.Empty,
                TimeZone = string.Empty,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
                SyncStatus = SyncStatus.Synced,
            };

            await _profiles.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
            await _profiles.SetActiveAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            await GetPreferencesAsync(cancellationToken).ConfigureAwait(false);
            return profile;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<AppPreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetActiveAsync(cancellationToken).ConfigureAwait(false)
                      ?? throw new InvalidOperationException("No active profile exists.");

        var stored = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (stored is not null)
        {
            return stored;
        }

        var defaults = new AppPreferences
        {
            LocalProfileId = profile.Id,
            TimeZone = string.Empty,
            UpdatedAt = _clock.UtcNow,
            SyncStatus = SyncStatus.Synced,
        };

        await _preferences.UpsertAsync(defaults, cancellationToken).ConfigureAwait(false);
        return defaults;
    }

    public async Task SavePreferencesAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        preferences.UpdatedAt = _clock.UtcNow;
        preferences.SyncStatus = preferences.SynchronizationEnabled ? SyncStatus.PendingUpdate : SyncStatus.Synced;
        await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
    }

    public async Task<UserProfile> BindProviderAsync(
        ProviderType providerType,
        string providerUserId,
        string displayName,
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);

        var profile = await GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        profile.ProviderType = providerType;
        profile.ProviderUserId = providerUserId;
        profile.DisplayName = displayName ?? string.Empty;
        profile.Email = email ?? string.Empty;
        profile.UpdatedAt = _clock.UtcNow;
        profile.SyncStatus = SyncStatus.PendingUpdate;

        await _profiles.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        await _profiles.SetActiveAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return profile;
    }

    public async Task<UserProfile> DetachProviderAsync(CancellationToken cancellationToken = default)
    {
        var profile = await GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        profile.ProviderType = ProviderType.None;
        profile.ProviderUserId = string.Empty;
        profile.Email = string.Empty;
        profile.UpdatedAt = _clock.UtcNow;

        await _profiles.UpsertAsync(profile, cancellationToken).ConfigureAwait(false);
        return profile;
    }
}
