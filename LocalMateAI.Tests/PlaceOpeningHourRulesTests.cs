using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class PlaceOpeningHourRulesTests
{
    private static readonly TimeOnly Open = new(17, 0);
    private static readonly TimeOnly Close = new(2, 0);

    [Fact]
    public void Validate_ValidDays_ReturnsThemMondayFirst()
    {
        var (days, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest(
        [
            new SavePlaceOpeningHourItem(DayOfWeek.Sunday, false, Open, Close),
            new SavePlaceOpeningHourItem(DayOfWeek.Monday, true, null, null),
            new SavePlaceOpeningHourItem(DayOfWeek.Friday, false, new TimeOnly(8, 0), new TimeOnly(17, 0))
        ]));

        Assert.Empty(errors);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Sunday], days!.Select(day => day.DayOfWeek));
        Assert.Equal(new PlaceOpeningHourDto(DayOfWeek.Sunday, false, Open, Close), days![2]); // mở qua đêm hợp lệ
    }

    [Fact]
    public void Validate_EmptyList_IsValidAndClearsEverything()
    {
        var (days, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest([]));

        Assert.Empty(errors);
        Assert.Empty(days!);
    }

    [Fact]
    public void Validate_MissingList_FailsOnDays()
    {
        var (days, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest(null));

        Assert.Null(days);
        Assert.True(errors.ContainsKey("Days"));
    }

    [Fact]
    public void Validate_MoreThanSevenRows_FailsOnDays()
    {
        var rows = Enumerable.Range(0, 8)
            .Select(_ => new SavePlaceOpeningHourItem(DayOfWeek.Monday, true, null, null))
            .ToList();

        var (days, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest(rows));

        Assert.Null(days);
        Assert.True(errors.ContainsKey("Days"));
    }

    [Fact]
    public void Validate_DuplicateDay_FailsOnThatRow()
    {
        var (days, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest(
        [
            new SavePlaceOpeningHourItem(DayOfWeek.Monday, true, null, null),
            new SavePlaceOpeningHourItem(DayOfWeek.Monday, false, Open, Close)
        ]));

        Assert.Null(days);
        Assert.Equal(["Days[1].DayOfWeek"], errors.Keys);
    }

    [Fact]
    public void Validate_MissingDayOrIsClosed_FailsOnThatField()
    {
        var (_, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest(
        [
            new SavePlaceOpeningHourItem(null, false, Open, Close),
            new SavePlaceOpeningHourItem(DayOfWeek.Monday, null, Open, Close)
        ]));

        Assert.Equal(["Days[0].DayOfWeek", "Days[1].IsClosed"], errors.Keys.Order());
    }

    [Theory]
    [InlineData(true, 8, 17)]     // đóng cả ngày mà có giờ
    [InlineData(false, null, 17)] // thiếu giờ mở
    [InlineData(false, 8, null)]  // thiếu giờ đóng
    [InlineData(false, 8, 8)]     // giờ mở bằng giờ đóng
    public void Validate_InconsistentRow_FailsOnTheRow(bool isClosed, int? openHour, int? closeHour)
    {
        var (days, errors) = PlaceOpeningHourRules.Validate(new SavePlaceOpeningHoursRequest(
        [
            new SavePlaceOpeningHourItem(
                DayOfWeek.Tuesday,
                isClosed,
                openHour is { } open ? new TimeOnly(open, 0) : null,
                closeHour is { } close ? new TimeOnly(close, 0) : null)
        ]));

        Assert.Null(days);
        Assert.Equal(["Days[0]"], errors.Keys);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseImportText_Blank_HasNoHoursAndNoWarning(string? raw)
    {
        var (days, warning) = PlaceOpeningHourRules.ParseImportText(raw);

        Assert.Empty(days);
        Assert.Null(warning);
    }

    [Theory]
    [InlineData("06:00-21:00")]
    [InlineData("6:00 – 21:00")]
    [InlineData("Mon-Sun 06:00-21:00")]
    [InlineData("mon-sun 06:00-21:00")]
    public void ParseImportText_WholeWeek_OpensEveryDay(string raw)
    {
        var (days, warning) = PlaceOpeningHourRules.ParseImportText(raw);

        Assert.Null(warning);
        Assert.Equal(7, days.Count);
        Assert.All(days, day =>
        {
            Assert.False(day.IsClosed);
            Assert.Equal(new TimeOnly(6, 0), day.OpenTime);
            Assert.Equal(new TimeOnly(21, 0), day.CloseTime);
        });
    }

    [Fact]
    public void ParseImportText_DayRange_ClosesTheOtherDays()
    {
        var (days, warning) = PlaceOpeningHourRules.ParseImportText("Tue-Sun 08:00-17:00");

        Assert.Null(warning);
        Assert.Equal(new PlaceOpeningHourDto(DayOfWeek.Monday, true, null, null), days[0]);
        Assert.All(days.Skip(1), day => Assert.Equal(new TimeOnly(8, 0), day.OpenTime));
    }

    [Fact]
    public void ParseImportText_RangeWrappingPastSunday_AndOvernightHours()
    {
        var (days, warning) = PlaceOpeningHourRules.ParseImportText("Fri-Mon 18:00-02:00");

        Assert.Null(warning);
        var open = days.Where(day => !day.IsClosed).Select(day => day.DayOfWeek);
        Assert.Equal([DayOfWeek.Monday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday], open);
        Assert.All(days.Where(day => !day.IsClosed), day => Assert.Equal(new TimeOnly(2, 0), day.CloseTime));
    }

    [Fact]
    public void ParseImportText_SingleDay_OpensOnlyThatDay()
    {
        var (days, _) = PlaceOpeningHourRules.ParseImportText("Sat 07:00-11:00");

        Assert.Equal(DayOfWeek.Saturday, Assert.Single(days, day => !day.IsClosed).DayOfWeek);
        Assert.Equal(6, days.Count(day => day.IsClosed));
    }

    [Theory]
    [InlineData("8h-22h")]
    [InlineData("abc")]
    [InlineData("08:00-08:00")]
    [InlineData("08:00")]
    [InlineData("Thu2-CN 08:00-17:00")]
    [InlineData("Mon-Tue-Wed 08:00-17:00")]
    [InlineData("25:00-26:00")]
    public void ParseImportText_Unreadable_HasNoHoursAndWarns(string raw)
    {
        var (days, warning) = PlaceOpeningHourRules.ParseImportText(raw);

        Assert.Empty(days);
        Assert.NotNull(warning);
        Assert.Contains(raw, warning);
    }

    [Fact]
    public void ToResponse_FillsAllSevenDaysMondayFirst()
    {
        var response = PlaceOpeningHourRules.ToResponse(
            Guid.NewGuid(),
            [new PlaceOpeningHourDto(DayOfWeek.Wednesday, false, Open, Close)]);

        Assert.True(response.HasData);
        Assert.Equal(PlaceOpeningHourRules.WeekOrder, response.Days.Select(day => day.DayOfWeek));
        Assert.Equal(Open, response.Days[2].OpenTime);
        Assert.All(response.Days.Where(day => day.DayOfWeek != DayOfWeek.Wednesday), day =>
        {
            Assert.False(day.IsClosed);
            Assert.Null(day.OpenTime);
        });
    }

    [Fact]
    public void ToResponse_NoRows_HasNoData()
    {
        var response = PlaceOpeningHourRules.ToResponse(Guid.NewGuid(), []);

        Assert.False(response.HasData);
        Assert.Equal(7, response.Days.Count);
    }
}
