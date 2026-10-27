using System.Text.Json;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.GoogleDrive;

/// <summary>Listing and change tracking for the Google Drive provider.</summary>
public sealed partial class GoogleDriveStorageProvider
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudObject>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await RefreshIndexAsync(cancellationToken).ConfigureAwait(false);

        var prefix = pathPrefix ?? string.Empty;
        var results = new List<CloudObject>();

        foreach (var file in _fileIndex.Values)
        {
            var logicalPath = ToLogicalPath(file.Name);
            if (!logicalPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            results.Add(new CloudObject
            {
                ObjectId = logicalPath,
                Path = logicalPath,
                Revision = file.Version,
                ModifiedAt = file.ModifiedAt,
                SizeBytes = file.SizeBytes,
                ContentHash = file.Hash,
            });
        }

        return results;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The Google Drive changes feed cannot be scoped to <c>appDataFolder</c>, so Diet Tracker uses a
    /// modified-time watermark instead of the global change feed. The watermark is intentionally one
    /// minute in the past to absorb clock skew between the PC and Google; an object may therefore be
    /// examined twice, which is harmless because every apply operation is idempotent.
    /// </remarks>
    public async Task<CloudChangeSet> GetChangesAsync(SyncCursor? cursor, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var watermark = cursor?.Watermark;
        string? query = null;

        if (watermark is null)
        {
            // Full enumeration (first run or after a restore).
            await RefreshIndexAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            query = $"modifiedTime > '{watermark.Value.UtcDateTime:yyyy-MM-ddTHH:mm:ss}'";
        }

        var files = await ListFilesAsync("name,id,version,modifiedTime,size,md5Checksum", query, cancellationToken).ConfigureAwait(false);

        var items = files
            .Where(f => f.Name.Length > 0)
            .Select(f => new CloudObject
            {
                ObjectId = ToLogicalPath(f.Name),
                Path = ToLogicalPath(f.Name),
                Revision = f.Version,
                ModifiedAt = f.ModifiedAt,
                SizeBytes = f.SizeBytes,
                ContentHash = f.Hash,
            })
            .ToArray();

        return new CloudChangeSet
        {
            Items = items,
            NextCursor = new SyncCursor
            {
                ProviderType = ProviderType.Google,
                Watermark = DateTimeOffset.UtcNow.AddMinutes(-1),
            },
        };
    }

    /// <inheritdoc />
    public async Task<CloudQuota?> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendRawAsync(
                new HttpRequestMessage(HttpMethod.Get, $"{DriveBase}/about?fields=storageQuota"),
                cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            if (document.RootElement.TryGetProperty("storageQuota", out var quota))
            {
                var limit = quota.TryGetProperty("limit", out var limitElement) && long.TryParse(limitElement.GetString(), out var total)
                    ? total
                    : (long?)null;
                var usage = quota.TryGetProperty("usage", out var usageElement) && long.TryParse(usageElement.GetString(), out var used)
                    ? used
                    : (long?)null;

                return new CloudQuota
                {
                    TotalBytes = limit,
                    RemainingBytes = limit is not null && usage is not null ? limit - usage : null,
                };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "The Google Drive quota could not be read.");
        }

        return null;
    }
}
