using DietTracker.Core.Enums;

namespace DietTracker.Infrastructure.Cloud;

/// <summary>
/// Helpers for the logical paths that make up the synchronized dataset. Both providers address
/// objects by these logical paths, which keeps the synchronization engine provider agnostic.
/// </summary>
public static class CloudObjectPaths
{
    /// <summary>Prefix of the per-record documents.</summary>
    public const string EntityPrefix = "entities/";

    /// <summary>Prefix of the photograph binaries.</summary>
    public const string PhotoPrefix = "photos/";

    /// <summary>Path of the dataset manifest.</summary>
    public const string ManifestPath = "manifest.json";

    /// <summary>True when the logical path belongs to the DietTracker dataset.</summary>
    public static bool IsManaged(string path)
        => path.StartsWith(EntityPrefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(PhotoPrefix, StringComparison.OrdinalIgnoreCase)
           || path.Equals(ManifestPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Extracts the entity type from a logical path, or <see langword="null"/> when it is not an entity document.</summary>
    public static EntityType? GetEntityType(string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !segments[0].Equals("entities", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Enum.TryParse<EntityType>(segments[1], ignoreCase: true, out var type) ? type : null;
    }

    /// <summary>Builds the binary path of a photograph from its identifier.</summary>
    public static string GetPhotoBinaryPath(Guid photoId) => $"{PhotoPrefix}{photoId:D}.bin";

    /// <summary>Builds the metadata path of a photograph from its identifier.</summary>
    public static string GetPhotoMetadataPath(Guid photoId) => $"{EntityPrefix}MealPhoto/{photoId:D}.json";
}
