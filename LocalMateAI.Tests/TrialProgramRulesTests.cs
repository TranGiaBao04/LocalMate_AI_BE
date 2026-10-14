using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TrialProgramRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Theory]
    [InlineData(false, 0, 99, null, 0, TrialProgramStatus.Disabled)]  // đang tắt thắng mọi trạng thái khác
    [InlineData(true, -10, -1, null, 0, TrialProgramStatus.Ended)]
    [InlineData(true, 1, 10, null, 0, TrialProgramStatus.Upcoming)]
    [InlineData(true, 0, 0, null, 0, TrialProgramStatus.Running)]     // một ngày, tính cả hai đầu
    [InlineData(true, -5, 5, 100, 99, TrialProgramStatus.Running)]
    [InlineData(true, -5, 5, 100, 100, TrialProgramStatus.Full)]
    [InlineData(true, -5, 5, 50, 80, TrialProgramStatus.Full)]        // admin hạ suất xuống dưới số đã nhận
    [InlineData(true, -10, -1, 10, 10, TrialProgramStatus.Ended)]     // đã kết thúc thắng "hết suất"
    public void Status_FollowsTheDocumentedOrder(bool enabled, int startOffset, int endOffset, int? max, int granted,
        TrialProgramStatus expected) =>
        Assert.Equal(expected, TrialProgramRules.Status(
            Program(enabled, Today.AddDays(startOffset), Today.AddDays(endOffset), max, granted), Today));

    [Theory]
    [InlineData(null, 37, null)]
    [InlineData(100, 37, 63)]
    [InlineData(50, 80, 0)]
    public void Remaining_IsNullWithoutACap_AndNeverNegative(int? max, int granted, int? expected) =>
        Assert.Equal(expected, TrialProgramRules.ToResponse(Program(true, Today, Today, max, granted), Today).Remaining);

    [Theory]
    [InlineData(16, 59, 14)] // 23:59 giờ VN ngày 14
    [InlineData(17, 0, 15)]  // 00:00 giờ VN ngày 15
    public void Today_IsTheVietnamCalendarDate(int utcHour, int utcMinute, int expectedDay) =>
        Assert.Equal(new DateOnly(2026, 10, expectedDay),
            TrialProgramRules.Today(new FixedClock(new DateTimeOffset(2026, 10, 14, utcHour, utcMinute, 0, TimeSpan.Zero))));

    private static TrialProgramReadModel Program(bool enabled, DateOnly startsOn, DateOnly endsOn, int? max, int granted) =>
        new(Guid.NewGuid(), "Khai trương", startsOn, endsOn, 7, max, enabled, granted, DateTime.UtcNow, DateTime.UtcNow);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
