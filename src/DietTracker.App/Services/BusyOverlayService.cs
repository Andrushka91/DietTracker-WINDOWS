namespace DietTracker.App.Services;

/// <summary>State of the window wide busy overlay.</summary>
/// <param name="IsBusy">True while the overlay covers the application.</param>
/// <param name="Message">Localized text shown under the spinner.</param>
/// <param name="BlocksClosing">
/// True while the window may not be closed: the operation writes data or replaces the account and
/// stopping it in the middle would leave the user without a clear answer.
/// </param>
public sealed record BusyOverlayState(bool IsBusy, string Message, bool BlocksClosing)
{
    public static BusyOverlayState Idle { get; } = new(false, string.Empty, false);
}

/// <summary>
/// Shows and hides the dark busy overlay of the main window, so every screen reports long running
/// cloud work in the same way: a spinner, a sentence about what is happening and - for critical
/// operations - a window that cannot be closed until the operation finished.
/// </summary>
/// <remarks>
/// The view models only provide localized text; the window owns the visual tree. Implementations must
/// be safe to call from the UI thread only (the view models run there).
/// </remarks>
public interface IBusyOverlayService
{
    /// <summary>Current state (idle when nothing is running).</summary>
    BusyOverlayState State { get; }

    /// <summary>Raised whenever the state changes; the main window listens to it.</summary>
    event EventHandler<BusyOverlayState>? StateChanged;

    /// <summary>Shows the overlay. Repeated calls update the message instead of stacking overlays.</summary>
    void Show(string message, bool blocksClosing = false);

    /// <summary>Replaces the message of a visible overlay (a stage of the same operation).</summary>
    void Update(string message);

    /// <summary>Hides the overlay.</summary>
    void Hide();
}

/// <summary>Single, application wide <see cref="IBusyOverlayService"/>.</summary>
public sealed class BusyOverlayService : IBusyOverlayService
{
    private readonly object _gate = new();
    private BusyOverlayState _state = BusyOverlayState.Idle;

    /// <inheritdoc />
    public BusyOverlayState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<BusyOverlayState>? StateChanged;

    /// <inheritdoc />
    public void Show(string message, bool blocksClosing = false)
        => Publish(new BusyOverlayState(true, message ?? string.Empty, blocksClosing));

    /// <inheritdoc />
    public void Update(string message)
    {
        BusyOverlayState next;
        lock (_gate)
        {
            // An update for an operation that already finished must not bring the overlay back.
            if (!_state.IsBusy)
            {
                return;
            }

            next = _state with { Message = message ?? string.Empty };
            _state = next;
        }

        StateChanged?.Invoke(this, next);
    }

    /// <inheritdoc />
    public void Hide() => Publish(BusyOverlayState.Idle);

    private void Publish(BusyOverlayState next)
    {
        lock (_gate)
        {
            _state = next;
        }

        StateChanged?.Invoke(this, next);
    }
}
