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
        var attached = await _photos.GetForMealAsync(mealEntryId, cancellationToken).ConfigureAwait(false);

        var slotCheck = Validation.PhotoValidator.ValidateSlotAvailability(attached.Count);
        if (!slotCheck.IsValid)
        {
            throw new ValidationException(slotCheck);
        }

        // Duplicate guard, evaluated before a single byte is copied: the source is hashed and the hash
        // is compared with the photographs of this meal, so uploading the same picture twice is refused
        // with an explanation instead of quietly being merged into the first one afterwards.
        var sourceHash = await _photoStore.ComputeContentHashAsync(sourceFilePath, cancellationToken).ConfigureAwait(false);
        var duplicateCheck = Validation.PhotoValidator.ValidateNoDuplicate(HasSameContent(attached, sourceHash, null));
        if (!duplicateCheck.IsValid)
        {
            throw new ValidationException(duplicateCheck);
        }

        var import = await _photoStore.ImportAsync(mealEntryId, sourceFilePath, cancellationToken).ConfigureAwait(false);
        if (!import.Succeeded || import.Photo is null)
        {
            throw new ValidationException(Validation.ValidationResult.Failure(
                new Validation.ValidationFailure(import.ReasonCode ?? "photo.import.failed", nameof(sourceFilePath), "The photograph could not be imported.")));
        }

        var photo = import.Photo;

        // Same rule again after the copy, to close the window between the check and the write: identical
        // bytes must still end up as a single photograph, so the fresh copy is discarded and the upload
        // is reported as the duplicate it is.
        var saved = await _photos.GetForMealAsync(mealEntryId, cancellationToken).ConfigureAwait(false);
        if (HasSameContent(saved, photo.ContentHash, photo.Id))
        {
            await _photoStore.DeleteFilesAsync(photo, cancellationToken).ConfigureAwait(false);
            throw new ValidationException(Validation.PhotoValidator.ValidateNoDuplicate(true));
        }

        await _photos.AddAsync(photo, cancellationToken).ConfigureAwait(false);
        await _journal.RecordUpsertAsync(EntityType.MealPhoto, photo.Id, 1, cancellationToken).ConfigureAwait(false);

        // Attaching evidence is optional: the entry itself is only touched to refresh its timestamp.
        // The new photograph is the most recent one, so appending it is the order the query would give.
        entry.Photos = saved.Append(photo).ToList();
        return photo;
    }

    /// <summary>
    /// True when one of a meal's photographs already carries exactly these bytes. The comparison is
    /// scoped to the meal on purpose: the same picture may legitimately illustrate two different meals,
    /// and a table-wide hash lookup cannot tell the two cases apart.
    /// </summary>
    /// <param name="attached">The photographs currently attached to one meal.</param>
    /// <param name="contentHash">SHA-256 of the bytes being attached; empty never matches.</param>
    /// <param name="excludeId">Identifier of the photograph being added, when it is already persisted.</param>
    private static bool HasSameContent(IEnumerable<MealPhoto> attached, string contentHash, Guid? excludeId)
        => !string.IsNullOrEmpty(contentHash)
           && attached.Any(photo => photo.Id != excludeId && photo.ContentHash == contentHash);

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
