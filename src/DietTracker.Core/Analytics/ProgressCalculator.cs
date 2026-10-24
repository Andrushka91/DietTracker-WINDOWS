using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Analytics;

/// <summary>
/// Computes diet progress and adherence metrics.
/// </summary>
/// <remarks>
/// <para>
/// The three states are stored, never inferred: a slot starts PENDING in the database, becomes
/// DONE when the user confirms it and MISSED when the user explicitly marks it with the cross.
/// Time alone never changes a state - a day the user skipped reports pending, not missed, and only
/// the user's own decision moves a slot out of pending.
/// </para>
/// <para>
/// Only the decided meals take part in a ratio: confirmed plus missed. Slots that are still
/// pending are excluded from every percentage - so an untouched day reports no value instead of
/// dragging a period down to 0 %.
/// </para>
/// <para>
/// Daily completion = confirmed / (confirmed + missed) * 100, and zero while nothing was decided.
/// The figures reflect what the user recorded, they are not medically verified dietary compliance;
/// the API exposes raw numbers only so the UI can present them without punitive wording.
/// </para>
/// </remarks>
public static class ProgressCalculator
{
    /// <summary>
    /// Builds the progress of a single date from the meal entries of that date.
    /// </summary>
    /// <param name="date">The date the entries belong to.</param>
    /// <param name="entries">Stored entries of that date; missing slots count as untouched.</param>
    public static DayProgress ForDay(DateOnly date, IEnumerable<MealEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries as IReadOnlyList<MealEntry> ?? entries.ToArray();
        var slots = new List<MealSlotStatus>();
        var completed = 0;
        var missed = 0;
        var pending = 0;

        foreach (var key in MealSlotRules.AllSlots)
        {
            var entry = materialized.FirstOrDefault(e => e.MealCategoryKey == key);
            var status = entry?.Status ?? MealStatus.Pending;

            if (status == MealStatus.Done)
            {
                completed++;
            }
            else if (status == MealStatus.Missed)
            {
                missed++;
            }
            else
            {
                pending++;
            }

            slots.Add(new MealSlotStatus
            {
                MealCategoryKey = key,
                Status = status,
                Title = entry?.Title ?? string.Empty,
                PlannedTime = entry?.PlannedTime,
                CompletedAt = entry?.CompletedAt,
                PhotoCount = entry?.Photos.Count ?? 0,
            });
        }

        return new DayProgress
        {
            Date = date,
            TotalSlots = MealSlotRules.SlotCount,
            Completed = completed,
            Missed = missed,
            Pending = pending,
            Slots = slots,
        };
    }

    /// <summary>
    /// Builds the completion percentage of a single date: the share of the decided meals that were
    /// confirmed. Zero when nothing was decided yet.
    /// </summary>
    public static double PercentageForDay(IEnumerable<MealEntry> entries, DateOnly date)
        => ForDay(date, entries).Percentage;
}
