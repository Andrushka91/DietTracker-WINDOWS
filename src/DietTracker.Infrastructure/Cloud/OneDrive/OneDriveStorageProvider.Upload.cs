using System.Net.Http.Headers;
using System.Text.Json;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.OneDrive;

/// <summary>Upload operations of the OneDrive provider (simple upload + resumable session).</summary>
public sealed partial class OneDriveStorageProvider
{
    /// <inheritdoc />
    public async Task<CloudObject> UploadAsync(CloudObject item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var content = item.Content ?? throw new InvalidOperationException("The cloud object has no content to upload.");
        JsonElement result;

        if (content.LongLength <= SimpleUploadLimitBytes)
        {
            using var response = await SendAsync(
                HttpMethod.Put,
                $"/me/drive/special/approot:/{item.Path}:/content",
                content,
                cancellationToken,
                optimisticConcurrency: item.Revision).ConfigureAwait(false);

            result = JsonDocument
                .Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
                .RootElement.Clone();
        }
        else
        {
            result = await UploadInSessionAsync(item, content, cancellationToken).ConfigureAwait(false);
        }

        return new CloudObject
        {
            ObjectId = item.Path,
            Path = item.Path,
            Revision = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("eTag", out var eTag) ? eTag.GetString() : null,
            ModifiedAt = result.ValueKind == JsonValueKind.Object
                         && result.TryGetProperty("lastModifiedDateTime", out var modified)
                         && DateTimeOffset.TryParse(modified.GetString(), out var parsed)
                ? parsed
                : DateTimeOffset.UtcNow,
            SizeBytes = content.LongLength,
            ContentHash = item.ContentHash,
        };
    }

    /// <summary>Resumable upload used for photographs larger than the 4 MB simple upload limit.</summary>
    private async Task<JsonElement> UploadInSessionAsync(CloudObject item, byte[] content, CancellationToken cancellationToken)
    {
        using var sessionResponse = await SendAsync(
            HttpMethod.Post,
            $"/me/drive/special/approot:/{item.Path}:/createUploadSession",
            new Dictionary<string, object>
            {
                ["item"] = new Dictionary<string, object> { ["@microsoft.graph.conflictBehavior"] = "replace" },
            },
            cancellationToken).ConfigureAwait(false);

        var session = JsonDocument
            .Parse(await sessionResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false))
            .RootElement.Clone();

        var uploadUrl = session.TryGetProperty("uploadUrl", out var url) ? url.GetString() : null;
        if (string.IsNullOrEmpty(uploadUrl))
        {
            throw new InvalidOperationException("OneDrive did not return an upload session URL.");
        }

        const int chunkSize = 3 * 1024 * 1024;
        var last = default(JsonElement);

        for (var offset = 0; offset < content.Length; offset += chunkSize)
        {
            var length = Math.Min(chunkSize, content.Length - offset);
            using var request = new HttpRequestMessage(HttpMethod.Put, uploadUrl)
            {
                Content = new ByteArrayContent(content, offset, length),
            };
            request.Content.Headers.ContentLength = length;
            request.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + length - 1, content.Length);

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(body))
            {
                last = JsonDocument.Parse(body).RootElement.Clone();
            }
        }

        return last;
    }
}
