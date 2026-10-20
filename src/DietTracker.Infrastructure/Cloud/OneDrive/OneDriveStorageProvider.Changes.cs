using System.Text.Json;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.OneDrive;

/// <summary>Change tracking and quota reporting for the OneDrive provider.</summary>
public sealed partial class OneDriveStorageProvider
{
    /// <inheritdoc />
    /// <remarks>
    /// Uses the Microsoft Graph delta feed rooted at the application folder, which is the provider
    /// specific change tracking mechanism: the engine only stores the opaque delta link.
    /// </remarks>
    public async Task<CloudChangeSet> GetChangesAsync(SyncCursor? cursor, CancellationToken cancellationToken = default)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var endpoint = string.IsNullOrEmpty(cursor?.Value)
            ? "/me/drive/special/approot/delta?$select=id,name,size,eTag,cTag,lastModifiedDateTime,file,folder,deleted,parentReference"
            : cursor.Value;

        var items = new List<CloudObject>();
        string? deltaLink = null;
        var pages = 0;

        while (!string.IsNullOrEmpty(endpoint) && pages++ < 50)
        {
            if (!endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = GraphBase + endpoint;
            }

            using var response = await SendRawAsync(new HttpRequestMessage(HttpMethod.Get, endpoint), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (document.RootElement.TryGetProperty("value", out var page))
            {
                foreach (var element in page.EnumerateArray())
                {
                    if (element.TryGetProperty("folder", out _))
                    {
                        continue;
                    }

                    var path = BuildPath(element);
                    if (path is null)
                    {
                        continue;
                    }

                    items.Add(new CloudObject
                    {
                        ObjectId = path,
                        Path = path,
                        Revision = GetRevision(element),
                        ModifiedAt = GetModified(element),
                        SizeBytes = element.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    });
                }
            }

            endpoint = document.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;
            deltaLink = document.RootElement.TryGetProperty("@odata.deltaLink", out var delta) ? delta.GetString() : deltaLink;
        }

        return new CloudChangeSet
        {
            Items = items,
            NextCursor = string.IsNullOrEmpty(deltaLink)
                ? cursor
                : new SyncCursor { ProviderType = ProviderType.Microsoft, Value = deltaLink },
        };
    }

    /// <inheritdoc />
    public async Task<CloudQuota?> GetQuotaAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendAsync(HttpMethod.Get, "/me/drive?$select=quota", content: null, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (document.RootElement.TryGetProperty("quota", out var quota))
            {
                return new CloudQuota
                {
                    TotalBytes = quota.TryGetProperty("total", out var total) ? total.GetInt64() : null,
                    RemainingBytes = quota.TryGetProperty("remaining", out var remaining) ? remaining.GetInt64() : null,
                };
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "The OneDrive quota could not be read.");
        }

        return null;
    }

    private static string? BuildPath(JsonElement element)
    {
        if (!element.TryGetProperty("name", out var name) || name.GetString() is not { Length: > 0 } fileName)
        {
            return null;
        }

        if (!element.TryGetProperty("parentReference", out var parent) || !parent.TryGetProperty("path", out var pathElement))
        {
            return null;
        }

        var parentPath = pathElement.GetString();
        if (string.IsNullOrEmpty(parentPath))
        {
            return null;
        }

        // The parent path looks like "/drive/root:/entities/MealEntry"; only the part after the colon
        // belongs to the application folder.
        var marker = parentPath.IndexOf(':');
        var relative = (marker >= 0 ? parentPath[(marker + 1)..] : parentPath).Trim('/');
        return string.IsNullOrEmpty(relative) ? fileName : $"{relative}/{fileName}";
    }
}
