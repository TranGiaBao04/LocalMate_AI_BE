using System.Text.Json;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripExplanationPromptTests
{
    private static readonly Guid Cafe = Guid.NewGuid();
    private static readonly Guid Park = Guid.NewGuid();

    [Fact]
    public void BuildInput_SendsOnlyWhatTheModelNeeds()
    {
        var trip = Trip(note: "muốn chỗ yên tĩnh", interestTags: ["Cà phê"],
            Stop(Cafe, "Tonkin", PlaceCategory.Cafe, "  Không gian yên tĩnh  ", ["Cà phê", "Chụp ảnh"], new TimeOnly(14, 5), 60),
            Stop(Park, "Công viên", PlaceCategory.CheckIn, "   ", [], new TimeOnly(18, 0), 45));

        var json = TripExplanationPrompt.BuildInput(trip, trip.Stops);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var criteria = root.GetProperty("criteria");
        Assert.Equal(4, criteria.GetProperty("durationHours").GetInt32());
        Assert.Equal("muốn chỗ yên tĩnh", criteria.GetProperty("note").GetString());
        Assert.Equal("Cà phê", criteria.GetProperty("interestTags")[0].GetString());

        var first = root.GetProperty("stops")[0];
        Assert.Equal(Cafe.ToString(), first.GetProperty("placeId").GetString());
        Assert.Equal("Tonkin", first.GetProperty("name").GetString());
        Assert.Equal("Quán cà phê", first.GetProperty("category").GetString());
        Assert.Equal("Không gian yên tĩnh", first.GetProperty("description").GetString());
        Assert.Equal("buổi chiều", first.GetProperty("partOfDay").GetString());
        Assert.Equal(60, first.GetProperty("visitMinutes").GetInt32());
        Assert.Equal(2, first.GetProperty("tags").GetArrayLength());

        var second = root.GetProperty("stops")[1];
        Assert.Equal(JsonValueKind.Null, second.GetProperty("description").ValueKind);
        Assert.Equal("buổi tối", second.GetProperty("partOfDay").GetString());

        // Chỉ đúng các trường này: không giá tiền, giờ cụ thể, toạ độ hay thông tin người dùng.
        Assert.Equal(
            ["category", "description", "name", "partOfDay", "placeId", "tags", "visitMinutes"],
            first.EnumerateObject().Select(property => property.Name).Order());
        Assert.Equal(
            ["durationHours", "interestTags", "note"],
            criteria.EnumerateObject().Select(property => property.Name).Order());
        Assert.DoesNotContain("14:05", json);
        // Tiếng Việt đi nguyên chữ, không bị đổi thành \uXXXX.
        Assert.Contains("yên tĩnh", json);
    }

    [Fact]
    public void BuildInput_WithoutNote_SendsNullNote()
    {
        var trip = Trip(note: null, interestTags: [], Stop(Cafe, "Tonkin", PlaceCategory.Cafe, "Yên tĩnh", [], new TimeOnly(9, 0), 60));

        using var document = JsonDocument.Parse(TripExplanationPrompt.BuildInput(trip, trip.Stops));

        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("criteria").GetProperty("note").ValueKind);
    }

    [Theory]
    [InlineData(0, "buổi sáng")]
    [InlineData(10, "buổi sáng")]
    [InlineData(11, "buổi trưa")]
    [InlineData(13, "buổi trưa")]
    [InlineData(14, "buổi chiều")]
    [InlineData(17, "buổi chiều")]
    [InlineData(18, "buổi tối")]
    [InlineData(23, "buổi tối")]
    public void PartOfDay_FollowsTheHour(int hour, string expected) =>
        Assert.Equal(expected, TripExplanationPrompt.PartOfDay(new TimeOnly(hour, 59)));

    [Fact]
    public void BuildSchema_OnlyAllowsTheGivenPlaces_AndRequiresTheDataFlag()
    {
        using var document = JsonDocument.Parse(TripExplanationPrompt.BuildSchema([Cafe, Park]).ToJsonString());
        var item = document.RootElement.GetProperty("properties").GetProperty("stops").GetProperty("items");

        Assert.Equal(
            [Cafe.ToString(), Park.ToString()],
            item.GetProperty("properties").GetProperty("placeId").GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            ["placeId", "hasEnoughData", "reason"],
            item.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("boolean", item.GetProperty("properties").GetProperty("hasEnoughData").GetProperty("type").GetString());
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("   ", false, false)]
    [InlineData("Quán yên tĩnh", false, true)]
    [InlineData(null, true, true)]
    public void HasDescribableData_NeedsADescriptionOrATag(string? description, bool hasTag, bool expected)
    {
        var stop = Stop(Cafe, "Quán", PlaceCategory.Cafe, description, hasTag ? ["Cà phê"] : [], new TimeOnly(9, 0), 60);

        Assert.Equal(expected, TripExplanationPrompt.HasDescribableData(stop));
    }

    [Fact]
    public void SystemInstruction_KeepsTheDataOnlyRules()
    {
        // Hai luật Bảo yêu cầu siết: chỉ dùng dữ liệu của mình, thiếu dữ liệu thì nói là thiếu.
        Assert.Contains("NGUỒN THÔNG TIN DUY NHẤT", TripExplanationPrompt.SystemInstruction);
        Assert.Contains("kể cả khi đó là nơi nổi tiếng", TripExplanationPrompt.SystemInstruction);
        Assert.Contains("hasEnoughData = false", TripExplanationPrompt.SystemInstruction);
        Assert.InRange(TripExplanationPrompt.InsufficientDataReason.Length, 1, TripExplanationPrompt.MaxReasonLength);
    }

    private static TripExplanationReadModel Trip(
        string? note, string[] interestTags, params TripExplanationStopReadModel[] stops) =>
        new(Guid.NewGuid(), TripStatus.Draft, 4, note, interestTags, stops);

    private static TripExplanationStopReadModel Stop(
        Guid placeId, string name, PlaceCategory category, string? description, string[] tags, TimeOnly time, int minutes) =>
        new(Guid.NewGuid(), placeId, name, category, description, tags, time, minutes);
}
