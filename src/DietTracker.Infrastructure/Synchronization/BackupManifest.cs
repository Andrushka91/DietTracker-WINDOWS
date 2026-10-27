using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>
/// Content of the <c>backup.json</c> entry inside a Diet Tracker backup archive.
/// </summary>
/// <remarks>
/// The manifest uses the same versioned record format as the cloud payload, so a backup can be
/// restored by any build that understands that schema version and can be migrated forward later.
/// </remarks>
public sealed class BackupManifest
{
    /// <summary>Schema version of the manifest; see <see cref="CloudSchema"/>.</summary>
    public int SchemaVersion { get; set; } = CloudSchema.CurrentVersion;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Non-identifying label of the machine that produced the backup.</summary>
    public string DeviceId { get; set; } = string.Empty;

    public IReadOnlyList<MealEntry> Meals { get; set; } = [];

    public IReadOnlyList<MealTemplate> Templates { get; set; } = [];

    public IReadOnlyList<ReminderSettings> Reminders { get; set; } = [];

    /// <summary>Metadata of the photographs contained in the archive.</summary>
    public IReadOnlyList<MealPhoto> Photos { get; set; } = [];
}
