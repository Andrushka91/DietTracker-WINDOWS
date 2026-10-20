using CommunityToolkit.Mvvm.Input;

namespace DietTracker.App.ViewModels;

/// <summary>Day navigation and day level actions of the dashboard.</summary>
public sealed partial class DashboardViewModel
{
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
    private async Task CopyYesterdayAsync()
    {
        var copied = await _tracking.DuplicateDayAsync(SelectedDate.AddDays(-1), SelectedDate);

        StatusMessage = copied == 0
            ? _localization.Get("Dashboard_NothingToCopy")
            : _localization.Format("Dashboard_CopiedMeals", copied);

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
}
