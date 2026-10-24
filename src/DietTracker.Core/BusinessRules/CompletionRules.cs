using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.BusinessRules;

/// <summary>Outcome of a state transition.</summary>
/// <param name="Changed">True when the entry was actually modified (false for repeated no-op calls).</param>
/// <param name="Reason">Stable reason code for the caller to localize if needed.</param>
public readonly record struct CompletionOutcome(bool Changed, string Reason)
{
    public static CompletionOutcome NoChange(string reason) => new(false, reason);

    public static CompletionOutcome Done() => new(true, "completed");

    public static CompletionOutcome Missed() => new(true, "marked-missed");
}

/// <summary>
/// Rules that govern the PENDING / DONE / MISSED lifecycle of a meal slot.
/// </summary>
/// <remarks>
/// <para>
/// The three states are always set by the user, never inferred: a meal becomes DONE when the green
/// check is pressed and MISSED when the red cross is pressed. Time alone never reclassifies a
/// meal - a day the user skipped stays pending forever, which keeps a forgotten day honest instead
/// of painting it red retroactively.
/// </para>
/// <para>
/// The date decides whether a change is allowed at all. Today and past dates are fully editable
/// (the user may come back to record what happened), while a future meal is locked in PENDING: it
/// can be neither confirmed nor marked missed, no matter how often the buttons are pressed.
/// </para>
/// </remarks>
public static class CompletionRules
{
    /// <summary>
    /// True when the meal's calendar date lies beyond today, which locks the meal in PENDING.
    /// <paramref name="now"/> is the local wall-clock time of the user.
    /// </summary>
    public static bool IsFutureMeal(MealEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Date > DateOnly.FromDateTime(now.DateTime);
    }

    /// <summary>
    /// Marks the meal as DONE and records the completion timestamp. Allowed for today and past
    /// dates only; a future meal stays pending. Calling this repeatedly is idempotent: the original
    /// timestamp is preserved so that statistics are never incremented twice.
    /// </summary>
    public static CompletionOutcome MarkDone(MealEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (IsFutureMeal(entry, now))
        {
            return CompletionOutcome.NoChange("future-locked");
        }

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

    /// <summary>
    /// Marks the meal as MISSED (the red state) and clears the completion timestamp. Allowed for
    /// today and past dates only; a future meal stays pending. MISSED is sticky: pressing the cross
    /// again on an already missed meal does nothing, it never cycles back to pending.
    /// </summary>
    public static CompletionOutcome MarkMissed(MealEntry entry, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (IsFutureMeal(entry, now))
        {
            return CompletionOutcome.NoChange("future-locked");
        }

        if (entry.Status == MealStatus.Missed)
        {
            return CompletionOutcome.NoChange("already-missed");
        }

        entry.Status = MealStatus.Missed;
        entry.CompletedAt = null;
        entry.UpdatedAt = now;
        entry.Revision++;
        return CompletionOutcome.Missed();
    }

    /// <summary>
    /// True when the user is allowed to attach or change content of the meal. Editing a meal that
    /// is already DONE never clears its completion state automatically. Content may also be planned
    /// ahead on future dates - only the state buttons are locked there.
    /// </summary>
    public static bool CanEdit(MealEntry entry) => entry is not null;
}
