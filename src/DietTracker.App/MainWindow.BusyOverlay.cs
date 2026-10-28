using DietTracker.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

namespace DietTracker.App;

/// <summary>
/// Window wide busy overlay: the dark scrim, the spinner and the sentence that explains a long running
/// cloud operation (signing in, signing out, synchronizing, restoring).
/// </summary>
/// <remarks>
/// While an operation that must not be interrupted is running, the window cannot be closed - neither
/// with the caption bar button nor from the notification area - and the overlay says so. A defensive
/// deadline (<see cref="BusyCloseGuard"/>) releases the block after two minutes, so a provider that
/// never answers can not trap the user in an application that refuses to close.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>Upper bound for the close block: a hung provider must not lock the application.</summary>
    private static readonly TimeSpan BusyCloseGuard = TimeSpan.FromMinutes(2);

    private static readonly TimeSpan BusyFadeIn = TimeSpan.FromMilliseconds(180);

    private static readonly TimeSpan BusyFadeOut = TimeSpan.FromMilliseconds(140);

    private IBusyOverlayService? _busyOverlay;
    private DispatcherQueueTimer? _busyHintTimer;
    private Storyboard? _busyStoryboard;
    private DateTimeOffset _busySince;
    private bool _busyBlocksClosing;
    private int _busyAnimation;

    /// <summary>Connects the overlay to the service the view models use.</summary>
    private void InitializeBusyOverlay()
    {
        _busyOverlay = _services.GetRequiredService<IBusyOverlayService>();
        _busyOverlay.StateChanged += OnBusyStateChanged;

        _busyHintTimer = DispatcherQueue.CreateTimer();
        _busyHintTimer.Interval = TimeSpan.FromSeconds(4);
        _busyHintTimer.IsRepeating = false;
        _busyHintTimer.Tick += (_, _) =>
        {
            if (BusyOverlay.Visibility == Visibility.Visible)
            {
                BusyHint.Text = _localization.Get("Busy_Hint");
            }
        };

        BusyHint.Text = _localization.Get("Busy_Hint");
    }

    /// <summary>
    /// True while the window may not be closed: a critical operation is running and stopping the
    /// application in the middle of it would leave the local cache in an unknown state.
    /// </summary>
    internal bool IsCloseBlocked => _busyBlocksClosing && DateTimeOffset.UtcNow - _busySince < BusyCloseGuard;

    private void OnBusyStateChanged(object? sender, BusyOverlayState state)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyBusyState(state);
            return;
        }

        DispatcherQueue.TryEnqueue(() => ApplyBusyState(state));
    }

    private void ApplyBusyState(BusyOverlayState state)
    {
        _busyBlocksClosing = state.IsBusy && state.BlocksClosing;
        if (state.IsBusy)
        {
            _busySince = DateTimeOffset.UtcNow;
        }

        // The navigation is disabled as well: a keyboard focus could otherwise still reach a control
        // behind the scrim, which would look like the overlay accepted input.
        Navigation.IsEnabled = !state.IsBusy;

        // Every state change invalidates a pending fade out, so a quick Hide/Show pair can not leave
        // the overlay collapsed while an operation is running.
        var animation = ++_busyAnimation;

        if (state.IsBusy)
        {
            BusyMessage.Text = state.Message;
            BusyHint.Text = _localization.Get("Busy_Hint");
            BusyRing.IsActive = true;
            BusyOverlay.Visibility = Visibility.Visible;
            FadeBusyOverlay(1d, BusyFadeIn, null);
            return;
        }

        BusyRing.IsActive = false;
        FadeBusyOverlay(0d, BusyFadeOut, () =>
        {
            if (animation == _busyAnimation)
            {
                BusyOverlay.Visibility = Visibility.Collapsed;
            }
        });
    }

    /// <summary>Fades the scrim (opacity is an independent animation, so no layout pass is involved).</summary>
    private void FadeBusyOverlay(double to, TimeSpan duration, Action? completed)
    {
        _busyStoryboard?.Stop();

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(duration),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };

        Storyboard.SetTarget(animation, BusyOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);

        if (completed is not null)
        {
            storyboard.Completed += (_, _) => completed();
        }

        // Kept in a field: an animation that is only referenced by the local variable could be
        // collected mid-flight, which would freeze the scrim half faded in.
        _busyStoryboard = storyboard;
        storyboard.Begin();
    }

    /// <summary>Explains why the window does not close while an operation is running.</summary>
    private void ShowCloseBlockedHint()
    {
        if (BusyOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        BusyHint.Text = _localization.Get("Busy_CloseBlocked");
        _busyHintTimer?.Stop();
        _busyHintTimer?.Start();
    }
}
