using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalMateAI.Application.Services;

// Kiểm câu trả lời của AI trước khi ghi vào chuyến đi. AI chỉ được trả chữ cho đúng các địa điểm đã hỏi.
public static class TripExplanationOutputParser
{
    /// <summary>
    /// Trả lý do hợp lệ theo placeId. Bỏ: placeId lạ, placeId lặp lại (giữ mục đầu), lý do rỗng hoặc quá dài.
    /// Mục báo không đủ dữ liệu nhận câu cố định. Trả null khi câu trả lời không phải JSON đúng khuôn.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string>? Parse(string json, IReadOnlyCollection<Guid> expectedPlaceIds)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root is not JsonObject || root["stops"] is not JsonArray stops)
        {
            return null;
        }

        var expected = expectedPlaceIds.ToHashSet();
        var reasons = new Dictionary<Guid, string>();

        foreach (var stop in stops.OfType<JsonObject>())
        {
            if (!TryGetString(stop["placeId"], out var rawId)
                || !Guid.TryParse(rawId, out var placeId)
                || !expected.Contains(placeId)
                || reasons.ContainsKey(placeId))
            {
                continue;
            }

            if (stop["hasEnoughData"] is JsonValue flag && flag.TryGetValue<bool>(out var hasEnoughData) && !hasEnoughData)
            {
                reasons[placeId] = TripExplanationPrompt.InsufficientDataReason;
                continue;
            }

            var reason = TryGetString(stop["reason"], out var rawReason) ? Clean(rawReason) : string.Empty;
            if (reason.Length is > 0 and <= TripExplanationPrompt.MaxReasonLength)
            {
                reasons[placeId] = reason;
            }
        }

        return reasons;
    }

    // Bỏ ký tự markdown và mọi kiểu xuống dòng, gộp khoảng trắng.
    private static string Clean(string text)
    {
        var withoutMarkup = new string(text.Where(character => character is not ('*' or '_' or '#' or '`')).ToArray());
        return string.Join(' ', withoutMarkup.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool TryGetString(JsonNode? node, out string value)
    {
        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var text) && text is not null)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
