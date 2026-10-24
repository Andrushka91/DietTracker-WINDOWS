namespace DietTracker.Core.Enums;

/// <summary>Identity / cloud provider selected by the user.</summary>
/// <remarks>
/// The numeric values are persisted in the SQLite database, so the gap left by the removed
/// Microsoft provider (value 1) is kept on purpose: existing databases never need a migration.
/// </remarks>
public enum ProviderType
{
    /// <summary>No account connected (Guest Mode). All features except cloud sync work offline.</summary>
    None = 0,

    /// <summary>Google account (OAuth 2.0 + PKCE) with Google Drive app data folder storage.</summary>
    Google = 2,
}
