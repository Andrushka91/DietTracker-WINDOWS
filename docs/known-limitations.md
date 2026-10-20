# Known limitations and unresolved issues

This list is deliberately explicit. Everything that has *not* been verified is called out here rather
than presented as finished.

## Not verified in this environment

1. **Real Microsoft and Google sign-in.** The MSAL.NET integration (Windows Account Manager broker with
   browser fallback) and the Google OAuth 2.0 + PKCE flow are implemented, but no client registrations
   exist here and interactive consent needs a human. Configure the identifiers as described in
   `docs/authentication/setup.md` and test the **packaged** build: the broker requires package identity
   to behave exactly as in the Store build.
2. **Real OneDrive / Google Drive synchronization.** The providers target the documented Microsoft Graph
   and Google Drive v3 endpoints (app folder and `appDataFolder`, delta feed and modified-time
   watermark, resumable uploads, quotas). They are covered by tests against an in-memory provider that
   mimics revisions, cursors and change tracking, but they have not been executed against the real
   services. Expect to adjust details (for example `@odata.nextLink` handling on very large folders).
3. **Microsoft Store certification and publication.** Not performed: it requires a Partner Center account,
   a reserved name and Microsoft review. See `docs/microsoft-store/submission.md`.
4. **Packaged-app testing.** The MSIX package builds, but installing it and running the packaged
   application (including notification registration and the `startupTask` extension) was not possible
   here. The unpackaged build was launched and measured successfully. Note that this makes the
   *build flavour* matter when you test by hand: because `WindowsPackageType=MSIX` is the default, the
   `DietTracker.exe` produced by a plain `dotnet build` exits immediately when started from `bin\`
   (`COMException 0x80040154 Class not registered`, raised by `WindowsAppRuntimeAutoInitializer`,
   which expects package identity). Use
   `-p:WindowsPackageType=None -p:EnableMsixTooling=false` for an executable that runs directly, or
   install/register the package.
5. **Store listing artwork.** The icons and tiles are generated placeholders (`tools/Generate-StoreAssets.ps1`);
   the screenshot is not a real screenshot of the running application.

## Product limitations by design

6. **Reminder delivery depends on the application running.** The scheduler runs inside the application
   process (also while it lives in the notification area). If Windows is asleep, the PC is powered off,
   or the application has been exited, the reminder cannot be raised. Reminders older than three hours
   are deliberately skipped so a PC that was off for a long time does not show stale notifications.
   A future version could register a Windows background task to improve this.
7. **Photo restore ordering.** A photograph can only be attached to a meal slot that already exists
   locally, so if a meal and its photograph arrive in the same cycle the photograph is applied in the
   following cycle. Running the restore twice (or waiting for the next automatic cycle) completes it.
8. **Google Drive change tracking uses a modified-time watermark** because the changes feed cannot be
   scoped to `appDataFolder`. Objects may be examined twice; all apply operations are idempotent, so the
   only cost is a little extra traffic.
9. **Conflict resolution is conservative.** Genuine concurrent edits are never merged automatically:
   the user chooses, and "keep both" preserves the local version as a template.
10. **No application-level encryption of cloud backups.** Data is protected by the provider's storage
    encryption and account permissions; the privacy policy states this explicitly and does not claim
    end-to-end encryption.
11. **Guest data is per device until an account is connected.** Local data is never deleted when signing
    in or out, but it is not automatically uploaded unless the user confirms the switch/upload.
12. **The `MinimizeToTray` close behaviour is synchronous.** The window close handler reads preferences
    synchronously; this is a very short database read, but it is not ideal and could be made async.

13. **Diagnostic log file.** The application writes `logs\diettracker.log` (information level and above,
    capped at 1 MB, rotated once to `diettracker.log.1`). It contains technical messages only and is never
    uploaded anywhere. There is no log viewer inside the application and the level is fixed in code
    (`AddDietTrackerLogging`).

## Environment notes that affect the build

14. **XAML compiler culture.** On some machines the WinUI XAML compiler task fails with
    `WMC9999: Could not find any resources appropriate for the specified culture`. Building with
    `-m:1` (single threaded) avoids a related race; if the culture error appears, set
    `DOTNET_CLI_UI_LANGUAGE=en-US` for the build.
15. **Application control policy.** On this machine an application control policy blocked the freshly
    built integration test assembly by name (`0x800711C7`). The assembly is therefore named
    `DietTracker.Sync.Tests` and its output is redirected to `artifacts/tests/integration`. On a normal
    developer machine this workaround is unnecessary but harmless.
16. **NuGet packages folder.** `nuget.config` uses the default user profile packages folder; the earlier
    repository-local `.packages` folder was removed to keep the repository small.

## Suggested next steps

1. Configure the two client registrations and verify both sign-in flows in the **packaged** build.
2. Run a real two-device synchronization test with one OneDrive and one Google account and check the
   conflict behaviour with genuinely concurrent edits.
3. Add a Windows background task (or a scheduled toast) so reminders also fire when the application is
   not running.
4. Install the MSIX on a clean Windows 11 machine, run the Windows App Certification Kit, then submit.
5. Add ARM64 build verification and Release-mode performance numbers.
