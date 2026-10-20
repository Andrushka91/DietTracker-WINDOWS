using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// Per provider / per account synchronization bookkeeping: cursors, timestamps and schema version.
/// </summary>
public sealed class SyncMetadata
{
    public ProviderType ProviderType { get; set; }

    public Guid CloudAccountId { get; set; }

    /// <summary>Opaque provider cursor (Graph delta link / Drive page token) of the last completed cycle.</summary>
    public string? LastSyncCursor { get; set; }

    public DateTimeOffset? LastSyncTimestamp { get; set; }

    /// <summary>Schema version of the cloud payload format that was written / read.</summary>
    public int SchemaVersion { get; set; } = CloudSchema.CurrentVersion;

    /// <summary>Identifier of this installation; used to detect echoes of our own uploads.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>ETag of the remote manifest file observed during the last successful cycle.</summary>
    public string? ManifestRevision { get; set; }
}

/// <summary>Versioning information for the cloud data format.</summary>
public static class CloudSchema
{
    /// <summary>Current cloud payload schema version written by this build.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Oldest cloud payload schema version this build can still read.</summary>
    public const int MinimumSupportedVersion = 1;
}
