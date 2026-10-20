using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;

namespace DietTracker.Infrastructure.Cloud.OneDrive;

/// <summary>
/// OneDrive storage provider that keeps every DietTracker object inside the application specific
/// folder (<c>/me/drive/special/approot</c>) of the signed-in Microsoft account.
/// </summary>
/// <remarks>
/// The least privileged delegated permission that can write to the application folder is requested
/// (<c>Files.ReadWrite.AppFolder</c>, see <c>MicrosoftAuthenticationOptions</c>), so DietTracker
/// never gains access to the rest of the user's OneDrive.
/// </remarks>
public sealed partial class OneDriveStorageProvider : ICloudStorageProvider
{
    private const string GraphBase = "https://graph.microsoft.com/v1.0";
    private const int SimpleUploadLimitBytes = 4 * 1024 * 1024;

    private readonly IAuthenticationProvider _authentication;
    private readonly HttpClient _httpClient;
    private readonly ILogger<OneDriveStorageProvider> _logger;

    private bool _initialized;

    public OneDriveStorageProvider(
        IAuthenticationProvider authentication,
        HttpClient httpClient,
        ILogger<OneDriveStorageProvider> logger)
    {
        _authentication = authentication;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public ProviderType ProviderType => ProviderType.Microsoft;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return;
        }

        // Touch the app folder so that a missing folder / missing permission surfaces during startup
        // instead of the first upload.
        await SendAsync(HttpMethod.Get, "/me/drive/special/approot?$select=id,name", content: null, cancellationToken).ConfigureAwait(false);

        foreach (var folder in new[] { "entities", "entities/MealEntry", "entities/MealPhoto", "entities/MealTemplate", "entities/ReminderSettings", "entities/AppPreferences", "photos" })
        {
            await EnsureFolderAsync(folder, cancellationToken).ConfigureAwait(false);
        }

        _initialized = true;
    }

    private async Task EnsureFolderAsync(string relativePath, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(HttpMethod.Get, $"/me/drive/special/approot:/{relativePath}", content: null, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var parent = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
            var name = Path.GetFileName(relativePath);
            var endpoint = string.IsNullOrEmpty(parent)
                ? "/me/drive/special/approot/children"
                : $"/me/drive/special/approot:/{parent}:/children";

            var payload = new Dictionary<string, object>
            {
                ["name"] = name,
                ["folder"] = new Dictionary<string, object>(),
                ["@microsoft.graph.conflictBehavior"] = "fail",
            };

            await SendAsync(HttpMethod.Post, endpoint, payload, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CloudObject>> ListAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        var results = new List<CloudObject>();
        if (string.IsNullOrEmpty(pathPrefix))
        {
            return results;
        }

        var prefix = pathPrefix.TrimEnd('/');
        var endpoint =
            $"/me/drive/special/approot:/{prefix}:/children?$select=id,name,size,eTag,cTag,lastModifiedDateTime,file,folder&$top=200";

        while (!string.IsNullOrEmpty(endpoint))
        {
            using var response = await SendAsync(HttpMethod.Get, endpoint, content: null, cancellationToken).ConfigureAwait(false);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));

            if (document.RootElement.TryGetProperty("value", out var items))
            {
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("folder", out _))
                    {
                        continue;
                    }

                    results.Add(new CloudObject
                    {
                        ObjectId = $"{prefix}/{item.GetProperty("name").GetString()}",
                        Path = $"{prefix}/{item.GetProperty("name").GetString()}",
                        Revision = GetRevision(item),
                        ModifiedAt = GetModified(item),
                        SizeBytes = item.TryGetProperty("size", out var size) ? size.GetInt64() : 0,
                    });
                }
            }

            endpoint = document.RootElement.TryGetProperty("@odata.nextLink", out var next) ? next.GetString() : null;

            // Absolute next links are not accepted by the relative helper, so only relative ones continue.
            if (endpoint is not null && endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                endpoint = null;
            }
        }

        return results;
    }

    private static DateTimeOffset GetModified(JsonElement item)
        => item.TryGetProperty("lastModifiedDateTime", out var modified)
           && DateTimeOffset.TryParse(modified.GetString(), out var value)
            ? value
            : DateTimeOffset.UtcNow;

    private static string? GetRevision(JsonElement item)
        => item.TryGetProperty("eTag", out var eTag) ? eTag.GetString() : null;
}
