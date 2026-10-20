using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// A cloud account the user authorized. Several accounts may be remembered (for example one
/// Microsoft and one Google account) but only one is active at a time; switching requires
/// explicit user confirmation (see <c>IAccountSwitchCoordinator</c>).
/// </summary>
public sealed class CloudAccount
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ProviderType ProviderType { get; set; }

    /// <summary>Provider side identifier (Microsoft object id / Google subject).</summary>
    public string ProviderUserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Identifier of the cloud root folder used for synchronization.</summary>
    public string? RootFolderId { get; set; }

    public DateTimeOffset? LastAuthenticatedAt { get; set; }

    public DateTimeOffset? LastSynchronizedAt { get; set; }

    /// <summary>True for exactly one account at a time.</summary>
    public bool IsActive { get; set; }

    /// <summary>Set when the user disabled synchronization but kept the authorization.</summary>
    public bool SynchronizationEnabled { get; set; } = true;

    /// <summary>Quota reported by the provider, in bytes; <see langword="null"/> when unknown.</summary>
    public long? QuotaTotalBytes { get; set; }

    /// <summary>Free space reported by the provider, in bytes; <see langword="null"/> when unknown.</summary>
    public long? QuotaRemainingBytes { get; set; }
}
