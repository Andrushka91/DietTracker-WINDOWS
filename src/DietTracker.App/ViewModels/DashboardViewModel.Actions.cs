using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>Day navigation and day level actions of the dashboard.</summary>
public sealed partial class DashboardViewModel
{
    /// <summary>True when at least one meal of the day can still be confirmed (enables the green check).
    /// False on future days: their meals are locked in pending.</summary>
    public bool CanMarkAllDone => Cards.Any(c => c.CanMarkDone);

    /// <summary>True when at least one meal of the day can still be marked missed (enables the red
    /// cross). False on future days: their meals are locked in pending.</summary>
    public bool CanMarkAllMissed => Cards.Any(c => c.CanMarkMissed);

    [RelayCommand]
    private async Task PreviousDayAsync() => await ChangeDateAsync(-1);

    [RelayCommand]
    private async Task NextDayAsync() => await ChangeDateAsync(1);

    [RelayCommand]
    private async Task TodayAsync()
    {
        SelectedDate = _clock.Today;
        await LoadAsync();
    }

    [RelayCommand]
    private async Task SaveDayTemplateAsync()
    {
        var name = await _dialogs.PromptTextAsync("Template_NameTitle", $"{SelectedDate:yyyy-MM-dd}");
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        await _tracking.SaveDayTemplateAsync(SelectedDate, name);
        StatusMessage = _localization.Get("Template_Saved");
    }

    private async Task ChangeDateAsync(int days)
    {
        SelectedDate = SelectedDate.AddDays(days);
        await LoadAsync();
    }

    /// <summary>
    /// Confirms every meal of the day at once (the green check above the cards). Only today and
    /// past days change - a future day stays pending. Meals with neither a title nor a description
    /// are refused by the domain and skipped here; the user is told about them once afterwards and
    /// can edit them first. Each card adopts the new state on its own; the collection is never
    /// rebuilt, so a card that is open in inline editing keeps its unsaved text (it reloads when
    /// it saves or cancels).
    /// </summary>
    [RelayCommand]
    private async Task MarkAllDoneAsync()
    {
        var skippedEmpty = 0;
        foreach (var card in Cards.Where(c => c.CanMarkDone).ToList())
        {
            if (string.IsNullOrWhiteSpace(card.Title) && string.IsNullOrWhiteSpace(card.Ingredients))
            {
                // CompletionRules refuses an empty meal: it keeps its pending state until edited.
                skippedEmpty++;
                continue;
            }

            var entry = await _tracking.MarkDoneAsync(card.Id);
            if (!card.IsEditing)
            {
                card.Reload(entry);
            }
        }

        await RefreshProgressAsync();
        StatusMessage = _localization.Get("Dashboard_AllDone");
        if (skippedEmpty > 0)
        {
            await _dialogs.ShowMessageAsync("Meal_EmptyDoneTitle", "Meal_EmptyDoneSomeMessage");
        }
    }

    /// <summary>
    /// Marks every meal of the day as missed (the red cross above the cards). Only today and past
    /// days change - a future day stays pending. Each card adopts the new state on its own; the
    /// collection is never rebuilt, so a card that is open in inline editing keeps its unsaved text
    /// (it reloads when it saves or cancels).
    /// </summary>
    [RelayCommand]
    private async Task MarkAllMissedAsync()
    {
        foreach (var card in Cards.Where(c => c.CanMarkMissed).ToList())
        {
            var entry = await _tracking.MarkMissedAsync(card.Id);
            if (!card.IsEditing)
            {
                card.Reload(entry);
            }
        }

        await RefreshProgressAsync();
        StatusMessage = _localization.Get("Dashboard_AllMissed");
    }

    /// <summary>
    /// True while at least one card of the day is open in inline editing: the "Save all" button
    /// enables the moment the first editor opens and disables again when the last one saved or
    /// cancelled, because then there is nothing left to write.
    /// </summary>
    public bool CanSaveAll => Cards.Any(c => c.IsEditing);

    /// <summary>
    /// Saves every card that is open in inline editing with one press, so no single Save button has
    /// to be pressed card by card. Each open card goes through exactly the same path as its own
    /// Save button: the title and the description are mandatory, so a card that is still missing
    /// one of them keeps its editor open and shows the same explanation on its own error line,
    /// while the complete cards are written and close. The day summary and the synchronization are
    /// refreshed once for the whole batch instead of once per card, and the status line reports
    /// how many of the open cards were saved.
    /// </summary>
    [RelayCommand]
    private async Task SaveAllAsync()
    {
        var open = Cards.Where(c => c.IsEditing).ToList();
        if (open.Count == 0)
        {
            return;
        }

        var saved = 0;
        foreach (var card in open)
        {
            // The same busy state the card's own Save button sets, so every open editor shows its
            // ring while its write runs and the reader can see the batch is working.
            card.IsBusy = true;
            try
            {
                if (await PersistCardAsync(card, CancellationToken.None))
                {
                    saved++;
                }
            }
            finally
            {
                card.IsBusy = false;
            }
        }

        if (saved > 0)
        {
            await RefreshProgressAsync();
            _syncEngine.RequestSync(SyncTrigger.LocalChange);
        }

        StatusMessage = saved == open.Count
            ? _localization.Get("Dashboard_SaveAllDone")
            : _localization.Format("Dashboard_SaveAllPartial", saved, open.Count);
    }

    /// <summary>
    /// Keeps <see cref="CanSaveAll"/> in step with the cards: the button follows the inline editors
    /// of this page, so every card that opens or closes one raises the change notification here
    /// instead of the button having to poll the collection.
    /// </summary>
    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MealCardViewModel.IsEditing))
        {
            OnPropertyChanged(nameof(CanSaveAll));
        }
    }
}
