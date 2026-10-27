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
        if (options.ClientId.StartsWith("00000000", StringComparison.Ordinal))
        {
            // The built-in placeholder is not a real Google registration: opening the browser would
            // only show Google's "The OAuth client was not found" (401 invalid_client) page, so the
            // missing configuration is reported locally instead (see docs/authentication/setup.md).
            throw new CloudAuthorizationRequiredException("google.not-configured");
        }

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

        // The system browser opens the Google page; Diet Tracker never renders the login form itself.
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
        var html = BuildResponsePage(success);
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

    /// <summary>
    /// Builds the page the browser lands on after the authorization redirect (RFC 8252 section 4.4
    /// recommends that the local server responds with a user friendly instruction page). The page
    /// follows the system light/dark preference like the application itself, and tries to close the
    /// tab automatically; browsers refuse closing for tabs they did not open, so a clear instruction
    /// remains visible as the fallback.
    /// </summary>
    private static string BuildResponsePage(bool success)
    {
        var title = success ? "You're connected" : "Sign-in was not completed";
        var message = success
            ? "Diet Tracker is now connected to your Google account. You can return to the application."
            : "The authorization did not finish. You can close this tab and try again in Diet Tracker.";

        // A filled circle with a check (success) or a cross (failure), drawn inline - the page must
        // not depend on any external resource.
        var badge = success
            ? "<div class='badge ok'><svg viewBox='0 0 24 24' fill='none' stroke='#ffffff' stroke-width='3' stroke-linecap='round' stroke-linejoin='round'><path d='M20 6L9 17l-5-5'/></svg></div>"
            : "<div class='badge failed'><svg viewBox='0 0 24 24' fill='none' stroke='#ffffff' stroke-width='3' stroke-linecap='round' stroke-linejoin='round'><path d='M18 6L6 18M6 6l12 12'/></svg></div>";

        return """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Diet Tracker</title>
<style>
  :root { color-scheme: light dark; }
  * { margin: 0; padding: 0; box-sizing: border-box; }
  body {
    font-family: "Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif;
    min-height: 100vh; display: flex; align-items: center; justify-content: center;
    background: #f3f3f3; color: #1a1a1a; padding: 24px;
  }
  .card {
    max-width: 420px; width: 100%; text-align: center;
    background: #ffffff; border: 1px solid #e4e4e4; border-radius: 12px;
    padding: 40px 32px 32px; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
  }
  .brand { display: flex; align-items: center; justify-content: center; gap: 8px;
           font-size: 13px; font-weight: 600; letter-spacing: 0.4px; margin-bottom: 28px; }
  .brand .dot { width: 10px; height: 10px; border-radius: 50%; background: #0f7b3f; }
  .badge { width: 64px; height: 64px; border-radius: 50%; margin: 0 auto 20px;
           display: flex; align-items: center; justify-content: center; }
  .badge.ok { background: #0f7b3f; }
  .badge.failed { background: #c4382d; }
  .badge svg { width: 30px; height: 30px; }
  h1 { font-size: 20px; font-weight: 600; margin-bottom: 10px; }
  p { font-size: 14px; line-height: 1.55; opacity: 0.78; }
  .hint { margin-top: 26px; font-size: 12px; opacity: 0.45; }
  @media (prefers-color-scheme: dark) {
    body { background: #1c1c1c; color: #f3f3f3; }
    .card { background: #2b2b2b; border-color: #3a3a3a; box-shadow: 0 8px 24px rgba(0, 0, 0, 0.35); }
  }
</style>
</head>
<body>
  <main class="card">
    <div class="brand"><span class="dot"></span>Diet Tracker</div>
    BADGE_PLACEHOLDER
    <h1>TITLE_PLACEHOLDER</h1>
    <p>MESSAGE_PLACEHOLDER</p>
    <p class="hint">This tab closes by itself in a moment. If it stays open, close it and return to Diet Tracker.</p>
  </main>
  <script>setTimeout(function () { window.close(); }, 2500);</script>
</body>
</html>
"""
            .Replace("BADGE_PLACEHOLDER", badge)
            .Replace("TITLE_PLACEHOLDER", title)
            .Replace("MESSAGE_PLACEHOLDER", message);
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
