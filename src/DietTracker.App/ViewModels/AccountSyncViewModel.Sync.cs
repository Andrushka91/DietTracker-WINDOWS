using CommunityToolkit.Mvvm.Input;
using DietTracker.App.Services;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>Synchronization and restore of the account screen.</summary>
public sealed partial class AccountSyncViewModel
{
    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsBusy = true;
        try
        {
            var result = await _syncEngine.SynchronizeAsync(SyncTrigger.Manual);
            StatusMessage = result.Succeeded
                ? _localization.Format("Account_SyncResult", result.Uploaded, result.Downloaded)
                : _localization.Get($"Sync_{result.State}");
        }
        finally
        {
            IsBusy = false;
            await RefreshStateAsync();
        }
    }

    [RelayCommand]
    private async Task PreviewRestoreAsync()
    {
        IsBusy = true;
        try
        {
            var preview = await _syncEngine.PreviewRestoreAsync();
            StatusMessage = preview is null
                ? _localization.Get("Account_NoCloudData")
                : _localization.Format("Account_RestorePreview", preview.MealEntries, preview.Photos);
        }
        finally
        {
            IsBusy = false;
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

        IsBusy = true;
        try
        {
            var result = await _syncEngine.RestoreAsync();
            StatusMessage = result.Succeeded
                ? _localization.Format("Account_RestoreDone", result.Downloaded)
                : _localization.Get($"Sync_{result.State}");
        }
        finally
        {
            IsBusy = false;
            await RefreshStateAsync();
        }
    }
}
