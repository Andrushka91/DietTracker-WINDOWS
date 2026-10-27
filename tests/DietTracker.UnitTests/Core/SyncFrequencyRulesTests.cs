using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using Xunit;

namespace DietTracker.UnitTests.Core;

/// <summary>
/// Tests of the automatic synchronization schedule: how long the application waits between two
/// background cycles and when the very first one is allowed to run.
/// </summary>
public sealed class SyncFrequencyRulesTests
{
    [Fact]
    public void Weekly_and_monthly_have_a_seven_and_a_thirty_day_cadence()
    {
        Assert.Equal(TimeSpan.FromDays(7), SyncFrequencyRules.Cadence(SyncFrequency.Weekly));
        Assert.Equal(TimeSpan.FromDays(30), SyncFrequencyRules.Cadence(SyncFrequency.Monthly));
    }

    [Fact]
    public void The_manual_frequency_never_synchronizes_on_its_own()
    {
        Assert.Null(SyncFrequencyRules.Cadence(SyncFrequency.Manual));
        Assert.False(SyncFrequencyRules.IsDue(SyncFrequency.Manual, lastSuccessfulSyncAge: null));
        Assert.False(SyncFrequencyRules.IsDue(SyncFrequency.Manual, TimeSpan.FromDays(365)));
    }

    [Fact]
    public void The_first_cycle_is_due_before_anything_was_ever_synchronized()
    {
        Assert.True(SyncFrequencyRules.IsDue(SyncFrequency.Weekly, lastSuccessfulSyncAge: null));
        Assert.True(SyncFrequencyRules.IsDue(SyncFrequency.Monthly, lastSuccessfulSyncAge: null));
    }

    [Fact]
    public void A_cycle_becomes_due_exactly_once_the_cadence_elapsed()
    {
        Assert.False(SyncFrequencyRules.IsDue(SyncFrequency.Weekly, TimeSpan.FromDays(6)));
        Assert.True(SyncFrequencyRules.IsDue(SyncFrequency.Weekly, TimeSpan.FromDays(7)));

        Assert.False(SyncFrequencyRules.IsDue(SyncFrequency.Monthly, TimeSpan.FromDays(29)));
        Assert.True(SyncFrequencyRules.IsDue(SyncFrequency.Monthly, TimeSpan.FromDays(30)));
    }
}