using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// Cloud payload envelope for a single entity. The envelope is provider agnostic and is what
/// gets serialized to the app folder as JSON; it carries everything the synchronization engine
/// needs to detect conflicts and to be idempotent.
/// </summary>
public sealed class CloudEnvelope<T>
{
    /// <summary>Cloud payload schema version.</summary>
    public int SchemaVersion { get; set; } = CloudSchema.CurrentVersion;

    public EntityType EntityType { get; set; }

    public Guid EntityId { get; set; }

    public Guid LocalProfileId { get; set; }

    /// <summary>Local revision the payload was produced from.</summary>
    public long Revision { get; set; }

    /// <summary>UTC timestamp of the local write.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Identifier of the device that produced this revision.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Set for tombstones so that deletions propagate deterministically.</summary>
    public bool IsDeleted { get; set; }

    /// <summary>SHA-256 of <see cref="Payload"/>; used for integrity checks and echo suppression.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Serialized entity (JSON).</summary>
    public T? Payload { get; set; }
}

/// <summary>Cloud object descriptor returned by <c>ICloudStorageProvider</c>.</summary>
public sealed class CloudObject
{
    public required string ObjectId { get; init; }

    /// <summary>Logical path inside the application folder, for example "entities/MealEntry/&lt;guid&gt;.json".</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>Provider revision (ETag / version / modified time token) used for compare-and-swap.</summary>
    public string? Revision { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }

    public long SizeBytes { get; init; }

    public string? ContentHash { get; init; }

    public byte[]? Content { get; init; }
}

/// <summary>Opaque provider cursor describing how far a change feed has been consumed.</summary>
public sealed class SyncCursor
{
    public ProviderType ProviderType { get; init; }

    public string ProviderUserId { get; init; } = string.Empty;

    /// <summary>Provider specific value (Graph delta link, Drive modified-time watermark, ...).</summary>
    public string? Value { get; init; }

    public DateTimeOffset? Watermark { get; init; }

    public bool IsEmpty => string.IsNullOrEmpty(Value) && Watermark is null;
}
