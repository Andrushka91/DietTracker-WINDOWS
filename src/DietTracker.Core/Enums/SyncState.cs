namespace DietTracker.Core.Enums;

/// <summary>
/// User-visible synchronization state. Never report <see cref="Synced"/> before the provider
/// has confirmed the upload.
/// </summary>
public enum SyncState
{
    /// <summary>No cloud account connected; synchronization is disabled.</summary>
    NotConfigured = 0,

    /// <summary>Everything local has been confirmed by the provider.</summary>
    Synced = 1,

    /// <summary>A synchronization cycle is currently running.</summary>
    Syncing = 2,

    /// <summary>Local changes exist that have not been uploaded yet.</summary>
    PendingChanges = 3,

    /// <summary>No network connectivity; work continues locally.</summary>
    Offline = 4,

    /// <summary>The provider requires interactive reauthentication.</summary>
    AuthenticationRequired = 5,

    /// <summary>The last cycle failed (retryable or not).</summary>
    Error = 6,

    /// <summary>Synchronization is configured but temporarily paused by the user.</summary>
    Paused = 7,
}
