using DietTracker.Core.Analytics;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Services;

/// <summary>Progress, statistics and trend queries (partial of <see cref="MealTrackingService"/>).</summary>
public sealed partial class MealTrackingService
{
    /// <inheritdoc />
    public async Task<DayProgress> GetDayProgressAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var meals = await GetDayAsync(date, cancellationToken).ConfigureAwait(false);
        return ProgressCalculator.ForDay(date, meals);
    }

    /// <inheritdoc />
    public async Task<PeriodProgress> GetWeekProgressAsync(DateOnly anyDay, CancellationToken cancellationToken = default)
        => await GetRangeProgressAsync(PeriodAnalytics.StartOfWeek(anyDay), 6, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<PeriodProgress> GetMonthProgressAsync(DateOnly anyDay, CancellationToken cancellationToken = default)
    {
        var first = new DateOnly(anyDay.Year, anyDay.Month, 1);
        var dayCount = DateTime.DaysInMonth(anyDay.Year, anyDay.Month) - 1;
        return await GetRangeProgressAsync(first, dayCount, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TrendPoint>> GetTrendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var range = await _entries.GetForRangeAsync(profile.Id, from, to, cancellationToken).ConfigureAwait(false);
        return PeriodAnalytics.Trend(from, to, range);
    }

    private async Task<PeriodProgress> GetRangeProgressAsync(DateOnly from, int dayCount, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var to = from.AddDays(dayCount);
        var range = await _entries.GetForRangeAsync(profile.Id, from, to, cancellationToken).ConfigureAwait(false);

        return PeriodAnalytics.ForRange(from, to, range);
    }
}
