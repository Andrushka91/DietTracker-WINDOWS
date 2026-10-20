using System.Net;
using System.Net.Http.Headers;

namespace DietTracker.Infrastructure.Cloud.GoogleDrive;

/// <summary>Authenticated HTTP plumbing of the Google Drive provider.</summary>
public sealed partial class GoogleDriveStorageProvider
{
    private async Task<HttpResponseMessage> SendRawAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authentication.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            var body = await SafeReadAsync(response, cancellationToken).ConfigureAwait(false);
            response.Dispose();

            if (body.Contains("storageQuotaExceeded", StringComparison.OrdinalIgnoreCase))
            {
                throw new Core.Interfaces.CloudQuotaExceededException("The Google Drive storage quota is exhausted.");
            }

            throw new Core.Interfaces.CloudAuthorizationRequiredException(
                body.Contains("insufficientPermissions", StringComparison.OrdinalIgnoreCase)
                    ? "google.insufficient-permissions"
                    : "google.unauthorized");
        }

        if (response.StatusCode == (HttpStatusCode)429)
        {
            response.Dispose();
            throw new HttpRequestException("Google Drive rate limit reached; the request will be retried.", null, HttpStatusCode.TooManyRequests);
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var status = response.StatusCode;
            response.Dispose();
            throw new HttpRequestException($"Google Drive request failed with status {(int)status}.", null, status);
        }

        return response;
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return body.Length > 1000 ? body[..1000] : body;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }
}
