using DietTracker.Core.Enums;

namespace DietTracker.Core.Models;

/// <summary>
/// Photograph attached to a meal. Only metadata lives in SQLite; the bytes are stored as
/// ordinary files in the user's private application data folder.
/// </summary>
public sealed class MealPhoto
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid MealEntryId { get; set; }

    /// <summary>Original file name as chosen by the user (used for display and export only).</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>File name (not full path) of the full size copy inside the photo store.</summary>
    public string LocalFileId { get; set; } = string.Empty;

    /// <summary>File name (not full path) of the generated thumbnail inside the photo store.</summary>
    public string ThumbnailId { get; set; } = string.Empty;

    public string MimeType { get; set; } = "image/jpeg";

    public long FileSize { get; set; }

    /// <summary>SHA-256 of the image bytes; prevents duplicate uploads and detects corruption.</summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>Cloud object identifier assigned by the storage provider after upload.</summary>
    public string? CloudObjectId { get; set; }

    /// <summary>Cloud revision/ETag observed during the last successful upload.</summary>
    public string? CloudRevision { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendingUpload;
}
