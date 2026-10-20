using DietTracker.App.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace DietTracker.App;

/// <summary>
/// Window sizing rules:
/// the window opens large enough for all five meal cards, can never be shrunk into uselessness and
/// can never be stretched beyond the space the content uses, so no unused empty area appears.
/// The maximum height follows the content of the current page (cards, trend chart, ...) and is
/// recomputed whenever the content or the window width changes.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Smallest useful width: two card columns plus the daily header.</summary>
    private const double MinWindowWidth = 760;

    /// <summary>Smallest useful height: the daily header plus at least one row of cards.</summary>
    private const double MinWindowHeight = 720;

    /// <summary>Widest useful width: page content (1440) + page padding (2 x 24) + navigation rail (48).</summary>
    private const double MaxWindowWidth = 1536;

    /// <summary>Opening width: all five cards in 3 + 2 rows.</summary>
    private const double InitialWindowWidth = 1440;

    /// <summary>Opening height: the daily header plus the two card rows.</summary>
    private const double InitialWindowHeight = 1080;

    private WindowSizeLimiter? _sizeLimiter;
    private FrameworkElement? _trackedContent;

    /// <summary>Opens the window at the "all cards visible" size and installs the size limits.</summary>
    private void InitializeWindowSize()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = WindowSizeLimiter.GetScaleForWindow(hwnd);
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        var width = (int)Math.Max(MinWindowWidth * scale, Math.Min(InitialWindowWidth * scale, workArea.Width));
        var height = (int)Math.Max(MinWindowHeight * scale, Math.Min(InitialWindowHeight * scale, workArea.Height));

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));

        _sizeLimiter = new WindowSizeLimiter(hwnd)
        {
            MinWidth = MinWindowWidth,
            MinHeight = MinWindowHeight,
            MaxWidth = MaxWindowWidth,
        };

        ContentFrame.Navigated += OnFrameNavigatedForSizing;
        UpdateMaxWindowSize();
    }

    private void OnFrameNavigatedForSizing(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Track the content of the new page: its measured height decides how tall the window may grow.
        if (_trackedContent is not null)
        {
            _trackedContent.SizeChanged -= OnTrackedContentSizeChanged;
            _trackedContent = null;
        }

        if (ContentFrame.Content is Page page && page.Content is ScrollViewer scroll && scroll.Content is FrameworkElement content)
        {
            _trackedContent = content;
            _trackedContent.SizeChanged += OnTrackedContentSizeChanged;
        }

        UpdateMaxWindowSize();
    }

    private void OnTrackedContentSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMaxWindowSize();

    /// <summary>
    /// Maximum window height = the height the current page actually needs (content + scroll padding)
    /// plus the window chrome (caption bar, navigation, status strip), measured from the live layout.
    /// </summary>
    private void UpdateMaxWindowSize()
    {
        if (_sizeLimiter is null)
        {
            return;
        }

        var maxHeight = double.PositiveInfinity;
        if (_trackedContent is not null
            && ContentFrame.Content is Page page
            && page.Content is ScrollViewer scroll)
        {
            var scale = WindowSizeLimiter.GetScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var chromeHeight = AppWindow.Size.Height / scale - scroll.ActualHeight;
            maxHeight = chromeHeight + scroll.Padding.Top + scroll.Padding.Bottom + _trackedContent.ActualHeight;
        }

        _sizeLimiter.MaxHeight = Math.Max(MinWindowHeight, maxHeight);
    }
}
