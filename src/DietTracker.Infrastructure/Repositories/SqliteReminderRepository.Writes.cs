namespace DietTracker.Infrastructure.Repositories;

/// <summary>Write operations of <see cref="SqliteReminderRepository"/>.</summary>
public sealed partial class SqliteReminderRepository
{
    public async Task UpsertAsync(Core.Models.ReminderSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ReminderSettings (Id, LocalProfileId, MealCategoryId, MealCategoryKey, Enabled, Time, Weekdays, SnoozeMinutes, UpdatedAt, SyncStatus)
            VALUES ($id, $profile, $categoryId, $key, $enabled, $time, $weekdays, $snooze, $updatedAt, $syncStatus)
            ON CONFLICT (LocalProfileId, MealCategoryKey) DO UPDATE SET
                Enabled = excluded.Enabled,
                Time = excluded.Time,
                Weekdays = excluded.Weekdays,
                SnoozeMinutes = excluded.SnoozeMinutes,
                UpdatedAt = excluded.UpdatedAt,
                SyncStatus = excluded.SyncStatus;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(settings.Id));
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(settings.LocalProfileId));
        command.Parameters.AddWithValue("$categoryId", SqliteValue.ToDb(settings.MealCategoryId));
        command.Parameters.AddWithValue("$key", (int)settings.MealCategoryKey);
        command.Parameters.AddWithValue("$enabled", settings.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$time", SqliteValue.ToDb(settings.Time));
        command.Parameters.AddWithValue("$weekdays", settings.Weekdays);
        command.Parameters.AddWithValue("$snooze", settings.SnoozeMinutes is { } snooze ? snooze : (object)DBNull.Value);
        command.Parameters.AddWithValue("$updatedAt", SqliteValue.ToDb(settings.UpdatedAt));
        command.Parameters.AddWithValue("$syncStatus", (int)settings.SyncStatus);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomically claims the right to raise a reminder for a slot and date. Returns false when the
    /// reminder already fired (after a restart, a resume from sleep or a duplicate timer tick).
    /// </summary>
    public async Task<bool> TryRecordFiredAsync(Guid reminderId, DateOnly date, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ReminderFired (Id, ReminderId, Date, FiredAt, MealEntryId)
            VALUES ($id, $reminder, $date, $firedAt, NULL)
            ON CONFLICT (ReminderId, Date) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(Guid.NewGuid()));
        command.Parameters.AddWithValue("$reminder", SqliteValue.ToDb(reminderId));
        command.Parameters.AddWithValue("$date", SqliteValue.ToDb(date));
        command.Parameters.AddWithValue("$firedAt", SqliteValue.ToDb(DateTimeOffset.UtcNow));

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
    }
}
