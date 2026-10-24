using DietTracker.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace DietTracker.App.Views;

/// <summary>Daily dashboard: the five permanent meal cards and the daily progress.</summary>
public sealed partial class DashboardPage : Page
{
    public DashboardPage()
    {
        ViewModel = App.Services.GetRequiredService<DashboardViewModel>();
        InitializeComponent();

        // Cap the content stack to the page width minus the ScrollViewer's horizontal padding
        // (2 x 16, the same 16 as the gap between the cards, so the layout is symmetrical).
        // x:Bind on ActualWidth never updates after the first (zero) value, so the cap
        // is maintained from the SizeChanged event instead; without it the card grid overflows or,
        // at the very first layout pass, collapses to a zero-width column.
        SizeChanged += (_, e) => ContentRoot.MaxWidth = SnapWidth(e.NewSize.Width - 32d);
    }

    /// <summary>View model of the page (exposed for <c>x:Bind</c>).</summary>
    public DashboardViewModel ViewModel { get; }

    /// <summary>
    /// Rounds the content cap down to a whole, even number of device pixels. At a fractional display
    /// scale (125 %, 150 %) a raw cap like 1344.8 leaves the right edge of the grid exactly on the
    /// ScrollViewer's clip boundary, where the outermost pixel of the right column's border is cut
    /// away - the right border of the morning and afternoon snack cards disappeared that way. With
    /// an even pixel count the two columns land on whole pixels as well, so every card border is
    /// drawn crisp instead of straddling two pixels.
    /// </summary>
    private double SnapWidth(double width)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1d;
        if (!double.IsFinite(scale) || scale <= 0d)
        {
            return Math.Max(0d, width);
        }

        var pixels = Math.Floor(width * scale);
        if (pixels < 2d)
        {
            return 0d;
        }

        // Even: subtracting the fixed 16 px gap (a whole pixel count at every scale) keeps the
        // column width a whole pixel count too.
        pixels -= pixels % 2d;
        return pixels / scale;
    }

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
            App.Services.GetService<ILogger<DashboardPage>>()
                ?.LogWarning(ex, "The dashboard could not be loaded.");
        }
    }
}
