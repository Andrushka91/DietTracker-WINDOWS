using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietTracker.Core.Enums;
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
    // The pressed day: the accent colour as a strong wash, so the selection is obvious without
    // hiding the state border of the cell.
    private static readonly Brush SelectedFillBrush = new SolidColorBrush(ColorHelper.FromArgb(104, 15, 108, 189));
    // Today is already the accent colour: a selected today deepens the fill instead, so the press
    // still changes the background while the white day number stays readable.
    private static readonly Brush SelectedTodayFillBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 9, 72, 124));

    /// <summary>
    /// Number of chip rows inside a day cell: two columns, three rows = the five permanent slots.
    /// The grid always keeps all three rows (untouched slots render invisible), so every square has
    /// exactly the same size and position no matter how many of them are filled.
    /// </summary>
    private const double ChipRows = 3d;

    /// <summary>Gap between the chip rows (the VerticalSpacing of the chip panel): the same 4 px as
    /// the horizontal spacing, so the vertical distance between two rows equals the distance
    /// between the two columns.</summary>
    private const double ChipGap = 4d;

    /// <summary>Smallest row a chip may get before its row hides: below it the day number and the
    /// title line no longer fit inside the square (the title alone needs its line plus the 3 px
    /// padding on both sides).</summary>
    private const double MinChipRow = 18d;

    /// <summary>Space of the cell the chips cannot use: button padding and border (28) plus the row
    /// of the day number (about 25), with one pixel of slack.</summary>
    private const double CellChrome = 54d;

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

    /// <summary>Width of this cell (the column of the grid), set by the page together with
    /// <see cref="CellHeight"/>. It decides how many chip columns fit into the square.</summary>
    [ObservableProperty]
    private double _cellWidth;

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

    /// <summary>True while this day is the selected day: the background takes the selection colour
    /// (the border keeps the state, so a selected day still reads green, red or gray).</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// The recorded meals of this day, rendered inside the cell as mini cards (at most five: one
    /// per permanent slot). Filled by the page's view model whenever the month is rebuilt; the
    /// collection instance never changes, so the cell's template binds to it once.
    /// </summary>
    public ObservableCollection<CalendarMealChipViewModel> Meals { get; } = [];

    /// <summary>
    /// True when the cell has room for the mini cards. The three rows always stretch over the whole
    /// area under the day number (two columns), so a square never changes size with the number of
    /// filled slots; below <see cref="MinChipRow"/> the row could no longer hold its title, and an
    /// untouched day never shows a row at all.
    /// </summary>
    public bool ShowChips => Meals.Any(meal => meal.IsRecorded)
        && !double.IsNaN(CellWidth)
        && CellWidth - 28d >= 48d
        && ChipRowHeight >= MinChipRow;

    /// <summary>Height one chip row gets from the cell: the three rows share everything the day
    /// number leaves over (zero until the page has sized the cell).</summary>
    private double ChipRowHeight => double.IsNaN(CellHeight)
        ? 0d
        : (CellHeight - CellChrome - ((ChipRows - 1) * ChipGap)) / ChipRows;

    /// <summary>
    /// Fits every chip of this day to the rows the cell offers: the photograph, the second line and
    /// its font grow with the row, so the squares use all the space that is available while the
    /// text never spills over the frame.
    /// </summary>
    private void UpdateChipLayout()
    {
        var rowHeight = ChipRowHeight;
        foreach (var meal in Meals)
        {
            meal.UpdateLayout(rowHeight, StateTextBrush);
        }
    }

    /// <summary>Replaces the chips of this day and fits them to the rows the cell offers.</summary>
    internal void SetMeals(IEnumerable<CalendarMealChipViewModel> meals)
    {
        Meals.Clear();
        foreach (var meal in meals)
        {
            Meals.Add(meal);
        }

        UpdateChipLayout();
        OnPropertyChanged(nameof(ShowChips));
    }

    /// <summary>True when the day has at least one recorded meal (used for the completion indicator).</summary>
    public bool HasRecords => Completed > 0;

    /// <summary>
    /// Percentage as compact text ("42 %"): the confirmed share of the five permanent slots of
    /// this day - completed meals divided by five (the same figure the dashboard bar shows). Empty
    /// while nothing was decided yet, so an untouched cell shows its day number and nothing else -
    /// pending meals never produce a figure.
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

        if (IsSelected)
        {
            // The pressed day shows the selection colour instead of the state tint - the border
            // keeps the state, so the cell still reads green, red or gray while selected.
            DayBackground = IsToday ? SelectedTodayFillBrush : SelectedFillBrush;
        }
        else if (IsToday)
        {
            // Today keeps the accent fill and the white day number so it can be found at a glance;
            // the border still carries the day state. The figure below is white as well, because the
            // strong accent fill no longer leaves room for the dark secondary text of the light theme.
            DayBackground = TodayFillBrush;
        }

        if (IsToday)
        {
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

        // The chips carry the same text colour as the percentage row (white on the accent fill of
        // today), so they follow every re-resolution of the theme dependent brushes.
        UpdateChipLayout();
    }

    partial void OnCompletedChanged(int value) => UpdateStateVisuals();

    partial void OnMissedChanged(int value) => UpdateStateVisuals();

    partial void OnIsFullyCompletedChanged(bool value) => UpdateStateVisuals();

    partial void OnPercentageChanged(double value) => OnPropertyChanged(nameof(PercentageText));

    partial void OnIsSelectedChanged(bool value) => UpdateStateVisuals();

    partial void OnCellHeightChanged(double value)
    {
        // The rows of the mini cards are carved out of the height the number leaves over.
        UpdateChipLayout();
        OnPropertyChanged(nameof(ShowChips));
    }

    partial void OnCellWidthChanged(double value) => OnPropertyChanged(nameof(ShowChips));
}

/// <summary>
/// One mini card inside a day cell: a scaled-down copy of the meal card with the first photograph,
/// the title and the description of the slot, framed by the state colour - green when the meal was
/// eaten, red when it was missed and gray while it is still pending. The title falls back to the
/// localized category name when the user never typed one, so a card always has something to read.
/// <para>
/// Every permanent slot produces a chip; an untouched slot is <see cref="IsRecorded"/>
/// <see langword="false"/> and renders invisible, so it keeps its place in the fixed grid without
/// showing an empty frame. The chip itself is deliberately inert: a press on it does nothing of its
/// own, the event bubbles to the day button and only moves the selection there; a double press
/// opens the day as usual.
/// </para>
/// </summary>
public sealed partial class CalendarMealChipViewModel : ObservableObject
{
    private static readonly Brush GreenBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 16, 124, 16));
    private static readonly Brush RedBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 164, 38, 44));
    private static readonly Brush GrayBorderBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 138, 136, 134));
    private static readonly Brush GreenFillBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 16, 124, 16));
    private static readonly Brush RedFillBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 164, 38, 44));
    private static readonly Brush GrayFillBrush = new SolidColorBrush(ColorHelper.FromArgb(26, 138, 136, 134));
    private static readonly Brush DefaultTextBrush = new SolidColorBrush(ColorHelper.FromArgb(255, 138, 136, 134));

    public CalendarMealChipViewModel(
        string title,
        string description,
        string statusLabel,
        MealStatus status,
        string? thumbnailPath,
        bool isRecorded)
    {
        Title = title;
        Description = description;
        ThumbnailPath = thumbnailPath;
        HasPhoto = thumbnailPath is not null;
        IsRecorded = isRecorded;
        BorderBrush = status switch
        {
            MealStatus.Done => GreenBorderBrush,
            MealStatus.Missed => RedBorderBrush,
            _ => GrayBorderBrush,
        };
        Fill = status switch
        {
            MealStatus.Done => GreenFillBrush,
            MealStatus.Missed => RedFillBrush,
            _ => GrayFillBrush,
        };

        // The second line of the miniature is the description when there is one and the status of
        // the slot otherwise, so every card keeps the same two-line shape and reaches both the top
        // and the bottom edge of its frame with the same padding as the sides.
        BottomText = string.IsNullOrWhiteSpace(description) ? statusLabel : description;

        // The miniature lines are trimmed to the frame; the tooltip always shows the full content.
        Tooltip = string.IsNullOrWhiteSpace(description) ? title : $"{title}\n{description}";
    }

    /// <summary>Meal title, or the localized category name when the entry has none.</summary>
    public string Title { get; }

    /// <summary>The description (ingredients) of the meal: the second miniature line.</summary>
    public string Description { get; }

    /// <summary>What sits on the bottom edge of the miniature: the description when the meal has
    /// one, the localized status of the slot (Done / Missed / Pending) otherwise.</summary>
    public string BottomText { get; }

    /// <summary>Border and background carry the state of the slot (green / red / gray).</summary>
    public Brush BorderBrush { get; }

    public Brush Fill { get; }

    /// <summary>Absolute path of the first photograph of the meal, null when it has none.</summary>
    public string? ThumbnailPath { get; }

    public bool HasPhoto { get; }

    /// <summary>False for an untouched slot: its square renders invisible and keeps its place in
    /// the fixed grid, so the filled squares never change size or position.</summary>
    public bool IsRecorded { get; }

    /// <summary>Complete content of the meal (title and description) for the hover tooltip.</summary>
    public string Tooltip { get; }

    /// <summary>Text colour of the lines: follows the day cell (white on the fill of today).</summary>
    [ObservableProperty]
    private Brush _textBrush = DefaultTextBrush;

    /// <summary>Edge of the square photograph: grows with the row the cell gives the chip.</summary>
    [ObservableProperty]
    private double _photoSize = 14d;

    /// <summary>Font of the description line: the taller the row, the more of it fits.</summary>
    [ObservableProperty]
    private double _descriptionFontSize = 6d;

    /// <summary>True when the row is tall enough for both lines (title and description).</summary>
    [ObservableProperty]
    private bool _showDescription;

    /// <summary>
    /// Fits the chip to the row its day cell offers: the photograph grows towards the frame (up to
    /// 28 px), and the bottom line appears as soon as title and second line fit inside the frame
    /// with the 3 px padding - the title then hugs the top edge, the second line the bottom edge,
    /// so the card fills its miniature without the text spilling over the border.
    /// </summary>
    internal void UpdateLayout(double rowHeight, Brush textBrush)
    {
        TextBrush = textBrush;

        // Fills the vertical slot (row minus the two borders and the 3 px top / bottom padding)
        // up to 28 px, so the photograph itself never has more air than the text around it.
        PhotoSize = Math.Clamp(rowHeight - 8d, 10d, 28d);
        ShowDescription = rowHeight >= 27d;
        DescriptionFontSize = rowHeight >= 40d ? 8d : rowHeight >= 30d ? 7d : 6d;
    }
}

/// <summary>
/// Monthly calendar with completion indicators, used to review history and to jump to a date.
/// </summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly IMealTrackingService _tracking;
    private readonly ILocalizationService _localization;
    private readonly IClock _clock;
    private readonly IPhotoStore _photoStore;

    public CalendarViewModel(
        IMealTrackingService tracking,
        ILocalizationService localization,
        IClock clock,
        IPhotoStore photoStore)
    {
        _tracking = tracking;
        _localization = localization;
        _clock = clock;
        _photoStore = photoStore;
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
    /// Width of one day cell (the column of the grid), set by the page from the width the panel has.
    /// The day cells read it to know how many chip columns fit into their square.
    /// </summary>
    [ObservableProperty]
    private double _cellWidth = double.NaN;

    partial void OnCellWidthChanged(double value)
    {
        foreach (var day in Days)
        {
            day.CellWidth = value;
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
                CellWidth = CellWidth,
                // The cell of the selected day carries the selection colour of the background.
                IsSelected = date == SelectedDate,
            };

            if (byDate.TryGetValue(date, out var dayProgress))
            {
                day.Completed = dayProgress.Completed;
                day.Missed = dayProgress.Missed;
                day.Percentage = dayProgress.Percentage;
                day.IsFullyCompleted = dayProgress.IsFullyCompleted;
                day.HasPhotos = dayProgress.Slots.Any(s => s.PhotoCount > 0);
                day.SetMeals(CreateMealChips(dayProgress.Slots));
            }

            Days.Add(day);
        }
    }

    /// <summary>
    /// Builds the mini cards drawn inside one day cell: one per permanent slot (at most five).
    /// Recorded slots - confirmed, missed or a pending entry that already holds a content - are
    /// visible; untouched ones are created as invisible placeholders so the five squares always
    /// share the space of the cell in the same fixed grid. A day the user never touched therefore
    /// shows its day number and nothing else.
    /// </summary>
    private List<CalendarMealChipViewModel> CreateMealChips(IReadOnlyList<MealSlotStatus> slots)
    {
        var chips = new List<CalendarMealChipViewModel>(5);
        foreach (var slot in slots)
        {
            if (chips.Count >= 5)
            {
                break;
            }

            var isRecorded = slot.Status != MealStatus.Pending
                || !string.IsNullOrWhiteSpace(slot.Title)
                || slot.PhotoCount > 0;

            var title = string.IsNullOrWhiteSpace(slot.Title)
                ? _localization.Get($"Meal_{slot.MealCategoryKey}_Name")
                : slot.Title;
            var statusLabel = _localization.Get($"Status_{slot.Status}");
            var thumbnailPath = string.IsNullOrEmpty(slot.FirstPhotoThumbnailId)
                ? null
                : _photoStore.GetThumbnailPath(slot.FirstPhotoThumbnailId);

            chips.Add(new CalendarMealChipViewModel(title, slot.Ingredients, statusLabel, slot.Status, thumbnailPath, isRecorded));
        }

        return chips;
    }

    /// <summary>Keeps the selection colour on the one cell whose date is now selected.</summary>
    partial void OnSelectedDateChanged(DateOnly value)
    {
        foreach (var day in Days)
        {
            day.IsSelected = day.Date == value;
        }
    }
}
