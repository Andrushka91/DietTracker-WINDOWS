using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>
/// Cross-platform import regressions (audit 31 Oct 2026): a record published by the mobile app is
/// applied when the cache is empty (even at revision zero), a placeholder slot adopts the cloud
/// identity without orphaning its photographs, a photograph whose meal is missing is reported as
/// unfinished instead of silently dropped, and category/preference documents keep the local ids and
/// device-specific settings.
/// </summary>
public sealed class SyncImportRegressionTests
{
    private static readonly DateOnly TestDate = new(2026, 10, 31);

    private static Task<UserProfile> ActiveProfileAsync(TestEnvironment environment)
        => environment.Services.GetRequiredService<IProfileService>().GetOrCreateActiveProfileAsync();

    private static ICloudPayloadSerializer Serializer(TestEnvironment environment)
        => environment.Services.GetRequiredService<ICloudPayloadSerializer>();

    private static CloudEnvelope<MealEntry> MobileMealEnvelope(Guid mealId, MealCategoryKey key, string title)
    {
        var now = DateTimeOffset.UtcNow;

        // Exactly the shape the mobile app publishes: the profile and category ids belong to the
        // sending installation and the revision of a freshly created record is zero.
        return new CloudEnvelope<MealEntry>
        {
            EntityType = EntityType.MealEntry,
            EntityId = mealId,
            LocalProfileId = Guid.NewGuid(),
            Revision = 0,
            UpdatedAt = now,
            DeviceId = "mobile-device",
            ContentHash = $"hash-{mealId:N}",
            Payload = new MealEntry
            {
                Id = mealId,
                LocalProfileId = Guid.NewGuid(),
                Date = TestDate,
                MealCategoryId = Guid.NewGuid(),
                MealCategoryKey = key,
                Title = title,
                Ingredients = "brânză, smântână 🥗",
                Revision = 0,
                Status = MealStatus.Pending,
                CreatedAt = now,
                UpdatedAt = now,
                SyncStatus = SyncStatus.PendingUpload,
            },
        };
    }

    private static ConflictDecision Resolve(IConflictResolver resolver, ConflictContext context)
        => resolver.Decide(context);

    [Fact]
    public async Task A_new_remote_record_is_accepted_even_at_revision_zero()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var resolver = environment.Services.GetRequiredService<IConflictResolver>();

        // Policy level: there is no local content hash, so there is nothing to conflict with.
        var decision = Resolve(resolver, new ConflictContext
        {
            EntityType = EntityType.MealEntry,
            EntityId = Guid.NewGuid(),
            LocalRevision = 0,
            RemoteRevision = 0,
            LocalContentHash = null,
            RemoteContentHash = "remote-hash",
            LocalUpdatedAt = DateTimeOffset.MinValue,
            RemoteUpdatedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(ConflictDecision.AcceptRemote, decision);
    }

    [Fact]
    public async Task A_revision_zero_record_of_the_mobile_app_is_materialized_and_not_a_conflict()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var profile = await ActiveProfileAsync(environment);
        var remoteId = Guid.NewGuid();
        var serializer = Serializer(environment);

        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealEntry, remoteId),
            serializer.Serialize(MobileMealEnvelope(remoteId, MealCategoryKey.Breakfast, "Șnițel cu mămăligă 🥗")));

        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(result.Succeeded);
        var stored = await environment.Entries.GetAsync(remoteId);
        Assert.NotNull(stored);

        // Unicode text survives, the profile and category references were mapped onto local rows.
        Assert.Equal("Șnițel cu mămăligă 🥗", stored!.Title);
        Assert.Equal("brânză, smântână 🥗", stored.Ingredients);
        Assert.Equal(profile.Id, stored.LocalProfileId);

        var category = await environment.Services.GetRequiredService<IMealCategoryRepository>()
            .GetByKeyAsync(MealCategoryKey.Breakfast);
        Assert.Equal(category!.Id, stored.MealCategoryId);

        var conflicts = await environment.Services.GetRequiredService<IConflictRepository>().CountOpenAsync();
        Assert.Equal(0, conflicts);
    }

    [Fact]
    public async Task A_placeholder_slot_adopts_the_cloud_identity_and_keeps_its_photograph()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var profile = await ActiveProfileAsync(environment);
        var category = await environment.Services.GetRequiredService<IMealCategoryRepository>()
            .GetByKeyAsync(MealCategoryKey.Lunch);
        var now = DateTimeOffset.UtcNow;

        var placeholderId = Guid.NewGuid();
        await environment.Entries.UpsertAsync(new MealEntry
        {
            Id = placeholderId,
            LocalProfileId = profile.Id,
            Date = TestDate,
            MealCategoryId = category!.Id,
            MealCategoryKey = MealCategoryKey.Lunch,
            CreatedAt = now,
            UpdatedAt = now,
            SyncStatus = SyncStatus.Synced,
        });

        var photoId = Guid.NewGuid();
        await environment.Photos.AddAsync(new MealPhoto
        {
            Id = photoId,
            MealEntryId = placeholderId,
            OriginalFileName = "lunch.jpg",
            LocalFileId = "lunch.jpg",
            ThumbnailId = "lunch-thumb.png",
            ContentHash = "local-photo-hash",
            CreatedAt = now,
            UpdatedAt = now,
            SyncStatus = SyncStatus.Synced,
        });

        var remoteId = Guid.NewGuid();
        var serializer = Serializer(environment);
        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealEntry, remoteId),
            serializer.Serialize(MobileMealEnvelope(remoteId, MealCategoryKey.Lunch, "Comida")));

        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        Assert.True(result.Succeeded);
        var slot = await environment.Entries.GetBySlotAsync(profile.Id, TestDate, MealCategoryKey.Lunch);
        Assert.Equal(remoteId, slot!.Id);
        Assert.Equal("Comida", slot.Title);
        Assert.Null(await environment.Entries.GetAsync(placeholderId));

        // The photograph followed the slot: it still points at an existing meal.
        var photo = await environment.Photos.GetAsync(photoId);
        Assert.Equal(remoteId, photo!.MealEntryId);
        Assert.Equal(0, await environment.Services.GetRequiredService<IConflictRepository>().CountOpenAsync());
    }


    [Fact]
    public async Task A_photograph_whose_meal_is_missing_is_reported_as_unfinished()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var serializer = Serializer(environment);
        var now = DateTimeOffset.UtcNow;
        var orphanMealId = Guid.NewGuid();
        var photoId = Guid.NewGuid();

        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealPhoto, photoId),
            serializer.Serialize(new CloudEnvelope<MealPhoto>
            {
                EntityType = EntityType.MealPhoto,
                EntityId = photoId,
                LocalProfileId = Guid.NewGuid(),
                Revision = 0,
                UpdatedAt = now,
                DeviceId = "mobile-device",
                ContentHash = "photo-hash",
                Payload = new MealPhoto
                {
                    Id = photoId,
                    MealEntryId = orphanMealId,
                    OriginalFileName = "orphan.jpg",
                    LocalFileId = "orphan.jpg",
                    ThumbnailId = "orphan-thumb.png",
                    ContentHash = "photo-hash",
                    CreatedAt = now,
                    UpdatedAt = now,
                    SyncStatus = SyncStatus.PendingUpload,
                },
            }));
        environment.Cloud.WriteRawFromAnotherDevice(serializer.GetPhotoObjectPath(photoId), [1, 2, 3, 4]);

        var result = await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // The photograph is not invented under a missing parent, and the cycle does not claim success.
        Assert.Null(await environment.Photos.GetAsync(photoId));
        Assert.True(result.Skipped >= 1);
        Assert.Equal(SyncState.PendingChanges, environment.Sync.State);
        Assert.Equal(SyncState.PendingChanges, result.State);
    }

    [Fact]
    public async Task A_category_document_keeps_the_local_id_and_takes_the_emoji_icon()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var categories = environment.Services.GetRequiredService<IMealCategoryRepository>();
        var before = await categories.GetByKeyAsync(MealCategoryKey.Lunch);
        var serializer = Serializer(environment);
        var remoteCategoryId = Guid.NewGuid();

        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealCategory, remoteCategoryId),
            serializer.Serialize(new CloudEnvelope<MealCategory>
            {
                EntityType = EntityType.MealCategory,
                EntityId = remoteCategoryId,
                LocalProfileId = Guid.NewGuid(),
                Revision = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeviceId = "mobile-device",
                ContentHash = "category-hash",
                Payload = new MealCategory
                {
                    Id = remoteCategoryId,
                    FixedCategoryKey = MealCategoryKey.Lunch,
                    Name = "Prânz",
                    Color = "#FF107C10",
                    Icon = "🍲️",
                    DefaultTime = new TimeOnly(13, 30),
                    DisplayOrder = 2,
                },
            }));

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var after = await categories.GetByKeyAsync(MealCategoryKey.Lunch);
        Assert.Equal(before!.Id, after!.Id);
        Assert.Equal("Prânz", after.Name);
        Assert.Equal("🍲️", after.Icon);
    }

    [Fact]
    public async Task Only_the_shared_preference_fields_travel_between_devices()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var profile = await ActiveProfileAsync(environment);
        var preferences = environment.Services.GetRequiredService<IAppPreferencesRepository>();
        var local = await preferences.GetAsync(profile.Id);
        Assert.NotNull(local);

        local!.SyncFrequency = SyncFrequency.Manual;
        local.SynchronizationEnabled = false;
        await preferences.UpsertAsync(local);

        var serializer = Serializer(environment);
        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.AppPreferences, profile.Id),
            serializer.Serialize(new CloudEnvelope<AppPreferences>
            {
                EntityType = EntityType.AppPreferences,
                EntityId = profile.Id,
                LocalProfileId = Guid.NewGuid(),
                Revision = 1,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeviceId = "mobile-device",
                ContentHash = "prefs-hash",
                Payload = new AppPreferences
                {
                    LocalProfileId = Guid.NewGuid(),
                    TimeZone = "Europe/Bucharest",
                    ShowSnackCards = false,
                    RemindersEnabled = false,
                    Use12HourClock = true,
                    SynchronizationEnabled = true,
                    SyncFrequency = SyncFrequency.Monthly,
                    CloudUploadConsentGranted = true,
                    OnboardingCompleted = true,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    SyncStatus = SyncStatus.Synced,
                },
            }));

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var merged = await preferences.GetAsync(profile.Id);
        Assert.Equal("Europe/Bucharest", merged!.TimeZone);
        Assert.False(merged.ShowSnackCards);
        Assert.False(merged.RemindersEnabled);
        Assert.True(merged.Use12HourClock);

        // Device-specific settings survive the merge.
        Assert.Equal(SyncFrequency.Manual, merged.SyncFrequency);
        Assert.False(merged.SynchronizationEnabled);
    }


    [Fact]
    public async Task Two_records_of_the_same_slot_keep_both_versions()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var profile = await ActiveProfileAsync(environment);
        var category = await environment.Services.GetRequiredService<IMealCategoryRepository>()
            .GetByKeyAsync(MealCategoryKey.Dinner);
        var now = DateTimeOffset.UtcNow;

        var localId = Guid.NewGuid();
        await environment.Entries.UpsertAsync(new MealEntry
        {
            Id = localId,
            LocalProfileId = profile.Id,
            Date = TestDate,
            MealCategoryId = category!.Id,
            MealCategoryKey = MealCategoryKey.Dinner,
            Title = "Created here",
            Revision = 3,
            CreatedAt = now,
            UpdatedAt = now,
            CloudRevision = "rev-1",
            ContentHash = "local-hash",
            SyncStatus = SyncStatus.Synced,
        });

        var remoteId = Guid.NewGuid();
        var serializer = Serializer(environment);
        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealEntry, remoteId),
            serializer.Serialize(MobileMealEnvelope(remoteId, MealCategoryKey.Dinner, "Created elsewhere")));

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // Neither version is discarded: the local row is untouched and both payloads are stored.
        var local = await environment.Entries.GetAsync(localId);
        Assert.Equal("Created here", local!.Title);
        Assert.Null(await environment.Entries.GetAsync(remoteId));

        var conflicts = await environment.Services.GetRequiredService<IConflictRepository>().GetOpenAsync();
        Assert.Contains(conflicts, conflict => conflict.EntityId == localId
            && conflict.LocalPayload.Contains("Created here", StringComparison.Ordinal)
            && conflict.RemotePayload.Contains("Created elsewhere", StringComparison.Ordinal));

        // A second cycle does not stack a duplicate row for the same cloud revision.
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);
        var afterSecondCycle = await environment.Services.GetRequiredService<IConflictRepository>().GetOpenAsync();
        Assert.Equal(conflicts.Count, afterSecondCycle.Count);
    }


    [Fact]
    public async Task A_stale_local_edit_never_overwrites_a_newer_cloud_copy()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var profile = await ActiveProfileAsync(environment);
        var category = await environment.Services.GetRequiredService<IMealCategoryRepository>()
            .GetByKeyAsync(MealCategoryKey.Lunch);
        var journal = environment.Services.GetRequiredService<ISyncJournal>();
        var serializer = Serializer(environment);
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();

        await environment.Entries.UpsertAsync(new MealEntry
        {
            Id = id,
            LocalProfileId = profile.Id,
            Date = TestDate,
            MealCategoryId = category!.Id,
            MealCategoryKey = MealCategoryKey.Lunch,
            Title = "First",
            Revision = 1,
            CreatedAt = now,
            UpdatedAt = now,
            SyncStatus = SyncStatus.PendingUpload,
        });
        await journal.RecordUpsertAsync(EntityType.MealEntry, id, 1);
        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        var uploaded = await environment.Entries.GetAsync(id);
        Assert.Equal(SyncStatus.Synced, uploaded!.SyncStatus);
        Assert.False(string.IsNullOrEmpty(uploaded.CloudRevision));

        // Another device publishes a newer version of the same document.
        environment.Cloud.WriteFromAnotherDevice(
            serializer.GetObjectPath(EntityType.MealEntry, id),
            serializer.Serialize(new CloudEnvelope<MealEntry>
            {
                EntityType = EntityType.MealEntry,
                EntityId = id,
                LocalProfileId = Guid.NewGuid(),
                Revision = 5,
                UpdatedAt = now,
                DeviceId = "other-device",
                ContentHash = "remote-hash",
                Payload = new MealEntry
                {
                    Id = id,
                    Date = TestDate,
                    MealCategoryKey = MealCategoryKey.Lunch,
                    Title = "Newer elsewhere",
                    Revision = 5,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
            }));

        // The user edits here, based on the stale cloud revision, and the change is journaled.
        await environment.Entries.UpsertAsync(new MealEntry
        {
            Id = id,
            LocalProfileId = profile.Id,
            Date = TestDate,
            MealCategoryId = category.Id,
            MealCategoryKey = MealCategoryKey.Lunch,
            Title = "Stale local",
            Revision = 2,
            CreatedAt = now,
            UpdatedAt = now,
            CloudRevision = uploaded.CloudRevision,
            ContentHash = uploaded.ContentHash,
            SyncStatus = SyncStatus.PendingUpdate,
        });
        await journal.RecordUpsertAsync(EntityType.MealEntry, id, 2);

        await environment.Sync.SynchronizeAsync(SyncTrigger.Manual);

        // The newer cloud version wins and the stale local edit did not overwrite it.
        var stored = await environment.Entries.GetAsync(id);
        Assert.Equal("Newer elsewhere", stored!.Title);

        var cloud = await environment.Cloud.DownloadAsync(serializer.GetObjectPath(EntityType.MealEntry, id));
        Assert.NotNull(cloud?.Content);
        Assert.Contains("Newer elsewhere", System.Text.Encoding.UTF8.GetString(cloud!.Content!), StringComparison.Ordinal);
    }

}
