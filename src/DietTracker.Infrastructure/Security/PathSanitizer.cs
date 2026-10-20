using System.Security.Cryptography;
using System.Text;

namespace DietTracker.Infrastructure.Security;

/// <summary>
/// Defensive helpers for file names and paths. Everything that reaches the file system from user
/// input, a provider or a backup archive passes through here, which prevents directory traversal
/// and illegal characters.
/// </summary>
public static class PathSanitizer
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>Turns an arbitrary string into a safe single-segment file name.</summary>
    public static string ToSafeFileName(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(Array.IndexOf(Invalid, ch) >= 0 || ch is '/' or '\\' or ':' ? '_' : ch);
        }

        var result = builder.ToString().Trim().Trim('.');
        if (string.IsNullOrEmpty(result))
        {
            result = "unnamed";
        }

        // Keep file names well below the Windows path segment limit.
        return result.Length > 100 ? result[..100] : result;
    }

    /// <summary>Creates an opaque, collision free file name with the supplied extension.</summary>
    public static string CreateOpaqueFileName(string extension)
    {
        var ext = extension.StartsWith('.') ? extension : "." + extension;
        return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)) + ext.ToLowerInvariant();
    }

    /// <summary>
    /// Combines a directory with a single file name and verifies that the result stays inside the
    /// directory. Returns <see langword="null"/> when the path would escape (traversal attempt).
    /// </summary>
    public static string? CombineInsideDirectory(string directory, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (fileName.IndexOfAny(Invalid) >= 0 || fileName.Contains('/') || fileName.Contains('\\') || fileName.Contains(".."))
        {
            return null;
        }

        var fullDirectory = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(fullDirectory, fileName));

        var comparison = StringComparison.OrdinalIgnoreCase;
        return candidate.StartsWith(fullDirectory + Path.DirectorySeparatorChar, comparison) ? candidate : null;
    }

    /// <summary>True when the extension is a supported image extension (case insensitive).</summary>
    public static bool HasAllowedImageExtension(string path)
        => Core.BusinessRules.PhotoRules.AllowedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
}
