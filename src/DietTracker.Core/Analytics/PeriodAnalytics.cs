using DietTracker.Core.BusinessRules;
using DietTracker.Core.Models;

namespace DietTracker.Core.Analytics;

/// <summary>Weekly and monthly aggregation, plus trend series for the charts.</summary>
public static class PeriodAnalytics
{
    /// <summary>
    /// Aggregates a date range (inclusive). Days without stored entries still count with five
    /// pending slots so that the percentage stays honest.
    /// </summary>
    public static PeriodProgress ForRange(
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<MealEntry>> entriesByDate,
        TimeOnly? nowTime = null,
        TimeSpan? grace = null,
        bool applyHistoricalRule = false)
    {
        ArgumentNullException.ThrowIfNull(entriesByDate);

        if (to < from)
        {
            throw new ArgumentException("The end date must not precede the start date.", nameof(to));
        }

        var days = new List<DayProgress>(to.DayNumber - from.DayNumber + 1);
        var completed = 0;
        var missed = 0;
        var pending = 0;
        var fullyCompleted = 0;

        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayEntries = entriesByDate.TryGetValue(date, out var list) ? list : [];
            var day = ProgressCalculator.ForDay(date, dayEntries, nowTime, grace, applyHistoricalRule);
            days.Add(day);

            completed += day.Completed;
            missed += day.Missed;
            pending += day.Pending;

            if (day.IsFullyCompleted)
            {
                fullyCompleted++;
            }
        }

        return new PeriodProgress
        {
            From = from,
            To = to,
            TotalSlots = days.Count * MealSlotRules.SlotCount,
            Completed = completed,
            Missed = missed,
            Pending = pending,
            FullyCompletedDays = fullyCompleted,
            Days = days,
        };
    }

    /// <summary>Weekly aggregate starting on Monday, matching the weekly statistics screen.</summary>
    public static PeriodProgress ForWeek(
        DateOnly anyDayOfWeek,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<MealEntry>> entriesByDate,
        TimeOnly? nowTime = null)
    {
        var from = StartOfWeek(anyDayOfWeek);
        return ForRange(from, from.AddDays(6), entriesByDate, nowTime);
    }

    /// <summary>Monthly aggregate for the month that contains <paramref name="anyDayOfMonth"/>.</summary>
    public static PeriodProgress ForMonth(
        DateOnly anyDayOfMonth,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<MealEntry>> entriesByDate,
        TimeOnly? nowTime = null)
    {
        var from = new DateOnly(anyDayOfMonth.Year, anyDayOfMonth.Month, 1);
        return ForRange(from, from.AddMonths(1).AddDays(-1), entriesByDate, nowTime);
    }

    /// <summary>Monday based start of week (ISO 8601).</summary>
    public static DateOnly StartOfWeek(DateOnly date)
    {
        var delta = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-delta);
    }

    /// <summary>Produces the trend series used by the weekly and monthly charts.</summary>
    public static IReadOnlyList<TrendPoint> Trend(
        DateOnly from,
        DateOnly to,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<MealEntry>> entriesByDate)
    {
        ArgumentNullException.ThrowIfNull(entriesByDate);

        var result = new List<TrendPoint>();
        for (var date = from; date <= to; date = date.AddDays(1))
        {
            var dayEntries = entriesByDate.TryGetValue(date, out var list) ? list : [];
            var day = ProgressCalculator.ForDay(date, dayEntries);
            result.Add(new TrendPoint { Date = date, Percentage = day.Percentage, Completed = day.Completed });
        }

        return result;
    }

    /// <summary>Arithmetic mean of the daily percentages, used by the "weekly average" widget.</summary>
    public static double AveragePercentage(IEnumerable<DayProgress> days)
    {
        ArgumentNullException.ThrowIfNull(days);

        var list = days as IReadOnlyCollection<DayProgress> ?? days.ToArray();
        return list.Count == 0 ? 0d : Math.Round(list.Average(d => d.Percentage), 2);
    }
}
