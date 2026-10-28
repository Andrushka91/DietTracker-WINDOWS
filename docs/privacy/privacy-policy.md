# Diet Tracker privacy policy (draft)

*Version 1.0 - applies to Diet Tracker for Windows (Microsoft Store edition and developer builds).*

## Summary

Diet Tracker keeps your meal plan, your completion history and your photographs in a private
application folder inside **your own** cloud storage when you sign in. While you are signed in, the
device holds a working copy so the interface works offline; signing out uploads what is still pending
and then deletes that copy. There is no Diet Tracker server: the application never sends your data to
the vendor.

## 1. Who processes your data

Diet Tracker is a local Windows application. The developer of Diet Tracker does not operate any backend
service, does not receive your meal data and cannot read your cloud files.

Contact for privacy questions and data deletion requests: `privacy@diettracker.example`
(replace with the publisher's real address before submitting to the Store).

## 2. Which information is processed

**Stored locally (working copy while an account is signed in):**

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
| Application preferences | yes | partially (time zone, reminder toggles) |
| Access/refresh tokens | DPAPI protected, per Windows user | never |
| Device identifier (random GUID) | yes | yes (used to detect echoes of your own uploads) |

Uploads only happen after you explicitly enable synchronization and accept the consent switch that is
shown before the first upload. "Local" means the working copy described above: it is deleted when you
sign out, after the pending changes were uploaded. Records created before a first sign-in are the only
exception - they are uploaded to the account instead of being deleted.

## 4. Where the data is stored in the cloud

* **Google account:** the hidden `appDataFolder` of your Google Drive, accessed with the scope
  `https://www.googleapis.com/auth/drive.appdata`.

The data is stored in **your** cloud storage account and is subject to that provider's terms and
privacy policy in addition to this document.

## 5. Purpose of synchronization

Synchronization keeps your data in your own cloud account and lets you use it on several Windows PCs
or after reinstalling the application. The account is the copy that persists: this device holds a
working copy while you are signed in. Signing in downloads the account's data; signing out uploads
what is still pending and then deletes every meal, photograph and template from the device, so a
second account can never see the first one's records. Without an account the application still works
offline; records created before the first sign-in are uploaded to (adopted by) that account.

## 6. Encryption

* All network traffic uses HTTPS/TLS to the provider endpoints.
* Credentials (the Google refresh token) are encrypted with **Windows DPAPI in the
  current-user scope** and stored in the private application data folder.
* Cloud backup contents are **not additionally encrypted by Diet Tracker**: they are protected by the
  provider's storage encryption and by the permissions of your account. This is not end-to-end
  encryption, and the policy deliberately does not claim it.

## 7. Your controls

In **Settings** you can:

* **Sign out** - uploads changes that are still pending, removes the credentials and deletes the local
  working copy (meals, photographs, templates, reminders and the synchronization journal). If changes
  cannot be uploaded, the operation is refused so nothing is lost; you can still sign out explicitly
  and discard them.
* **Export data** - writes either a `.dtbackup` archive (JSON plus photographs) that you can keep
  anywhere and restore later, or a printable PDF report of the stored meals.
* **Restore backup** and **check integrity** - merge a `.dtbackup` archive into the device and verify
  the database; neither of them deletes anything.

The destructive operations (deleting the local data, deleting the photographs, deleting the cloud copy
and turning synchronization off while keeping the authorization) are not offered as buttons on the
Settings page any more. The local working copy is still deleted by **Sign out**, the application data
folder can be removed at any time with the usual Windows tools (or by uninstalling the application),
and the Diet Tracker folder of the connected account can be removed in the provider's own interface.

## 8. Retention

Data is retained until you delete it. The working copy on the device lives while an account is signed
in: signing out deletes it (after uploading the pending changes), and a cache that survives a crash is
deleted at the next start. Uninstalling the application (packaged installs remove the application's
local data folder) or restoring a backup over it also removes it. Cloud data lives until you delete it
in Diet Tracker or in the provider's own interface.

## 9. Children

Diet Tracker is a personal planning tool and is not directed at children. It does not contain
advertising, tracking or user generated content sharing.

## 10. Changes

Material changes to this policy are published with a new application version and are visible in the
Store listing. The version and date are stated at the top of this document.

## 11. Store listing

The same text (or a link to it) is provided in the Microsoft Store listing so that it is available
before installation.
