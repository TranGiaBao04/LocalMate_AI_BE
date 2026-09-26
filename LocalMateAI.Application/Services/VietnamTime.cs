namespace LocalMateAI.Application.Services;

/// <summary>Giờ Việt Nam (UTC+7 cố định, VN không có giờ mùa hè). Nhận TimeProvider để test được.</summary>
public static class VietnamTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(7);

    public static DateTime Now(TimeProvider clock) =>
        DateTime.SpecifyKind(clock.GetUtcNow().UtcDateTime + Offset, DateTimeKind.Unspecified);
}
