using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace DietTracker.Infrastructure.Cloud.OneDrive;

/// <summary>Authenticated HTTP plumbing shared by every OneDrive operation.</summary>
public sealed partial class OneDriveStorageProvider
{
    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string endpoint,
        object? content,
        CancellationToken cancellationToken,
        string? optimisticConcurrency = null)
    {
        var uri = endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? endpoint : GraphBase + endpoint;
        using var request = new HttpRequestMessage(method, uri);

        // Optimistic concurrency: the upload only succeeds if the cloud object is still the revision
        // this device last saw. A mismatch surfaces as a conflict instead of silently overwriting.
        if (!string.IsNullOrEmpty(optimisticConcurrency))
        {
            request.Headers.TryAddWithoutValidation("If-Match", optimisticConcurrency);
        }

        if (content is byte[] bytes)
        {
            request.Content = new ByteArrayContent(bytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        }
        else if (content is Dictionary<string, object> dictionary)
        {
            request.Content = new StringContent(JsonSerializer.Serialize(dictionary), Encoding.UTF8, "application/json");
        }

        return await SendRawAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authentication.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new Core.Interfaces.CloudAuthorizationRequiredException("graph.unauthorized");
        }

        if (response.StatusCode == HttpStatusCode.InsufficientStorage)
        {
            response.Dispose();
            throw new Core.Interfaces.CloudQuotaExceededException("The OneDrive storage quota is exhausted.");
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Microsoft Graph request failed with status {(int)status}{Describe(status)}.", null, status);
        }

        return response;
    }

    private static string Describe(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => " (rate limited; the request will be retried)",
        HttpStatusCode.Conflict => " (the cloud object changed on another device)",
        HttpStatusCode.PreconditionFailed => " (the cloud object changed on another device)",
        HttpStatusCode.RequestEntityTooLarge => " (the object is too large for a simple upload)",
        _ => string.Empty,
    };
}
