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
            new JsonStringEnumConverter(),
            new DateOnlyJsonConverter(),
            new TimeOnlyJsonConverter(),
        },
    };
}
