using DietTracker.Core.Models;

namespace DietTracker.Core.BusinessRules;

/// <summary>
/// Rules for duplicating meals, days and templates. Copied content always starts as PENDING:
/// completion state, completion timestamps and photographic evidence are deliberately not copied.
/// </summary>
public static class MealCopyRules
{
    /// <summary>
    /// Creates a new PENDING <see cref="MealEntry"/> for <paramref name="targetDate"/> from
    /// <paramref name="source"/>. Photos, status and completion timestamp are not copied.
    /// This method never mutates the source entry.
    /// </summary>
    public static MealEntry CopyMealToDate(MealEntry source, DateOnly targetDate, Guid targetProfileId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new MealEntry
        {
            Id = Guid.NewGuid(),
            LocalProfileId = targetProfileId,
            Date = targetDate,
            MealCategoryId = source.MealCategoryId,
            MealCategoryKey = source.MealCategoryKey,
            Title = source.Title,
            Ingredients = source.Ingredients,
            PlannedTime = source.PlannedTime,
            Status = Enums.MealStatus.Pending,
            CompletedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            Revision = 0,
            SyncStatus = Enums.SyncStatus.PendingUpload,
            Photos = [],
        };
    }

    /// <summary>
    /// Copies a whole day of meals onto another date. Only slots present in
    /// <paramref name="sourceMeals"/> are produced; missing slots stay untouched so that the caller
    /// can merge with existing content.
    /// </summary>
    public static IReadOnlyList<MealEntry> CopyDayToDate(
        IEnumerable<MealEntry> sourceMeals,
        DateOnly targetDate,
        Guid targetProfileId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sourceMeals);

        return sourceMeals
            .Select(meal => CopyMealToDate(meal, targetDate, targetProfileId, now))
            .ToArray();
    }

    /// <summary>Creates a reusable single-meal template from a daily entry (never carries status or photos).</summary>
    public static MealTemplate CreateTemplate(MealEntry source, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(source);

        return new MealTemplate
        {
            Id = Guid.NewGuid(),
            LocalProfileId = source.LocalProfileId,
            Name = name,
            IsFullDay = false,
            TemplateMealSlot = source.MealCategoryKey,
            TemplateMeal = ToTemplateContent(source),
            TemplateData = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Creates a reusable full-day template from five daily entries.</summary>
    public static MealTemplate CreateDayTemplate(IEnumerable<MealEntry> sourceMeals, Guid profileId, string name, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(sourceMeals);

        var ordered = sourceMeals.OrderBy(m => m.MealCategoryKey).ToArray();
        return new MealTemplate
        {
            Id = Guid.NewGuid(),
            LocalProfileId = profileId,
            Name = name,
            IsFullDay = true,
            TemplateDay = ordered.Select(ToTemplateContent).ToList(),
            TemplateData = "{}",
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Materializes template content into a concrete PENDING meal entry for a date.</summary>
    public static MealEntry ApplyTemplateContent(
        MealTemplateContent content,
        Guid categoryId,
        DateOnly targetDate,
        Guid profileId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);

        return new MealEntry
        {
            Id = Guid.NewGuid(),
            LocalProfileId = profileId,
            Date = targetDate,
            MealCategoryId = categoryId,
            MealCategoryKey = content.MealCategoryKey,
            Title = content.Title,
            Ingredients = content.Ingredients,
            PlannedTime = content.PlannedTime,
            Status = Enums.MealStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Projects an entry onto template content, dropping all consumption evidence.</summary>
    public static MealTemplateContent ToTemplateContent(MealEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new MealTemplateContent
        {
            MealCategoryKey = entry.MealCategoryKey,
            Title = entry.Title,
            Ingredients = entry.Ingredients,
            PlannedTime = entry.PlannedTime,
        };
    }
}
