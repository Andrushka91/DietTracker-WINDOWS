using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using Xunit;

namespace DietTracker.UnitTests.Core;

/// <summary>
/// Tests of the mandatory business rules: the five permanent meal cards, the completion lifecycle and
/// the copying rules. These tests encode the requirements that must never regress.
/// </summary>
public sealed class MealSlotRulesTests
{
    [Fact]
    public void The_five_permanent_slots_are_always_available()
    {
        Assert.Equal(5, MealSlotRules.SlotCount);
        Assert.Equal(
            [MealCategoryKey.Breakfast, MealCategoryKey.MorningSnack, MealCategoryKey.Lunch, MealCategoryKey.AfternoonSnack, MealCategoryKey.Dinner],
            MealSlotRules.AllSlots);
    }

    [Fact]
    public void Breakfast_lunch_and_dinner_are_main_meals_and_snacks_are_compact()
    {
        Assert.True(MealSlotRules.IsMainMeal(MealCategoryKey.Breakfast));
        Assert.True(MealSlotRules.IsMainMeal(MealCategoryKey.Lunch));
        Assert.True(MealSlotRules.IsMainMeal(MealCategoryKey.Dinner));
        Assert.True(MealSlotRules.IsSnack(MealCategoryKey.MorningSnack));
        Assert.True(MealSlotRules.IsSnack(MealCategoryKey.AfternoonSnack));
        Assert.False(MealSlotRules.IsMainMeal(MealCategoryKey.MorningSnack));
    }

    [Fact]
    public void Every_created_category_is_permanent()
    {
        var defaults = MealSlotRules.CreateDefaults();
        Assert.Equal(5, defaults.Count);
        Assert.All(defaults, c => Assert.True(MealSlotRules.IsPermanent(c.FixedCategoryKey)));
    }

    [Fact]
    public void A_category_that_disappeared_is_restored_instead_of_being_removed()
    {
        var persisted = MealSlotRules.CreateDefaults().Where(c => c.FixedCategoryKey != MealCategoryKey.Lunch).ToList();
        var normalized = MealSlotRules.Normalize(persisted);

        Assert.Equal(5, normalized.Count);
        Assert.Contains(normalized, c => c.FixedCategoryKey == MealCategoryKey.Lunch);
        Assert.Empty(MealSlotRules.FindMissingSlots(normalized));
    }

    [Fact]
    public void Missing_slots_are_reported_for_the_integrity_checker()
    {
        var persisted = MealSlotRules.CreateDefaults().Take(3).ToList();
        var missing = MealSlotRules.FindMissingSlots(persisted);

        Assert.Equal(2, missing.Count);
        Assert.Contains(MealCategoryKey.AfternoonSnack, missing);
        Assert.Contains(MealCategoryKey.Dinner, missing);
    }

    [Fact]
    public void Categories_cannot_be_removed_by_normalizing_an_empty_collection()
    {
        var normalized = MealSlotRules.Normalize([]);
        Assert.Equal(5, normalized.Count);
    }
}

public sealed class CompletionRulesTests
{
    private static MealEntry CreateEntry(DateOnly date) => new()
    {
        Date = date,
        MealCategoryKey = MealCategoryKey.Lunch,
        MealCategoryId = Guid.NewGuid(),
        LocalProfileId = Guid.NewGuid(),
    };

    [Fact]
    public void Marking_done_sets_status_and_records_the_timestamp()
    {
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        var now = DateTimeOffset.UtcNow;

        var outcome = CompletionRules.MarkDone(entry, now);

        Assert.True(outcome.Changed);
        Assert.Equal(MealStatus.Done, entry.Status);
        Assert.Equal(now, entry.CompletedAt);
        Assert.Equal(1, entry.Revision);
    }

    [Fact]
    public void Repeated_done_actions_are_idempotent_and_never_double_count()
    {
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        var first = DateTimeOffset.UtcNow;
        CompletionRules.MarkDone(entry, first);

        var outcome = CompletionRules.MarkDone(entry, first.AddHours(2));

        Assert.False(outcome.Changed);
        Assert.Equal("already-done", outcome.Reason);
        Assert.Equal(first, entry.CompletedAt);
        Assert.Equal(1, entry.Revision);
    }

    [Fact]
    public void Undo_returns_the_meal_to_pending_and_clears_the_timestamp()
    {
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        CompletionRules.MarkDone(entry, DateTimeOffset.UtcNow);

        var outcome = CompletionRules.UndoToPending(entry, DateTimeOffset.UtcNow);

        Assert.True(outcome.Changed);
        Assert.Equal(MealStatus.Pending, entry.Status);
        Assert.Null(entry.CompletedAt);
    }

    [Fact]
    public void A_future_meal_can_never_be_classified_as_missed()
    {
        var today = new DateOnly(2026, 3, 10);
        var entry = CreateEntry(today.AddDays(1));

        var outcome = CompletionRules.MarkMissed(entry, today, DateTimeOffset.UtcNow);

        Assert.False(outcome.Changed);
        Assert.Equal("future-date-cannot-be-missed", outcome.Reason);
        Assert.Equal(MealStatus.Pending, entry.Status);
    }

    [Fact]
    public void A_past_pending_meal_can_be_marked_as_missed()
    {
        var today = new DateOnly(2026, 3, 10);
        var entry = CreateEntry(today.AddDays(-2));

        var outcome = CompletionRules.MarkMissed(entry, today, DateTimeOffset.UtcNow);

        Assert.True(outcome.Changed);
        Assert.Equal(MealStatus.Missed, entry.Status);
    }

    [Fact]
    public void The_historical_rule_only_applies_to_days_before_today()
    {
        var today = new DateOnly(2026, 3, 10);
        var yesterday = CreateEntry(today.AddDays(-1));
        var current = CreateEntry(today);

        Assert.True(CompletionRules.IsHistoricallyMissed(yesterday, today, new TimeOnly(12, 0), TimeSpan.Zero));
        Assert.False(CompletionRules.IsHistoricallyMissed(current, today, new TimeOnly(12, 0), TimeSpan.Zero));
    }
}

public sealed class MealCopyRulesTests
{
    private static MealEntry CreateCompletedEntryWithPhoto(DateOnly date)
    {
        var entry = new MealEntry
        {
            Date = date,
            MealCategoryKey = MealCategoryKey.Lunch,
            LocalProfileId = Guid.NewGuid(),
            MealCategoryId = Guid.NewGuid(),
            Title = "Rice with vegetables",
            Ingredients = "Rice, peppers, onion",
            Notes = "Light",
            PlannedTime = new TimeOnly(13, 30),
            Quantities = [IngredientLine.Create("Rice", 80, MeasurementUnit.Gram)],
            Status = MealStatus.Done,
            CompletedAt = DateTimeOffset.UtcNow,
            Photos = [new MealPhoto { LocalFileId = "abc.jpg" }],
        };

        return entry;
    }

    [Fact]
    public void A_copied_meal_starts_pending_and_carries_no_photographs()
    {
        var source = CreateCompletedEntryWithPhoto(new DateOnly(2026, 3, 10));

        var copy = MealCopyRules.CopyMealToDate(source, new DateOnly(2026, 3, 11), source.LocalProfileId, DateTimeOffset.UtcNow);

        Assert.Equal(MealStatus.Pending, copy.Status);
        Assert.Null(copy.CompletedAt);
        Assert.Empty(copy.Photos);
        Assert.NotEqual(source.Id, copy.Id);
    }

    [Fact]
    public void Copying_preserves_title_ingredients_quantities_notes_and_time()
    {
        var source = CreateCompletedEntryWithPhoto(new DateOnly(2026, 3, 10));

        var copy = MealCopyRules.CopyMealToDate(source, new DateOnly(2026, 3, 11), source.LocalProfileId, DateTimeOffset.UtcNow);

        Assert.Equal(source.Title, copy.Title);
        Assert.Equal(source.Ingredients, copy.Ingredients);
        Assert.Equal(source.Notes, copy.Notes);
        Assert.Equal(source.PlannedTime, copy.PlannedTime);
        Assert.Equal(source.Quantities.Count, copy.Quantities.Count);
    }

    [Fact]
    public void Templates_never_contain_completion_state_or_photographs()
    {
        var source = CreateCompletedEntryWithPhoto(new DateOnly(2026, 3, 10));

        var template = MealCopyRules.CreateTemplate(source, "Lunch template", DateTimeOffset.UtcNow);

        Assert.False(template.IsFullDay);
        Assert.NotNull(template.TemplateMeal);
        Assert.Equal(MealCategoryKey.Lunch, template.TemplateMealSlot);
        Assert.Equal(source.Title, template.TemplateMeal!.Title);
    }

    [Fact]
    public void Applying_a_template_produces_a_pending_meal()
    {
        var content = new MealTemplateContent
        {
            MealCategoryKey = MealCategoryKey.Dinner,
            Title = "Soup",
            Quantities = [IngredientLine.Create("Carrot", 2, MeasurementUnit.Unit)],
        };

        var entry = MealCopyRules.ApplyTemplateContent(content, Guid.NewGuid(), new DateOnly(2026, 4, 1), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(MealStatus.Pending, entry.Status);
        Assert.Equal("Soup", entry.Title);
        Assert.Single(entry.Quantities);
    }
}
