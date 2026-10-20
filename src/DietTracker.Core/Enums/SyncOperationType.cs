namespace DietTracker.Core.Enums;

/// <summary>Operation recorded in the local synchronization journal (outbox).</summary>
public enum SyncOperationType
{
    Upsert = 0,
    Delete = 1,
}
