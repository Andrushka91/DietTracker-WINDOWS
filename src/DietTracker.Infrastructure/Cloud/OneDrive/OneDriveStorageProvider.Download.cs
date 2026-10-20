using System.Net;
using System.Text.Json;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.OneDrive;

/// <summary>Download and delete operations of the OneDrive provider.</summary>
public sealed partial class OneDriveStorageProvider
{
    /// <inheritdoc />
    public async Task<CloudObject?> DownloadAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        try
        {
            using var metadataResponse = await SendAsync(
                HttpMethod.Get,
                $"/me/drive/special/approot:/{objectId}?$select=id,name,size,eTag,cTag,lastModifiedDateTime",
                content: null,
                cancellationToken).ConfigureAwait(false);

            using var document = JsonDocument.Parse(await metadataResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var element = document.RootElement;

            byte[]? bytes = null;
            await using (var stream = await OpenReadAsync(objectId, cancellationToken).ConfigureAwait(false))
            {
                if (stream is not null)
                {
                    using var buffer = new MemoryStream();
                    await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                    bytes = buffer.ToArray();
                }
            }

            return new CloudObject
            {
                ObjectId = objectId,
                Path = objectId,
                Revision = GetRevision(element),
                ModifiedAt = GetModified(element),
                SizeBytes = element.TryGetProperty("size", out var size) ? size.GetInt64() : bytes?.LongLength ?? 0,
                Content = bytes,
            };
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        try
        {
            var response = await SendRawAsync(
                new HttpRequestMessage(HttpMethod.Get, $"{GraphBase}/me/drive/special/approot:/{objectId}:/content"),
                cancellationToken).ConfigureAwait(false);

            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);

        try
        {
            using var response = await SendAsync(
                HttpMethod.Delete,
                $"/me/drive/special/approot:/{objectId}:",
                content: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Already gone: deleting the same object twice must not fail.
            _logger.LogDebug("OneDrive object '{Path}' was already absent.", objectId);
        }
    }
}
