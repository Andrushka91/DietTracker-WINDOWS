using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>Synchronization, restore and provider switching of the account screen.</summary>
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

    /// <summary>
    /// Switches to another provider. The strategy is chosen explicitly by the user; nothing is merged
    /// or overwritten implicitly, and both accounts are named in the confirmation.
    /// </summary>
    [RelayCommand]
    private async Task SwitchProviderAsync(string providerTag)
    {
        if (!Enum.TryParse<ProviderType>(providerTag, ignoreCase: true, out var target))
        {
            return;
        }

        var preview = await _switcher.PreviewAsync(target);
        var detail = _localization.Format(
            "Account_SwitchDetail",
            _localization.Get($"Account_Provider{preview.CurrentProvider}"),
            preview.CurrentAccountEmail,
            preview.LocalRecords,
            preview.PendingChanges);

        var choice = await _dialogs.ChooseAsync(
            "Account_SwitchTitle",
            "Account_SwitchMessage",
            ["Account_SwitchKeepLocal", "Account_SwitchRestore", "Account_SwitchMerge", "Common_Cancel"]);

        var strategy = choice switch
        {
            0 => AccountSwitchStrategy.UploadLocalToNewAccount,
            1 => AccountSwitchStrategy.RestoreFromNewAccount,
            2 => AccountSwitchStrategy.Merge,
            _ => AccountSwitchStrategy.Cancel,
        };

        if (strategy == AccountSwitchStrategy.Cancel)
        {
            StatusMessage = detail;
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _switcher.ExecuteAsync(target, strategy);
            StatusMessage = result.Succeeded
                ? _localization.Format("Account_SwitchDone", result.RecordsUploaded, result.RecordsDownloaded)
                : _localization.Get(result.ReasonCode ?? "Auth_Unknown");
        }
        finally
        {
            IsBusy = false;
            await RefreshStateAsync();
        }
    }
}
