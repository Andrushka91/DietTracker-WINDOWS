using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;

namespace DietTracker.UnitTests.Support;

/// <summary>Provider factory that always returns the supplied in-memory provider.</summary>
public sealed class TestCloudStorageFactory : ICloudStorageProviderFactory
{
    private readonly InMemoryCloudStorageProvider _provider;

    public TestCloudStorageFactory(InMemoryCloudStorageProvider provider) => _provider = provider;

    public ICloudStorageProvider Create(ProviderType providerType) => _provider;
}
