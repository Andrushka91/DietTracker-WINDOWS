using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DietTracker.App;

/// <summary>Status strip and notification deep links of the shell window.</summary>
public sealed partial class MainWindow
{
    /// <summary>
    /// How long the "everything is synchronized" confirmation stays on screen before it hides itself.
    /// Long enough to be read, short enough not to occupy the top of the window permanently.
    /// </summary>
    private static readonly TimeSpan StatusConfirmationLifetime = TimeSpan.FromSeconds(6);

    private DispatcherTimer? _statusTimer;

    /// <summary>True while the strip shows a confirmation that is allowed to fade away on its own.</summary>
    private bool _statusIsConfirmation;

    /// <summary>
    /// True while the pointer rests on the strip. WinUI has no "is this element hovered" query, so the
    /// pointer events of the InfoBar itself maintain it.
    /// </summary>
    private bool _statusPointerOver;

    private void OnSyncStateChanged(object? sender, SyncStateChangedEventArgs e)
    {
        // The engine raises the event from a background thread, so it is marshalled to the UI thread.
        DispatcherQueue.TryEnqueue(() =>
        {
            var (severity, message) = DescribeState(e.State);
            _statusIsConfirmation = severity == InfoBarSeverity.Success;
            StatusBar.Severity = severity;
            StatusBar.Message = message;
            StatusBar.IsOpen = e.State is not SyncState.NotConfigured;
            RestartStatusCountdown();

            // A cycle that actually moved data is also reported in the notification area, so the
            // outcome is visible even while the window is hidden in the tray.
            if (e.State is SyncState.Synced
                && e.LastResult is { } completed
                && completed.Uploaded + completed.Downloaded > 0)
            {
                _ = _trayIcon.ShowBalloonAsync(
                    _localization.Get("Sync_BalloonTitle"),
                    _localization.Format("Sync_BalloonDetail", completed.Uploaded, completed.Downloaded));
            }
        });
    }

    /// <summary>
    /// Re-arms the countdown of the confirmation strip. Only a success hides itself: offline, "sign in
    /// again" and error states stay on screen until they are resolved. The countdown is armed again on
    /// every cycle (so each finished synchronization shows its timestamp for a moment) and it waits
    /// while the pointer rests on the strip, so a message that is being read never vanishes.
    /// </summary>
    private void RestartStatusCountdown()
    {
        _statusTimer ??= CreateStatusTimer();
        _statusTimer.Stop();

        if (_statusIsConfirmation && !_statusPointerOver)
        {
            _statusTimer.Start();
        }
    }

    private DispatcherTimer CreateStatusTimer()
    {
        var timer = new DispatcherTimer { Interval = StatusConfirmationLifetime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            StatusBar.IsOpen = false;
        };
        return timer;
    }

    private void OnStatusBarPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _statusPointerOver = true;
        RestartStatusCountdown();
    }

    private void OnStatusBarPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _statusPointerOver = false;
        RestartStatusCountdown();
    }

    private (InfoBarSeverity Severity, string Message) DescribeState(SyncState state)
    {
        var lastSync = _syncEngine.LastSuccessfulSyncAt is { } timestamp
            ? _localization.Format("Sync_LastSuccess", timestamp.ToLocalTime().ToString("g"))
            : string.Empty;

        return state switch
        {
            SyncState.Syncing => (InfoBarSeverity.Informational, _localization.Get("Sync_Syncing")),
            SyncState.PendingChanges => (InfoBarSeverity.Informational, _localization.Get("Sync_PendingChanges")),
            SyncState.Offline => (InfoBarSeverity.Warning, _localization.Get("Sync_Offline")),
            SyncState.AuthenticationRequired => (InfoBarSeverity.Warning, _localization.Get("Sync_AuthenticationRequired")),
            SyncState.Error => (InfoBarSeverity.Error, _localization.Get("Sync_Error")),
            SyncState.Synced => (InfoBarSeverity.Success, lastSync),
            _ => (InfoBarSeverity.Informational, string.Empty),
        };
    }

    private void OnNotificationActivated(object? sender, NotificationActivatedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var mealEntryId = ReadGuid(e.Argument, "mealEntryId");
                if (mealEntryId is null)
                {
                    RestoreFromTray();
                    return;
                }

                RestoreFromTray();

                switch (e.Action)
                {
                    case "mark-done":
                        await _mealTracking.MarkDoneAsync(mealEntryId.Value);
                        break;

                    case "snooze":
                        var minutes = ReadInt(e.Argument, "minutes") ?? 10;
                        await _services.GetRequiredService<IReminderScheduler>()
                            .SnoozeAsync(mealEntryId.Value, TimeSpan.FromMinutes(minutes));
                        break;
                }

                Navigate("dashboard", mealEntryId);
            }
            catch (Exception)
            {
                // A malformed activation argument must never crash the application.
                RestoreFromTray();
            }
        });
    }

    private static Guid? ReadGuid(string arguments, string key)
        => Guid.TryParse(Infrastructure.Notifications.WindowsNotificationService.GetArgument(arguments, key), out var value)
            ? value
            : null;

    private static int? ReadInt(string arguments, string key)
        => int.TryParse(Infrastructure.Notifications.WindowsNotificationService.GetArgument(arguments, key), out var value)
            ? value
            : null;
}
