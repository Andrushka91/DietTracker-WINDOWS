using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.BusinessRules;

/// <summary>Outcome of a completion transition.</summary>
/// <param name="Changed">True when the entry was actually modified (false for repeated no-op calls).</param>
/// <param name="Reason">Stable reason code for the caller to localize if needed.</param>
public readonly record struct CompletionOutcome(bool Changed, string Reason)
{
    public static CompletionOutcome NoChange(string reason) => new(false, reason);

    public static CompletionOutcome Done() => new(true, "completed");
}

/// <summary>
/// Rules that govern the PENDING / DONE / MISSED lifecycle of a meal slot.
/// </summary>
/// <remarks>
/// DietTracker never infers completion: only the user marks a meal as DONE or MISSED.
/// Completion is always bound to a single calendar date, and future dates can never be
/// classified as missed.
/// </remarks>
public static class CompletionRules
{
    /// <summary>
    /// Marks the meal as DONE and records the completion timestamp.
    /// Calling this repeatedly is idempotent: the original timestamp is preserved so that
    /// statistics are never incremented twice.
    /// </summary>
    public static CompletionOutcome MarkDone(MealEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Status == MealStatus.Done)
        {
            return CompletionOutcome.NoChange("already-done");
        }

        entry.Status = MealStatus.Done;
        entry.CompletedAt = now;
        entry.UpdatedAt = now;
        entry.Revision++;
        return CompletionOutcome.Done();
    }

    /// <summary>Reverts a completed or missed meal back to PENDING and clears the completion timestamp.</summary>
    public static CompletionOutcome UndoToPending(MealEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Status == MealStatus.Pending)
        {
            return CompletionOutcome.NoChange("already-pending");
        }

        entry.Status = MealStatus.Pending;
        entry.CompletedAt = null;
        entry.UpdatedAt = now;
        entry.Revision++;
        return new CompletionOutcome(true, "reverted-to-pending");
    }

    /// <summary>
    /// Marks the meal as MISSED. Only allowed for the current or a past date; future scheduled
    /// meals are never classified as missed.
    /// </summary>
    public static CompletionOutcome MarkMissed(MealEntry entry, DateOnly today, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Date > today)
        {
            return CompletionOutcome.NoChange("future-date-cannot-be-missed");
        }

        if (entry.Status == MealStatus.Missed)
        {
            return CompletionOutcome.NoChange("already-missed");
        }

        entry.Status = MealStatus.Missed;
        entry.CompletedAt = null;
        entry.UpdatedAt = now;
        entry.Revision++;
        return new CompletionOutcome(true, "marked-missed");
    }

    /// <summary>
    /// Decides whether a pending slot of a past date should be reported as missed by the
    /// "historical day" rule (the day is over and the meal was never confirmed).
    /// The rule is never applied to today or future dates and never mutates the record.
    /// </summary>
    public static bool IsHistoricallyMissed(MealEntry entry, DateOnly today, TimeOnly nowTime, TimeSpan grace)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Status != MealStatus.Pending || entry.Date >= today)
        {
            return false;
        }

        if (entry.PlannedTime is { } planned)
        {
            var plannedInstant = entry.Date.ToDateTime(planned);
            var threshold = today.ToDateTime(nowTime) - grace;
            return plannedInstant > threshold;
        }

        return true;
    }

    /// <summary>Effective status shown in the UI, taking the historical rule into account.</summary>
    public static MealStatus EffectiveStatus(MealEntry entry, DateOnly today, TimeOnly nowTime, TimeSpan? grace = null)
        => IsHistoricallyMissed(entry, today, nowTime, grace ?? TimeSpan.FromHours(12))
            ? MealStatus.Missed
            : entry.Status;

    /// <summary>
    /// True when the user is allowed to attach or change content of the meal. Editing a meal that
    /// is already DONE never clears its completion state automatically.
    /// </summary>
    public static bool CanEdit(MealEntry entry) => entry is not null;
}
