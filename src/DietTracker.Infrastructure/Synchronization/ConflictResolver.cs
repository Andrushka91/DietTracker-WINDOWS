using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Conflict policy for records edited on two Windows computers.
/// </summary>
/// <remarks>
/// The decision prefers provider revisions and content hashes over local wall-clock timestamps:
/// <list type="number">
/// <item>identical payload hashes mean "no change";</item>
/// <item>a payload produced by this device is an echo of our own upload;</item>
/// <item>if the cloud revision still equals the revision the local record was based on, only the
/// local side changed, so the local version wins;</item>
/// <item>if the remote revision is higher than the local revision, the other device is ahead and
/// the remote version is accepted;</item>
/// <item>anything else is a genuine concurrent edit: both versions are preserved and the user is
/// asked to choose. Nothing is ever silently discarded.</item>
/// </list>
/// </remarks>
public sealed partial class ConflictResolver : IConflictResolver
{
    private readonly IConflictRepository _conflicts;
    private readonly IMealEntryRepository _entries;
    private readonly IMealTemplateRepository _templates;
    private readonly ICloudPayloadSerializer _serializer;
    private readonly ISyncOperationRepository _operations;
    private readonly ICloudAccountRepository _accounts;
    private readonly IClock _clock;
    private readonly ILogger<ConflictResolver> _logger;

    public ConflictResolver(
        IConflictRepository conflicts,
        IMealEntryRepository entries,
        IMealTemplateRepository templates,
        ICloudPayloadSerializer serializer,
        ISyncOperationRepository operations,
        ICloudAccountRepository accounts,
        IClock clock,
        ILogger<ConflictResolver> logger)
    {
        _conflicts = conflicts;
        _entries = entries;
        _templates = templates;
        _serializer = serializer;
        _operations = operations;
        _accounts = accounts;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Journals an upsert for the active account. The journal service depends on the engine, so this
    /// class writes to the outbox directly - which also keeps the dependency graph acyclic.
    /// </summary>
    private async Task JournalUpsertAsync(EntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var account = await _accounts.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        if (account is null || !account.SynchronizationEnabled)
        {
            return;
        }

        await _operations.EnqueueAsync(
            new SyncOperation
            {
                CloudAccountId = account.Id,
                EntityType = entityType,
                EntityId = entityId,
                OperationType = SyncOperationType.Upsert,
                CreatedAt = _clock.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ConflictDecision Decide(ConflictContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrEmpty(context.LocalContentHash)
            && !string.IsNullOrEmpty(context.RemoteContentHash)
            && string.Equals(context.LocalContentHash, context.RemoteContentHash, StringComparison.Ordinal))
        {
            return ConflictDecision.NoChange;
        }

        // Our own upload coming back from the cloud: accept it so the record stops being dirty.
        if (!string.IsNullOrEmpty(context.RemoteDeviceId)
            && string.Equals(context.RemoteDeviceId, context.LocalDeviceId, StringComparison.Ordinal))
        {
            return ConflictDecision.AcceptRemote;
        }

        // The cloud object was not modified since this device last synchronized it.
        if (!string.IsNullOrEmpty(context.RemoteRevisionToken)
            && !string.IsNullOrEmpty(context.LocalBaseRevision)
            && string.Equals(context.RemoteRevisionToken, context.LocalBaseRevision, StringComparison.Ordinal))
        {
            return ConflictDecision.KeepLocal;
        }

        // Another device produced a strictly newer revision.
        if (context.RemoteRevision > context.LocalRevision)
        {
            return ConflictDecision.AcceptRemote;
        }

        // Local revision is newer than the remote one: re-upload.
        if (context.LocalRevision > context.RemoteRevision)
        {
            return ConflictDecision.KeepLocal;
        }

        // Same revision number but different content and no usable revision token: genuine conflict.
        return ConflictDecision.RequiresUserDecision;
    }

    /// <summary>Stores both versions of a conflicting record so nothing is lost while the user decides.</summary>
    public async Task<ConflictRecord> RecordAsync(
        EntityType entityType,
        Guid entityId,
        string localPayload,
        string remotePayload,
        DateTimeOffset localModifiedAt,
        DateTimeOffset remoteModifiedAt,
        string? remoteRevision,
        string? remoteDeviceId,
        CancellationToken cancellationToken = default)
    {
        var conflict = new ConflictRecord
        {
            EntityType = entityType,
            EntityId = entityId,
            LocalPayload = localPayload,
            RemotePayload = remotePayload,
            LocalModifiedAt = localModifiedAt,
            RemoteModifiedAt = remoteModifiedAt,
            RemoteRevision = remoteRevision,
            DeviceId = remoteDeviceId,
            DetectedAt = _clock.UtcNow,
        };

        await _conflicts.AddAsync(conflict, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Conflict recorded for {EntityType} {EntityId}.", entityType, entityId);
        return conflict;
    }
}
