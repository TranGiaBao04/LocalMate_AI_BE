using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class OpeningWindowsTests
{
    private static readonly DateOnly Sunday = new(2026, 10, 4);

    [Fact]
    public void Build_NoData_IsSixToTwentyTwoAroundThePlannedDay()
    {
        var windows = OpeningWindows.Build([], Sunday);

        Assert.Equal(
        [
            new MinuteRange(-1440 + 360, -1440 + 1320),
            new MinuteRange(360, 1320),
            new MinuteRange(1440 + 360, 1440 + 1320)
        ], windows);
    }

    [Fact]
    public void Build_ClosedDay_HasNoWindowThatDay()
    {
        IReadOnlyList<PlaceOpeningHourDto> week =
        [
            .. TestOpeningHours.EveryDay(8, 17).Where(day => day.DayOfWeek != DayOfWeek.Sunday),
            new PlaceOpeningHourDto(DayOfWeek.Sunday, true, null, null)
        ];

        var windows = OpeningWindows.Build(week, Sunday);

        Assert.False(OpeningWindows.Covers(windows, 600, 660)); // 10:00–11:00 Chủ nhật
        Assert.True(OpeningWindows.Covers(windows, 1440 + 600, 1440 + 660)); // 10:00 thứ Hai
    }

    [Fact]
    public void Build_OvernightFromThePreviousDay_CoversEarlyMorning()
    {
        // Thứ Bảy 17:00 → 02:00 Chủ nhật.
        IReadOnlyList<PlaceOpeningHourDto> week = [new(DayOfWeek.Saturday, false, new TimeOnly(17, 0), new TimeOnly(2, 0))];

        var windows = OpeningWindows.Build(week, Sunday);

        Assert.True(OpeningWindows.Covers(windows, 60, 120));  // 01:00–02:00, rời đúng giờ đóng
        Assert.False(OpeningWindows.Covers(windows, 60, 121)); // quá giờ đóng 1 phút
    }

    [Fact]
    public void Build_UnknownDaysAroundAKnownDay_KeepTheNightRule()
    {
        // Chỉ có dữ liệu Chủ nhật (mở 17:00–02:00); thứ Bảy và thứ Hai chưa có dữ liệu ⇒ 06:00–22:00.
        IReadOnlyList<PlaceOpeningHourDto> week = [new(DayOfWeek.Sunday, false, new TimeOnly(17, 0), new TimeOnly(2, 0))];

        var windows = OpeningWindows.Build(week, Sunday);

        Assert.True(OpeningWindows.Covers(windows, 1380, 1500));    // 23:00 Chủ nhật → 01:00 thứ Hai
        Assert.False(OpeningWindows.Covers(windows, 600, 660));    // 10:00 Chủ nhật: chưa mở
        Assert.True(OpeningWindows.Covers(windows, -1440 + 600, -1440 + 660)); // 10:00 thứ Bảy
    }

    [Fact]
    public void Build_AdjacentWindows_AreMerged()
    {
        // Chủ nhật 08:00–24:00, thứ Hai 00:00–02:00: chặng 23:30–00:30 vẫn nằm trọn.
        IReadOnlyList<PlaceOpeningHourDto> week =
        [
            new(DayOfWeek.Sunday, false, new TimeOnly(8, 0), new TimeOnly(0, 0)),
            new(DayOfWeek.Monday, false, new TimeOnly(0, 0), new TimeOnly(2, 0))
        ];

        var windows = OpeningWindows.Build(week, Sunday);

        Assert.True(OpeningWindows.Covers(windows, 1410, 1470));
    }

    [Fact]
    public void StatusFor_NoData_IsUnknown()
    {
        Assert.Equal(PlaceOpeningStatus.Unknown, OpeningWindows.StatusFor([], Sunday, 600, 660));
    }

    [Theory]
    [InlineData(540, 600, PlaceOpeningStatus.Open)]     // 09:00–10:00
    [InlineData(990, 1050, PlaceOpeningStatus.Closed)]  // 16:30–17:30, quá giờ đóng 17:00
    [InlineData(1380, 1440, PlaceOpeningStatus.Closed)] // 23:00
    public void StatusFor_KnownWeek(int start, int end, PlaceOpeningStatus expected)
    {
        Assert.Equal(expected, OpeningWindows.StatusFor(TestOpeningHours.EveryDay(8, 17), Sunday, start, end));
    }

    [Fact]
    public void StatusFor_ClosedDay_IsClosed()
    {
        Assert.Equal(
            PlaceOpeningStatus.Closed,
            OpeningWindows.StatusFor(TestOpeningHours.ClosedEveryDay(), Sunday, 600, 660));
    }

    [Fact]
    public void StatusFor_VisitOnADayWithoutData_IsUnknown()
    {
        // Chỉ nhập thứ Bảy; chuyến đi Chủ nhật.
        IReadOnlyList<PlaceOpeningHourDto> week = [new(DayOfWeek.Saturday, false, new TimeOnly(8, 0), new TimeOnly(17, 0))];

        Assert.Equal(PlaceOpeningStatus.Unknown, OpeningWindows.StatusFor(week, Sunday, 600, 660));
    }

    [Fact]
    public void StatusFor_OvernightFromThePreviousDay_IsOpenAfterMidnight()
    {
        // Thứ Bảy 17:00 → 02:00 Chủ nhật; chuyến đi thứ Bảy, chặng 01:00 Chủ nhật (phút 1500).
        IReadOnlyList<PlaceOpeningHourDto> week =
        [
            new(DayOfWeek.Saturday, false, new TimeOnly(17, 0), new TimeOnly(2, 0)),
            new(DayOfWeek.Sunday, true, null, null)
        ];

        Assert.Equal(PlaceOpeningStatus.Open, OpeningWindows.StatusFor(week, Sunday.AddDays(-1), 1500, 1560));
    }

    [Theory]
    [InlineData(1260, 1320, true)]  // 21:00–22:00: rời đúng 22:00
    [InlineData(1290, 1350, false)] // 21:30–22:30
    [InlineData(360, 420, true)]    // 06:00–07:00
    [InlineData(330, 390, false)]   // 05:30–06:30
    public void Covers_NoDataBoundaries(int start, int end, bool expected)
    {
        Assert.Equal(expected, OpeningWindows.Covers(OpeningWindows.Build([], Sunday), start, end));
    }
}
