using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// The synchronization facts the screens rely on: an account that is connected is never reported as
/// "no account", a withdrawn upload consent is reported as paused, a photograph is addressed by its
/// logical cloud path, and a local edit is not held back by the weekly schedule.
/// </summary>
public sealed class SynchronizationStateTests
{
    private static readonly DateOnly TestDate = new(2026, 3, 10);

    [Fact]
    public async Task Only_a_missing_account_is_reported_as_not_configured()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        Assert.Equal(SyncState.NotConfigured, await environment.Sync.RefreshStateAsync());
    }

    [Fact]
    public async Task A_connected_account_is_reported_before_the_first_cycle()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        var state = await environment.Sync.RefreshStateAsync();

        // Nothing has been synchronized yet, so the outstanding work is pending - but the account
        // exists, and the screens show "not configured" as "no account connected".
        Assert.Equal(SyncState.PendingChanges, state);
        Assert.NotEqual(SyncState.NotConfigured, state);
    }

    [Fact]
    public async Task A_withdrawn_upload_consent_is_paused_and_not_reported_as_no_account()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var privacy = environment.Services.GetRequiredService<IPrivacyService>();

        await privacy.SetUploadConsentAsync(false);

        Assert.Equal(SyncState.Paused, await environment.Sync.RefreshStateAsync());

        // Granting it again resumes the same account instead of asking for a new sign-in.
        await privacy.SetUploadConsentAsync(true);
        Assert.Equal(SyncState.PendingChanges, await environment.Sync.RefreshStateAsync());
    }

    [Fact]
    public async Task A_photograph_is_uploaded_with_its_logical_path_and_after_its_binary()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var day = await environment.Tracking.GetDayAsync(TestDate);
        var lunch = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Lunch);

        var photo = await environment.Tracking.AddPhotoAsync(
            lunch.Id, MealTrackingServiceTests.CreateTestJpeg(environment.RootPath));

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var binaryPath = $"photos/{photo.Id:D}.bin";
        var metadataPath = $"entities/MealPhoto/{photo.Id:D}.json";

        var stored = await environment.Photos.GetAsync(photo.Id);
        Assert.NotNull(stored);
        Assert.Equal(SyncStatus.Synced, stored!.SyncStatus);

        // The provider revision token must never be mistaken for the object identifier.
        Assert.Equal(binaryPath, stored.CloudObjectId);
        Assert.Contains(binaryPath, environment.Cloud.Paths);
        Assert.Contains(metadataPath, environment.Cloud.Paths);

        // Another device can only download a photograph that was announced after its bytes arrived.
        var uploads = environment.Cloud.UploadedPaths;
        Assert.True(uploads.ToList().IndexOf(binaryPath) < uploads.ToList().IndexOf(metadataPath));
    }

    [Fact]
    public async Task A_local_change_is_uploaded_even_when_the_weekly_schedule_is_not_due()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var day = await environment.Tracking.GetDayAsync(TestDate);
        var lunch = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Lunch);

        // The first cycle makes the weekly schedule start running.
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var photo = await environment.Tracking.AddPhotoAsync(
            lunch.Id, MealTrackingServiceTests.CreateTestJpeg(environment.RootPath));

        // An automatic knock within the same week is held back ...
        Assert.True((await environment.Sync.SynchronizeAsync(SyncTrigger.Periodic)).Deferred);

        // ... but the edit itself is never held back: it is uploaded as soon as it was journaled.
        var cycle = await environment.Sync.SynchronizeAsync(SyncTrigger.LocalChange);

        Assert.True(cycle.Succeeded);
        Assert.Contains($"photos/{photo.Id:D}.bin", environment.Cloud.Paths);
        Assert.Contains($"entities/MealPhoto/{photo.Id:D}.json", environment.Cloud.Paths);
    }

    [Fact]
    public async Task A_photograph_attached_while_signed_out_is_uploaded_after_signing_in()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(TestDate);
        var lunch = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Lunch);

        // Guest Mode: nothing is journaled, the photograph exists on this device only.
        var photo = await environment.Tracking.AddPhotoAsync(
            lunch.Id, MealTrackingServiceTests.CreateTestJpeg(environment.RootPath));

        await environment.ConnectAccountAsync();
        var cycle = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // The push phase reconciles it into the outbox, so it is not lost because the journal was empty.
        Assert.True(cycle.Succeeded);
        Assert.Contains($"photos/{photo.Id:D}.bin", environment.Cloud.Paths);
        Assert.Contains($"entities/MealPhoto/{photo.Id:D}.json", environment.Cloud.Paths);

        var stored = await environment.Photos.GetAsync(photo.Id);
        Assert.Equal(SyncStatus.Synced, stored!.SyncStatus);
    }

    [Fact]
    public async Task A_photograph_whose_local_file_was_deleted_does_not_fail_the_cycle()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var day = await environment.Tracking.GetDayAsync(TestDate);
        var lunch = day.First(entry => entry.MealCategoryKey == MealCategoryKey.Lunch);

        var photo = await environment.Tracking.AddPhotoAsync(
            lunch.Id, MealTrackingServiceTests.CreateTestJpeg(environment.RootPath));

        var store = environment.Services.GetRequiredService<IPhotoStore>();
        File.Delete(store.GetFullSizePath(photo.LocalFileId)!);

        var cycle = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // Nothing can be uploaded, but the cycle still finishes: the missing file is no permanent error
        // that would retry forever with backoff.
        Assert.True(cycle.Succeeded);
        Assert.DoesNotContain($"photos/{photo.Id:D}.bin", environment.Cloud.Paths);
    }
}
