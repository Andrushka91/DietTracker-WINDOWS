# Diet Tracker privacy policy (draft)

*Version 1.0 - applies to Diet Tracker for Windows (Microsoft Store edition and developer builds).*

## Summary

Diet Tracker stores your meal plan, your completion history and your photographs **on your own device**.
If you choose to sign in with a Google account, the same data is copied to a private
application folder inside **your own** cloud storage. There is no Diet Tracker server: the application
never sends your data to the vendor.

## 1. Who processes your data

Diet Tracker is a local Windows application. The developer of Diet Tracker does not operate any backend
service, does not receive your meal data and cannot read your cloud files.

Contact for privacy questions and data deletion requests: `privacy@diettracker.example`
(replace with the publisher's real address before submitting to the Store).

## 2. Which information is processed

**Stored locally (always):**

* meal plan content: titles, descriptions and planned times;
* completion state and completion timestamps (DONE / PENDING);
* attached photographs and generated thumbnails;
* meal and day templates, reminder configuration and card colours;
* application preferences (language, time zone, reminder and window behaviour);
* synchronization bookkeeping (account identity, cursors, revision numbers, device identifier);
* a diagnostic log file (`logs\diettracker.log`, capped at 1 MB and rotated once): technical messages only -
  never credentials, tokens, meal content or other personal data.

**Obtained from the identity provider (only after you sign in):**

* a stable account identifier, the account display name and the e-mail address - used to show which
  account is connected and to keep accounts apart.

**Never collected:** advertising identifiers, location, contacts, health records, biometric data,
other files on your device, or the content of your Google Drive outside the Diet Tracker
application folder.

## 3. What stays local and what is uploaded

| Data | Local | Uploaded when you sign in and enable synchronization |
|---|---|---|
| Meal content, templates | yes | yes |
| Completion state and timestamps | yes | yes |
| Photographs and thumbnails | yes | yes (original images; thumbnails are regenerated locally) |
| Reminder settings and card colours | yes | yes |
| Application preferences | yes | partially (language, time zone, reminder toggles) |
| Access/refresh tokens | DPAPI protected, per Windows user | never |
| Device identifier (random GUID) | yes | yes (used to detect echoes of your own uploads) |

Uploads only happen after you explicitly enable synchronization and accept the consent switch that is
shown before the first upload.

## 4. Where the data is stored in the cloud

* **Google account:** the hidden `appDataFolder` of your Google Drive, accessed with the scope
  `https://www.googleapis.com/auth/drive.appdata`.

The data is stored in **your** cloud storage account and is subject to that provider's terms and
privacy policy in addition to this document.

## 5. Purpose of synchronization

Synchronization lets you keep one meal plan across several Windows PCs and restore your records after
reinstalling the application or moving to another computer. It is optional: every feature except
cross-device synchronization works in Guest Mode.

## 6. Encryption

* All network traffic uses HTTPS/TLS to the provider endpoints.
* Credentials (the Google refresh token) are encrypted with **Windows DPAPI in the
  current-user scope** and stored in the private application data folder.
* Cloud backup contents are **not additionally encrypted by Diet Tracker**: they are protected by the
  provider's storage encryption and by the permissions of your account. This is not end-to-end
  encryption, and the policy deliberately does not claim it.

## 7. Your controls

In **Settings -> Privacy** and **Account and sync** you can, independently of each other:

* **Sign out** - removes the credentials from this device; your meals and photographs stay.
* **Disable synchronization** - keeps the authorization but stops uploading and downloading. Files
  already in the cloud are **not** deleted.
* **Disconnect the account** - forgets the authorization for an account.
* **Delete local data** - removes meals, templates, reminders and photographs from this device; a safety
  copy is created first and the cloud data is untouched.
* **Delete photographs** - removes only the local image files.
* **Delete cloud data** - deletes the Diet Tracker files in the connected account (requires a separate
  confirmation).
* **Export data / create local backup** - writes a `.dtbackup` archive (JSON plus photographs) that you
  can keep anywhere.

## 8. Retention

Data is retained until you delete it. Local data lives until you delete it, uninstall the application
(packaged installs remove the application's local data folder) or restore a backup over it. Cloud data
lives until you delete it in Diet Tracker or in the provider's own interface.

## 9. Children

Diet Tracker is a personal planning tool and is not directed at children. It does not contain
advertising, tracking or user generated content sharing.

## 10. Changes

Material changes to this policy are published with a new application version and are visible in the
Store listing. The version and date are stated at the top of this document.

## 11. Store listing

The same text (or a link to it) is provided in the Microsoft Store listing so that it is available
before installation.
