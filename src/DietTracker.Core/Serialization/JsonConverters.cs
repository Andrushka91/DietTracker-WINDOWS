using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DietTracker.Core.Serialization;

/// <summary>Serializes <see cref="DateOnly"/> as an ISO-8601 calendar date ("yyyy-MM-dd").</summary>
public sealed class DateOnlyJsonConverter : JsonConverter<DateOnly>
{
    private const string Format = "yyyy-MM-dd";

    public override DateOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => DateOnly.ParseExact(reader.GetString() ?? throw new JsonException("Missing date value."), Format, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, DateOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}

/// <summary>Serializes <see cref="TimeOnly"/> as "HH:mm:ss" with optional fractional seconds.</summary>
public sealed class TimeOnlyJsonConverter : JsonConverter<TimeOnly>
{
    private const string Format = "HH:mm:ss";

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => TimeOnly.ParseExact(reader.GetString() ?? throw new JsonException("Missing time value."), Format, CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}
