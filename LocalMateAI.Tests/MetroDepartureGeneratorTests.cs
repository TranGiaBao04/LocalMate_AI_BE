using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class MetroDepartureGeneratorTests
{
    [Fact]
    public void GenerateOriginDepartures_SwitchesHeadwayAtBandBoundary()
    {
        var service = Service(new TimeOnly(5, 0), new TimeOnly(6, 0),
            new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(5, 30), 10),
            new MetroHeadway(new TimeOnly(5, 30), new TimeOnly(6, 0), 15));

        Assert.Equal(Times("05:00", "05:10", "05:20", "05:30", "05:45", "06:00"),
            MetroDepartureGenerator.GenerateOriginDepartures(service));
    }

    [Fact]
    public void GenerateOriginDepartures_UsesHeadwayOfBandContainingCurrentTrain()
    {
        // 05:12 vẫn thuộc khung 12 phút nên chuyến sau là 05:24, rồi mới sang khung 5 phút.
        var service = Service(new TimeOnly(5, 0), new TimeOnly(5, 40),
            new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(5, 15), 12),
            new MetroHeadway(new TimeOnly(5, 15), new TimeOnly(5, 40), 5));

        Assert.Equal(Times("05:00", "05:12", "05:24", "05:29", "05:34", "05:39", "05:40"),
            MetroDepartureGenerator.GenerateOriginDepartures(service));
    }

    [Fact]
    public void GenerateOriginDepartures_AlwaysEndsWithLastDeparture()
    {
        var service = Service(new TimeOnly(5, 0), new TimeOnly(5, 25),
            new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(5, 25), 10));

        Assert.Equal(Times("05:00", "05:10", "05:20", "05:25"),
            MetroDepartureGenerator.GenerateOriginDepartures(service));
    }

    [Fact]
    public void ShiftToStation_AddsTravelMinutes()
        => Assert.Equal(Times("05:13", "05:25"),
            MetroDepartureGenerator.ShiftToStation(Times("05:00", "05:12"), 13));

    [Fact]
    public void ShiftHeadwaysToStation_ClampsLastBandAtLastDeparture()
    {
        // Khung cuối kéo tới 23:59 nhưng chuyến cuối 23:00: cắt tại 23:00 rồi +30 = 23:30, không quay vòng qua 00:29.
        var service = Service(new TimeOnly(5, 0), new TimeOnly(23, 0),
            new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(23, 59), 12));

        var headway = Assert.Single(MetroDepartureGenerator.ShiftHeadwaysToStation(service, 30));

        Assert.Equal(new MetroHeadway(new TimeOnly(5, 30), new TimeOnly(23, 30), 12), headway);
    }

    private static MetroService Service(TimeOnly first, TimeOnly last, params MetroHeadway[] headways) =>
        new([DayOfWeek.Monday], MetroDirection.TowardSuoiTien, first, last, headways);

    private static List<TimeOnly> Times(params string[] values) => values.Select(TimeOnly.Parse).ToList();
}
