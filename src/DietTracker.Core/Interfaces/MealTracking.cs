using DietTracker.Core.Models;
using DietTracker.Core.Validation;

namespace DietTracker.Core.Interfaces;

/// <summary>
/// Single entry point to the meal tracking use cases. ViewModels depend on this interface only, so
/// the UI stays free of persistence and synchronization details.
/// </summary>
public interface IMealTrackingService
{
    Task<IReadOnlyList<MealCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the five permanent slots of a date, materializing empty PENDING slots when needed.</summary>
    Task<IReadOnlyList<MealEntry>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Returns a single slot of a date, creating the placeholder when it does not exist yet.</summary>
    Task<MealEntry> GetOrCreateSlotAsync(DateOnly date, Enums.MealCategoryKey key, CancellationToken cancellationToken = default);

    Task<DayProgress> GetDayProgressAsync(DateOnly date, CancellationToken cancellationToken = default);

    Task<PeriodProgress> GetWeekProgressAsync(DateOnly anyDay, CancellationToken cancellationToken = default);

    Task<PeriodProgress> GetMonthProgressAsync(DateOnly anyDay, CancellationToken cancellationToken = default);

    /// <summary>Persists an inline edit, validates it, bumps the revision and journals the change for sync.</summary>
    Task<ValidationResult> SaveEditsAsync(MealEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Confirms the meal as eaten (today or a past date; future meals stay pending).</summary>
    Task<MealEntry> MarkDoneAsync(Guid mealEntryId, CancellationToken cancellationToken = default);

    /// <summary>Explicitly marks the meal as missed (today or a past date; future meals stay
    /// pending). Idempotent: an already missed meal is returned unchanged.</summary>
    Task<MealEntry> MarkMissedAsync(Guid mealEntryId, CancellationToken cancellationToken = default);

    /// <summary>Copies a meal to another date. The copy starts PENDING and carries no photographs.</summary>
    Task<MealEntry> CopyMealToDateAsync(Guid sourceMealEntryId, DateOnly targetDate, CancellationToken cancellationToken = default);

    /// <summary>Duplicates a whole day (for example "copy yesterday"). Returns the number of copied slots.</summary>
    Task<int> DuplicateDayAsync(DateOnly sourceDate, DateOnly targetDate, CancellationToken cancellationToken = default);

    Task<MealTemplate> SaveTemplateAsync(MealEntry entry, string name, CancellationToken cancellationToken = default);

    Task<MealTemplate> SaveDayTemplateAsync(DateOnly date, string name, CancellationToken cancellationToken = default);

    Task ApplyTemplateAsync(Guid templateId, DateOnly targetDate, CancellationToken cancellationToken = default);

    Task UpdateCategoryAsync(MealCategory category, CancellationToken cancellationToken = default);

    Task<MealPhoto> AddPhotoAsync(Guid mealEntryId, string sourceFilePath, CancellationToken cancellationToken = default);

    Task RemovePhotoAsync(Guid photoId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealPhoto>> GetPhotosAsync(Guid mealEntryId, CancellationToken cancellationToken = default);

    Task<ReminderSettings> SaveReminderAsync(ReminderSettings settings, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ReminderSettings>> GetRemindersAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrendPoint>> GetTrendAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealTemplate>> GetTemplatesAsync(CancellationToken cancellationToken = default);
}
