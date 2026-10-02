namespace LocalMateAI.Application.Services;

/// <summary>Luật ngày/giờ của chuyến đi, dùng chung cho generate, feasibility-check và apply lịch mẫu.</summary>
public static class TripTimingRules
{
    public const int MaxDaysAhead = 90;
    public static readonly TimeSpan PastGrace = TimeSpan.FromMinutes(5);
    private const int MinutesPerDay = 24 * 60;

    /// <summary>plannedDate không ở quá khứ và không quá 90 ngày. Null = hôm nay, luôn hợp lệ.</summary>
    public static string? ValidatePlannedDate(DateOnly? plannedDate, DateTime vietnamNow)
    {
        if (plannedDate is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(vietnamNow);
        if (plannedDate < today)
        {
            return "Ngày đi không được ở quá khứ.";
        }

        return plannedDate > today.AddDays(MaxDaysAhead)
            ? $"Ngày đi không được quá {MaxDaysAhead} ngày kể từ hôm nay."
            : null;
    }

    /// <summary>
    /// Chỉ kiểm tra khi client GỬI startTime và ngày đi là hôm nay. Không gửi startTime thì dùng 08:00 như cũ
    /// (để FE cũ không vỡ), nên không coi là lỗi dù đã quá 08:00.
    /// </summary>
    public static string? ValidateStartTime(DateOnly? plannedDate, TimeOnly? startTime, DateTime vietnamNow)
    {
        if (startTime is null)
        {
            return null;
        }

        var isToday = plannedDate is null || plannedDate == DateOnly.FromDateTime(vietnamNow);
        if (!isToday)
        {
            return null;
        }

        // Tính bằng số phút (không dùng TimeOnly - TimeSpan vì nó quay vòng qua nửa đêm: 00:02 - 5 phút = 23:57).
        var earliestMinutes = vietnamNow.TimeOfDay.TotalMinutes - PastGrace.TotalMinutes;
        return startTime.Value.ToTimeSpan().TotalMinutes < earliestMinutes
            ? "Giờ bắt đầu không được ở quá khứ."
            : null;
    }

    /// <summary>startTime (hoặc 08:00) + tổng số phút không được vượt 24:00; chưa hỗ trợ lịch qua nửa đêm.</summary>
    public static string? ValidateWindow(TimeOnly? startTime, int totalMinutes)
    {
        var start = startTime ?? ItineraryScheduler.DefaultStartTime;
        return start.ToTimeSpan().TotalMinutes + totalMinutes > MinutesPerDay
            ? "Giờ bắt đầu cộng thời lượng không được vượt 24:00 (chưa hỗ trợ lịch qua nửa đêm)."
            : null;
    }

    /// <summary>Giá trị thực sự dùng để xếp lịch: ngày mặc định hôm nay, giờ mặc định 08:00.</summary>
    public static DateTime ResolveStart(DateOnly? plannedDate, TimeOnly? startTime, DateTime vietnamNow) =>
        (plannedDate ?? DateOnly.FromDateTime(vietnamNow))
            .ToDateTime(startTime ?? ItineraryScheduler.DefaultStartTime, DateTimeKind.Unspecified);
}
