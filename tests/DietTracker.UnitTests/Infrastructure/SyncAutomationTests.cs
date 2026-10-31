using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// The automatic side of synchronization: what happens without the user pressing anything.
/// </summary>
/// <remarks>
/// These facts exist because three situations used to need a manual cycle: an edit committed while
/// another cycle was already reading the journal, a connection that came back, and the other
/// device's changes while the window stayed open. Each of them is a promise the screens make, so
/// each of them is asserted here.
/// </remarks>
public sealed class SyncAutomationTests
{
    private static readonly DateOnly TestDate = new(2026, 4, 7);

    private static async Task<MealEntry> SeedLunchAsync(TestEnvironment environment, DateOnly date, string title)
    {
        var day = await environment.Tracking.GetDayAsync(date);
        var lunch = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = title;
        lunch.Ingredients = "Ciorbă de legume";
        await environment.Tracking.SaveEditsAsync(lunch);
        return lunch;
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(100);
        }

        return await condition();
    }

    [Fact]
    public async Task A_change_committed_during_a_running_cycle_gets_its_own_pass()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        // Every upload takes a moment, which leaves a window to commit the second edit while the
        // first cycle is still pushing.
        environment.Cloud.UploadDelay = TimeSpan.FromMilliseconds(250);

        await SeedLunchAsync(environment, TestDate, "Prima masă");
        var cycle = environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        await Task.Delay(120);
        var day = await environment.Tracking.GetDayAsync(TestDate);
        var dinner = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Dinner);
        dinner.Title = "Cină târzie";
        dinner.Ingredients = "Omletă cu brânză";
        await environment.Tracking.SaveEditsAsync(dinner);

        // Saving journals the change and asks for a cycle; the request lands while the manual cycle
        // is still running, so it must not be swallowed.
        await cycle;
        environment.Cloud.UploadDelay = TimeSpan.Zero;

        var expected = dinner.Id.ToString("D") + ".json";
        var uploaded = await WaitUntilAsync(
            () => Task.FromResult(environment.Cloud.Paths.Any(path => path.EndsWith(expected, StringComparison.OrdinalIgnoreCase))),
            TimeSpan.FromSeconds(15));

        Assert.True(uploaded, "The edit committed during the cycle never reached the cloud.");

        var account = await environment.Accounts.GetActiveAsync();
        var pending = await environment.Services.GetRequiredService<ISyncOperationRepository>()
            .CountPendingAsync(account!.Id);
        Assert.Equal(0, pending);
    }

    [Fact]
    public async Task The_passive_pull_never_uploads_and_is_throttled()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        // A local change is waiting in the journal: the passive download must not push it, because
        // the edit itself already asked for a cycle and two pushes racing would only duplicate work.
        await SeedLunchAsync(environment, TestDate, "Așteaptă rândul");
        var uploadsBefore = environment.Cloud.UploadCount;

        var pull = await environment.Sync.SynchronizeAsync(SyncTrigger.AutoPull);
        Assert.True(pull.Succeeded);
        Assert.Equal(uploadsBefore, environment.Cloud.UploadCount);
        Assert.Equal(0, pull.Uploaded);

        // A second knock inside the throttle window costs no request at all.
        var throttled = await environment.Sync.SynchronizeAsync(SyncTrigger.AutoPull);
        Assert.True(throttled.Deferred);
        Assert.Equal(uploadsBefore, environment.Cloud.UploadCount);
    }

    [Fact]
    public async Task A_returning_connection_synchronizes_without_any_user_action()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var connectivity = environment.Services.GetRequiredService<TestConnectivityMonitor>();

        connectivity.Raise(false);
        await SeedLunchAsync(environment, TestDate, "Offline geschrieben");
        await environment.Sync.SynchronizeAsync(SyncTrigger.LocalChange);
        Assert.Equal(0, environment.Cloud.ObjectCount);

        // Nothing calls the engine here: the network coming back is the trigger.
        connectivity.Raise(true);

        var arrived = await WaitUntilAsync(
            () => Task.FromResult(environment.Cloud.ObjectCount > 0),
            TimeSpan.FromSeconds(20));

        Assert.True(arrived, "The change was not uploaded after connectivity returned.");
    }

    [Fact]
    public async Task A_passive_pull_applies_the_other_devices_records()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        // First device: one meal in the cloud.
        await SeedLunchAsync(environment, TestDate, "De pe telefon");
        await environment.Sync.SynchronizeAsync(SyncTrigger.LocalChange);
        Assert.True(environment.Cloud.ObjectCount > 0);

        // Second device: same account, empty cache. The passive pull is the only thing that can
        // bring the record in, because nothing on this installation was edited.
        var secondRoot = Path.Combine(Path.GetTempPath(), "diettracker-tests", Guid.NewGuid().ToString("N"));
        await using var second = await TestEnvironment.CreateAsync(connectAccount: true, rootPath: secondRoot);
        foreach (var path in environment.Cloud.Paths)
        {
            var item = await environment.Cloud.DownloadAsync(path);
            await second.Cloud.UploadAsync(item!);
        }

        var localDay = await second.Tracking.GetDayAsync(TestDate);
        Assert.DoesNotContain(localDay, entry => entry.Title == "De pe telefon");

        var pulled = await second.Sync.SynchronizeAsync(SyncTrigger.AutoPull);
        Assert.True(pulled.Succeeded);
        Assert.True(pulled.Downloaded > 0, "The passive pull downloaded nothing.");

        localDay = await second.Tracking.GetDayAsync(TestDate);
        Assert.Contains(localDay, entry => entry.Title == "De pe telefon");
    }
}
