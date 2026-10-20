using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using DietTracker.Core.Interfaces;

namespace DietTracker.Infrastructure.Authentication.Google;

/// <summary>
/// Authorization Code flow with PKCE for a native Windows desktop client.
/// </summary>
/// <remarks>
/// The authorization page is always shown by the external system browser (never inside an embedded
/// WebView) and the redirect is received on a loopback listener bound to 127.0.0.1 - the flow Google
/// requires for desktop applications. No client secret is used: the code verifier proves that the
/// authorization code is redeemed by this very process.
/// </remarks>
internal static partial class GooglePkceFlow
{
    /// <summary>Runs the interactive flow and returns the resulting tokens.</summary>
    public static async Task<GoogleTokenSet?> RunAsync(
        GoogleAuthenticationOptions options,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var verifier = CreateCodeVerifier();
        var challenge = CreateCodeChallenge(verifier);
        var state = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var port = GetFreeLoopbackPort();
        var redirectUri = $"http://127.0.0.1:{port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirectUri);

        try
        {
            listener.Start();
        }
        catch (HttpListenerException)
        {
            throw new CloudAuthorizationRequiredException("google.loopback.unavailable");
        }

        var authorizeUrl = BuildAuthorizeUrl(options, redirectUri, challenge, state);

        // The system browser opens the Google page; DietTracker never renders the login form itself.
        Process.Start(new ProcessStartInfo(authorizeUrl) { UseShellExecute = true });

        HttpListenerContext context;
        using (var registration = cancellationToken.Register(listener.Stop))
        {
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (HttpListenerException)
            {
                throw new CloudAuthorizationRequiredException("google.authorization-cancelled");
            }
        }

        var query = context.Request.QueryString;
        var code = query["code"];
        var returnedState = query["state"];
        var error = query["error"];

        await WriteBrowserResponseAsync(context, error is null).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(error))
        {
            throw new CloudAuthorizationRequiredException($"google.{error}");
        }

        if (!string.Equals(returnedState, state, StringComparison.Ordinal))
        {
            // State mismatch: a possible cross-site request forgery attempt, so the code is discarded.
            throw new CloudAuthorizationRequiredException("google.state-mismatch");
        }

        return string.IsNullOrEmpty(code)
            ? null
            : await ExchangeCodeAsync(options, httpClient, code, verifier, redirectUri, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildAuthorizeUrl(GoogleAuthenticationOptions options, string redirectUri, string challenge, string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = options.ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', options.Scopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent",
        };

        return options.AuthorizationEndpoint
               + "?"
               + string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static async Task WriteBrowserResponseAsync(HttpListenerContext context, bool success)
    {
        var message = success
            ? "DietTracker is connected. You can close this tab and return to the application."
            : "Authorization was not completed. You can close this tab and try again in DietTracker.";

        var html = "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>DietTracker</title></head><body>"
                   + "<h1>DietTracker</h1><p>" + message + "</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);

        try
        {
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static string CreateCodeVerifier() => Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    private static string CreateCodeChallenge(string verifier)
        => Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static int GetFreeLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
