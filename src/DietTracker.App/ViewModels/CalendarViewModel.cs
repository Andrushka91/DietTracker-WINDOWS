using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;

namespace DietTracker.App.ViewModels;

/// <summary>One cell of the monthly calendar.</summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    public CalendarDayViewModel(DateOnly date, int dayNumber, bool isCurrentMonth, bool isToday)
    {
        Date = date;
        DayNumber = dayNumber;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
    }

    public DateOnly Date { get; }

    public int DayNumber { get; }

    public bool IsCurrentMonth { get; }

    public bool IsToday { get; }

    [ObservableProperty]
    private int _completed;

    [ObservableProperty]
    private double _percentage;

    [ObservableProperty]
    private bool _hasPhotos;

    [ObservableProperty]
    private bool _isFullyCompleted;

    /// <summary>True when the day has at least one recorded meal (used for the completion indicator).</summary>
    public bool HasRecords => Completed > 0;
}

/// <summary>
/// Monthly calendar with completion indicators, used to review history and to jump to a date.
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly IMealTrackingService _tracking;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;

    public CalendarViewModel(IMealTrackingService tracking, ILocalizationService localization, IClock clock)
    {
        _tracking = tracking;
        _localization = localization;
        _clock = clock;
        _visibleMonth = new DateOnly(clock.Today.Year, clock.Today.Month, 1);
    }

    public ObservableCollection<CalendarDayViewModel> Days { get; } = [];

    [ObservableProperty]
    private DateOnly _visibleMonth;

    [ObservableProperty]
    private string _monthLabel = string.Empty;

    [ObservableProperty]
    private DateOnly _selectedDate;

    [ObservableProperty]
    private string _selectedSummary = string.Empty;

    public string WeekdayHeader => _localization.Get("Calendar_Weekdays");

    /// <summary>Loads the visible month.</summary>
    public async Task InitializeAsync(object? parameter = null)
    {
        SelectedDate = parameter is DateOnly date ? date : _clock.Today;
        VisibleMonth = new DateOnly(SelectedDate.Year, SelectedDate.Month, 1);
        await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        VisibleMonth = VisibleMonth.AddMonths(-1);
        await LoadMonthAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        VisibleMonth = VisibleMonth.AddMonths(1);
        await LoadMonthAsync();
    }

    /// <summary>Selects a day and shows its summary; the dashboard is opened from here.</summary>
    [RelayCommand]
    private async Task SelectDayAsync(CalendarDayViewModel? day)
    {
        if (day is null)
        {
            return;
        }

        SelectedDate = day.Date;
        var progress = await _tracking.GetDayProgressAsync(day.Date);
        SelectedSummary = _localization.Format("Calendar_DaySummary", progress.Completed, progress.TotalSlots, progress.Percentage);
    }

    /// <summary>Raised so the page can navigate to the dashboard for the selected date.</summary>
    public event EventHandler<DateOnly>? OpenDayRequested;

    [RelayCommand]
    private void OpenSelectedDay() => OpenDayRequested?.Invoke(this, SelectedDate);

    private async Task LoadMonthAsync()
    {
        var first = new DateOnly(VisibleMonth.Year, VisibleMonth.Month, 1);
        var last = first.AddMonths(1).AddDays(-1);
        var progress = await _tracking.GetMonthProgressAsync(first);

        var byDate = progress.Days.ToDictionary(d => d.Date, d => d);

        // The grid starts on Monday so the layout is stable across cultures.
        var leading = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-leading);

        Days.Clear();
        for (var index = 0; index < 42; index++)
        {
            var date = gridStart.AddDays(index);
            var day = new CalendarDayViewModel(date, date.Day, date.Month == first.Month, date == _clock.Today);

            if (byDate.TryGetValue(date, out var dayProgress))
            {
                day.Completed = dayProgress.Completed;
                day.Percentage = dayProgress.Percentage;
                day.IsFullyCompleted = dayProgress.IsFullyCompleted;
                day.HasPhotos = dayProgress.Slots.Any(s => s.PhotoCount > 0);
            }

            Days.Add(day);
        }

        MonthLabel = first.ToDateTime(TimeOnly.MinValue).ToString("Y");
        var selectedProgress = await _tracking.GetDayProgressAsync(SelectedDate);
        SelectedSummary = _localization.Format("Calendar_DaySummary", selectedProgress.Completed, selectedProgress.TotalSlots, selectedProgress.Percentage);
    }
}
