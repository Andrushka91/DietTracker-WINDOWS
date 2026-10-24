using CommunityToolkit.Mvvm.Input;

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
    /// past days change - a future day stays pending - and the cards are materialized first because
    /// <see cref="LoadAsync"/> rebuilds the collection at the end.
    /// </summary>
    [RelayCommand]
    private async Task MarkAllDoneAsync()
    {
        foreach (var card in Cards.Where(c => c.CanMarkDone).ToList())
        {
            await _tracking.MarkDoneAsync(card.Id);
        }

        await LoadAsync();
        StatusMessage = _localization.Get("Dashboard_AllDone");
    }

    /// <summary>
    /// Marks every meal of the day as missed (the red cross above the cards). Only today and past
    /// days change - a future day stays pending - and the cards are materialized first because
    /// <see cref="LoadAsync"/> rebuilds the collection at the end.
    /// </summary>
    [RelayCommand]
    private async Task MarkAllMissedAsync()
    {
        foreach (var card in Cards.Where(c => c.CanMarkMissed).ToList())
        {
            await _tracking.MarkMissedAsync(card.Id);
        }

        await LoadAsync();
        StatusMessage = _localization.Get("Dashboard_AllMissed");
    }
}
