using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>Result of an authentication attempt.</summary>
public sealed record AuthenticationResult
{
    public bool Succeeded { get; init; }

    /// <summary>True when the user dismissed the provider dialog. Not an error.</summary>
    public bool IsCancelled { get; init; }

    /// <summary>True when the flow completed without showing any UI (silent / restored session).</summary>
    public bool Silent { get; init; }

    public ProviderType ProviderType { get; init; }

    public string ProviderUserId { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    /// <summary>Stable reason code that the UI maps to a localized message.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>Diagnostic detail for logs only; never contains tokens, codes or personal data.</summary>
    public string? Diagnostic { get; init; }

    public static AuthenticationResult Ok(ProviderType provider, string userId, string displayName, string email, bool silent)
        => new()
        {
            Succeeded = true,
            Silent = silent,
            ProviderType = provider,
            ProviderUserId = userId,
            DisplayName = displayName,
            Email = email,
        };

    public static AuthenticationResult Cancelled(ProviderType provider)
        => new() { IsCancelled = true, ProviderType = provider, ReasonCode = "auth.cancelled" };

    public static AuthenticationResult Failure(ProviderType provider, string reasonCode, string? diagnostic = null)
        => new() { ProviderType = provider, ReasonCode = reasonCode, Diagnostic = diagnostic };
}

/// <summary>
/// Provider specific authentication (Microsoft via MSAL, Google via OAuth 2.0 + PKCE).
/// Implementations must never ask the user to type a password into DietTracker.
/// </summary>
public interface IAuthenticationProvider
{
    ProviderType ProviderType { get; }

    /// <summary>
    /// Tries to restore the previously authorized session without user interaction.
    /// Returns <see cref="AuthenticationResult.Cancelled"/> style failure and never throws when
    /// the provider requires interactive consent.
    /// </summary>
    Task<AuthenticationResult> AcquireSilentAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs the interactive flow (account picker / system browser).</summary>
    Task<AuthenticationResult> SignInInteractiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a currently valid access token, refreshing silently when possible.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// URL of the profile picture of the connected account, or an empty string when the provider has
    /// none (no photo, no session, identity unavailable). Display only: the value is never persisted,
    /// because a provider may hand out a different URL for the same account later.
    /// </summary>
    Task<string> GetProfilePhotoUrlAsync(CancellationToken cancellationToken = default);

    /// <summary>True when a token can be produced without user interaction right now.</summary>
    Task<bool> HasValidSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes cached tokens for the current account locally (does not delete cloud data).</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);
}

/// <summary>Resolves authentication providers and keeps track of the account the user selected.</summary>
public interface IAuthenticationProviderFactory
{
    IAuthenticationProvider Get(ProviderType providerType);

    IReadOnlyList<ProviderType> SupportedProviders { get; }
}

/// <summary>Coordinates sign-in, silent restore, account switching and sign-out at application level.</summary>
public interface IAuthenticationCoordinator
{
    /// <summary>Account currently used for synchronization, or <see langword="null"/> in Guest Mode.</summary>
    Task<CloudAccount?> GetActiveAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Profile picture of the active account (for the avatar in the interface), or an empty string in
    /// Guest Mode, while the provider has no photo or when it cannot be read right now - the caller
    /// then keeps its placeholder.
    /// </summary>
    Task<string> GetActiveAccountPhotoAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs the interactive sign-in for a provider and records the chosen account.</summary>
    Task<AuthenticationResult> SignInAsync(ProviderType providerType, bool grantUploadConsent, CancellationToken cancellationToken = default);

    /// <summary>Attempts to restore the previous session silently, starting synchronization when allowed.</summary>
    Task<AuthenticationResult?> TryRestoreSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>Signs out locally. Meal data, photos and local history are preserved.</summary>
    Task SignOutAsync(CancellationToken cancellationToken = default);

    /// <summary>Raises when the provider session changes (sign-in, sign-out, re-auth required).</summary>
    event EventHandler<ProviderType>? ActiveAccountChanged;
}
