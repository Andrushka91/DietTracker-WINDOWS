using DietTracker.Core.BusinessRules;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using DietTracker.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.FileStorage;

/// <summary>
/// Stores photograph bytes as ordinary files in the per-user private application data folder and
/// generates thumbnails. SQLite only ever holds metadata, never image BLOBs.
/// </summary>
public sealed partial class PhotoStore : IPhotoStore
{
    private const int HeaderSize = 16;

    private readonly IAppDataPaths _paths;
    private readonly ILogger<PhotoStore> _logger;

    public PhotoStore(IAppDataPaths paths, ILogger<PhotoStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PhotoImportResult> ImportAsync(Guid mealEntryId, string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            return PhotoImportResult.Failed("photo.source.missing");
        }

        if (!PathSanitizer.HasAllowedImageExtension(sourcePath))
        {
            return PhotoImportResult.Failed("photo.format.unsupported");
        }

        var info = new FileInfo(sourcePath);
        var header = await ReadHeaderAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        var validation = Core.Validation.PhotoValidator.Validate(info.Name, info.Length, header);
        if (!validation.IsValid)
        {
            return PhotoImportResult.Failed(validation.Failures[0].Code);
        }

        var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
        var mimeType = PhotoRules.MimeTypeFromExtension(extension) ?? "image/jpeg";
        var contentHash = await ComputeHashAsync(sourcePath, cancellationToken).ConfigureAwait(false);

        _paths.EnsureCreated();
        var localFileId = PathSanitizer.CreateOpaqueFileName(extension);
        var targetPath = PathSanitizer.CombineInsideDirectory(_paths.PhotoDirectory, localFileId)
            ?? throw new InvalidOperationException("The photo store path is not valid.");

        await using (var source = File.OpenRead(sourcePath))
        await using (var target = File.Create(targetPath))
        {
            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
        }

        var thumbnailId = await CreateThumbnailAsync(targetPath, cancellationToken).ConfigureAwait(false);

        return PhotoImportResult.Ok(new MealPhoto
        {
            Id = Guid.NewGuid(),
            MealEntryId = mealEntryId,
            OriginalFileName = Path.GetFileName(sourcePath),
            LocalFileId = localFileId,
            ThumbnailId = thumbnailId,
            MimeType = mimeType,
            FileSize = info.Length,
            ContentHash = contentHash,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    /// <inheritdoc />
    public async Task<string> ComputeContentHashAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        if (!File.Exists(sourcePath))
        {
            return string.Empty;
        }

        return await ComputeHashAsync(sourcePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public string? GetFullSizePath(string localFileId)
        => string.IsNullOrEmpty(localFileId) ? null : PathSanitizer.CombineInsideDirectory(_paths.PhotoDirectory, localFileId);

    /// <inheritdoc />
    public string? GetThumbnailPath(string thumbnailId)
        => string.IsNullOrEmpty(thumbnailId) ? null : PathSanitizer.CombineInsideDirectory(_paths.ThumbnailDirectory, thumbnailId);

    private async Task<string> CreateThumbnailAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var thumbnailId = PathSanitizer.CreateOpaqueFileName(".png");
        var targetPath = PathSanitizer.CombineInsideDirectory(_paths.ThumbnailDirectory, thumbnailId);
        if (targetPath is null)
        {
            return string.Empty;
        }

        var created = await ThumbnailGenerator
            .TryCreateAsync(sourcePath, targetPath, PhotoRules.ThumbnailSize, cancellationToken)
            .ConfigureAwait(false);

        return created ? thumbnailId : string.Empty;
    }

    private static async Task<byte[]> ReadHeaderAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var buffer = new byte[HeaderSize];
        var read = await stream.ReadAtLeastAsync(buffer, HeaderSize, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
        return buffer[..Math.Max(read, 0)];
    }

    private static async Task<string> ComputeHashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return await Hashing.Sha256Async(stream, cancellationToken).ConfigureAwait(false);
    }
}
