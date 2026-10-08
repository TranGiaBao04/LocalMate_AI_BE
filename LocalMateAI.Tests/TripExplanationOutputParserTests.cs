using System.Text.Json;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TripExplanationOutputParserTests
{
    private static readonly Guid Cafe = Guid.NewGuid();
    private static readonly Guid Park = Guid.NewGuid();
    private static readonly Guid[] Expected = [Cafe, Park];

    [Fact]
    public void ValidAnswer_ReturnsOneReasonPerPlace()
    {
        var reasons = TripExplanationOutputParser.Parse(
            Answer(Stop(Cafe, true, "Quán yên tĩnh, hợp ngồi làm việc."), Stop(Park, true, "Công viên thoáng mát.")),
            Expected);

        Assert.Equal("Quán yên tĩnh, hợp ngồi làm việc.", reasons![Cafe]);
        Assert.Equal("Công viên thoáng mát.", reasons[Park]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"stops\":\"none\"}")]
    [InlineData("")]
    public void MalformedAnswer_ReturnsNull(string json) =>
        Assert.Null(TripExplanationOutputParser.Parse(json, Expected));

    [Fact]
    public void NotEnoughData_BecomesTheFixedPoliteSentence_WhateverTheModelWrote()
    {
        var reasons = TripExplanationOutputParser.Parse(
            Answer(Stop(Cafe, false, ""), Stop(Park, false, "Nơi này nổi tiếng với kiến trúc Pháp.")),
            Expected);

        Assert.Equal(TripExplanationPrompt.InsufficientDataReason, reasons![Cafe]);
        Assert.Equal(TripExplanationPrompt.InsufficientDataReason, reasons[Park]);
    }

    [Fact]
    public void UnknownPlace_IsDropped_AndRepeatedPlaceKeepsTheFirstEntry()
    {
        var reasons = TripExplanationOutputParser.Parse(
            Answer(
                Stop(Guid.NewGuid(), true, "Địa điểm AI tự thêm."),
                Stop(Cafe, true, "Lý do đầu."),
                Stop(Cafe, true, "Lý do lặp lại.")),
            Expected);

        Assert.Equal([Cafe], reasons!.Keys);
        Assert.Equal("Lý do đầu.", reasons[Cafe]);
    }

    [Fact]
    public void EmptyOrTooLongReason_IsDropped_ButOthersAreKept()
    {
        var tooLong = new string('a', TripExplanationPrompt.MaxReasonLength + 1);

        var reasons = TripExplanationOutputParser.Parse(
            Answer(Stop(Cafe, true, "   "), Stop(Park, true, tooLong)),
            Expected);

        Assert.Empty(reasons!);

        var atLimit = new string('a', TripExplanationPrompt.MaxReasonLength);
        Assert.Equal(atLimit, TripExplanationOutputParser.Parse(Answer(Stop(Cafe, true, atLimit)), Expected)![Cafe]);
    }

    [Fact]
    public void Reason_IsCleanedOfMarkdownAndLineBreaks()
    {
        var reasons = TripExplanationOutputParser.Parse(
            Answer(Stop(Cafe, true, "**Quán** yên tĩnh,\n\n  hợp `làm việc`.\t# ok")),
            Expected);

        Assert.Equal("Quán yên tĩnh, hợp làm việc. ok", reasons![Cafe]);
    }

    [Fact]
    public void EntriesWithWrongTypes_AreSkipped()
    {
        var json = $$"""
            {"stops":[
              "not an object",
              {"placeId": 123, "hasEnoughData": true, "reason": "sai kiểu id"},
              {"placeId": "not-a-guid", "hasEnoughData": true, "reason": "id hỏng"},
              {"placeId": "{{Cafe}}", "hasEnoughData": true, "reason": 42},
              {"placeId": "{{Park}}", "reason": "Thiếu cờ vẫn nhận nếu có lý do."}
            ]}
            """;

        var reasons = TripExplanationOutputParser.Parse(json, Expected);

        Assert.Equal([Park], reasons!.Keys);
    }

    private static object Stop(Guid placeId, bool hasEnoughData, string reason) =>
        new { placeId = placeId.ToString(), hasEnoughData, reason };

    private static string Answer(params object[] stops) => JsonSerializer.Serialize(new { stops });
}
