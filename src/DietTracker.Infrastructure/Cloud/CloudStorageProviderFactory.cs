using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Infrastructure.Cloud.GoogleDrive;
using DietTracker.Infrastructure.Cloud.OneDrive;

namespace DietTracker.Infrastructure.Cloud;

/// <summary>
/// Creates the storage provider that belongs to an account. This is the single place where the
/// engine is bound to a concrete provider, so adding a third cloud later does not touch the
/// synchronization logic.
/// </summary>
public sealed class CloudStorageProviderFactory : ICloudStorageProviderFactory
{
    private readonly IAuthenticationProviderFactory _authentication;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceProvider _services;

    public CloudStorageProviderFactory(
        IAuthenticationProviderFactory authentication,
        IHttpClientFactory httpClientFactory,
        IServiceProvider services)
    {
        _authentication = authentication;
        _httpClientFactory = httpClientFactory;
        _services = services;
    }

    /// <inheritdoc />
    public ICloudStorageProvider Create(ProviderType providerType) => providerType switch
    {
        ProviderType.Microsoft => new OneDriveStorageProvider(
            _authentication.Get(ProviderType.Microsoft),
            _httpClientFactory.CreateClient("cloud"),
            Resolve<ILogger<OneDriveStorageProvider>>()),

        ProviderType.Google => new GoogleDriveStorageProvider(
            _authentication.Get(ProviderType.Google),
            _httpClientFactory.CreateClient("cloud"),
            Resolve<ILogger<GoogleDriveStorageProvider>>()),

        _ => throw new NotSupportedException($"No cloud storage provider is registered for '{providerType}'."),
    };

    private T Resolve<T>() where T : notnull
        => (T)(_services.GetService(typeof(T)) ?? throw new InvalidOperationException($"Service '{typeof(T).Name}' is not registered."));
}
