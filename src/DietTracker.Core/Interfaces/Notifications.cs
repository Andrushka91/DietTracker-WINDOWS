using DietTracker.Core.Enums;

namespace DietTracker.Core.Interfaces;

/// <summary>Raises native Windows notifications and reacts to user interaction with them.</summary>
public interface INotificationService
{
    /// <summary>Initializes the notification subsystem and registers the application when needed.</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Raises a meal reminder notification for a specific date and slot.</summary>
    Task ShowMealReminderAsync(MealReminderNotification notification, CancellationToken cancellationToken = default);

    /// <summary>Raises an informational notification (synchronization result, error, restore finished).</summary>
    Task ShowInfoAsync(string title, string message, string? argument = null, CancellationToken cancellationToken = default);

    /// <summary>Removes pending/visible notifications that belong to one meal slot on one date.</summary>
    Task CancelMealReminderAsync(Guid mealEntryId, CancellationToken cancellationToken = default);

    /// <summary>Removes every notification raised by DietTracker.</summary>
    Task CancelAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Raised when the user clicks a notification (the argument identifies the target).</summary>
    event EventHandler<NotificationActivatedEventArgs>? Activated;
}

/// <summary>Content of a meal reminder notification.</summary>
public sealed record MealReminderNotification
{
    public required Guid MealEntryId { get; init; }

    public required Guid ReminderId { get; init; }

    public required MealCategoryKey MealCategoryKey { get; init; }

    public required DateOnly Date { get; init; }

    /// <summary>Localized category name or the user supplied meal title.</summary>
    public string MealTitle { get; init; } = string.Empty;

    public string Body { get; init; } = string.Empty;

    public bool AllowSnooze { get; init; } = true;

    public int SnoozeMinutes { get; init; } = 10;
}

/// <summary>Arguments of a notification activation (deep link back into the UI).</summary>
public sealed record NotificationActivatedEventArgs
{
    public required string Argument { get; init; }

    public string? Action { get; init; }
}

/// <summary>
/// Computes and processes due reminders. Implementations are event driven (no busy polling) and
/// must survive application restarts, time zone changes, DST shifts, resume from sleep, and must
/// never raise the same reminder twice for the same meal slot and date.
/// </summary>
public interface IReminderScheduler
{
    /// <summary>Loads reminder configuration, recomputes due instants and starts the scheduler.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Recomputes schedules (settings changed, time zone changed, resumed from sleep).</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>Next instant a reminder is due, or <see langword="null"/> when nothing is scheduled.</summary>
    DateTimeOffset? NextDueAt { get; }

    /// <summary>Snoozes a reminder for a meal slot by the supplied duration.</summary>
    Task SnoozeAsync(Guid mealEntryId, TimeSpan duration, CancellationToken cancellationToken = default);

    /// <summary>Processes all reminders that are due right now. Called by the timer and on resume.</summary>
    Task ProcessDueAsync(CancellationToken cancellationToken = default);
}
