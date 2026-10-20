using DietTracker.Core.Enums;
using DietTracker.Core.Interfaces;
using DietTracker.Core.Models;
using Microsoft.Data.Sqlite;

namespace DietTracker.Infrastructure.Repositories;

/// <summary>SQLite implementation of <see cref="IMealCategoryRepository"/>.</summary>
public sealed class SqliteMealCategoryRepository : IMealCategoryRepository
{
    private const string SelectColumns =
        "Id, FixedCategoryKey, Name, Color, Icon, DefaultTime, DisplayOrder";

    private readonly ISqliteConnectionFactory _factory;

    public SqliteMealCategoryRepository(ISqliteConnectionFactory factory) => _factory = factory;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MealCategory>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealCategory ORDER BY DisplayOrder;";

        var result = new List<MealCategory>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(Map(reader));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<MealCategory?> GetByKeyAsync(MealCategoryKey key, CancellationToken cancellationToken = default)
    {
        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM MealCategory WHERE FixedCategoryKey = $key;";
        command.Parameters.AddWithValue("$key", (int)key);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false) ? Map(reader) : null;
    }

    /// <inheritdoc />
    /// <remarks>Inserts only the categories that are missing; nothing is ever removed or overwritten.</remarks>
    public async Task EnsureDefaultsAsync(IEnumerable<MealCategory> defaults, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(defaults);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        foreach (var category in defaults)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO MealCategory (Id, FixedCategoryKey, Name, Color, Icon, DefaultTime, DisplayOrder)
                VALUES ($id, $key, $name, $color, $icon, $time, $order)
                ON CONFLICT (FixedCategoryKey) DO NOTHING;
                """;
            command.Parameters.AddWithValue("$id", SqliteValue.ToDb(category.Id));
            command.Parameters.AddWithValue("$key", (int)category.FixedCategoryKey);
            command.Parameters.AddWithValue("$name", category.Name);
            command.Parameters.AddWithValue("$color", category.Color);
            command.Parameters.AddWithValue("$icon", category.Icon);
            command.Parameters.AddWithValue("$time", SqliteValue.ToDb(category.DefaultTime));
            command.Parameters.AddWithValue("$order", category.DisplayOrder);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(MealCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);

        await using var connection = await _factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO MealCategory (Id, FixedCategoryKey, Name, Color, Icon, DefaultTime, DisplayOrder)
            VALUES ($id, $key, $name, $color, $icon, $time, $order)
            ON CONFLICT (FixedCategoryKey) DO UPDATE SET
                Name = excluded.Name,
                Color = excluded.Color,
                Icon = excluded.Icon,
                DefaultTime = excluded.DefaultTime,
                DisplayOrder = excluded.DisplayOrder;
            """;
        command.Parameters.AddWithValue("$id", SqliteValue.ToDb(category.Id));
        command.Parameters.AddWithValue("$key", (int)category.FixedCategoryKey);
        command.Parameters.AddWithValue("$name", category.Name ?? string.Empty);
        command.Parameters.AddWithValue("$color", category.Color ?? string.Empty);
        command.Parameters.AddWithValue("$icon", category.Icon ?? string.Empty);
        command.Parameters.AddWithValue("$time", SqliteValue.ToDb(category.DefaultTime));
        command.Parameters.AddWithValue("$order", category.DisplayOrder);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static MealCategory Map(SqliteDataReader reader) => new()
    {
        Id = SqliteValue.GetGuid(reader.GetValue(0)),
        FixedCategoryKey = SqliteValue.GetEnum<MealCategoryKey>(reader.GetValue(1)),
        Name = SqliteValue.GetString(reader.GetValue(2)),
        Color = SqliteValue.GetString(reader.GetValue(3)),
        Icon = SqliteValue.GetString(reader.GetValue(4)),
        DefaultTime = SqliteValue.GetTimeOnly(reader.GetValue(5)),
        DisplayOrder = SqliteValue.GetInt(reader.GetValue(6)),
    };
}
