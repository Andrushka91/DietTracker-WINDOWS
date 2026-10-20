using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>Creates and restores local backups (JSON + photo files) independent of any cloud provider.</summary>
public interface IBackupService
{
    /// <summary>Exports every local record and photograph into a single portable archive.</summary>
    Task<BackupSummary> ExportAsync(string targetFilePath, CancellationToken cancellationToken = default);

    /// <summary>Imports a portable archive. Nothing is deleted; records are merged by identifier.</summary>
    Task<BackupSummary> RestoreAsync(string sourceFilePath, CancellationToken cancellationToken = default);

    /// <summary>Creates an internal pre-migration safety copy inside the application data folder.</summary>
    Task<string?> CreateSafetyCopyAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>Returns the internal safety copies, newest first.</summary>
    Task<IReadOnlyList<string>> ListSafetyCopiesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Explicit, user driven data lifecycle operations. Signing out, disabling synchronization,
/// deleting local data and deleting cloud data are four distinct operations and are never
/// treated as interchangeable.
/// </summary>
public interface IPrivacyService
{
    /// <summary>Signs out of the provider. Local meals, photos and history are preserved.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns synchronization off but keeps the authorization and all local data.</summary>
    Task DisableSynchronizationAsync(CancellationToken cancellationToken = default);

    /// <summary>Forgets the authorization for an account (tokens removed from secure storage).</summary>
    Task DisconnectAccountAsync(Guid cloudAccountId, CancellationToken cancellationToken = default);

    /// <summary>Deletes every local meal, photo, template and reminder. Cloud data is untouched.</summary>
    Task DeleteLocalDataAsync(bool includePhotographs, CancellationToken cancellationToken = default);

    /// <summary>Deletes the DietTracker objects in the connected cloud account. Returns the number of deleted objects.</summary>
    Task<int> DeleteCloudDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes only the local photograph files, keeping the metadata rows.</summary>
    Task<int> DeleteAllPhotographsAsync(CancellationToken cancellationToken = default);
}
