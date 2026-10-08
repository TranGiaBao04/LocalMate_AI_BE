using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Validators.Trips;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

// Kiểm câu trả lời của AI theo đúng luật của form tạo lịch. Trường nào sai thì bỏ (để rỗng), không báo lỗi:
// người dùng sẽ tự điền trường đó trên form. Khi có lịch gốc (base), trường AI không trả hoặc trả sai thì
// giữ giá trị của lịch gốc.
public static class TripRequestParsingOutputParser
{
    public const string MissingDurationHours = "durationHours";
    public const string MissingBudgetMax = "budgetMax";
    public const string MissingStartLocation = "startLocation";

    private static readonly string[] TimeFormats = ["HH:mm", "H:mm"];

    /// <summary>
    /// Đọc câu trả lời của AI, ghép vào tiêu chí của lịch gốc (nếu có) rồi kiểm theo luật của form.
    /// Trả null khi câu trả lời không phải JSON đúng khuôn.
    /// </summary>
    public static ParsedTripAnswer? Parse(
        string json,
        IReadOnlyDictionary<string, Guid> tagIdsByName,
        IReadOnlySet<int> stationOrders,
        DateTime vietnamNow,
        ParsedTripFields? baseFields = null)
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

        if (root is not JsonObject answer)
        {
            return null;
        }

        var activeTagIds = tagIdsByName.Values.ToHashSet();
        var current = baseFields is null
            ? ParsedTripFields.Empty
            : Clean(baseFields, activeTagIds, stationOrders, vietnamNow);

        // AI nói đây không phải yêu cầu đi chơi thì bỏ mọi trường nó lỡ điền, giữ nguyên tiêu chí đang có.
        if (answer["isTripRequest"] is JsonValue flag && flag.TryGetValue<bool>(out var isTripRequest) && !isTripRequest)
        {
            return new ParsedTripAnswer(false, current, []);
        }

        var tagsMentioned = answer["tags"] is JsonArray;
        var read = DropInvalidValues(
            new ParsedTripFields(
                ReadInt(answer["durationHours"]),
                ReadDecimal(answer["budgetMax"]),
                answer["tags"] is JsonArray tags
                    ? tags
                        .Select(ReadString)
                        .Where(name => name is not null && tagIdsByName.ContainsKey(name))
                        .Select(name => tagIdsByName[name!])
                        .Distinct()
                        .ToList()
                    : [],
                ReadTravelMode(answer["travelMode"]),
                ReadInt(answer["startStationOrder"]),
                ReadInt(answer["destinationStationOrder"]),
                ReadDate(answer["plannedDate"]),
                ReadTime(answer["startTime"]),
                ReadString(answer["note"])),
            activeTagIds,
            stationOrders,
            vietnamNow);

        // Trường AI không trả (hoặc trả sai luật) thì giữ giá trị đang có.
        var merged = new ParsedTripFields(
            read.DurationHours ?? current.DurationHours,
            read.BudgetMax ?? current.BudgetMax,
            // Có lịch gốc: tags = null nghĩa là không đổi; có danh sách (kể cả rỗng) là danh sách mới.
            baseFields is not null && !tagsMentioned ? current.TagIds : read.TagIds,
            read.TravelMode ?? current.TravelMode,
            read.StartStationOrder ?? current.StartStationOrder,
            read.DestinationStationOrder ?? current.DestinationStationOrder,
            read.PlannedDate ?? current.PlannedDate,
            read.StartTime ?? current.StartTime,
            read.Note ?? current.Note);

        var fields = ApplyCrossFieldRules(merged, vietnamNow);

        return new ParsedTripAnswer(true, fields, baseFields is null ? [] : Changed(current, fields));
    }

    /// <summary>
    /// Lọc một bộ tiêu chí theo luật của form: bỏ giá trị sai, rồi áp luật liên quan hai trường.
    /// Dùng cho lịch gốc do client gửi lên (BE không tin dữ liệu client).
    /// </summary>
    public static ParsedTripFields Clean(
        ParsedTripFields fields,
        IReadOnlySet<Guid> activeTagIds,
        IReadOnlySet<int> stationOrders,
        DateTime vietnamNow) =>
        ApplyCrossFieldRules(DropInvalidValues(fields, activeTagIds, stationOrders, vietnamNow), vietnamNow);

    public static IReadOnlyList<string> Missing(ParsedTripFields fields)
    {
        var missing = new List<string>();

        if (fields.DurationHours is null)
        {
            missing.Add(MissingDurationHours);
        }

        if (fields.BudgetMax is null)
        {
            missing.Add(MissingBudgetMax);
        }

        if (fields.StartStationOrder is null)
        {
            missing.Add(MissingStartLocation);
        }

        return missing;
    }

    // Luật của từng trường riêng lẻ: sai thì bỏ trường đó.
    private static ParsedTripFields DropInvalidValues(
        ParsedTripFields fields,
        IReadOnlySet<Guid> activeTagIds,
        IReadOnlySet<int> stationOrders,
        DateTime vietnamNow)
    {
        var note = TripNoteRules.Normalize(fields.Note);

        return new ParsedTripFields(
            fields.DurationHours is >= TripRequestValidator.MinDurationHours and <= TripRequestValidator.MaxDurationHours
                ? fields.DurationHours
                : null,
            fields.BudgetMax is { } budget
            && budget >= 0
            && budget <= TripRequestParsingPrompt.MaxBudget
            && budget == decimal.Truncate(budget)
                ? budget
                : null,
            // Client có thể bỏ trống tagIds trong lịch gốc.
            (fields.TagIds ?? []).Where(activeTagIds.Contains).Distinct().ToList(),
            fields.TravelMode is { } mode && Enum.IsDefined(mode) ? mode : null,
            fields.StartStationOrder is { } start && stationOrders.Contains(start) ? start : null,
            fields.DestinationStationOrder is { } destination && stationOrders.Contains(destination) ? destination : null,
            fields.PlannedDate is { } date && TripTimingRules.ValidatePlannedDate(date, vietnamNow) is null ? date : null,
            fields.StartTime,
            note is { Length: <= TripNoteRules.MaxLength } ? note : null);
    }

    // Luật liên quan hai trường, kiểm SAU khi ghép: giờ xuất phát đã qua so với ngày đi thì bỏ giờ;
    // giờ + số giờ vượt 24:00 (không có giờ thì form tính từ 08:00) thì giữ giờ, bỏ số giờ.
    private static ParsedTripFields ApplyCrossFieldRules(ParsedTripFields fields, DateTime vietnamNow)
    {
        var startTime = fields.StartTime is { } time
            && TripTimingRules.ValidateStartTime(fields.PlannedDate, time, vietnamNow) is null
                ? time
                : (TimeOnly?)null;

        var durationHours = fields.DurationHours is { } hours
            && TripTimingRules.ValidateWindow(startTime, hours * 60) is null
                ? hours
                : (int?)null;

        return fields with { StartTime = startTime, DurationHours = durationHours };
    }

    private static IReadOnlyList<string> Changed(ParsedTripFields before, ParsedTripFields after)
    {
        var changed = new List<string>();

        if (before.DurationHours != after.DurationHours)
        {
            changed.Add("durationHours");
        }

        if (before.BudgetMax != after.BudgetMax)
        {
            changed.Add("budgetMax");
        }

        if (!before.TagIds.ToHashSet().SetEquals(after.TagIds))
        {
            changed.Add("tagIds");
        }

        if (before.TravelMode != after.TravelMode)
        {
            changed.Add("travelMode");
        }

        if (before.StartStationOrder != after.StartStationOrder)
        {
            changed.Add("startStationOrder");
        }

        if (before.DestinationStationOrder != after.DestinationStationOrder)
        {
            changed.Add("destinationStationOrder");
        }

        if (before.PlannedDate != after.PlannedDate)
        {
            changed.Add("plannedDate");
        }

        if (before.StartTime != after.StartTime)
        {
            changed.Add("startTime");
        }

        if (!string.Equals(before.Note, after.Note, StringComparison.Ordinal))
        {
            changed.Add("note");
        }

        return changed;
    }

    private static TravelMode? ReadTravelMode(JsonNode? node) =>
        ReadString(node) is { } raw && Enum.TryParse<TravelMode>(raw, ignoreCase: true, out var mode) ? mode : null;

    private static DateOnly? ReadDate(JsonNode? node) =>
        ReadString(node) is { } raw
        && DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    private static TimeOnly? ReadTime(JsonNode? node) =>
        ReadString(node) is { } raw
        && TimeOnly.TryParseExact(raw, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;

    private static int? ReadInt(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

    private static decimal? ReadDecimal(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : null;

    private static string? ReadString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
