using System.Net;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.GoogleDrive;

/// <summary>Download and delete operations of the Google Drive provider.</summary>
public sealed partial class GoogleDriveStorageProvider
{
    /// <inheritdoc />
    public async Task<CloudObject?> DownloadAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var file = await ResolveAsync(objectId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            return null;
        }

        byte[]? bytes = null;
        await using (var stream = await OpenReadByIdAsync(file.Id, cancellationToken).ConfigureAwait(false))
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
            Revision = file.Version,
            ModifiedAt = file.ModifiedAt,
            SizeBytes = file.SizeBytes,
            ContentHash = file.Hash,
            Content = bytes,
        };
    }

    /// <inheritdoc />
    public async Task<Stream?> OpenReadAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var file = await ResolveAsync(objectId, cancellationToken).ConfigureAwait(false);
        return file is null ? null : await OpenReadByIdAsync(file.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string objectId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectId);
        await InitializeAsync(cancellationToken).ConfigureAwait(false);

        var driveName = ToDriveName(objectId);
        var file = await ResolveAsync(objectId, cancellationToken).ConfigureAwait(false);
        if (file is null)
        {
            // Nothing to delete: the object never existed or was already removed.
            return;
        }

        try
        {
            using var response = await SendRawAsync(
                new HttpRequestMessage(HttpMethod.Delete, $"{DriveBase}/files/{file.Id}"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogDebug("Google Drive object '{Name}' was already absent.", driveName);
        }

        _fileIndex.Remove(driveName);
    }

    private async Task<Stream?> OpenReadByIdAsync(string fileId, CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendRawAsync(
                new HttpRequestMessage(HttpMethod.Get, $"{DriveBase}/files/{fileId}?alt=media"),
                cancellationToken).ConfigureAwait(false);

            return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<DriveFile?> ResolveAsync(string logicalPath, CancellationToken cancellationToken)
    {
        var driveName = ToDriveName(logicalPath);
        if (_fileIndex.TryGetValue(driveName, out var cached))
        {
            return cached;
        }

        // Another device (or the user through the Drive web interface) may have created the object
        // after this instance was initialized, so the index is refreshed once.
        await RefreshIndexAsync(cancellationToken).ConfigureAwait(false);
        return _fileIndex.TryGetValue(driveName, out var file) ? file : null;
    }
}
