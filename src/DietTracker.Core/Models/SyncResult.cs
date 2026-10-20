using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>Result of a synchronization cycle, surfaced to the UI.</summary>
public sealed record SyncResult
{
    public bool Succeeded { get; init; }

    public SyncState State { get; init; } = SyncState.NotConfigured;

    public int Uploaded { get; init; }

    public int Downloaded { get; init; }

    public int Deleted { get; init; }

    public int Conflicts { get; init; }

    public int Skipped { get; init; }

    /// <summary>Localized-independent reason code; the UI maps it to a resource string.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Technical detail for logs only - never contains personal data or tokens.</summary>
    public string? Diagnostic { get; init; }

    public DateTimeOffset FinishedAt { get; init; } = DateTimeOffset.UtcNow;

    public bool RequiresReauthentication { get; init; }

    public static SyncResult Ok(int uploaded, int downloaded, int deleted, int conflicts, int skipped)
        => new()
        {
            Succeeded = true,
            State = conflicts > 0 ? SyncState.PendingChanges : SyncState.Synced,
            Uploaded = uploaded,
            Downloaded = downloaded,
            Deleted = deleted,
            Conflicts = conflicts,
            Skipped = skipped,
        };

    public static SyncResult Failure(string reasonCode, string? diagnostic = null, bool requiresReauth = false, SyncState state = SyncState.Error)
        => new() { Succeeded = false, State = state, ReasonCode = reasonCode, Diagnostic = diagnostic, RequiresReauthentication = requiresReauth };
}

/// <summary>Summary returned by the export / backup services.</summary>
public sealed record BackupSummary
{
    public required string FilePath { get; init; }

    public int MealEntries { get; init; }

    public int Photos { get; init; }

    public int Templates { get; init; }

    public int Reminders { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A cloud snapshot discovered during restore ("what is in the cloud account").</summary>
public sealed record RestorePreview
{
    public ProviderType ProviderType { get; init; }

    public string AccountEmail { get; init; } = string.Empty;

    public int MealEntries { get; init; }

    public int Photos { get; init; }

    public int Templates { get; init; }

    public DateTimeOffset? OldestRecord { get; init; }

    public DateTimeOffset? NewestRecord { get; init; }

    public int SchemaVersion { get; init; }

    public bool IsCompatible => SchemaVersion >= CloudSchema.MinimumSupportedVersion;
}
