using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Manual synchronization, cloud preview and restore of the settings screen: the actions that used
/// to live on the separate account page. Synchronization and restore report what they did in the
/// synchronization card, the preview answers in a popup of its own.
/// </summary>
public sealed partial class SettingsViewModel
{
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsSyncBusy = true;

        // The overlay keeps the window in place while the provider is contacted: the user sees that
        // the application is working and cannot start a second cycle by accident.
        _busy.Show(_localization.Get("Busy_Synchronizing"), blocksClosing: true);
        try
        {
            var result = await _syncEngine.SynchronizeAsync(SyncTrigger.Manual);
            SyncStatusMessage = result.Succeeded
                ? _localization.Format("Account_SyncResult", result.Uploaded, result.Downloaded)
                : _localization.Get($"Sync_{result.State}");
        }
        finally
        {
            _busy.Hide();
            IsSyncBusy = false;
            await RefreshSyncTextAsync();
        }
    }

    [RelayCommand]
    private async Task PreviewRestoreAsync()
    {
        IsSyncBusy = true;
        try
        {
            // The answer is shown as a popup: a line at the bottom of the page was too easy to
            // miss, so pressing the button now always opens a dialog with the result.
            var preview = await _syncEngine.PreviewRestoreAsync();
            if (preview is null)
            {
                await _dialogs.ShowMessageAsync("Account_Preview", "Account_NoCloudData");
            }
            else
            {
                await _dialogs.ShowMessageAsync(
                    "Account_Preview",
                    "Account_PreviewIntro",
                    _localization.Format("Account_RestorePreview", preview.MealEntries, preview.Photos));
            }
        }
        finally
        {
            IsSyncBusy = false;
        }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var confirmed = await _dialogs.ConfirmAsync("Account_RestoreTitle", "Account_RestoreMessage");
        if (!confirmed)
        {
            return;
        }

        IsSyncBusy = true;
        _busy.Show(_localization.Get("Busy_Restoring"), blocksClosing: true);
        try
        {
            var result = await _syncEngine.RestoreAsync();
            SyncStatusMessage = result.Succeeded
                ? _localization.Format("Account_RestoreDone", result.Downloaded)
                : _localization.Get($"Sync_{result.State}");
        }
        finally
        {
            _busy.Hide();
            IsSyncBusy = false;
            await RefreshSyncTextAsync();
        }
    }
}
