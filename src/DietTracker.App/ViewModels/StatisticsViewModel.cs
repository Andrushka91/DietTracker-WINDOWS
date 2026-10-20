using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>One bar of the trend chart.</summary>
public sealed class TrendBarViewModel
{
    public TrendBarViewModel(TrendPoint point)
    {
        Date = point.Date;
        Percentage = point.Percentage;
        Completed = point.Completed;
    }

    public DateOnly Date { get; }

    public double Percentage { get; }

    public int Completed { get; }

    public string Label => Date.ToString("dd");

    /// <summary>Percentage rendered as text under the bar (never as a tooltip, see StatisticsPage).</summary>
    public string PercentageText => $"{Percentage:0.#}%";
}

/// <summary>
/// Progress and statistics screen: daily, weekly and monthly completion plus the trend series.
/// </summary>
/// <remarks>
/// The figures describe what the user recorded; they are not medically verified dietary compliance,
/// and the wording deliberately stays neutral (no punitive feedback).
/// </remarks>
public sealed partial class StatisticsViewModel : ObservableObject
{
    private readonly IMealTrackingService _tracking;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;

    public StatisticsViewModel(IMealTrackingService tracking, ILocalizationService localization, IClock clock)
    {
        _tracking = tracking;
        _localization = localization;
        _clock = clock;
        _anchorDate = clock.Today;
    }

    public ObservableCollection<TrendBarViewModel> Trend { get; } = [];

    [ObservableProperty]
    private DateOnly _anchorDate;

    [ObservableProperty]
    private string _periodLabel = string.Empty;

    [ObservableProperty]
    private double _dailyPercentage;

    [ObservableProperty]
    private string _dailyText = string.Empty;

    [ObservableProperty]
    private double _weeklyPercentage;

    [ObservableProperty]
    private string _weeklyText = string.Empty;

    [ObservableProperty]
    private int _fullyCompletedDays;

    [ObservableProperty]
    private string _fullyCompletedText = string.Empty;

    [ObservableProperty]
    private double _monthlyPercentage;

    [ObservableProperty]
    private string _monthlyText = string.Empty;

    [ObservableProperty]
    private string _trendTitle = string.Empty;

    [ObservableProperty]
    private string _disclaimer = string.Empty;

    /// <summary>Loads the statistics for the anchor date.</summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        if (parameter is DateOnly date)
        {
            AnchorDate = date;
        }

        Disclaimer = _localization.Get("Statistics_Disclaimer");
        TrendTitle = _localization.Get("Statistics_Trend");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        PeriodLabel = AnchorDate.ToDateTime(TimeOnly.MinValue).ToString("D");

        var daily = await _tracking.GetDayProgressAsync(AnchorDate);
        DailyPercentage = daily.Percentage;
        DailyText = _localization.Format("Statistics_DailyText", daily.Completed, daily.Pending, daily.Missed);

        var weekly = await _tracking.GetWeekProgressAsync(AnchorDate);
        WeeklyPercentage = weekly.Percentage;
        FullyCompletedDays = weekly.FullyCompletedDays;
        WeeklyText = _localization.Format("Statistics_WeeklyText", weekly.Completed, weekly.TotalSlots, weekly.Percentage);
        FullyCompletedText = _localization.Format("Statistics_FullyCompleted", weekly.FullyCompletedDays);

        var monthly = await _tracking.GetMonthProgressAsync(AnchorDate);
        MonthlyPercentage = monthly.Percentage;
        MonthlyText = _localization.Format("Statistics_MonthlyText", monthly.Completed, monthly.TotalSlots, monthly.Percentage);

        // Only the visible window is materialized, which keeps memory bounded for long histories.
        var points = await _tracking.GetTrendAsync(weekly.From, weekly.To);
        Trend.Clear();
        foreach (var point in points)
        {
            Trend.Add(new TrendBarViewModel(point));
        }
    }

    [RelayCommand]
    private async Task PreviousWeekAsync()
    {
        AnchorDate = AnchorDate.AddDays(-7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task NextWeekAsync()
    {
        AnchorDate = AnchorDate.AddDays(7);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task ThisWeekAsync()
    {
        AnchorDate = _clock.Today;
        await LoadAsync();
    }
}
