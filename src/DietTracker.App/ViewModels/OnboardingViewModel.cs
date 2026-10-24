using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.App.Services;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// First-launch experience: sign in with Google, or continue without an account. Every essential
/// feature works in Guest Mode; signing in only adds secure cloud synchronization and recovery.
/// </summary>
public sealed partial class OnboardingViewModel : ObservableObject
{
    private readonly IAuthenticationCoordinator _authentication;
    private readonly IProfileService _profiles;
    private readonly ILocalizationService _localization;
    private readonly ISyncEngine _syncEngine;

    public OnboardingViewModel(
        IAuthenticationCoordinator authentication,
        IProfileService profiles,
        ILocalizationService localization,
        ISyncEngine syncEngine)
    {
        _authentication = authentication;
        _profiles = profiles;
        _localization = localization;
        _syncEngine = syncEngine;
    }

    [ObservableProperty]
    private bool _consentGranted;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public string Title => _localization.Get("Onboarding_Title");

    public string Description => _localization.Get("Onboarding_Description");

    public string ConsentText => _localization.Get("Onboarding_ConsentText");

    public string GuestDescription => _localization.Get("Onboarding_GuestDescription");

    /// <summary>Raised when onboarding is finished so the shell can show the dashboard.</summary>
    public event EventHandler? Completed;

    [RelayCommand]
    private async Task ContinueAsGuestAsync() => await CompleteAsync(null);

    [RelayCommand]
    private async Task SignInGoogleAsync() => await CompleteAsync(ProviderType.Google);

    private async Task CompleteAsync(ProviderType? providerType)
    {
        IsBusy = true;
        try
        {
            var preferences = await _profiles.GetPreferencesAsync();

            if (providerType is null)
            {
                preferences.SynchronizationEnabled = false;
                await _profiles.SavePreferencesAsync(preferences);
                Completed?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!ConsentGranted)
            {
                StatusMessage = _localization.Get("Account_ConsentRequired");
                return;
            }

            preferences.CloudUploadConsentGranted = true;
            preferences.SynchronizationEnabled = true;
            await _profiles.SavePreferencesAsync(preferences);

            var result = await _authentication.SignInAsync(providerType.Value, grantUploadConsent: true);
            if (!result.Succeeded)
            {
                StatusMessage = result.IsCancelled
                    ? _localization.Get("Auth_Cancelled")
                    : _localization.Get(LocalizationKeys.ForReason(result.ReasonCode));

                // The user can always continue offline; nothing is blocked by a failed sign-in.
                Completed?.Invoke(this, EventArgs.Empty);
                return;
            }

            _syncEngine.RequestSync(SyncTrigger.PostSignIn);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
