using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Notifications;

/// <summary>Due-reminder evaluation of <see cref="ReminderScheduler"/>.</summary>
public sealed partial class ReminderScheduler
{
    /// <inheritdoc />
    public async Task ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);

        if (preferences is { RemindersEnabled: false })
        {
            _nextDueAt = null;
            return;
        }

        var now = _clock.UtcNow;
        var today = _clock.Today;

        var reminders = await _reminders.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        var meals = await _entries.GetForDateAsync(profile.Id, today, cancellationToken).ConfigureAwait(false);
        DateTimeOffset? soonest = null;

        foreach (var reminder in reminders)
        {
            if (!reminder.Enabled || !reminder.AppliesTo(today.DayOfWeek))
            {
                continue;
            }

            if (_snoozes.TryGetValue(reminder.Id, out var snoozeUntil) && snoozeUntil > now)
            {
                soonest = soonest is null || snoozeUntil < soonest ? snoozeUntil : soonest;
                continue;
            }

            var entry = meals.FirstOrDefault(m => m.MealCategoryKey == reminder.MealCategoryKey);
            if (entry is null || entry.Status != MealStatus.Pending)
            {
                // Decided meals - eaten or explicitly missed - never remind again.
                continue;
            }

            var due = ToInstant(today, entry.PlannedTime ?? reminder.Time);

            if (due > now)
            {
                soonest = soonest is null || due < soonest ? due : soonest;
                continue;
            }

            if (now - due > StaleReminderWindow)
            {
                // The PC was off or the application was closed: the stale reminder is dropped instead
                // of being shown hours late.
                continue;
            }

            // The ledger guarantees the notification is raised at most once per slot and date.
            var claimed = await _reminders.TryRecordFiredAsync(reminder.Id, today, cancellationToken).ConfigureAwait(false);
            if (!claimed)
            {
                continue;
            }

            var category = await _categories.GetByKeyAsync(reminder.MealCategoryKey, cancellationToken).ConfigureAwait(false);
            var title = string.IsNullOrWhiteSpace(entry.Title)
                ? category?.Name ?? reminder.MealCategoryKey.ToString()
                : entry.Title;

            await _notifications.ShowMealReminderAsync(
                new MealReminderNotification
                {
                    MealEntryId = entry.Id,
                    ReminderId = reminder.Id,
                    MealCategoryKey = reminder.MealCategoryKey,
                    Date = today,
                    MealTitle = title,
                    Body = string.IsNullOrWhiteSpace(entry.Ingredients) ? category?.Name ?? string.Empty : entry.Ingredients,
                    AllowSnooze = reminder.SnoozeMinutes is > 0,
                    SnoozeMinutes = reminder.SnoozeMinutes ?? 0,
                },
                cancellationToken).ConfigureAwait(false);
        }

        _nextDueAt = soonest;
    }

    /// <summary>Converts a local date and time into an absolute instant, honouring DST.</summary>
    private DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var offset = _clock.TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
