using DietTracker.Core.Enums;

namespace DietTracker.Core.BusinessRules;

/// <summary>Rules of the automatic synchronization schedule chosen by the user.</summary>
public static class SyncFrequencyRules
{
    /// <summary>Distance between two weekly cycles.</summary>
    public static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>Distance between two monthly cycles.</summary>
    public static readonly TimeSpan Month = TimeSpan.FromDays(30);

    /// <summary>
    /// How long the application waits between two automatic cycles, or <see langword="null"/> when the
    /// frequency means "never automatically" (only the explicit button synchronizes).
    /// </summary>
    public static TimeSpan? Cadence(SyncFrequency frequency) => frequency switch
    {
        SyncFrequency.Weekly => Week,
        SyncFrequency.Monthly => Month,
        _ => null,
    };

    /// <summary>True when an automatic cycle may run once <paramref name="elapsed"/> has gone by.</summary>
    public static bool IsDue(SyncFrequency frequency, TimeSpan? lastSuccessfulSyncAge)
    {
        var cadence = Cadence(frequency);
        if (cadence is null)
        {
            return false;
        }

        // Never synchronized before: the first cycle is always due (sign-in, first launch).
        return lastSuccessfulSyncAge is not { } age || age >= cadence.Value;
    }
}