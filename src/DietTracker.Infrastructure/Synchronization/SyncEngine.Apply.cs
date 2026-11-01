using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>What happened to one received cloud document.</summary>
internal enum ApplyOutcome
{
    /// <summary>The document was materialized in the local cache.</summary>
    Applied,

    /// <summary>Nothing to do: identical content, our own echo, an unsupported schema or a picture that is already here.</summary>
    Unchanged,

    /// <summary>Both versions were preserved; the user decides which one to keep.</summary>
    Conflicted,

    /// <summary>
    /// The document cannot be applied yet (missing bytes, parent record not local). The cursor is kept
    /// behind it, so the next cycle retries it - it is never counted as synchronized.
    /// </summary>
    Retry,
}

/// <summary>Dispatches remote payloads to the matching local apply operation.</summary>
public sealed partial class SyncEngine
{
    private async Task<ApplyOutcome> ApplyRemoteEntityAsync(
        ICloudStorageProvider provider,
        CloudObject item,
        string json,
        (int SchemaVersion, EntityType EntityType, Guid EntityId, long Revision, DateTimeOffset UpdatedAt, string DeviceId, bool IsDeleted, string ContentHash) header,
        Guid accountId,
        PhotoBytePrefetcher? binaries,
        CancellationToken cancellationToken)
    {
        return header.EntityType switch
        {
            EntityType.MealEntry => await ApplyMealEntryAsync(item, json, header, accountId, cancellationToken).ConfigureAwait(false),
            EntityType.MealPhoto => await ApplyPhotoAsync(provider, item, json, binaries, cancellationToken).ConfigureAwait(false),

            EntityType.MealTemplate => await ApplyTemplateAsync(json, cancellationToken).ConfigureAwait(false),
            EntityType.ReminderSettings => await ApplyReminderAsync(json, cancellationToken).ConfigureAwait(false),
            EntityType.AppPreferences => await ApplyPreferencesAsync(json, cancellationToken).ConfigureAwait(false),
            EntityType.MealCategory => await ApplyCategoryAsync(json, cancellationToken).ConfigureAwait(false),

            // Profiles are per installation (the provider identity is the key) and are never replaced
            // by another device's document.
            _ => ApplyOutcome.Unchanged,
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
