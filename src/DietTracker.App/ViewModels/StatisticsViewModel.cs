using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.UI.Xaml;

namespace DietTracker.App.ViewModels;

/// <summary>One bar of a trend chart.</summary>
public sealed partial class TrendBarViewModel : ObservableObject
{
    private readonly string _label;
    private readonly bool _showPercentage;

    /// <param name="point">Day (or aggregated month) the bar stands for.</param>
    /// <param name="label">Text under the bar: a day number, a day range, a month abbreviation or
    /// empty on a chart that does not label every bar.</param>
    /// <param name="showPercentage">Whether the share is printed under the bar.</param>
    public TrendBarViewModel(TrendPoint point, string label, bool showPercentage)
    {
        Date = point.Date;
        Percentage = point.Percentage;
        Completed = point.Completed;
        Missed = point.Missed;
        Decided = point.Decided;
        _label = label;
        _showPercentage = showPercentage;
        BarFill = new GridLength(Math.Clamp(Percentage, 0d, 100d), GridUnitType.Star);
        BarRest = new GridLength(Math.Clamp(100d - Percentage, 0d, 100d), GridUnitType.Star);
    }

    /// <summary>
    /// Height of the lane the bar stands in. The page raises it so the statistics fill the whole
    /// panel height like the other screens; the bar itself follows through <see cref="BarFill"/>.
    /// </summary>
    [ObservableProperty]
    private double _laneHeight = 140d;

    public DateOnly Date { get; }

    public double Percentage { get; }

    public int Completed { get; }

    public int Missed { get; }

    /// <summary>Confirmed plus missed meals of that day; zero when nothing was decided yet.</summary>
    public int Decided { get; }

    /// <summary>Label rendered under the bar.</summary>
    public string Label => _label;

    /// <summary>
    /// Percentage rendered as text under the bar (never as a tooltip, see StatisticsPage). A day
    /// without any decided meal has no ratio to show and prints a dash instead of "0 %"; a bar the
    /// chart does not label prints nothing at all.
    /// </summary>
    public string PercentageText
        => !_showPercentage
            ? string.Empty
            : Decided > 0 ? FormattableString.Invariant($"{Percentage:0.#}%") : "–";

    /// <summary>Height of the filled part of the bar (bottom rows, star share of <see cref="Percentage"/>).</summary>
    public GridLength BarFill { get; }

    /// <summary>Height of the empty distance above the bar (top rows).</summary>
    public GridLength BarRest { get; }
}

/// <summary>
/// Progress and statistics screen: the state of the day and the day counts of the week and month,
/// plus the trend series.
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

        // Empty placeholders so the page can bind before the first load finishes.
        _daily = BuildPeriod(string.Empty, 0, 0, 0, 0);
        _weekly = BuildPeriod(string.Empty, 0, 0, 0, 0);
        _monthly = BuildPeriod(string.Empty, 0, 0, 0, 0);
    }

    /// <summary>Daily bars of the visible week.</summary>
    public ObservableCollection<TrendBarViewModel> Trend { get; } = [];

    /// <summary>One bar per week of the visible month (labelled with the day range it spans).</summary>
    public ObservableCollection<TrendBarViewModel> MonthlyTrend { get; } = [];

    /// <summary>One bar per month of the visible year (labelled with the month name).</summary>
    public ObservableCollection<TrendBarViewModel> YearlyTrend { get; } = [];

    /// <summary>
    /// Height of one bar lane: the indicator columns of the period cards and the lanes of the trend
    /// charts. The page raises it until the statistics reach the bottom of the panel, so every
    /// screen uses the full height it is given; both rows always share the same value, which keeps
    /// the layout even.
    /// </summary>
    [ObservableProperty]
    private double _laneHeight = 140d;

    /// <summary>Raised after the periods and all three trend series have been rebuilt, so the page
    /// can re-fit the lanes to the panel with the final content measured.</summary>
    public event EventHandler? DataLoaded;

    partial void OnLaneHeightChanged(double value)
    {
        foreach (var bar in Trend)
        {
            bar.LaneHeight = value;
        }

        foreach (var bar in MonthlyTrend)
        {
            bar.LaneHeight = value;
        }

        foreach (var bar in YearlyTrend)
        {
            bar.LaneHeight = value;
        }

        ApplyLaneHeight(Daily, value);
        ApplyLaneHeight(Weekly, value);
        ApplyLaneHeight(Monthly, value);
    }

    private static void ApplyLaneHeight(PeriodStateViewModel period, double value)
    {
        period.Done.LaneHeight = value;
        period.Missed.LaneHeight = value;
        period.Pending.LaneHeight = value;
    }

    [ObservableProperty]
    private DateOnly _anchorDate;

    [ObservableProperty]
    private string _periodLabel = string.Empty;

    /// <summary>
    /// Today split into the three meal states: the state of the day and one vertical percentage
    /// indicator per state.
    /// </summary>
    [ObservableProperty]
    private PeriodStateViewModel _daily;

    /// <summary>The week containing the anchor date, split the same way as <see cref="Daily"/>.</summary>
    [ObservableProperty]
    private PeriodStateViewModel _weekly;

    /// <summary>The month containing the anchor date, split the same way as <see cref="Daily"/>.</summary>
    [ObservableProperty]
    private PeriodStateViewModel _monthly;

    [ObservableProperty]
    private string _trendTitle = string.Empty;

    [ObservableProperty]
    private string _monthlyTrendTitle = string.Empty;

    [ObservableProperty]
    private string _yearlyTrendTitle = string.Empty;

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
        MonthlyTrendTitle = _localization.Get("Statistics_MonthlyTrend");
        YearlyTrendTitle = _localization.Get("Statistics_YearlyTrend");
        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        PeriodLabel = AnchorDate.ToDateTime(TimeOnly.MinValue).ToString("D");

        // Only confirmed and missed meals are counted; untouched slots stay pending in the database.
        // Every period shows its day counts and one vertical percentage indicator per state with
        // the number of slots and the percentage of the period it represents.
        var daily = await _tracking.GetDayProgressAsync(AnchorDate);
        Daily = BuildPeriod(
            daily.IsFullyCompleted
                ? _localization.Get("Statistics_DayDone")
                : daily.Missed > 0
                    ? _localization.Get("Statistics_DayMissed")
                    : _localization.Get("Statistics_DayPending"),
            daily.TotalSlots,
            daily.Completed,
            daily.Missed,
            daily.Pending);

        var weekly = await _tracking.GetWeekProgressAsync(AnchorDate);
        Weekly = BuildPeriod(
            FormatDaysSummary(weekly),
            weekly.TotalSlots,
            weekly.Completed,
            weekly.Missed,
            weekly.Pending);

        var monthly = await _tracking.GetMonthProgressAsync(AnchorDate);
        Monthly = BuildPeriod(
            FormatDaysSummary(monthly),
            monthly.TotalSlots,
            monthly.Completed,
            monthly.Missed,
            monthly.Pending);

        // Only the visible windows are materialized, which keeps memory bounded for long histories.
        var points = await _tracking.GetTrendAsync(weekly.From, weekly.To);
        Trend.Clear();
        foreach (var point in points)
        {
            Trend.Add(new TrendBarViewModel(point, point.Date.ToString("dd"), showPercentage: true)
            {
                LaneHeight = LaneHeight,
            });
        }

        // The month as one bar per week of the month: five wide buckets keep every label readable at
        // any window width (thirty-one thin bars would clip their own day numbers) while still
        // showing how the month went. Each bucket carries the day range it spans and the share of
        // its decided meals.
        var monthPoints = await _tracking.GetTrendAsync(monthly.From, monthly.To);
        MonthlyTrend.Clear();
        foreach (var group in monthPoints.GroupBy(point => (point.Date.Day - 1) / 7))
        {
            var completed = group.Sum(point => point.Completed);
            var missed = group.Sum(point => point.Missed);
            var first = group.First();
            var last = group.Last();
            MonthlyTrend.Add(new TrendBarViewModel(
                AggregateTrendPoint(first.Date, completed, missed),
                FormattableString.Invariant($"{first.Date:dd}–{last.Date:dd}"),
                showPercentage: true)
            {
                LaneHeight = LaneHeight,
            });
        }

        // The year as one bar per month: the daily points of the whole year summed up per month.
        // The month name is the only text - the columns are too narrow for a percentage at small
        // window widths - and the share is carried by the height of the bar and the cards above.
        var yearStart = new DateOnly(AnchorDate.Year, 1, 1);
        var yearEnd = new DateOnly(AnchorDate.Year, 12, 31);
        var yearPoints = await _tracking.GetTrendAsync(yearStart, yearEnd);
        YearlyTrend.Clear();
        foreach (var month in yearPoints.GroupBy(point => point.Date.Month).OrderBy(group => group.Key))
        {
            var first = month.First();
            YearlyTrend.Add(new TrendBarViewModel(
                AggregateTrendPoint(first.Date, month.Sum(point => point.Completed), month.Sum(point => point.Missed)),
                first.Date.ToString("MMM"),
                showPercentage: false)
            {
                LaneHeight = LaneHeight,
            });
        }

        // The page fits its lanes to the panel only after everything is in place.
        DataLoaded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>One trend point for a bucket of days (a week of the month or a whole month), with
    /// the share computed the same way a single day computes it.</summary>
    private static TrendPoint AggregateTrendPoint(DateOnly date, int completed, int missed)
        => new()
        {
            Date = date,
            Percentage = completed + missed == 0 ? 0d : Math.Round(completed * 100d / (completed + missed), 2),
            Completed = completed,
            Missed = missed,
        };

    /// <summary>
    /// Formats the day counts of a period: the total number of days - neutral by default - of which
    /// the done ones (all five meals confirmed) and the missed ones (at least one missed meal); the
    /// remaining days are the untouched, neutral ones.
    /// </summary>
    private string FormatDaysSummary(PeriodProgress period)
        => _localization.Format(
            "Statistics_DaysSummary",
            period.Days.Count,
            period.FullyCompletedDays,
            period.Days.Count(day => day.Missed > 0));

    /// <summary>
    /// Builds one statistics period: the summary line and the three vertical percentage indicators -
    /// each with its slot count and its share of the period in percent, so done, missed and pending
    /// can be compared directly.
    /// </summary>
    private PeriodStateViewModel BuildPeriod(
        string summary,
        int totalSlots,
        int completed,
        int missed,
        int pending)
    {
        return new PeriodStateViewModel
        {
            Summary = summary,
            Done = new StateSliceViewModel(
                _localization.Get("Status_Done"), MealCardViewModel.DoneBrush, completed, totalSlots)
            {
                LaneHeight = LaneHeight,
            },
            Missed = new StateSliceViewModel(
                _localization.Get("Status_Missed"), MealCardViewModel.MissedBrush, missed, totalSlots)
            {
                LaneHeight = LaneHeight,
            },
            Pending = new StateSliceViewModel(
                _localization.Get("Status_Pending"), MealCardViewModel.PendingBrush, pending, totalSlots)
            {
                LaneHeight = LaneHeight,
            },
        };
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
