using System.Security.Cryptography;
using System.Text;
using DietTracker.Core.Interfaces;
using DietTracker.Infrastructure.FileStorage;
using Microsoft.Extensions.Logging;

namespace DietTracker.Infrastructure.Security;

/// <summary>
/// Stores serialized token caches (MSAL cache, Google refresh tokens) as DPAPI protected files in
/// the per-user secure directory. Plaintext tokens are never written to disk, to the database or
/// to logs.
/// </summary>
public sealed class FileTokenStore : ITokenStore
{
    private readonly IAppDataPaths _paths;
    private readonly ISecretProtector _protector;
    private readonly ILogger<FileTokenStore> _logger;

    public FileTokenStore(IAppDataPaths paths, ISecretProtector protector, ILogger<FileTokenStore> logger)
    {
        _paths = paths;
        _protector = protector;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(string key, string serializedTokens, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(serializedTokens);

        _paths.EnsureCreated();
        var path = GetPath(key);
        var protectedBytes = _protector.Protect(System.Text.Encoding.UTF8.GetBytes(serializedTokens), key);

        // Write to a temporary file first so that an interrupted write cannot corrupt the token store.
        var tempPath = path + ".tmp";
        await File.WriteAllBytesAsync(tempPath, protectedBytes, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <inheritdoc />
    public async Task<string?> LoadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = GetPath(key);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var protectedBytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var plaintext = _protector.Unprotect(protectedBytes, key);
            return System.Text.Encoding.UTF8.GetString(plaintext);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // A token that cannot be decrypted (different Windows user, corrupted file) is treated as
            // "no session": the user is asked to sign in again and no local meal data is touched.
            _logger.LogWarning("Stored credentials for '{Key}' could not be read ({Reason}).", key, ex.GetType().Name);
            return null;
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var path = GetPath(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(GetPath(key)));

    /// <inheritdoc />
    public Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(_paths.SecureDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(_paths.SecureDirectory, "*.bin"))
            {
                File.Delete(file);
            }
        }

        return Task.CompletedTask;
    }

    private string GetPath(string key)
    {
        var safe = PathSanitizer.ToSafeFileName(key);
        return Path.Combine(_paths.SecureDirectory, safe + ".bin");
    }
}
