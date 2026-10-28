using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// Local user profile. The profile is created for Guest Mode as well so that every database
/// row always has an owner, which keeps switching between Guest Mode and signed-in mode safe.
/// </summary>
public sealed class UserProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Provider this profile is bound to; <see cref="ProviderType.None"/> for Guest Mode.</summary>
    public ProviderType ProviderType { get; set; } = ProviderType.None;

    /// <summary>Stable provider side identifier (object id / subject claim). Empty in Guest Mode.</summary>
    public string ProviderUserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>BCP-47 language tag, for example "ro-RO".</summary>
    public string Language { get; set; } = "en-US";

    /// <summary>IANA time zone identifier, for example "Europe/Bucharest".</summary>
    public string TimeZone { get; set; } = "Europe/Bucharest";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendingUpload;
}
