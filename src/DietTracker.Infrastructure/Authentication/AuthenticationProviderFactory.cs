using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Infrastructure.Authentication.Google;
using DietTracker.Infrastructure.Authentication.Microsoft;

namespace DietTracker.Infrastructure.Authentication;

/// <summary>
/// Resolves the authentication provider for a given identity provider. Providers are singletons so
/// that their in-memory token state and their internal locks survive across calls.
/// </summary>
public sealed class AuthenticationProviderFactory : IAuthenticationProviderFactory
{
    private readonly IReadOnlyDictionary<ProviderType, IAuthenticationProvider> _providers;

    public AuthenticationProviderFactory(
        MsalAuthenticationProvider microsoft,
        GoogleAuthenticationProvider google)
    {
        _providers = new Dictionary<ProviderType, IAuthenticationProvider>
        {
            [ProviderType.Microsoft] = microsoft,
            [ProviderType.Google] = google,
        };
    }

    /// <inheritdoc />
    public IReadOnlyList<ProviderType> SupportedProviders => [ProviderType.Microsoft, ProviderType.Google];

    /// <inheritdoc />
    public IAuthenticationProvider Get(ProviderType providerType)
        => _providers.TryGetValue(providerType, out var provider)
            ? provider
            : throw new NotSupportedException($"No authentication provider is registered for '{providerType}'.");
}
