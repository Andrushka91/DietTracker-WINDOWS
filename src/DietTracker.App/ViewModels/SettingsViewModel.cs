using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Settings screen: appearance, reminders, Windows integration, backups and the explicit privacy
/// operations (which are kept strictly separate from each other).
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IProfileService _profiles;
    private readonly IStartupTaskService _startupTask;
    private readonly IBackupService _backup;
    private readonly IPrivacyService _privacy;
    private readonly IFilePickerService _filePicker;
    private readonly IDialogService _dialogs;
    private readonly ILocalizationService _localization;
    private readonly IReminderScheduler _reminders;
    private readonly ISyncEngine _syncEngine;
    private readonly IPhotoStore _photoStore;
    private readonly IDataIntegrityChecker _integrity;
    private readonly IAppEnvironment _environment;
    private readonly IAppDataPaths _paths;

    private AppPreferences? _preferences;

    public SettingsViewModel(
        IProfileService profiles,
        IStartupTaskService startupTask,
        IBackupService backup,
        IPrivacyService privacy,
        IFilePickerService filePicker,
        IDialogService dialogs,
        ILocalizationService localization,
        IReminderScheduler reminders,
        ISyncEngine syncEngine,
        IPhotoStore photoStore,
        IDataIntegrityChecker integrity,
        IAppEnvironment environment,
        IAppDataPaths paths)
    {
        _profiles = profiles;
        _startupTask = startupTask;
        _backup = backup;
        _privacy = privacy;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _localization = localization;
        _reminders = reminders;
        _syncEngine = syncEngine;
        _photoStore = photoStore;
        _integrity = integrity;
        _environment = environment;
        _paths = paths;
    }

    public IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new("ro-RO", "Română"),
        new("en-US", "English"),
        new("es-ES", "Español"),
    ];

    [ObservableProperty]
    private string _selectedLanguage = string.Empty;

    [ObservableProperty]
    private bool _remindersEnabled = true;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private bool _use12HourClock;

    [ObservableProperty]
    private bool _showSnackCards = true;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _storageText = string.Empty;

    [ObservableProperty]
    private string _aboutText = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _lastSyncText = string.Empty;

    /// <summary>Loads the current preferences.</summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        _preferences = await _profiles.GetPreferencesAsync();

        SelectedLanguage = string.IsNullOrEmpty(_preferences.Language) ? "ro-RO" : _preferences.Language;
        RemindersEnabled = _preferences.RemindersEnabled;
        MinimizeToTray = _preferences.MinimizeToTray;
        Use12HourClock = _preferences.Use12HourClock;
        ShowSnackCards = _preferences.ShowSnackCards;
        StartWithWindows = await _startupTask.IsEnabledAsync();

        var usage = await _photoStore.GetStorageUsageAsync();
        StorageText = _localization.Format("Settings_StorageUsage", usage / 1024d / 1024d);
        DatabasePath = _paths.DatabaseFilePath;
        AboutText = _localization.Format("Settings_AboutText", _environment.ApplicationVersion, _environment.OperatingSystemDescription);
        LastSyncText = _syncEngine.LastSuccessfulSyncAt is { } timestamp
            ? _localization.Format("Sync_LastSuccess", timestamp.ToLocalTime().ToString("g"))
            : _localization.Get("Sync_Never");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_preferences is null)
        {
            return;
        }

        _preferences.Language = SelectedLanguage;
        _preferences.RemindersEnabled = RemindersEnabled;
        _preferences.MinimizeToTray = MinimizeToTray;
        _preferences.Use12HourClock = Use12HourClock;
        _preferences.ShowSnackCards = ShowSnackCards;

        await _profiles.SavePreferencesAsync(_preferences);
        await _startupTask.TrySetEnabledAsync(StartWithWindows);
        await _reminders.RefreshAsync();

        _localization.SetLanguage(SelectedLanguage);
        StatusMessage = _localization.Get("Settings_Saved");
    }
}

/// <summary>A selectable interface language.</summary>
public sealed record LanguageOption(string Tag, string DisplayName);
