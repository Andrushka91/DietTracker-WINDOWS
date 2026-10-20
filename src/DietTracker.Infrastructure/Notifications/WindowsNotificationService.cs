using DietTracker.Core.Interfaces;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace DietTracker.Infrastructure.Notifications;

/// <summary>
/// Raises native Windows notifications through the Windows App SDK notification APIs.
/// </summary>
/// <remarks>
/// Notifications are raised by the application process (not by a UI timer), carry a group/tag per meal
/// slot so they can be removed again after the meal is completed, and deep link back into the
/// application when the user clicks them.
/// </remarks>
public sealed partial class WindowsNotificationService : INotificationService
{
    private readonly ILogger<WindowsNotificationService> _logger;
    private AppNotificationManager? _manager;
    private bool _registered;

    public WindowsNotificationService(ILogger<WindowsNotificationService> logger) => _logger = logger;

    /// <inheritdoc />
    public event EventHandler<NotificationActivatedEventArgs>? Activated;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_registered)
        {
            return Task.CompletedTask;
        }

        try
        {
            _manager = AppNotificationManager.Default;
            _manager.NotificationInvoked += OnNotificationInvoked;

            // Register() is required for unpackaged and self-hosted packaged applications. It is
            // idempotent, and a failure (for example a missing shortcut) must never block startup.
            _manager.Register();
            _registered = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Native notifications are not available in this environment.");
            _manager = null;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ShowMealReminderAsync(MealReminderNotification notification, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (_manager is null)
        {
            _logger.LogInformation(
                "The reminder for {Meal} on {Date} was not shown because notifications are unavailable.",
                notification.MealCategoryKey, notification.Date);
            return Task.CompletedTask;
        }

        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(notification.MealTitle)
                .AddText(notification.Body)
                .AddArgument("action", "open-meal")
                .AddArgument("mealEntryId", notification.MealEntryId.ToString("D"))
                .AddArgument("date", notification.Date.ToString("yyyy-MM-dd"))
                .AddArgument("categoryKey", ((int)notification.MealCategoryKey).ToString());

            if (notification.AllowSnooze && notification.SnoozeMinutes > 0)
            {
                builder = builder
                    .AddButton(new AppNotificationButton("Snooze")
                        .AddArgument("action", "snooze")
                        .AddArgument("mealEntryId", notification.MealEntryId.ToString("D"))
                        .AddArgument("minutes", notification.SnoozeMinutes.ToString()))
                    .AddButton(new AppNotificationButton("Mark as DONE")
                        .AddArgument("action", "mark-done")
                        .AddArgument("mealEntryId", notification.MealEntryId.ToString("D")));
            }

            var appNotification = builder.BuildNotification();
            appNotification.Tag = GetTag(notification.MealEntryId, notification.Date);
            appNotification.Group = ReminderGroup;

            _manager.Show(appNotification);
        }
        catch (Exception ex)
        {
            // A failed notification must never interrupt meal tracking.
            _logger.LogWarning(ex, "The reminder notification could not be shown.");
        }

        return Task.CompletedTask;
    }

    /// <summary>Notification group used for every meal reminder.</summary>
    internal const string ReminderGroup = "meal-reminders";

    /// <summary>Builds the notification tag that identifies a meal slot on a date.</summary>
    internal static string GetTag(Guid mealEntryId, DateOnly date) => $"meal-{mealEntryId:D}-{date:yyyyMMdd}";
}
