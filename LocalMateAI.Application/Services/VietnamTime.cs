namespace LocalMateAI.Application.Services;

/// <summary>Giờ Việt Nam (UTC+7 cố định, VN không có giờ mùa hè). Nhận TimeProvider để test được.</summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTime Now(TimeProvider clock) =>
        DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime + Offset, DateTimeKind.Unspecified);

    /// <summary>00:00 hôm nay theo giờ Việt Nam, đổi sang UTC. Dùng cho các hạn mức tính theo ngày.</summary>
    public static DateTime StartOfTodayUtc(TimeProvider clock) =>
        StartOfDayUtc(clock.GetUtcNow().UtcDateTime);

    public static DateTime StartOfDayUtc(DateTime nowUtc) =>
        DateTime.SpecifyKind((nowUtc + Offset).Date - Offset, DateTimeKind.Utc);
}
