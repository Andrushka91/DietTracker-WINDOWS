-- DietTracker export -> PostgreSQL
-- Restore with:  psql -U <user> -d <database> -f diettracker-postgres.sql
-- (or open the file in pgAdmin's Query tool and execute it)
-- Values that were stored as ISO-8601 text in SQLite are cast to TIMESTAMPTZ / DATE / TIME.
SET client_encoding = 'UTF8';
BEGIN;
CREATE TABLE IF NOT EXISTS "AppPreferences" (
    "LocalProfileId" TEXT,
    "Language" TEXT,
    "TimeZone" TEXT,
    "ShowSnackCards" BIGINT,
    "StartWithWindows" BIGINT,
    "MinimizeToTray" BIGINT,
    "RemindersEnabled" BIGINT,
    "Use12HourClock" BIGINT,
    "SynchronizationEnabled" BIGINT,
    "PreferredProvider" BIGINT,
    "CloudUploadConsentGranted" BIGINT,
    "UpdatedAt" TIMESTAMPTZ,
    "SyncStatus" BIGINT,
    "OnboardingCompleted" BIGINT
);

INSERT INTO "AppPreferences" ("LocalProfileId","Language","TimeZone","ShowSnackCards","StartWithWindows","MinimizeToTray","RemindersEnabled","Use12HourClock","SynchronizationEnabled","PreferredProvider","CloudUploadConsentGranted","UpdatedAt","SyncStatus","OnboardingCompleted") VALUES ('4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,''::TEXT,''::TEXT,1,0,1,1,0,0,0,0,'2026-10-19T16:46:06.8072753+00:00'::TIMESTAMPTZ,1,0);
CREATE TABLE IF NOT EXISTS "CloudAccount" (
    "Id" TEXT,
    "ProviderType" BIGINT,
    "ProviderUserId" TEXT,
    "DisplayName" TEXT,
    "Email" TEXT,
    "RootFolderId" TEXT,
    "LastAuthenticatedAt" TIMESTAMPTZ,
    "LastSynchronizedAt" TIMESTAMPTZ,
    "IsActive" BIGINT,
    "SynchronizationEnabled" BIGINT,
    "QuotaTotalBytes" BIGINT,
    "QuotaRemainingBytes" BIGINT
);

CREATE TABLE IF NOT EXISTS "ConflictRecord" (
    "Id" TEXT,
    "EntityType" BIGINT,
    "EntityId" TEXT,
    "LocalPayload" TEXT,
    "RemotePayload" TEXT,
    "LocalModifiedAt" TIMESTAMPTZ,
    "RemoteModifiedAt" TIMESTAMPTZ,
    "RemoteRevision" TEXT,
    "DeviceId" TEXT,
    "DetectedAt" TIMESTAMPTZ,
    "IsResolved" BIGINT
);

CREATE TABLE IF NOT EXISTS "DeletionTombstone" (
    "Id" TEXT,
    "EntityType" BIGINT,
    "EntityId" TEXT,
    "DeletedAt" TIMESTAMPTZ,
    "SyncStatus" BIGINT
);

CREATE TABLE IF NOT EXISTS "MealCategory" (
    "Id" TEXT,
    "FixedCategoryKey" BIGINT,
    "Name" TEXT,
    "Color" TEXT,
    "Icon" TEXT,
    "DefaultTime" TIME,
    "DisplayOrder" BIGINT
);

INSERT INTO "MealCategory" ("Id","FixedCategoryKey","Name","Color","Icon","DefaultTime","DisplayOrder") VALUES ('db14af1e-a12e-49e2-97a4-862153d2be28'::TEXT,1,''::TEXT,'#FF0F6CBD'::TEXT,''::TEXT,'07:30:00'::TIME,0);
INSERT INTO "MealCategory" ("Id","FixedCategoryKey","Name","Color","Icon","DefaultTime","DisplayOrder") VALUES ('8e1b6473-f209-4e3b-9803-0f8dab96db17'::TEXT,2,''::TEXT,'#FF8764B8'::TEXT,''::TEXT,'10:30:00'::TIME,1);
INSERT INTO "MealCategory" ("Id","FixedCategoryKey","Name","Color","Icon","DefaultTime","DisplayOrder") VALUES ('1b5b16dd-5c35-4e9a-9e29-8887a09f5d41'::TEXT,3,''::TEXT,'#FF107C10'::TEXT,''::TEXT,'13:30:00'::TIME,2);
INSERT INTO "MealCategory" ("Id","FixedCategoryKey","Name","Color","Icon","DefaultTime","DisplayOrder") VALUES ('9a28ed4d-e803-4cdb-a1c5-a405215cba4b'::TEXT,4,''::TEXT,'#FFCA5010'::TEXT,''::TEXT,'17:00:00'::TIME,3);
INSERT INTO "MealCategory" ("Id","FixedCategoryKey","Name","Color","Icon","DefaultTime","DisplayOrder") VALUES ('d356d82d-37de-4e76-bdad-d1ccff329666'::TEXT,5,''::TEXT,'#FFB146C2'::TEXT,''::TEXT,'20:00:00'::TIME,4);
CREATE TABLE IF NOT EXISTS "MealEntry" (
    "Id" TEXT,
    "LocalProfileId" TEXT,
    "Date" DATE,
    "MealCategoryId" TEXT,
    "MealCategoryKey" BIGINT,
    "Title" TEXT,
    "Ingredients" TEXT,
    "PlannedTime" TIME,
    "Status" BIGINT,
    "CompletedAt" TIMESTAMPTZ,
    "CreatedAt" TIMESTAMPTZ,
    "UpdatedAt" TIMESTAMPTZ,
    "Revision" BIGINT,
    "SyncStatus" BIGINT,
    "CloudRevision" TEXT,
    "ContentHash" TEXT
);

INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('4260deb2-c618-4e1f-8264-62ff1e58be69'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-19'::DATE,'db14af1e-a12e-49e2-97a4-862153d2be28'::TEXT,1,'Mic Dejun'::TEXT,''::TEXT,NULL,1,'2026-10-19T16:59:35.1112855+00:00'::TIMESTAMPTZ,'2026-10-19T16:46:06.8103219+00:00'::TIMESTAMPTZ,'2026-10-19T16:59:35.1114402+00:00'::TIMESTAMPTZ,8,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('21f5a539-897e-4e83-8fa3-cbe683a55915'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-19'::DATE,'8e1b6473-f209-4e3b-9803-0f8dab96db17'::TEXT,2,''::TEXT,''::TEXT,NULL,2,NULL,'2026-10-19T16:46:06.8103406+00:00'::TIMESTAMPTZ,'2026-10-19T16:59:42.2646442+00:00'::TIMESTAMPTZ,2,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('8c19c9af-0c0e-49b6-8733-b67f6a48fa18'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-19'::DATE,'1b5b16dd-5c35-4e9a-9e29-8887a09f5d41'::TEXT,3,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:46:06.8103409+00:00'::TIMESTAMPTZ,'2026-10-19T16:46:06.8103409+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('9e361121-a253-4199-9abe-802561632a1c'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-19'::DATE,'9a28ed4d-e803-4cdb-a1c5-a405215cba4b'::TEXT,4,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:46:06.8103416+00:00'::TIMESTAMPTZ,'2026-10-19T16:46:06.8103416+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('ca28b924-0330-48ae-a574-f2c585d2a3f5'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-19'::DATE,'d356d82d-37de-4e76-bdad-d1ccff329666'::TEXT,5,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:46:06.8103418+00:00'::TIMESTAMPTZ,'2026-10-19T16:46:06.8103418+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('9fd9dc37-818d-4522-a66c-02428b27be05'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-01'::DATE,'db14af1e-a12e-49e2-97a4-862153d2be28'::TEXT,1,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:08.2038876+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:08.2038878+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('012866aa-e5c4-4ed7-b7d0-4095dd469536'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-01'::DATE,'8e1b6473-f209-4e3b-9803-0f8dab96db17'::TEXT,2,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:08.2038887+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:08.2038888+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('86f3a32b-96ac-4fdf-b503-6266123aca9a'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-01'::DATE,'1b5b16dd-5c35-4e9a-9e29-8887a09f5d41'::TEXT,3,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:08.2038889+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:08.2038890+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('7061a233-cbf3-4208-9246-eda6f19ba7bc'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-01'::DATE,'9a28ed4d-e803-4cdb-a1c5-a405215cba4b'::TEXT,4,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:08.2038891+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:08.2038892+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('da772261-7a88-4a9c-a673-ba85503e2e9f'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-01'::DATE,'d356d82d-37de-4e76-bdad-d1ccff329666'::TEXT,5,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:08.2038894+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:08.2038894+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('44ecd026-d97d-42fc-8014-f97f060d3a79'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-20'::DATE,'db14af1e-a12e-49e2-97a4-862153d2be28'::TEXT,1,'Mic Dejun'::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:26.7563054+00:00'::TIMESTAMPTZ,'2026-10-19T16:54:43.0935639+00:00'::TIMESTAMPTZ,1,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('ba61adb9-5b92-4a1b-8741-2ac6d474ab9d'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-20'::DATE,'8e1b6473-f209-4e3b-9803-0f8dab96db17'::TEXT,2,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:26.7563060+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:26.7563060+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('f2368408-38ec-4ae8-8f06-de23bb23c6a2'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-20'::DATE,'1b5b16dd-5c35-4e9a-9e29-8887a09f5d41'::TEXT,3,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:26.7563062+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:26.7563062+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('afce9efa-506a-4f40-a6fb-3d780a2a4f40'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-20'::DATE,'9a28ed4d-e803-4cdb-a1c5-a405215cba4b'::TEXT,4,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:26.7563064+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:26.7563064+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('0ab57793-510b-4454-9e49-cfc69578c0ef'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-20'::DATE,'d356d82d-37de-4e76-bdad-d1ccff329666'::TEXT,5,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:26.7563075+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:26.7563089+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('c43272a9-ef10-43a5-92c1-2abb44d25ec6'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-18'::DATE,'db14af1e-a12e-49e2-97a4-862153d2be28'::TEXT,1,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:29.9055288+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:29.9055289+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('6933ca7f-09a0-46f0-a83b-7143a21f41a6'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-18'::DATE,'8e1b6473-f209-4e3b-9803-0f8dab96db17'::TEXT,2,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:29.9055296+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:29.9055296+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('303f82d8-9f3e-4656-98c3-e79046ba98b7'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-18'::DATE,'1b5b16dd-5c35-4e9a-9e29-8887a09f5d41'::TEXT,3,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:29.9055297+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:29.9055297+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('b6bb24d1-2f93-45ef-8bc4-7c4946d07eec'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-18'::DATE,'9a28ed4d-e803-4cdb-a1c5-a405215cba4b'::TEXT,4,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:29.9055300+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:29.9055300+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
INSERT INTO "MealEntry" ("Id","LocalProfileId","Date","MealCategoryId","MealCategoryKey","Title","Ingredients","PlannedTime","Status","CompletedAt","CreatedAt","UpdatedAt","Revision","SyncStatus","CloudRevision","ContentHash") VALUES ('b0a38316-86c8-4e7d-bce9-51e8953fd220'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'2026-10-18'::DATE,'d356d82d-37de-4e76-bdad-d1ccff329666'::TEXT,5,''::TEXT,''::TEXT,NULL,0,NULL,'2026-10-19T16:52:29.9055302+00:00'::TIMESTAMPTZ,'2026-10-19T16:52:29.9055302+00:00'::TIMESTAMPTZ,0,0,NULL,NULL);
CREATE TABLE IF NOT EXISTS "MealPhoto" (
    "Id" TEXT,
    "MealEntryId" TEXT,
    "OriginalFileName" TEXT,
    "LocalFileId" TEXT,
    "ThumbnailId" TEXT,
    "CloudObjectId" TEXT,
    "CloudRevision" TEXT,
    "MimeType" TEXT,
    "FileSize" BIGINT,
    "ContentHash" TEXT,
    "CreatedAt" TIMESTAMPTZ,
    "UpdatedAt" TIMESTAMPTZ,
    "SyncStatus" BIGINT
);

INSERT INTO "MealPhoto" ("Id","MealEntryId","OriginalFileName","LocalFileId","ThumbnailId","CloudObjectId","CloudRevision","MimeType","FileSize","ContentHash","CreatedAt","UpdatedAt","SyncStatus") VALUES ('53c192fa-709b-46dd-8e21-a5a1403c0684'::TEXT,'4260deb2-c618-4e1f-8264-62ff1e58be69'::TEXT,'IMG_8819.JPG'::TEXT,'e7cdf27eb0a1f0bfc8d1388d8bba93f3.jpg'::TEXT,'1fb0bafa5245be72c9da6de4e5461f00.png'::TEXT,NULL,NULL,'image/jpeg'::TEXT,2627120,'e49fd77630c536c254b3472b53bbd5d8efa88d2ad3940fd1e80aa0977f488847'::TEXT,'2026-10-19T16:54:15.9848702+00:00'::TIMESTAMPTZ,'2026-10-19T16:54:15.9848793+00:00'::TIMESTAMPTZ,0);
CREATE TABLE IF NOT EXISTS "MealTemplate" (
    "Id" TEXT,
    "LocalProfileId" TEXT,
    "Name" TEXT,
    "IsFullDay" BIGINT,
    "TemplateData" TEXT,
    "RecurrenceRules" TEXT,
    "CreatedAt" TIMESTAMPTZ,
    "UpdatedAt" TIMESTAMPTZ,
    "SyncStatus" BIGINT
);

INSERT INTO "MealTemplate" ("Id","LocalProfileId","Name","IsFullDay","TemplateData","RecurrenceRules","CreatedAt","UpdatedAt","SyncStatus") VALUES ('249077ad-3b43-43fb-8af3-0b43d35e4158'::TEXT,'4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,'Mic Dejun'::TEXT,0,'{"single":{"mealCategoryKey":"Breakfast","title":"Mic Dejun"},"day":[]}'::TEXT,NULL,'2026-10-19T16:54:30.5026351+00:00'::TIMESTAMPTZ,'2026-10-19T16:54:30.5026351+00:00'::TIMESTAMPTZ,0);
CREATE TABLE IF NOT EXISTS "ReminderFired" (
    "Id" TEXT,
    "ReminderId" TEXT,
    "Date" DATE,
    "FiredAt" TIMESTAMPTZ,
    "MealEntryId" TEXT
);

CREATE TABLE IF NOT EXISTS "ReminderSettings" (
    "Id" TEXT,
    "LocalProfileId" TEXT,
    "MealCategoryId" TEXT,
    "MealCategoryKey" BIGINT,
    "Enabled" BIGINT,
    "Time" TIME,
    "Weekdays" BIGINT,
    "SnoozeMinutes" BIGINT,
    "UpdatedAt" TIMESTAMPTZ,
    "SyncStatus" BIGINT
);

CREATE TABLE IF NOT EXISTS "SyncMetadata" (
    "CloudAccountId" TEXT,
    "ProviderType" BIGINT,
    "LastSyncCursor" TEXT,
    "LastSyncTimestamp" TEXT,
    "SchemaVersion" BIGINT,
    "DeviceId" TEXT,
    "ManifestRevision" TEXT,
    "LastFullUploadAt" TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS "SyncOperation" (
    "Id" TEXT,
    "CloudAccountId" TEXT,
    "EntityType" BIGINT,
    "EntityId" TEXT,
    "OperationType" BIGINT,
    "Status" BIGINT,
    "EntityRevision" BIGINT,
    "RetryCount" BIGINT,
    "LastError" TEXT,
    "NextAttemptAt" TIMESTAMPTZ,
    "CreatedAt" TIMESTAMPTZ,
    "CompletedAt" TIMESTAMPTZ
);

CREATE TABLE IF NOT EXISTS "UserProfile" (
    "Id" TEXT,
    "ProviderType" BIGINT,
    "ProviderUserId" TEXT,
    "DisplayName" TEXT,
    "Email" TEXT,
    "Language" TEXT,
    "TimeZone" TEXT,
    "IsActive" BIGINT,
    "CreatedAt" TIMESTAMPTZ,
    "UpdatedAt" TIMESTAMPTZ,
    "SyncStatus" BIGINT,
    "CloudRevision" TEXT
);

INSERT INTO "UserProfile" ("Id","ProviderType","ProviderUserId","DisplayName","Email","Language","TimeZone","IsActive","CreatedAt","UpdatedAt","SyncStatus","CloudRevision") VALUES ('4715ad2d-2fca-4bcc-9bf4-837f71875c24'::TEXT,0,''::TEXT,''::TEXT,''::TEXT,''::TEXT,''::TEXT,1,'2026-10-19T16:46:06.7992832+00:00'::TIMESTAMPTZ,'2026-10-19T16:46:06.7992971+00:00'::TIMESTAMPTZ,1,NULL);
COMMIT;
