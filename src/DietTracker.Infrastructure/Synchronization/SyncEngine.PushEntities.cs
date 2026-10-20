using System.Text;
using System.Text.Json;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Serialization;
using DietTracker.Core.Utilities;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Upload support for templates, reminders and preferences.</summary>
public sealed partial class SyncEngine
{
    private async Task<bool> PushTemplateAsync(ICloudStorageProvider provider, SyncOperation operation, CancellationToken cancellationToken)
    {
        var template = await _templates.GetAsync(operation.EntityId, cancellationToken).ConfigureAwait(false);
        return template is not null && await UploadEnvelopeAsync(
            provider, EntityType.MealTemplate, template.Id, template.LocalProfileId, 1, template.UpdatedAt, template, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PushReminderAsync(ICloudStorageProvider provider, SyncOperation operation, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var settings = (await _reminders.GetAllAsync(profile.Id, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(r => r.Id == operation.EntityId);

        return settings is not null && await UploadEnvelopeAsync(
            provider, EntityType.ReminderSettings, settings.Id, settings.LocalProfileId, 1, settings.UpdatedAt, settings, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> PushPreferencesAsync(ICloudStorageProvider provider, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetOrCreateActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        var preferences = await _preferences.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false);
        return preferences is not null && await UploadEnvelopeAsync(
            provider, EntityType.AppPreferences, profile.Id, profile.Id, 1, preferences.UpdatedAt, preferences, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> UploadEnvelopeAsync<T>(
        ICloudStorageProvider provider,
        EntityType entityType,
        Guid entityId,
        Guid profileId,
        long revision,
        DateTimeOffset updatedAt,
        T payload,
        CancellationToken cancellationToken)
    {
        var hash = Hashing.Sha256(JsonSerializer.Serialize(payload, DietTrackerJson.Compact));
        var envelope = new CloudEnvelope<T>
        {
            EntityType = entityType,
            EntityId = entityId,
            LocalProfileId = profileId,
            Revision = revision,
            UpdatedAt = updatedAt,
            DeviceId = _environment.DeviceId,
            ContentHash = hash,
            Payload = payload,
        };

        var path = _serializer.GetObjectPath(entityType, entityId);
        var json = _serializer.Serialize(envelope);
        await provider.UploadAsync(
            new CloudObject
            {
                ObjectId = path,
                Path = path,
                ContentHash = hash,
                ModifiedAt = updatedAt,
                SizeBytes = Encoding.UTF8.GetByteCount(json),
                Content = Encoding.UTF8.GetBytes(json),
            },
            cancellationToken).ConfigureAwait(false);

        return true;
    }
}
