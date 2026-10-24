using DietTracker.Core.Enums;
using DietTracker.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DietTracker.Core.Serialization;

/// <summary>
/// Central <see cref="JsonSerializerOptions"/> used for local JSON columns and for the cloud payload.
/// </summary>
public static class DietTrackerJson
{
    /// <summary>Options used for SQLite JSON columns (compact, stable, no indentation).</summary>
    public static JsonSerializerOptions Compact { get; } = Create(indented: false);

    /// <summary>Options used for backups, exports and cloud payloads (readable, diff friendly).</summary>
    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented) => new()
    {
        WriteIndented = indented,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters =
        {
            // Registered before the generic string-enum converter so it wins for MealStatus.
            new MealStatusJsonConverter(),
            new JsonStringEnumConverter(),
            new DateOnlyJsonConverter(),
            new TimeOnlyJsonConverter(),
        },
    };
}

/// <summary>
/// Reads and writes <see cref="MealStatus"/> as the words "Pending" / "Done" / "Missed". A value
/// written by an unknown build reads back as <see cref="MealStatus.Pending"/> instead of failing the
/// whole payload, so the three-state model also holds for cloud data and for backups restored from
/// an older version (the legacy integer 0 / 1 / 2 is accepted as well).
/// </summary>
public sealed class MealStatusJsonConverter : JsonConverter<MealStatus>
{
    /// <inheritdoc />
    public override MealStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        // Old payloads are strings ("Pending"), but tolerate the raw integer (0 / 1 / 2) as well,
        // because the options allow numbers written as strings and hand-edited files.
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32() switch
            {
                1 => MealStatus.Done,
                2 => MealStatus.Missed,
                _ => MealStatus.Pending,
            };
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            return MealStatus.Pending;
        }

        var text = reader.GetString();
        if (string.Equals(text, "Done", StringComparison.OrdinalIgnoreCase))
        {
            return MealStatus.Done;
        }

        if (string.Equals(text, "Missed", StringComparison.OrdinalIgnoreCase))
        {
            return MealStatus.Missed;
        }

        return MealStatus.Pending;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, MealStatus value, JsonSerializerOptions options)
        => writer.WriteStringValue(value switch
        {
            MealStatus.Done => "Done",
            MealStatus.Missed => "Missed",
            _ => "Pending",
        });
}
