using DietTracker.Core.BusinessRules;
using DietTracker.Core.Models;

namespace DietTracker.Core.Validation;

/// <summary>Validates a daily meal entry before it is written to SQLite.</summary>
public static class MealEntryValidator
{
    /// <summary>Maximum length accepted for free-text fields (keeps the UI and the cloud payload bounded).</summary>
    public const int MaxTitleLength = 120;

    public const int MaxIngredientsLength = 2000;

    /// <summary>
    /// Validates a daily meal entry before it is written to SQLite.
    /// </summary>
    /// <param name="entry">The entry that is about to be persisted.</param>
    /// <param name="requireContent">
    /// Whether the two mandatory fields must be filled. Every entry saved from an editor needs a
    /// title and a description, so an empty card never reaches the database - neither through its
    /// own Save button nor through "Save all". The clear action passes <see langword="false"/>:
    /// it deliberately writes the empty placeholder of the slot back.
    /// </param>
    public static ValidationResult Validate(MealEntry entry, bool requireContent = true)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var failures = new List<ValidationFailure>();

        if (!MealSlotRules.IsPermanent(entry.MealCategoryKey))
        {
            failures.Add(new ValidationFailure("meal.category.invalid", nameof(entry.MealCategoryKey), "Meal category is not one of the five permanent slots."));
        }

        if (entry.MealCategoryId == Guid.Empty)
        {
            failures.Add(new ValidationFailure("meal.category.missing", nameof(entry.MealCategoryId), "Meal entry is not linked to a permanent category."));
        }

        if (entry.LocalProfileId == Guid.Empty)
        {
            failures.Add(new ValidationFailure("meal.profile.missing", nameof(entry.LocalProfileId), "Meal entry is not linked to a local profile."));
        }

        // Title and description are the two mandatory fields of a meal: both are checked before the
        // length rules, so a blank field reports "required" instead of nothing at all.
        if (requireContent)
        {
            if (string.IsNullOrWhiteSpace(entry.Title))
            {
                failures.Add(new ValidationFailure("meal.title.required", nameof(entry.Title), "A meal needs a title."));
            }

            if (string.IsNullOrWhiteSpace(entry.Ingredients))
            {
                failures.Add(new ValidationFailure("meal.ingredients.required", nameof(entry.Ingredients), "A meal needs a description."));
            }
        }

        if (entry.Title.Length > MaxTitleLength)
        {
            failures.Add(new ValidationFailure("meal.title.tooLong", nameof(entry.Title), $"Title must not exceed {MaxTitleLength} characters."));
        }

        if (entry.Ingredients.Length > MaxIngredientsLength)
        {
            failures.Add(new ValidationFailure("meal.ingredients.tooLong", nameof(entry.Ingredients), $"Ingredients must not exceed {MaxIngredientsLength} characters."));
        }

        if (entry.Status == Enums.MealStatus.Done && entry.CompletedAt is null)
        {
            failures.Add(new ValidationFailure("meal.completedAt.missing", nameof(entry.CompletedAt), "A completed meal must carry a completion timestamp."));
        }

        if (entry.Status != Enums.MealStatus.Done && entry.CompletedAt is not null)
        {
            failures.Add(new ValidationFailure("meal.completedAt.unexpected", nameof(entry.CompletedAt), "Only completed meals may carry a completion timestamp."));
        }

        return failures.Count == 0 ? ValidationResult.Success() : ValidationResult.Failure(failures);
    }
}
