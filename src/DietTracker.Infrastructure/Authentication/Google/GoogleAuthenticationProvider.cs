using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using Microsoft.Extensions.Options;

namespace DietTracker.Infrastructure.Authentication.Google;

/// <summary>
/// Google authentication: OAuth 2.0 authorization code flow with PKCE, DPAPI protected token storage
/// and automatic session restoration through refresh tokens.
/// </summary>
public sealed partial class GoogleAuthenticationProvider : IAuthenticationProvider
{
    /// <summary>Access tokens are refreshed this long before they expire.</summary>
    internal static readonly TimeSpan ExpiryMargin = TimeSpan.FromMinutes(5);

    private readonly GoogleAuthenticationOptions _options;
    private readonly ITokenStore _tokenStore;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleAuthenticationProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private GoogleTokenSet? _tokens;
    private GoogleIdentity? _identity;

    public GoogleAuthenticationProvider(
        IOptions<GoogleAuthenticationOptions> options,
        ITokenStore tokenStore,
        HttpClient httpClient,
        ILogger<GoogleAuthenticationProvider> logger)
    {
        _options = options.Value;
        _tokenStore = tokenStore;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public ProviderType ProviderType => ProviderType.Google;

    /// <inheritdoc />
    public async Task<AuthenticationResult> AcquireSilentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokens = await LoadTokensAsync(cancellationToken).ConfigureAwait(false);
            if (tokens?.RefreshToken is null)
            {
                return AuthenticationResult.Failure(ProviderType.Google, "auth.no-account");
            }

            if (!tokens.IsValid(ExpiryMargin))
            {
                tokens = await GooglePkceFlow.RefreshAsync(_options, _httpClient, tokens.RefreshToken, cancellationToken).ConfigureAwait(false)
                         ?? throw new CloudAuthorizationRequiredException("google.refresh-failed");

                _tokens = tokens;
                await PersistTokensAsync(tokens, cancellationToken).ConfigureAwait(false);
            }

            var identity = await EnsureIdentityAsync(cancellationToken).ConfigureAwait(false);
            return identity is null
                ? AuthenticationResult.Failure(ProviderType.Google, "google.identity-unavailable")
                : AuthenticationResult.Ok(ProviderType.Google, identity.Subject, identity.Name, identity.Email, silent: true);
        }
        catch (CloudAuthorizationRequiredException ex)
        {
            // Consent is required again (revoked or expired refresh token). The user is asked, and no
            // local record is ever deleted because of it.
            _logger.LogInformation("The Google session could not be restored silently ({Reason}).", ex.ReasonCode);
            return AuthenticationResult.Failure(ProviderType.Google, ex.ReasonCode, ex.Diagnostic);
        }
        catch (HttpRequestException ex)
        {
            return AuthenticationResult.Failure(ProviderType.Google, "google.network-error", ex.GetType().Name);
        }
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> SignInInteractiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tokens = await GooglePkceFlow.RunAsync(_options, _httpClient, cancellationToken).ConfigureAwait(false);
            if (tokens is null)
            {
                return AuthenticationResult.Cancelled(ProviderType.Google);
            }

            _tokens = tokens;
            _identity = null;
            await PersistTokensAsync(tokens, cancellationToken).ConfigureAwait(false);

            var identity = await EnsureIdentityAsync(cancellationToken).ConfigureAwait(false);
            return identity is null
                ? AuthenticationResult.Failure(ProviderType.Google, "google.identity-unavailable")
                : AuthenticationResult.Ok(ProviderType.Google, identity.Subject, identity.Name, identity.Email, silent: false);
        }
        catch (OperationCanceledException)
        {
            return AuthenticationResult.Cancelled(ProviderType.Google);
        }
        catch (CloudAuthorizationRequiredException ex)
        {
            _logger.LogWarning("Google authorization failed ({Reason}).", ex.ReasonCode);
            if (!string.IsNullOrEmpty(ex.Diagnostic))
            {
                _logger.LogWarning("Google token endpoint detail: {Diagnostic}", ex.Diagnostic);
            }

            return AuthenticationResult.Failure(ProviderType.Google, ex.ReasonCode, ex.Diagnostic);
        }
    }

    /// <inheritdoc />
    public async Task<bool> HasValidSessionAsync(CancellationToken cancellationToken = default)
    {
        var result = await AcquireSilentAsync(cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        _tokens = null;
        _identity = null;
        await _tokenStore.DeleteAsync(_options.TokenStoreKey, cancellationToken).ConfigureAwait(false);
    }
}
