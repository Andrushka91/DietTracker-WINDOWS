using DietTracker.Core.Enums;
using DietTracker.Core.Models;

namespace DietTracker.App.ViewModels;

/// <summary>A photograph attached to a meal, together with its local file paths.</summary>
public sealed class MealPhotoViewModel
{
    public MealPhotoViewModel(MealPhoto photo, string? thumbnailPath, string? fullSizePath, string? localPath)
    {
        Photo = photo;
        ThumbnailPath = thumbnailPath;
        FullSizePath = fullSizePath;
        LocalPath = localPath;
    }

    public MealPhoto Photo { get; }

    public Guid Id => Photo.Id;

    public string OriginalFileName => Photo.OriginalFileName;

    /// <summary>Absolute path of the thumbnail used by the card (null when generation failed).</summary>
    public string? ThumbnailPath { get; }

    public string? FullSizePath { get; }

    public string? LocalPath { get; }

    public string SizeText => $"{Photo.FileSize / 1024d:0.#} KB";

    public SyncStatus SyncStatus => Photo.SyncStatus;
}
