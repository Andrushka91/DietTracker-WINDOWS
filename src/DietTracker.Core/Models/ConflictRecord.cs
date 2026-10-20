using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// A record whose local and remote revisions diverged in a way that cannot be merged
/// automatically. Both versions are preserved until the user picks one; no data is silently lost.
/// </summary>
public sealed class ConflictRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    /// <summary>Local payload at detection time (JSON).</summary>
    public string LocalPayload { get; set; } = string.Empty;

    /// <summary>Remote payload at detection time (JSON).</summary>
    public string RemotePayload { get; set; } = string.Empty;

    public DateTimeOffset LocalModifiedAt { get; set; }

    public DateTimeOffset RemoteModifiedAt { get; set; }

    public string? RemoteRevision { get; set; }

    public string? DeviceId { get; set; }

    public DateTimeOffset DetectedAt { get; set; } = DateTimeOffset.UtcNow;

    public bool IsResolved { get; set; }
}

/// <summary>
/// Deletion marker. Deletions are propagated as tombstones instead of hard deletes so that
/// a second PC does not re-upload a record the user removed.
/// </summary>
public sealed class DeletionTombstone
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public EntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public DateTimeOffset DeletedAt { get; set; } = DateTimeOffset.UtcNow;

    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendingDelete;
}
