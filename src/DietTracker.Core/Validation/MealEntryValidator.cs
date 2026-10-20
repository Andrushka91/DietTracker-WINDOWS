using DietTracker.Core.BusinessRules;
using DietTracker.Core.Models;

namespace DietTracker.Core.Validation;

/// <summary>Validates a daily meal entry before it is written to SQLite.</summary>
public static class MealEntryValidator
{
    /// <summary>Maximum length accepted for free-text fields (keeps the UI and the cloud payload bounded).</summary>
    public const int MaxTitleLength = 120;

    public const int MaxIngredientsLength = 2000;

    public const int MaxNotesLength = 2000;

    public const int MaxIngredientLines = 40;

    public static ValidationResult Validate(MealEntry entry)
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

        if (entry.Title.Length > MaxTitleLength)
        {
            failures.Add(new ValidationFailure("meal.title.tooLong", nameof(entry.Title), $"Title must not exceed {MaxTitleLength} characters."));
        }

        if (entry.Ingredients.Length > MaxIngredientsLength)
        {
            failures.Add(new ValidationFailure("meal.ingredients.tooLong", nameof(entry.Ingredients), $"Ingredients must not exceed {MaxIngredientsLength} characters."));
        }

        if (entry.Notes.Length > MaxNotesLength)
        {
            failures.Add(new ValidationFailure("meal.notes.tooLong", nameof(entry.Notes), $"Notes must not exceed {MaxNotesLength} characters."));
        }

        if (entry.Quantities.Count > MaxIngredientLines)
        {
            failures.Add(new ValidationFailure("meal.quantities.tooMany", nameof(entry.Quantities), $"At most {MaxIngredientLines} ingredient lines are allowed."));
        }

        for (var i = 0; i < entry.Quantities.Count; i++)
        {
            var line = entry.Quantities[i];
            if (line.Name.Length > MaxTitleLength)
            {
                failures.Add(new ValidationFailure("meal.ingredient.nameTooLong", $"Quantities[{i}].Name", $"Ingredient name must not exceed {MaxTitleLength} characters."));
            }

            if (line.Amount is < 0)
            {
                failures.Add(new ValidationFailure("meal.ingredient.negativeAmount", $"Quantities[{i}].Amount", "Ingredient amount cannot be negative."));
            }

            if (line.Amount is > 100_000)
            {
                failures.Add(new ValidationFailure("meal.ingredient.amountTooLarge", $"Quantities[{i}].Amount", "Ingredient amount is unrealistically large."));
            }
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
