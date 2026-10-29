using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using DietTracker.Core.Validation;

namespace DietTracker.Core.Services;

/// <summary>Inline editing, completion tracking and category customization.</summary>
public sealed partial class MealTrackingService
{
    /// <inheritdoc />
    public async Task<ValidationResult> SaveEditsAsync(MealEntry edited, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edited);

        var persisted = await _entries.GetAsync(edited.Id, cancellationToken).ConfigureAwait(false)
            ?? await CreatePlaceholderAsync(edited, cancellationToken).ConfigureAwait(false);

        // Only user editable fields are copied: status and completion timestamp are preserved so
        // that editing a meal which is already DONE never silently removes its completion state.
        persisted.Title = edited.Title.Trim();
        persisted.Ingredients = edited.Ingredients.Trim();
        persisted.PlannedTime = edited.PlannedTime;

        var validation = MealEntryValidator.Validate(persisted);
        if (!validation.IsValid)
        {
            return validation;
        }

        await PersistAsync(persisted, cancellationToken).ConfigureAwait(false);

        edited.Status = persisted.Status;
        edited.CompletedAt = persisted.CompletedAt;
        edited.Revision = persisted.Revision;
        edited.UpdatedAt = persisted.UpdatedAt;
        edited.SyncStatus = persisted.SyncStatus;
        return validation;
    }

    /// <inheritdoc />
    public async Task<MealEntry> ClearMealAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        var entry = await RequireEntryAsync(mealEntryId, cancellationToken).ConfigureAwait(false);

        // Everything the card holds goes back to the empty placeholder of the permanent slot: the
        // text, the planned time and the whole completion state. The slot row itself is never
        // deleted, so the dashboard and the calendar keep their five permanent cards.
        entry.Title = string.Empty;
        entry.Ingredients = string.Empty;
        entry.PlannedTime = null;
        entry.Status = MealStatus.Pending;
        entry.CompletedAt = null;
        entry.Photos = [];

        var validation = MealEntryValidator.Validate(entry, requireContent: false);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation);
        }

        await PersistAsync(entry, cancellationToken).ConfigureAwait(false);

        // The photographs are wiped as well: the record first, then the local files, each step
        // journaled so the deletion reaches the connected account at the next synchronization.
        var photos = await _photos.GetForMealAsync(mealEntryId, cancellationToken).ConfigureAwait(false);
        foreach (var photo in photos)
        {
            await _photos.DeleteAsync(photo.Id, cancellationToken).ConfigureAwait(false);
            await _photoStore.DeleteFilesAsync(photo, cancellationToken).ConfigureAwait(false);
            await _journal.RecordDeleteAsync(EntityType.MealPhoto, photo.Id, cancellationToken).ConfigureAwait(false);
        }

        return entry;
    }

    /// <inheritdoc />
    public async Task<MealEntry> MarkDoneAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        var entry = await RequireEntryAsync(mealEntryId, cancellationToken).ConfigureAwait(false);

        // Local wall-clock time: the date guard compares the meal's calendar date against today.
        var outcome = CompletionRules.MarkDone(entry, _clock.LocalNow);

        if (!outcome.Changed)
        {
            // Repeated DONE actions (and future-dated meals) must never change anything.
            return entry;
        }

        await PersistAsync(entry, cancellationToken).ConfigureAwait(false);

        // Pending reminders for this meal on this date are no longer useful.
        if (_notifications is not null)
        {
            await _notifications.CancelMealReminderAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        }

        return entry;
    }

    /// <inheritdoc />
    public async Task<MealEntry> MarkMissedAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
    {
        var entry = await RequireEntryAsync(mealEntryId, cancellationToken).ConfigureAwait(false);

        // Local wall-clock time: the date guard compares the meal's calendar date against today.
        var outcome = CompletionRules.MarkMissed(entry, _clock.LocalNow);

        if (!outcome.Changed)
        {
            // Already missed, or a future meal that must stay pending: nothing to persist.
            return entry;
        }

        await PersistAsync(entry, cancellationToken).ConfigureAwait(false);

        // A decided meal (eaten or missed) no longer needs its reminder.
        if (_notifications is not null)
        {
            await _notifications.CancelMealReminderAsync(entry.Id, cancellationToken).ConfigureAwait(false);
        }

        return entry;
    }

    /// <inheritdoc />
    public async Task UpdateCategoryAsync(MealCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        if (!MealSlotRules.IsPermanent(category.FixedCategoryKey))
        {
            throw new InvalidOperationException("Only the five permanent meal categories can be updated.");
        }

        await _categories.UpdateAsync(category, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.MealCategory, category.Id, 1, cancellationToken).ConfigureAwait(false);
    }

    private async Task<MealEntry> RequireEntryAsync(Guid mealEntryId, CancellationToken cancellationToken)
        => await _entries.GetAsync(mealEntryId, cancellationToken).ConfigureAwait(false)
           ?? throw new InvalidOperationException($"Meal entry {mealEntryId} does not exist locally.");

    private async Task<MealEntry> CreatePlaceholderAsync(MealEntry edited, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var category = await _categories.GetByKeyAsync(edited.MealCategoryKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Permanent category {edited.MealCategoryKey} is missing.");

        var placeholder = new MealEntry
        {
            Id = edited.Id,
            LocalProfileId = profile.Id,
            Date = edited.Date,
            MealCategoryId = category.Id,
            MealCategoryKey = category.FixedCategoryKey,
            CreatedAt = _clock.UtcNow,
            UpdatedAt = _clock.UtcNow,
        };

        await _entries.UpsertAsync(placeholder, cancellationToken).ConfigureAwait(false);
        return placeholder;
    }

    /// <summary>Writes an entry to SQLite and journals the change for cloud synchronization.</summary>
    private async Task PersistAsync(MealEntry entry, CancellationToken cancellationToken)
    {
        entry.UpdatedAt = _clock.UtcNow;
        entry.Revision++;
        entry.SyncStatus = entry.CloudRevision is null ? SyncStatus.PendingUpload : SyncStatus.PendingUpdate;

        await _entries.UpsertAsync(entry, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.MealEntry, entry.Id, entry.Revision, cancellationToken).ConfigureAwait(false);
    }
}
