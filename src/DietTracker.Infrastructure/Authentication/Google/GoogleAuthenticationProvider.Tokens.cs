using System.Net.Http.Headers;
using System.Text.Json;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Serialization;

namespace DietTracker.Infrastructure.Authentication.Google;

/// <summary>Token management and identity resolution of <see cref="GoogleAuthenticationProvider"/>.</summary>
public sealed partial class GoogleAuthenticationProvider
{
    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var tokens = _tokens ?? await LoadTokensAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new CloudAuthorizationRequiredException("auth.no-account");

            if (!tokens.IsValid(ExpiryMargin))
            {
                if (tokens.RefreshToken is null)
                {
                    throw new CloudAuthorizationRequiredException("google.refresh-token-missing");
                }

                tokens = await GooglePkceFlow.RefreshAsync(_options, _httpClient, tokens.RefreshToken, cancellationToken).ConfigureAwait(false)
                         ?? throw new CloudAuthorizationRequiredException("google.refresh-failed");

                _tokens = tokens;
                await PersistTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
            }

            return tokens.AccessToken;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GoogleTokenSet?> LoadTokensAsync(CancellationToken cancellationToken)
    {
        if (_tokens is not null)
        {
            return _tokens;
        }

        var serialized = await _tokenStore.LoadAsync(_options.TokenStoreKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(serialized))
        {
            return null;
        }

        try
        {
            _tokens = JsonSerializer.Deserialize<GoogleTokenSet>(serialized, DietTrackerJson.Compact);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("The stored Google tokens could not be read ({Reason}).", ex.GetType().Name);
        }

        return _tokens;
    }

    private async Task PersistTokensAsync(GoogleTokenSet tokens, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(tokens, DietTrackerJson.Compact);
        await _tokenStore.SaveAsync(_options.TokenStoreKey, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the minimal identity of the signed-in account (subject, e-mail, display name) from the
    /// OpenID Connect user information endpoint over TLS.
    /// </summary>
    private async Task<GoogleIdentity?> EnsureIdentityAsync(CancellationToken cancellationToken)
    {
        if (_identity is not null)
        {
            return _identity;
        }

        try
        {
            var token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, _options.UserInfoEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            _identity = new GoogleIdentity(
                Subject: root.TryGetProperty("sub", out var sub) ? sub.GetString() ?? string.Empty : string.Empty,
                Email: root.TryGetProperty("email", out var email) ? email.GetString() ?? string.Empty : string.Empty,
                Name: root.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty);

            return _identity;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or CloudAuthorizationRequiredException)
        {
            _logger.LogInformation("The Google account identity could not be read ({Reason}).", ex.GetType().Name);
            return null;
        }
    }

    /// <summary>Minimal OpenID Connect identity of the signed-in Google account.</summary>
    private sealed record GoogleIdentity(string Subject, string Email, string Name);
}
