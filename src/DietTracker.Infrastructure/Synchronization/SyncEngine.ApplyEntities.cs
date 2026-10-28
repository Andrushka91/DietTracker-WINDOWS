using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Applies photographs, templates, reminders and preferences received from the cloud.</summary>
public sealed partial class SyncEngine
{
    private async Task<bool> ApplyPhotoAsync(ICloudStorageProvider provider, CloudObject item, string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealPhoto>(json);
        var remote = envelope?.Payload;
        if (remote is null)
        {
            return false;
        }

        var local = await _photos.GetAsync(remote.Id, cancellationToken).ConfigureAwait(false);
        if (local is not null)
        {
            local.CloudRevision = item.Revision;
            local.CloudObjectId = _serializer.GetPhotoObjectPath(remote.Id);
            local.SyncStatus = SyncStatus.Synced;
            await _photos.UpdateAsync(local, cancellationToken).ConfigureAwait(false);
            return false;
        }

        // The photograph belongs to a meal slot that must already exist locally.
        var meal = await _entries.GetAsync(remote.MealEntryId, cancellationToken).ConfigureAwait(false);
        if (meal is null)
        {
            _logger.LogInformation("Skipping photograph {PhotoId}: its meal entry is not present locally yet.", remote.Id);
            return false;
        }

        await using var stream = await provider
            .OpenReadAsync(_serializer.GetPhotoObjectPath(remote.Id), cancellationToken)
            .ConfigureAwait(false);

        if (stream is null)
        {
            return false;
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
        return true;
    }

    private async Task<bool> ApplyTemplateAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<MealTemplate>(json);
        if (envelope?.Payload is null)
        {
            return false;
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var remote = envelope.Payload;
        remote.LocalProfileId = profile.Id;
        remote.SyncStatus = SyncStatus.Synced;

        await _templates.UpsertAsync(remote, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyReminderAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<ReminderSettings>(json);
        if (envelope?.Payload is null)
        {
            return false;
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var category = await _categories.GetByKeyAsync(envelope.Payload.MealCategoryKey, cancellationToken).ConfigureAwait(false);
        if (category is null)
        {
            return false;
        }

        var remote = envelope.Payload;
        remote.LocalProfileId = profile.Id;
        remote.MealCategoryId = category.Id;
        remote.SyncStatus = SyncStatus.Synced;

        await _reminders.UpsertAsync(remote, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyPreferencesAsync(string json, CancellationToken cancellationToken)
    {
        var envelope = _serializer.Deserialize<AppPreferences>(json);
        if (envelope?.Payload is null)
        {
            return false;
        }

        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        if (preferences is null)
        {
            return false;
        }

        var remote = envelope.Payload;

        // Synchronization is intentionally device specific and is therefore not taken over. The
        // interface language is not synchronized either: Diet Tracker ships in English only, so a value
        // written by an older build must never be taken over again.
        preferences.TimeZone = remote.TimeZone;
        preferences.ShowSnackCards = remote.ShowSnackCards;
        preferences.RemindersEnabled = remote.RemindersEnabled;
        preferences.Use12HourClock = remote.Use12HourClock;
        preferences.UpdatedAt = _clock.UtcNow;
        preferences.SyncStatus = SyncStatus.Synced;

        await _preferences.UpsertAsync(preferences, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
