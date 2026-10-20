using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Services;

/// <summary>
/// System clock abstraction. The time zone can be overridden by the user (stored in preferences);
/// when it is empty the Windows time zone is used, and all calculations stay DST correct because
/// <see cref="TimeZoneInfo"/> conversions are used instead of fixed offsets.
/// </summary>
public sealed class SystemClock : IClock
{
    private readonly TimeProvider _timeProvider;
    private TimeZoneInfo _timeZone;

    public SystemClock(TimeProvider? timeProvider = null, string? timeZoneId = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _timeZone = ResolveTimeZone(timeZoneId);
    }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset LocalNow => TimeZoneInfo.ConvertTime(UtcNow, TimeZone);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);

    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(LocalNow.DateTime);

    public string TimeZoneId => _timeZone.Id;

    public TimeZoneInfo TimeZone => _timeZone;

    /// <summary>Switches the time zone used for "today", reminders and formatting.</summary>
    public void SetTimeZone(string? timeZoneId) => _timeZone = ResolveTimeZone(timeZoneId);

    /// <summary>Converts a local date and time into an absolute instant in the configured zone.</summary>
    public DateTimeOffset ToInstant(DateOnly date, TimeOnly time)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(time), DateTimeKind.Unspecified);
        var offset = _timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
    {
        if (!string.IsNullOrWhiteSpace(timeZoneId))
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                // Fall through to the Windows time zone.
            }
            catch (InvalidTimeZoneException)
            {
                // Fall through to the Windows time zone.
            }
        }

        return TimeZoneInfo.Local;
    }
}
