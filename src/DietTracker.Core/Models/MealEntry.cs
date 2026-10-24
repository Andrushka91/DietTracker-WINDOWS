using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// One meal slot instance for one calendar date. Exactly one <see cref="MealEntry"/> exists
/// per (profile, date, <see cref="MealCategoryKey"/>) tuple; the row is created lazily and is
/// never deleted through normal application operations.
/// </summary>
public sealed class MealEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Local profile that owns this entry (supports several local profiles later on).</summary>
    public Guid LocalProfileId { get; set; }

    /// <summary>Calendar date the meal belongs to. Completion is always date specific.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Reference to the permanent category.</summary>
    public Guid MealCategoryId { get; set; }

    /// <summary>Denormalized fixed key, kept in sync with the category for fast queries and sync.</summary>
    public MealCategoryKey MealCategoryKey { get; set; }

    /// <summary>Meal title, for example "Porridge with berries".</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Free-form food description.</summary>
    public string Ingredients { get; set; } = string.Empty;

    /// <summary>Planned consumption time.</summary>
    public TimeOnly? PlannedTime { get; set; }

    public MealStatus Status { get; set; } = MealStatus.Pending;

    /// <summary>Actual completion timestamp (UTC) - set only when the user confirms the meal.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Monotonic local revision. Incremented on every local write; used for optimistic concurrency.</summary>
    public long Revision { get; set; }

    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendingUpload;

    /// <summary>Server revision/ETag observed during the last successful synchronization.</summary>
    public string? CloudRevision { get; set; }

    /// <summary>Hash of the synchronized payload; used for idempotency, dedupe and integrity checks.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Attached photographs (not part of the aggregate persistence; loaded on demand).</summary>
    public IList<MealPhoto> Photos { get; set; } = new List<MealPhoto>();
}
