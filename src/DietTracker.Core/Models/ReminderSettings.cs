namespace DietTracker.Core.Models;

/// <summary>
/// Reminder configuration. A reminder can be attached to a permanent meal category
/// (applies to every day) which is the default in DietTracker.
/// </summary>
public sealed class ReminderSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid LocalProfileId { get; set; }

    public Guid MealCategoryId { get; set; }

    public Enums.MealCategoryKey MealCategoryKey { get; set; }

    public bool Enabled { get; set; }

    /// <summary>Local time of day at which the notification should be raised.</summary>
    public TimeOnly Time { get; set; }

    /// <summary>Bit mask of <see cref="DayOfWeek"/> values (Sunday = bit 0). All days = 127.</summary>
    public int Weekdays { get; set; } = AllDays;

    /// <summary>Snooze duration offered on the notification; <see langword="null"/> disables snoozing.</summary>
    public int? SnoozeMinutes { get; set; } = 10;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Enums.SyncStatus SyncStatus { get; set; } = Enums.SyncStatus.PendingUpload;

    /// <summary>Mask with every weekday enabled.</summary>
    public const int AllDays = 0b1111111;

    /// <summary>True when the reminder applies on the supplied day of week.</summary>
    public bool AppliesTo(DayOfWeek day) => (Weekdays & (1 << (int)day)) != 0;
}
