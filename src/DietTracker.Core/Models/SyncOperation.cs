using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>One entry of the local synchronization journal (durable outbox).</summary>
public sealed class SyncOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CloudAccountId { get; set; }

    public EntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public SyncOperationType OperationType { get; set; }

    public SyncOperationStatus Status { get; set; } = SyncOperationStatus.Pending;

    /// <summary>Monotonic revision of the entity when the operation was journaled.</summary>
    public long EntityRevision { get; set; }

    public int RetryCount { get; set; }

    /// <summary>Last recorded failure reason (sanitized, never contains tokens).</summary>
    public string? LastError { get; set; }

    /// <summary>When the next attempt is allowed (exponential backoff).</summary>
    public DateTimeOffset? NextAttemptAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; set; }
}
