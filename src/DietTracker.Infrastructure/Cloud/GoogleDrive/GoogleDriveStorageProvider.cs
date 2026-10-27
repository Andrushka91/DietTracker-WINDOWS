using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.GoogleDrive;

/// <summary>
/// Google Drive storage provider that keeps every Diet Tracker object inside the hidden
/// <c>appDataFolder</c> of the signed-in Google account.
/// </summary>
/// <remarks>
/// Files are stored with flat names (<c>entities__MealEntry__&lt;id&gt;.json</c>) and mapped back to
/// logical paths, because the application data folder is a flat space. Nothing outside
/// <c>appDataFolder</c> is ever read or written.
/// </remarks>
public sealed partial class GoogleDriveStorageProvider : ICloudStorageProvider
{
    /// <summary>Separator used to encode the logical path in a flat Google Drive file name.</summary>
    internal const string NameSeparator = "__";

    internal const string DriveBase = "https://www.googleapis.com/drive/v3";
    internal const string UploadBase = "https://www.googleapis.com/upload/drive/v3";

    private readonly IAuthenticationProvider _authentication;
    private readonly HttpClient _httpClient;
    private readonly ILogger<GoogleDriveStorageProvider> _logger;

    private readonly Dictionary<string, DriveFile> _fileIndex = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public GoogleDriveStorageProvider(
        IAuthenticationProvider authentication,
        HttpClient httpClient,
        ILogger<GoogleDriveStorageProvider> logger)
    {
        _authentication = authentication;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public ProviderType ProviderType => ProviderType.Google;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        await RefreshIndexAsync(cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    /// <summary>Re-reads the file list of the application data folder.</summary>
    private async Task RefreshIndexAsync(CancellationToken cancellationToken)
    {
        _fileIndex.Clear();

        var files = await ListFilesAsync("name,id,version,modifiedTime,size,md5Checksum", q: null, cancellationToken).ConfigureAwait(false);
        foreach (var file in files)
        {
            _fileIndex[file.Name] = file;
        }
    }

    private async Task<List<DriveFile>> ListFilesAsync(string fields, string? q, CancellationToken cancellationToken)
    {
        var results = new List<DriveFile>();
        string? pageToken = null;

        do
        {
            var url = $"{DriveBase}/files?spaces=appDataFolder&pageSize=1000&fields=nextPageToken,files({fields})";
            if (!string.IsNullOrEmpty(q))
            {
                url += $"&q={Uri.EscapeDataString(q)}";
            }

            if (!string.IsNullOrEmpty(pageToken))
            {
                url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
            }

            using var response = await SendRawAsync(new HttpRequestMessage(HttpMethod.Get, url), cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (document.RootElement.TryGetProperty("files", out var files))
            {
                foreach (var element in files.EnumerateArray())
                {
                    results.Add(ParseFile(element));
                }
            }

            pageToken = document.RootElement.TryGetProperty("nextPageToken", out var token) ? token.GetString() : null;
        }
        while (!string.IsNullOrEmpty(pageToken));

        return results;
    }

    internal static DriveFile ParseFile(JsonElement element) => new(
        Id: element.TryGetProperty("id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
        Name: element.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
        Version: element.TryGetProperty("version", out var version) ? version.GetRawText().Trim('"') : null,
        ModifiedAt: element.TryGetProperty("modifiedTime", out var modified) && DateTimeOffset.TryParse(modified.GetString(), out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow,
        SizeBytes: element.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var length) ? length : 0,
        Hash: element.TryGetProperty("md5Checksum", out var hash) ? hash.GetString() : null);

    /// <summary>Encodes a logical path (with slashes) as a flat Google Drive file name.</summary>
    internal static string ToDriveName(string logicalPath) => logicalPath.Replace("/", NameSeparator, StringComparison.Ordinal);

    /// <summary>Decodes a flat Google Drive file name back into the logical path.</summary>
    internal static string ToLogicalPath(string driveName) => driveName.Replace(NameSeparator, "/", StringComparison.Ordinal);

    /// <summary>File metadata as returned by the Google Drive API.</summary>
    internal sealed record DriveFile(string Id, string Name, string? Version, DateTimeOffset ModifiedAt, long SizeBytes, string? Hash);
}
