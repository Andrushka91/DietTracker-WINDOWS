using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Services;

/// <summary>Photograph attachments and reminder configuration.</summary>
public sealed partial class MealTrackingService
{
    /// <inheritdoc />
    public async Task<MealPhoto> AddPhotoAsync(Guid mealEntryId, string sourceFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFilePath);

        var entry = await RequireEntryAsync(mealEntryId, cancellationToken).ConfigureAwait(false);
        var current = await _photos.CountForMealAsync(mealEntryId, cancellationToken).ConfigureAwait(false);

        var slotCheck = Validation.PhotoValidator.ValidateSlotAvailability(current);
        if (!slotCheck.IsValid)
        {
            throw new ValidationException(slotCheck);
        }

        var import = await _photoStore.ImportAsync(mealEntryId, sourceFilePath, cancellationToken).ConfigureAwait(false);
        if (!import.Succeeded || import.Photo is null)
        {
            throw new ValidationException(Validation.ValidationResult.Failure(
                new Validation.ValidationFailure(import.ReasonCode ?? "photo.import.failed", nameof(sourceFilePath), "The photograph could not be imported.")));
        }

        var photo = import.Photo;

        // Dedupe: if the very same bytes are already attached to this meal, keep a single copy.
        var existing = await _photos.FindByContentHashAsync(photo.ContentHash, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.MealEntryId == mealEntryId)
        {
            await _photoStore.DeleteFilesAsync(photo, cancellationToken).ConfigureAwait(false);
            return existing;
        }

        await _photos.AddAsync(photo, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.MealPhoto, photo.Id, 1, cancellationToken).ConfigureAwait(false);

        // Attaching evidence is optional: the entry itself is only touched to refresh its timestamp.
        entry.Photos = (await _photos.GetForMealAsync(mealEntryId, cancellationToken).ConfigureAwait(false)).ToList();
        return photo;
    }

    /// <inheritdoc />
    public async Task RemovePhotoAsync(Guid photoId, CancellationToken cancellationToken = default)
    {
        var photo = await _photos.GetAsync(photoId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            return;
        }

        await _photos.DeleteAsync(photoId, cancellationToken).ConfigureAwait(false);
        await _photoStore.DeleteFilesAsync(photo, cancellationToken).ConfigureAwait(false);
        await _journal.RecordDeleteAsync(EntityType.MealPhoto, photoId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealPhoto>> GetPhotosAsync(Guid mealEntryId, CancellationToken cancellationToken = default)
        => await _photos.GetForMealAsync(mealEntryId, cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ReminderSettings> SaveReminderAsync(ReminderSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var category = await _categories.GetByKeyAsync(settings.MealCategoryKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Permanent category {settings.MealCategoryKey} is missing.");

        settings.MealCategoryId = category.Id;
        settings.UpdatedAt = _clock.UtcNow;

        var validation = Validation.ReminderValidator.Validate(settings);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation);
        }

        await _reminders.UpsertAsync(settings, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.ReminderSettings, settings.Id, 1, cancellationToken).ConfigureAwait(false);
        return settings;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReminderSettings>> GetRemindersAsync(CancellationToken cancellationToken = default)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        return await _reminders.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Resolves the effective planned time of a slot: the user value, else the category default.</summary>
    public async Task<TimeOnly> GetEffectivePlannedTimeAsync(MealEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.PlannedTime is { } planned)
        {
            return planned;
        }

        var category = await _categories.GetByKeyAsync(entry.MealCategoryKey, cancellationToken).ConfigureAwait(false);
        return category?.DefaultTime ?? new TimeOnly(12, 0);
    }
}
