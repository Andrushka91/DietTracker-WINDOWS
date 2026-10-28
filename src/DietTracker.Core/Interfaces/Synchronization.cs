using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>Why a synchronization cycle was started.</summary>
public enum SyncTrigger
{
    Startup = 0,
    PostSignIn = 1,
    LocalChange = 2,
    ConnectivityRestored = 3,
    Periodic = 4,
    Manual = 5,
    PostRestore = 6,
}

/// <summary>Payload of <see cref="ISyncEngine.StateChanged"/>.</summary>
public sealed record SyncStateChangedEventArgs
{
    public required SyncState State { get; init; }

    public SyncResult? LastResult { get; init; }

    public DateTimeOffset? LastSuccessfulSyncAt { get; init; }
}

/// <summary>
/// Offline-first synchronization engine. Local SQLite is always authoritative for the user
/// experience: writes never wait for the cloud, and a failing provider can never corrupt
/// local data.
/// </summary>
public interface ISyncEngine
{
    SyncState State { get; }

    DateTimeOffset? LastSuccessfulSyncAt { get; }

    event EventHandler<SyncStateChangedEventArgs>? StateChanged;

    /// <summary>Requests a cycle; concurrent requests are coalesced through the supplied trigger.</summary>
    Task<SyncResult> SynchronizeAsync(SyncTrigger trigger, CancellationToken cancellationToken = default);

    /// <summary>Debounced request used after local changes (never blocks the caller).</summary>
    void RequestSync(SyncTrigger trigger);

    /// <summary>
    /// Re-reads the connected account and reports the state that follows from it - no account,
    /// synchronization switched off, pending changes or the outcome of the previous cycle - without
    /// contacting the provider. Used at start-up, so a screen never claims that no account is
    /// connected while the user is signed in.
    /// </summary>
    Task<SyncState> RefreshStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Inspects the cloud account and reports what a restore would bring in.</summary>
    Task<RestorePreview?> PreviewRestoreAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads and applies cloud data to this device.</summary>
    Task<SyncResult> RestoreAsync(IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the whole dataset of the connected account and applies every document, including the
    /// ones this installation uploaded itself. Used when the local cache was emptied on purpose (an
    /// account change): the regular pull skips its own uploads as "echo", which would leave a rebuilt
    /// cache empty.
    /// </summary>
    Task<SyncResult> RebuildFromCloudAsync(IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Uploads every local record, ignoring cursors (used after switching providers).</summary>
    Task<SyncResult> PushAllAsync(IProgress<RestoreProgress>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Progress of a long running restore or full upload.</summary>
public sealed record RestoreProgress
{
    public int Processed { get; init; }

    public int Total { get; init; }

    public string Stage { get; init; } = string.Empty;

    public double Percentage => Total <= 0 ? 0d : Math.Round(Processed * 100d / Total, 2);
}

/// <summary>Decides what happens when the same record was changed on two devices.</summary>
public interface IConflictResolver
{
    /// <summary>
    /// Compares a local and a remote revision and decides the outcome.
    /// Provider revisions are preferred over local wall-clock timestamps.
    /// </summary>
    ConflictDecision Decide(ConflictContext context);

    /// <summary>
    /// Stores both versions of a conflicting record so nothing is lost while the user decides.
    /// </summary>
    Task<ConflictRecord> RecordAsync(
        EntityType entityType,
        Guid entityId,
        string localPayload,
        string remotePayload,
        DateTimeOffset localModifiedAt,
        DateTimeOffset remoteModifiedAt,
        string? remoteRevision,
        string? remoteDeviceId,
        CancellationToken cancellationToken = default);

    /// <summary>Applies the user's choice to a previously stored conflict.</summary>
    Task ResolveAsync(Guid conflictId, ConflictResolutionChoice choice, CancellationToken cancellationToken = default);
}

/// <summary>Inputs of a conflict decision.</summary>
public sealed record ConflictContext
{
    public required EntityType EntityType { get; init; }

    public required Guid EntityId { get; init; }

    public long LocalRevision { get; init; }

    public long RemoteRevision { get; init; }

    public string? LocalContentHash { get; init; }

    public string? RemoteContentHash { get; init; }

    public DateTimeOffset LocalUpdatedAt { get; init; }

    public DateTimeOffset RemoteUpdatedAt { get; init; }

    public string? LocalBaseRevision { get; init; }

    public string? RemoteRevisionToken { get; init; }

    public string? RemoteDeviceId { get; init; }

    public string LocalDeviceId { get; init; } = string.Empty;
}

/// <summary>Outcome of a conflict decision.</summary>
public enum ConflictDecision
{
    /// <summary>Payloads are identical; nothing to do.</summary>
    NoChange = 0,

    /// <summary>Remote wins; the local revision was already synchronized and the cloud is newer.</summary>
    AcceptRemote = 1,

    /// <summary>Local wins; the local record is newer and is re-uploaded.</summary>
    KeepLocal = 2,

    /// <summary>Both versions are preserved and the user is asked which one to keep.</summary>
    RequiresUserDecision = 3,
}

/// <summary>User choice for an open conflict.</summary>
public enum ConflictResolutionChoice
{
    KeepLocal = 0,
    KeepRemote = 1,
    KeepBoth = 2,
}
