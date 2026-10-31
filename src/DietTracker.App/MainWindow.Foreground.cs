using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using Microsoft.UI.Xaml;

namespace DietTracker.App;

/// <summary>
/// Foreground awareness of the shell window.
/// </summary>
/// <remarks>
/// The other device's changes are only worth fetching while the window is actually being looked at,
/// so the automatic download loop in <see cref="App"/> asks this property before it knocks. Coming
/// back to the window is also the moment the user expects the newest data, which is why regaining
/// focus requests a download immediately instead of waiting for the next tick.
/// </remarks>
public sealed partial class MainWindow
{
    /// <summary>True while the window has the user's focus (and is not hidden in the tray).</summary>
    public bool IsForeground { get; private set; }

    private void OnWindowActivated(object sender, WindowActivatedEventArgs e)
    {
        var active = e.WindowActivationState != WindowActivationState.Deactivated;
        if (active == IsForeground)
        {
            return;
        }

        IsForeground = active;
        if (active)
        {
            _syncEngine.RequestSync(SyncTrigger.AutoPull);
        }
    }
}
