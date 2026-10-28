using System.Text;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Synchronization;

/// <summary>Upload support for photograph binaries (metadata document plus binary object).</summary>
public sealed partial class SyncEngine
{
    private async Task<bool> PushPhotoAsync(ICloudStorageProvider provider, SyncOperation operation, CancellationToken cancellationToken)
    {
        var photo = await _photos.GetAsync(operation.EntityId, cancellationToken).ConfigureAwait(false);
        if (photo is null)
        {
            return false;
        }

        // Identical content is never uploaded twice: the content hash identifies the binary.
        if (photo.SyncStatus == SyncStatus.Synced && !string.IsNullOrEmpty(photo.CloudObjectId))
        {
            return false;
        }

        var localPath = _photoStore.GetFullSizePath(photo.LocalFileId);
        if (localPath is null || !File.Exists(localPath))
        {
            // The bytes were deleted from this device (for example by "Delete photographs"), so there
            // is nothing to upload. Failing the operation would retry it forever with backoff.
            _logger.LogInformation("Photograph {PhotoId} is not uploaded: its local file is missing.", photo.Id);
            return false;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The file can vanish or be locked between the existence check and the read: the photograph
            // is skipped this time instead of being parked in a failure state.
            _logger.LogWarning(ex, "Photograph {PhotoId} could not be read and will be retried later.", photo.Id);
            return false;
        }

        // The bytes are uploaded first: the metadata document must never announce a photograph that
        // another device cannot download yet.
        var binaryPath = _serializer.GetPhotoObjectPath(photo.Id);
        await provider.UploadAsync(
            new CloudObject
            {
                ObjectId = binaryPath,
                Path = binaryPath,
                Revision = null,
                ContentHash = photo.ContentHash,
                ModifiedAt = photo.UpdatedAt,
                SizeBytes = bytes.LongLength,
                Content = bytes,
            },
            cancellationToken).ConfigureAwait(false);

        var metadataPath = _serializer.GetObjectPath(EntityType.MealPhoto, photo.Id);
        var envelope = new CloudEnvelope<MealPhoto>
        {
            EntityType = EntityType.MealPhoto,
            EntityId = photo.Id,
            Revision = 1,
            UpdatedAt = photo.UpdatedAt,
            DeviceId = _environment.DeviceId,
            ContentHash = photo.ContentHash,
            Payload = photo,
        };

        var json = _serializer.Serialize(envelope);
        var metadataUpload = await provider.UploadAsync(
            new CloudObject
            {
                ObjectId = metadataPath,
                Path = metadataPath,
                Revision = photo.CloudRevision,
                ContentHash = photo.ContentHash,
                ModifiedAt = photo.UpdatedAt,
                SizeBytes = Encoding.UTF8.GetByteCount(json),
                Content = Encoding.UTF8.GetBytes(json),
            },
            cancellationToken).ConfigureAwait(false);

        photo.CloudRevision = metadataUpload.Revision;

        // The provider's revision token is not an object identifier: the binary is addressed by its
        // logical path, exactly like the pull side addresses it.
        photo.CloudObjectId = binaryPath;
        photo.SyncStatus = SyncStatus.Synced;
        photo.UpdatedAt = _clock.UtcNow;
        await _photos.UpdateAsync(photo, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
