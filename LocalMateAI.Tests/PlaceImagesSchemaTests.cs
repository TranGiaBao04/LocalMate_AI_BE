using LocalMateAI.Domain.Entities;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class PlaceImagesSchemaTests
{
    [Fact]
    public void PlaceImage_DefaultValues_AreCorrect()
    {
        var placeId = Guid.NewGuid();
        var image = new PlaceImage
        {
            PlaceId = placeId,
            Url = "https://example.com/image.jpg",
            Caption = "Mô tả ảnh"
        };

        Assert.NotEqual(Guid.Empty, image.Id);
        Assert.Equal(placeId, image.PlaceId);
        Assert.Equal("https://example.com/image.jpg", image.Url);
        Assert.Equal("Mô tả ảnh", image.Caption);
        Assert.Equal(0, image.DisplayOrder);
        Assert.False(image.IsPrimary);
    }

    [Fact]
    public void Place_ImagesCollection_IsInitialized()
    {
        var place = new Place
        {
            Name = "Quán Cà Phê A",
            Address = "123 Nguyễn Huệ",
            Location = new NetTopologySuite.Geometries.Point(106.7009, 10.7769) { SRID = 4326 }
        };

        Assert.NotNull(place.Images);
        Assert.Empty(place.Images);

        var image = new PlaceImage
        {
            PlaceId = place.Id,
            Place = place,
            Url = "https://example.com/cafe.png",
            IsPrimary = true
        };

        place.Images.Add(image);

        Assert.Single(place.Images);
        Assert.True(place.Images.First().IsPrimary);
    }
}
