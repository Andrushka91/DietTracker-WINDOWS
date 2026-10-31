using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Applies photographs, templates, reminders, preferences and categories received from the cloud.</summary>
public sealed partial class SyncEngine
{
    private async Task<ApplyOutcome> ApplyPhotoAsync(ICloudStorageProvider provider, CloudObject item, string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealPhoto>(json);
        var remote = envelope?.Payload;
        if (remote is null)
        {
            return ApplyOutcome.Unchanged;
        }

        var local = await _photos.GetAsync(remote.Id, cancellationToken).ConfigureAwait(false);
        if (local is not null)
        {
            local.CloudRevision = item.Revision;
            local.CloudObjectId = _serializer.GetPhotoObjectPath(remote.Id);
            local.SyncStatus = SyncStatus.Synced;
            await _photos.UpdateAsync(local, cancellationToken).ConfigureAwait(false);
            return ApplyOutcome.Unchanged;
        }

        // The photograph belongs to a meal slot that must already exist locally. Returning Retry keeps
        // the cursor before this document, so a picture whose meal arrives in the next page (or the next
        // cycle) is applied later instead of being skipped for good.
        var meal = await _entries.GetAsync(remote.MealEntryId, cancellationToken).ConfigureAwait(false);
        if (meal is null)
        {
            _logger.LogInformation("Photograph {PhotoId} is not applied yet: its meal entry is not present locally.", remote.Id);
            return ApplyOutcome.Retry;
        }

        await using var stream = await provider
            .OpenReadAsync(_serializer.GetPhotoObjectPath(remote.Id), cancellationToken)
            .ConfigureAwait(false);

        if (stream is null)
        {
            _logger.LogInformation("Photograph {PhotoId} is not applied yet: its bytes could not be downloaded.", remote.Id);
            return ApplyOutcome.Retry;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var stored = await _photoStore
            .StoreDownloadedAsync(remote.MealEntryId, remote.OriginalFileName, remote.MimeType, bytes, cancellationToken)
            .ConfigureAwait(false);

        // Keep the cloud identity so the same photograph is never downloaded or uploaded twice.
        stored.Id = remote.Id;
        stored.ContentHash = remote.ContentHash;
        stored.CloudObjectId = _serializer.GetPhotoObjectPath(remote.Id);
        stored.CloudRevision = item.Revision;
        stored.SyncStatus = SyncStatus.Synced;
        stored.CreatedAt = remote.CreatedAt;
        stored.UpdatedAt = _clock.UtcNow;

        await _photos.AddAsync(stored, cancellationToken).ConfigureAwait(false);
        return ApplyOutcome.Applied;
    }

    private async Task<ApplyOutcome> ApplyTemplateAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealTemplate>(json);
        if (envelope?.Payload is null)
        {
            return ApplyOutcome.Unchanged;
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var remote = envelope.Payload;
        remote.LocalProfileId = profile.Id;
        remote.SyncStatus = SyncStatus.Synced;

        await _templates.UpsertAsync(remote, cancellationToken).ConfigureAwait(false);
        return ApplyOutcome.Applied;
    }

    private async Task<ApplyOutcome> ApplyReminderAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<ReminderSettings>(json);
        if (envelope?.Payload is null)
        {
            return ApplyOutcome.Unchanged;
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var category = await _categories.GetByKeyAsync(envelope.Payload.MealCategoryKey, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return ApplyOutcome.Retry;
        }

        var remote = envelope.Payload;
        remote.LocalProfileId = profile.Id;
        remote.MealCategoryId = category.Id;
        remote.SyncStatus = SyncStatus.Synced;

        await _reminders.UpsertAsync(remote, cancellationToken).ConfigureAwait(false);
        return ApplyOutcome.Applied;
    }

    private async Task<ApplyOutcome> ApplyPreferencesAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<AppPreferences>(json);
        if (envelope?.Payload is null)
        {
            return ApplyOutcome.Unchanged;
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (preferences is null)
        {
            return ApplyOutcome.Unchanged;
        }

        var remote = envelope.Payload;

        // Synchronization is intentionally device specific and is therefore not taken over. The
        // interface language is deliberately absent from the payload: Diet Tracker ships in English
        // only, and a language field written by an older build is ignored on purpose. The same list of
        // shared fields is applied by the mobile app (identityMapping.mergeSharedPreferences).
        preferences.TimeZone = remote.TimeZone;
        preferences.ShowSnackCards = remote.ShowSnackCards;
        preferences.RemindersEnabled = remote.RemindersEnabled;
        preferences.Use12HourClock = remote.Use12HourClock;
        preferences.UpdatedAt = _clock.UtcNow;
        preferences.SyncStatus = SyncStatus.Synced;

        await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
        return ApplyOutcome.Applied;
    }

    /// <summary>
    /// Applies the configuration of one permanent meal category (colour, emoji icon, default time).
    /// The row is matched by its FixedCategoryKey: the local id never changes, so the meals and
    /// reminders that reference it keep a valid foreign key.
    /// </summary>
    private async Task<ApplyOutcome> ApplyCategoryAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealCategory>(json);
        var remote = envelope?.Payload;
        if (remote is null)
        {
            return ApplyOutcome.Unchanged;
        }

        var local = await _categories.GetByKeyAsync(remote.FixedCategoryKey, cancellationToken).ConfigureAwait(false);
        if (local is null)
        {
            return ApplyOutcome.Unchanged;
        }

        if (string.Equals(local.Name, remote.Name, StringComparison.Ordinal)
            && string.Equals(local.Color, remote.Color, StringComparison.Ordinal)
            && string.Equals(local.Icon, remote.Icon, StringComparison.Ordinal)
            && local.DefaultTime == remote.DefaultTime
            && local.DisplayOrder == remote.DisplayOrder)
        {
            return ApplyOutcome.Unchanged;
        }

        local.Name = remote.Name;
        local.Color = remote.Color;
        local.Icon = remote.Icon;
        local.DefaultTime = remote.DefaultTime;
        local.DisplayOrder = remote.DisplayOrder;

        await _categories.UpdateAsync(local, cancellationToken).ConfigureAwait(false);
        return ApplyOutcome.Applied;
    }

}
