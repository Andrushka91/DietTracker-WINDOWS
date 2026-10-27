namespace DietTracker.Infrastructure.Authentication;

/// <summary>Configuration of the Google integration.</summary>
public sealed class GoogleAuthenticationOptions
{
    /// <summary>
    /// OAuth 2.0 client identifier of a "Desktop app" registration in Google Cloud Console.
    /// Desktop clients are public: no client secret is embedded in the application, and PKCE is used
    /// to prove that the authorization code is redeemed by the same application instance.
    /// </summary>
    public string ClientId { get; set; } = "000000000000-diettracker.apps.googleusercontent.com";

    /// <summary>
    /// Client secret of the same "Desktop app" registration. Google's token endpoint expects it even
    /// though the secret of an installed application is explicitly not treated as confidential
    /// (RFC 8252); it is therefore read from the local settings file and never embedded in the build.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Authorization endpoint.</summary>
    public string AuthorizationEndpoint { get; set; } = "https://accounts.google.com/o/oauth2/v2/auth";

    /// <summary>Token endpoint used for the code exchange and for refresh tokens.</summary>
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";

    /// <summary>OpenID Connect user information endpoint (used to read the account identity).</summary>
    public string UserInfoEndpoint { get; set; } = "https://openidconnect.googleapis.com/v1/userinfo";

    /// <summary>
    /// Minimal scopes: identity plus the hidden application data folder of Google Drive.
    /// Diet Tracker never requests access to the user's other Drive files.
    /// </summary>
    public string[] Scopes { get; set; } =
    [
        "openid",
        "email",
        "profile",
        "https://www.googleapis.com/auth/drive.appdata",
    ];

    /// <summary>Key under which the refresh token is stored in the protected token store.</summary>
    public string TokenStoreKey { get; set; } = "google-tokens";
}

/// <summary>Tokens returned by the Google token endpoint.</summary>
public sealed record GoogleTokenSet
{
    public required string AccessToken { get; init; }

    public string? RefreshToken { get; init; }

    public required DateTimeOffset ExpiresAt { get; init; }

    public string? IdToken { get; init; }

    public string? Scope { get; init; }

    /// <summary>True when the access token is still valid for at least the supplied margin.</summary>
    public bool IsValid(TimeSpan margin) => DateTimeOffset.UtcNow + margin < ExpiresAt;
}
