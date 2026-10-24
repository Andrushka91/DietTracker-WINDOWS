using System.Globalization;
using System.Text.Json;
using DietTracker.Core.Serialization;

namespace DietTracker.Infrastructure.Database;

/// <summary>
/// Converts between .NET domain types and the textual representation used inside SQLite.
/// Keeping the conversions in one place guarantees that dates, times and instants round-trip
/// exactly and stay comparable/sortable inside the database.
/// </summary>
public static class SqliteValue
{
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm:ss";

    public static string ToDb(DateOnly value) => value.ToString(DateFormat, CultureInfo.InvariantCulture);

    public static string ToDb(TimeOnly value) => value.ToString(TimeFormat, CultureInfo.InvariantCulture);

    public static string ToDb(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    public static string ToDb(Guid value) => value.ToString("D");

    public static object? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    public static DateOnly GetDateOnly(object? value) => value switch
    {
        null or DBNull => default,
        DateOnly dateOnly => dateOnly,
        string text => DateOnly.ParseExact(text, DateFormat, CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException($"Cannot read '{value}' as a calendar date."),
    };

    public static TimeOnly? GetTimeOnlyOrNull(object? value) => value switch
    {
        null or DBNull => null,
        TimeOnly timeOnly => timeOnly,
        string text => TimeOnly.ParseExact(text, TimeFormat, CultureInfo.InvariantCulture),
        _ => throw new InvalidCastException($"Cannot read '{value}' as a time of day."),
    };

    public static TimeOnly GetTimeOnly(object? value) => GetTimeOnlyOrNull(value) ?? default;

    public static DateTimeOffset? GetDateTimeOffsetOrNull(object? value) => value switch
    {
        null or DBNull => null,
        DateTimeOffset offset => offset,
        string text => DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        _ => throw new InvalidCastException($"Cannot read '{value}' as an instant."),
    };

    public static DateTimeOffset GetDateTimeOffset(object? value) => GetDateTimeOffsetOrNull(value) ?? default;

    public static Guid GetGuid(object? value) => value switch
    {
        Guid guid => guid,
        string text => Guid.Parse(text),
        _ => throw new InvalidCastException($"Cannot read '{value}' as an identifier."),
    };

    public static string GetString(object? value) => value as string ?? string.Empty;

    public static string? GetStringOrNull(object? value) => value as string is { Length: > 0 } text ? text : null;

    public static int GetInt(object? value) => value switch
    {
        int number => number,
        long number => (int)number,
        string text => int.Parse(text, CultureInfo.InvariantCulture),
        _ => 0,
    };

    public static long GetLong(object? value) => value switch
    {
        long number => number,
        int number => number,
        string text => long.Parse(text, CultureInfo.InvariantCulture),
        _ => 0L,
    };

    public static long? GetLongOrNull(object? value) => value switch
    {
        null or DBNull => null,
        long number => number,
        int number => number,
        string text => long.Parse(text, CultureInfo.InvariantCulture),
        _ => null,
    };

    public static bool GetBool(object? value) => GetInt(value) != 0;

    public static TEnum GetEnum<TEnum>(object? value) where TEnum : struct, Enum
        => (TEnum)Enum.ToObject(typeof(TEnum), GetInt(value));

    /// <summary>Serializes a collection into a JSON column.</summary>
    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value, DietTrackerJson.Compact);

    /// <summary>Deserializes a JSON column, returning <see langword="null"/> when the value is unusable.</summary>
    public static T? FromJson<T>(object? value) where T : class
    {
        var text = GetStringOrNull(value);
        if (text is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(text, DietTrackerJson.Compact);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
