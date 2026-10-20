namespace DietTracker.Core.Enums;

/// <summary>Per-record synchronization state stored alongside every synced entity.</summary>
public enum SyncStatus
{
    /// <summary>Record only exists locally; nothing has been uploaded yet.</summary>
    PendingUpload = 0,

    /// <summary>Record is identical to the last known cloud revision.</summary>
    Synced = 1,

    /// <summary>Record was modified locally after the last successful upload.</summary>
    PendingUpdate = 2,

    /// <summary>Record is marked for deletion locally (tombstone) and must be deleted in the cloud.</summary>
    PendingDelete = 3,

    /// <summary>Last synchronization attempt for this record failed (retryable).</summary>
    Failed = 4,

    /// <summary>A remote change conflicts with the local change and requires user resolution.</summary>
    Conflict = 5,
}
