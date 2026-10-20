using CommunityToolkit.Mvvm.ComponentModel;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Shell level state: the synchronization badge shown in the navigation and the "first launch"
/// decision.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IProfileService _profiles;
    private readonly ISyncEngine _syncEngine;
    private readonly ILocalizationService _localization;

    public ShellViewModel(IProfileService profiles, ISyncEngine syncEngine, ILocalizationService localization)
    {
        _profiles = profiles;
        _syncEngine = syncEngine;
        _localization = localization;

        _syncEngine.StateChanged += (_, _) =>
        {
            SyncState = _syncEngine.State;
            SyncBadge = _localization.Get($"Sync_{_syncEngine.State}");
        };
    }

    [ObservableProperty]
    private SyncState _syncState = SyncState.NotConfigured;

    [ObservableProperty]
    private string _syncBadge = string.Empty;

    public string ApplicationName => _localization.Get("App_DisplayName");

    /// <summary>True when the first-launch welcome screen has not been completed yet.</summary>
    public async Task<bool> IsFirstLaunchAsync()
    {
        var preferences = await _profiles.GetPreferencesAsync();
        return !preferences.OnboardingCompleted;
    }

    /// <summary>Marks the welcome screen as completed.</summary>
    public async Task CompleteOnboardingAsync()
    {
        var preferences = await _profiles.GetPreferencesAsync();
        preferences.OnboardingCompleted = true;
        await _profiles.SavePreferencesAsync(preferences);
    }
}
