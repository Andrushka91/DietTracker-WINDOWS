using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.App.Services;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.UI.Xaml;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Settings screen: the connected account, synchronization and restore, reminders, Windows integration
/// and the data lifecycle (export, restore, integrity check).
/// </summary>
/// <remarks>
/// There is no Save button: every switch and every selection is written as soon as it changes, so the
/// page can never show a state the application does not use. The upload consent is not a switch
/// either - signing in stores the meals in the account automatically.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAuthenticationCoordinator _authentication;
    private readonly IProfileService _profiles;
    private readonly IStartupTaskService _startupTask;
    private readonly IBackupService _backup;
    private readonly IPrivacyService _privacy;
    private readonly IMealReportService _reports;
    private readonly ICloudDataSession _cloudData;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly IBusyOverlayService _busy;
    private readonly ILocalizationService _localization;
    private readonly IReminderScheduler _reminders;
    private readonly ISyncEngine _syncEngine;
    private readonly IPhotoStore _photoStore;
    private readonly IDataIntegrityChecker _integrity;
    private readonly IAppEnvironment _environment;
    private readonly IAppDataPaths _paths;

    /// <summary>Serializes the automatic saves so a quick series of toggles always ends on the last one.</summary>
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    private AppPreferences? _preferences;

    /// <summary>True while the cards are filled from the database, so loading never saves back.</summary>
    private bool _applyingPreferences;

    /// <summary>True while this view model listens to the engine (removed when the page is left).</summary>
    private bool _stateSubscribed;

    public SettingsViewModel(
        IAuthenticationCoordinator authentication,
        IProfileService profiles,
        IStartupTaskService startupTask,
        IBackupService backup,
        IPrivacyService privacy,
        IMealReportService reports,
        ICloudDataSession cloudData,
        IFilePickerService filePicker,
        IDialogService dialogs,
        IBusyOverlayService busy,
        ILocalizationService localization,
        IReminderScheduler reminders,
        ISyncEngine syncEngine,
        IPhotoStore photoStore,
        IDataIntegrityChecker integrity,
        IAppEnvironment environment,
        IAppDataPaths paths)
    {
        _authentication = authentication;
        _profiles = profiles;
        _startupTask = startupTask;
        _backup = backup;
        _privacy = privacy;
        _reports = reports;
        _cloudData = cloudData;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _busy = busy;
        _localization = localization;
        _reminders = reminders;
        _syncEngine = syncEngine;
        _photoStore = photoStore;
        _integrity = integrity;
        _environment = environment;
        _paths = paths;
    }

    /// <summary>
    /// Turns the stage of a cloud-only transition into the sentence shown under the busy spinner.
    /// Signing out and signing in traverse the same stages but mean different things to the user.
    /// </summary>
    private void ReportStage(AccountTransitionStage stage, bool signOut)
    {
        var key = (stage, signOut) switch
        {
            (AccountTransitionStage.Uploading, true) => "Busy_Synchronizing",
            (AccountTransitionStage.Uploading, false) => "Busy_UploadingChanges",
            (AccountTransitionStage.ClearingLocalData, true) => "Busy_SigningOut",
            (AccountTransitionStage.ClearingLocalData, false) => "Busy_LoadingAccount",
            (AccountTransitionStage.Downloading, _) => "Busy_Downloading",
            _ => null,
        };

        if (key is not null)
        {
            _busy.Update(_localization.Get(key));
        }
    }

    /// <summary>
    /// Automatic synchronization schedules offered in Settings, with localized labels. The tags are
    /// the enum names of <see cref="SyncFrequency"/>, so the selection round-trips through the
    /// preference of the same name.
    /// </summary>
    public IReadOnlyList<LanguageOption> SyncFrequencyOptions =>
    [
        new(SyncFrequency.Weekly.ToString(), _localization.Get("Sync_FrequencyWeekly")),
        new(SyncFrequency.Monthly.ToString(), _localization.Get("Sync_FrequencyMonthly")),
        new(SyncFrequency.Manual.ToString(), _localization.Get("Sync_FrequencyManual")),
    ];

    [ObservableProperty]
    private string _selectedSyncFrequency = SyncFrequency.Weekly.ToString();

    /// <summary>E-mail of the connected Google account, or the "not signed in" text.</summary>
    [ObservableProperty]
    private string _accountEmailText = string.Empty;

    /// <summary>Provider name under the e-mail (for example "Google Drive").</summary>
    [ObservableProperty]
    private string _accountDetailText = string.Empty;

    /// <summary>
    /// Profile picture of the connected account. Empty in Guest Mode, when the account has no photo or
    /// when the provider could not be asked - the framed glyph of the tile then stays as it is.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccountPhotoVisibility))]
    [NotifyPropertyChangedFor(nameof(AccountPlaceholderVisibility))]
    private string _accountPhotoUrl = string.Empty;

    /// <summary>What the last sign-in or sign-out reported (shown in the Account card).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusMessage))]
    private string _accountStatusMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignInVisibility))]
    [NotifyPropertyChangedFor(nameof(SignedInVisibility))]
    [NotifyPropertyChangedFor(nameof(IsGuest))]
    private bool _isSignedIn;

    /// <summary>Cloud space left of the connected account (empty while no account is connected).</summary>
    [ObservableProperty]
    private string _quotaText = string.Empty;

    [ObservableProperty]
    private string _syncStateText = string.Empty;

    /// <summary>What the last manual synchronization or restore reported.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusMessage))]
    private string _syncStatusMessage = string.Empty;

    /// <summary>True while a manual synchronization, a preview or a restore is running.</summary>
    [ObservableProperty]
    private bool _isSyncBusy;

    /// <summary>Guest mode: no account is connected, so the InfoBar above the cards explains it.</summary>
    public bool IsGuest => !IsSignedIn;

    /// <summary>What the application keeps on the device while no account is connected.</summary>
    public string PrivacySummary => _localization.Get("Account_PrivacySummary");

    /// <summary>
    /// What exactly leaves the device. Shown without a switch: an account always stores the meals, so
    /// the text describes the behaviour instead of asking for it.
    /// </summary>
    public string ConsentText => _localization.Get("Account_ConsentText");

    /// <summary>The line that explains the automatic upload once the account is connected.</summary>
    public string CloudSaveText => _localization.Get("Account_CloudSaveHint");

    /// <summary>The sign-in button is only offered while no account is connected.</summary>
    public Visibility SignInVisibility => IsSignedIn ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Signing out only makes sense while an account is connected.</summary>
    public Visibility SignedInVisibility => IsSignedIn ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>The photo of the account replaces the glyph of the tile as soon as there is one.</summary>
    public Visibility AccountPhotoVisibility => string.IsNullOrEmpty(AccountPhotoUrl) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>The framed person glyph is the fallback while there is no photo to show.</summary>
    public Visibility AccountPlaceholderVisibility => string.IsNullOrEmpty(AccountPhotoUrl) ? Visibility.Visible : Visibility.Collapsed;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _use12HourClock;

    [ObservableProperty]
    private bool _showSnackCards = true;

    /// <summary>Confirmation of the last automatic save of a switch (shown in the General card).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusMessage))]
    private string _statusMessage = string.Empty;

    /// <summary>Result of the last export, restore or integrity check (shown in the data card).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveStatusMessage))]
    private string _backupStatusMessage = string.Empty;

    /// <summary>
    /// The one message the page currently reports: the most specific of the four status texts that
    /// is set. It is bound to the floating confirmation of the settings page instead of to a line
    /// inside a card - the cards have no spare height (the page fills the window exactly), so a line
    /// that arrived inside one would push every row below it down and put the page into a scrollbar.
    /// Floating above the layout, the same message costs no height at all.
    /// </summary>
    public string ActiveStatusMessage =>
        !string.IsNullOrEmpty(BackupStatusMessage) ? BackupStatusMessage :
        !string.IsNullOrEmpty(SyncStatusMessage) ? SyncStatusMessage :
        !string.IsNullOrEmpty(AccountStatusMessage) ? AccountStatusMessage :
        StatusMessage;

    [ObservableProperty]
    private string _storageText = string.Empty;

    [ObservableProperty]
    private string _aboutText = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _lastSyncText = string.Empty;

    /// <summary>Loads the connected account and the current preferences.</summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        _preferences = await _profiles.GetPreferencesAsync();

        // English is the only interface language, so a tag written by an older build is normalized
        // once here instead of being offered as a choice that could never take effect.
        if (!string.Equals(_preferences.Language, App.SupportedLanguage, StringComparison.OrdinalIgnoreCase))
        {
            _preferences.Language = App.SupportedLanguage;
            await _profiles.SavePreferencesAsync(_preferences);
        }

        // The switches save themselves when they change, so filling them from the database is done
        // behind the guard: otherwise opening the page would immediately write every value back.
        _applyingPreferences = true;
        try
        {
            SelectedSyncFrequency = _preferences.SyncFrequency.ToString();
            MinimizeToTray = _preferences.MinimizeToTray;
            Use12HourClock = _preferences.Use12HourClock;
            ShowSnackCards = _preferences.ShowSnackCards;
            StartWithWindows = await _startupTask.IsEnabledAsync();
        }
        finally
        {
            _applyingPreferences = false;
        }

        AccountStatusMessage = string.Empty;
        SyncStatusMessage = string.Empty;
        StatusMessage = string.Empty;
        BackupStatusMessage = string.Empty;

        await RestoreAutomaticUploadAsync();

        // The engine outlives the page: listening here keeps the state texts live while the page is
        // open, and Detach() removes the subscription as soon as the page is left.
        if (!_stateSubscribed)
        {
            _syncEngine.StateChanged += OnSyncStateChanged;
            _stateSubscribed = true;
        }

        await LoadAccountAsync();
        await RefreshSyncTextAsync();

        var usage = await _photoStore.GetStorageUsageAsync();
        StorageText = _localization.Format("Settings_StorageUsage", usage / 1024d / 1024d);
        DatabasePath = _paths.DatabaseFilePath;
        AboutText = _localization.Format("Settings_AboutText", _environment.ApplicationVersion, _environment.OperatingSystemDescription);
    }

    /// <summary>
    /// A signed-in account always stores the meals, because there is no consent switch any more. An
    /// account that an older build left with uploads switched off is switched back on here and the
    /// changes that are still waiting are pushed, so the page and the application tell the same story.
    /// </summary>
    private async Task RestoreAutomaticUploadAsync()
    {
        var account = await _authentication.GetActiveAccountAsync();
        if (account is null || account.SynchronizationEnabled)
        {
            return;
        }

        await _privacy.SetUploadConsentAsync(true);
        _syncEngine.RequestSync(SyncTrigger.LocalChange);
    }

    /// <summary>
    /// Reads the connected account. The engine is asked for the state it derives from that account, so
    /// the page shows "no account connected" only when there really is none.
    /// </summary>
    private async Task LoadAccountAsync()
    {
        var account = await _authentication.GetActiveAccountAsync();

        IsSignedIn = account is not null;
        AccountEmailText = account is null
            ? _localization.Get("Account_NoEmail")
            : account.Email;
        AccountDetailText = account is null
            ? _localization.Get("Account_GuestDescription")
            : _localization.Get($"Account_Provider{account.ProviderType}");

        // The remaining cloud space of the account (formerly shown on the account page).
        QuotaText = account?.QuotaRemainingBytes is { } remaining
            ? _localization.Format("Account_Quota", remaining / 1024d / 1024d / 1024d)
            : string.Empty;

        // The photo of the account for the tile. Display only: no photo, no session or a failing
        // provider must never keep the page from loading, so every failure leaves the placeholder.
        try
        {
            AccountPhotoUrl = account is null
                ? string.Empty
                : await _authentication.GetActiveAccountPhotoAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccountPhotoUrl = string.Empty;
        }
    }

    /// <summary>
    /// Asks the engine what it knows and paints the result. Only called for explicit moments (the page
    /// was opened, a cycle finished because the user pressed the button), never from the state event -
    /// see <see cref="OnSyncStateChanged"/>.
    /// </summary>
    private async Task RefreshSyncTextAsync()
    {
        await _syncEngine.RefreshStateAsync();
        ApplySyncText();
    }

    /// <summary>
    /// Paints the state the engine already reports, without asking it to refresh.
    /// </summary>
    private void ApplySyncText()
    {
        SyncStateText = _localization.Get($"Sync_{_syncEngine.State}");
        LastSyncText = _syncEngine.LastSuccessfulSyncAt is { } timestamp
            ? _localization.Format("Sync_LastSuccess", timestamp.ToLocalTime().ToString("g"))
            : _localization.Get("Sync_Never");
    }

    /// <summary>
    /// The engine raises the event from a background thread, so the update is marshalled to the UI
    /// thread before any bound property is written.
    /// </summary>
    /// <remarks>
    /// This handler must stay read-only. <c>SyncEngine.RefreshStateAsync</c> raises
    /// <c>StateChanged</c> on every call, even when the state did not change, so asking the engine
    /// for a refresh from here would re-enter the event forever and freeze the interface (the
    /// dispatcher queue would never become empty). The bound texts are read from the engine as-is.
    /// </remarks>
    private void OnSyncStateChanged(object? sender, SyncStateChangedEventArgs e)
    {
        var dispatcher = App.MainAppWindow?.DispatcherQueue;
        if (dispatcher is null)
        {
            return;
        }

        dispatcher.TryEnqueue(() =>
        {
            ApplySyncText();

            // The remaining cloud space and the consent follow the cycle without touching the engine.
            _ = LoadAccountAsync();
        });
    }

    /// <summary>Stops listening to the engine; called when the page is left.</summary>
    public void Detach()
    {
        if (_stateSubscribed)
        {
            _syncEngine.StateChanged -= OnSyncStateChanged;
            _stateSubscribed = false;
        }
    }

    /// <summary>
    /// Every switch of the General card is written as soon as it changes. A setting that only looks
    /// applied until a button is pressed is a setting the user cannot trust, and there is no Save
    /// button on the page any more, so the change is what saves it.
    /// </summary>
    partial void OnStartWithWindowsChanged(bool value) => QueuePreferencesSave();

    partial void OnMinimizeToTrayChanged(bool value) => QueuePreferencesSave();

    partial void OnUse12HourClockChanged(bool value) => QueuePreferencesSave();

    partial void OnShowSnackCardsChanged(bool value) => QueuePreferencesSave();

    partial void OnSelectedSyncFrequencyChanged(string value) => QueuePreferencesSave();

    /// <summary>
    /// Starts a save without blocking the toggle: the switch must move at once, the write is done in
    /// the background and serialized by <see cref="_saveGate"/> so the last change always wins.
    /// </summary>
    private void QueuePreferencesSave()
    {
        if (_applyingPreferences || _preferences is null)
        {
            return;
        }

        _ = SavePreferencesAsync();
    }

    /// <summary>Writes the current state of the cards; called automatically, never by a button.</summary>
    private async Task SavePreferencesAsync()
    {
        if (_preferences is null)
        {
            return;
        }

        await _saveGate.WaitAsync();
        try
        {
            var preferences = _preferences;

            // English is the only interface language; the stored tag is kept at that value.
            preferences.Language = App.SupportedLanguage;
            preferences.SyncFrequency = Enum.TryParse<SyncFrequency>(SelectedSyncFrequency, out var syncFrequency)
                ? syncFrequency
                : SyncFrequency.Weekly;
            preferences.MinimizeToTray = MinimizeToTray;
            preferences.Use12HourClock = Use12HourClock;
            preferences.ShowSnackCards = ShowSnackCards;
            preferences.StartWithWindows = StartWithWindows;

            await _profiles.SavePreferencesAsync(preferences);
            await _startupTask.TrySetEnabledAsync(StartWithWindows);
            await _reminders.RefreshAsync();

            StatusMessage = _localization.Get("Settings_Saved");
        }
        catch (Exception)
        {
            // A failed write must never leave the switch silently lying about the stored value, so the
            // card says that the change was not kept.
            StatusMessage = _localization.Get("Settings_SaveFailed");
        }
        finally
        {
            _saveGate.Release();
        }
    }

    [RelayCommand]
    private async Task SignInGoogleAsync()
    {
        AccountStatusMessage = string.Empty;
        var overlayShown = false;
        try
        {
            // The overlay covers the window while the account changes; the window stays closable
            // because the sign-in itself happens in the browser.
            var stage = new Progress<AccountTransitionStage>(value => ReportStage(value, signOut: false));
            _busy.Show(_localization.Get("Busy_UploadingChanges"));
            overlayShown = true;

            // The pending changes of the current account are uploaded before the credentials are
            // replaced; the cache is emptied once the new account is really connected (cloud-only
            // policy), so a cancelled sign-in costs nothing.
            var preparation = await _cloudData.PrepareForSignInAsync(stage);
            if (!preparation.Succeeded)
            {
                AccountStatusMessage = _localization.Format("Account_SignInPendingChanges", preparation.PendingChanges);
                return;
            }

            // The upload consent is granted by the sign-in itself: once an account is connected the
            // meals and photographs are stored in it, so the account is the answer to the question
            // "where do my meals live?" and no separate switch is needed.
            _busy.Update(_localization.Get("Busy_WaitingForBrowser"));
            var result = await _authentication.SignInAsync(ProviderType.Google, grantUploadConsent: true);
            AccountStatusMessage = result.Succeeded
                ? _localization.Get("Account_SignedIn")
                : result.IsCancelled
                    ? _localization.Get("Auth_Cancelled")
                    : _localization.Get(LocalizationKeys.ForReason(result.ReasonCode));

            if (result.Succeeded)
            {
                var completion = await _cloudData.CompleteSignInAsync(preparation.LocalDataAdopted, stage);
                AccountStatusMessage = completion.CloudDataLoaded
                    ? _localization.Format(
                        "Account_CloudDataLoaded",
                        completion.LocalDataAdopted ? completion.Uploaded : completion.Downloaded)
                    : _localization.Get("Account_CloudDataPending");

                await RefreshSyncTextAsync();
            }
        }
        finally
        {
            if (overlayShown)
            {
                _busy.Hide();
            }

            await LoadAccountAsync();
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

        try
        {
            // Signing out synchronizes on its own (upload, then delete the local cache) and locks the
            // window for its duration: leaving in the middle would leave the cache half deleted.
            var stage = new Progress<AccountTransitionStage>(value => ReportStage(value, signOut: true));

            _busy.Show(_localization.Get("Busy_Synchronizing"), blocksClosing: true);
            AccountTransitionResult result;
            try
            {
                result = await _cloudData.SignOutAsync(progress: stage);
            }
            finally
            {
                _busy.Hide();
            }

            if (!result.Succeeded && result.FailureReason == AccountTransitionResult.PendingChangesReason)
            {
                var detail = _localization.Format("Account_SignOutPendingDetail", result.PendingChanges);
                var discard = await _dialogs.ConfirmAsync(
                    "Account_SignOutPendingTitle",
                    "Account_SignOutPendingMessage",
                    detail);

                if (!discard)
                {
                    AccountStatusMessage = _localization.Get("Account_SignOutPendingKept");
                    return;
                }

                _busy.Show(_localization.Get("Busy_SigningOut"), blocksClosing: true);
                try
                {
                    await _cloudData.SignOutAsync(discardPendingChanges: true, progress: stage);
                }
                finally
                {
                    _busy.Hide();
                }

                AccountStatusMessage = _localization.Get("Account_SignOutDiscarded");
            }
            else
            {
                AccountStatusMessage = _localization.Get("Account_SignedOut");
            }
        }
        finally
        {
            await LoadAccountAsync();
            await RefreshSyncTextAsync();
        }
    }

}

/// <summary>A selectable option of a combo box (tag plus the text shown to the user).</summary>
public sealed record LanguageOption(string Tag, string DisplayName);
