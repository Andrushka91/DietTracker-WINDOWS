namespace DietTracker.Core.Enums;

/// <summary>Lifecycle of a single journaled synchronization operation.</summary>
public enum SyncOperationStatus
{
    Pending = 0,
    InProgress = 1,
    Completed = 2,
    Failed = 3,
    Abandoned = 4,
}
