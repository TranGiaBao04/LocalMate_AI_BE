using LocalMateAI.Application.DTOs.Metro;

namespace LocalMateAI.Application.Services;

/// <summary>Kiểm tra metro-timetable.json; trả danh sách lỗi (rỗng = hợp lệ). Lỗi chỉ để log cho dev, không trả ra API.</summary>
public static class MetroTimetableValidator
{
    public const int FirstStationOrder = 1;
    public const int LastStationOrder = 14;
    public const int MaxHeadwayMinutes = 60;
    public const int MaxNoticeLength = 500;
    private const int MinutesPerDay = 24 * 60;

    public static IReadOnlyList<string> Validate(MetroTimetable timetable)
    {
        var errors = new List<string>();

        if (timetable.EffectiveFrom == default)
        {
            errors.Add("effectiveFrom is required.");
        }

        if (timetable.EffectiveTo is { } effectiveTo && effectiveTo < timetable.EffectiveFrom)
        {
            errors.Add("effectiveTo must be on or after effectiveFrom.");
        }

        if (string.IsNullOrWhiteSpace(timetable.Source))
        {
            errors.Add("source is required.");
        }

        if (timetable.Notice?.Length > MaxNoticeLength)
        {
            errors.Add($"notice must be at most {MaxNoticeLength} characters.");
        }

        var offsetsValid = ValidateOffsets(timetable.StationOffsets, errors);
        ValidateServices(timetable, offsetsValid, errors);

        return errors;
    }

    public static int MinutesFromOrigin(MetroStationOffset offset, MetroDirection direction) =>
        direction == MetroDirection.TowardSuoiTien ? offset.MinutesTowardSuoiTien : offset.MinutesTowardBenThanh;

    private static bool ValidateOffsets(IReadOnlyList<MetroStationOffset>? offsets, List<string> errors)
    {
        const int stationCount = LastStationOrder - FirstStationOrder + 1;
        if (offsets is null
            || offsets.Count != stationCount
            || offsets.Select(offset => offset.StationOrder).Distinct().Count() != stationCount
            || offsets.Any(offset => offset.StationOrder is < FirstStationOrder or > LastStationOrder))
        {
            errors.Add($"stationOffsets must contain each station order {FirstStationOrder}-{LastStationOrder} exactly once.");
            return false;
        }

        var errorCount = errors.Count;
        var ordered = offsets.OrderBy(offset => offset.StationOrder).ToList();

        if (ordered[0].MinutesTowardSuoiTien != 0)
        {
            errors.Add($"Station {FirstStationOrder} must have minutesTowardSuoiTien = 0.");
        }

        if (ordered[^1].MinutesTowardBenThanh != 0)
        {
            errors.Add($"Station {LastStationOrder} must have minutesTowardBenThanh = 0.");
        }

        // Tàu phải mất thời gian để tới ga sau, nên số phút tăng chặt theo hướng đi.
        for (var index = 1; index < ordered.Count; index++)
        {
            var previous = ordered[index - 1];
            var current = ordered[index];

            if (current.MinutesTowardSuoiTien <= previous.MinutesTowardSuoiTien)
            {
                errors.Add($"minutesTowardSuoiTien must increase from station {previous.StationOrder} to {current.StationOrder}.");
            }

            if (previous.MinutesTowardBenThanh <= current.MinutesTowardBenThanh)
            {
                errors.Add($"minutesTowardBenThanh must increase from station {current.StationOrder} to {previous.StationOrder}.");
            }
        }

        return errors.Count == errorCount;
    }

    private static void ValidateServices(MetroTimetable timetable, bool offsetsValid, List<string> errors)
    {
        var services = timetable.Services;
        if (services is null || services.Count == 0)
        {
            errors.Add("services must not be empty.");
            return;
        }

        for (var index = 0; index < services.Count; index++)
        {
            var service = services[index];
            var label = $"services[{index}]";

            if (service.Days is null || service.Days.Count == 0)
            {
                errors.Add($"{label}.days must not be empty.");
            }
            else if (service.Days.Distinct().Count() != service.Days.Count)
            {
                errors.Add($"{label}.days must not contain duplicates.");
            }

            if (service.FirstDeparture >= service.LastDeparture)
            {
                errors.Add($"{label}.firstDeparture must be before lastDeparture.");
            }

            ValidateHeadways(service, label, errors);

            // Chưa hỗ trợ qua nửa đêm: chuyến cuối tới ga xa nhất vẫn phải trước 24:00.
            if (offsetsValid)
            {
                var maxOffset = timetable.StationOffsets.Max(offset => MinutesFromOrigin(offset, service.Direction));
                if (ToMinutes(service.LastDeparture) + maxOffset >= MinutesPerDay)
                {
                    errors.Add($"{label}: last train plus travel time must arrive before 24:00.");
                }
            }
        }

        // Mỗi chiều phải phủ đủ 7 ngày, mỗi ngày đúng một service.
        foreach (var direction in Enum.GetValues<MetroDirection>())
        {
            foreach (var day in Enum.GetValues<DayOfWeek>())
            {
                var count = services.Count(service => service.Direction == direction && service.Days?.Contains(day) == true);
                if (count == 0)
                {
                    errors.Add($"{direction} has no service on {day}.");
                }
                else if (count > 1)
                {
                    errors.Add($"{direction} has {count} services on {day}.");
                }
            }
        }
    }

    private static void ValidateHeadways(MetroService service, string label, List<string> errors)
    {
        var headways = service.Headways;
        if (headways is null || headways.Count == 0)
        {
            errors.Add($"{label}.headways must not be empty.");
            return;
        }

        if (headways[0].From != service.FirstDeparture)
        {
            errors.Add($"{label}.headways[0].from must equal firstDeparture.");
        }

        for (var index = 0; index < headways.Count; index++)
        {
            var headway = headways[index];

            if (headway.From >= headway.To)
            {
                errors.Add($"{label}.headways[{index}].from must be before to.");
            }

            if (headway.Minutes is < 1 or > MaxHeadwayMinutes)
            {
                errors.Add($"{label}.headways[{index}].minutes must be between 1 and {MaxHeadwayMinutes}.");
            }

            if (index > 0 && headway.From != headways[index - 1].To)
            {
                errors.Add($"{label}.headways[{index}].from must equal the previous headway's to.");
            }
        }

        if (headways[^1].To < service.LastDeparture)
        {
            errors.Add($"{label}: headways must cover up to lastDeparture.");
        }
    }

    private static int ToMinutes(TimeOnly time) => time.Hour * 60 + time.Minute;
}
