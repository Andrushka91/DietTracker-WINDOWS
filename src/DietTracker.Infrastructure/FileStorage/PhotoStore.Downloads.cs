using DietTracker.Core.BusinessRules;
using DietTracker.Core.Models;
using DietTracker.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.FileStorage;

/// <summary>Download, delete and usage-reporting operations of <see cref="PhotoStore"/>.</summary>
public sealed partial class PhotoStore
{
    /// <inheritdoc />
    public async Task<MealPhoto> StoreDownloadedAsync(Guid mealEntryId, string path, string mimeType, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var header = content.Length >= HeaderSize ? content.AsSpan(0, HeaderSize) : content.AsSpan();
        if (!PhotoRules.HasValidSignature(mimeType, header))
        {
            // Integrity check: a downloaded payload that does not match its declared format is
            // rejected instead of being written into the photo store.
            throw new InvalidDataException("The downloaded photograph does not match its declared image format.");
        }

        var extension = mimeType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".jpg",
        };

        _paths.EnsureCreated();
        var localFileId = PathSanitizer.CreateOpaqueFileName(extension);
        var targetPath = PathSanitizer.CombineInsideDirectory(_paths.PhotoDirectory, localFileId)
            ?? throw new InvalidOperationException("The photo store path is not valid.");

        await File.WriteAllBytesAsync(targetPath, content, cancellationToken).ConfigureAwait(false);
        var thumbnailId = await CreateThumbnailAsync(targetPath, cancellationToken).ConfigureAwait(false);

        return new MealPhoto
        {
            Id = Guid.NewGuid(),
            MealEntryId = mealEntryId,
            OriginalFileName = Path.GetFileName(path),
            LocalFileId = localFileId,
            ThumbnailId = thumbnailId,
            MimeType = mimeType,
            FileSize = content.LongLength,
            ContentHash = Hashing.Sha256(content),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <inheritdoc />
    public Task DeleteFilesAsync(MealPhoto photo, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(photo);

        foreach (var path in new[] { GetFullSizePath(photo.LocalFileId), GetThumbnailPath(photo.ThumbnailId) })
        {
            if (path is null)
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not delete photo file '{Path}'.", path);
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<long> GetStorageUsageAsync(CancellationToken cancellationToken = default)
    {
        long total = 0;
        foreach (var directory in new[] { _paths.PhotoDirectory, _paths.ThumbnailDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            total += new DirectoryInfo(directory)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }

        return Task.FromResult(total);
    }
}
