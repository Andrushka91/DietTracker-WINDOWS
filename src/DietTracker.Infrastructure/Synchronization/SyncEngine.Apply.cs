using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Dispatches remote payloads to the matching local apply operation.</summary>
public sealed partial class SyncEngine
{
    private async Task<bool> ApplyRemoteEntityAsync(
        ICloudStorageProvider provider,
        CloudObject item,
        string json,
        (int SchemaVersion, EntityType EntityType, Guid EntityId, long Revision, DateTimeOffset UpdatedAt, string DeviceId, bool IsDeleted, string ContentHash) header,
        Guid accountId,
        CancellationToken cancellationToken)
    {
        return header.EntityType switch
        {
            EntityType.MealEntry => await ApplyMealEntryAsync(item, json, header, accountId, cancellationToken).ConfigureAwait(false),
            EntityType.MealPhoto => await ApplyPhotoAsync(provider, item, json, cancellationToken).ConfigureAwait(false),
            EntityType.MealTemplate => await ApplyTemplateAsync(json, cancellationToken).ConfigureAwait(false),
            EntityType.ReminderSettings => await ApplyReminderAsync(json, cancellationToken).ConfigureAwait(false),
            EntityType.AppPreferences => await ApplyPreferencesAsync(json, cancellationToken).ConfigureAwait(false),
            _ => false,
        };
    }

    private async Task JournalUpsertAsync(Guid accountId, EntityType entityType, Guid entityId, long revision, CancellationToken cancellationToken)
    {
        await _operations.EnqueueAsync(
            new SyncOperation
            {
                CloudAccountId = accountId,
                EntityType = entityType,
                EntityId = entityId,
                OperationType = SyncOperationType.Upsert,
                EntityRevision = revision,
                CreatedAt = _clock.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);
    }
}
