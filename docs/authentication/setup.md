# Identity provider setup

DietTracker is a **public client**: no client secret is ever embedded in the application or the
repository. Client identifiers are read at runtime from an optional settings file, so a developer or
an organization can point a build at its own registration without recompiling:

```json
// %LOCALAPPDATA%\DietTracker\appsettings.json
{
  "microsoftClientId": "<application (client) id>",
  "microsoftAuthority": "https://login.microsoftonline.com/consumers",
  "googleClientId": "<desktop app client id>.apps.googleusercontent.com"
}
```

Without this file the built-in placeholders are used and interactive sign-in cannot succeed - Guest
Mode remains fully functional.

## Microsoft account (MSAL.NET + Windows Account Manager)

1. Open the [Microsoft Entra admin center](https://entra.microsoft.com) or the Azure portal and create
   an **App registration**.
2. Supported account types: *Accounts in any organizational directory and personal Microsoft accounts*
   (personal accounts are the primary target).
3. Add a **Mobile and desktop application** platform and enable the redirect URI that MSAL uses for the
   Windows broker (`ms-appx-web://Microsoft.AAD.BrokerPlugin/<client id>`) plus
   `http://localhost` for the browser fallback.
4. API permissions (delegated):
   * `Files.ReadWrite.AppFolder` - write access restricted to the OneDrive *application folder* of the
     signed-in user. The rest of the drive is inaccessible.
   * `User.Read` - the account identity shown in the Account & Sync screen.
5. Grant consent if your tenant requires an administrator.
6. Put the application (client) identifier into `microsoftClientId`.

**Account types and permissions:** `Files.ReadWrite.AppFolder` is a delegated personal-account scope.
Work/school accounts can sign in when the organization allows the scope and the tenant policy permits
user consent; the application reports the failure instead of silently degrading.

**Silent sign-in:** the serialized MSAL token cache is stored DPAPI-protected in the per-user secure
folder. On startup `AcquireTokenSilent` is attempted first; only if the provider requires interaction is
the user asked to sign in again, and local data is never touched in that case.

## Google account (OAuth 2.0 + PKCE)

1. Create a project in the [Google Cloud Console](https://console.cloud.google.com) and enable the
   **Google Drive API**.
2. Configure the OAuth consent screen (External, testing or published).
3. Create an OAuth client identifier of type **Desktop app**.
4. Add `http://127.0.0.1` as an authorized redirect origin (the loopback port is chosen at runtime).
5. Put the client identifier into `googleClientId`.

Scopes requested (minimal):

```text
openid
email
profile
https://www.googleapis.com/auth/drive.appdata
```

`drive.appdata` grants access only to the hidden *application data folder* of Google Drive. DietTracker
never sees the user's other files.

**Flow details:** the authorization page is opened in the **system browser** (never in an embedded
WebView), the redirect is received on a loopback listener bound to `127.0.0.1`, the `state` parameter is
verified, and the code is exchanged with the PKCE verifier (`S256`). The refresh token is stored
DPAPI-protected; access tokens are refreshed five minutes before expiry. If Google revokes or expires
the refresh token the user is asked to authorize again - no local record is deleted.

## Guest mode and provider switching

* Guest mode is a first class profile (`ProviderType.None`): signing in later never migrates or moves
  data.
* Switching provider (Microsoft <-> Google) always shows the current account, the target account, the
  number of local records and the pending changes, then asks the user to choose: upload local data,
  restore from the new account, merge with conflict handling, or cancel. Accounts are matched by
  provider + provider user id, never by e-mail address.
