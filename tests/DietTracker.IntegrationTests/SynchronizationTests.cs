using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.IntegrationTests;

/// <summary>
/// End to end tests of the offline first synchronization behaviour, using the real SQLite database
/// and the real engine with an in-memory provider standing in for OneDrive / Google Drive.
/// </summary>
public sealed class SynchronizationTests
{
    private static async Task<MealEntry> SeedLunchAsync(TestEnvironment environment, DateOnly date, string title)
    {
        var day = await environment.Tracking.GetDayAsync(date);
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = title;
        lunch.Ingredients = "Rice and vegetables";
        await environment.Tracking.SaveEditsAsync(lunch);
        return lunch;
    }

    [Fact]
    public async Task Changes_are_stored_locally_even_without_any_account()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);

        var lunch = await SeedLunchAsync(environment, date, "Offline lunch");
        await environment.Tracking.MarkDoneAsync(lunch.Id);

        // No account: nothing is uploaded, but nothing is lost either.
        Assert.Equal(0, environment.Cloud.ObjectCount);

        var reloaded = await environment.Entries.GetAsync(lunch.Id);
        Assert.Equal("Offline lunch", reloaded!.Title);
        Assert.Equal(MealStatus.Done, reloaded.Status);
    }

    [Fact]
    public async Task Local_changes_are_uploaded_when_an_account_is_connected()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var date = new DateOnly(2026, 3, 10);

        await SeedLunchAsync(environment, date, "Synced lunch");
        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(result.Succeeded);
        Assert.True(result.Uploaded >= 1);
        Assert.Contains(environment.Cloud.Paths, path => path.Contains("/MealEntry/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Empty_placeholder_slots_are_never_uploaded()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        // Opening a day materializes five empty cards; none of them carries user content.
        await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(result.Succeeded);
        Assert.Equal(0, environment.Cloud.ObjectCount);
    }

    [Fact]
    public async Task Unchanged_records_are_not_uploaded_twice()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        await SeedLunchAsync(environment, new DateOnly(2026, 3, 10), "Idempotent lunch");

        var first = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);
        var second = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(first.Uploaded >= 1);
        Assert.Equal(0, second.Uploaded);
    }

    [Fact]
    public async Task A_change_made_on_another_device_is_downloaded()
    {
        await using var deviceA = await TestEnvironment.CreateAsync(connectAccount: true);
        var date = new DateOnly(2026, 3, 12);
        var lunch = await SeedLunchAsync(deviceA, date, "Device A lunch");
        await deviceA.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // A second installation starts empty and synchronizes from the same cloud account.
        await using var deviceB = await TestEnvironment.CreateAsync(connectAccount: true, provider: ProviderType.Google);
        await deviceB.ConnectAccountAsync(ProviderType.Google, "user@example.com");
        ShareCloud(deviceA, deviceB);

        var restore = await deviceB.Sync.RestoreAsync();

        Assert.True(restore.Succeeded);
        var downloaded = await deviceB.Entries.GetAsync(lunch.Id);
        Assert.NotNull(downloaded);
        Assert.Equal("Device A lunch", downloaded!.Title);
        Assert.Equal(date, downloaded.Date);
    }

    [Fact]
    public async Task A_remote_change_wins_when_the_local_record_was_not_modified()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var date = new DateOnly(2026, 3, 13);
        var lunch = await SeedLunchAsync(environment, date, "Original");
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // Another device updates the same record with a higher revision.
        var envelope = new CloudEnvelope<MealEntry>
        {
            EntityType = EntityType.MealEntry,
            EntityId = lunch.Id,
            Revision = 99,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeviceId = "device-b",
            ContentHash = "remote-hash",
            Payload = new MealEntry
            {
                Id = lunch.Id,
                Date = date,
                MealCategoryKey = MealCategoryKey.Lunch,
                Title = "Changed elsewhere",
                Revision = 99,
                Status = MealStatus.Done,
                CompletedAt = DateTimeOffset.UtcNow,
            },
        };

        var serializer = environment.Services.GetRequiredService<ICloudPayloadSerializer>();
        environment.Cloud.WriteFromAnotherDevice(serializer.GetObjectPath(EntityType.MealEntry, lunch.Id), serializer.Serialize(envelope));

        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(result.Succeeded);
        var stored = await environment.Entries.GetAsync(lunch.Id);
        Assert.Equal("Changed elsewhere", stored!.Title);
        Assert.Equal(MealStatus.Done, stored.Status);
    }

    [Fact]
    public async Task Concurrent_edits_are_preserved_for_the_user_instead_of_being_overwritten()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var date = new DateOnly(2026, 3, 14);
        var lunch = await SeedLunchAsync(environment, date, "Mine");
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // The remote payload has the same revision as the local record but different content, and no
        // usable revision token: a genuine conflict.
        var envelope = new CloudEnvelope<MealEntry>
        {
            EntityType = EntityType.MealEntry,
            EntityId = lunch.Id,
            Revision = lunch.Revision,
            UpdatedAt = DateTimeOffset.UtcNow,
            DeviceId = "device-b",
            ContentHash = "different-hash",
            Payload = new MealEntry
            {
                Id = lunch.Id,
                Date = date,
                MealCategoryKey = MealCategoryKey.Lunch,
                Title = "Theirs",
                Revision = lunch.Revision,
            },
        };

        var serializer = environment.Services.GetRequiredService<ICloudPayloadSerializer>();
        environment.Cloud.WriteFromAnotherDevice(serializer.GetObjectPath(EntityType.MealEntry, lunch.Id), serializer.Serialize(envelope));

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var conflicts = await environment.Services.GetRequiredService<IConflictRepository>().GetOpenAsync();

        // Both versions are kept; the local content was not silently discarded.
        Assert.NotEmpty(conflicts);
        Assert.Contains("Mine", conflicts[0].LocalPayload);
        Assert.Contains("Theirs", conflicts[0].RemotePayload);
    }

    [Fact]
    public async Task A_failing_provider_never_corrupts_the_local_database()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var date = new DateOnly(2026, 3, 15);
        var lunch = await SeedLunchAsync(environment, date, "Survives failure");

        environment.Cloud.FailureToThrow = new HttpRequestException("simulated network failure");
        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.False(result.Succeeded);
        Assert.Equal(SyncState.Error, environment.Sync.State);

        // The change is still local, the database is intact and the journal keeps it for a retry with
        // exponential backoff (the failed operation is not retried immediately).
        environment.Cloud.FailureToThrow = null;

        var stored = await environment.Entries.GetAsync(lunch.Id);
        Assert.Equal("Survives failure", stored!.Title);

        var integrity = await environment.Services.GetRequiredService<IDataIntegrityChecker>().CheckAsync();
        Assert.Empty(integrity);

        var account = await environment.Accounts.GetActiveAsync();
        var pending = await environment.Services.GetRequiredService<ISyncOperationRepository>()
            .CountPendingAsync(account!.Id);

        Assert.True(pending >= 1, "The failed operation must stay in the journal for a later retry.");
    }

    [Fact]
    public async Task Being_offline_keeps_the_application_fully_usable()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        environment.SetOnline(false);

        var lunch = await SeedLunchAsync(environment, new DateOnly(2026, 3, 16), "Offline edit");
        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.False(result.Succeeded);
        Assert.Equal(SyncState.Offline, environment.Sync.State);

        var stored = await environment.Entries.GetAsync(lunch.Id);
        Assert.Equal("Offline edit", stored!.Title);

        environment.SetOnline(true);
        var retry = await environment.Sync.SynchronizeAsync(SyncTrigger.ConnectivityRestored);
        Assert.True(retry.Succeeded);
    }

    [Fact]
    public async Task An_edit_is_uploaded_immediately_and_the_schedule_only_governs_automatic_checks()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        // The cycle after sign-in runs (nothing has ever been synchronized) and stamps the account.
        var first = await environment.Sync.SynchronizeAsync(SyncTrigger.PostSignIn);
        Assert.True(first.Succeeded);

        // An edit is uploaded at once instead of waiting up to a week: otherwise a photograph attached
        // today would never reach the cloud until the schedule came round, which looked like a broken
        // synchronization while the journal silently filled up.
        await SeedLunchAsync(environment, new DateOnly(2026, 3, 20), "Scheduled lunch");
        var localChange = await environment.Sync.SynchronizeAsync(SyncTrigger.LocalChange);

        Assert.True(localChange.Succeeded);
        Assert.False(localChange.Deferred);
        Assert.True(localChange.Uploaded >= 1);
        Assert.True(environment.Cloud.ObjectCount >= 1);

        // The schedule still governs the automatic knocks: nothing is due during the same week, so an
        // hourly check produces neither cloud traffic nor a state change (the status strip stays quiet).
        var objectsAfterEdit = environment.Cloud.ObjectCount;
        var deferred = await environment.Sync.SynchronizeAsync(SyncTrigger.Periodic);

        Assert.True(deferred.Deferred);
        Assert.True(deferred.Succeeded);
        Assert.Equal(objectsAfterEdit, environment.Cloud.ObjectCount);
    }

    [Fact]
    public async Task The_manual_frequency_only_synchronizes_when_the_button_is_pressed()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);

        var profiles = environment.Services.GetRequiredService<IProfileService>();
        await profiles.GetOrCreateActiveProfileAsync(); // a fresh installation starts as Guest
        var preferences = await profiles.GetPreferencesAsync();
        preferences.SyncFrequency = SyncFrequency.Manual;
        await profiles.SavePreferencesAsync(preferences);

        var automatic = await environment.Sync.SynchronizeAsync(SyncTrigger.PostSignIn);
        Assert.True(automatic.Deferred, $"state={automatic.State}, reason={automatic.ReasonCode}, diagnostic={automatic.Diagnostic}");
        Assert.Equal(0, environment.Cloud.ObjectCount);

        await SeedLunchAsync(environment, new DateOnly(2026, 3, 22), "Only on demand");
        var edit = await environment.Sync.SynchronizeAsync(SyncTrigger.LocalChange);
        Assert.True(edit.Deferred);
        Assert.Equal(0, environment.Cloud.ObjectCount);

        var manual = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);
        Assert.True(manual.Succeeded);
        Assert.True(manual.Uploaded >= 1);
    }

    [Fact]
    public async Task The_weekly_schedule_elapses_and_the_next_automatic_cycle_runs()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        await environment.Sync.SynchronizeAsync(SyncTrigger.PostSignIn);
        await SeedLunchAsync(environment, new DateOnly(2026, 3, 23), "Eight days later");

        // The last successful cycle is moved eight days into the past: the week is up.
        var clock = environment.Services.GetRequiredService<IClock>();
        var account = await environment.Accounts.GetActiveAsync();
        account!.LastSynchronizedAt = clock.UtcNow.AddDays(-8);
        await environment.Accounts.UpdateAsync(account);

        var due = await environment.Sync.SynchronizeAsync(SyncTrigger.LocalChange);

        Assert.False(due.Deferred);
        Assert.True(due.Succeeded);
        Assert.True(due.Uploaded >= 1);
    }

    [Fact]
    public async Task Photographs_are_uploaded_once_and_restored_on_another_device()
    {
        await using var deviceA = await TestEnvironment.CreateAsync(connectAccount: true);
        var date = new DateOnly(2026, 3, 17);
        var lunch = await SeedLunchAsync(deviceA, date, "With photo");
        var photoPath = Path.Combine(deviceA.RootPath, "meal.jpg");
        await File.WriteAllBytesAsync(photoPath, Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q=="));

        var photo = await deviceA.Tracking.AddPhotoAsync(lunch.Id, photoPath);
        await deviceA.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var binaryPath = deviceA.Services.GetRequiredService<ICloudPayloadSerializer>().GetPhotoObjectPath(photo.Id);
        Assert.Contains(deviceA.Cloud.Paths, path => path.Equals(binaryPath, StringComparison.OrdinalIgnoreCase));

        // A second synchronization must not upload the unchanged photograph again.
        var before = deviceA.Cloud.UploadCount;
        await deviceA.Sync.SynchronizeAsync(SyncTrigger.Manual);
        Assert.Equal(before, deviceA.Cloud.UploadCount);

        // A second device restores the meal first and the photograph in the following cycle, because a
        // photograph can only be attached to a meal slot that already exists locally.
        await using var deviceB = await TestEnvironment.CreateAsync(connectAccount: true);
        ShareCloud(deviceA, deviceB);
        await deviceB.Sync.RestoreAsync();
        await deviceB.Sync.RestoreAsync();

        var restored = await deviceB.Photos.GetForMealAsync(lunch.Id);
        Assert.Single(restored);

        var store = deviceB.Services.GetRequiredService<IPhotoStore>();
        var restoredPath = store.GetFullSizePath(restored[0].LocalFileId);
        Assert.NotNull(restoredPath);
        Assert.True(File.Exists(restoredPath));
    }

    [Fact]
    public async Task Signing_out_keeps_every_local_record()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, new DateOnly(2026, 3, 18), "Kept after sign out");

        await environment.Services.GetRequiredService<IAuthenticationCoordinator>().SignOutAsync();

        var stored = await environment.Entries.GetAsync(lunch.Id);
        Assert.NotNull(stored);
        Assert.Equal("Kept after sign out", stored!.Title);
    }

    [Fact]
    public async Task A_local_backup_can_be_restored_on_a_clean_installation()
    {
        await using var source = await TestEnvironment.CreateAsync();
        var lunch = await SeedLunchAsync(source, new DateOnly(2026, 3, 20), "Backed up lunch");
        await source.Tracking.MarkDoneAsync(lunch.Id);

        var backupPath = Path.Combine(source.RootPath, "backup.dtbackup");
        var summary = await source.Backup.ExportAsync(backupPath);

        Assert.True(File.Exists(backupPath));
        Assert.True(summary.MealEntries >= 1);

        await using var target = await TestEnvironment.CreateAsync();
        var restored = await target.Backup.RestoreAsync(backupPath);

        Assert.True(restored.MealEntries >= 1);
        var restoredEntry = await target.Entries.GetAsync(lunch.Id);
        Assert.NotNull(restoredEntry);
        Assert.Equal("Backed up lunch", restoredEntry!.Title);
        Assert.Equal(MealStatus.Done, restoredEntry.Status);
    }

    [Fact]
    public async Task Deleting_local_data_never_touches_the_cloud_dataset()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        await SeedLunchAsync(environment, new DateOnly(2026, 3, 21), "Local only");
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var cloudObjects = environment.Cloud.ObjectCount;
        Assert.True(cloudObjects > 0);

        var privacy = environment.Services.GetRequiredService<IPrivacyService>();
        await privacy.DeleteLocalDataAsync(includePhotographs: true);

        var profile = await environment.Services.GetRequiredService<IProfileService>().GetOrCreateActiveProfileAsync();
        Assert.Equal(0, await environment.Entries.CountAsync(profile.Id));
        Assert.Equal(cloudObjects, environment.Cloud.ObjectCount);
    }

    [Fact]
    public async Task Deleting_cloud_data_never_touches_the_local_dataset()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var lunch = await SeedLunchAsync(environment, new DateOnly(2026, 3, 22), "Cloud only");
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var privacy = environment.Services.GetRequiredService<IPrivacyService>();
        var deleted = await privacy.DeleteCloudDataAsync();

        Assert.True(deleted > 0);
        Assert.Equal(0, environment.Cloud.ObjectCount);

        var stored = await environment.Entries.GetAsync(lunch.Id);
        Assert.Equal("Cloud only", stored!.Title);
    }

    [Fact]
    public async Task Background_cycles_stay_silent_by_reporting_their_trigger()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        await SeedLunchAsync(environment, new DateOnly(2026, 3, 24), "Trigger lunch");

        var triggers = new List<SyncTrigger?>();
        environment.Sync.StateChanged += (_, e) => triggers.Add(e.Trigger);

        // A cycle the user did not start (start-up, the schedule, a local edit) carries its own
        // trigger, so the shell keeps quiet: no status strip, no tray balloon.
        await environment.Sync.SynchronizeAsync(SyncTrigger.PostSignIn);
        Assert.NotEmpty(triggers);
        Assert.All(triggers, trigger => Assert.Equal(SyncTrigger.PostSignIn, trigger));

        // The "Synchronize now" button carries Manual - the only trigger the shell shows.
        triggers.Clear();
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);
        Assert.NotEmpty(triggers);
        Assert.All(triggers, trigger => Assert.Equal(SyncTrigger.Manual, trigger));

        // The start-up refresh of the stored account comes from no cycle at all: no trigger.
        triggers.Clear();
        await environment.Sync.RefreshStateAsync();
        Assert.NotEmpty(triggers);
        Assert.All(triggers, trigger => Assert.Null(trigger));
    }

    /// <summary>Points a second environment at the cloud store of the first one (a shared account).</summary>
    private static void ShareCloud(TestEnvironment from, TestEnvironment to)
    {
        foreach (var path in from.Cloud.Paths.ToArray())
        {
            var content = from.Cloud.DownloadAsync(path).GetAwaiter().GetResult();
            if (content?.Content is { Length: > 0 } bytes)
            {
                // Raw bytes are copied so that photograph binaries survive the transfer unchanged.
                to.Cloud.WriteRawFromAnotherDevice(path, bytes);
            }
        }
    }
}
