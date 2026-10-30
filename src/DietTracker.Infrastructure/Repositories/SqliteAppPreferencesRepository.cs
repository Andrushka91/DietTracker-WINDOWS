using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IAppPreferencesRepository"/>.</summary>
public sealed class SqliteAppPreferencesRepository : IAppPreferencesRepository
{
    private const string SelectColumns =
        "LocalProfileId, TimeZone, ShowSnackCards, StartWithWindows, MinimizeToTray, RemindersEnabled, " +
        "Use12HourClock, SynchronizationEnabled, PreferredProvider, CloudUploadConsentGranted, UpdatedAt, SyncStatus, OnboardingCompleted, SyncFrequency";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteAppPreferencesRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<AppPreferences?> GetAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM AppPreferences WHERE LocalProfileId = $profile;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? RowMappers.MapAppPreferences(reader) : null;
    }

    public async Task UpsertAsync(AppPreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO AppPreferences (
                LocalProfileId, TimeZone, ShowSnackCards, StartWithWindows, MinimizeToTray, RemindersEnabled,
                Use12HourClock, SynchronizationEnabled, PreferredProvider, CloudUploadConsentGranted, UpdatedAt, SyncStatus, OnboardingCompleted, SyncFrequency)
            VALUES (
                $profile, $timeZone, $snacks, $startWithWindows, $tray, $reminders,
                $clock12, $sync, $provider, $consent, $updatedAt, $syncStatus, $onboarding, $syncFrequency)
            ON CONFLICT (LocalProfileId) DO UPDATE SET
                TimeZone = excluded.TimeZone,
                ShowSnackCards = excluded.ShowSnackCards,
                StartWithWindows = excluded.StartWithWindows,
                MinimizeToTray = excluded.MinimizeToTray,
                RemindersEnabled = excluded.RemindersEnabled,
                Use12HourClock = excluded.Use12HourClock,
                SynchronizationEnabled = excluded.SynchronizationEnabled,
                PreferredProvider = excluded.PreferredProvider,
                CloudUploadConsentGranted = excluded.CloudUploadConsentGranted,
                UpdatedAt = excluded.UpdatedAt,
                SyncStatus = excluded.SyncStatus,
                OnboardingCompleted = excluded.OnboardingCompleted,
                SyncFrequency = excluded.SyncFrequency;
            """;
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(preferences.LocalProfileId));
        command.Parameters.AddWithValue("$timeZone", preferences.TimeZone ?? string.Empty);
        command.Parameters.AddWithValue("$snacks", preferences.ShowSnackCards ? 1 : 0);
        command.Parameters.AddWithValue("$startWithWindows", preferences.StartWithWindows ? 1 : 0);
        command.Parameters.AddWithValue("$tray", preferences.MinimizeToTray ? 1 : 0);
        command.Parameters.AddWithValue("$reminders", preferences.RemindersEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$clock12", preferences.Use12HourClock ? 1 : 0);
        command.Parameters.AddWithValue("$sync", preferences.SynchronizationEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$provider", (int)preferences.PreferredProvider);
        command.Parameters.AddWithValue("$consent", preferences.CloudUploadConsentGranted ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", SqliteValue.ToDb(preferences.UpdatedAt));
        command.Parameters.AddWithValue("$syncStatus", (int)preferences.SyncStatus);
        command.Parameters.AddWithValue("$onboarding", preferences.OnboardingCompleted ? 1 : 0);
        command.Parameters.AddWithValue("$syncFrequency", (int)preferences.SyncFrequency);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
