using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using AuthenticationResult = DietTracker.Core.Interfaces.AuthenticationResult;

namespace DietTracker.Infrastructure.Authentication.Microsoft;

/// <summary>
/// Microsoft account authentication built on MSAL.NET, using the Windows Account Manager (WAM)
/// broker when available and falling back to the system browser otherwise.
/// </summary>
/// <remarks>
/// The user never types a password into DietTracker: the account picker belongs to Windows or to
/// Microsoft's own page. Only non-sensitive profile data is persisted, and the token cache is
/// serialized and protected with DPAPI in the per-user secure folder.
/// </remarks>
public sealed partial class MsalAuthenticationProvider : IAuthenticationProvider
{
    private readonly MicrosoftAuthenticationOptions _options;
    private readonly ITokenStore _tokenStore;
    private readonly IUserInterfaceContext _uiContext;
    private readonly ILogger<MsalAuthenticationProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IPublicClientApplication? _application;
    private IAccount? _account;
    private bool _cacheLoaded;

    public MsalAuthenticationProvider(
        IOptions<MicrosoftAuthenticationOptions> options,
        ITokenStore tokenStore,
        IUserInterfaceContext uiContext,
        ILogger<MsalAuthenticationProvider> logger)
    {
        _options = options.Value;
        _tokenStore = tokenStore;
        _uiContext = uiContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public ProviderType ProviderType => ProviderType.Microsoft;

    /// <inheritdoc />
    public async Task<AuthenticationResult> AcquireSilentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var app = await GetApplicationAsync(cancellationToken).ConfigureAwait(false);
            var account = await GetAccountAsync(app, cancellationToken).ConfigureAwait(false);
            if (account is null)
            {
                return AuthenticationResult.Failure(ProviderType.Microsoft, "auth.no-account");
            }

            var result = await app
                .AcquireTokenSilent(_options.Scopes, account)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            await PersistCacheAsync(app, cancellationToken).ConfigureAwait(false);
            var name = result.ClaimsPrincipal?.FindFirst("name")?.Value ?? account.Username;
            var email = account.Username;
            return AuthenticationResult.Ok(ProviderType.Microsoft, result.UniqueId, name, email, silent: true);
        }
        catch (MsalUiRequiredException)
        {
            return AuthenticationResult.Failure(ProviderType.Microsoft, "auth.interaction-required");
        }
        catch (MsalException ex)
        {
            _logger.LogInformation("Silent Microsoft sign-in was not possible ({Error}).", ex.ErrorCode);
            return AuthenticationResult.Failure(ProviderType.Microsoft, "auth.silent-failed", ex.ErrorCode);
        }
    }

    /// <inheritdoc />
    public async Task<AuthenticationResult> SignInInteractiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var app = await GetApplicationAsync(cancellationToken).ConfigureAwait(false);

            var builder = app.AcquireTokenInteractive(_options.Scopes).WithPrompt(Prompt.SelectAccount);

            var handle = _uiContext.GetWindowHandle();
            if (handle != IntPtr.Zero)
            {
                builder = builder.WithParentActivityOrWindow(handle);
            }

            var result = await builder.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            _account = result.Account;
            await PersistCacheAsync(app, cancellationToken).ConfigureAwait(false);

            var name = result.ClaimsPrincipal?.FindFirst("name")?.Value ?? result.Account?.Username ?? string.Empty;
            var email = result.Account?.Username ?? string.Empty;
            return AuthenticationResult.Ok(ProviderType.Microsoft, result.UniqueId, name, email, silent: false);
        }
        catch (MsalClientException ex) when (ex.ErrorCode is "authentication_canceled" or "user_canceled")
        {
            return AuthenticationResult.Cancelled(ProviderType.Microsoft);
        }
        catch (MsalException ex)
        {
            _logger.LogWarning("Interactive Microsoft sign-in failed ({Error}).", ex.ErrorCode);
            return AuthenticationResult.Failure(ProviderType.Microsoft, "auth.microsoft.failed", ex.ErrorCode);
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
        var app = await GetApplicationAsync(cancellationToken).ConfigureAwait(false);
        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);

        foreach (var account in accounts)
        {
            await app.RemoveAsync(account).ConfigureAwait(false);
        }

        _account = null;

        // The serialized cache is removed; local meals, photographs and history are untouched.
        await _tokenStore.DeleteAsync(_options.TokenCacheKey, cancellationToken).ConfigureAwait(false);
    }
}
