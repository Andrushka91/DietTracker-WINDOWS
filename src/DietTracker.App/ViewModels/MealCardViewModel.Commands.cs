using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>
/// Card commands. Every command routes through <see cref="IMealCardHost"/>, so the card itself never
/// touches persistence, the file system or the network.
/// </summary>
public sealed partial class MealCardViewModel
{
    /// <summary>Switches the card into inline editing mode.</summary>
    [RelayCommand]
    private void Edit()
    {
        ErrorMessage = null;
        IsEditing = true;
    }

    /// <summary>Validates and persists the inline edits, then shows the read-only card again.</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        IsBusy = true;
        try
        {
            await _host.SaveCardAsync(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Discards the pending changes of this card.</summary>
    [RelayCommand]
    private async Task CancelAsync()
    {
        IsEditing = false;
        ErrorMessage = null;

        // Reloading only this slot restores the persisted values of this card; the other cards -
        // including one that is open in inline editing - keep their unsaved state untouched.
        await _host.RefreshCardAsync(this);
    }

    /// <summary>Wipes the whole content of this card; the host asks for a yes/no confirmation first
    /// and leaves everything untouched when the user answers no.</summary>
    [RelayCommand]
    private async Task ClearAsync()
    {
        IsBusy = true;
        try
        {
            await _host.ClearCardAsync(this);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Marks the meal as DONE and records the completion timestamp.</summary>
    [RelayCommand]
    private Task MarkDoneAsync() => _host.ChangeStatusAsync(this, MealStatus.Done);

    /// <summary>Marks the meal as MISSED (red); only available on today or a past date.</summary>
    [RelayCommand]
    private Task MarkMissedAsync() => _host.ChangeStatusAsync(this, MealStatus.Missed);

    /// <summary>Adds a photograph through the native Windows file picker.</summary>
    [RelayCommand]
    private Task AddPhotoAsync() => _host.AddPhotoAsync(this);

    /// <summary>Removes an attached photograph.</summary>
    [RelayCommand]
    private Task RemovePhotoAsync(MealPhotoViewModel? photo)
        => photo is null ? Task.CompletedTask : _host.RemovePhotoAsync(this, photo);

    /// <summary>Opens a photograph full size.</summary>
    [RelayCommand]
    private Task OpenPhotoAsync(MealPhotoViewModel? photo)
        => photo is null ? Task.CompletedTask : _host.OpenPhotoAsync(photo);
}
