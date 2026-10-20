using DietTracker.Core.Interfaces;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DietTracker.Infrastructure.Notifications;

/// <summary>Informational notifications, cancellation and activation handling.</summary>
public sealed partial class WindowsNotificationService
{
    /// <inheritdoc />
    public async Task ShowInfoAsync(string title, string message, string? argument = null, CancellationToken cancellationToken = default)
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            var notification = new AppNotificationBuilder()
                .AddText(title)
                .AddText(message)
                .AddArgument("action", argument ?? "open-app")
                .BuildNotification();

            notification.Group = "diettracker";
            _manager.Show(notification);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An informational notification could not be shown.");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CancelMealReminderAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            // Removes pending and visible reminders for the slot; the group covers every date tag.
            await _manager.RemoveByGroupAsync(ReminderGroup).AsTask(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("Pending reminders for meal {MealEntryId} were cancelled.", mealEntryId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The pending notification could not be removed.");
        }
    }

    /// <inheritdoc />
    public async Task CancelAllAsync(CancellationToken cancellationToken = default)
    {
        if (_manager is null)
        {
            return;
        }

        try
        {
            await _manager.RemoveAllAsync().AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The notifications could not be cleared.");
        }
    }

    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        try
        {
            var arguments = args.Argument ?? string.Empty;
            Activated?.Invoke(this, new NotificationActivatedEventArgs
            {
                Argument = arguments,
                Action = GetArgument(arguments, "action"),
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The notification activation could not be handled.");
        }
    }

    /// <summary>Reads one key from the <c>key=value&amp;key=value</c> activation argument string.</summary>
    public static string? GetArgument(string arguments, string key)
    {
        foreach (var pair in arguments.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        return null;
    }
}
