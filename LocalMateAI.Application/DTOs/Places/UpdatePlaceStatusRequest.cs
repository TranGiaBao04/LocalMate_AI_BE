using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Places;

[JsonConverter(typeof(UpdatePlaceStatusRequestJsonConverter))]
public sealed record UpdatePlaceStatusRequest(PlaceStatus Status);

public sealed class UpdatePlaceStatusRequestJsonConverter
    : JsonConverter<UpdatePlaceStatusRequest>
{
    private const int InvalidStatusValue = -1;

    public override UpdatePlaceStatusRequest Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return InvalidRequest();
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!string.Equals(property.Name, "status", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String
                && Enum.TryParse<PlaceStatus>(property.Value.GetString(), true, out var parsedStatus))
            {
                return new UpdatePlaceStatusRequest(parsedStatus);
            }

            if (property.Value.ValueKind == JsonValueKind.Number
                && property.Value.TryGetInt32(out var numericStatus))
            {
                return new UpdatePlaceStatusRequest((PlaceStatus)numericStatus);
            }

            return InvalidRequest();
        }

        return InvalidRequest();
    }

    public override void Write(
        Utf8JsonWriter writer,
        UpdatePlaceStatusRequest value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("status", value.Status.ToString());
        writer.WriteEndObject();
    }

    private static UpdatePlaceStatusRequest InvalidRequest() =>
        new((PlaceStatus)InvalidStatusValue);
}
