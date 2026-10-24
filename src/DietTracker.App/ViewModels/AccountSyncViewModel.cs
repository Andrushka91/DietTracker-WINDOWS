using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.App.Services;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using Microsoft.UI.Xaml;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Account and synchronization screen: the connected Google account, the current synchronization
/// state, manual sync and restore.
/// </summary>
public sealed partial class AccountSyncViewModel : ObservableObject
{
    private readonly IAuthenticationCoordinator _authentication;
    private readonly IPrivacyService _privacy;
    private readonly ISyncEngine _syncEngine;
    private readonly IProfileService _profiles;
    private readonly IDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly IAppEnvironment _environment;

    public AccountSyncViewModel(
        IAuthenticationCoordinator authentication,
        IPrivacyService privacy,
        ISyncEngine syncEngine,
        IProfileService profiles,
        IDialogService dialogs,
        ILocalizationService localization,
        IAppEnvironment environment)
    {
        _authentication = authentication;
        _privacy = privacy;
        _syncEngine = syncEngine;
        _profiles = profiles;
        _dialogs = dialogs;
        _localization = localization;
        _environment = environment;
    }

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _providerText = string.Empty;

    [ObservableProperty]
    private string _accountText = string.Empty;

    [ObservableProperty]
    private string _stateText = string.Empty;

    [ObservableProperty]
    private string _lastSyncText = string.Empty;

    [ObservableProperty]
    private string _quotaText = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _consentGranted;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInVisibility))]
    [NotifyPropertyChangedFor(nameof(SignedInVisibility))]
    private bool _isGuest = true;

    /// <summary>Sign-in controls are visible in Guest Mode only.</summary>
    public Visibility SignInVisibility => IsGuest ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Signed-in controls (sign out, synchronization) hide in Guest Mode.</summary>
    public Visibility SignedInVisibility => IsGuest ? Visibility.Collapsed : Visibility.Visible;

    public string ConsentText => _localization.Get("Account_ConsentText");

    public string PrivacySummary => _localization.Get("Account_PrivacySummary");

    public string EnvironmentText => $"{_environment.ApplicationVersion} - {_environment.PackageFamilyName}";

    /// <summary>Loads the account state.</summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        _syncEngine.StateChanged += (_, _) => _ = RefreshStateAsync();
        await RefreshStateAsync();
    }

    [RelayCommand]
    private async Task RefreshStateAsync()
    {
        var account = await _authentication.GetActiveAccountAsync();
        var preferences = await _profiles.GetPreferencesAsync();

        IsGuest = account is null;
        ProviderText = account is null
            ? _localization.Get("Account_ProviderNone")
            : _localization.Get($"Account_Provider{account.ProviderType}");

        AccountText = account is null
            ? _localization.Get("Account_GuestDescription")
            : $"{account.DisplayName} <{account.Email}>";

        StateText = _localization.Get($"Sync_{_syncEngine.State}");
        LastSyncText = _syncEngine.LastSuccessfulSyncAt is { } timestamp
            ? _localization.Format("Sync_LastSuccess", timestamp.ToLocalTime().ToString("g"))
            : _localization.Get("Sync_Never");

        QuotaText = account?.QuotaRemainingBytes is { } remaining
            ? _localization.Format("Account_Quota", remaining / 1024d / 1024d / 1024d)
            : string.Empty;

        ConsentGranted = preferences.CloudUploadConsentGranted;
    }

    [RelayCommand]
    private async Task SignInGoogleAsync() => await SignInAsync(ProviderType.Google);

    private async Task SignInAsync(ProviderType providerType)
    {
        if (!ConsentGranted)
        {
            // Explicit consent is required before any personal data is uploaded.
            StatusMessage = _localization.Get("Account_ConsentRequired");
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _authentication.SignInAsync(providerType, grantUploadConsent: true);
            StatusMessage = result.Succeeded
                ? _localization.Get("Account_SignedIn")
                : result.IsCancelled
                    ? _localization.Get("Auth_Cancelled")
                    : _localization.Get(LocalizationKeys.ForReason(result.ReasonCode));

            if (result.Succeeded)
            {
                _syncEngine.RequestSync(SyncTrigger.PostSignIn);
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshStateAsync();
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Account_SignOutTitle", "Account_SignOutMessage");
        if (!confirmed)
        {
            return;
        }

        // Signing out never deletes meals or photographs.
        await _privacy.SignOutAsync();
        StatusMessage = _localization.Get("Account_SignedOut");
        await RefreshStateAsync();
    }
}
