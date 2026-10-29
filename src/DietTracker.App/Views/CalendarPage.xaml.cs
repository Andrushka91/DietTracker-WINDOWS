using DietTracker.App.Controls;
using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Monthly calendar used to review history and to jump to a specific day.</summary>
public sealed partial class CalendarPage : Page
{
    public CalendarPage()
    {
        ViewModel = App.Services.GetRequiredService<CalendarViewModel>();
        InitializeComponent();

        // The "Open day" button (and the double tap below) open the dashboard for the clicked date:
        // the view model only raises the request, the page owns the shell and performs the navigation.
        ViewModel.OpenDayRequested += (_, date) => OpenDay(date);

        // The day squares are fitted to the panel as early as possible: the ScrollViewer handler runs
        // inside the layout pass itself, so the cells are born at their final height - without it
        // the first pass would lay them out at their natural height and the grid would visibly
        // grow right afterwards (and the shell's content fit, active while the first page loads,
        // would shrink and re-grow the window with it). The dispatcher handlers then cover the
        // later changes (window resize, month switch). All of them read the settled layout.
        ScrollHost.SizeChanged += (_, _) => UpdateGridSizing();
        DaysHost.SizeChanged += (_, _) => UpdateGridSizing();
        SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateGridSizing);

        // The system switched between dark and light while the calendar is on screen: the cells hold
        // brushes that were resolved from the old theme, so re-resolve them for every day.
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => ViewModel.RefreshDayVisuals());
        ViewModel.PropertyChanged += (_, e) =>
        {
            // A new month can need a different number of week rows: refit the squares after the grid.
            if (e.PropertyName == nameof(CalendarViewModel.Rows))
            {
                DispatcherQueue.TryEnqueue(UpdateGridSizing);
            }
        };
    }

    /// <summary>Gap between the day cells (HorizontalSpacing / VerticalSpacing of the grid panel).</summary>
    private const double CellSpacing = 8d;

    /// <summary>StackPanel spacing between the five content children (four gaps of 16).</summary>
    private const double StackSpacingTotal = 64d;

    /// <summary>
    /// Gives the day grid the row height that keeps the whole month inside the panel: the week rows
    /// of the visible month share exactly the height the viewport has left, but a row never becomes
    /// taller than its cell is wide. The rectangles therefore turn into squares whenever there is
    /// room and become a little smaller when the window is short - and nothing ever grows past the
    /// month rows, so at most the few padding days of the neighbouring months appear. The padding of
    /// the ScrollViewer is deliberately not subtracted: the shell fits the window height to
    /// (chrome + padding + content), so content of exactly the viewport height leaves that fit at
    /// the current window size instead of chasing it (a viewport based value minus the padding made
    /// the window shrink towards its minimum on every correction).
    /// </summary>
    private void UpdateGridSizing()
    {
        // The viewport is derived from the height of the ScrollViewer (minus its padding, which is
        // not part of the scrollable area): unlike ViewportHeight it is already fresh inside a
        // SizeChanged handler, so the grid can be fitted during the layout pass itself.
        var viewport = ScrollHost.ActualHeight - ScrollHost.Padding.Top - ScrollHost.Padding.Bottom;
        if (HeaderBar.ActualHeight <= 0
            || viewport <= 0
            || DaysHost.ActualWidth <= 0
            || DaysHost.ItemsPanelRoot is not ColumnGridPanel panel)
        {
            return; // Not laid out yet.
        }

        var rows = Math.Max(1, ViewModel.Rows);
        var reserved = HeaderBar.ActualHeight
            + SummaryText.ActualHeight
            + WeekdayHeader.ActualHeight
            + LegendRow.ActualHeight
            + StackSpacingTotal;

        // Height of a row when the month must fit the viewport exactly, and height of a row when
        // the cell is a perfect square (as wide as the seven columns make it).
        var fittingRow = (viewport - reserved - (CellSpacing * (rows - 1))) / rows;
        var squareRow = (DaysHost.ActualWidth - (CellSpacing * 6)) / 7d;

        // The width of one cell decides how many chip columns fit inside a day square, so the day
        // cells learn it here (the height follows below once the row is settled). The NaN check
        // comes first: NaN arithmetic would keep the value unset forever and the meal squares
        // would never appear.
        if (double.IsNaN(ViewModel.CellWidth) || Math.Abs(ViewModel.CellWidth - squareRow) > 0.5d)
        {
            ViewModel.CellWidth = squareRow;
        }

        var rowHeight = Math.Max(0d, Math.Min(fittingRow, squareRow));
        if (rowHeight <= 0d)
        {
            return; // No usable space (cannot happen within the window limits - never collapse the cells).
        }

        // The half-pixel tolerance keeps the layout from oscillating on rounding noise; the value
        // only ever changes when the window, the month or the panel width actually changed.
        if (Math.Abs(panel.RowHeight - rowHeight) > 0.5d)
        {
            panel.RowHeight = rowHeight;
        }

        // The cell itself must be as tall as its row: the item container arranges the day button at
        // its desired height, so only an explicit height - not the stretch of the row - makes the
        // square fill the cell.
        if (double.IsNaN(ViewModel.CellHeight) || Math.Abs(ViewModel.CellHeight - rowHeight) > 0.5d)
        {
            ViewModel.CellHeight = rowHeight;
        }
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public CalendarViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Guarded like StatisticsPage: an escaping exception in this async void handler would exit the
        // application, so a failed load leaves the page in its previous state instead.
        try
        {
            await ViewModel.InitializeAsync(e.Parameter);
        }
        catch (Exception ex)
        {
            App.Services.GetService<ILogger<CalendarPage>>()
                ?.LogWarning(ex, "The calendar could not be loaded.");
        }
    }

    private void OnDayClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CalendarDayViewModel day })
        {
            _ = ViewModel.SelectDayCommand.ExecuteAsync(day);
        }
    }

    private void OnDayDoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: CalendarDayViewModel day })
        {
            // Selecting first refreshes the summary, then the dashboard opens for that date.
            ViewModel.SelectedDate = day.Date;
            OpenDay(day.Date);
        }
    }

    private void OpenDay(DateOnly date)
    {
        // Opens the dashboard for the selected date; the day is passed as navigation parameter.
        if (App.MainAppWindow is MainWindow shell)
        {
            shell.Navigate("dashboard", date);
        }
    }
}
