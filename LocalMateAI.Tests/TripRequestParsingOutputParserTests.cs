using System.Text.Json;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripRequestParsingOutputParserTests
{
    // Thứ Hai 05/10/2026, 15:30 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 10, 5, 15, 30, 0, DateTimeKind.Unspecified);
    private static readonly Guid Coffee = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Dictionary<string, Guid> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cà phê"] = Coffee,
        ["Ẩm thực"] = Food
    };
    private static readonly HashSet<int> Stations = Enumerable.Range(1, 14).ToHashSet();

    [Fact]
    public void FullAnswer_IsMappedToFormFields()
    {
        var answer = Parse(new
        {
            isTripRequest = true,
            durationHours = 4,
            budgetMax = 300000,
            tags = new[] { "Cà phê", "ẩm thực", "Cà phê" },
            travelMode = "metro",
            startStationOrder = 11,
            destinationStationOrder = 3,
            plannedDate = "2026-10-06",
            startTime = "13:00",
            note = "  yên tĩnh,   có view sông "
        });

        Assert.True(answer!.IsTripRequest);
        var fields = answer.Fields;
        Assert.Equal(4, fields.DurationHours);
        Assert.Equal(300_000m, fields.BudgetMax);
        Assert.Equal([Coffee, Food], fields.TagIds); // tên không phân biệt hoa thường, bỏ trùng
        Assert.Equal(TravelMode.Metro, fields.TravelMode);
        Assert.Equal(11, fields.StartStationOrder);
        Assert.Equal(3, fields.DestinationStationOrder);
        Assert.Equal(new DateOnly(2026, 10, 6), fields.PlannedDate);
        Assert.Equal(new TimeOnly(13, 0), fields.StartTime);
        Assert.Equal("yên tĩnh, có view sông", fields.Note);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("")]
    public void MalformedAnswer_ReturnsNull(string json) =>
        Assert.Null(TripRequestParsingOutputParser.Parse(json, Tags, Stations, Now));

    [Fact]
    public void NotATripRequest_DropsEveryField_EvenIfTheModelFilledSome()
    {
        var answer = Parse(new { isTripRequest = false, durationHours = 4, tags = new[] { "Cà phê" }, note = "buồn" });

        Assert.False(answer!.IsTripRequest);
        Assert.False(answer.Fields.HasAnyValue);
    }

    [Fact]
    public void TripRequestWithoutAnyField_StaysATripRequest()
    {
        var answer = Parse(new { isTripRequest = true, tags = Array.Empty<string>() });

        Assert.True(answer!.IsTripRequest);
        Assert.False(answer.Fields.HasAnyValue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(30)]
    [InlineData(-2)]
    public void DurationOutsideOneToTwentyFour_IsDropped(int hours) =>
        Assert.Null(Parse(new { isTripRequest = true, durationHours = hours })!.Fields.DurationHours);

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    public void DurationThatFitsBeforeMidnight_IsKept(int hours) =>
        Assert.Equal(hours, Parse(new { isTripRequest = true, durationHours = hours })!.Fields.DurationHours);

    [Fact]
    public void StartTimePlusDurationPastMidnight_KeepsStartTime_AndDropsDuration()
    {
        var fields = Parse(new { isTripRequest = true, durationHours = 5, startTime = "22:00" })!.Fields;

        Assert.Equal(new TimeOnly(22, 0), fields.StartTime);
        Assert.Null(fields.DurationHours);
    }

    [Fact]
    public void DurationTooLongForTheDefaultStart_IsDropped()
    {
        // Không có giờ xuất phát thì form tính từ 08:00: 17 tiếng sẽ vượt 24:00.
        Assert.Null(Parse(new { isTripRequest = true, durationHours = 17 })!.Fields.DurationHours);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100_000_001)]
    public void BudgetOutOfRange_IsDropped(long budget) =>
        Assert.Null(Parse(new { isTripRequest = true, budgetMax = budget })!.Fields.BudgetMax);

    [Fact]
    public void BudgetWithFraction_IsDropped_AndZeroIsKept()
    {
        Assert.Null(Parse(new { isTripRequest = true, budgetMax = 300000.5 })!.Fields.BudgetMax);
        Assert.Equal(0m, Parse(new { isTripRequest = true, budgetMax = 0 })!.Fields.BudgetMax);
    }

    [Fact]
    public void UnknownTagsModesAndStations_AreDropped()
    {
        var fields = Parse(new
        {
            isTripRequest = true,
            tags = new object[] { "Leo núi", 42, "Cà phê" },
            travelMode = "Helicopter",
            startStationOrder = 15,
            destinationStationOrder = 0
        })!.Fields;

        Assert.Equal([Coffee], fields.TagIds);
        Assert.Null(fields.TravelMode);
        Assert.Null(fields.StartStationOrder);
        Assert.Null(fields.DestinationStationOrder);
    }

    [Theory]
    [InlineData("2020-01-01")] // quá khứ
    [InlineData("2026-10-04")] // hôm qua
    [InlineData("2027-02-01")] // quá 90 ngày
    [InlineData("06/10/2026")] // sai định dạng
    [InlineData("mai")]
    public void InvalidPlannedDate_IsDropped(string date) =>
        Assert.Null(Parse(new { isTripRequest = true, plannedDate = date })!.Fields.PlannedDate);

    [Fact]
    public void TodayAndNinetyDaysAhead_AreKept()
    {
        Assert.Equal(new DateOnly(2026, 10, 5), Parse(new { isTripRequest = true, plannedDate = "2026-10-05" })!.Fields.PlannedDate);
        Assert.Equal(new DateOnly(2027, 1, 3), Parse(new { isTripRequest = true, plannedDate = "2027-01-03" })!.Fields.PlannedDate);
    }

    [Fact]
    public void StartTimeAlreadyPassedToday_IsDropped_ButKeptForAnotherDay()
    {
        // Bây giờ là 15:30: "buổi sáng" hôm nay (hoặc không nói ngày) đã qua.
        Assert.Null(Parse(new { isTripRequest = true, startTime = "08:00" })!.Fields.StartTime);
        Assert.Null(Parse(new { isTripRequest = true, plannedDate = "2026-10-05", startTime = "08:00" })!.Fields.StartTime);
        Assert.Equal(
            new TimeOnly(8, 0),
            Parse(new { isTripRequest = true, plannedDate = "2026-10-06", startTime = "08:00" })!.Fields.StartTime);
        Assert.Equal(new TimeOnly(18, 0), Parse(new { isTripRequest = true, startTime = "18:00" })!.Fields.StartTime);
    }

    [Theory]
    [InlineData("25:00")]
    [InlineData("chiều")]
    [InlineData("13h")]
    public void MalformedStartTime_IsDropped(string time) =>
        Assert.Null(Parse(new { isTripRequest = true, plannedDate = "2026-10-06", startTime = time })!.Fields.StartTime);

    [Fact]
    public void SingleDigitHour_IsAccepted() =>
        Assert.Equal(
            new TimeOnly(9, 5),
            Parse(new { isTripRequest = true, plannedDate = "2026-10-06", startTime = "9:05" })!.Fields.StartTime);

    [Fact]
    public void BlankOrTooLongNote_IsDropped()
    {
        Assert.Null(Parse(new { isTripRequest = true, note = "   " })!.Fields.Note);
        Assert.Null(Parse(new { isTripRequest = true, note = new string('a', TripNoteRules.MaxLength + 1) })!.Fields.Note);
    }

    [Fact]
    public void Missing_ListsWhatTheFormStillNeeds()
    {
        Assert.Equal(
            ["durationHours", "budgetMax", "startLocation"],
            TripRequestParsingOutputParser.Missing(ParsedTripFields.Empty));
        Assert.Empty(TripRequestParsingOutputParser.Missing(
            ParsedTripFields.Empty with { DurationHours = 4, BudgetMax = 0m, StartStationOrder = 1 }));
        // Ga muốn chơi không thay cho điểm xuất phát.
        Assert.Equal(
            ["startLocation"],
            TripRequestParsingOutputParser.Missing(
                ParsedTripFields.Empty with { DurationHours = 4, BudgetMax = 300_000m, DestinationStationOrder = 3 }));
    }

    private static ParsedTripAnswer? Parse(object answer) =>
        TripRequestParsingOutputParser.Parse(JsonSerializer.Serialize(answer), Tags, Stations, Now);
}
