using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace DietTracker.App.ViewModels;

/// <summary>
/// One cell of the monthly calendar. The cell carries its day state so the view can mark it the
/// way the Microsoft calendar marks events: gray is the pending state - the default of a day the
/// user has not touched yet -, red marks a day on which at least one meal was missed
/// (<see cref="Missed"/>) and green a day where all five meals were eaten
/// (<see cref="IsFullyCompleted"/>). The border repeats the colour of the background, so the cell
/// needs no icon of its own. Untouched meals never count, so a day the user recorded nothing for
/// shows only its day number.
/// </summary>
public sealed partial class CalendarDayViewModel : ObservableObject
{
    private static readonly Brush GreenBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
    // Red marks a day on which at least one recorded meal was missed.
    private static readonly Brush RedBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 164, 38, 44));
    // Gray is the pending state: the default of a day where nothing was decided yet.
    private static readonly Brush GrayBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 138, 136, 134));
    private static readonly Brush TransparentBrush = new SolidColorBrush(Colors.Transparent);
    private static readonly Brush WhiteBrush = new SolidColorBrush(Colors.White);
    // The today fill: the accent colour at high opacity so the white day number stays readable
    // on both the dark and the light theme (a translucent wash becomes too pale on white).
    private static readonly Brush TodayFillBrush = new SolidColorBrush(ColorHelper.FromArgb(235, 15, 108, 189));
    // Day state tints: the background - not an icon - shows whether the meals were eaten (green),
    // at least one was missed (red) or nothing was decided yet (gray).
    private static readonly Brush DoneFillBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 16, 124, 16));
    private static readonly Brush MissedFillBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 164, 38, 44));
    private static readonly Brush NeutralFillBrush = new SolidColorBrush(ColorHelper.FromArgb(18, 138, 136, 134));

    /// <summary>
    /// The normal day number color, resolved from the active theme; the theme lookup is defensive
    /// because it is not available in every WinAppSDK version. A null brush would render nothing,
    /// so there is always a real fallback.
    /// </summary>
    private static Brush DefaultTextBrush()
    {
        try
        {
            if (Microsoft.UI.Xaml.Application.Current?.Resources["TextFillColorPrimaryBrush"] is Brush brush)
            {
                return brush;
            }
        }
        catch (Exception)
        {
            // Fall through to the system's own foreground colour.
        }

        // The system foreground is dark in light mode and light in dark mode, so the fallback can
        // never produce unreadable text on one of the two themes.
        var dark = Services.ThemeHelper.IsDarkAppMode();
        return new SolidColorBrush(dark
            ? ColorHelper.FromArgb(255, 232, 232, 232)
            : ColorHelper.FromArgb(255, 32, 32, 32));
    }

    public CalendarDayViewModel(DateOnly date, int dayNumber, bool isCurrentMonth, bool isToday)
    {
        Date = date;
        DayNumber = dayNumber;
        IsCurrentMonth = isCurrentMonth;
        IsToday = isToday;
        UpdateStateVisuals();
    }

    public DateOnly Date { get; }

    public int DayNumber { get; }

    public bool IsCurrentMonth { get; }

    public bool IsToday { get; }

    /// <summary>
    /// Height of this cell in device-independent pixels (NaN = natural height). The page keeps it
    /// in step with the row of the grid: the item container arranges the day button at its desired
    /// size, so only an explicit height makes the cell fill the (up to square) height of its row.
    /// </summary>
    [ObservableProperty]
    private double _cellHeight = double.NaN;

    [ObservableProperty]
    private int _completed;

    [ObservableProperty]
    private double _percentage;

    [ObservableProperty]
    private bool _hasPhotos;

    [ObservableProperty]
    private bool _isFullyCompleted;

    /// <summary>Number of meals of the day the user recorded but never confirmed after their planned
    /// time had passed. Untouched slots are not counted here - they stay pending.</summary>
    [ObservableProperty]
    private int _missed;

    /// <summary>True when the day has at least one recorded meal (used for the completion indicator).</summary>
    public bool HasRecords => Completed > 0;

    /// <summary>
    /// Percentage as compact text ("42 %"): the confirmed share of the meals that were decided on
    /// this day (the same figure the dashboard bar shows). Empty while nothing was decided yet, so
    /// an untouched cell shows its day number and nothing else - pending meals never produce a
    /// figure.
    /// </summary>
    public string PercentageText => Completed > 0 || Missed > 0
        ? FormattableString.Invariant($"{Percentage:0.#} %")
        : string.Empty;

    // --- Visual state, recomputed whenever the day data changes ---

    /// <summary>Border of the day cell: green when every meal was eaten, red when at least one was
    /// missed, gray while nothing was decided on that day.</summary>
    public Brush DayBorderBrush { get; private set; } = GrayBrush;

    /// <summary>Background tint of the day state - the tint is what tells the story, no icon is drawn.</summary>
    public Brush DayBackground { get; private set; } = TransparentBrush;

    /// <summary>Text color of the day number (white on the blue today cell). The constructor
    /// replaces the placeholder with the theme colour before the cell is ever rendered.</summary>
    public Brush DayNumberBrush { get; private set; } = TransparentBrush;

    /// <summary>Cells outside the visible month are dimmed.</summary>
    public double CellOpacity => IsCurrentMonth ? 1d : 0.35d;

    /// <summary>
    /// Text colour of the percentage row: the secondary text colour of the active theme, but white
    /// on the strong accent fill of today, so the figure stays readable on both dark and light.
    /// </summary>
    public Brush StateTextBrush { get; private set; } = GrayBrush;

    /// <summary>The secondary text colour of the active theme, resolved fresh so a theme switch
    /// between two updates can never leave a stale colour behind; neutral fallback.</summary>
    private static Brush SubtleTextBrush()
    {
        try
        {
            if (Microsoft.UI.Xaml.Application.Current?.Resources["TextFillColorSecondaryBrush"] is Brush brush)
            {
                return brush;
            }
        }
        catch (Exception)
        {
            // Fall through to the literal colour.
        }

        return new SolidColorBrush(ColorHelper.FromArgb(255, 138, 136, 134));
    }

    /// <summary>Re-resolves every theme dependent brush of the cell (used after a dark/light switch
    /// while the calendar is on screen).</summary>
    internal void RefreshVisuals() => UpdateStateVisuals();

    private void UpdateStateVisuals()
    {
        if (IsFullyCompleted)
        {
            // Every meal of the day was eaten: green border, green tint in the background.
            DayBorderBrush = GreenBrush;
            DayBackground = DoneFillBrush;
        }
        else if (Missed > 0)
        {
            // At least one recorded meal was missed: red border and a red tint.
            DayBorderBrush = RedBrush;
            DayBackground = MissedFillBrush;
        }
        else
        {
            // Nothing decided on this day (untouched day, a day still in progress without a miss,
            // or a padding cell of the neighbouring months): the neutral gray state.
            DayBorderBrush = GrayBrush;
            DayBackground = NeutralFillBrush;
        }

        if (IsToday)
        {
            // Today keeps the accent fill and the white day number so it can be found at a glance;
            // the border still carries the day state. The figure below is white as well, because the
            // strong accent fill no longer leaves room for the dark secondary text of the light theme.
            DayBackground = TodayFillBrush;
            DayNumberBrush = WhiteBrush;
            StateTextBrush = WhiteBrush;
        }
        else
        {
            DayNumberBrush = DefaultTextBrush();
            StateTextBrush = SubtleTextBrush();
        }

        OnPropertyChanged(nameof(DayBorderBrush));
        OnPropertyChanged(nameof(DayBackground));
        OnPropertyChanged(nameof(DayNumberBrush));
        OnPropertyChanged(nameof(StateTextBrush));
        OnPropertyChanged(nameof(CellOpacity));
        OnPropertyChanged(nameof(PercentageText));
    }

    partial void OnCompletedChanged(int value) => UpdateStateVisuals();

    partial void OnMissedChanged(int value) => UpdateStateVisuals();

    partial void OnIsFullyCompletedChanged(bool value) => UpdateStateVisuals();

    partial void OnPercentageChanged(double value) => OnPropertyChanged(nameof(PercentageText));
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

    /// <summary>The seven localized weekday names, Monday first (matches the grid layout).</summary>
    public IReadOnlyList<string> WeekdayNames
        => WeekdayHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Legend labels of the day states, shown under the grid in one horizontal row.</summary>
    public string LegendGreenLabel => _localization.Get("Calendar_LegendGreen");

    public string LegendRedLabel => _localization.Get("Calendar_LegendRed");

    public string LegendPendingLabel => _localization.Get("Calendar_LegendPending");

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

    /// <summary>Jumps back to the month of today.</summary>
    [RelayCommand]
    private async Task GoToTodayAsync()
    {
        VisibleMonth = new DateOnly(_clock.Today.Year, _clock.Today.Month, 1);
        SelectedDate = _clock.Today;
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
        SelectedSummary = _localization.Format("Calendar_DaySummary", progress.Completed, progress.Missed, progress.Pending);
    }

    /// <summary>Raised so the page can navigate to the dashboard for the selected date.</summary>
    public event EventHandler<DateOnly>? OpenDayRequested;

    [RelayCommand]
    private void OpenSelectedDay() => OpenDayRequested?.Invoke(this, SelectedDate);

    /// <summary>Progress of the currently visible month (cached for grid rebuilds).</summary>
    private PeriodProgress? _monthProgress;

    /// <summary>
    /// Number of week rows of the grid: exactly the rows the visible month spans (five or six), so
    /// every day of the month - up to the 31st - is always on screen and only the leading/trailing
    /// padding days of the neighbouring months share the grid with them. The page reads it to fit
    /// the day squares into the panel.
    /// </summary>
    public int Rows { get; private set; } = 6;

    /// <summary>
    /// Height of one day cell (NaN = natural height), set by the page from the space the panel has.
    /// Every cell receives the same value, so the grid keeps its even rows.
    /// </summary>
    [ObservableProperty]
    private double _cellHeight = double.NaN;

    partial void OnCellHeightChanged(double value)
    {
        foreach (var day in Days)
        {
            day.CellHeight = value;
        }
    }

    /// <summary>
    /// Re-resolves the theme dependent brushes of every cell. The page raises it after a dark/light
    /// switch of the system, so cells that were built under the old theme do not keep its colours.
    /// </summary>
    public void RefreshDayVisuals()
    {
        foreach (var day in Days)
        {
            day.RefreshVisuals();
        }
    }

    private async Task LoadMonthAsync()
    {
        var first = new DateOnly(VisibleMonth.Year, VisibleMonth.Month, 1);
        _monthProgress = await _tracking.GetMonthProgressAsync(first);
        RebuildDays();

        MonthLabel = first.ToDateTime(TimeOnly.MinValue).ToString("Y");
        var selectedProgress = await _tracking.GetDayProgressAsync(SelectedDate);
        SelectedSummary = _localization.Format("Calendar_DaySummary", selectedProgress.Completed, selectedProgress.Missed, selectedProgress.Pending);
    }

    /// <summary>Rebuilds the day grid for the visible month with the current row count.</summary>
    private void RebuildDays()
    {
        var first = new DateOnly(VisibleMonth.Year, VisibleMonth.Month, 1);
        var byDate = _monthProgress?.Days.ToDictionary(d => d.Date, d => d) ?? [];

        // The grid starts on Monday so the layout is stable across cultures.
        var leading = ((int)first.DayOfWeek + 6) % 7;
        var gridStart = first.AddDays(-leading);

        // Exactly the rows the month needs (five or six): the whole month - all 31 days - fits the
        // grid and only the padding cells of the neighbouring months are added around it.
        var monthRows = Math.Max(1, (leading + DateTime.DaysInMonth(first.Year, first.Month) + 6) / 7);
        if (monthRows != Rows)
        {
            Rows = monthRows;
            OnPropertyChanged(nameof(Rows));
        }

        Days.Clear();
        var count = Rows * 7;
        for (var index = 0; index < count; index++)
        {
            var date = gridStart.AddDays(index);
            var day = new CalendarDayViewModel(date, date.Day, date.Month == first.Month, date == _clock.Today)
            {
                CellHeight = CellHeight,
            };

            if (byDate.TryGetValue(date, out var dayProgress))
            {
                day.Completed = dayProgress.Completed;
                day.Missed = dayProgress.Missed;
                day.Percentage = dayProgress.Percentage;
                day.IsFullyCompleted = dayProgress.IsFullyCompleted;
                day.HasPhotos = dayProgress.Slots.Any(s => s.PhotoCount > 0);
            }

            Days.Add(day);
        }
    }
}
