using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Feedback;

[JsonConverter(typeof(CreateFeedbackRequestJsonConverter))]
public sealed record CreateFeedbackRequest(
    Guid TripId,
    FeedbackQuickTag QuickTag,
    string? Comment);

public sealed class CreateFeedbackRequestJsonConverter
    : JsonConverter<CreateFeedbackRequest>
{
    private const int InvalidQuickTagValue = -1;

    public override CreateFeedbackRequest Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return InvalidRequest();
        }

        Guid tripId = Guid.Empty;
        var quickTag = (FeedbackQuickTag)InvalidQuickTagValue;
        string? comment = null;
        var hasTripId = false;
        var hasQuickTag = false;

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "tripId", StringComparison.OrdinalIgnoreCase)
                && !hasTripId
                && property.Value.ValueKind == JsonValueKind.String
                && Guid.TryParse(property.Value.GetString(), out tripId))
            {
                hasTripId = true;
                continue;
            }

            if (string.Equals(property.Name, "quickTag", StringComparison.OrdinalIgnoreCase)
                && !hasQuickTag
                && property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (Enum.TryParse<FeedbackQuickTag>(value, out var parsedQuickTag)
                    && string.Equals(value, parsedQuickTag.ToString(), StringComparison.Ordinal))
                {
                    quickTag = parsedQuickTag;
                    hasQuickTag = true;
                    continue;
                }
            }

            if (string.Equals(property.Name, "comment", StringComparison.OrdinalIgnoreCase)
                && (property.Value.ValueKind == JsonValueKind.Null
                    || property.Value.ValueKind == JsonValueKind.String))
            {
                comment = property.Value.ValueKind == JsonValueKind.Null
                    ? null
                    : property.Value.GetString();
                continue;
            }

            return InvalidRequest();
        }

        return hasTripId && hasQuickTag
            ? new CreateFeedbackRequest(tripId, quickTag, comment)
            : InvalidRequest();
    }

    public override void Write(
        Utf8JsonWriter writer,
        CreateFeedbackRequest value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("tripId", value.TripId);
        writer.WriteString("quickTag", value.QuickTag.ToString());

        if (value.Comment is not null)
        {
            writer.WriteString("comment", value.Comment);
        }

        writer.WriteEndObject();
    }

    private static CreateFeedbackRequest InvalidRequest() =>
        new(Guid.Empty, (FeedbackQuickTag)InvalidQuickTagValue, null);
}
