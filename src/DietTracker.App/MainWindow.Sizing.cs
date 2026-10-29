using DietTracker.App.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Graphics;

namespace DietTracker.App;

/// <summary>
/// Window sizing rules: the window opens as tall as the dashboard needs (all five meal cards, no
/// vertical scrollbar), keeps that height for the whole session and can never be shrunk into
/// uselessness or stretched beyond the useful width.
/// </summary>
/// <remarks>
/// The height is fitted <b>once</b>, while the first page is still loading, and then frozen (minimum
/// and maximum height become the same value). Switching between the pages never resizes the window: a
/// short page (the settings page, for example) must not pull it down and a long page must not push it
/// up. The fit is therefore restricted to the dashboard and stops for good once the content settled.
/// Three things make that a guarantee rather than an intention: the freeze runs when the first fit
/// measurement exists and the user leaves the dashboard (so the single possible correction happens
/// before they can work on another page), an unconditional deadline timer freezes the height at the
/// latest <see cref="InitialFitTimeout"/> after start-up (the fit cannot be skipped by a navigation),
/// and <see cref="ResizeWindowHeight"/> refuses to move the height once it is frozen - the minimum
/// and maximum being equal, Windows clamps every further height change, from the user or from code.
/// </remarks>
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

    /// <summary>Give the first page this long to finish loading before the height is frozen anyway.</summary>
    private static readonly TimeSpan InitialFitTimeout = TimeSpan.FromSeconds(8);

    /// <summary>How long the measured height must stay unchanged before it is considered final.</summary>
    private static readonly TimeSpan InitialFitSettleTime = TimeSpan.FromMilliseconds(1200);

    private WindowSizeLimiter? _sizeLimiter;
    private bool _initialSizeAdjusted;

    /// <summary>True once the height has been fitted to the dashboard; from then on it never changes.</summary>
    private bool _heightFixed;

    /// <summary>Content of the dashboard page the opening height is derived from.</summary>
    private FrameworkElement? _firstPageContent;

    private DispatcherQueueTimer? _fitSettleTimer;

    /// <summary>Unconditional backstop: freezes the height at the latest this long after start-up.</summary>
    private DispatcherQueueTimer? _fitDeadlineTimer;

    private DateTimeOffset _fitDeadline;

    /// <summary>Last measured height of the dashboard content (DIP), used when the height is frozen.</summary>
    private double _fittedHeight;

    /// <summary>Opens the window at the "all cards visible" size and installs the size limits.</summary>
    private void InitializeWindowSize()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var scale = WindowSizeLimiter.GetScaleForWindow(hwnd);
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

        ApplyInitialWindowSize(scale, workArea);

        // Minimum height/width keep the layout usable; the maximum width stops the content from
        // stretching over an unused area. The height limits are refined once, right below.
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

        // The dashboard cards are loaded asynchronously, so the opening height is measured while the
        // first page is still filling up and frozen as soon as its height stops changing.
        _fitDeadline = DateTimeOffset.UtcNow + InitialFitTimeout;
        _fitSettleTimer = DispatcherQueue.CreateTimer();
        _fitSettleTimer.Interval = InitialFitSettleTime;
        _fitSettleTimer.IsRepeating = false;
        _fitSettleTimer.Tick += (_, _) => FreezeWindowHeight();

        // Backstop: whatever happens to the fit - a navigation in the first second, content that
        // never settles, a page that is not the dashboard - the height is frozen when this fires.
        // Without it a navigation away from the dashboard can leave the fit unrunnable (the deadline
        // check lives inside the fit, which returns early), the height would stay flexible and a
        // later correction could still move the window while the user works on another page.
        _fitDeadlineTimer = DispatcherQueue.CreateTimer();
        _fitDeadlineTimer.Interval = InitialFitTimeout;
        _fitDeadlineTimer.IsRepeating = false;
        _fitDeadlineTimer.Tick += (_, _) => FreezeWindowHeight();
        _fitDeadlineTimer.Start();

        ContentFrame.Navigated += OnFrameNavigatedForSizing;
        RootGrid.SizeChanged += OnRootGridSizeChangedForSizing;

        QueueInitialFit();
    }

    private void OnRootGridSizeChangedForSizing(object sender, SizeChangedEventArgs e)
    {
        // Only until the height is final: afterwards nothing may move the window - not a page switch,
        // not a resize, and certainly not this handler.
        if (!_heightFixed)
        {
            QueueInitialFit();
        }
    }

    private void OnFrameNavigatedForSizing(object sender, NavigationEventArgs e)
    {
        // A non-null _firstPageContent means the dashboard was the page being left.
        var leavingDashboard = _firstPageContent is not null;
        if (_firstPageContent is not null)
        {
            _firstPageContent.SizeChanged -= OnFirstPageContentSizeChanged;
            _firstPageContent = null;
        }

        // Leaving the dashboard ends the fit: the page is unloaded, so its measured height cannot
        // change any more and the last value is exactly what the settle timer would have frozen a
        // moment later. Freezing here moves that single possible correction to a moment when the
        // user has not started working on the new page yet - never while they are using it.
        if (!_heightFixed && leavingDashboard && _fittedHeight > 0d)
        {
            FreezeWindowHeight();
        }

        // Only the dashboard defines the height. A short page (account, settings) or a long one must
        // never be able to change it, so their content is not tracked at all.
        if (_heightFixed
            || ContentFrame.CurrentSourcePageType != typeof(Views.DashboardPage)
            || ContentFrame.Content is not Page page
            || page.Content is not ScrollViewer scroll
            || scroll.Content is not FrameworkElement content)
        {
            return;
        }

        _firstPageContent = content;
        _firstPageContent.SizeChanged += OnFirstPageContentSizeChanged;

        QueueInitialFit();
    }

    private void OnFirstPageContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_heightFixed)
        {
            QueueInitialFit();
        }
    }

    /// <summary>
    /// The fit must be computed after the layout has settled: a SizeChanged handler runs inside the
    /// layout pass, where the viewport height is still stale. Deferring to the dispatcher queue reads
    /// the fresh layout and converges after one correction.
    /// </summary>
    private void QueueInitialFit() => DispatcherQueue.TryEnqueue(UpdateInitialWindowHeight);

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

    /// <summary>
    /// Fits the height to the dashboard content while the first page is loading. Runs only while the
    /// window is not frozen, only on the dashboard and only with a measured layout.
    /// </summary>
    private void UpdateInitialWindowHeight()
    {
        if (_heightFixed || _sizeLimiter is null)
        {
            return;
        }

        // The deadline comes before every "cannot measure right now" answer below: a page that is
        // not the dashboard or a content that is not measured yet must not be able to postpone the
        // freeze past the deadline (the unconditional timer in InitializeWindowSize is the second
        // safety net for the case where this method is never called at all).
        if (DateTimeOffset.UtcNow > _fitDeadline)
        {
            FreezeWindowHeight();
            return;
        }

        if (_firstPageContent is null)
        {
            return;
        }

        if (ContentFrame.CurrentSourcePageType != typeof(Views.DashboardPage)
            || ContentFrame.Content is not Page page
            || page.Content is not ScrollViewer scroll)
        {
            return;
        }

        if (_firstPageContent.ActualHeight <= 0d || scroll.ActualHeight <= 0d)
        {
            return; // Not measured yet; the next layout pass tries again.
        }

        var scale = WindowSizeLimiter.GetScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));

        // Chrome = everything that is not the page viewport (caption bar, status strip, navigation).
        var chromeHeight = AppWindow.Size.Height / scale - scroll.ActualHeight;

        // The natural height of the content, not ActualHeight (which is stretched to the viewport and
        // would keep the window at whatever height it currently has).
        var neededHeight = chromeHeight + scroll.Padding.Top + scroll.Padding.Bottom + _firstPageContent.DesiredSize.Height;

        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var workAreaHeight = workArea.Height / scale;
        var fitHeight = Math.Min(Math.Max(MinWindowHeight, neededHeight), Math.Max(MinWindowHeight, workAreaHeight));
        _fittedHeight = fitHeight;

        // While the page is still loading the window may only grow: shrinking to a half loaded layout
        // (the cards are added one by one) would make the window jump on every start. The exact height
        // is applied once, when it is frozen.
        if (fitHeight > AppWindow.Size.Height / scale + 0.5d)
        {
            ResizeWindowHeight(fitHeight, scale);
        }

        // The cards arrive in several steps: keep following the content until it stops changing, then
        // freeze. A timer (not a fixed delay) makes sure the last step is included.
        _fitSettleTimer?.Stop();
        _fitSettleTimer?.Start();
    }

    /// <summary>Fixes the fitted height as the window's only height for the rest of the session.</summary>
    private void FreezeWindowHeight()
    {
        if (_heightFixed || _sizeLimiter is null)
        {
            return;
        }

        _fitSettleTimer?.Stop();
        _fitDeadlineTimer?.Stop();
        _heightFixed = true;

        var scale = WindowSizeLimiter.GetScaleForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        var targetHeight = Math.Max(MinWindowHeight, _fittedHeight > 0d ? _fittedHeight : AppWindow.Size.Height / scale);

        // Minimum = maximum: neither the user nor a page can resize the height from now on.
        _sizeLimiter.MinHeight = targetHeight;
        _sizeLimiter.MaxHeight = targetHeight;

        // The one and only correction of the final measurement (grow or shrink), never again after
        // this: it goes straight to the window instead of through ResizeWindowHeight, which refuses
        // to move a height that is already frozen.
        ApplyWindowHeight(targetHeight, scale);
    }

    /// <summary>
    /// Resizes the window height through the dispatcher (never inside a layout pass). Refuses to run
    /// once the height is frozen, so no later fit, page or content change can move the window again.
    /// </summary>
    private void ResizeWindowHeight(double height, double scale)
    {
        if (_heightFixed)
        {
            return;
        }

        ApplyWindowHeight(height, scale);
    }

    private void ApplyWindowHeight(double height, double scale)
    {
        var targetHeight = (int)Math.Round(height * scale);
        if (Math.Abs(AppWindow.Size.Height - targetHeight) <= 1)
        {
            return;
        }

        var width = AppWindow.Size.Width;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (Math.Abs(AppWindow.Size.Height - targetHeight) > 1)
            {
                AppWindow.Resize(new SizeInt32(width, targetHeight));
            }
        });
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

}
