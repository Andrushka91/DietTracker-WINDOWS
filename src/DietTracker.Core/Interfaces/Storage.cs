using DietTracker.Core.Models;

namespace DietTracker.Core.Interfaces;

/// <summary>
/// Resolves the per-user, per-application data locations DietTracker is allowed to write to.
/// User data never lives inside the installation directory so that MSIX updates cannot erase it.
/// </summary>
public interface IAppDataPaths
{
    /// <summary>Root of the private application data folder (for example %LOCALAPPDATA%\Packages\&lt;family&gt;\LocalState).</summary>
    string RootDirectory { get; }

    /// <summary>Directory that holds the SQLite database file.</summary>
    string DatabaseDirectory { get; }

    /// <summary>Directory that holds full-size photographs.</summary>
    string PhotoDirectory { get; }

    /// <summary>Directory that holds generated thumbnails.</summary>
    string ThumbnailDirectory { get; }

    /// <summary>Directory for user initiated exports and backups.</summary>
    string BackupDirectory { get; }

    /// <summary>Directory for non-sensitive logs.</summary>
    string LogDirectory { get; }

    /// <summary>Directory that holds DPAPI protected secret files.</summary>
    string SecureDirectory { get; }

    /// <summary>Full path of the database file.</summary>
    string DatabaseFilePath { get; }

    /// <summary>Creates every directory that is missing. Safe to call repeatedly.</summary>
    void EnsureCreated();
}

/// <summary>Stores the image bytes of a photograph and produces thumbnails.</summary>
public interface IPhotoStore
{
    /// <summary>
    /// Imports a photograph from an arbitrary source path: validates it, copies it under a new
    /// opaque file name, generates a thumbnail and returns the metadata to persist.
    /// </summary>
    Task<PhotoImportResult> ImportAsync(Guid mealEntryId, string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// SHA-256 (lower-case hexadecimal) of the bytes of a source file, without copying anything into
    /// the store. Used to reject a photograph that is already attached before it is imported.
    /// </summary>
    Task<string> ComputeContentHashAsync(string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Absolute path of the full size image, or <see langword="null"/> when it is not present locally.</summary>
    string? GetFullSizePath(string localFileId);

    /// <summary>Absolute path of the thumbnail, or <see langword="null"/> when it is not present locally.</summary>
    string? GetThumbnailPath(string thumbnailId);

    /// <summary>Writes image bytes received from the cloud into the photo store (used during restore).</summary>
    Task<MealPhoto> StoreDownloadedAsync(Guid mealEntryId, string path, string mimeType, byte[] content, CancellationToken cancellationToken = default);

    /// <summary>
    /// Regenerates thumbnails that no longer match their source (damaged files, missing files) and
    /// returns how many were rewritten. The thumbnail file name is reused, so no database record
    /// changes. Healthy thumbnails are left untouched - only headers are decoded to compare them.
    /// </summary>
    Task<int> RepairThumbnailsAsync(IReadOnlyList<MealPhoto> photos, CancellationToken cancellationToken = default);

    /// <summary>Removes the local files of a photograph. Missing files are ignored.</summary>
    Task DeleteFilesAsync(MealPhoto photo, CancellationToken cancellationToken = default);

    /// <summary>Total number of bytes currently occupied by photographs and thumbnails.</summary>
    Task<long> GetStorageUsageAsync(CancellationToken cancellationToken = default);
}

/// <summary>Result of importing a photograph.</summary>
public sealed record PhotoImportResult
{
    public required bool Succeeded { get; init; }

    public MealPhoto? Photo { get; init; }

    /// <summary>Stable validation reason code when <see cref="Succeeded"/> is false.</summary>
    public string? ReasonCode { get; init; }

    public static PhotoImportResult Ok(MealPhoto photo) => new() { Succeeded = true, Photo = photo };

    public static PhotoImportResult Failed(string reasonCode) => new() { Succeeded = false, ReasonCode = reasonCode };
}

/// <summary>
/// Encrypts small secrets (tokens) with a Windows backed, current-user mechanism (DPAPI).
/// Implementations must never write plaintext secrets to disk.
/// </summary>
public interface ISecretProtector
{
    /// <summary>Protects the payload for the current Windows user on this machine.</summary>
    byte[] Protect(byte[] plaintext, string entropyContext);

    /// <summary>Unprotects a payload previously produced by <see cref="Protect"/>.</summary>
    byte[] Unprotect(byte[] protectedData, string entropyContext);
}

/// <summary>
/// Persists provider tokens in secure, Windows backed storage. Implementations store
/// MSAL / Google serialized caches or refresh tokens, protected per user.
/// </summary>
public interface ITokenStore
{
    Task SaveAsync(string key, string serializedTokens, CancellationToken cancellationToken = default);

    Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Removes every stored secret (used by "disconnect account" and "delete local data").</summary>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Abstracts the system clock so that time dependent rules stay testable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }

    /// <summary>Current instant in the user's configured time zone.</summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>Today's date in the user's configured time zone.</summary>
    DateOnly Today { get; }

    /// <summary>Current local time of day in the user's configured time zone.</summary>
    TimeOnly TimeOfDay { get; }

    string TimeZoneId { get; }

    TimeZoneInfo TimeZone { get; }
}
