namespace LocalMateAI.Application.Services;

public sealed record VietnamMonthWindow(DateTime StartUtc, DateTime NextStartUtc)
{
    private static readonly TimeZoneInfo VietnamTimeZone =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");

    public static VietnamMonthWindow For(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, VietnamTimeZone);
        var startLocal = new DateTime(local.Year, local.Month, 1);
        var nextStartLocal = startLocal.AddMonths(1);

        return new VietnamMonthWindow(
            TimeZoneInfo.ConvertTimeToUtc(startLocal, VietnamTimeZone),
            TimeZoneInfo.ConvertTimeToUtc(nextStartLocal, VietnamTimeZone));
    }
}
