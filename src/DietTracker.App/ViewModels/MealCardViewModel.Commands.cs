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

        // Reloading the day restores the persisted values exactly as the user left them.
        await _host.RefreshAsync();
    }

    /// <summary>Marks the meal as DONE and records the completion timestamp.</summary>
    [RelayCommand]
    private Task MarkDoneAsync() => _host.ChangeStatusAsync(this, MealStatus.Done);

    /// <summary>Reverts a DONE or MISSED meal back to PENDING.</summary>
    [RelayCommand]
    private Task UndoAsync() => _host.ChangeStatusAsync(this, MealStatus.Pending);

    /// <summary>Marks the meal as MISSED (only offered for today or earlier).</summary>
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

    /// <summary>Copies the meal content to another date (without status or photographs).</summary>
    [RelayCommand]
    private Task CopyToDateAsync() => _host.CopyToDateAsync(this);

    /// <summary>Saves the meal as a reusable template.</summary>
    [RelayCommand]
    private Task SaveAsTemplateAsync() => _host.SaveAsTemplateAsync(this);

    /// <summary>Adds an empty ingredient line to the editor.</summary>
    [RelayCommand]
    private void AddIngredientLine() => Quantities.Add(new IngredientLineViewModel(new IngredientLine(), RemoveIngredientLine));
}
