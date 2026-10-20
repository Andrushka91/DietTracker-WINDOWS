using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Analytics;

/// <summary>
/// Computes diet progress and adherence metrics.
/// </summary>
/// <remarks>
/// Daily completion = completed meals / total meal slots * 100. The denominator is always the
/// number of permanent slots (five), so an empty day is 0 % - never 100 %. The figures reflect
/// what the user recorded; they are not medically verified dietary compliance. The API exposes
/// raw numbers only so the UI can present them without punitive wording.
/// </remarks>
public static class ProgressCalculator
{
    /// <summary>Builds the progress of a single date from the meal entries of that date.</summary>
    public static DayProgress ForDay(
        DateOnly date,
        IEnumerable<MealEntry> entries,
        TimeOnly? nowTime = null,
        TimeSpan? grace = null,
        bool applyHistoricalRule = false)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries as IReadOnlyList<MealEntry> ?? entries.ToArray();
        var slots = new List<MealSlotStatus>();

        foreach (var key in MealSlotRules.AllSlots)
        {
            var entry = materialized.FirstOrDefault(e => e.MealCategoryKey == key);
            var status = entry is null
                ? MealStatus.Pending
                : applyHistoricalRule && nowTime is { } time
                    ? CompletionRules.EffectiveStatus(entry, date, time, grace)
                    : entry.Status;

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
            Completed = slots.Count(s => s.Status == MealStatus.Done),
            Missed = slots.Count(s => s.Status == MealStatus.Missed),
            Pending = slots.Count(s => s.Status == MealStatus.Pending),
            Slots = slots,
        };
    }

    /// <summary>Builds the completion percentage for a single date.</summary>
    public static double PercentageForDay(IEnumerable<MealEntry> entries, DateOnly date)
        => ForDay(date, entries).Percentage;
}
