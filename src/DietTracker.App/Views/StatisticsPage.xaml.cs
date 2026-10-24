using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Progress and statistics: daily, weekly and monthly completion plus the trend series.</summary>
public sealed partial class StatisticsPage : Page
{
    /// <summary>Bar lanes of the page: one behind the period indicators, one behind the trend charts.</summary>
    private const int LaneCount = 2;

    public StatisticsPage()
    {
        ViewModel = App.Services.GetRequiredService<StatisticsViewModel>();
        InitializeComponent();

        // Fill the panel height like the other screens: the two bar lanes share whatever height the
        // fixed content (headers, titles, labels, padding) leaves over, so the statistics always
        // reach the bottom of the viewport. The ScrollViewer handler runs inside the layout pass;
        // the other two cover window resizes and the (async) reloads of the data.
        ScrollHost.SizeChanged += (_, _) => UpdateLaneHeight();
        SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateLaneHeight);
        ViewModel.DataLoaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            // The items were just replaced: measure them before reading the content height.
            ScrollHost.UpdateLayout();
            UpdateLaneHeight();
        });
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public StatisticsViewModel ViewModel { get; }

    /// <summary>
    /// Gives the height the panel has left to the two bar lanes, so the statistics use the full
    /// height of the screen like the dashboard does. The lanes are the only part of the page that
    /// can grow: subtracting them from the measured content leaves exactly what is fixed, and the
    /// remainder divided by the lanes is the value that makes the content reach the bottom of the
    /// viewport. The result is stable - measured with the new value the content equals the
    /// viewport, so the next pass computes the same number and stops.
    /// </summary>
    private void UpdateLaneHeight()
    {
        if (ScrollHost.ActualHeight <= 0
            || ScrollHost.Content is not FrameworkElement content
            || ScrollHost.ActualHeight - ScrollHost.Padding.Top - ScrollHost.Padding.Bottom <= 0)
        {
            return; // Not laid out yet.
        }

        var viewport = ScrollHost.ActualHeight - ScrollHost.Padding.Top - ScrollHost.Padding.Bottom;
        var fixedHeight = content.DesiredSize.Height - (LaneCount * ViewModel.LaneHeight);
        var lane = Math.Max(0d, (viewport - fixedHeight) / LaneCount);

        // The half-pixel tolerance keeps the layout from oscillating on rounding noise.
        if (Math.Abs(ViewModel.LaneHeight - lane) > 0.5d)
        {
            ViewModel.LaneHeight = lane;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // This is an async void handler: an exception that escapes it terminates the process, so the
        // load is guarded - when the data cannot be read the page keeps its previous state instead.
        try
        {
            await ViewModel.InitializeAsync(e.Parameter);
        }
        catch (Exception ex)
        {
            App.Services.GetService<ILogger<StatisticsPage>>()
                ?.LogWarning(ex, "The statistics could not be loaded.");
        }
    }
}
