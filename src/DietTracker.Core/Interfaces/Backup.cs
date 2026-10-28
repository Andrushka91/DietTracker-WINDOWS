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
/// <remarks>
/// <see cref="SignOutAsync"/> only forgets the authorization. The cloud-only policy that uploads the
/// pending changes and then empties the local cache is implemented by
/// <see cref="ICloudDataSession"/>, which calls this service after a successful sign-out.
/// </remarks>
public interface IPrivacyService
{
    /// <summary>Signs out of the provider. Local meals, photos and history are preserved.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Turns synchronization off but keeps the authorization and all local data.</summary>
    Task DisableSynchronizationAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the user's upload consent: <see langword="true"/> allows meals and photographs to be
    /// uploaded, <see langword="false"/> keeps everything on this device. The choice is stored on the
    /// connected account and in the preferences, so it survives a restart and is visible to every
    /// screen. Signing out is a separate operation.
    /// </summary>
    Task SetUploadConsentAsync(bool granted, CancellationToken cancellationToken = default);

    /// <summary>Forgets the authorization for an account (tokens removed from secure storage).</summary>
    Task DisconnectAccountAsync(Guid cloudAccountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes every local meal, photo, template and reminder of the active profile, together with the
    /// synchronization journal and the provider cursor (so the connected account is enumerated again
    /// instead of the device believing that it is up to date). Cloud data is untouched.
    /// </summary>
    Task DeleteLocalDataAsync(bool includePhotographs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every local trace of user data, regardless of the profile that owns it: meals,
    /// photographs, templates, reminders, conflicts, deletion markers, the synchronization journal and
    /// the provider cursor. Photograph files are deleted too when <paramref name="includePhotographs"/>
    /// is set. Preferences, the remembered account and the credentials are kept, so the next sign-in
    /// needs no configuration. Cloud data is untouched.
    /// </summary>
    /// <returns>Number of photograph files that were removed.</returns>
    Task<int> PurgeLocalCacheAsync(bool includePhotographs, CancellationToken cancellationToken = default);

    /// <summary>Deletes the Diet Tracker objects in the connected cloud account. Returns the number of deleted objects.</summary>
    Task<int> DeleteCloudDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Deletes only the local photograph files, keeping the metadata rows.</summary>
    Task<int> DeleteAllPhotographsAsync(CancellationToken cancellationToken = default);
}
