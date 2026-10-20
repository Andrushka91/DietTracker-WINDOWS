using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>
/// Provider agnostic cloud storage used by the synchronization engine.
/// Implementations exist for Microsoft OneDrive (<c>Files.ReadWrite.AppFolder</c>) and
/// Google Drive (<c>appDataFolder</c>); the engine never depends on their specific semantics.
/// </summary>
public interface ICloudStorageProvider
{
    ProviderType ProviderType { get; }

    /// <summary>Resolves the application root folder and prepares the provider (idempotent).</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads (or replaces) an object. Implementations must support optimistic concurrency through
    /// <see cref="CloudObject.Revision"/> when the provider offers it.
    /// </summary>
    Task<CloudObject> UploadAsync(CloudObject item, CancellationToken cancellationToken = default);

    /// <summary>Downloads metadata and content of a single object, or <see langword="null"/> when it no longer exists.</summary>
    Task<CloudObject?> DownloadAsync(string objectId, CancellationToken cancellationToken = default);

    /// <summary>Downloads content only; used for photographs to avoid holding metadata in memory.</summary>
    Task<Stream?> OpenReadAsync(string objectId, CancellationToken cancellationToken = default);

    /// <summary>Enumerates objects changed since the supplied cursor. A null cursor means "full enumeration".</summary>
    Task<CloudChangeSet> GetChangesAsync(SyncCursor? cursor, CancellationToken cancellationToken = default);

    /// <summary>Deletes an object. Deleting something that is already gone must not fail.</summary>
    Task DeleteAsync(string objectId, CancellationToken cancellationToken = default);

    /// <summary>Lists objects under a logical path prefix (used by the restore preview).</summary>
    Task<IReadOnlyList<CloudObject>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default);

    /// <summary>Quota information, when the provider exposes it.</summary>
    Task<CloudQuota?> GetQuotaAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of a change enumeration, including the cursor to use on the next cycle.</summary>
public sealed record CloudChangeSet
{
    public IReadOnlyList<CloudObject> Items { get; init; } = [];

    /// <summary>Cursor to persist for the next cycle. Null means "keep the previous cursor".</summary>
    public SyncCursor? NextCursor { get; init; }

    /// <summary>True when the provider reported that the previous cursor expired and a resync is required.</summary>
    public bool CursorExpired { get; init; }

    public static CloudChangeSet Empty { get; } = new();
}

/// <summary>Quota information reported by a provider.</summary>
public sealed record CloudQuota
{
    public long? TotalBytes { get; init; }

    public long? RemainingBytes { get; init; }

    public bool IsInsufficient(long requiredBytes) => RemainingBytes is { } remaining && remaining < requiredBytes;
}

/// <summary>Creates the storage provider for a given account. Keeps the engine provider agnostic.</summary>
public interface ICloudStorageProviderFactory
{
    ICloudStorageProvider Create(ProviderType providerType);
}

/// <summary>Serializes entities to and from the versioned cloud payload format.</summary>
public interface ICloudPayloadSerializer
{
    /// <summary>Object path used for an entity inside the application folder.</summary>
    string GetObjectPath(EntityType entityType, Guid entityId);

    /// <summary>Object path used for a photograph binary inside the application folder.</summary>
    string GetPhotoObjectPath(Guid photoId);

    /// <summary>Object path of the manifest describing the synchronized dataset.</summary>
    string ManifestPath { get; }

    string Serialize<T>(CloudEnvelope<T> envelope);

    CloudEnvelope<T>? Deserialize<T>(string json);

    /// <summary>Reads only the envelope header, used to cheaply decide whether a payload must be processed.</summary>
    (int SchemaVersion, EntityType EntityType, Guid EntityId, long Revision, DateTimeOffset UpdatedAt, string DeviceId, bool IsDeleted, string ContentHash)? ReadHeader(string json);
}
