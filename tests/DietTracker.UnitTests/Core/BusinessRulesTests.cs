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

        // Content is mandatory for DONE (see the empty-content rule below); these tests exercise
        // the state machine, so the entry carries a title from the start.
        Title = "Lunch",
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
    public void Marking_missed_sets_the_missed_state_and_clears_the_timestamp()
    {
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        CompletionRules.MarkDone(entry, DateTimeOffset.UtcNow);

        var outcome = CompletionRules.MarkMissed(entry, DateTimeOffset.UtcNow);

        Assert.True(outcome.Changed);
        Assert.Equal("marked-missed", outcome.Reason);
        Assert.Equal(MealStatus.Missed, entry.Status);
        Assert.Null(entry.CompletedAt);
    }

    [Fact]
    public void Repeated_missed_actions_keep_the_meal_missed_and_never_change_anything()
    {
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        CompletionRules.MarkMissed(entry, DateTimeOffset.UtcNow);
        var revision = entry.Revision;

        var outcome = CompletionRules.MarkMissed(entry, DateTimeOffset.UtcNow);

        Assert.False(outcome.Changed);
        Assert.Equal("already-missed", outcome.Reason);
        Assert.Equal(MealStatus.Missed, entry.Status);
        Assert.Equal(revision, entry.Revision);
    }

    [Fact]
    public void A_missed_meal_of_a_past_day_can_still_be_confirmed_as_eaten()
    {
        // The user comes back to a forgotten day and records what actually happened.
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        CompletionRules.MarkMissed(entry, DateTimeOffset.UtcNow);

        var outcome = CompletionRules.MarkDone(entry, DateTimeOffset.UtcNow);

        Assert.True(outcome.Changed);
        Assert.Equal(MealStatus.Done, entry.Status);
        Assert.NotNull(entry.CompletedAt);
    }

    [Fact]
    public void An_empty_meal_cannot_be_confirmed_until_it_has_content()
    {
        // The green check refuses the untouched placeholder: nothing is known about the meal, so
        // it cannot be eaten yet (the card explains this and offers to open the editor).
        var entry = CreateEntry(new DateOnly(2026, 3, 10));
        entry.Title = string.Empty;
        entry.Ingredients = string.Empty;

        var refused = CompletionRules.MarkDone(entry, DateTimeOffset.UtcNow);

        Assert.False(refused.Changed);
        Assert.Equal("empty-content", refused.Reason);
        Assert.Equal(MealStatus.Pending, entry.Status);
        Assert.Null(entry.CompletedAt);

        // Content in either field lifts the restriction: a title alone is enough.
        entry.Title = "Soup";
        var allowed = CompletionRules.MarkDone(entry, DateTimeOffset.UtcNow);

        Assert.True(allowed.Changed);
        Assert.Equal(MealStatus.Done, entry.Status);
    }

    [Fact]
    public void A_future_meal_is_locked_in_pending_no_matter_which_button_is_pressed()
    {
        // Tomorrow's meals can be neither confirmed nor marked missed.
        var tomorrow = DateOnly.FromDateTime(DateTime.Now).AddDays(1);
        var entry = CreateEntry(tomorrow);

        var done = CompletionRules.MarkDone(entry, DateTimeOffset.Now);
        var missed = CompletionRules.MarkMissed(entry, DateTimeOffset.Now);

        Assert.False(done.Changed);
        Assert.Equal("future-locked", done.Reason);
        Assert.False(missed.Changed);
        Assert.Equal("future-locked", missed.Reason);
        Assert.Equal(MealStatus.Pending, entry.Status);
        Assert.Null(entry.CompletedAt);
    }

    [Fact]
    public void The_meal_status_model_has_exactly_three_states()
    {
        // PENDING is the default, DONE the confirmation and MISSED the explicit red state; the
        // stored values must stay stable because they live in the database and in sync payloads.
        var states = Enum.GetValues<MealStatus>();

        Assert.Equal(3, states.Length);
        Assert.Equal(MealStatus.Pending, Enum.Parse<MealStatus>("0"));
        Assert.Equal(MealStatus.Done, Enum.Parse<MealStatus>("1"));
        Assert.Equal(MealStatus.Missed, Enum.Parse<MealStatus>("2"));
    }

    [Fact]
    public void A_past_pending_meal_stays_pending_until_the_user_confirms_it()
    {
        // The app never reclassifies a meal: what the user never confirmed simply stays PENDING,
        // no matter how old the date is.
        var today = new DateOnly(2026, 3, 10);
        var entry = CreateEntry(today.AddDays(-2));

        Assert.Equal(MealStatus.Pending, entry.Status);

        CompletionRules.MarkDone(entry, DateTimeOffset.UtcNow);

        Assert.Equal(MealStatus.Done, entry.Status);
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
            PlannedTime = new TimeOnly(13, 30),
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
    public void Copying_preserves_title_ingredients_and_time()
    {
        var source = CreateCompletedEntryWithPhoto(new DateOnly(2026, 3, 10));

        var copy = MealCopyRules.CopyMealToDate(source, new DateOnly(2026, 3, 11), source.LocalProfileId, DateTimeOffset.UtcNow);

        Assert.Equal(source.Title, copy.Title);
        Assert.Equal(source.Ingredients, copy.Ingredients);
        Assert.Equal(source.PlannedTime, copy.PlannedTime);
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
        };

        var entry = MealCopyRules.ApplyTemplateContent(content, Guid.NewGuid(), new DateOnly(2026, 4, 1), Guid.NewGuid(), DateTimeOffset.UtcNow);

        Assert.Equal(MealStatus.Pending, entry.Status);
        Assert.Equal("Soup", entry.Title);
    }
}
