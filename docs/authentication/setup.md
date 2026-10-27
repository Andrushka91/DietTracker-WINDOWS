# Identity provider setup

Diet Tracker synchronizes with **Google Drive** and is a **public client**: no secret is embedded in
the application or the repository. The Google client identifier and client secret are read at
runtime from an optional settings file, so a developer or an organization can point a build at its
own registration without recompiling:

```json
// %LOCALAPPDATA%\DietTracker\appsettings.json
{
  "googleClientId": "<desktop app client id>.apps.googleusercontent.com",
  "googleClientSecret": "<desktop app client secret>"
}
```

Without this file the built-in placeholders are used and interactive sign-in cannot succeed - Guest
Mode remains fully functional. Keys are matched case-insensitively and `//` comments are allowed;
restart Diet Tracker after changing the file.

## Google account (OAuth 2.0 + PKCE)

1. Create a project in the [Google Cloud Console](https://console.cloud.google.com) and enable the
   **Google Drive API**.
2. Configure the OAuth consent screen (External, testing or published). While the consent screen is
   in *Testing* mode, add your Google account under **Test users** - otherwise Google blocks the
   consent page even with a valid client. Publishing the app removes that restriction.
3. Create an OAuth client identifier of type **Desktop app**.
4. Loopback redirects (`http://127.0.0.1` with a port chosen at runtime) are permitted automatically
   for Desktop app clients; Google does not offer (or require) registering a redirect URI for them.
5. Copy the client identifier into `googleClientId` and the client secret into `googleClientSecret`.
   The secret of a Desktop app client is not treated as confidential (Google's token endpoint expects
   it although the application cannot keep it), but it stays out of the repository: it lives in the
   per-user settings file only. Restart Diet Tracker after changing the file.

Scopes requested (minimal):

```text
openid
email
profile
https://www.googleapis.com/auth/drive.appdata
```

`drive.appdata` grants access only to the hidden *application data folder* of Google Drive. Diet Tracker
never sees the user's other files.

**Flow details:** the authorization page is opened in the **system browser** (never in an embedded
WebView), the redirect is received on a loopback listener bound to `127.0.0.1`, the `state` parameter is
verified, and the code is exchanged with the PKCE verifier (`S256`) together with the client secret.
The refresh token is stored DPAPI-protected; access tokens are refreshed five minutes before expiry.
If Google revokes or expires the refresh token the user is asked to authorize again - no local record
is deleted.

## Guest mode and provider switching

* Guest mode is a first class profile (`ProviderType.None`): signing in later never migrates or moves
  data.
* Switching provider (Microsoft <-> Google) always shows the current account, the target account, the
  number of local records and the pending changes, then asks the user to choose: upload local data,
  restore from the new account, merge with conflict handling, or cancel. Accounts are matched by
  provider + provider user id, never by e-mail address.
