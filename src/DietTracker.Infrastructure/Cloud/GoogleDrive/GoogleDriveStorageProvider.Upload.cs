using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.GoogleDrive;

/// <summary>Upload operations of the Google Drive provider.</summary>
public sealed partial class GoogleDriveStorageProvider
{
    /// <inheritdoc />
    public async Task<CloudObject> UploadAsync(CloudObject item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var content = item.Content ?? throw new InvalidOperationException("The cloud object has no content to upload.");
        var driveName = ToDriveName(item.Path);

        // Reuse the cached file identity so a changed object is updated in place instead of being
        // duplicated (Google Drive has no path-based addressing).
        if (!_fileIndex.TryGetValue(driveName, out var existing))
        {
            await RefreshIndexAsync(cancellationToken).ConfigureAwait(false);
            _fileIndex.TryGetValue(driveName, out existing);
        }

        var metadata = new Dictionary<string, object> { ["name"] = driveName };
        if (existing is null)
        {
            metadata["parents"] = new[] { "appDataFolder" };
        }

        var endpoint = existing is null
            ? $"{UploadBase}/files?uploadType=multipart&fields=id,name,version,modifiedTime,size,md5Checksum"
            : $"{UploadBase}/files/{existing.Id}?uploadType=multipart&fields=id,name,version,modifiedTime,size,md5Checksum";

        var method = existing is null ? HttpMethod.Post : HttpMethod.Patch;
        using var multipart = BuildMultipart(metadata, GetMimeType(item.Path), content);
        using var response = await SendRawAsync(new HttpRequestMessage(method, endpoint) { Content = multipart }, cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var file = ParseFile(document.RootElement);
        _fileIndex[driveName] = file;

        return new CloudObject
        {
            ObjectId = item.Path,
            Path = item.Path,
            Revision = file.Version,
            ModifiedAt = file.ModifiedAt,
            SizeBytes = content.LongLength,
            ContentHash = item.ContentHash,
        };
    }

    /// <summary>Builds a <c>multipart/related</c> body carrying the metadata and the binary content.</summary>
    private static MultipartContent BuildMultipart(Dictionary<string, object> metadata, string mimeType, byte[] content)
    {
        var boundary = "diettracker" + Guid.NewGuid().ToString("N");
        var multipart = new MultipartContent("related", boundary)
        {
            new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json"),
        };

        var mediaPart = new ByteArrayContent(content);
        mediaPart.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
        multipart.Add(mediaPart);

        return multipart;
    }

    private static string GetMimeType(string logicalPath)
        => logicalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? "application/json"
            : "application/octet-stream";
}
