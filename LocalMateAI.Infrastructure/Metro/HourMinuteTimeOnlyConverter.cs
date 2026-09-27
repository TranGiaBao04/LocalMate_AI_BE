using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalMateAI.Infrastructure.Metro;

/// <summary>Đọc giờ dạng "HH:mm" trong metro-timetable.json (mặc định System.Text.Json đòi "HH:mm:ss").</summary>
internal sealed class HourMinuteTimeOnlyConverter : JsonConverter<TimeOnly>
{
    private const string Format = "HH:mm";

    public override TimeOnly Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();

        return TimeOnly.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new JsonException($"Time '{value}' must use the {Format} format.");
    }

    public override void Write(Utf8JsonWriter writer, TimeOnly value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
}
