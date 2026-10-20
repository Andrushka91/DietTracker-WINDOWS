using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IMealTemplateRepository"/>.</summary>
public sealed class SqliteMealTemplateRepository : IMealTemplateRepository
{
    private const string SelectColumns =
        "Id, LocalProfileId, Name, IsFullDay, TemplateData, RecurrenceRules, CreatedAt, UpdatedAt, SyncStatus";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteMealTemplateRepository(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<MealTemplate>> GetAllAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealTemplate WHERE LocalProfileId = $profile ORDER BY Name;";
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(profileId));

        var result = new List<MealTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Map(reader));
        }

        return result;
    }

    public async Task<MealTemplate?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealTemplate WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    public async Task UpsertAsync(MealTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO MealTemplate (Id, LocalProfileId, Name, IsFullDay, TemplateData, RecurrenceRules, CreatedAt, UpdatedAt, SyncStatus)
            VALUES ($id, $profile, $name, $fullDay, $data, $recurrence, $createdAt, $updatedAt, $syncStatus)
            ON CONFLICT (Id) DO UPDATE SET
                Name = excluded.Name,
                IsFullDay = excluded.IsFullDay,
                TemplateData = excluded.TemplateData,
                RecurrenceRules = excluded.RecurrenceRules,
                UpdatedAt = excluded.UpdatedAt,
                SyncStatus = excluded.SyncStatus;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(template.Id));
        command.Parameters.AddWithValue("$profile", SqliteValue.ToDb(template.LocalProfileId));
        command.Parameters.AddWithValue("$name", template.Name);
        command.Parameters.AddWithValue("$fullDay", template.IsFullDay ? 1 : 0);
        command.Parameters.AddWithValue("$data", SerializeContent(template));
        command.Parameters.AddWithValue("$recurrence", SqliteValue.NullIfEmpty(template.RecurrenceRules) ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", SqliteValue.ToDb(template.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", SqliteValue.ToDb(template.UpdatedAt));
        command.Parameters.AddWithValue("$syncStatus", (int)template.SyncStatus);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MealTemplate WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(id));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    // Template content (single meal or full day) is stored as JSON in TemplateData: the payload is
    // small, always read as a whole and never queried by field.
    private static string SerializeContent(MealTemplate template)
        => SqliteValue.ToJson(new TemplatePayload(template.IsFullDay ? null : template.TemplateMeal, template.TemplateDay.ToList()));

    private static MealTemplate Map(SqliteDataReader reader)
    {
        var payload = SqliteValue.FromJson<TemplatePayload>(reader.GetValue(4)) ?? new TemplatePayload(null, []);

        return new MealTemplate
        {
            Id = SqliteValue.GetGuid(reader.GetValue(0)),
            LocalProfileId = SqliteValue.GetGuid(reader.GetValue(1)),
            Name = SqliteValue.GetString(reader.GetValue(2)),
            IsFullDay = SqliteValue.GetBool(reader.GetValue(3)),
            TemplateData = SqliteValue.GetString(reader.GetValue(4)),
            RecurrenceRules = SqliteValue.GetStringOrNull(reader.GetValue(5)),
            CreatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(6)),
            UpdatedAt = SqliteValue.GetDateTimeOffset(reader.GetValue(7)),
            SyncStatus = SqliteValue.GetEnum<SyncStatus>(reader.GetValue(8)),
            TemplateMealSlot = payload.Single?.MealCategoryKey,
            TemplateMeal = payload.Single,
            TemplateDay = payload.Day ?? [],
        };
    }

    private sealed record TemplatePayload(MealTemplateContent? Single, List<MealTemplateContent>? Day);
}
