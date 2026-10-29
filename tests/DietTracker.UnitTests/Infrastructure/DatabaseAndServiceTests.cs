using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Infrastructure.Database;
using DietTracker.UnitTests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DietTracker.UnitTests.Infrastructure;

/// <summary>Database schema, migrations and the guarantees the Store update path depends on.</summary>
public sealed class DatabaseTests
{
    [Fact]
    public async Task A_new_database_is_created_at_the_expected_schema_version()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var version = await environment.Database.GetSchemaVersionAsync();

        Assert.Equal(environment.Database.TargetSchemaVersion, version);
        Assert.True(File.Exists(Path.Combine(environment.RootPath, "database", "diettracker.db")));
    }

    [Fact]
    public async Task Initializing_twice_is_idempotent_and_applies_no_migration_the_second_time()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var result = await environment.Database.InitializeAsync();

        Assert.False(result.Upgraded);
        Assert.Empty(result.AppliedMigrations);
    }

    [Fact]
    public async Task An_older_database_is_upgraded_transactionally_and_keeps_its_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "diettracker-tests", "upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new DietTracker.Infrastructure.FileStorage.AppDataPaths(root);
        paths.EnsureCreated();

        var factory = new SqliteConnectionFactory(paths);

        // Simulate a database created by the first release: the v1 schema plus a record, marked as v1.
        var assembly = typeof(DatabaseInitializer).Assembly;
        var resourceName = assembly.GetManifestResourceNames().Single(n => n.EndsWith("0001_initial.sql", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        var v1Script = await reader.ReadToEndAsync();

        await using (var connection = await factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = v1Script
                + "\nINSERT INTO UserProfile (Id, ProviderType, ProviderUserId, DisplayName, Email, Language, TimeZone, IsActive, CreatedAt, UpdatedAt, SyncStatus)"
                + " VALUES ('11111111-1111-1111-1111-111111111111', 0, '', 'Guest', '', 'ro-RO', 'Europe/Bucharest', 1, '2026-01-01T00:00:00+00:00', '2026-01-01T00:00:00+00:00', 0);"
                + "\nPRAGMA user_version = 1;";
            await command.ExecuteNonQueryAsync();
        }

        var initializer = new DatabaseInitializer(factory, paths, new TestLogger<DatabaseInitializer>());
        var result = await initializer.InitializeAsync();

        Assert.True(result.Upgraded);
        Assert.Equal(1, result.FromVersion);
        Assert.Equal(initializer.TargetSchemaVersion, result.ToVersion);
        Assert.Equal(initializer.TargetSchemaVersion - result.FromVersion, result.AppliedMigrations.Count);

        // A safety copy of the pre-migration state exists.
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath!));

        // The existing profile survived the upgrade and the new column is usable.
        await using (var connection = await factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT DisplayName FROM UserProfile WHERE Id = '11111111-1111-1111-1111-111111111111';";
            Assert.Equal("Guest", (string?)await command.ExecuteScalarAsync());

            await using var column = connection.CreateCommand();
            column.CommandText = "SELECT OnboardingCompleted FROM AppPreferences LIMIT 1;";
            Assert.Null(await column.ExecuteScalarAsync());
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The SQLite connection pool may still hold the file; Windows cleans the temp folder up.
        }
    }

    [Fact]
    public async Task A_database_created_by_a_newer_build_is_refused_instead_of_opened()
    {
        var root = Path.Combine(Path.GetTempPath(), "diettracker-tests", "future-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new DietTracker.Infrastructure.FileStorage.AppDataPaths(root);
        paths.EnsureCreated();

        var factory = new SqliteConnectionFactory(paths);
        await using (var connection = await factory.OpenAsync())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            await command.ExecuteNonQueryAsync();
        }

        var initializer = new DatabaseInitializer(factory, paths, new TestLogger<DatabaseInitializer>());

        await Assert.ThrowsAsync<DatabaseSchemaTooNewException>(() => initializer.InitializeAsync());

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
            // The SQLite connection pool may still hold the file; Windows cleans the temp folder up.
        }
    }

    [Fact]
    public async Task The_schema_enforces_one_slot_per_profile_date_and_category()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var profile = await environment.Services.GetRequiredService<IProfileService>().GetOrCreateActiveProfileAsync();
        var categories = await environment.Tracking.GetCategoriesAsync();
        var lunch = categories.First(c => c.FixedCategoryKey == MealCategoryKey.Lunch);
        var date = new DateOnly(2026, 3, 10);

        var first = new MealEntry
        {
            LocalProfileId = profile.Id,
            Date = date,
            MealCategoryId = lunch.Id,
            MealCategoryKey = MealCategoryKey.Lunch,
            Title = "First",
        };

        await environment.Entries.UpsertAsync(first);

        // A second row for the same slot is merged into the existing one instead of creating a duplicate.
        var second = new MealEntry
        {
            Id = Guid.NewGuid(),
            LocalProfileId = profile.Id,
            Date = date,
            MealCategoryId = lunch.Id,
            MealCategoryKey = MealCategoryKey.Lunch,
            Title = "Second",
        };

        await environment.Entries.UpsertAsync(second);

        var stored = await environment.Entries.GetForDateAsync(profile.Id, date);
        Assert.Single(stored);
        Assert.Equal("Second", stored[0].Title);
    }

    [Fact]
    public async Task The_integrity_checker_reports_a_healthy_database()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var checker = environment.Services.GetRequiredService<IDataIntegrityChecker>();

        var issues = await checker.CheckAsync();

        Assert.Empty(issues);
    }

    [Fact]
    public async Task The_journal_coalesces_repeated_edits_of_the_same_record()
    {
        await using var environment = await TestEnvironment.CreateAsync(connectAccount: true);
        var account = await environment.Accounts.GetActiveAsync();
        var operations = environment.Services.GetRequiredService<ISyncOperationRepository>();
        var entityId = Guid.NewGuid();

        for (var revision = 1; revision <= 5; revision++)
        {
            await operations.EnqueueAsync(new SyncOperation
            {
                CloudAccountId = account!.Id,
                EntityType = EntityType.MealEntry,
                EntityId = entityId,
                OperationType = SyncOperationType.Upsert,
                EntityRevision = revision,
            });
        }

        var pending = await operations.GetPendingAsync(account!.Id, 100);

        Assert.Single(pending);
        Assert.Equal(5, pending[0].EntityRevision);
    }
}

/// <summary>The five permanent cards and the DONE/PENDING lifecycle as seen by the application service.</summary>
public sealed class MealTrackingServiceTests
{
    [Fact]
    public async Task Every_date_always_exposes_exactly_five_slots()
    {
        await using var environment = await TestEnvironment.CreateAsync();

        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));

        Assert.Equal(5, day.Count);
        Assert.Equal(
            [MealCategoryKey.Breakfast, MealCategoryKey.MorningSnack, MealCategoryKey.Lunch, MealCategoryKey.AfternoonSnack, MealCategoryKey.Dinner],
            day.Select(e => e.MealCategoryKey).ToArray());
        Assert.All(day, e => Assert.Equal(MealStatus.Pending, e.Status));
    }

    [Fact]
    public async Task Slots_of_different_dates_are_independent()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var first = new DateOnly(2026, 3, 10);
        var second = new DateOnly(2026, 3, 11);

        var firstDay = await environment.Tracking.GetDayAsync(first);
        var lunch = firstDay.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = "Lentils";
        lunch.Ingredients = "Lentil stew";
        await environment.Tracking.SaveEditsAsync(lunch);
        await environment.Tracking.MarkDoneAsync(lunch.Id);

        var secondDay = await environment.Tracking.GetDayAsync(second);
        var secondLunch = secondDay.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);

        Assert.Equal(string.Empty, secondLunch.Title);
        Assert.Equal(MealStatus.Pending, secondLunch.Status);
    }

    [Fact]
    public async Task Editing_a_completed_meal_keeps_its_completion_state()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);
        var day = await environment.Tracking.GetDayAsync(date);
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);

        lunch.Title = "Soup";
        lunch.Ingredients = "With bread";
        await environment.Tracking.SaveEditsAsync(lunch);
        var completed = await environment.Tracking.MarkDoneAsync(lunch.Id);
        var completedAt = completed.CompletedAt;

        completed.Title = "Edited later";
        await environment.Tracking.SaveEditsAsync(completed);

        var stored = await environment.Entries.GetAsync(lunch.Id);
        Assert.Equal(MealStatus.Done, stored!.Status);
        Assert.Equal(completedAt, stored.CompletedAt);
        Assert.Equal("Edited later", stored.Title);
    }

    [Fact]
    public async Task Saving_without_a_title_or_a_description_is_refused_and_writes_nothing()
    {
        // The two fields are mandatory for every save - the same rule behind the card's own Save
        // button and behind "Save all" - so an incomplete card is reported and stays untouched.
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);
        var day = await environment.Tracking.GetDayAsync(date);
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = "   ";
        lunch.Ingredients = string.Empty;

        var result = await environment.Tracking.SaveEditsAsync(lunch);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "meal.title.required");
        Assert.Contains(result.Failures, f => f.Code == "meal.ingredients.required");

        var reread = await environment.Tracking.GetDayAsync(date);
        Assert.Equal(string.Empty, reread.Single(e => e.MealCategoryKey == MealCategoryKey.Lunch).Title);
    }

    [Fact]
    public async Task Marking_done_twice_increments_the_statistics_only_once()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);
        var day = await environment.Tracking.GetDayAsync(date);
        var breakfast = day.First(e => e.MealCategoryKey == MealCategoryKey.Breakfast);
        breakfast.Title = "Porridge";
        breakfast.Ingredients = "Milk";
        await environment.Tracking.SaveEditsAsync(breakfast);

        await environment.Tracking.MarkDoneAsync(breakfast.Id);
        await environment.Tracking.MarkDoneAsync(breakfast.Id);

        var progress = await environment.Tracking.GetDayProgressAsync(date);
        Assert.Equal(1, progress.Completed);
        Assert.Equal(0, progress.Missed);

        // The other four slots are untouched placeholders: they still count towards the total, so
        // one confirmed meal out of the five permanent slots is twenty percent.
        Assert.Equal(4, progress.Pending);
        Assert.Equal(20d, progress.Percentage);
    }

    [Fact]
    public async Task Marking_a_meal_missed_moves_it_from_the_done_count_to_the_missed_count()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);
        var day = await environment.Tracking.GetDayAsync(date);
        var dinner = day.First(e => e.MealCategoryKey == MealCategoryKey.Dinner);
        dinner.Title = "Fish";
        dinner.Ingredients = "Green salad";
        await environment.Tracking.SaveEditsAsync(dinner);

        await environment.Tracking.MarkDoneAsync(dinner.Id);
        await environment.Tracking.MarkMissedAsync(dinner.Id);

        var progress = await environment.Tracking.GetDayProgressAsync(date);
        Assert.Equal(0, progress.Completed);
        Assert.Equal(1, progress.Missed);
        Assert.Equal(4, progress.Pending);
    }

    [Fact]
    public async Task Completing_a_meal_cancels_its_pending_reminder()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = "Soup";
        lunch.Ingredients = "With bread";
        await environment.Tracking.SaveEditsAsync(lunch);

        await environment.Tracking.MarkDoneAsync(lunch.Id);

        Assert.Contains(lunch.Id.ToString("D"), environment.Notifications.Cancelled);
    }

    [Fact]
    public async Task Copying_a_meal_into_another_date_produces_a_pending_meal()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var source = new DateOnly(2026, 3, 10);
        var target = new DateOnly(2026, 3, 12);

        var day = await environment.Tracking.GetDayAsync(source);
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = "Pasta";
        lunch.Ingredients = "Tomato sauce";
        await environment.Tracking.SaveEditsAsync(lunch);
        await environment.Tracking.MarkDoneAsync(lunch.Id);

        var copy = await environment.Tracking.CopyMealToDateAsync(lunch.Id, target);

        Assert.Equal(MealStatus.Pending, copy.Status);
        Assert.Null(copy.CompletedAt);
        Assert.Equal("Pasta", copy.Title);
        Assert.Equal(target, copy.Date);
    }

    [Fact]
    public async Task Duplicating_a_day_copies_only_the_slots_that_have_content()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var source = new DateOnly(2026, 3, 10);
        var target = new DateOnly(2026, 3, 11);

        var day = await environment.Tracking.GetDayAsync(source);
        var breakfast = day.First(e => e.MealCategoryKey == MealCategoryKey.Breakfast);
        breakfast.Title = "Porridge";
        breakfast.Ingredients = "Milk";
        await environment.Tracking.SaveEditsAsync(breakfast);

        var copied = await environment.Tracking.DuplicateDayAsync(source, target);

        Assert.Equal(1, copied);
        var targetDay = await environment.Tracking.GetDayAsync(target);
        Assert.Equal("Porridge", targetDay.First(e => e.MealCategoryKey == MealCategoryKey.Breakfast).Title);
        Assert.Equal(string.Empty, targetDay.First(e => e.MealCategoryKey == MealCategoryKey.Lunch).Title);
    }

    [Fact]
    public async Task The_month_progress_carries_every_recorded_slot_the_calendar_squares_need()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);

        // One confirmed meal with a title and a photograph, one missed with a title; the rest of
        // the day stays untouched.
        var day = await environment.Tracking.GetDayAsync(date);
        var breakfast = day.First(e => e.MealCategoryKey == MealCategoryKey.Breakfast);
        breakfast.Title = "Porridge with berries";
        breakfast.Ingredients = "Oats, milk and berries";
        await environment.Tracking.SaveEditsAsync(breakfast);
        await environment.Tracking.MarkDoneAsync(breakfast.Id);
        await environment.Tracking.AddPhotoAsync(breakfast.Id, CreateTestJpeg(environment.RootPath));

        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        lunch.Title = "Soup";
        lunch.Ingredients = "With bread";
        await environment.Tracking.SaveEditsAsync(lunch);
        await environment.Tracking.MarkMissedAsync(lunch.Id);

        var month = await environment.Tracking.GetMonthProgressAsync(date);

        var recorded = Assert.Single(month.Days, d => d.Date == date);
        var slots = recorded.Slots;

        // The calendar draws one square per recorded slot: title, state and first picture all
        // have to survive the range query (the photographs live in their own table).
        var breakfastSlot = Assert.Single(slots, s => s.MealCategoryKey == MealCategoryKey.Breakfast);
        Assert.Equal(MealStatus.Done, breakfastSlot.Status);
        Assert.Equal("Porridge with berries", breakfastSlot.Title);
        Assert.Equal(1, breakfastSlot.PhotoCount);
        Assert.False(string.IsNullOrEmpty(breakfastSlot.FirstPhotoThumbnailId));

        var lunchSlot = Assert.Single(slots, s => s.MealCategoryKey == MealCategoryKey.Lunch);
        Assert.Equal(MealStatus.Missed, lunchSlot.Status);
        Assert.Equal("Soup", lunchSlot.Title);
        Assert.Equal("With bread", lunchSlot.Ingredients);

        // An untouched slot stays pending without a title, so it never produces a visible square.
        var dinnerSlot = Assert.Single(slots, s => s.MealCategoryKey == MealCategoryKey.Dinner);
        Assert.Equal(MealStatus.Pending, dinnerSlot.Status);
        Assert.Equal(string.Empty, dinnerSlot.Title);
        Assert.Equal(string.Empty, dinnerSlot.Ingredients);
        Assert.Equal(string.Empty, dinnerSlot.FirstPhotoThumbnailId);
    }

    [Fact]
    public async Task Clearing_a_meal_wipes_its_text_state_and_photographs()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var date = new DateOnly(2026, 3, 10);
        var day = await environment.Tracking.GetDayAsync(date);
        var breakfast = day.First(e => e.MealCategoryKey == MealCategoryKey.Breakfast);
        breakfast.Title = "Porridge with berries";
        breakfast.Ingredients = "Oats, milk and berries";
        breakfast.PlannedTime = new TimeOnly(8, 0);
        await environment.Tracking.SaveEditsAsync(breakfast);
        await environment.Tracking.MarkDoneAsync(breakfast.Id);
        var photo = await environment.Tracking.AddPhotoAsync(breakfast.Id, CreateTestJpeg(environment.RootPath));
        var store = environment.Services.GetRequiredService<IPhotoStore>();
        var thumbnailPath = store.GetThumbnailPath(photo.ThumbnailId);

        var cleared = await environment.Tracking.ClearMealAsync(breakfast.Id);

        // The whole content of the card is gone: text, planned time and the completion state.
        Assert.Equal(string.Empty, cleared.Title);
        Assert.Equal(string.Empty, cleared.Ingredients);
        Assert.Null(cleared.PlannedTime);
        Assert.Equal(MealStatus.Pending, cleared.Status);
        Assert.Null(cleared.CompletedAt);

        // The photographs were wiped with their local files as well.
        Assert.Empty(cleared.Photos);
        Assert.Empty(await environment.Tracking.GetPhotosAsync(breakfast.Id));
        Assert.NotNull(thumbnailPath);
        Assert.False(File.Exists(thumbnailPath));

        // The permanent slot itself survives as an empty pending placeholder, so the dashboard and
        // the calendar keep their five cards.
        var reread = await environment.Tracking.GetDayAsync(date);
        Assert.Equal(MealStatus.Pending, reread.Single(e => e.MealCategoryKey == MealCategoryKey.Breakfast).Status);
    }

    [Fact]
    public async Task Templates_can_be_saved_and_applied_to_another_date()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var dinner = day.First(e => e.MealCategoryKey == MealCategoryKey.Dinner);
        dinner.Title = "Fish and salad";
        dinner.Ingredients = "Green salad";
        await environment.Tracking.SaveEditsAsync(dinner);

        var template = await environment.Tracking.SaveTemplateAsync(dinner, "Dinner template");
        var target = new DateOnly(2026, 3, 20);
        await environment.Tracking.ApplyTemplateAsync(template.Id, target);

        var targetDay = await environment.Tracking.GetDayAsync(target);
        var applied = targetDay.First(e => e.MealCategoryKey == MealCategoryKey.Dinner);

        Assert.Equal("Fish and salad", applied.Title);
        Assert.Equal(MealStatus.Pending, applied.Status);
    }

    [Fact]
    public async Task A_fourth_photograph_is_rejected_for_the_same_meal()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);

        // Three photographs are already attached. They are inserted directly so the test does not
        // depend on distinct image content, which the de-duplication logic would otherwise collapse.
        for (var index = 0; index < 3; index++)
        {
            await environment.Photos.AddAsync(new MealPhoto
            {
                MealEntryId = lunch.Id,
                LocalFileId = $"file-{index}.jpg",
                ThumbnailId = $"thumb-{index}.png",
                ContentHash = $"hash-{index}",
            });
        }

        var photoPath = CreateTestJpeg(environment.RootPath);

        var exception = await Assert.ThrowsAsync<global::DietTracker.Core.BusinessRules.ValidationException>(
            () => environment.Tracking.AddPhotoAsync(lunch.Id, photoPath));

        Assert.Contains(exception.Result.Failures, f => f.Code == "photo.limit.reached");
    }

    [Fact]
    public async Task The_same_photograph_is_refused_when_it_is_uploaded_twice()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        var photoPath = CreateTestJpeg(environment.RootPath);

        var photo = await environment.Tracking.AddPhotoAsync(lunch.Id, photoPath);

        var exception = await Assert.ThrowsAsync<global::DietTracker.Core.BusinessRules.ValidationException>(
            () => environment.Tracking.AddPhotoAsync(lunch.Id, photoPath));

        Assert.Contains(exception.Result.Failures, f => f.Code == "photo.duplicate");

        // The repeat is refused before anything is copied: the meal still has exactly one photograph.
        var attached = await environment.Tracking.GetPhotosAsync(lunch.Id);
        Assert.Single(attached);
        Assert.Equal(photo.Id, attached[0].Id);
    }

    [Fact]
    public async Task The_same_photograph_may_be_attached_to_a_different_meal()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        var dinner = day.First(e => e.MealCategoryKey == MealCategoryKey.Dinner);
        var photoPath = CreateTestJpeg(environment.RootPath);

        var lunchPhoto = await environment.Tracking.AddPhotoAsync(lunch.Id, photoPath);
        var dinnerPhoto = await environment.Tracking.AddPhotoAsync(dinner.Id, photoPath);

        // Identical bytes are only a duplicate within one meal; two meals may show the same dish.
        Assert.NotEqual(lunchPhoto.Id, dinnerPhoto.Id);
        Assert.Equal(lunchPhoto.ContentHash, dinnerPhoto.ContentHash);
    }

    [Fact]
    public async Task Photographs_are_available_offline_through_the_local_photo_store()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);
        var photoPath = CreateTestJpeg(environment.RootPath);

        var photo = await environment.Tracking.AddPhotoAsync(lunch.Id, photoPath);

        var store = environment.Services.GetRequiredService<IPhotoStore>();
        var localPath = store.GetFullSizePath(photo.LocalFileId);

        Assert.NotNull(localPath);
        Assert.True(File.Exists(localPath));
        Assert.False(string.IsNullOrEmpty(photo.ThumbnailId));
        Assert.True(File.Exists(store.GetThumbnailPath(photo.ThumbnailId)));
    }

    [Fact]
    public async Task An_invalid_image_file_is_rejected()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        var day = await environment.Tracking.GetDayAsync(new DateOnly(2026, 3, 10));
        var lunch = day.First(e => e.MealCategoryKey == MealCategoryKey.Lunch);

        var fake = Path.Combine(environment.RootPath, "not-an-image.jpg");
        await File.WriteAllBytesAsync(fake, "MZthis-is-not-an-image"u8.ToArray());

        await Assert.ThrowsAsync<global::DietTracker.Core.BusinessRules.ValidationException>(
            () => environment.Tracking.AddPhotoAsync(lunch.Id, fake));
    }

    /// <summary>
    /// Writes a real (1x1 pixel) JPEG so that the Windows Imaging Component can decode it and the
    /// thumbnail pipeline is exercised end to end.
    /// </summary>
    internal static string CreateTestJpeg(string directory)
    {
        var path = Path.Combine(directory, "test-image.jpg");
        if (File.Exists(path))
        {
            return path;
        }

        File.WriteAllBytes(path, Convert.FromBase64String(
            "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q=="));

        return path;
    }
}
