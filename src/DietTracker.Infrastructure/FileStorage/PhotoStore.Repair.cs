using DietTracker.Core.BusinessRules;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.FileStorage;

/// <summary>Maintenance pass that keeps the thumbnails of the photo store consistent with their sources.</summary>
public sealed partial class PhotoStore
{
    /// <inheritdoc />
    public async Task<int> RepairThumbnailsAsync(IReadOnlyList<MealPhoto> photos, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photos);

        var repaired = 0;
        foreach (var photo in photos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourcePath = GetFullSizePath(photo.LocalFileId);
            var thumbnailPath = GetThumbnailPath(photo.ThumbnailId);
            if (sourcePath is null || thumbnailPath is null || !File.Exists(sourcePath))
            {
                // Nothing local to heal: cloud-only photograph or an incomplete record.
                continue;
            }

            if (await ThumbnailGenerator
                .IsThumbnailValidAsync(sourcePath, thumbnailPath, PhotoRules.ThumbnailSize, cancellationToken)
                .ConfigureAwait(false))
            {
                continue;
            }

            // The existing thumbnail id is reused, so no database record has to change.
            if (await ThumbnailGenerator
                .TryCreateAsync(sourcePath, thumbnailPath, PhotoRules.ThumbnailSize, cancellationToken)
                .ConfigureAwait(false))
            {
                repaired++;
                _logger.LogInformation(
                    "Regenerated the damaged thumbnail {ThumbnailId} of photograph {PhotoId}.",
                    photo.ThumbnailId,
                    photo.Id);
            }
        }

        return repaired;
    }
}