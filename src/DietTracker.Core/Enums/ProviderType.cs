namespace DietTracker.Core.Enums;

/// <summary>Identity / cloud provider selected by the user.</summary>
public enum ProviderType
{
    /// <summary>No account connected (Guest Mode). All features except cloud sync work offline.</summary>
    None = 0,

    /// <summary>Microsoft account (MSAL.NET + Windows Account Manager) with OneDrive app folder storage.</summary>
    Microsoft = 1,

    /// <summary>Google account (OAuth 2.0 + PKCE) with Google Drive app data folder storage.</summary>
    Google = 2,
}
