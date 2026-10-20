using DietTracker.Core.Analytics;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using DietTracker.Core.Validation;
using Xunit;

namespace DietTracker.UnitTests.Core;

public sealed class ProgressCalculatorTests
{
    private static MealEntry Entry(DateOnly date, MealCategoryKey key, MealStatus status) => new()
    {
        Date = date,
        MealCategoryKey = key,
        Status = status,
        CompletedAt = status == MealStatus.Done ? DateTimeOffset.UtcNow : null,
    };

    [Fact]
    public void Three_completed_meals_out_of_five_is_sixty_percent()
    {
        var date = new DateOnly(2026, 3, 10);
        var entries = new[]
        {
            Entry(date, MealCategoryKey.Breakfast, MealStatus.Done),
            Entry(date, MealCategoryKey.MorningSnack, MealStatus.Done),
            Entry(date, MealCategoryKey.Lunch, MealStatus.Done),
            Entry(date, MealCategoryKey.AfternoonSnack, MealStatus.Pending),
            Entry(date, MealCategoryKey.Dinner, MealStatus.Pending),
        };

        var progress = ProgressCalculator.ForDay(date, entries);

        Assert.Equal(5, progress.TotalSlots);
        Assert.Equal(3, progress.Completed);
        Assert.Equal(60d, progress.Percentage);
        Assert.Equal(2, progress.Pending);
    }

    [Fact]
    public void A_day_without_records_is_zero_percent_and_never_one_hundred()
    {
        var progress = ProgressCalculator.ForDay(new DateOnly(2026, 3, 10), []);

        Assert.Equal(0d, progress.Percentage);
        Assert.Equal(5, progress.Pending);
        Assert.Equal(5, progress.Slots.Count);
    }

    [Fact]
    public void A_fully_completed_day_is_detected()
    {
        var date = new DateOnly(2026, 3, 10);
        var entries = Enum.GetValues<MealCategoryKey>().Select(k => Entry(date, k, MealStatus.Done));

        var progress = ProgressCalculator.ForDay(date, entries);

        Assert.True(progress.IsFullyCompleted);
        Assert.Equal(100d, progress.Percentage);
    }

    [Fact]
    public void Weekly_aggregation_counts_every_day_of_the_week_including_empty_ones()
    {
        var monday = new DateOnly(2026, 3, 9);
        var range = new Dictionary<DateOnly, IReadOnlyList<MealEntry>>
        {
            [monday] = Enum.GetValues<MealCategoryKey>().Select(k => Entry(monday, k, MealStatus.Done)).ToArray(),
        };

        var week = PeriodAnalytics.ForWeek(monday, range);

        Assert.Equal(35, week.TotalSlots);
        Assert.Equal(5, week.Completed);
        Assert.Equal(1, week.FullyCompletedDays);
        Assert.Equal(Math.Round(5 * 100d / 35, 2), week.Percentage);
    }

    [Fact]
    public void Monthly_aggregation_covers_the_whole_month()
    {
        var month = new DateOnly(2026, 2, 15);
        var month2 = PeriodAnalytics.ForMonth(month, new Dictionary<DateOnly, IReadOnlyList<MealEntry>>());

        Assert.Equal(new DateOnly(2026, 2, 1), month2.From);
        Assert.Equal(new DateOnly(2026, 2, 28), month2.To);
        Assert.Equal(28 * 5, month2.TotalSlots);
    }

    [Fact]
    public void The_week_starts_on_monday()
    {
        Assert.Equal(new DateOnly(2026, 3, 9), PeriodAnalytics.StartOfWeek(new DateOnly(2026, 3, 15)));
    }

    [Fact]
    public void The_trend_series_has_one_point_per_day()
    {
        var from = new DateOnly(2026, 3, 9);
        var trend = PeriodAnalytics.Trend(from, from.AddDays(6), new Dictionary<DateOnly, IReadOnlyList<MealEntry>>());

        Assert.Equal(7, trend.Count);
        Assert.All(trend, point => Assert.Equal(0d, point.Percentage));
    }
}

public sealed class MealEntryValidatorTests
{
    private static MealEntry Valid() => new()
    {
        LocalProfileId = Guid.NewGuid(),
        MealCategoryId = Guid.NewGuid(),
        MealCategoryKey = MealCategoryKey.Breakfast,
        Date = new DateOnly(2026, 3, 10),
    };

    [Fact]
    public void A_valid_entry_passes()
    {
        Assert.True(MealEntryValidator.Validate(Valid()).IsValid);
    }

    [Fact]
    public void A_title_that_is_too_long_is_rejected()
    {
        var entry = Valid();
        entry.Title = new string('a', MealEntryValidator.MaxTitleLength + 1);

        var result = MealEntryValidator.Validate(entry);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "meal.title.tooLong");
    }

    [Fact]
    public void A_completed_meal_must_carry_a_timestamp_and_a_pending_meal_must_not()
    {
        var done = Valid();
        done.Status = MealStatus.Done;
        Assert.False(MealEntryValidator.Validate(done).IsValid);

        var pending = Valid();
        pending.CompletedAt = DateTimeOffset.UtcNow;
        Assert.False(MealEntryValidator.Validate(pending).IsValid);
    }

    [Fact]
    public void A_negative_ingredient_amount_is_rejected()
    {
        var entry = Valid();
        entry.Quantities = [IngredientLine.Create("Sugar", -5, MeasurementUnit.Gram)];

        Assert.False(MealEntryValidator.Validate(entry).IsValid);
    }
}

public sealed class PhotoValidatorTests
{
    private static readonly byte[] JpegHeader = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
    private static readonly byte[] WebpHeader = [0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50];

    [Theory]
    [InlineData("photo.jpg", 1024)]
    [InlineData("photo.jpeg", 1024)]
    [InlineData("photo.png", 2048)]
    public void Supported_formats_are_accepted(string fileName, long size)
    {
        var header = fileName.EndsWith(".png", StringComparison.Ordinal) ? PngHeader : JpegHeader;
        Assert.True(PhotoValidator.Validate(fileName, size, header).IsValid);
    }

    [Fact]
    public void Webp_is_accepted()
        => Assert.True(PhotoValidator.Validate("photo.webp", 4096, WebpHeader).IsValid);

    [Fact]
    public void An_unsupported_extension_is_rejected()
    {
        var result = PhotoValidator.Validate("photo.gif", 1024, JpegHeader);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "photo.format.unsupported");
    }

    [Fact]
    public void A_file_larger_than_ten_megabytes_is_rejected()
    {
        var result = PhotoValidator.Validate("photo.jpg", global::DietTracker.Core.BusinessRules.PhotoRules.MaxPhotoBytes + 1, JpegHeader);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "photo.tooLarge");
    }

    [Fact]
    public void Content_that_does_not_match_the_declared_format_is_rejected()
    {
        // A renamed executable would have an unrecognised signature.
        var result = PhotoValidator.Validate("photo.jpg", 1024, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00]);

        Assert.False(result.IsValid);
        Assert.Contains(result.Failures, f => f.Code == "photo.content.invalid");
    }

    [Fact]
    public void A_fourth_photograph_is_rejected()
    {
        Assert.True(PhotoValidator.ValidateSlotAvailability(2).IsValid);
        Assert.False(PhotoValidator.ValidateSlotAvailability(3).IsValid);
    }
}
