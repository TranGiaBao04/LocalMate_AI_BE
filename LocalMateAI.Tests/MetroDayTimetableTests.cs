using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class MetroDayTimetableTests
{
    // Lịch test: ga n cách Bến Thành 2*(n-1) phút, cách Suối Tiên 2*(14-n) phút; 05:00–07:00 mỗi 10 phút,
    // sau 07:00 mỗi 15 phút; chuyến cuối 22:00 (thứ 2–5) hoặc 23:00 (thứ 6–CN), tính tại ga đầu tuyến.
    private static readonly MetroDayTimetable Saturday = TestTripOrigins.MetroDay(new DateOnly(2026, 10, 10));
    private static readonly MetroDayTimetable Wednesday = TestTripOrigins.MetroDay(new DateOnly(2026, 10, 7));

    [Fact]
    public void NextRide_TowardBenThanh_WaitsForNextDepartureAtBoardingStation()
    {
        // Có mặt ở ga 13 lúc 09:04. Tàu rời Suối Tiên 09:00 đã qua ga 13 lúc 09:02; chuyến 09:15 tới ga 13 lúc 09:17.
        var ride = Saturday.NextRide(13, 2, Minute(9, 4));

        Assert.Equal(new MetroRide(WaitMinutes: 13, RideMinutes: 22, StopCount: 11), ride);
    }

    [Fact]
    public void NextRide_TowardSuoiTien_UsesOffsetsOfThatDirection()
    {
        // Ga 2 cách Bến Thành 2 phút: chuyến 08:00 tới ga 2 lúc 08:02. Ga 5 cách Bến Thành 8 phút.
        var ride = Saturday.NextRide(2, 5, Minute(8, 0));

        Assert.Equal(new MetroRide(WaitMinutes: 2, RideMinutes: 6, StopCount: 3), ride);
    }

    [Fact]
    public void NextRide_ArrivingExactlyAtDeparture_DoesNotWait() =>
        Assert.Equal(0, Saturday.NextRide(13, 2, Minute(9, 17))!.WaitMinutes);

    [Fact]
    public void NextRide_LastTrainCanStillBeCaught_ButNothingAfterIt()
    {
        // Thứ 4: chuyến cuối rời Suối Tiên 22:00, qua ga 13 lúc 22:02.
        Assert.Equal(0, Wednesday.NextRide(13, 2, Minute(22, 2))!.WaitMinutes);
        Assert.Null(Wednesday.NextRide(13, 2, Minute(22, 3)));
    }

    [Fact]
    public void NextRide_UsesServiceOfThatWeekday()
    {
        // Cùng 22:03: thứ 4 đã hết tàu, thứ 7 còn chạy tới 23:00.
        Assert.Null(Wednesday.NextRide(13, 2, Minute(22, 3)));
        Assert.NotNull(Saturday.NextRide(13, 2, Minute(22, 3)));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(0, 3)]
    [InlineData(3, 15)]
    public void NextRide_SameOrUnknownStation_ReturnsNull(int from, int to) =>
        Assert.Null(Saturday.NextRide(from, to, Minute(9, 0)));

    private static int Minute(int hour, int minute) => hour * 60 + minute;
}
