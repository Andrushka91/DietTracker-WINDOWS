namespace DietTracker.Core.BusinessRules;

/// <summary>Limits and validation rules for meal photographs.</summary>
public static class PhotoRules
{
    /// <summary>Maximum number of photographs attached to a single meal slot.</summary>
    public const int MaxPhotosPerMeal = 3;

    /// <summary>Maximum size of an original photograph: 10 MB.</summary>
    public const long MaxPhotoBytes = 10L * 1024 * 1024;

    /// <summary>Maximum width/height of a generated thumbnail.</summary>
    public const int ThumbnailSize = 256;

    /// <summary>JPEG quality used when the thumbnail is encoded.</summary>
    public const int ThumbnailQuality = 80;

    /// <summary>MIME types accepted by the file picker and by validation.</summary>
    public static IReadOnlyList<string> AllowedMimeTypes { get; } = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>File extensions accepted by the native file picker.</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } = [".jpg", ".jpeg", ".png", ".webp"];

    /// <summary>Maps a file extension to the canonical MIME type, or <see langword="null"/> when unsupported.</summary>
    public static string? MimeTypeFromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        _ => null,
    };

    /// <summary>
    /// Sniffs the file signature. A file whose bytes do not match the declared image format is
    /// rejected so that a renamed executable can never end up in the photo store.
    /// </summary>
    public static bool HasValidSignature(string mimeType, ReadOnlySpan<byte> header)
    {
        if (header.Length < 12)
        {
            return false;
        }

        return mimeType switch
        {
            "image/jpeg" => header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
            "image/png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
                           && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A,
            "image/webp" => header[0] == (byte)'R' && header[1] == (byte)'I' && header[2] == (byte)'F' && header[3] == (byte)'F'
                            && header[8] == (byte)'W' && header[9] == (byte)'E' && header[10] == (byte)'B' && header[11] == (byte)'P',
            _ => false,
        };
    }

    /// <summary>Checks whether another photograph may be added to the meal.</summary>
    public static bool CanAddPhoto(int currentPhotoCount) => currentPhotoCount < MaxPhotosPerMeal;
}
