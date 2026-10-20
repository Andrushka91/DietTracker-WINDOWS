namespace DietTracker.Core.Interfaces;

/// <summary>
/// Raised by a cloud provider when the user has to sign in again before synchronization can
/// continue. The engine maps it to the "Authentication Required" state instead of an error, and the
/// local database is never modified.
/// </summary>
public sealed class CloudAuthorizationRequiredException : Exception
{
    public CloudAuthorizationRequiredException(string reasonCode, string? diagnostic = null)
        : base($"The cloud provider requires reauthentication ({reasonCode}).")
    {
        ReasonCode = reasonCode;
        Diagnostic = diagnostic;
    }

    /// <summary>Stable reason code that the UI maps to a localized message.</summary>
    public string ReasonCode { get; }

    /// <summary>Technical detail for logs only; never contains tokens or personal data.</summary>
    public string? Diagnostic { get; }
}

/// <summary>
/// Raised when the provider rejects an operation because the user's cloud storage is full. The
/// engine reports it to the user and keeps the local change journaled for a later retry.
/// </summary>
public sealed class CloudQuotaExceededException : Exception
{
    public CloudQuotaExceededException(string message, long? requiredBytes = null)
        : base(message)
        => RequiredBytes = requiredBytes;

    public long? RequiredBytes { get; }
}
