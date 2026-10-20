using System.Security.Cryptography;
using System.Text;

namespace DietTracker.Core.Utilities;

/// <summary>Hashing helpers used for content based deduplication and integrity verification.</summary>
public static class Hashing
{
    /// <summary>SHA-256 of a UTF-8 string, lower-case hexadecimal.</summary>
    public static string Sha256(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Sha256(Encoding.UTF8.GetBytes(value));
    }

    /// <summary>SHA-256 of raw bytes, lower-case hexadecimal.</summary>
    public static string Sha256(ReadOnlySpan<byte> value)
        => Convert.ToHexStringLower(SHA256.HashData(value));

    /// <summary>SHA-256 of a stream, lower-case hexadecimal.</summary>
    public static async Task<string> Sha256Async(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }
}
