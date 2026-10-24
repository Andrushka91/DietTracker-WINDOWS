using System.Net;
using System.Text.Json;
using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Authentication.Google;

/// <summary>Code exchange and refresh-token handling of the PKCE flow.</summary>
internal static partial class GooglePkceFlow
{
    /// <summary>Exchanges a refresh token for a fresh access token.</summary>
    public static async Task<GoogleTokenSet?> RefreshAsync(
        GoogleAuthenticationOptions options,
        HttpClient httpClient,
        string refreshToken,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token",
        };
        AddClientSecret(form, options);

        using var response = await httpClient
            .PostAsync(options.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            // The refresh token was revoked or expired: the user has to authorize again.
            throw new CloudAuthorizationRequiredException(
                "google.refresh-rejected",
                await DescribeTokenErrorAsync(response, cancellationToken).ConfigureAwait(false));
        }

        response.EnsureSuccessStatusCode();
        return ParseTokenResponse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), refreshToken);
    }

    private static async Task<GoogleTokenSet> ExchangeCodeAsync(
        GoogleAuthenticationOptions options,
        HttpClient httpClient,
        string code,
        string verifier,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["code"] = code,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
        };
        AddClientSecret(form, options);

        using var response = await httpClient
            .PostAsync(options.TokenEndpoint, new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new CloudAuthorizationRequiredException(
                "google.code-exchange-failed",
                await DescribeTokenErrorAsync(response, cancellationToken).ConfigureAwait(false));
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseTokenResponse(json, refreshToken: null)
               ?? throw new CloudAuthorizationRequiredException("google.invalid-token-response");
    }

    /// <summary>
    /// Adds the client secret when one is configured. Google's token endpoint expects the secret of
    /// the Desktop app registration even though the secret of an installed application is explicitly
    /// not treated as confidential (RFC 8252); it is read from the local settings file.
    /// </summary>
    private static void AddClientSecret(Dictionary<string, string> form, GoogleAuthenticationOptions options)
    {
        // Values that still look like an unfilled settings placeholder are treated as absent.
        if (!string.IsNullOrEmpty(options.ClientSecret) && !options.ClientSecret.StartsWith('<'))
        {
            form["client_secret"] = options.ClientSecret;
        }
    }

    /// <summary>Extracts the OAuth error fields of a failed token response for the diagnostic log.</summary>
    private static async Task<string> DescribeTokenErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var error = root.TryGetProperty("error", out var errorProperty) ? errorProperty.GetString() : null;
            var description = root.TryGetProperty("error_description", out var descriptionProperty) ? descriptionProperty.GetString() : null;
            return $"{(int)response.StatusCode} {error}: {description}";
        }
        catch (Exception)
        {
            return $"{(int)response.StatusCode} (no OAuth error payload)";
        }
    }

    private static GoogleTokenSet? ParseTokenResponse(string json, string? refreshToken)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (!root.TryGetProperty("access_token", out var accessToken))
            {
                return null;
            }

            var expiresIn = root.TryGetProperty("expires_in", out var expires) && expires.TryGetInt32(out var seconds)
                ? seconds
                : 3600;

            return new GoogleTokenSet
            {
                AccessToken = accessToken.GetString() ?? string.Empty,
                RefreshToken = root.TryGetProperty("refresh_token", out var refresh) ? refresh.GetString() : refreshToken,
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresIn),
                IdToken = root.TryGetProperty("id_token", out var idToken) ? idToken.GetString() : null,
                Scope = root.TryGetProperty("scope", out var scope) ? scope.GetString() : null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
