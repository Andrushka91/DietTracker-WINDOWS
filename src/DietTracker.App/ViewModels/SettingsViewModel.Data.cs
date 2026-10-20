using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Data lifecycle commands of the settings screen.
/// </summary>
/// <remarks>
/// The four operations below are deliberately distinct and each one is confirmed explicitly:
/// creating a backup, restoring a backup, deleting local data and deleting cloud data. Deleting local
/// data never touches the cloud, and deleting cloud data requires a separate confirmation.
/// </remarks>
public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private async Task ExportBackupAsync()
    {
        var path = await _filePicker.PickSaveFileAsync($"diettracker-{DateTime.Now:yyyyMMdd}", ".dtbackup");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var summary = await _backup.ExportAsync(path);
        StatusMessage = _localization.Format("Settings_BackupCreated", summary.MealEntries, summary.Photos);
    }

    [RelayCommand]
    private async Task ImportBackupAsync()
    {
        var path = await _filePicker.PickOpenFileAsync(".dtbackup");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync("Settings_RestoreTitle", "Settings_RestoreMessage");
        if (!confirmed)
        {
            return;
        }

        var summary = await _backup.RestoreAsync(path);
        StatusMessage = _localization.Format("Settings_RestoreDone", summary.MealEntries, summary.Photos);
    }

    [RelayCommand]
    private async Task CheckIntegrityAsync()
    {
        var issues = await _integrity.CheckAsync();
        StatusMessage = issues.Count == 0
            ? _localization.Get("Settings_IntegrityOk")
            : _localization.Format("Settings_IntegrityIssues", issues.Count, string.Join("; ", issues.Select(i => i.Code)));
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        await _syncEngine.SynchronizeAsync(SyncTrigger.Manual);
        LastSyncText = _syncEngine.LastSuccessfulSyncAt is { } timestamp
            ? _localization.Format("Sync_LastSuccess", timestamp.ToLocalTime().ToString("g"))
            : _localization.Get("Sync_Never");
    }

    [RelayCommand]
    private async Task DeleteLocalDataAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Privacy_DeleteLocalTitle", "Privacy_DeleteLocalMessage");
        if (!confirmed)
        {
            return;
        }

        // A safety copy is written first so a mistaken click is always recoverable.
        await _backup.CreateSafetyCopyAsync("delete-local");
        await _privacy.DeleteLocalDataAsync(includePhotographs: true);

        StatusMessage = _localization.Get("Privacy_DeleteLocalDone");
    }

    [RelayCommand]
    private async Task DeleteCloudDataAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Privacy_DeleteCloudTitle", "Privacy_DeleteCloudMessage");
        if (!confirmed)
        {
            return;
        }

        var deleted = await _privacy.DeleteCloudDataAsync();
        StatusMessage = _localization.Format("Privacy_DeleteCloudDone", deleted);
    }

    [RelayCommand]
    private async Task DeletePhotographsAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Privacy_DeletePhotosTitle", "Privacy_DeletePhotosMessage");
        if (!confirmed)
        {
            return;
        }

        var deleted = await _privacy.DeleteAllPhotographsAsync();
        StatusMessage = _localization.Format("Privacy_DeletePhotosDone", deleted);
    }

    [RelayCommand]
    private async Task DisableSynchronizationAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Privacy_DisableSyncTitle", "Privacy_DisableSyncMessage");
        if (!confirmed)
        {
            return;
        }

        await _privacy.DisableSynchronizationAsync();
        StatusMessage = _localization.Get("Privacy_DisableSyncDone");
    }
}
