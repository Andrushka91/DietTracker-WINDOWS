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
/// The height is fitted to the current page when the window itself is resized (by the user or a
/// script); switching between the pages keeps the window size, it is never resized on navigation.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Smallest useful width: two card columns plus the daily header.</summary>
    private const double MinWindowWidth = 760;

    /// <summary>Smallest useful height: the daily header plus at least one row of cards.</summary>
    private const double MinWindowHeight = 720;

    /// <summary>Widest useful width: page content (1440) + page padding (2 x 24) + navigation rail (48).</summary>
    private const double MaxWindowWidth = 1536;

    /// <summary>Opening width: two card columns plus the navigation rail and the page padding.</summary>
    private const double InitialWindowWidth = 1440;

    /// <summary>Opening height: the daily header plus all three card rows.</summary>
    private const double InitialWindowHeight = 1080;

    private WindowSizeLimiter? _sizeLimiter;
    private FrameworkElement? _trackedContent;
    private bool _initialSizeAdjusted;

    /// <summary>
    /// Until this deadline the window may still be fitted to the freshly loading content (the cards
    /// appear in several steps after the first layout pass). Afterwards the window size is only
    /// touched when the window itself is resized - page switches never resize it.
    /// </summary>
    private DateTime _contentFitDeadline = DateTime.UtcNow.AddSeconds(5);

    /// <summary>Opens the window at the "all cards visible" size and installs the size limits.</summary>
    private void InitializeWindowSize()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = WindowSizeLimiter.GetScaleForWindow(hwnd);
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        ApplyInitialWindowSize(scale, workArea);

        _sizeLimiter = new WindowSizeLimiter(hwnd)
        {
            MinWidth = MinWindowWidth,
            MinHeight = MinWindowHeight,
            MaxWidth = MaxWindowWidth,
        };

        // Windows can report 96 DPI while the window is still being created, so the opening size may
        // come out too small on a scaled display (which clipped the right card column). Re-apply the
        // size once with the DPI the window actually has when it is first activated.
        Activated += OnFirstActivatedForSize;

        ContentFrame.Navigated += OnFrameNavigatedForSizing;

        // The fit height must also be re-applied when the window itself is resized from the outside
        // (a drag of a side, a script or a restore), so the area below the cards is never left empty
        // no matter how the window is resized: every size change is corrected back to the content.
        RootGrid.SizeChanged += (_, _) => QueueFitUpdate();

        QueueFitUpdate();
    }

    private void OnFirstActivatedForSize(object sender, WindowActivatedEventArgs e)
    {
        if (_initialSizeAdjusted)
        {
            return;
        }

        _initialSizeAdjusted = true;
        Activated -= OnFirstActivatedForSize;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = WindowSizeLimiter.GetScaleForWindow(hwnd);
        if (Math.Abs(scale - 1d) < 0.001)
        {
            return; // The initial guess used the same scale; nothing to correct.
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        ApplyInitialWindowSize(scale, workArea);
    }

    private void ApplyInitialWindowSize(double scale, RectInt32 workArea)
    {
        var width = (int)Math.Max(MinWindowWidth * scale, Math.Min(InitialWindowWidth * scale, workArea.Width));
        var height = (int)Math.Max(MinWindowHeight * scale, Math.Min(InitialWindowHeight * scale, workArea.Height));

        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(
            workArea.X + Math.Max(0, (workArea.Width - width) / 2),
            workArea.Y + Math.Max(0, (workArea.Height - height) / 2)));
    }

    private void OnFrameNavigatedForSizing(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        // Track the content of the new page so a later window resize can be fitted to it, but do NOT
        // resize the window here: switching between the pages (home, calendar, statistics, ...) must
        // keep the window size the user has, instead of jumping to each page's own content height.
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
    }

    private void OnTrackedContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // One-shot: only while the first page content is still loading may the window be refitted
        // (the cards appear step by step). Later changes and page switches keep the window size.
        if (DateTime.UtcNow < _contentFitDeadline)
        {
            QueueFitUpdate();
        }
    }

    /// <summary>
    /// The fit must be computed after the layout has settled: a SizeChanged handler runs inside the
    /// layout pass, where the viewport height is still stale, and a fit computed from it would be
    /// wrong (the window keeps the empty area below the content). Deferring to the dispatcher queue
    /// reads the fresh layout and converges after one correction.
    /// </summary>
    private void QueueFitUpdate() => DispatcherQueue.TryEnqueue(UpdateMaxWindowSize);

    /// <summary>
    /// Maximum window height = the height the current page actually needs (content + scroll padding)
    /// plus the window chrome (caption bar, navigation, status strip), measured from the live layout.
    /// The height is also clamped from below with the same value, so the window can neither be
    /// stretched nor shrunk away from the content height - the vertical scrollbar can never appear.
    /// This runs when the window itself is resized; page switches leave the window size untouched.
    /// </summary>
    private void UpdateMaxWindowSize()
    {
        if (_sizeLimiter is null)
        {
            return;
        }

        var neededHeight = double.PositiveInfinity;
        if (_trackedContent is not null
            && ContentFrame.Content is Page page
            && page.Content is ScrollViewer scroll)
        {
            // Right after a navigation the new page is not laid out yet (ActualHeight 0); deriving
            // the chrome from "window - scroll" would then report the whole window as chrome and
            // inflate the window by that amount on every navigation. Skip until it is measured.
            if (scroll.ActualHeight <= 0d || _trackedContent.ActualHeight <= 0d)
            {
                return;
            }

            var scale = WindowSizeLimiter.GetScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            var chromeHeight = AppWindow.Size.Height / scale - scroll.ActualHeight;
            // The desired height is the natural height of the content: ActualHeight would report the
            // viewport-stretched height, which grows with the window and would pin the window to
            // whatever height it currently has (the empty area below the content could never shrink).
            neededHeight = chromeHeight + scroll.Padding.Top + scroll.Padding.Bottom + _trackedContent.DesiredSize.Height;
        }

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var scaleForWindow = WindowSizeLimiter.GetScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var workAreaHeight = workArea.Height / scaleForWindow;

        // On a screen too small for the whole content the scrollbar must stay available, so the
        // fit height is capped by the work area before it becomes the window's vertical size.
        var fitHeight = Math.Min(Math.Max(MinWindowHeight, neededHeight), Math.Max(MinWindowHeight, workAreaHeight));

        if (double.IsFinite(neededHeight))
        {
            // Exactly as tall as the content: no empty area below, no scrollbar.
            _sizeLimiter.MinHeight = fitHeight;
            _sizeLimiter.MaxHeight = fitHeight;

            var targetHeight = (int)Math.Round(fitHeight * scaleForWindow);
            var currentWidth = AppWindow.Size.Width;
            // The resize must not run synchronously inside the layout pass: UpdateMaxWindowSize is
            // called from SizeChanged, which fires while XAML lays out, and a synchronous window
            // resize in that moment corrupts the visual tree (the content area stops rendering).
            DispatcherQueue.TryEnqueue(() =>
            {
                if (Math.Abs(AppWindow.Size.Height - targetHeight) > 1)
                {
                    AppWindow.Resize(new SizeInt32(currentWidth, targetHeight));
                }
            });
        }
        else
        {
            // No measurable content yet: fall back to the static limits.
            _sizeLimiter.MinHeight = MinWindowHeight;
            _sizeLimiter.MaxHeight = double.PositiveInfinity;
        }
    }
}
