using DietTracker.Core.BusinessRules;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>
/// Verifies the logical invariants that the schema can only partly express: exactly five permanent
/// categories, one slot per category and date, orphaned photographs and reminders that point at a
/// category which no longer exists.
/// </summary>
public sealed class DataIntegrityChecker : IDataIntegrityChecker
{
    private readonly ISqliteConnectionFactory _factory;

    public DataIntegrityChecker(ISqliteConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlyList<DataIntegrityIssue>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var issues = new List<DataIntegrityIssue>();
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var categories = new List<MealCategory>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT Id, FixedCategoryKey, Name, Color, Icon, DefaultTime, DisplayOrder FROM MealCategory;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                categories.Add(new MealCategory
                {
                    Id = SqliteValue.GetGuid(reader.GetValue(0)),
                    FixedCategoryKey = SqliteValue.GetEnum<Core.Enums.MealCategoryKey>(reader.GetValue(1)),
                    DisplayOrder = SqliteValue.GetInt(reader.GetValue(6)),
                });
            }
        }

        foreach (var missing in MealSlotRules.FindMissingSlots(categories))
        {
            issues.Add(new DataIntegrityIssue
            {
                Code = "integrity.category.missing",
                Description = $"The permanent meal category '{missing}' is missing from the database.",
            });
        }

        await AddIssuesAsync(
            connection,
            issues,
            "integrity.slot.duplicate",
            """
            SELECT LocalProfileId, Date, MealCategoryKey, COUNT(*)
            FROM MealEntry GROUP BY LocalProfileId, Date, MealCategoryKey HAVING COUNT(*) > 1;
            """,
            "More than one meal slot exists for the same profile, date and category.",
            cancellationToken).ConfigureAwait(false);

        await AddIssuesAsync(
            connection,
            issues,
            "integrity.entry.orphan",
            """
            SELECT e.Id, 0, 0, 1 FROM MealEntry e
            LEFT JOIN MealCategory c ON c.Id = e.MealCategoryId
            WHERE c.Id IS NULL;
            """,
            "A meal entry references a meal category that does not exist.",
            cancellationToken).ConfigureAwait(false);

        await AddIssuesAsync(
            connection,
            issues,
            "integrity.photo.orphan",
            """
            SELECT p.Id, 0, 0, 1 FROM MealPhoto p
            LEFT JOIN MealEntry e ON e.Id = p.MealEntryId
            WHERE e.Id IS NULL;
            """,
            "A photograph references a meal entry that does not exist.",
            cancellationToken).ConfigureAwait(false);

        await AddIssuesAsync(
            connection,
            issues,
            "integrity.completion.inconsistent",
            """
            SELECT Id, Status, 0, 1 FROM MealEntry
            WHERE (Status = 1 AND CompletedAt IS NULL) OR (Status <> 1 AND CompletedAt IS NOT NULL);
            """,
            "A meal entry has a completion state that contradicts its completion timestamp.",
            cancellationToken).ConfigureAwait(false);

        return issues;
    }

    private static async Task AddIssuesAsync(
        SqliteConnection connection,
        List<DataIntegrityIssue> issues,
        string code,
        string sql,
        string description,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var entityId = reader.FieldCount > 0 && reader.GetValue(0) is string text && Guid.TryParse(text, out var parsed)
                ? parsed
                : (Guid?)null;

            issues.Add(new DataIntegrityIssue { Code = code, Description = description, EntityId = entityId });
        }
    }
}
