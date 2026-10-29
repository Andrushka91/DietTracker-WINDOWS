using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>Read/write access to the five permanent meal categories.</summary>
public interface IMealCategoryRepository
{
    Task<IReadOnlyList<MealCategory>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<MealCategory?> GetByKeyAsync(Enums.MealCategoryKey key, CancellationToken cancellationToken = default);

    /// <summary>Inserts missing permanent categories. Never deletes anything.</summary>
    Task EnsureDefaultsAsync(IEnumerable<MealCategory> defaults, CancellationToken cancellationToken = default);

    /// <summary>Updates colour, icon, default time and display order. The category itself is never removed.</summary>
    Task UpdateAsync(MealCategory category, CancellationToken cancellationToken = default);
}

/// <summary>Read/write access to daily meal entries.</summary>
public interface IMealEntryRepository
{
    Task<MealEntry?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<MealEntry?> GetBySlotAsync(Guid profileId, DateOnly date, Enums.MealCategoryKey key, CancellationToken cancellationToken = default);

    /// <summary>Loads all five slots of a single date (creates nothing).</summary>
    Task<IReadOnlyList<MealEntry>> GetForDateAsync(Guid profileId, DateOnly date, CancellationToken cancellationToken = default);

    /// <summary>Bulk load for a date range (used by statistics and the calendar; keeps memory bounded).</summary>
    Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<MealEntry>>> GetForRangeAsync(Guid profileId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task UpsertAsync(MealEntry entry, CancellationToken cancellationToken = default);

    Task UpsertRangeAsync(IEnumerable<MealEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>Hard delete, used only by explicit user driven data deletion and by restore.</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid profileId, CancellationToken cancellationToken = default);
}

/// <summary>Read/write access to photograph metadata (never to the image bytes).</summary>
public interface IPhotoRepository
{
    /// <summary>Every photograph of every meal (used by maintenance passes such as thumbnail repair).</summary>
    Task<IReadOnlyList<MealPhoto>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MealPhoto>> GetForMealAsync(Guid mealEntryId, CancellationToken cancellationToken = default);

    /// <summary>Every photograph of every meal inside a date range (inclusive), ordered by date,
    /// slot and creation time - one query instead of one per meal, used to fill a whole calendar
    /// month with the first picture of each slot.</summary>
    Task<IReadOnlyList<MealPhoto>> GetForDateRangeAsync(Guid profileId, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);

    Task<MealPhoto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(MealPhoto photo, CancellationToken cancellationToken = default);

    Task UpdateAsync(MealPhoto photo, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountForMealAsync(Guid mealEntryId, CancellationToken cancellationToken = default);

    /// <summary>Finds an already stored photo with the same content hash (dedupe support).</summary>
    Task<MealPhoto?> FindByContentHashAsync(string contentHash, CancellationToken cancellationToken = default);
}

/// <summary>Read/write access to reusable meal and day templates.</summary>
public interface IMealTemplateRepository
{
    Task<IReadOnlyList<MealTemplate>> GetAllAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<MealTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task UpsertAsync(MealTemplate template, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <summary>Read/write access to reminder configuration.</summary>
public interface IReminderRepository
{
    Task<IReadOnlyList<ReminderSettings>> GetAllAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task<ReminderSettings?> GetForCategoryAsync(Guid profileId, Enums.MealCategoryKey key, CancellationToken cancellationToken = default);

    Task UpsertAsync(ReminderSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Records that a reminder fired, so the same notification is never raised twice for a slot.</summary>
    Task<bool> TryRecordFiredAsync(Guid reminderId, DateOnly date, CancellationToken cancellationToken = default);
}

/// <summary>Read/write access to local profiles.</summary>
public interface IProfileRepository
{
    Task<UserProfile?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<UserProfile?> GetActiveAsync(CancellationToken cancellationToken = default);

    Task UpsertAsync(UserProfile profile, CancellationToken cancellationToken = default);

    Task SetActiveAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UserProfile>> GetAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Read/write access to application preferences.</summary>
public interface IAppPreferencesRepository
{
    Task<AppPreferences?> GetAsync(Guid profileId, CancellationToken cancellationToken = default);

    Task UpsertAsync(AppPreferences preferences, CancellationToken cancellationToken = default);
}
