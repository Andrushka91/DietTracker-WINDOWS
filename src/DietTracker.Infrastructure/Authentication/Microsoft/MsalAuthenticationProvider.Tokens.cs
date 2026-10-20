using DietTracker.Core.Interfaces;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;
using AuthenticationResult = DietTracker.Core.Interfaces.AuthenticationResult;

namespace DietTracker.Infrastructure.Authentication.Microsoft;

/// <summary>Token acquisition and cache persistence of <see cref="MsalAuthenticationProvider"/>.</summary>
public sealed partial class MsalAuthenticationProvider
{
    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var app = await GetApplicationAsync(cancellationToken).ConfigureAwait(false);
        var account = await GetAccountAsync(app, cancellationToken).ConfigureAwait(false)
            ?? throw new CloudAuthorizationRequiredException("auth.no-account");

        try
        {
            var result = await app
                .AcquireTokenSilent(_options.Scopes, account)
                .ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);

            await PersistCacheAsync(app, cancellationToken).ConfigureAwait(false);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            // Never force an interactive prompt from a background operation: the engine reports
            // "authentication required" and the user decides when to sign in again.
            throw new CloudAuthorizationRequiredException("auth.interaction-required");
        }
    }

    private async Task<IPublicClientApplication> GetApplicationAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_application is not null)
            {
                return _application;
            }

            var builder = PublicClientApplicationBuilder
                .Create(_options.ClientId)
                .WithAuthority(_options.Authority)
                .WithDefaultRedirectUri();

            if (_options.UseWindowsAccountManager)
            {
                try
                {
                    // The Windows Account Manager (WAM) broker provides the native Windows account
                    // picker and single sign-on. When the platform cannot host it, MSAL falls back to
                    // the system browser, which is why this call is protected.
                    builder = builder.WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
                    {
                        Title = "DietTracker",
                    });
                }
                catch (Exception ex)
                {
                    // Broker unavailable (older Windows build, unpackaged process): use the browser.
                    _logger.LogInformation(
                        "Windows Account Manager is unavailable ({Reason}); using the system browser instead.",
                        ex.GetType().Name);
                }
            }

            _application = builder.Build();
            await LoadCacheAsync(_application, cancellationToken).ConfigureAwait(false);
            return _application;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task LoadCacheAsync(IPublicClientApplication app, CancellationToken cancellationToken)
    {
        if (_cacheLoaded)
        {
            return;
        }

        var serialized = await _tokenStore.LoadAsync(_options.TokenCacheKey, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(serialized) && app.UserTokenCache is ITokenCacheSerializer serializer)
        {
            try
            {
                serializer.DeserializeMsalV3(Convert.FromBase64String(serialized), true);
            }
            catch (Exception ex)
            {
                // A cache that cannot be restored simply means "sign in again"; no data is lost.
                _logger.LogWarning("The stored Microsoft token cache could not be restored ({Reason}).", ex.GetType().Name);
            }
        }

        _cacheLoaded = true;
    }

    private async Task PersistCacheAsync(IPublicClientApplication app, CancellationToken cancellationToken)
    {
        if (app.UserTokenCache is not ITokenCacheSerializer serializer)
        {
            return;
        }

        var bytes = serializer.SerializeMsalV3();
        await _tokenStore.SaveAsync(_options.TokenCacheKey, Convert.ToBase64String(bytes), cancellationToken).ConfigureAwait(false);
    }

    private async Task<IAccount?> GetAccountAsync(IPublicClientApplication app, CancellationToken cancellationToken)
    {
        if (_account is not null)
        {
            return _account;
        }

        var accounts = await app.GetAccountsAsync().ConfigureAwait(false);
        _account = accounts.FirstOrDefault();
        return _account;
    }
}
