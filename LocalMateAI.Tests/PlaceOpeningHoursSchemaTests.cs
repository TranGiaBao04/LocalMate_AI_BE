using LocalMateAI.Domain.Entities;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class PlaceOpeningHoursSchemaTests
{
    [Fact]
    public void PlaceOpeningHour_DefaultValues_AreCorrect()
    {
        var placeId = Guid.NewGuid();
        var openingHour = new PlaceOpeningHour
        {
            PlaceId = placeId,
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(22, 0)
        };

        Assert.NotEqual(Guid.Empty, openingHour.Id);
        Assert.Equal(placeId, openingHour.PlaceId);
        Assert.Equal(DayOfWeek.Monday, openingHour.DayOfWeek);
        Assert.Equal(new TimeOnly(8, 0), openingHour.OpenTime);
        Assert.Equal(new TimeOnly(22, 0), openingHour.CloseTime);
        Assert.False(openingHour.IsClosed);
    }

    [Fact]
    public void Place_OpeningHoursCollection_IsInitialized()
    {
        var place = new Place
        {
            Name = "Quán Cà Phê A",
            Address = "123 Nguyễn Huệ",
            Location = new NetTopologySuite.Geometries.Point(106.7009, 10.7769) { SRID = 4326 }
        };

        Assert.NotNull(place.OpeningHours);
        Assert.Empty(place.OpeningHours);

        var openingHour = new PlaceOpeningHour
        {
            PlaceId = place.Id,
            Place = place,
            DayOfWeek = DayOfWeek.Sunday,
            OpenTime = new TimeOnly(9, 0),
            CloseTime = new TimeOnly(18, 0)
        };

        place.OpeningHours.Add(openingHour);

        Assert.Single(place.OpeningHours);
        Assert.Equal(DayOfWeek.Sunday, place.OpeningHours.First().DayOfWeek);
    }

    [Fact]
    public void Validate_ValidOpeningHours_ReturnsTrue()
    {
        var openingHour = new PlaceOpeningHour
        {
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(22, 0)
        };

        Assert.True(openingHour.Validate(out var error));
        Assert.Equal(string.Empty, error);
    }

    [Fact]
    public void Validate_OpenTimeAfterCloseTime_ReturnsFalse()
    {
        var openingHour = new PlaceOpeningHour
        {
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeOnly(22, 0),
            CloseTime = new TimeOnly(8, 0)
        };

        Assert.False(openingHour.Validate(out var error));
        Assert.Equal("Giờ mở phải trước giờ đóng.", error);
    }

    [Fact]
    public void Validate_MissingTimes_ReturnsFalse()
    {
        var openingHour = new PlaceOpeningHour
        {
            DayOfWeek = DayOfWeek.Monday,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = null
        };

        Assert.False(openingHour.Validate(out var error));
        Assert.Equal("Phải có cả giờ mở và giờ đóng.", error);
    }

    [Fact]
    public void Validate_ClosedWithTimes_ReturnsFalse()
    {
        var openingHour = new PlaceOpeningHour
        {
            DayOfWeek = DayOfWeek.Monday,
            IsClosed = true,
            OpenTime = new TimeOnly(8, 0),
            CloseTime = new TimeOnly(22, 0)
        };

        Assert.False(openingHour.Validate(out var error));
        Assert.Equal("Đóng cả ngày thì không được có giờ mở/đóng.", error);
    }

    [Fact]
    public void Validate_ClosedWithoutTimes_ReturnsTrue()
    {
        var openingHour = new PlaceOpeningHour
        {
            DayOfWeek = DayOfWeek.Monday,
            IsClosed = true
        };

        Assert.True(openingHour.Validate(out var error));
        Assert.Equal(string.Empty, error);
    }
}
