using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Core.Services;

/// <summary>
/// Orchestrates every meal tracking use case: the five permanent cards, inline editing,
/// DONE/PENDING/MISSED tracking, copying, templates, photographs, reminders and statistics.
/// </summary>
/// <remarks>
/// Design notes:
/// <list type="bullet">
/// <item>Every write goes to SQLite first and is journaled for synchronization afterwards; the
/// caller never waits for a cloud round trip (offline-first).</item>
/// <item>Placeholder slots of a date are materialized with <c>PlannedTime = null</c>, so an
/// untouched card is not treated as user content and produces no cloud traffic.</item>
/// <item>Editing a meal that is already DONE preserves its status and completion timestamp.</item>
/// </list>
/// </remarks>
public sealed partial class MealTrackingService : IMealTrackingService
{
    private readonly IMealCategoryRepository _categories;
    private readonly IMealEntryRepository _entries;
    private readonly IPhotoRepository _photos;
    private readonly IMealTemplateRepository _templates;
    private readonly IReminderRepository _reminders;
    private readonly IPhotoStore _photoStore;
    private readonly IProfileService _profiles;
    private readonly ISyncJournal _journal;
    private readonly IClock _clock;
    private readonly INotificationService? _notifications;

    public MealTrackingService(
        IMealCategoryRepository categories,
        IMealEntryRepository entries,
        IPhotoRepository photos,
        IMealTemplateRepository templates,
        IReminderRepository reminders,
        IPhotoStore photoStore,
        IProfileService profiles,
        ISyncJournal journal,
        IClock clock,
        INotificationService? notifications = null)
    {
        _categories = categories;
        _entries = entries;
        _photos = photos;
        _templates = templates;
        _reminders = reminders;
        _photoStore = photoStore;
        _profiles = profiles;
        _journal = journal;
        _clock = clock;
        _notifications = notifications;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealCategory>> GetCategoriesAsync(CancellationToken cancellationToken = default)
    {
        var stored = await _categories.GetAllAsync(cancellationToken).ConfigureAwait(false);

        if (MealSlotRules.FindMissingSlots(stored).Count > 0)
        {
            await _categories.EnsureDefaultsAsync(MealSlotRules.CreateDefaults(), cancellationToken).ConfigureAwait(false);
            stored = await _categories.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }

        return MealSlotRules.Normalize(stored);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealEntry>> GetDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var categories = await GetCategoriesAsync(cancellationToken).ConfigureAwait(false);
        var existing = await _entries.GetForDateAsync(profile.Id, date, cancellationToken).ConfigureAwait(false);

        var missing = categories
            .Where(c => existing.All(e => e.MealCategoryKey != c.FixedCategoryKey))
            .Select(c => new MealEntry
            {
                LocalProfileId = profile.Id,
                Date = date,
                MealCategoryId = c.Id,
                MealCategoryKey = c.FixedCategoryKey,
                PlannedTime = null,
                Status = MealStatus.Pending,
                CreatedAt = _clock.UtcNow,
                UpdatedAt = _clock.UtcNow,
            })
            .ToArray();

        if (missing.Length > 0)
        {
            await _entries.UpsertRangeAsync(missing, cancellationToken).ConfigureAwait(false);
        }

        var all = existing.Concat(missing).ToArray();
        foreach (var entry in all)
        {
            entry.Photos = (await _photos.GetForMealAsync(entry.Id, cancellationToken).ConfigureAwait(false)).ToList();
        }

        return all.OrderBy(e => e.MealCategoryKey).ToArray();
    }

    /// <inheritdoc />
    public async Task<MealEntry> GetOrCreateSlotAsync(DateOnly date, MealCategoryKey key, CancellationToken cancellationToken = default)
    {
        var day = await GetDayAsync(date, cancellationToken).ConfigureAwait(false);
        return day.First(e => e.MealCategoryKey == key);
    }
}
