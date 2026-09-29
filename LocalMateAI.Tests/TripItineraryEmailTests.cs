using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Email;

namespace LocalMateAI.Tests;

public sealed class TripItineraryEmailTests
{
    private static readonly User Traveller = new() { FullName = "Nguyễn <An>", Email = "an@example.com" };

    [Theory]
    [InlineData(0, "0 phút")]
    [InlineData(45, "45 phút")]
    [InlineData(60, "1 giờ")]
    [InlineData(145, "2 giờ 25 phút")]
    public void Duration_FormatsHoursAndMinutes(int minutes, string expected) =>
        Assert.Equal(expected, EmailDisplayFormat.Duration(minutes));

    [Fact]
    public void Build_PlannedTrip_FormatsHeaderAndStops()
    {
        var trip = SampleTrip(new DateTime(2026, 10, 1, 8, 0, 0));

        var entry = TripItineraryEmailBuilder.Build(Traveller, trip, TripDetailService.ToResponse(trip));

        Assert.Equal("Lịch trình chuyến đi ngày 01/10/2026 - LocalMate AI", entry.Subject);
        Assert.Equal($"trip-itinerary:{trip.Id}", entry.DeduplicationKey);
        var model = Deserialize(entry);
        Assert.Equal("Thứ Năm, 01/10/2026", model.PlannedDate);
        Assert.Equal("08:00 – 10:25", model.TimeRange);
        Assert.Equal("2 giờ 25 phút", model.TotalDuration); // tham quan 120 + đi 15 (tới chặng đầu) + 10
        Assert.Equal("50.000đ / người", model.EstimatedBudget);
        Assert.Equal("Đi bộ", model.TravelModeLabel);
        Assert.Equal("2 chặng", model.StopCount);
        Assert.Equal(
            new TripItineraryStopEmailModel(
                "1", "08:15", "Bột chiên Cô Mẽ", "Ẩm thực", "10 Phan Bội Châu, Bến Thành",
                "Bến Thành", "1 giờ 15 phút", "50.000đ", "15 phút di chuyển từ điểm xuất phát"),
            model.Stops[0]);
        Assert.Equal(
            new TripItineraryStopEmailModel(
                "2", "09:40", "Chợ Bến Thành", "Check-in", "Lê Lai, Bến Thành",
                "Bến Thành", "45 phút", "Miễn phí", "10 phút di chuyển từ chặng trước"),
            model.Stops[1]);
    }

    [Fact]
    public void Build_TripWithoutPlannedDate_UsesFallbacks()
    {
        var trip = SampleTrip(plannedStartAt: null);

        var entry = TripItineraryEmailBuilder.Build(Traveller, trip, TripDetailService.ToResponse(trip));

        Assert.Equal("Lịch trình chuyến đi của bạn - LocalMate AI", entry.Subject);
        var model = Deserialize(entry);
        Assert.Equal("Chưa đặt ngày", model.PlannedDate);
        Assert.Equal("08:15 – 10:25", model.TimeRange);
        Assert.Equal(string.Empty, model.Stops[0].TravelNote);
    }

    [Fact]
    public async Task Render_RealTemplate_ShowsStopsAndEncodesHtml()
    {
        var trip = SampleTrip(new DateTime(2026, 10, 1, 8, 0, 0));
        var entry = TripItineraryEmailBuilder.Build(Traveller, trip, TripDetailService.ToResponse(trip));
        var model = EmailOutboxModelRegistry.Default.Deserialize(entry.TemplateName, entry.ModelJson)!;

        var html = await new FluidEmailTemplateRenderer().RenderAsync(entry.TemplateName, model);

        Assert.Contains("Thứ Năm, 01/10/2026", html);
        Assert.Contains("Bột chiên Cô Mẽ", html);
        Assert.Contains("10 Phan Bội Châu, Bến Thành", html);
        Assert.Contains("10 phút di chuyển từ chặng trước", html);
        Assert.Contains("Miễn phí", html);
        Assert.Contains("Ga gần điểm xuất phát", html);
        Assert.Contains("Nguyễn &lt;An&gt;", html);
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("{%", html);
    }

    private static TripItineraryEmailModel Deserialize(EmailOutboxEntry entry) =>
        Assert.IsType<TripItineraryEmailModel>(
            EmailOutboxModelRegistry.Default.Deserialize(entry.TemplateName, entry.ModelJson));

    private static TripDetailReadModel SampleTrip(DateTime? plannedStartAt) =>
        new(
            Guid.NewGuid(),
            TripStatus.Finalized,
            10.7726,
            106.6980,
            "Bến Thành",
            3,
            0,
            300_000,
            [],
            [
                Item(1, "Bột chiên Cô Mẽ", PlaceCategory.Food, "10 Phan Bội Châu, Bến Thành",
                    new TimeOnly(8, 15), 75, 50_000, 10.77264, 106.69876),
                Item(2, "Chợ Bến Thành", PlaceCategory.CheckIn, "Lê Lai, Bến Thành",
                    new TimeOnly(9, 40), 45, 0, 10.77252, 106.69801)
            ],
            DateTime.UtcNow,
            DateTime.UtcNow,
            DateTime.UtcNow,
            TravelMode.Walking,
            plannedStartAt);

    private static TripItemReadModel Item(
        int order, string name, PlaceCategory category, string address,
        TimeOnly time, int duration, decimal cost, double latitude, double longitude) =>
        new(
            Guid.NewGuid(), Guid.NewGuid(), name, category, null, latitude, longitude, "Bến Thành",
            order, time, duration, cost, null, false, null, Address: address);
}
