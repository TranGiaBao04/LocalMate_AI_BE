using System.Text.Json;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

/// <summary>GĐ 5b: câu của người dùng là yêu cầu THAY ĐỔI so với tiêu chí của lịch đang xem (base).</summary>
public sealed class TripRequestParsingBaseTests
{
    // Thứ Hai 05/10/2026, 15:30 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 10, 5, 15, 30, 0, DateTimeKind.Unspecified);
    private static readonly Guid Coffee = Guid.NewGuid();
    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Photo = Guid.NewGuid();
    private static readonly Dictionary<string, Guid> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Cà phê"] = Coffee,
        ["Ẩm thực"] = Food,
        ["Chụp ảnh"] = Photo
    };
    private static readonly HashSet<int> Stations = Enumerable.Range(1, 14).ToHashSet();

    // Lịch gốc: 5 giờ, 500.000đ, tag Cà phê, quanh ga 2, ngày mai 09:00, ghi chú "yên tĩnh".
    private static readonly ParsedTripFields Base = new(
        5, 500_000m, [Coffee], TravelMode.Auto, null, 2, new DateOnly(2026, 10, 6), new TimeOnly(9, 0), "yên tĩnh");

    [Fact]
    public void FieldsTheModelLeavesNull_KeepTheBaseValues()
    {
        var answer = Parse(new { isTripRequest = true, budgetMax = 350000 });

        Assert.True(answer!.IsTripRequest);
        Assert.Equal(Base with { BudgetMax = 350_000m }, answer.Fields, FieldsComparer.Instance);
        Assert.Equal(["budgetMax"], answer.Changed);
    }

    [Fact]
    public void ModelEchoingUnchangedValues_IsNotReportedAsAChange()
    {
        // AI hay chép lại cả giá trị cũ: chỉ trường thật sự khác mới tính là đổi.
        var answer = Parse(new
        {
            isTripRequest = true,
            durationHours = 5,
            budgetMax = 350000,
            tags = new[] { "Cà phê" },
            note = "yên tĩnh"
        });

        Assert.Equal(["budgetMax"], answer!.Changed);
    }

    [Fact]
    public void InvalidValueFromTheModel_IsIgnored_AndTheBaseValueStays()
    {
        var answer = Parse(new { isTripRequest = true, durationHours = 30, destinationStationOrder = 99, plannedDate = "2020-01-01" });

        Assert.Equal(Base, answer!.Fields, FieldsComparer.Instance);
        Assert.Empty(answer.Changed);
    }

    [Fact]
    public void NullTags_KeepBaseTags_WhileAListReplacesThem_EvenAnEmptyOne()
    {
        Assert.Equal([Coffee], Parse(new { isTripRequest = true, tags = (string[]?)null })!.Fields.TagIds);

        var replaced = Parse(new { isTripRequest = true, tags = new[] { "Chụp ảnh" } })!;
        Assert.Equal([Photo], replaced.Fields.TagIds);
        Assert.Equal(["tagIds"], replaced.Changed);

        var added = Parse(new { isTripRequest = true, tags = new[] { "Ẩm thực", "Cà phê" } })!;
        Assert.Equal([Food, Coffee], added.Fields.TagIds);
        Assert.Equal(["tagIds"], added.Changed);

        var cleared = Parse(new { isTripRequest = true, tags = Array.Empty<string>() })!;
        Assert.Empty(cleared.Fields.TagIds);
        Assert.Equal(["tagIds"], cleared.Changed);
    }

    [Fact]
    public void SameTagsInAnotherOrder_AreNotAChange()
    {
        var twoTags = Base with { TagIds = [Coffee, Food] };

        var answer = Parse(new { isTripRequest = true, tags = new[] { "Ẩm thực", "Cà phê" } }, twoTags);

        Assert.Empty(answer!.Changed);
    }

    [Fact]
    public void NewStartTimeThatNoLongerFitsTheBaseDuration_KeepsTheTime_AndDropsTheDuration()
    {
        // Người dùng vừa nói "bắt đầu lúc 22h" trên lịch 5 giờ: giữ điều họ vừa nói, để họ chọn lại số giờ.
        var answer = Parse(new { isTripRequest = true, startTime = "22:00" });

        Assert.Equal(new TimeOnly(22, 0), answer!.Fields.StartTime);
        Assert.Null(answer.Fields.DurationHours);
        Assert.Equal(["durationHours", "startTime"], answer.Changed);
    }

    [Fact]
    public void MovingTheTripToToday_DropsABaseStartTimeThatHasAlreadyPassed()
    {
        // Bây giờ là 15:30: dời lịch 09:00 ngày mai về hôm nay thì 09:00 đã qua.
        var answer = Parse(new { isTripRequest = true, plannedDate = "2026-10-05" });

        Assert.Equal(new DateOnly(2026, 10, 5), answer!.Fields.PlannedDate);
        Assert.Null(answer.Fields.StartTime);
        Assert.Equal(["plannedDate", "startTime"], answer.Changed);
    }

    [Fact]
    public void BaseSentByTheClient_IsFilteredByTheSameRules()
    {
        var dirty = new ParsedTripFields(
            30, -5m, [Coffee, Guid.NewGuid()], (TravelMode)99, 15, 2, new DateOnly(2026, 9, 1), new TimeOnly(9, 0),
            new string('a', TripNoteRules.MaxLength + 1));

        var answer = Parse(new { isTripRequest = true, tags = (string[]?)null }, dirty);

        var fields = answer!.Fields;
        Assert.Null(fields.DurationHours);
        Assert.Null(fields.BudgetMax);
        Assert.Equal([Coffee], fields.TagIds); // tag không còn bật bị lọc
        Assert.Null(fields.TravelMode);
        Assert.Null(fields.StartStationOrder);
        Assert.Equal(2, fields.DestinationStationOrder);
        Assert.Null(fields.PlannedDate); // lịch cũ có ngày đã qua: người dùng phải chọn ngày mới
        Assert.Null(fields.StartTime); // không còn ngày ⇒ tính là hôm nay, 09:00 đã qua
        Assert.Null(fields.Note);
        Assert.Empty(answer.Changed); // việc lọc lịch gốc không tính là thay đổi của người dùng
    }

    [Fact]
    public void BaseWithoutTagIds_IsTreatedAsNoTags()
    {
        var answer = Parse(new { isTripRequest = true, durationHours = 4 }, Base with { TagIds = null! });

        Assert.Empty(answer!.Fields.TagIds);
        Assert.Equal(["durationHours"], answer.Changed);
    }

    [Fact]
    public void NotATripRequest_ReturnsTheBaseUntouched()
    {
        var answer = Parse(new { isTripRequest = false, budgetMax = 1 });

        Assert.False(answer!.IsTripRequest);
        Assert.Equal(Base, answer.Fields, FieldsComparer.Instance);
        Assert.Empty(answer.Changed);
    }

    [Fact]
    public void WithoutBase_ChangedIsAlwaysEmpty()
    {
        var answer = TripRequestParsingOutputParser.Parse(
            JsonSerializer.Serialize(new { isTripRequest = true, durationHours = 4 }), Tags, Stations, Now);

        Assert.Equal(4, answer!.Fields.DurationHours);
        Assert.Empty(answer.Changed);
    }

    [Fact]
    public void BuildInput_WithBase_SendsItWithTagNames_AndWithoutBase_OmitsTheField()
    {
        var names = Tags.ToDictionary(pair => pair.Value, pair => pair.Key);
        MetroStationSummaryResponse[] stations = [new(Guid.NewGuid(), "Bến Thành", 1, 10.77, 106.69)];

        using var withBase = JsonDocument.Parse(TripRequestParsingPrompt.BuildInput(
            "rẻ hơn chút", Now, ["Cà phê"], stations, [], Base, names));
        var sent = withBase.RootElement.GetProperty("base");
        Assert.Equal(5, sent.GetProperty("durationHours").GetInt32());
        Assert.Equal(500000, sent.GetProperty("budgetMax").GetInt32());
        Assert.Equal(["Cà phê"], sent.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal("Auto", sent.GetProperty("travelMode").GetString());
        Assert.Equal(JsonValueKind.Null, sent.GetProperty("startStationOrder").ValueKind);
        Assert.Equal(2, sent.GetProperty("destinationStationOrder").GetInt32());
        Assert.Equal("2026-10-06", sent.GetProperty("plannedDate").GetString());
        Assert.Equal("09:00", sent.GetProperty("startTime").GetString());
        Assert.Equal("yên tĩnh", sent.GetProperty("note").GetString());
        Assert.DoesNotContain(Coffee.ToString(), withBase.RootElement.GetRawText()); // gửi tên tag, không gửi id

        using var withoutBase = JsonDocument.Parse(TripRequestParsingPrompt.BuildInput(
            "muốn đi chơi", Now, ["Cà phê"], stations, []));
        Assert.False(withoutBase.RootElement.TryGetProperty("base", out _));
    }

    [Fact]
    public void SystemInstruction_ExplainsHowToChangeAnExistingTrip()
    {
        var prompt = TripRequestParsingPrompt.SystemInstruction;

        Assert.Contains("yêu cầu THAY ĐỔI so với base", prompt);
        Assert.Contains("giảm 30% budgetMax của base", prompt);
        Assert.Contains("\"ngắn hơn\" = bớt 1 giờ", prompt);
        Assert.Contains("ĐẦY ĐỦ sau thay đổi", prompt);
    }

    private static ParsedTripAnswer? Parse(object answer, ParsedTripFields? baseFields = null) =>
        TripRequestParsingOutputParser.Parse(JsonSerializer.Serialize(answer), Tags, Stations, Now, baseFields ?? Base);

    // ParsedTripFields chứa một danh sách nên so sánh mặc định của record không so được nội dung.
    private sealed class FieldsComparer : IEqualityComparer<ParsedTripFields>
    {
        public static readonly FieldsComparer Instance = new();

        public bool Equals(ParsedTripFields? x, ParsedTripFields? y) =>
            x is not null && y is not null
            && x.DurationHours == y.DurationHours && x.BudgetMax == y.BudgetMax
            && x.TagIds.SequenceEqual(y.TagIds) && x.TravelMode == y.TravelMode
            && x.StartStationOrder == y.StartStationOrder && x.DestinationStationOrder == y.DestinationStationOrder
            && x.PlannedDate == y.PlannedDate && x.StartTime == y.StartTime && x.Note == y.Note;

        public int GetHashCode(ParsedTripFields obj) => HashCode.Combine(obj.DurationHours, obj.BudgetMax, obj.Note);
    }
}
