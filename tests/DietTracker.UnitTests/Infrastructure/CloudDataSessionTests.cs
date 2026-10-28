using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// The cloud-only data lifecycle: the local database is a cache of exactly one account. Signing out
/// uploads what is pending and then deletes every local trace, switching accounts never shows the
/// records of the previous one, and a cache that was emptied on purpose can be rebuilt from the cloud
/// even though this installation uploaded the documents itself.
/// </summary>
public sealed class CloudDataSessionTests
{
    private static readonly DateOnly TestDate = new(2026, 4, 1);

    private static ICloudDataSession Session(TestEnvironment environment)
        => environment.Services.GetRequiredService<ICloudDataSession>();

    private static async Task<MealEntry> SeedLunchAsync(TestEnvironment environment, string title)
    {
        var day = await environment.Tracking.GetDayAsync(TestDate);
        var lunch = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = title;
        await environment.Tracking.SaveEditsAsync(lunch);
        return lunch;
    }

    private static async Task<IReadOnlyList<MealEntry>> StoredLunchesAsync(TestEnvironment environment)
    {
        var profile = await environment.Services.GetRequiredService<IProfileService>().GetOrCreateActiveProfileAsync();
        return (await environment.Entries.GetForDateAsync(profile.Id, TestDate))
            .Where(entry => entry.MealCategoryKey == MealCategoryKey.Lunch)
            .ToArray();
    }

    [Fact]
    public async Task Signing_out_uploads_the_pending_changes_and_then_deletes_every_local_record()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, "Lunch before the sign-out");
        var photo = await environment.Tracking.AddPhotoAsync(
            lunch.Id, MealTrackingServiceTests.CreateTestJpeg(environment.RootPath));

        var result = await Session(environment).SignOutAsync();

        Assert.True(result.Succeeded);
        Assert.True(result.LocalDataCleared);

        // The cloud copy was confirmed before anything was deleted.
        Assert.Contains($"entities/MealEntry/{lunch.Id:D}.json", environment.Cloud.Paths);
        Assert.Contains($"photos/{photo.Id:D}.bin", environment.Cloud.Paths);

        // Nothing of the session survives on the device.
        Assert.Empty(await StoredLunchesAsync(environment));
        Assert.Empty(await environment.Photos.GetAllAsync());
        Assert.Null(await environment.Accounts.GetActiveAsync());
        Assert.Equal(SyncState.NotConfigured, await environment.Sync.RefreshStateAsync());

        var store = environment.Services.GetRequiredService<IPhotoStore>();
        Assert.False(File.Exists(store.GetFullSizePath(photo.LocalFileId)));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(environment.RootPath, "photos"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_cache_that_was_emptied_can_be_rebuilt_from_the_cloud_by_the_same_installation()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, "Rebuilt lunch");
        await environment.Tracking.MarkDoneAsync(lunch.Id);
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // The cache and its cursor are gone, exactly like after an account change.
        await environment.Services.GetRequiredService<IPrivacyService>().PurgeLocalCacheAsync(includePhotographs: true);
        Assert.Empty(await StoredLunchesAsync(environment));

        var rebuild = await environment.Sync.RebuildFromCloudAsync();

        // The regular pull would skip these documents as the echo of our own upload; the rebuild must
        // apply them, otherwise switching accounts would leave an empty database.
        Assert.True(rebuild.Succeeded);
        var restored = await environment.Entries.GetAsync(lunch.Id);
        Assert.NotNull(restored);
        Assert.Equal("Rebuilt lunch", restored!.Title);
        Assert.Equal(MealStatus.Done, restored.Status);
    }

    [Fact]
    public async Task Signing_out_is_refused_while_changes_are_still_waiting_for_the_cloud()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, "Offline lunch");
        environment.SetOnline(false);

        var refused = await Session(environment).SignOutAsync();

        // The cloud copy is the only copy: nothing is deleted while it does not exist.
        Assert.False(refused.Succeeded);
        Assert.Equal(AccountTransitionResult.PendingChangesReason, refused.FailureReason);
        Assert.True(refused.PendingChanges >= 1);
        Assert.False(refused.LocalDataCleared);
        Assert.NotNull(await environment.Accounts.GetActiveAsync());
        Assert.NotNull(await environment.Entries.GetAsync(lunch.Id));

        // The caller can decide explicitly to discard them.
        var discarded = await Session(environment).SignOutAsync(discardPendingChanges: true);

        Assert.True(discarded.Succeeded);
        Assert.True(discarded.LocalDataCleared);
        Assert.Null(await environment.Entries.GetAsync(lunch.Id));
        Assert.Null(await environment.Accounts.GetActiveAsync());
    }

    [Fact]
    public async Task Preparing_a_sign_in_uploads_the_previous_account_without_deleting_anything()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, "Previous account lunch");

        var preparation = await Session(environment).PrepareForSignInAsync();

        // Uploaded - and still there: a cancelled sign-in must not cost the data of the current account.
        Assert.True(preparation.Succeeded);
        Assert.Contains($"entities/MealEntry/{lunch.Id:D}.json", environment.Cloud.Paths);
        Assert.NotNull(await environment.Entries.GetAsync(lunch.Id));

        // Completing the sign-in (to the same account here) empties the cache and rebuilds it from the
        // cloud, so nothing of the previous session survives on the device.
        var completion = await Session(environment).CompleteSignInAsync(preparation.LocalDataAdopted);

        Assert.True(completion.LocalDataCleared);
        Assert.True(completion.CloudDataLoaded);
        var restored = await environment.Entries.GetAsync(lunch.Id);
        Assert.NotNull(restored);
        Assert.Equal("Previous account lunch", restored!.Title);
    }

    [Fact]
    public async Task Switching_accounts_never_shows_the_records_of_the_previous_one()
    {
        // Account A with its own cloud folder.
        await using var deviceA = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunchA = await SeedLunchAsync(deviceA, "Lunch of account A");
        await deviceA.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // Account B, a different folder with different records.
        await using var deviceB = await TestEnvironment.CreateAsync(connectAccount: true);
        await SeedLunchAsync(deviceB, "Lunch of account B");
        await deviceB.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // The installation signs out of A: everything is uploaded, then deleted locally.
        var signedOut = await Session(deviceA).SignOutAsync();
        Assert.True(signedOut.LocalDataCleared);
        Assert.Null(await deviceA.Entries.GetAsync(lunchA.Id));

        // Signing in to B replaces the cloud folder; the cache must show B and only B.
        ClearCloud(deviceA);
        CopyCloud(deviceB, deviceA);
        await deviceA.ConnectAccountAsync(ProviderType.Google, "b@example.com");

        var completion = await Session(deviceA).CompleteSignInAsync(adoptLocalData: false);

        Assert.True(completion.CloudDataLoaded);
        var lunches = await StoredLunchesAsync(deviceA);
        Assert.Single(lunches);
        Assert.Equal("Lunch of account B", lunches[0].Title);
        Assert.Null(await deviceA.Entries.GetAsync(lunchA.Id));
    }

    [Fact]
    public async Task A_cache_that_survived_without_an_account_is_removed_at_start_up()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, "Left behind");
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // A crash between the sign-out and the deletion leaves an active-less cache behind.
        await environment.Accounts.ClearActiveAsync();

        var enforcement = await Session(environment).EnforceOnStartupAsync();

        Assert.NotNull(enforcement);
        Assert.True(enforcement!.LocalDataCleared);
        Assert.Null(await environment.Entries.GetAsync(lunch.Id));
    }

    [Fact]
    public async Task A_cache_with_changes_that_never_reached_the_cloud_is_kept_at_start_up()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, "Never uploaded");

        // No cycle ran, so the journal still holds the change; the account marker disappears (a crash
        // between the sign-out and the deletion, or a cache written by an older build).
        environment.SetOnline(false);
        await environment.Accounts.ClearActiveAsync();

        var enforcement = await Session(environment).EnforceOnStartupAsync();

        // Nothing can upload it here, so the only safe answer is to keep it for the next sign-in.
        Assert.Null(enforcement);
        Assert.NotNull(await environment.Entries.GetAsync(lunch.Id));
    }

    [Fact]
    public async Task An_installation_that_never_had_an_account_keeps_its_offline_records()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var lunch = await SeedLunchAsync(environment, "Only copy");

        var enforcement = await Session(environment).EnforceOnStartupAsync();

        // Nothing was ever uploaded, so deleting it would destroy the only copy.
        Assert.Null(enforcement);
        Assert.NotNull(await environment.Entries.GetAsync(lunch.Id));
    }

    [Fact]
    public async Task The_first_sign_in_adopts_the_records_created_without_an_account()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var lunch = await SeedLunchAsync(environment, "Guest lunch");

        var preparation = await Session(environment).PrepareForSignInAsync();
        Assert.True(preparation.LocalDataAdopted);
        Assert.NotNull(await environment.Entries.GetAsync(lunch.Id));

        // The interactive sign-in registers the account; the session then uploads the adopted records.
        await environment.ConnectAccountAsync();
        var completion = await Session(environment).CompleteSignInAsync(preparation.LocalDataAdopted);

        Assert.True(completion.Succeeded);
        Assert.True(completion.LocalDataAdopted);
        Assert.False(completion.LocalDataCleared);
        Assert.Contains($"entities/MealEntry/{lunch.Id:D}.json", environment.Cloud.Paths);
        Assert.NotNull(await environment.Entries.GetAsync(lunch.Id));
    }

    /// <summary>Empties the in-memory cloud, as if another account's folder was connected.</summary>
    private static void ClearCloud(TestEnvironment environment)
    {
        foreach (var path in environment.Cloud.Paths.ToArray())
        {
            environment.Cloud.DeleteAsync(path).GetAwaiter().GetResult();
        }
    }

    /// <summary>Copies the dataset of one cloud folder into another (a different account).</summary>
    private static void CopyCloud(TestEnvironment from, TestEnvironment to)
    {
        foreach (var path in from.Cloud.Paths.ToArray())
        {
            var content = from.Cloud.DownloadAsync(path).GetAwaiter().GetResult();
            if (content?.Content is { Length: > 0 } bytes)
            {
                to.Cloud.WriteRawFromAnotherDevice(path, bytes);
            }
        }
    }
}

