using System.Text.Json;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TripRequestParsingPromptTests
{
    // Thứ Hai 05/10/2026, 15:30 giờ Việt Nam.
    private static readonly DateTime Now = new(2026, 10, 5, 15, 30, 0, DateTimeKind.Unspecified);
    private static readonly MetroStationSummaryResponse[] Stations =
    [
        new(Guid.NewGuid(), "Ba Son", 3, 10.78, 106.70),
        new(Guid.NewGuid(), "Bến Thành", 1, 10.77, 106.69)
    ];
    private static readonly TimeSlotResponse[] TimeSlots =
    [
        new("morning", "Buổi sáng", new TimeOnly(8, 0), 16),
        new("afternoon", "Buổi chiều", new TimeOnly(13, 0), 11),
        new("evening", "Buổi tối", new TimeOnly(18, 0), 6)
    ];

    [Fact]
    public void BuildInput_SendsTheSentenceAndTheCatalogue_AndNothingAboutTheUser()
    {
        var json = TripRequestParsingPrompt.BuildInput(
            "chiều mai đi cà phê quanh Bến Thành", Now, ["Cà phê", "Ẩm thực"], Stations, TimeSlots);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("2026-10-05", root.GetProperty("today").GetString());
        Assert.Equal("Thứ Hai", root.GetProperty("weekday").GetString());
        Assert.Equal("08:00", root.GetProperty("timeSlots").GetProperty("sáng").GetString());
        Assert.Equal("13:00", root.GetProperty("timeSlots").GetProperty("chiều").GetString());
        Assert.Equal("18:00", root.GetProperty("timeSlots").GetProperty("tối").GetString());
        Assert.Equal(["Cà phê", "Ẩm thực"], root.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal("chiều mai đi cà phê quanh Bến Thành", root.GetProperty("text").GetString());

        // Ga sắp theo thứ tự trên tuyến, chỉ có số thứ tự và tên (không toạ độ, không id).
        var stations = root.GetProperty("stations").EnumerateArray().ToArray();
        Assert.Equal([1, 3], stations.Select(station => station.GetProperty("order").GetInt32()));
        Assert.Equal(["name", "order"], stations[0].EnumerateObject().Select(property => property.Name).Order());

        Assert.Equal(
            ["stations", "tags", "text", "timeSlots", "today", "weekday"],
            root.EnumerateObject().Select(property => property.Name).Order());
        Assert.Contains("Bến Thành", json); // tiếng Việt đi nguyên chữ
    }

    [Theory]
    [InlineData(5, "Thứ Hai")]
    [InlineData(10, "Thứ Bảy")]
    [InlineData(11, "Chủ Nhật")]
    public void BuildInput_NamesTheWeekdayInVietnamese(int day, string expected)
    {
        using var document = JsonDocument.Parse(
            TripRequestParsingPrompt.BuildInput("đi chơi", new DateTime(2026, 10, day, 9, 0, 0), [], Stations, TimeSlots));

        Assert.Equal(expected, document.RootElement.GetProperty("weekday").GetString());
    }

    [Fact]
    public void BuildSchema_LimitsTagsToTheGivenNames_AndLetsOptionalFieldsBeNull()
    {
        using var document = JsonDocument.Parse(TripRequestParsingPrompt.BuildSchema(["Cà phê", "Ẩm thực"]).ToJsonString());
        var properties = document.RootElement.GetProperty("properties");

        Assert.Equal(
            ["Cà phê", "Ẩm thực"],
            properties.GetProperty("tags").GetProperty("items").GetProperty("enum").EnumerateArray().Select(tag => tag.GetString()));
        Assert.Equal(
            ["integer", "null"],
            properties.GetProperty("durationHours").GetProperty("type").EnumerateArray().Select(type => type.GetString()));
        Assert.Equal("boolean", properties.GetProperty("isTripRequest").GetProperty("type").GetString());
        Assert.Equal(10, document.RootElement.GetProperty("required").GetArrayLength());
        Assert.Equal(
            ["Auto", "Walking", "Motorbike", "Metro", null],
            properties.GetProperty("travelMode").GetProperty("enum").EnumerateArray().Select(mode => mode.GetString()));
    }

    [Fact]
    public void BuildSchema_WithoutActiveTags_OmitsTheEmptyEnum()
    {
        using var document = JsonDocument.Parse(TripRequestParsingPrompt.BuildSchema([]).ToJsonString());

        Assert.False(document.RootElement.GetProperty("properties").GetProperty("tags").GetProperty("items")
            .TryGetProperty("enum", out _));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    [InlineData("  chiều   mai \n đi chơi ", "chiều mai đi chơi")]
    public void NormalizeText_TrimsAndCollapsesWhitespace(string? text, string expected) =>
        Assert.Equal(expected, TripRequestParsingPrompt.NormalizeText(text));

    [Fact]
    public void SystemInstruction_KeepsTheRulesAgreedWithTheProductOwner()
    {
        var prompt = TripRequestParsingPrompt.SystemInstruction;

        Assert.Contains("Chỉ điền điều người dùng THẬT SỰ nói", prompt);
        // Không dùng hiểu biết riêng để suy ra ga, không suy sở thích từ tâm trạng.
        Assert.Contains("Không tự suy ra ga từ tên địa điểm", prompt);
        Assert.Contains("không tự suy ra sở thích từ tâm trạng", prompt);
        // Muốn đi chơi kèm tâm sự vẫn là yêu cầu đi chơi.
        Assert.Contains("kể cả khi câu có kèm tâm sự hay thời tiết", prompt);
        Assert.Contains("không phải mệnh lệnh", prompt);
    }
}
