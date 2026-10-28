using System.Text;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.UnitTests.Support;

/// <summary>
/// In-memory cloud storage provider used by the synchronization tests. It behaves like a provider
/// with revisions, optimistic concurrency and change tracking, so the engine can be exercised without
/// any network access.
/// </summary>
public sealed class InMemoryCloudStorageProvider : ICloudStorageProvider
{
    private readonly Dictionary<string, Entry> _objects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _uploadedPaths = [];
    private int _revision;

    public InMemoryCloudStorageProvider(ProviderType providerType = ProviderType.Google)
        => ProviderType = providerType;

    public ProviderType ProviderType { get; }

    /// <summary>When set, every operation fails, which lets tests prove that a failing provider is harmless.</summary>
    public Exception? FailureToThrow { get; set; }

    /// <summary>Number of uploads performed (used to prove that identical content is not uploaded twice).</summary>
    public int UploadCount => _uploadedPaths.Count;

    /// <summary>Paths of the uploads in the order they happened (used to prove the upload ordering).</summary>
    public IReadOnlyList<string> UploadedPaths => _uploadedPaths;

    public IReadOnlyCollection<string> Paths => _objects.Keys;

    public int ObjectCount => _objects.Count;

    public Task InitializeAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<CloudObject> UploadAsync(CloudObject item, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        _revision++;
        _uploadedPaths.Add(item.Path);
        _objects[item.Path] = new Entry(item.Content ?? [], $"rev-{_revision}", DateTimeOffset.UtcNow, item.ContentHash);

        return Task.FromResult(new CloudObject
        {
            ObjectId = item.Path,
            Path = item.Path,
            Revision = $"rev-{_revision}",
            ModifiedAt = DateTimeOffset.UtcNow,
            SizeBytes = item.Content?.LongLength ?? 0,
            ContentHash = item.ContentHash,
        });
    }

    public Task<CloudObject?> DownloadAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        if (!_objects.TryGetValue(objectId, out var entry))
        {
            return Task.FromResult<CloudObject?>(null);
        }

        return Task.FromResult<CloudObject?>(new CloudObject
        {
            ObjectId = objectId,
            Path = objectId,
            Revision = entry.Revision,
            ModifiedAt = entry.ModifiedAt,
            SizeBytes = entry.Content.LongLength,
            ContentHash = entry.ContentHash,
            Content = entry.Content,
        });
    }

    public Task<Stream?> OpenReadAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        return Task.FromResult<Stream?>(_objects.TryGetValue(objectId, out var entry)
            ? new MemoryStream(entry.Content, writable: false)
            : null);
    }

    public Task<CloudChangeSet> GetChangesAsync(SyncCursor? cursor, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var items = _objects.Select(kv => new CloudObject
        {
            ObjectId = kv.Key,
            Path = kv.Key,
            Revision = kv.Value.Revision,
            ModifiedAt = kv.Value.ModifiedAt,
            SizeBytes = kv.Value.Content.LongLength,
            ContentHash = kv.Value.ContentHash,
            Content = kv.Value.Content,
        }).ToArray();

        return Task.FromResult(new CloudChangeSet
        {
            Items = items,
            NextCursor = new SyncCursor { ProviderType = ProviderType, Value = $"cursor-{items.Length}" },
        });
    }

    public Task DeleteAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        _objects.Remove(objectId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloudObject>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        ThrowIfFailing();
        var items = _objects
            .Where(kv => kv.Key.StartsWith(pathPrefix ?? string.Empty, StringComparison.OrdinalIgnoreCase))
            .Select(kv => new CloudObject
            {
                ObjectId = kv.Key,
                Path = kv.Key,
                Revision = kv.Value.Revision,
                ModifiedAt = kv.Value.ModifiedAt,
                SizeBytes = kv.Value.Content.LongLength,
                ContentHash = kv.Value.ContentHash,
            })
            .ToArray();

        return Task.FromResult<IReadOnlyList<CloudObject>>(items);
    }

    public Task<CloudQuota?> GetQuotaAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<CloudQuota?>(new CloudQuota { TotalBytes = 5L * 1024 * 1024 * 1024, RemainingBytes = 4L * 1024 * 1024 * 1024 });

    /// <summary>Simulates another device writing an object (used by the conflict tests).</summary>
    public void WriteFromAnotherDevice(string path, string json)
        => WriteRawFromAnotherDevice(path, Encoding.UTF8.GetBytes(json));

    /// <summary>Simulates another device writing raw content (used for photograph binaries).</summary>
    public void WriteRawFromAnotherDevice(string path, byte[] content)
    {
        _revision++;
        _objects[path] = new Entry(content, $"rev-{_revision}", DateTimeOffset.UtcNow, "external");
    }

    private void ThrowIfFailing()
    {
        if (FailureToThrow is not null)
        {
            throw FailureToThrow;
        }
    }

    private sealed record Entry(byte[] Content, string Revision, DateTimeOffset ModifiedAt, string? ContentHash);
}
