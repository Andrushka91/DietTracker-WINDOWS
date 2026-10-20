using System.IO.Compression;
using DietTracker.Core.BusinessRules;
using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using DietTracker.Core.Utilities;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Per-entity restore logic of <see cref="BackupService"/>.</summary>
public sealed partial class BackupService
{
    private async Task<int> RestorePhotosAsync(ZipArchive archive, BackupManifest manifest, CancellationToken cancellationToken)
    {
        var restored = 0;

        foreach (var photo in manifest.Photos)
        {
            var existing = await _photos.GetAsync(photo.Id, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                continue;
            }

            var entry = archive.GetEntry($"{PhotoFolder}{photo.Id:D}");
            if (entry is null)
            {
                continue;
            }

            using var buffer = new MemoryStream();
            await using (var entryStream = entry.Open())
            {
                await entryStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            }

            var bytes = buffer.ToArray();
            var header = bytes.Length >= 16 ? bytes.AsSpan(0, 16) : bytes.AsSpan();

            // Integrity check: content that does not match its declared image format is rejected.
            if (!PhotoRules.HasValidSignature(photo.MimeType, header))
            {
                _logger.LogWarning("Photograph {PhotoId} was skipped: its content does not match {Mime}.", photo.Id, photo.MimeType);
                continue;
            }

            var stored = await _photoStore
                .StoreDownloadedAsync(photo.MealEntryId, photo.OriginalFileName, photo.MimeType, bytes, cancellationToken)
                .ConfigureAwait(false);

            stored.Id = photo.Id;
            stored.ContentHash = string.IsNullOrEmpty(photo.ContentHash) ? Hashing.Sha256(bytes) : photo.ContentHash;
            stored.CreatedAt = photo.CreatedAt;
            await _photos.AddAsync(stored, cancellationToken).ConfigureAwait(false);
            restored++;
        }

        return restored;
    }

    private async Task<int> RestoreMealsAsync(BackupManifest manifest, UserProfile profile, CancellationToken cancellationToken)
    {
        var restored = 0;

        foreach (var meal in manifest.Meals)
        {
            var category = await _categories.GetByKeyAsync(meal.MealCategoryKey, cancellationToken).ConfigureAwait(false);
            if (category is null)
            {
                continue;
            }

            var local = await _entries.GetAsync(meal.Id, cancellationToken).ConfigureAwait(false)
                        ?? await _entries.GetBySlotAsync(profile.Id, meal.Date, meal.MealCategoryKey, cancellationToken).ConfigureAwait(false);

            if (local is not null && local.Revision > meal.Revision)
            {
                // The local record is newer: the backup must not roll it back.
                continue;
            }

            var target = local ?? new MealEntry { Id = meal.Id, CreatedAt = meal.CreatedAt };
            target.LocalProfileId = profile.Id;
            target.Date = meal.Date;
            target.MealCategoryId = category.Id;
            target.MealCategoryKey = meal.MealCategoryKey;
            target.Title = meal.Title;
            target.Ingredients = meal.Ingredients;
            target.Quantities = meal.Quantities;
            target.Notes = meal.Notes;
            target.PlannedTime = meal.PlannedTime;
            target.Status = meal.Status;
            target.CompletedAt = meal.CompletedAt;
            target.Revision = Math.Max(target.Revision, meal.Revision);
            target.UpdatedAt = _clock.UtcNow;
            target.SyncStatus = SyncStatus.PendingUpdate;

            await _entries.UpsertAsync(target, cancellationToken).ConfigureAwait(false);
            restored++;
        }

        return restored;
    }

    private async Task<int> RestoreTemplatesAsync(BackupManifest manifest, UserProfile profile, CancellationToken cancellationToken)
    {
        var restored = 0;

        foreach (var template in manifest.Templates)
        {
            template.LocalProfileId = profile.Id;
            template.SyncStatus = SyncStatus.PendingUpdate;
            await _templates.UpsertAsync(template, cancellationToken).ConfigureAwait(false);
            restored++;
        }

        return restored;
    }

    private async Task<int> RestoreRemindersAsync(BackupManifest manifest, UserProfile profile, CancellationToken cancellationToken)
    {
        var restored = 0;

        foreach (var reminder in manifest.Reminders)
        {
            var category = await _categories.GetByKeyAsync(reminder.MealCategoryKey, cancellationToken).ConfigureAwait(false);
            if (category is null)
            {
                continue;
            }

            reminder.LocalProfileId = profile.Id;
            reminder.MealCategoryId = category.Id;
            reminder.SyncStatus = SyncStatus.PendingUpdate;
            await _reminders.UpsertAsync(reminder, cancellationToken).ConfigureAwait(false);
            restored++;
        }

        return restored;
    }
}
